using System.Text.Json.Serialization;

namespace Shared.Security;

[JsonSourceGenerationOptions(
    PropertyNameCaseInsensitive = false,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(BootstrapSignedManifest))]
internal sealed partial class BootstrapManifestJsonContext : JsonSerializerContext;
