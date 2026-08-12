using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Shared.Security;

/// <summary>
/// 在配置读取前清除已知敏感键，避免不完整的 InIReader 读取把旧密码带回内存或写回磁盘。
/// </summary>
public static class SensitiveIniSanitizer
{
    public static void Sanitize(string fileName)
    {
        Sanitize(fileName, out _);
    }

    public static void Sanitize(string fileName, out string legacyMicroCode)
    {
        legacyMicroCode = string.Empty;
        if (string.IsNullOrWhiteSpace(fileName))
            throw new ArgumentException("配置文件路径不能为空。", nameof(fileName));

        var path = Path.GetFullPath(fileName);
        byte[] bytes;
        try
        {
            using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.SequentialScan);
            using var copy = new MemoryStream();
            input.CopyTo(copy);
            bytes = copy.ToArray();
        }
        catch (FileNotFoundException) { return; }
        catch (DirectoryNotFoundException) { return; }

        Encoding encoding = new UTF8Encoding(false);
        var offset = 0;
        var hasBom = false;
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            encoding = new UTF8Encoding(true);
            offset = 3;
            hasBom = true;
        }
        else if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
        {
            encoding = new UnicodeEncoding(false, true);
            offset = 2;
            hasBom = true;
        }
        else if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
        {
            encoding = new UnicodeEncoding(true, true);
            offset = 2;
            hasBom = true;
        }

        var text = encoding.GetString(bytes, offset, bytes.Length - offset);
        var newline = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n"
            : text.Contains("\n", StringComparison.Ordinal) ? "\n"
            : text.Contains("\r", StringComparison.Ordinal) ? "\r" : Environment.NewLine;
        var trailingNewline = text.EndsWith("\r", StringComparison.Ordinal) || text.EndsWith("\n", StringComparison.Ordinal);
        var lines = new List<string>(text.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None));
        if (trailingNewline && lines.Count > 0 && lines[^1].Length == 0)
            lines.RemoveAt(lines.Count - 1);

        var section = string.Empty;
        var removed = false;
        for (var i = 0; i < lines.Count;)
        {
            var line = lines[i] ?? string.Empty;
            if (line.Length >= 2 && line[0] == '[' && line[^1] == ']')
            {
                section = line.Substring(1, line.Length - 2);
                i++;
                continue;
            }

            var separator = line.IndexOf('=');
            var key = separator > 0 ? line.Substring(0, separator) : string.Empty;
            if (((section == "Game" || section == "Launcher")
                    && (key == "Password" || key == "RememberPassword"))
                || (section == "Micro" && key == "Code"))
            {
                if (section == "Micro" && key == "Code" && separator >= 0)
                    legacyMicroCode = line.Substring(separator + 1).Trim();
                lines.RemoveAt(i);
                removed = true;
                continue;
            }

            i++;
        }

        if (!removed)
            return;

        var directory = Path.GetDirectoryName(path) ?? Directory.GetCurrentDirectory();
        var temporary = Path.Combine(directory, "." + Path.GetFileName(path) + "." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            var output = string.Join(newline, lines) + (trailingNewline ? newline : string.Empty);
            using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                if (hasBom)
                {
                    var preamble = encoding.GetPreamble();
                    file.Write(preamble, 0, preamble.Length);
                }

                var outputBytes = encoding.GetBytes(output);
                file.Write(outputBytes, 0, outputBytes.Length);
                file.Flush(true);
            }

            if (OperatingSystem.IsWindows())
            {
                try { File.Replace(temporary, path, null, true); }
                catch (PlatformNotSupportedException) { File.Move(temporary, path, true); }
            }
            else
            {
                File.Move(temporary, path, true);
            }

            temporary = null;
        }
        finally
        {
            if (!string.IsNullOrEmpty(temporary))
            {
                try { File.Delete(temporary); }
                catch { }
            }
        }
    }
}
