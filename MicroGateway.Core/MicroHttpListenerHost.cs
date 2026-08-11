using System.Net;
using System.Collections.Concurrent;

namespace LyoCrystal.MicroGateway;

public sealed class MicroHttpListenerHost : IAsyncDisposable
{
    private readonly MicroGatewayCore _core = new();
    private HttpListener? _listener;
    private CancellationTokenSource? _cancellation;
    private Task? _loop;
    private readonly ConcurrentDictionary<long, Task> _requests = new();
    private long _requestId;

    public MicroGatewaySnapshot GetSnapshot() => _core.GetSnapshot();
    public Task<bool> ReconcileResourcesAsync(CancellationToken cancellationToken = default) => _core.ReconcileResourcesAsync(cancellationToken);

    public async Task StartAsync(string prefix, MicroGatewayOptions options)
    {
        if (_listener is not null) throw new InvalidOperationException("微端网关已启动。");
        await _core.StartAsync(options).ConfigureAwait(false);
        var listener = new HttpListener();
        listener.Prefixes.Add(NormalizePrefix(prefix));
        try { listener.Start(); }
        catch { await _core.StopAsync().ConfigureAwait(false); listener.Close(); throw; }
        _listener = listener;
        _cancellation = new CancellationTokenSource();
        _loop = AcceptLoopAsync(listener, _cancellation.Token);
    }

    public async Task StopAsync()
    {
        CancellationTokenSource? cancellation = _cancellation;
        HttpListener? listener = _listener;
        Task? loop = _loop;
        _cancellation = null; _listener = null; _loop = null;
        cancellation?.Cancel();
        listener?.Close();
        if (loop is not null) { try { await loop.ConfigureAwait(false); } catch (OperationCanceledException) { } catch (HttpListenerException) { } catch (ObjectDisposedException) { } }
        Task[] requests = _requests.Values.ToArray();
        if (requests.Length > 0) { try { await Task.WhenAll(requests).ConfigureAwait(false); } catch (OperationCanceledException) { } catch (HttpListenerException) { } catch (IOException) { } }
        cancellation?.Dispose();
        await _core.StopAsync().ConfigureAwait(false);
    }

    private async Task AcceptLoopAsync(HttpListener listener, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            HttpListenerContext context = await listener.GetContextAsync().ConfigureAwait(false);
            long requestId = Interlocked.Increment(ref _requestId);
            Task request = ProcessAsync(context, cancellationToken);
            _requests[requestId] = request;
            _ = request.ContinueWith(
                completed => _requests.TryRemove(requestId, out _),
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }
    }

    private async Task ProcessAsync(HttpListenerContext context, CancellationToken cancellationToken)
    {
        try { await HttpListenerMicroAdapter.HandleAsync(_core, context.Request, context.Response, cancellationToken).ConfigureAwait(false); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch { try { context.Response.StatusCode = 500; } catch { } }
        finally { try { context.Response.Close(); } catch { } }
    }

    private static string NormalizePrefix(string value)
    {
        string prefix = value.Trim();
        if (!prefix.StartsWith("http://", StringComparison.OrdinalIgnoreCase)) prefix = "http://" + prefix;
        prefix = prefix.Replace("http://0.0.0.0:", "http://+:", StringComparison.OrdinalIgnoreCase);
        return prefix.EndsWith('/') ? prefix : prefix + "/";
    }

    public async ValueTask DisposeAsync() => await StopAsync().ConfigureAwait(false);
}
