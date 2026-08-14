using System.Text.Json;
using System.Text.Json.Serialization;

namespace Shared.CustomGui;

public sealed class CustomGuiSchemaException : Exception
{
    public CustomGuiSchemaException(string code, string message, Exception innerException = null)
        : base($"{code}: {message}", innerException)
    {
        Code = code;
    }

    public string Code { get; }
}

public static class CustomGuiDocumentCodec
{
    private static readonly CustomGuiJsonContext JsonContext = CreateContext();

    public static byte[] Serialize(CustomGuiRuntimeDocument document)
    {
        EnsureSupported(document);
        try
        {
            return JsonSerializer.SerializeToUtf8Bytes(document, JsonContext.CustomGuiRuntimeDocument);
        }
        catch (Exception error) when (error is JsonException or NotSupportedException)
        {
            throw InvalidDocument(error);
        }
    }

    public static CustomGuiRuntimeDocument Deserialize(ReadOnlySpan<byte> utf8Json)
    {
        if (utf8Json.IsEmpty) throw InvalidDocument();
        try
        {
            CustomGuiRuntimeDocument document = JsonSerializer.Deserialize(utf8Json, JsonContext.CustomGuiRuntimeDocument);
            EnsureSupported(document);
            return document;
        }
        catch (CustomGuiSchemaException)
        {
            throw;
        }
        catch (Exception error) when (error is JsonException or NotSupportedException)
        {
            throw InvalidDocument(error);
        }
    }

    private static CustomGuiJsonContext CreateContext()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = false,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            RespectRequiredConstructorParameters = true,
            WriteIndented = false,
        };
        return new CustomGuiJsonContext(options);
    }

    private static void EnsureSupported(CustomGuiRuntimeDocument document)
    {
        if (document == null ||
            document.Viewport == null ||
            document.Elements == null ||
            string.IsNullOrWhiteSpace(document.DocumentId) ||
            document.Elements.Any(element => element == null || string.IsNullOrWhiteSpace(element.Id)))
            throw InvalidDocument();
        if (document.SchemaVersion != CustomGuiSchema.CurrentVersion)
            throw new CustomGuiSchemaException("GUI01-SCHEMA-002", $"不支持 Schema 版本 {document.SchemaVersion}，当前仅接受 {CustomGuiSchema.CurrentVersion}");
    }

    private static CustomGuiSchemaException InvalidDocument(Exception innerException = null) =>
        new("GUI01-SCHEMA-001", "运行描述格式无效或包含未知控件、属性或枚举值", innerException);
}
