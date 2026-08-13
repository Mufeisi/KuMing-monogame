using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Launcher.ThemeRuntime;

namespace LyoCrystal.LauncherEditor;

internal enum DistributionEndpointRole { Primary, Backup }
internal enum DistributionEndpointStatus { Passed, Unreachable, TimedOut, InvalidResponse, IdentityMismatch }

internal sealed record DistributionEndpointResult(
    string Scope,
    DistributionEndpointRole Role,
    string Address,
    int Port,
    DistributionEndpointStatus Status,
    string ResourceVersion,
    string SigningIdentity,
    TimeSpan Elapsed,
    string Message)
{
    internal bool Passed => Status == DistributionEndpointStatus.Passed;
}

internal static class DistributionEndpointPreflight
{
    private const int MaximumResponseBytes = 16 * 1024;
    internal static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(3);

    internal static async Task<IReadOnlyList<DistributionEndpointResult>> RunAsync(
        EditorProject project,
        CancellationToken cancellationToken,
        TimeSpan? timeout = null,
        Func<HttpMessageHandler>? handlerFactory = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        TimeSpan effectiveTimeout = timeout ?? DefaultTimeout;
        if (effectiveTimeout <= TimeSpan.Zero || effectiveTimeout > TimeSpan.FromSeconds(30)) throw new ArgumentOutOfRangeException(nameof(timeout));
        EndpointTarget[] targets = BuildTargets(project).ToArray();
        Task<DistributionEndpointResult>[] probes = targets.Select(target => ProbeAsync(
            target, project.Snapshot.DefaultMicro.ResourceVersion, project.Snapshot.DefaultMicro.SigningIdentity,
            effectiveTimeout, handlerFactory, cancellationToken)).ToArray();
        return await Task.WhenAll(probes).ConfigureAwait(false);
    }

    private static IEnumerable<EndpointTarget> BuildTargets(EditorProject project)
    {
        if (project.Snapshot.DefaultMicro.Enabled)
            foreach (EndpointTarget target in ForEndpoint("项目默认入口", project.Snapshot.DefaultMicro)) yield return target;
        foreach (LauncherServer server in project.Snapshot.Servers.Where(item => item.MicroOverride?.Enabled == true))
            foreach (EndpointTarget target in ForEndpoint("区服“" + server.Name + "”覆盖", server.MicroOverride!)) yield return target;
    }

    private static IEnumerable<EndpointTarget> ForEndpoint(string scope, MicroEndpoint endpoint)
    {
        yield return new EndpointTarget(scope, DistributionEndpointRole.Primary, endpoint.Address, endpoint.Port);
        if (!string.IsNullOrWhiteSpace(endpoint.BackupAddress) && endpoint.BackupPort > 0)
            yield return new EndpointTarget(scope, DistributionEndpointRole.Backup, endpoint.BackupAddress, endpoint.BackupPort);
    }

    private static async Task<DistributionEndpointResult> ProbeAsync(
        EndpointTarget target,
        string expectedVersion,
        string expectedSigningIdentity,
        TimeSpan timeout,
        Func<HttpMessageHandler>? handlerFactory,
        CancellationToken cancellationToken)
    {
        var elapsed = Stopwatch.StartNew();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);
        try
        {
            using HttpMessageHandler handler = handlerFactory?.Invoke() ?? new HttpClientHandler { UseProxy = false, AllowAutoRedirect = false };
            using var client = new HttpClient(handler, disposeHandler: false) { Timeout = Timeout.InfiniteTimeSpan };
            Uri uri = new UriBuilder(Uri.UriSchemeHttp, target.Address, target.Port, "api/version").Uri;
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            using HttpResponseMessage response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return Result(target, DistributionEndpointStatus.Unreachable, elapsed.Elapsed, $"HTTP {(int)response.StatusCode}，服务未就绪");
            byte[] body = await ReadBoundedAsync(response.Content, deadline.Token).ConfigureAwait(false);
            MicroVersionDocument document;
            try
            {
                document = JsonSerializer.Deserialize(body, DistributionEndpointJsonContext.Default.MicroVersionDocument)
                    ?? throw new JsonException("响应为空");
            }
            catch (JsonException error)
            {
                return Result(target, DistributionEndpointStatus.InvalidResponse, elapsed.Elapsed, "版本响应无效：" + error.Message);
            }
            if (!string.Equals(document.Format, "lyocrystal-micro-version-v1", StringComparison.Ordinal))
                return Result(target, DistributionEndpointStatus.InvalidResponse, elapsed.Elapsed, "服务响应格式不受支持", document);
            if (!string.Equals(document.ResourceVersion, expectedVersion, StringComparison.Ordinal) ||
                !string.Equals(document.SigningIdentity, expectedSigningIdentity, StringComparison.Ordinal))
                return Result(target, DistributionEndpointStatus.IdentityMismatch, elapsed.Elapsed,
                    $"远端身份不一致；期望版本 {Display(expectedVersion)}、签名 {Display(expectedSigningIdentity)}；实际版本 {Display(document.ResourceVersion)}、签名 {Display(document.SigningIdentity)}", document);
            return Result(target, DistributionEndpointStatus.Passed, elapsed.Elapsed, "连通且版本、签名身份一致", document);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Result(target, DistributionEndpointStatus.TimedOut, elapsed.Elapsed, $"连接超过 {timeout.TotalSeconds:0.#} 秒");
        }
        catch (Exception error) when (error is HttpRequestException or UriFormatException or IOException or InvalidDataException or ArgumentException)
        {
            DistributionEndpointStatus status = error is InvalidDataException ? DistributionEndpointStatus.InvalidResponse : DistributionEndpointStatus.Unreachable;
            return Result(target, status, elapsed.Elapsed, (status == DistributionEndpointStatus.InvalidResponse ? "版本响应无效：" : "无法连接：") + error.Message);
        }
    }

    private static async Task<byte[]> ReadBoundedAsync(HttpContent content, CancellationToken cancellationToken)
    {
        long declared = content.Headers.ContentLength ?? 0;
        if (declared > MaximumResponseBytes) throw new InvalidDataException("版本响应超过 16 KB 上限");
        await using Stream input = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var output = new MemoryStream();
        byte[] buffer = new byte[4096];
        int read;
        while ((read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            if (output.Length + read > MaximumResponseBytes) throw new InvalidDataException("版本响应超过 16 KB 上限");
            output.Write(buffer, 0, read);
        }
        return output.ToArray();
    }

    private static DistributionEndpointResult Result(EndpointTarget target, DistributionEndpointStatus status, TimeSpan elapsed, string message, MicroVersionDocument? document = null)
        => new(target.Scope, target.Role, target.Address, target.Port, status, document?.ResourceVersion ?? string.Empty, document?.SigningIdentity ?? string.Empty, elapsed, message);

    private static string Display(string value) => string.IsNullOrWhiteSpace(value) ? "未配置" : value;
    private sealed record EndpointTarget(string Scope, DistributionEndpointRole Role, string Address, int Port);

    internal static void ThrowIfInvalid(IReadOnlyList<DistributionEndpointResult> results)
    {
        DistributionEndpointResult[] failures = results.Where(result => !result.Passed).ToArray();
        if (failures.Length == 0) return;
        throw new InvalidDataException("微端入口预检未通过：\r\n" + string.Join("\r\n", failures.Select(result =>
            $"{result.Scope} {RoleText(result.Role)} {result.Address}:{result.Port}：{result.Message}")));
    }

    private static string RoleText(DistributionEndpointRole role) => role == DistributionEndpointRole.Primary ? "主入口" : "备用入口";
}

internal sealed class MicroVersionDocument
{
    public string Format { get; set; } = string.Empty;
    public string ResourceVersion { get; set; } = string.Empty;
    public string SigningIdentity { get; set; } = string.Empty;
}

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(MicroVersionDocument))]
internal sealed partial class DistributionEndpointJsonContext : JsonSerializerContext;
