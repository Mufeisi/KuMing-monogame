using System.Text.Json;
using System.Text.Json.Serialization;

namespace Launcher.Remote;

public sealed class LauncherStateStore
{
    private const string StateFileName = "LauncherState.json";
    private readonly string _statePath;
    private readonly SemaphoreSlim _saveLock = new(1, 1);

    public LauncherStateStore(string rootPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        _statePath = Path.Combine(rootPath, StateFileName);
    }

    public async Task<string> LoadLastServerNameAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_statePath)) return string.Empty;

        try
        {
            await using FileStream stream = new(
                _statePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                4096,
                FileOptions.Asynchronous | FileOptions.SequentialScan);

            using JsonDocument document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("lastServerName", out JsonElement nameElement) ||
                nameElement.ValueKind != JsonValueKind.String)
            {
                return string.Empty;
            }

            return nameElement.GetString()?.Trim() ?? string.Empty;
        }
        catch (JsonException)
        {
            return string.Empty;
        }
        catch (IOException)
        {
            return string.Empty;
        }
        catch (UnauthorizedAccessException)
        {
            return string.Empty;
        }
    }

    public async Task SaveLastServerNameAsync(string serverName, CancellationToken cancellationToken = default)
    {
        await _saveLock.WaitAsync(cancellationToken);
        try
        {
            string directory = Path.GetDirectoryName(_statePath)!;
            Directory.CreateDirectory(directory);

            string temporaryPath = _statePath + ".tmp";
            byte[] json = JsonSerializer.SerializeToUtf8Bytes(new State(serverName?.Trim() ?? string.Empty));
            await File.WriteAllBytesAsync(temporaryPath, json, cancellationToken);

            try
            {
                if (File.Exists(_statePath))
                {
                    File.Replace(temporaryPath, _statePath, null);
                }
                else
                {
                    File.Move(temporaryPath, _statePath);
                }
            }
            finally
            {
                if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
            }
        }
        finally
        {
            _saveLock.Release();
        }
    }

    private sealed record State([property: JsonPropertyName("lastServerName")] string LastServerName);
}
