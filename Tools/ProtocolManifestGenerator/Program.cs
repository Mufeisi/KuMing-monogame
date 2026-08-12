using System.Buffers.Binary;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

internal static class Program
{
    private static readonly IReadOnlyDictionary<ushort, OpCode> OpCodesByValue = typeof(OpCodes)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(field => field.FieldType == typeof(OpCode))
        .Select(field => (OpCode)field.GetValue(null)!)
        .ToDictionary(opCode => unchecked((ushort)opCode.Value));

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
            "PROTO-02.generated-wire-manifest.v2",
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
        // 原始 IL 中的 metadata token 会随编译环境变化；解析为稳定语义身份后再计算审计哈希。
        string canonical = CanonicalMethodIl(method, il);
        return Sha256(Encoding.UTF8.GetBytes(canonical));
    }

    private static string CanonicalMethodIl(MethodInfo method, byte[] il)
    {
        var builder = new StringBuilder(il.Length * 3);
        int offset = 0;
        while (offset < il.Length)
        {
            int instructionOffset = offset;
            ushort value = il[offset++];
            if (value == 0xfe)
            {
                if (offset >= il.Length) throw InvalidIl(method, instructionOffset);
                value = (ushort)(0xfe00 | il[offset++]);
            }

            if (!OpCodesByValue.TryGetValue(value, out OpCode opCode))
                throw InvalidIl(method, instructionOffset);

            builder.Append(instructionOffset).Append(':').Append(opCode.Name).Append(':');
            AppendCanonicalOperand(builder, method, il, ref offset, instructionOffset, opCode.OperandType);
            builder.Append('\n');
        }
        return builder.ToString();
    }

    private static void AppendCanonicalOperand(
        StringBuilder builder,
        MethodInfo method,
        byte[] il,
        ref int offset,
        int instructionOffset,
        OperandType operandType)
    {
        switch (operandType)
        {
            case OperandType.InlineNone:
                return;
            case OperandType.ShortInlineI:
                builder.Append(unchecked((sbyte)ReadByte(method, il, ref offset)));
                return;
            case OperandType.InlineI:
                builder.Append(ReadInt32(method, il, ref offset));
                return;
            case OperandType.InlineI8:
                builder.Append(ReadInt64(method, il, ref offset));
                return;
            case OperandType.ShortInlineR:
                builder.Append(BitConverter.Int32BitsToSingle(ReadInt32(method, il, ref offset)).ToString("R", System.Globalization.CultureInfo.InvariantCulture));
                return;
            case OperandType.InlineR:
                builder.Append(BitConverter.Int64BitsToDouble(ReadInt64(method, il, ref offset)).ToString("R", System.Globalization.CultureInfo.InvariantCulture));
                return;
            case OperandType.ShortInlineVar:
                builder.Append(ReadByte(method, il, ref offset));
                return;
            case OperandType.InlineVar:
                builder.Append(ReadUInt16(method, il, ref offset));
                return;
            case OperandType.ShortInlineBrTarget:
                int shortDelta = unchecked((sbyte)ReadByte(method, il, ref offset));
                builder.Append(offset + shortDelta);
                return;
            case OperandType.InlineBrTarget:
                int delta = ReadInt32(method, il, ref offset);
                builder.Append(offset + delta);
                return;
            case OperandType.InlineSwitch:
                int count = ReadInt32(method, il, ref offset);
                int baseOffset = checked(offset + count * sizeof(int));
                for (int index = 0; index < count; index++)
                {
                    if (index > 0) builder.Append(',');
                    builder.Append(baseOffset + ReadInt32(method, il, ref offset));
                }
                return;
            case OperandType.InlineString:
                builder.Append("string:").Append(Escape(method.Module.ResolveString(ReadInt32(method, il, ref offset))));
                return;
            case OperandType.InlineField:
                builder.Append("field:").Append(MemberIdentity(ResolveMember(method, ReadInt32(method, il, ref offset))));
                return;
            case OperandType.InlineMethod:
                builder.Append("method:").Append(MemberIdentity(ResolveMember(method, ReadInt32(method, il, ref offset))));
                return;
            case OperandType.InlineType:
                builder.Append("type:").Append(TypeIdentity(method.Module.ResolveType(
                    ReadInt32(method, il, ref offset),
                    method.DeclaringType?.GetGenericArguments(),
                    method.GetGenericArguments())));
                return;
            case OperandType.InlineTok:
                builder.Append("token:").Append(MemberIdentity(ResolveMember(method, ReadInt32(method, il, ref offset))));
                return;
            case OperandType.InlineSig:
                throw new InvalidOperationException($"协议方法不支持 calli 签名：{method.DeclaringType?.FullName}.{method.Name} IL_{instructionOffset:x4}");
            default:
                throw new InvalidOperationException($"不支持的 IL 操作数类型 {operandType}：{method.DeclaringType?.FullName}.{method.Name} IL_{instructionOffset:x4}");
        }
    }

    private static MemberInfo ResolveMember(MethodInfo method, int token) => method.Module.ResolveMember(
        token,
        method.DeclaringType?.GetGenericArguments(),
        method.GetGenericArguments()) ?? throw new InvalidOperationException($"无法解析 metadata token 0x{token:x8}");

    private static string MemberIdentity(MemberInfo member) => member switch
    {
        Type type => TypeIdentity(type),
        FieldInfo field => $"{TypeIdentity(field.DeclaringType!)}::{field.Name}:{TypeIdentity(field.FieldType)}",
        MethodInfo target => $"{TypeIdentity(target.DeclaringType!)}::{target.Name}{MethodGenericArguments(target)}({string.Join(',', target.GetParameters().Select(parameter => TypeIdentity(parameter.ParameterType)))}):{TypeIdentity(target.ReturnType)}",
        ConstructorInfo constructor => $"{TypeIdentity(constructor.DeclaringType!)}::.ctor({string.Join(',', constructor.GetParameters().Select(parameter => TypeIdentity(parameter.ParameterType)))})",
        _ => $"{member.MemberType}:{member.DeclaringType?.FullName}::{member.Name}",
    };

    private static string MethodGenericArguments(MethodInfo method)
    {
        Type[] arguments = method.GetGenericArguments();
        return arguments.Length == 0 ? string.Empty : "<" + string.Join(',', arguments.Select(TypeIdentity)) + ">";
    }

    private static string TypeIdentity(Type type)
    {
        if (type.IsByRef) return TypeIdentity(type.GetElementType()!) + "&";
        if (type.IsPointer) return TypeIdentity(type.GetElementType()!) + "*";
        if (type.IsArray) return TypeIdentity(type.GetElementType()!) + "[" + new string(',', type.GetArrayRank() - 1) + "]";
        if (type.IsGenericParameter) return (type.DeclaringMethod == null ? "!" : "!!") + type.GenericParameterPosition;
        if (!type.IsGenericType) return type.FullName ?? type.Name;
        string definition = type.GetGenericTypeDefinition().FullName?.Split('`')[0] ?? type.Name.Split('`')[0];
        return definition + "<" + string.Join(',', type.GetGenericArguments().Select(TypeIdentity)) + ">";
    }

    private static string Escape(string value) => Convert.ToBase64String(Encoding.UTF8.GetBytes(value));

    private static byte ReadByte(MethodInfo method, byte[] il, ref int offset)
    {
        EnsureAvailable(method, il, offset, sizeof(byte));
        return il[offset++];
    }

    private static ushort ReadUInt16(MethodInfo method, byte[] il, ref int offset)
    {
        EnsureAvailable(method, il, offset, sizeof(ushort));
        ushort value = BinaryPrimitives.ReadUInt16LittleEndian(il.AsSpan(offset, sizeof(ushort)));
        offset += sizeof(ushort);
        return value;
    }

    private static int ReadInt32(MethodInfo method, byte[] il, ref int offset)
    {
        EnsureAvailable(method, il, offset, sizeof(int));
        int value = BinaryPrimitives.ReadInt32LittleEndian(il.AsSpan(offset, sizeof(int)));
        offset += sizeof(int);
        return value;
    }

    private static long ReadInt64(MethodInfo method, byte[] il, ref int offset)
    {
        EnsureAvailable(method, il, offset, sizeof(long));
        long value = BinaryPrimitives.ReadInt64LittleEndian(il.AsSpan(offset, sizeof(long)));
        offset += sizeof(long);
        return value;
    }

    private static void EnsureAvailable(MethodInfo method, byte[] il, int offset, int length)
    {
        if (offset < 0 || length < 0 || offset > il.Length - length) throw InvalidIl(method, offset);
    }

    private static InvalidOperationException InvalidIl(MethodInfo method, int offset) =>
        new($"无效 IL：{method.DeclaringType?.FullName}.{method.Name} IL_{offset:x4}");

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
