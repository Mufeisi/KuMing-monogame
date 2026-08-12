using System.Net;

namespace LyoCrystal.MicroGateway;

public static class HttpListenerMicroAdapter
{
    public static async Task HandleAsync(MicroGatewayCore core, HttpListenerRequest request, HttpListenerResponse response, CancellationToken cancellationToken = default)
    {
        var headers = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (string? key in request.Headers.AllKeys)
            if (key is not null) headers[key] = request.Headers[key];
        MicroGatewayResponse? result = null;
        try
        {
            result = await core.HandleAsync(new MicroGatewayRequest(
                request.HttpMethod, request.Url?.AbsolutePath ?? "/", headers), cancellationToken).ConfigureAwait(false);
            response.StatusCode = result.StatusCode;
            if (!string.IsNullOrWhiteSpace(result.ContentType)) response.ContentType = result.ContentType;
            foreach ((string key, string value) in result.Headers) response.AddHeader(key, value);
            if (result.ContentLength.HasValue) response.ContentLength64 = result.ContentLength.Value;
            if (result.WriteBodyAsync is not null) await result.WriteBodyAsync(response.OutputStream, cancellationToken).ConfigureAwait(false);
        }
        finally { result?.Dispose(); }
    }
}
