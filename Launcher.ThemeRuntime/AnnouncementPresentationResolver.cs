namespace Launcher.ThemeRuntime;

public static class AnnouncementPresentationResolver
{
    public sealed record Presentation(AnnouncementDisplayMode Mode, string Html);

    public static async Task<AnnouncementDisplayMode> ResolveAsync(LauncherSnapshot snapshot, HttpClient? client = null, CancellationToken cancellationToken = default)
        => (await LoadAsync(snapshot, client, cancellationToken).ConfigureAwait(false)).Mode;

    public static async Task<Presentation> LoadAsync(LauncherSnapshot snapshot, HttpClient? client = null, CancellationToken cancellationToken = default)
    {
        LauncherSnapshotValidator.Validate(snapshot);
        if (snapshot.AnnouncementMode != AnnouncementDisplayMode.ExternalPage) return new Presentation(AnnouncementDisplayMode.NativeCards, string.Empty);
        bool ownsClient = client is null;
        client ??= new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, snapshot.ExternalAnnouncementUrl);
            using HttpResponseMessage response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength > 2 * 1024 * 1024) return new Presentation(AnnouncementDisplayMode.NativeCards, string.Empty);
            await using Stream input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var output = new MemoryStream(); byte[] buffer = new byte[16 * 1024]; int read;
            while ((read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
            {
                if (output.Length + read > 2 * 1024 * 1024) return new Presentation(AnnouncementDisplayMode.NativeCards, string.Empty);
                output.Write(buffer, 0, read);
            }
            string html = System.Text.Encoding.UTF8.GetString(output.ToArray());
            return string.IsNullOrWhiteSpace(html) ? new Presentation(AnnouncementDisplayMode.NativeCards, string.Empty) : new Presentation(AnnouncementDisplayMode.ExternalPage, html);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or OperationCanceledException)
        {
            return new Presentation(AnnouncementDisplayMode.NativeCards, string.Empty);
        }
        finally
        {
            if (ownsClient) client.Dispose();
        }
    }

    public static string RenderSafeText(string html)
    {
        if (string.IsNullOrEmpty(html)) return string.Empty;
        var output = new System.Text.StringBuilder(Math.Min(html.Length, 2 * 1024 * 1024));
        bool insideTag = false;
        foreach (char character in html)
        {
            if (character == '<') { insideTag = true; output.Append(' '); continue; }
            if (character == '>') { insideTag = false; output.Append(' '); continue; }
            if (!insideTag) output.Append(character);
        }
        return System.Net.WebUtility.HtmlDecode(output.ToString()).Trim();
    }
}
