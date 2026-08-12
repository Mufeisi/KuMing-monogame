using System.Text.RegularExpressions;

namespace Server.Scripting
{
    /// <summary>
    /// C# 侧 NPC 对话输出构建器（不要直接暴露网络包）。
    /// 说明：
    /// - Lines 为“原样输出”的行文本（与 txt 的 #SAY 行一致），最终会以 NPCResponse 发给客户端。
    /// - Button/Close 只是生成与旧脚本一致的按钮语法：&lt;按钮文本/@PAGE&gt;
    /// </summary>
    public sealed class NpcDialog
    {
        private static readonly Regex ButtonRegex = new Regex(@"<.*?/(\@.*?)>", RegexOptions.Compiled);

        private readonly List<string> _lines = new List<string>();
        private readonly HashSet<string> _allowedPageKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// 可选：用于服务端“立即跳转”的页面 Key（不需要客户端点击按钮）。
        /// </summary>
        public string RedirectToPageKey { get; private set; } = string.Empty;

        public IReadOnlyList<string> Lines => _lines;

        /// <summary>
        /// 允许的下一跳 PageKey（用于按钮安全校验）；会与 Lines 中解析出的按钮 Key 合并。
        /// </summary>
        public IReadOnlyCollection<string> AllowedPageKeys => _allowedPageKeys;

        public void Say(string line)
        {
            _lines.Add(line ?? string.Empty);
        }

        public void Say(params string[] lines)
        {
            if (lines == null) return;
            for (var i = 0; i < lines.Length; i++)
            {
                Say(lines[i]);
            }
        }

        public void Button(string text, string gotoPageKey)
        {
            var label = NormalizeLabel(gotoPageKey);
            _lines.Add($"<{text}/{label}>");
            AllowPageKeyFromLabel(label);
        }

        public void Close(string text = "关闭")
        {
            // 客户端对 "@Exit" 有特殊处理：会直接关闭对话框而不再向服务端发起 CallNPC。
            Button(text, "[@Exit]");
        }

        /// <summary>
        /// 请求客户端弹出输入框，并在输入完成后回调到 <paramref name="inputPageKey"/>（即 legacy 的 [@@] 机制）。
        /// 注意：该方法只负责“发起输入”；处理输入请在脚本侧注册对应的 [@@] 页面处理器（见 NpcRegistry.RequestInput）。
        /// </summary>
        public void RequestInput(string inputPageKey)
        {
            var pageKey = NormalizeInputPageKey(inputPageKey);
            if (string.IsNullOrEmpty(pageKey)) return;

            // 确保输入页通过按钮安全校验（NPCConfirmInput 回调时会走同一套校验）。
            Allow(pageKey);

            // 通过服务端 GOTO 触发输入页，让引擎按旧流程发送 NPCRequestInput。
            Goto(pageKey);
        }

        /// <summary>
        /// 允许某个 Key 作为下一跳（不自动输出按钮行）。
        /// </summary>
        public void Allow(string pageKey)
        {
            _allowedPageKeys.Add(NormalizePageKey(pageKey));
        }

        /// <summary>
        /// 服务端立即跳转到指定页面（等价于脚本里的 GOTO，但不依赖客户端点击）。
        /// </summary>
        public void Goto(string pageKey)
        {
            RedirectToPageKey = NormalizePageKey(pageKey);
        }

        public void Clear()
        {
            _lines.Clear();
            _allowedPageKeys.Clear();
            RedirectToPageKey = string.Empty;
        }

        internal void ImportAllowedKeysFromLines()
        {
            for (var i = 0; i < _lines.Count; i++)
            {
                var line = _lines[i] ?? string.Empty;
                var match = ButtonRegex.Match(line);

                while (match.Success)
                {
                    var label = match.Groups[1].Captures[0].Value;
                    label = label.Split('/')[0];
                    AllowPageKeyFromLabel(label);

                    match = match.NextMatch();
                }
            }
        }

        private void AllowPageKeyFromLabel(string label)
        {
            if (string.IsNullOrWhiteSpace(label)) return;
            label = NormalizeLabel(label);
            _allowedPageKeys.Add($"[{label}]");
        }

        private static string NormalizePageKey(string pageKey)
        {
            if (string.IsNullOrWhiteSpace(pageKey))
                return string.Empty;

            var key = pageKey.Trim();

            if (key.StartsWith("[@", StringComparison.Ordinal) && key.EndsWith("]", StringComparison.Ordinal))
                return key;

            var label = NormalizeLabel(key);
            return $"[{label}]";
        }

        private static string NormalizeLabel(string labelOrPageKey)
        {
            if (string.IsNullOrWhiteSpace(labelOrPageKey))
                return string.Empty;

            var s = labelOrPageKey.Trim();

            // 传入 PageKey：[@MAIN] -> @MAIN
            if (s.StartsWith("[@", StringComparison.Ordinal) && s.EndsWith("]", StringComparison.Ordinal) && s.Length >= 3)
            {
                return s.Substring(1, s.Length - 2);
            }

            // 传入 Label：@MAIN -> @MAIN
            if (s.StartsWith("@", StringComparison.Ordinal))
                return s;

            // 传入简写：MAIN -> @MAIN
            return "@" + s;
        }

        private static string NormalizeInputPageKey(string inputKeyOrPageKey)
        {
            if (string.IsNullOrWhiteSpace(inputKeyOrPageKey))
                return string.Empty;

            var s = inputKeyOrPageKey.Trim();

            if (s.StartsWith("[@@", StringComparison.OrdinalIgnoreCase) && s.EndsWith("]", StringComparison.Ordinal))
                return s;

            // [@X] -> [@@X]
            if (s.StartsWith("[@", StringComparison.OrdinalIgnoreCase) && s.EndsWith("]", StringComparison.Ordinal))
                return "[@@" + s.Substring(2);

            // @@X -> [@@X]
            if (s.StartsWith("@@", StringComparison.OrdinalIgnoreCase))
                return $"[{s}]";

            // @X -> [@@X]
            if (s.StartsWith("@", StringComparison.Ordinal))
                return $"[@@{s.Substring(1)}]";

            // X -> [@@X]
            return $"[@@{s}]";
        }
    }
}
