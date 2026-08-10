using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

internal static class Program
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static int Main(string[] args)
    {
        try
        {
            if (args.Length != 2 || (args[0] != "--write" && args[0] != "--verify"))
                throw new ArgumentException("用法：ProtocolManifestGenerator --write|--verify <清单路径>");

            string repositoryRoot = FindRepositoryRoot(AppContext.BaseDirectory);
            string outputPath = Path.GetFullPath(args[1], Directory.GetCurrentDirectory());
            string generated = Generate(repositoryRoot);

            if (args[0] == "--write")
            {
                Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
                File.WriteAllText(outputPath, generated, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                Console.WriteLine($"协议清单已生成：{outputPath}");
                return 0;
            }

            if (!File.Exists(outputPath))
                throw new InvalidOperationException($"协议清单不存在：{outputPath}");
            string existing = NormalizeNewlines(File.ReadAllText(outputPath));
            if (!string.Equals(existing, generated, StringComparison.Ordinal))
                throw new InvalidOperationException("协议清单与 Shared C# 事实源不一致；请重新生成并审查差异。");

            Console.WriteLine($"协议清单无漂移：{outputPath}");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 2;
        }
    }

    private static string Generate(string repositoryRoot)
    {
        Assembly assembly = typeof(Packet).Assembly;
        var packets = assembly.GetTypes()
            .Where(type => !type.IsAbstract && typeof(Packet).IsAssignableFrom(type))
            .Where(type => type.Namespace is "ClientPackets" or "ServerPackets")
            .Select(CreatePacket)
            .OrderBy(packet => packet.Direction, StringComparer.Ordinal)
            .ThenBy(packet => packet.Id)
            .ThenBy(packet => packet.Type, StringComparer.Ordinal)
            .ToArray();

        var enums = assembly.GetTypes()
            .Where(type => type.IsEnum && type.IsPublic)
            .OrderBy(type => type.FullName, StringComparer.Ordinal)
            .Select(CreateEnum)
            .ToArray();

        string[] sourceFiles = ProtocolSourceFiles(repositoryRoot);
        var sources = sourceFiles.Select(path => new SourceEntry(
            Path.GetRelativePath(repositoryRoot, path).Replace('\\', '/'),
            Sha256(Encoding.UTF8.GetBytes(NormalizeNewlines(File.ReadAllText(path))))))
            .ToArray();

        var manifest = new GeneratedManifest(
            "PROTO-02.generated-wire-manifest.v1",
            new ManifestCoverage(
                packets.Count(packet => packet.Direction == "clientToServer"),
                packets.Count(packet => packet.Direction == "serverToClient"),
                enums.Length),
            sources,
            packets,
            enums);
        return NormalizeNewlines(JsonSerializer.Serialize(manifest, JsonOptions)) + "\n";
    }

    private static PacketEntry CreatePacket(Type type)
    {
        object instance = RuntimeHelpers.GetUninitializedObject(type);
        short id = (short)(type.GetProperty(nameof(Packet.Index))?.GetValue(instance)
            ?? throw new InvalidOperationException($"协议包缺少 Index：{type.FullName}"));
        bool compressed = (bool)(type.GetProperty(nameof(Packet.Compressed))?.GetValue(instance) ?? false);
        FieldEntry[] fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
            .OrderBy(field => field.MetadataToken)
            .Select(field => new FieldEntry(field.Name, TypeName(field.FieldType)))
            .ToArray();
        MethodInfo read = type.GetMethod("ReadPacket", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
            ?? throw new InvalidOperationException($"协议包缺少 ReadPacket：{type.FullName}");
        MethodInfo write = type.GetMethod("WritePacket", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
            ?? throw new InvalidOperationException($"协议包缺少 WritePacket：{type.FullName}");
        return new PacketEntry(
            type.Namespace == "ClientPackets" ? "clientToServer" : "serverToClient",
            id,
            type.FullName ?? type.Name,
            compressed,
            fields,
            MethodHash(read),
            MethodHash(write));
    }

    private static EnumEntry CreateEnum(Type type)
    {
        Type underlying = Enum.GetUnderlyingType(type);
        bool signed = underlying == typeof(sbyte) || underlying == typeof(short) ||
                      underlying == typeof(int) || underlying == typeof(long);
        EnumValueEntry[] values = Enum.GetNames(type)
            .Select(name =>
            {
                object value = Enum.Parse(type, name);
                string numeric = signed
                    ? Convert.ToInt64(value).ToString(System.Globalization.CultureInfo.InvariantCulture)
                    : Convert.ToUInt64(value).ToString(System.Globalization.CultureInfo.InvariantCulture);
                return new EnumValueEntry(name, numeric);
            })
            .ToArray();
        return new EnumEntry(type.FullName ?? type.Name, TypeName(underlying), values);
    }

    private static string[] ProtocolSourceFiles(string repositoryRoot)
    {
        string shared = Path.Combine(repositoryRoot, "Shared");
        string[] roots =
        [
            "BaseStats.cs", "ClientPackets.cs", "Enums.cs", "Globals.cs", "Packet.cs", "ServerPackets.cs",
            "Extensions/ExtensionMethods.cs", "Functions/Functions.cs", "Functions/RegexFunctions.cs", "Helpers/FileIO.cs",
        ];
        return roots.Select(path => Path.Combine(shared, path.Replace('/', Path.DirectorySeparatorChar)))
            .Concat(Directory.GetFiles(Path.Combine(shared, "Data"), "*.cs", SearchOption.TopDirectoryOnly))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
    }

    private static string MethodHash(MethodInfo method)
    {
        byte[] il = method.GetMethodBody()?.GetILAsByteArray()
            ?? throw new InvalidOperationException($"无法读取协议方法 IL：{method.DeclaringType?.FullName}.{method.Name}");
        return Sha256(il);
    }

    private static string TypeName(Type type)
    {
        if (type.IsArray) return TypeName(type.GetElementType()!) + "[]";
        if (type.IsGenericType)
        {
            string name = type.GetGenericTypeDefinition().FullName?.Split('`')[0] ?? type.Name.Split('`')[0];
            return name + "<" + string.Join(",", type.GetGenericArguments().Select(TypeName)) + ">";
        }
        return type.FullName ?? type.Name;
    }

    private static string FindRepositoryRoot(string start)
    {
        var current = new DirectoryInfo(start);
        while (current != null)
        {
            if (File.Exists(Path.Combine(current.FullName, "global.json")) &&
                Directory.Exists(Path.Combine(current.FullName, "Shared")))
                return current.FullName;
            current = current.Parent;
        }
        throw new DirectoryNotFoundException("无法定位仓库根目录。");
    }

    private static string Sha256(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private static string NormalizeNewlines(string value) => value
        .Replace("\r\n", "\n", StringComparison.Ordinal)
        .Replace("\r", "\n", StringComparison.Ordinal);

    private sealed record GeneratedManifest(
        string SchemaVersion,
        ManifestCoverage Coverage,
        SourceEntry[] Sources,
        PacketEntry[] Packets,
        EnumEntry[] Enums);
    private sealed record ManifestCoverage(int ClientPacketCount, int ServerPacketCount, int EnumCount);
    private sealed record SourceEntry(string Path, string Sha256);
    private sealed record PacketEntry(
        string Direction,
        int Id,
        string Type,
        bool Compressed,
        FieldEntry[] Fields,
        string ReadIlSha256,
        string WriteIlSha256);
    private sealed record FieldEntry(string Name, string Type);
    private sealed record EnumEntry(string Type, string UnderlyingType, EnumValueEntry[] Values);
    private sealed record EnumValueEntry(string Name, string Value);
}
