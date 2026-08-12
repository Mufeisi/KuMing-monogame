using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace Launcher.ThemeRuntime;

public enum ExternalAnnouncementElementKind { Paragraph, Heading, Link, Image }
public sealed record ExternalAnnouncementElement(ExternalAnnouncementElementKind Kind, string Text, string Url = "", bool Bold = false, string Color = "");

public static class SafeExternalAnnouncementDocument
{
    private static readonly Regex ColorPattern = new("#[0-9A-Fa-f]{6}", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(50));

    public static IReadOnlyList<ExternalAnnouncementElement> Parse(string html, Uri? documentUri = null)
    {
        var result = new List<ExternalAnnouncementElement>();
        var text = new StringBuilder();
        string link = string.Empty, color = string.Empty; bool bold = false, heading = false;
        void Flush()
        {
            string value = WebUtility.HtmlDecode(text.ToString()).Trim(); text.Clear();
            if (value.Length == 0) return;
            result.Add(new ExternalAnnouncementElement(!string.IsNullOrEmpty(link) ? ExternalAnnouncementElementKind.Link : heading ? ExternalAnnouncementElementKind.Heading : ExternalAnnouncementElementKind.Paragraph, value, link, bold || heading, color));
        }
        for (int index = 0; index < html.Length;)
        {
            int open = html.IndexOf('<', index);
            if (open < 0) { text.Append(html, index, html.Length - index); break; }
            text.Append(html, index, open - index);
            int close = html.IndexOf('>', open + 1); if (close < 0) break;
            string tag = html[(open + 1)..close].Trim();
            if (tag.Length > 4096) { index = close + 1; continue; }
            bool closing = tag.StartsWith('/'); string body = closing ? tag[1..].TrimStart() : tag;
            string name = body.Split(new[] { ' ', '\t', '\r', '\n', '/' }, 2, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.ToLowerInvariant() ?? string.Empty;
            if (!closing && name is "script" or "style" or "object" or "iframe" or "form")
            {
                Flush(); string end = "</" + name; int endAt = html.IndexOf(end, close + 1, StringComparison.OrdinalIgnoreCase);
                if (endAt < 0) break; int endClose = html.IndexOf('>', endAt + end.Length); index = endClose < 0 ? html.Length : endClose + 1; continue;
            }
            if (name is "br" or "p" or "div" or "li" or "tr" or "blockquote" || name.Length == 2 && name[0] == 'h' && char.IsDigit(name[1]))
            {
                Flush(); heading = !closing && name.Length == 2 && name[0] == 'h';
                bold = heading || !closing && name is "strong" or "b"; color = closing ? string.Empty : ExtractColor(body);
            }
            else if (name is "strong" or "b") { Flush(); bold = !closing; }
            else if (name == "a")
            {
                Flush(); link = closing ? string.Empty : SafeUrl(ExtractAttribute(body, "href"), documentUri);
            }
            else if (!closing && name == "img")
            {
                Flush(); string url = SafeImageUrl(ExtractAttribute(body, "src"), documentUri);
                if (!string.IsNullOrEmpty(url) && result.Count(item => item.Kind == ExternalAnnouncementElementKind.Image) < 6)
                    result.Add(new ExternalAnnouncementElement(ExternalAnnouncementElementKind.Image, WebUtility.HtmlDecode(ExtractAttribute(body, "alt")), url));
            }
            index = close + 1;
        }
        Flush();
        if (result.Count == 0)
        {
            string fallback = AnnouncementPresentationResolver.RenderSafeText(html);
            if (!string.IsNullOrWhiteSpace(fallback)) result.Add(new ExternalAnnouncementElement(ExternalAnnouncementElementKind.Paragraph, fallback));
        }
        return result.Take(200).ToArray();
    }

    private static string SafeUrl(string value, Uri? documentUri = null)
    {
        string decoded = WebUtility.HtmlDecode(value);
        if (documentUri is not null && Uri.TryCreate(documentUri, decoded, out Uri? relative) && relative.Scheme is "http" or "https") return relative.AbsoluteUri;
        return LauncherActionDispatcher.TryGetHttpUri(decoded, out Uri? uri) ? uri!.AbsoluteUri : string.Empty;
    }
    private static string SafeImageUrl(string value, Uri? documentUri)
    {
        string safe = SafeUrl(value, documentUri);
        if (string.IsNullOrEmpty(safe) || documentUri is null || !Uri.TryCreate(safe, UriKind.Absolute, out Uri? imageUri)) return string.Empty;
        return imageUri.Scheme == documentUri.Scheme && imageUri.Host == documentUri.Host && imageUri.Port == documentUri.Port ? imageUri.AbsoluteUri : string.Empty;
    }
    private static string ExtractColor(string tag) { Match match = ColorPattern.Match(tag); return match.Success ? match.Value : string.Empty; }

    private static string ExtractAttribute(string tag, string name)
    {
        Match match = Regex.Match(tag, "(?i)\\b" + Regex.Escape(name) + "\\s*=\\s*(?:\"([^\"]*)\"|'([^']*)'|([^\\s>]+))", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(50));
        if (!match.Success) return string.Empty;
        for (int i = 1; i < match.Groups.Count; i++) if (match.Groups[i].Success) return match.Groups[i].Value;
        return string.Empty;
    }
}
