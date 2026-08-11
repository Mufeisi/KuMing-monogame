namespace LyoCrystal.MicroGateway;

public sealed record MicroGatewayOptions(
    string ResourceRoot,
    string User,
    string Code,
    string? LauncherRoot = null,
    Func<bool>? ResourceUpdateEnabled = null);

public sealed record MicroGatewayRequest(
    string Method,
    string AbsolutePath,
    IReadOnlyDictionary<string, string?> Headers);

public sealed record MicroGatewaySnapshot(
    bool IsRunning,
    string ResourceRoot,
    long RequestCount,
    long ActiveRequestCount,
    string? LastError);

public sealed class MicroGatewayResponse : IDisposable, IAsyncDisposable
{
    private Action? _completion;
    public int StatusCode { get; init; }
    public string? ContentType { get; init; }
    public long? ContentLength { get; init; }
    public IReadOnlyDictionary<string, string> Headers { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    public Func<Stream, CancellationToken, Task>? WriteBodyAsync { get; init; }
    internal Action? Completion { init => _completion = value; }

    public void Dispose() => Interlocked.Exchange(ref _completion, null)?.Invoke();
    public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }

    public static MicroGatewayResponse Text(int statusCode, string text)
    {
        byte[] bytes = System.Text.Encoding.UTF8.GetBytes(text ?? string.Empty);
        return Bytes(statusCode, bytes, "text/plain; charset=UTF-8");
    }

    public static MicroGatewayResponse Bytes(int statusCode, byte[] bytes, string contentType)
    {
        return new MicroGatewayResponse
        {
            StatusCode = statusCode,
            ContentType = contentType,
            ContentLength = bytes.LongLength,
            WriteBodyAsync = (output, cancellationToken) =>
                output.WriteAsync(bytes, cancellationToken).AsTask(),
        };
    }
}
