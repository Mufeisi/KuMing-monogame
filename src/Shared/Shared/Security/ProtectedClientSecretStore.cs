#nullable enable
using System.Runtime.InteropServices;
using System.Text;

namespace Shared.Security;

/// <summary>使用 Windows 凭据管理器保存玩家侧共享秘密，不落普通 INI，也不进入游戏命令行。</summary>
public static class ProtectedClientSecretStore
{
    private const string TargetPrefix = "LyoCrystal/Client/MicroCode/";
    private const uint GenericCredential = 1;
    private const uint LocalMachinePersistence = 2;

    public static string ReadMicroCode(string projectId)
    {
        if (!OperatingSystem.IsWindows()) return string.Empty;
        string target = GetTarget(projectId);
        if (!CredReadW(target, GenericCredential, 0, out nint pointer)) return string.Empty;
        try
        {
            CREDENTIAL credential = Marshal.PtrToStructure<CREDENTIAL>(pointer);
            if (credential.CredentialBlob == nint.Zero || credential.CredentialBlobSize == 0) return string.Empty;
            byte[] bytes = new byte[credential.CredentialBlobSize];
            Marshal.Copy(credential.CredentialBlob, bytes, 0, bytes.Length);
            return Encoding.Unicode.GetString(bytes).TrimEnd('\0');
        }
        finally { CredFree(pointer); }
    }

    public static void WriteMicroCode(string projectId, string? value)
    {
        if (!OperatingSystem.IsWindows()) return;
        string target = GetTarget(projectId);
        value = value?.Trim() ?? string.Empty;
        if (value.Length == 0) { CredDeleteW(target, GenericCredential, 0); return; }
        if (value.Length > 256) throw new ArgumentOutOfRangeException(nameof(value), "微端 Code 超过 256 字符");
        byte[] bytes = Encoding.Unicode.GetBytes(value);
        nint blob = Marshal.AllocHGlobal(bytes.Length);
        try
        {
            Marshal.Copy(bytes, 0, blob, bytes.Length);
            var credential = new CREDENTIAL
            {
                Type = GenericCredential,
                TargetName = target,
                CredentialBlobSize = (uint)bytes.Length,
                CredentialBlob = blob,
                Persist = LocalMachinePersistence,
                UserName = Environment.UserName,
            };
            if (!CredWriteW(ref credential, 0)) throw new IOException("无法写入 Windows 凭据管理器，错误码 " + Marshal.GetLastWin32Error());
        }
        finally { Marshal.FreeHGlobal(blob); }
    }

    private static string GetTarget(string projectId)
    {
        projectId = projectId?.Trim() ?? string.Empty;
        if (projectId.Length is < 1 or > 64 || projectId.Any(character =>
                !(character is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '.' or '_' or '-')))
            throw new ArgumentException("项目标识无效", nameof(projectId));
        return TargetPrefix + projectId;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct CREDENTIAL
    {
        public uint Flags, Type;
        public string TargetName;
        public string? Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint CredentialBlobSize;
        public nint CredentialBlob;
        public uint Persist, AttributeCount;
        public nint Attributes;
        public string? TargetAlias;
        public string UserName;
    }

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool CredWriteW(ref CREDENTIAL credential, uint flags);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool CredReadW(string target, uint type, uint flags, out nint credential);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool CredDeleteW(string target, uint type, uint flags);
    [DllImport("advapi32.dll")] private static extern void CredFree(nint buffer);
}
