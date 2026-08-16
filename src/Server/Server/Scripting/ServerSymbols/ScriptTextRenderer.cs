using System.Collections.ObjectModel;
using System.Text;

namespace Server.Scripting.ServerSymbols
{
    public interface IScriptTextRenderer
    {
        ScriptTextRenderResult Render(ServerSymbolContext context, string text);
        ScriptTextRenderResult Render(ServerSymbolContext context, string text, ScriptTextRenderLimits limits);
    }

    public enum ScriptTextRenderStatus
    {
        Unchanged,
        Rendered,
        CompletedWithDiagnostics,
        InvalidSyntax,
        LimitExceeded
    }

    public enum ScriptTextDiagnosticCode
    {
        SymbolResolutionFailed,
        CompatibilitySubstitute,
        InvalidSyntax,
        LimitExceeded
    }

    public sealed class ScriptTextRenderLimits
    {
        private const int AbsoluteMaximumInputLength = 1024 * 1024;
        private const int AbsoluteMaximumPlaceholders = 4096;
        private const int AbsoluteMaximumNestingDepth = 16;
        private const int AbsoluteMaximumOutputLength = 4 * 1024 * 1024;

        public ScriptTextRenderLimits(
            int maximumInputLength,
            int maximumPlaceholders,
            int maximumNestingDepth,
            int maximumOutputLength)
        {
            if (maximumInputLength <= 0) throw new ArgumentOutOfRangeException(nameof(maximumInputLength));
            if (maximumPlaceholders <= 0) throw new ArgumentOutOfRangeException(nameof(maximumPlaceholders));
            if (maximumNestingDepth <= 0) throw new ArgumentOutOfRangeException(nameof(maximumNestingDepth));
            if (maximumOutputLength <= 0) throw new ArgumentOutOfRangeException(nameof(maximumOutputLength));
            if (maximumInputLength > AbsoluteMaximumInputLength) throw new ArgumentOutOfRangeException(nameof(maximumInputLength));
            if (maximumPlaceholders > AbsoluteMaximumPlaceholders) throw new ArgumentOutOfRangeException(nameof(maximumPlaceholders));
            if (maximumNestingDepth > AbsoluteMaximumNestingDepth) throw new ArgumentOutOfRangeException(nameof(maximumNestingDepth));
            if (maximumOutputLength > AbsoluteMaximumOutputLength) throw new ArgumentOutOfRangeException(nameof(maximumOutputLength));

            MaximumInputLength = maximumInputLength;
            MaximumPlaceholders = maximumPlaceholders;
            MaximumNestingDepth = maximumNestingDepth;
            MaximumOutputLength = maximumOutputLength;
        }

        public static ScriptTextRenderLimits Default { get; } = new ScriptTextRenderLimits(8192, 64, 4, 32768);

        public int MaximumInputLength { get; }
        public int MaximumPlaceholders { get; }
        public int MaximumNestingDepth { get; }
        public int MaximumOutputLength { get; }
    }

    public readonly struct ScriptTextDiagnostic
    {
        internal ScriptTextDiagnostic(
            ScriptTextDiagnosticCode code,
            ServerSymbolStatus? symbolStatus,
            string canonicalName,
            int position,
            int length,
            string message)
        {
            Code = code;
            SymbolStatus = symbolStatus;
            CanonicalName = canonicalName ?? string.Empty;
            Position = Math.Max(0, position);
            Length = Math.Max(0, length);
            Message = message ?? string.Empty;
        }

        public ScriptTextDiagnosticCode Code { get; }
        public ServerSymbolStatus? SymbolStatus { get; }
        public string CanonicalName { get; }
        public int Position { get; }
        public int Length { get; }
        public string Message { get; }
    }

    public sealed class ScriptTextRenderResult
    {
        internal ScriptTextRenderResult(
            ScriptTextRenderStatus status,
            string text,
            int placeholderCount,
            IEnumerable<ScriptTextDiagnostic> diagnostics)
        {
            Status = status;
            Text = text ?? string.Empty;
            PlaceholderCount = Math.Max(0, placeholderCount);
            Diagnostics = new ReadOnlyCollection<ScriptTextDiagnostic>(
                (diagnostics ?? Array.Empty<ScriptTextDiagnostic>()).ToList());
        }

        public ScriptTextRenderStatus Status { get; }
        public string Text { get; }
        public int PlaceholderCount { get; }
        public IReadOnlyList<ScriptTextDiagnostic> Diagnostics { get; }
        public bool Success => Status is ScriptTextRenderStatus.Unchanged or ScriptTextRenderStatus.Rendered;
    }

    public sealed class ScriptTextRenderer : IScriptTextRenderer
    {
        private readonly IServerSymbolResolver _resolver;

        public ScriptTextRenderer(IServerSymbolResolver resolver) =>
            _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));

        public ScriptTextRenderResult Render(ServerSymbolContext context, string text) =>
            Render(context, text, ScriptTextRenderLimits.Default);

        public ScriptTextRenderResult Render(
            ServerSymbolContext context,
            string text,
            ScriptTextRenderLimits limits)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (text == null) throw new ArgumentNullException(nameof(text));
            if (limits == null) throw new ArgumentNullException(nameof(limits));

            var state = new RenderState(limits);
            if (text.Length > limits.MaximumInputLength)
            {
                state.FailLimit(0, text.Length, "脚本文本超过单行输入长度限制。");
                return AtomicFailure(text, state);
            }

            string rendered = RenderFragment(context, text, 0, 0, false, state);
            if (state.FatalStatus.HasValue) return AtomicFailure(text, state);

            ScriptTextRenderStatus status = state.Diagnostics.Count > 0
                ? ScriptTextRenderStatus.CompletedWithDiagnostics
                : !string.Equals(rendered, text, StringComparison.Ordinal)
                    ? ScriptTextRenderStatus.Rendered
                    : ScriptTextRenderStatus.Unchanged;
            return new ScriptTextRenderResult(status, rendered, state.PlaceholderCount, state.Diagnostics);
        }

        private string RenderFragment(
            ServerSymbolContext context,
            string fragment,
            int nestingDepth,
            int sourceOffset,
            bool skipQuotedPlaceholders,
            RenderState state)
        {
            var output = new StringBuilder(Math.Min(fragment.Length, state.Limits.MaximumOutputLength));
            int cursor = 0;
            while (cursor < fragment.Length)
            {
                int start = FindNextPlaceholderStart(fragment, cursor, skipQuotedPlaceholders);
                if (start < 0)
                {
                    output.Append(fragment, cursor, fragment.Length - cursor);
                    break;
                }

                output.Append(fragment, cursor, start - cursor);
                int remainingDepth = state.Limits.MaximumNestingDepth - nestingDepth;
                int end = FindPlaceholderEnd(fragment, start, remainingDepth, out bool nestingLimitExceeded);
                if (end < 0)
                {
                    if (nestingLimitExceeded)
                        state.FailLimit(sourceOffset + start, fragment.Length - start, "服务器常量超过嵌套深度限制。");
                    else
                        state.FailSyntax(sourceOffset + start, fragment.Length - start);
                    return fragment;
                }

                state.PlaceholderCount++;
                if (state.PlaceholderCount > state.Limits.MaximumPlaceholders)
                {
                    state.FailLimit(sourceOffset + start, end - start + 1, "脚本文本超过占位符数量限制。");
                    return fragment;
                }

                int currentDepth = nestingDepth + 1;
                if (currentDepth > state.Limits.MaximumNestingDepth)
                {
                    state.FailLimit(sourceOffset + start, end - start + 1, "服务器常量超过嵌套深度限制。");
                    return fragment;
                }

                string originalToken = fragment.Substring(start, end - start + 1);
                string inner = fragment.Substring(start + 2, end - start - 2);
                int diagnosticsBeforeNested = state.Diagnostics.Count;
                string renderedInner = RenderFragment(
                    context,
                    inner,
                    currentDepth,
                    sourceOffset + start + 2,
                    true,
                    state);
                if (state.FatalStatus.HasValue) return fragment;

                if (state.Diagnostics.Count > diagnosticsBeforeNested)
                {
                    output.Append(originalToken);
                    cursor = end + 1;
                    continue;
                }

                ServerSymbolReference reference = ServerSymbolReference.Parse("<$" + renderedInner + ">");
                if (!reference.IsValid)
                {
                    state.FailSyntax(
                        sourceOffset + start,
                        originalToken.Length,
                        "服务器常量引用语法无效。");
                    return fragment;
                }

                ServerSymbolResult result;
                try
                {
                    result = _resolver.Resolve(context, reference);
                }
                catch
                {
                    result = ServerSymbolResult.Fail(
                        ServerSymbolStatus.Faulted,
                        reference.NormalizedName,
                        "服务器常量解析失败。");
                }

                if (result.Status == ServerSymbolStatus.InvalidReference)
                {
                    state.FailSyntax(
                        sourceOffset + start,
                        originalToken.Length,
                        "服务器常量引用参数或索引无效。");
                    return fragment;
                }
                if (result.Success)
                {
                    output.Append(result.Value.Format());
                    if (result.Status == ServerSymbolStatus.CompatibilitySubstitute)
                    {
                        state.Diagnostics.Add(new ScriptTextDiagnostic(
                            ScriptTextDiagnosticCode.CompatibilitySubstitute,
                            result.Status,
                            result.CanonicalName,
                            sourceOffset + start,
                            originalToken.Length,
                            "服务器常量使用当前模型的兼容显示值。"));
                    }
                }
                else
                {
                    state.Diagnostics.Add(new ScriptTextDiagnostic(
                        ScriptTextDiagnosticCode.SymbolResolutionFailed,
                        result.Status,
                        result.CanonicalName,
                        sourceOffset + start,
                        originalToken.Length,
                        DiagnosticMessage(result.Status)));
                    output.Append(originalToken);
                }

                if (output.Length > state.Limits.MaximumOutputLength)
                {
                    state.FailLimit(sourceOffset + start, originalToken.Length, "脚本文本超过展开后长度限制。");
                    return fragment;
                }
                cursor = end + 1;
            }

            if (output.Length > state.Limits.MaximumOutputLength)
                state.FailLimit(sourceOffset, fragment.Length, "脚本文本超过展开后长度限制。");
            return output.ToString();
        }

        private static int FindPlaceholderEnd(
            string text,
            int start,
            int maximumDepth,
            out bool nestingLimitExceeded)
        {
            nestingLimitExceeded = false;
            var frames = new List<PlaceholderScanFrame> { default };
            for (int index = start + 2; index < text.Length; index++)
            {
                int frameIndex = frames.Count - 1;
                PlaceholderScanFrame frame = frames[frameIndex];
                if (frame.Quoted)
                {
                    if (frame.Escaped)
                        frame.Escaped = false;
                    else if (text[index] == '\\')
                        frame.Escaped = true;
                    else if (text[index] == '"')
                        frame.Quoted = false;
                    frames[frameIndex] = frame;
                    continue;
                }

                if (index + 1 < text.Length && text[index] == '<' && text[index + 1] == '$')
                {
                    if (frames.Count >= maximumDepth)
                    {
                        nestingLimitExceeded = true;
                        return -1;
                    }
                    frames.Add(default);
                    index++;
                    continue;
                }

                if (text[index] == '"')
                {
                    frame.Quoted = true;
                    frames[frameIndex] = frame;
                    continue;
                }

                if (text[index] == '(')
                    frame.ParenthesisDepth++;
                else if (text[index] == ')')
                {
                    if (frame.ParenthesisDepth == 0) return -1;
                    frame.ParenthesisDepth--;
                }
                else if (text[index] == '>' && frame.ParenthesisDepth == 0)
                {
                    frames.RemoveAt(frameIndex);
                    if (frames.Count == 0) return index;
                    continue;
                }
                frames[frameIndex] = frame;
            }
            return -1;
        }

        private static int FindNextPlaceholderStart(string text, int start, bool skipQuotedPlaceholders)
        {
            if (!skipQuotedPlaceholders)
                return text.IndexOf("<$", start, StringComparison.Ordinal);

            bool quoted = false;
            bool escaped = false;
            for (int index = start; index + 1 < text.Length; index++)
            {
                if (quoted)
                {
                    if (escaped)
                        escaped = false;
                    else if (text[index] == '\\')
                        escaped = true;
                    else if (text[index] == '"')
                        quoted = false;
                    continue;
                }
                if (text[index] == '"')
                {
                    quoted = true;
                    continue;
                }
                if (text[index] == '<' && text[index + 1] == '$') return index;
            }
            return -1;
        }

        private struct PlaceholderScanFrame
        {
            public int ParenthesisDepth;
            public bool Quoted;
            public bool Escaped;
        }

        private static string DiagnosticMessage(ServerSymbolStatus status) => status switch
        {
            ServerSymbolStatus.ContextUnavailable => "当前事件缺少服务器常量所需上下文。",
            ServerSymbolStatus.DependencyMissing => "服务器常量所需数据尚未提供。",
            ServerSymbolStatus.SensitiveDenied => "服务器常量因安全策略拒绝解析。",
            ServerSymbolStatus.Unsupported => "服务器常量尚未登记支持。",
            ServerSymbolStatus.InvalidReference => "服务器常量引用语法无效。",
            ServerSymbolStatus.Faulted => "服务器常量解析失败。",
            ServerSymbolStatus.CompatibilitySubstitute => "服务器常量使用当前模型的兼容显示值。",
            _ => "服务器常量未能解析。"
        };

        private static ScriptTextRenderResult AtomicFailure(string source, RenderState state) =>
            new ScriptTextRenderResult(
                state.FatalStatus ?? ScriptTextRenderStatus.InvalidSyntax,
                source,
                state.PlaceholderCount,
                state.Diagnostics);

        private sealed class RenderState
        {
            public RenderState(ScriptTextRenderLimits limits) => Limits = limits;

            public ScriptTextRenderLimits Limits { get; }
            public int PlaceholderCount { get; set; }
            public ScriptTextRenderStatus? FatalStatus { get; private set; }
            public List<ScriptTextDiagnostic> Diagnostics { get; } = new List<ScriptTextDiagnostic>();

            public void FailSyntax(int position, int length, string message = "服务器常量占位符未闭合或括号不匹配。")
            {
                if (FatalStatus.HasValue) return;
                FatalStatus = ScriptTextRenderStatus.InvalidSyntax;
                Diagnostics.Add(new ScriptTextDiagnostic(
                    ScriptTextDiagnosticCode.InvalidSyntax,
                    ServerSymbolStatus.InvalidReference,
                    string.Empty,
                    position,
                    length,
                    message));
            }

            public void FailLimit(int position, int length, string message)
            {
                if (FatalStatus.HasValue) return;
                FatalStatus = ScriptTextRenderStatus.LimitExceeded;
                Diagnostics.Add(new ScriptTextDiagnostic(
                    ScriptTextDiagnosticCode.LimitExceeded,
                    null,
                    string.Empty,
                    position,
                    length,
                    message));
            }
        }
    }
}
