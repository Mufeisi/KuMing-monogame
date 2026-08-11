namespace Launcher.ThemeRuntime;

public static class AnnouncementPresentationResolver
{
    public static async Task<AnnouncementDisplayMode> ResolveAsync(LauncherSnapshot snapshot, HttpClient? client = null, CancellationToken cancellationToken = default)
    {
        LauncherSnapshotValidator.Validate(snapshot);
        if (snapshot.AnnouncementMode != AnnouncementDisplayMode.ExternalPage) return AnnouncementDisplayMode.NativeCards;
        bool ownsClient = client is null;
        client ??= new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, snapshot.ExternalAnnouncementUrl);
            using HttpResponseMessage response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            return response.IsSuccessStatusCode ? AnnouncementDisplayMode.ExternalPage : AnnouncementDisplayMode.NativeCards;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return AnnouncementDisplayMode.NativeCards;
        }
        finally
        {
            if (ownsClient) client.Dispose();
        }
    }
}
