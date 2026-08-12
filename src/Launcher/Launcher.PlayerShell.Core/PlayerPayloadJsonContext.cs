using System.Text.Json.Serialization;

namespace Launcher.PlayerShell;

[JsonSourceGenerationOptions(
    PropertyNameCaseInsensitive = false,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    WriteIndented = true)]
[JsonSerializable(typeof(PayloadManifest))]
[JsonSerializable(typeof(PlayerReplacementJournal))]
internal sealed partial class PlayerPayloadJsonContext : JsonSerializerContext;
