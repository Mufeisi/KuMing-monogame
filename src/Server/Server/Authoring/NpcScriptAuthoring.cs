using Server.Scripting;

namespace Server.Authoring;

public sealed record NpcScriptPagePreview(string Key, IReadOnlyList<string> Lines, IReadOnlyList<string> Links)
{
    public override string ToString() => $"{Key}（{Lines.Count} 行，{Links.Count} 个链接）";
}
public sealed record NpcScriptDiagnostic(string Code, string PageKey, string Message);
public sealed record NpcScriptPreview(
    string NpcFileName,
    string Source,
    IReadOnlyList<NpcScriptPagePreview> Pages,
    IReadOnlyList<NpcScriptDiagnostic> Diagnostics);

/// <summary>将现有 NPC 文本定义投影为可预览、可检查的页面图。</summary>
public static class NpcScriptAuthoring
{
    public static NpcScriptPreview BuildPreview(string npcFileName, IEnumerable<string> lines, string source)
    {
        string fileName = (npcFileName ?? string.Empty).Trim();
        var diagnostics = new List<NpcScriptDiagnostic>();
        var pages = new List<NpcScriptPagePreview>();

        if (fileName.Length == 0)
            diagnostics.Add(new NpcScriptDiagnostic("CONTENT03-NPC-001", string.Empty, "NPC 脚本名不能为空。"));

        IReadOnlyList<NpcDialogPageGraphEntry> graph = NpcScriptCoverage.BuildDialogPageGraph(lines);
        var keys = graph.Select(value => value.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (NpcDialogPageGraphEntry page in graph)
        {
            pages.Add(new NpcScriptPagePreview(page.Key, page.Lines, page.Links));
            foreach (string link in page.Links)
                if (!IsBuiltin(link) && !keys.Contains(link))
                    diagnostics.Add(new NpcScriptDiagnostic("CONTENT03-LINK-001", page.Key, $"链接目标不存在：{link}"));
        }

        if (pages.Count == 0)
            diagnostics.Add(new NpcScriptDiagnostic("CONTENT03-NPC-002", string.Empty, "脚本没有可预览的对话页。"));
        else if (!keys.Contains("[@MAIN]"))
            diagnostics.Add(new NpcScriptDiagnostic("CONTENT03-LINK-002", string.Empty, "脚本缺少入口页 [@MAIN]。"));

        return new NpcScriptPreview(fileName, source ?? string.Empty, pages, diagnostics);
    }

    private static bool IsBuiltin(string value) => value.Equals("[@EXIT]", StringComparison.OrdinalIgnoreCase);
}
