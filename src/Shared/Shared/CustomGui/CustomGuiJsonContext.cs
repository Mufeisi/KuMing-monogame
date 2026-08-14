using System.Text.Json.Serialization;

namespace Shared.CustomGui;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = false,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    UseStringEnumConverter = true,
    WriteIndented = false)]
[JsonSerializable(typeof(CustomGuiRuntimeDocument))]
[JsonSerializable(typeof(CustomGuiResourceBindingsDocument))]
internal sealed partial class CustomGuiJsonContext : JsonSerializerContext;
