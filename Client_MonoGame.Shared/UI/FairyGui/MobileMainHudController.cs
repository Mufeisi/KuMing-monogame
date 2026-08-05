using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using FairyGUI;
using MonoShare.MirControls;
using MonoShare.MirScenes;

namespace MonoShare
{
    internal sealed class MobileMainHudController : IDisposable
    {
        private const string ConfigSectionName = "FairyGUI";
        private const string ConfigKeyPrefix = "MobileMainHud.";

        private sealed class MatchCandidate
        {
            public int Score;
            public GObject Target;
        }

        private sealed class ClickBinding
        {
            public string ActionKey;
            public string ActionName;
            public GObject Target;
            public EventCallback0 Callback;
        }

        private sealed class BindingResult
        {
            public string ActionKey;
            public string ActionName;
            public string OverrideSpec;
            public string Keywords;
            public string Error;
            public GObject Target;
            public int SelectedScore;
            public List<MatchCandidate> Candidates;
        }

        private readonly GComponent _root;
        private readonly List<ClickBinding> _bindings = new List<ClickBinding>();
        private readonly List<BindingResult> _results = new List<BindingResult>();
        private bool _bound;
        private long _lastActionClickLogMs;

        public MobileMainHudController(GComponent root)
        {
            _root = root ?? throw new ArgumentNullException(nameof(root));
        }

        public bool BindIfNeeded()
        {
            if (_bound)
                return _bindings.Count > 0;

            _bound = true;
            _results.Clear();

            bool allBound = true;
            InIReader reader = TryCreateReader();

            var usedTargets = new HashSet<GObject>();

            allBound &= Bind(reader, usedTargets, "Pickup", "拾取", new[] { "pickup", "pick", "拾取" }, MobileHudActionCommands.TryPickUp);
            allBound &= Bind(reader, usedTargets, "Attack", "攻击", new[] { "attack", "arrack", "atk", "攻击" }, MobileHudActionCommands.TryAttack);
            allBound &= Bind(reader, usedTargets, "AutoRun", "自动跑", new[] { "autorun", "auto_run", "run", "autowalk", "自动跑", "跑" }, () => MapControl.AutoRun = !MapControl.AutoRun);
            allBound &= Bind(reader, usedTargets, "AutoHit", "自动打", new[] { "autohit", "auto_hit", "hit", "autofight", "自动打", "打" }, () => MapControl.AutoHit = !MapControl.AutoHit);
            allBound &= Bind(reader, usedTargets, "AttackMode", "模式", new[] { "attackmode", "arrackmodel", "darrackmodelui", "mode", "模式" }, () => GameScene.Scene?.ChangeAttackMode());

            allBound &= Bind(reader, usedTargets, "Magic", "技能", new[] { "magic", "skill", "技能", "技" }, () => GameScene.Scene?.ToggleMobileMagicOverlay());
            allBound &= Bind(reader, usedTargets, "State", "角色", new[] { "dbtnstate", "state", "角色", "属性", "状态" }, () => GameScene.Scene?.ToggleMobileStateOverlay());
            allBound &= Bind(reader, usedTargets, "Inventory", "背包", new[] { "dbtnbag", "bag", "inventory", "背包", "包" }, () => GameScene.Scene?.ToggleMobileInventoryOverlay());
            allBound &= Bind(reader, usedTargets, "Chat", "聊天", new[] { "dbtnchat", "chat", "聊天", "聊" }, () => GameScene.Scene?.ToggleMobileChatOverlay());
            allBound &= Bind(reader, usedTargets, "Shop", "商店", new[] { "shop", "store", "mall", "商店", "商" }, () => GameScene.Scene?.ToggleMobileGameShopOverlay());
            allBound &= Bind(reader, usedTargets, "System", "系统", new[] { "system", "setting", "menu", "options", "help", "系统", "设置" }, () => GameScene.Scene?.ToggleMobileSystemMenuOverlay());
            allBound &= Bind(reader, usedTargets, "BigMap", "大地图", new[] { "dbtnmap", "bigmap", "big_map", "dbigmap", "大地图", "地图", "map" }, () => GameScene.Scene?.ToggleMobileBigMapOverlay());

            TryWriteBindingsReport();

            if (!allBound)
            {
                var missing = new List<string>();
                for (int i = 0; i < _results.Count; i++)
                {
                    BindingResult item = _results[i];
                    if (item?.Target == null)
                        missing.Add(item?.ActionName ?? "(null)");
                }

                if (missing.Count > 0)
                    CMain.SaveLog("FairyGUI: 主界面按钮绑定未完全命中，待补充映射：" + string.Join("、", missing));
            }
            else
            {
                CMain.SaveLog("FairyGUI: 主界面按钮绑定完成（POC）");
            }

            return allBound;
        }

        public void Dispose()
        {
            for (int i = 0; i < _bindings.Count; i++)
            {
                ClickBinding binding = _bindings[i];
                try
                {
                    binding.Target?.onClick.Remove(binding.Callback);
                }
                catch
                {
                }
            }

            _bindings.Clear();
            _results.Clear();
        }

        private bool Bind(InIReader reader, ISet<GObject> usedTargets, string actionKey, string actionName, string[] defaultKeywords, Action action)
        {
            if (_root == null || _root._disposed)
                return false;

            if (string.IsNullOrWhiteSpace(actionKey) || string.IsNullOrWhiteSpace(actionName))
                return false;

            if (action == null)
                return false;

            string overrideSpec = ReadOverride(reader, actionKey);

            bool explicitOverride = LooksLikeExplicitSpec(overrideSpec);
            bool explicitOverrideResolved = false;

            string[] keywordsForMatch = defaultKeywords ?? Array.Empty<string>();
            if (!string.IsNullOrWhiteSpace(overrideSpec) && !explicitOverride)
            {
                string[] overrideKeywords = SplitKeywords(overrideSpec);
                if (overrideKeywords.Length > 0)
                    keywordsForMatch = overrideKeywords;
            }

            GObject target = null;
            string error = null;
            int selectedScore = 0;
            List<MatchCandidate> candidates = null;

            if (!string.IsNullOrWhiteSpace(overrideSpec) && explicitOverride)
            {
                target = FindByOverride(_root, overrideSpec);
                if (target == null)
                    error = "OverrideNotFound";
                else
                    explicitOverrideResolved = true;
            }

            // 移动端 HUD 里一些按钮的名字是稳定的（例如 DBtnState），优先按精确名字绑定，避免命中到“关闭/装饰”等同关键字对象导致点击无效。
            if (target == null && !explicitOverride)
            {
                string preferName = actionKey switch
                {
                    "Pickup" => "BottomAuPickupBtn",
                    "AttackMode" => "DBtnAttack0",
                    "State" => "DBtnState",
                    "Inventory" => "DBtnBag",
                    "Chat" => "DBtnChat",
                    "BigMap" => "DBtnMap",
                    _ => null,
                };

                if (!string.IsNullOrWhiteSpace(preferName))
                {
                    try
                    {
                        // 有些 publish 的 instance name 可能不是固定值，但 packageItem.name 通常稳定。
                        GObject byName = FindByExact(_root, preferName, matchName: true, matchItem: true, matchUrl: false, matchTitle: false);
                        if (byName != null && !byName._disposed &&
                            (usedTargets == null || !usedTargets.Contains(byName)))
                        {
                            try
                            {
                                byName.touchable = true;
                                if (byName is GButton button)
                                {
                                    button.enabled = true;
                                    button.grayed = false;
                                }
                            }
                            catch
                            {
                            }

                            target = byName;
                        }
                    }
                    catch
                    {
                    }
                }
            }

            if (target == null)
            {
                target = FindBestMatch(_root, keywordsForMatch, excluded: usedTargets, out selectedScore, out candidates);
                if (target == null)
                    error = string.IsNullOrWhiteSpace(error) ? "NoMatch" : (error + ";NoMatch");
            }

            if (target != null &&
                string.Equals(actionKey, "Attack", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    string name = target.name ?? string.Empty;
                    string itemName = target.packageItem?.name ?? string.Empty;
                    bool isAttackRingTarget =
                        string.Equals(name, "DArrackModelUI", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(itemName, "DArrackModelUI", StringComparison.OrdinalIgnoreCase) ||
                        name.StartsWith("DBtnAttack", StringComparison.OrdinalIgnoreCase) ||
                        itemName.StartsWith("DBtnAttack", StringComparison.OrdinalIgnoreCase);
                    if (isAttackRingTarget)
                    {
                        target = null;
                        error = string.IsNullOrWhiteSpace(error) ? "FilteredAttackRingTarget" : (error + ";FilteredAttackRingTarget");
                    }
                }
                catch
                {
                }
            }

            if (target == null || target._disposed)
            {
                _results.Add(new BindingResult
                {
                    ActionKey = actionKey,
                    ActionName = actionName,
                    OverrideSpec = overrideSpec,
                    Keywords = string.Join("|", keywordsForMatch ?? Array.Empty<string>()),
                    Error = string.IsNullOrWhiteSpace(error) ? "NoMatch" : error,
                    Target = null,
                    SelectedScore = selectedScore,
                    Candidates = candidates,
                });
                return false;
            }

            if (!explicitOverrideResolved && usedTargets != null && usedTargets.Contains(target))
            {
                _results.Add(new BindingResult
                {
                    ActionKey = actionKey,
                    ActionName = actionName,
                    OverrideSpec = overrideSpec,
                    Keywords = string.Join("|", keywordsForMatch ?? Array.Empty<string>()),
                    Error = "TargetAlreadyBound",
                    Target = null,
                    SelectedScore = selectedScore,
                    Candidates = candidates,
                });
                return false;
            }

            EventCallback0 callback = () =>
            {
                try
                {
                    try
                    {
                        if (Settings.LogErrors &&
                            (string.Equals(actionKey, "State", StringComparison.OrdinalIgnoreCase) ||
                             string.Equals(actionKey, "Inventory", StringComparison.OrdinalIgnoreCase) ||
                             string.Equals(actionKey, "BigMap", StringComparison.OrdinalIgnoreCase)))
                        {
                            long now = CMain.Time;
                            if (now - _lastActionClickLogMs > 1000)
                            {
                                _lastActionClickLogMs = now;
                                CMain.SaveLog($"FairyGUI: HUD 点击触发：{actionKey} target={Describe(_root, target)}");
                            }
                        }
                    }
                    catch
                    {
                    }

                    if (Settings.DebugMode)
                    {
                        string line = "FairyGUI Action: " + actionKey + " (" + actionName + ")";
                        CMain.SaveLog(line);

                        string existing = CMain.DebugText ?? string.Empty;
                        CMain.DebugText = string.IsNullOrWhiteSpace(existing) ? line : (existing + "\n" + line);
                    }

                    action();
                }
                catch (Exception ex)
                {
                    CMain.SaveError("FairyGUI: 主界面按钮[" + actionName + "] 点击执行异常：" + ex);
                }
            };

            try
            {
                target.onClick.Add(callback);
                _bindings.Add(new ClickBinding { ActionKey = actionKey, ActionName = actionName, Target = target, Callback = callback });
                _results.Add(new BindingResult
                {
                    ActionKey = actionKey,
                    ActionName = actionName,
                    OverrideSpec = overrideSpec,
                    Keywords = string.Join("|", keywordsForMatch ?? Array.Empty<string>()),
                    Error = error,
                    Target = target,
                    SelectedScore = selectedScore,
                    Candidates = candidates,
                });

                usedTargets?.Add(target);

                if (Settings.DebugMode)
                    CMain.SaveLog("FairyGUI: 已绑定按钮[" + actionName + "] -> " + Describe(_root, target));
            }
            catch (Exception ex)
            {
                CMain.SaveError("FairyGUI: 绑定按钮[" + actionName + "] 失败：" + ex.Message);
                _results.Add(new BindingResult
                {
                    ActionKey = actionKey,
                    ActionName = actionName,
                    OverrideSpec = overrideSpec,
                    Keywords = string.Join("|", keywordsForMatch ?? Array.Empty<string>()),
                    Error = ex.Message,
                    Target = null,
                    SelectedScore = selectedScore,
                    Candidates = candidates,
                });
                return false;
            }

            return true;
        }

        private static InIReader TryCreateReader()
        {
            try
            {
                return new InIReader(Settings.ConfigFilePath);
            }
            catch
            {
                return null;
            }
        }

        private static string ReadOverride(InIReader reader, string actionKey)
        {
            if (reader == null || string.IsNullOrWhiteSpace(actionKey))
                return string.Empty;

            try
            {
                string key = ConfigKeyPrefix + actionKey.Trim();
                return reader.ReadString(ConfigSectionName, key, string.Empty, writeWhenNull: false)?.Trim() ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private void TryWriteBindingsReport()
        {
            if (!Settings.DebugMode)
                return;

            try
            {
                Directory.CreateDirectory(ClientResourceLayout.RuntimeRoot);

                var builder = new StringBuilder(8 * 1024);
                builder.AppendLine("FairyGUI 主界面按钮绑定报告（用于排障/补充映射）");
                builder.AppendLine($"GeneratedAtUtc={DateTime.UtcNow:o}");
                builder.AppendLine($"Root={Describe(_root, _root)}");
                builder.AppendLine();
                builder.AppendLine("配置覆盖（可选）：Mir2Config.ini -> [FairyGUI]");
                builder.AppendLine("  MobileMainHud.<ActionKey>=<Spec>");
                builder.AppendLine("  Spec 支持：path:... / idx:... / name:... / item:... / url:... / title:... / 或者关键字列表(a|b|c)");
                builder.AppendLine("  ActionKey: Pickup/Attack/AutoRun/AutoHit/AttackMode/Magic/Inventory/Chat/Shop/System/BigMap");
                builder.AppendLine();

                if (_results.Count == 0)
                {
                    builder.AppendLine("无绑定结果（未执行 BindIfNeeded 或提前返回）。");
                }
                else
                {
                    for (int i = 0; i < _results.Count; i++)
                    {
                        BindingResult item = _results[i];
                        if (item == null)
                            continue;

                        string status = item.Target != null ? "Bound" : "Missing";
                        builder.AppendLine($"[{status}] {item.ActionKey} ({item.ActionName})");

                        if (!string.IsNullOrWhiteSpace(item.OverrideSpec))
                            builder.AppendLine($"  Override={item.OverrideSpec}");

                        if (!string.IsNullOrWhiteSpace(item.Keywords))
                            builder.AppendLine($"  Keywords={item.Keywords}");

                        if (item.SelectedScore > 0)
                            builder.AppendLine($"  Score={item.SelectedScore}");

                        if (!string.IsNullOrWhiteSpace(item.Error))
                            builder.AppendLine($"  Error={item.Error}");

                        if (item.Target != null)
                            builder.AppendLine($"  Target={Describe(_root, item.Target)}");

                        if (item.Candidates != null && item.Candidates.Count > 0)
                        {
                            builder.AppendLine("  Candidates(top):");
                            for (int j = 0; j < item.Candidates.Count; j++)
                            {
                                MatchCandidate candidate = item.Candidates[j];
                                if (candidate?.Target == null || candidate.Target._disposed)
                                    continue;

                                builder.AppendLine($"    - Score={candidate.Score} {Describe(_root, candidate.Target)}");
                            }
                        }

                        builder.AppendLine();
                    }
                }

                string path = Path.Combine(ClientResourceLayout.RuntimeRoot, "FairyGui-MobileMainHudBindings.txt");
                File.WriteAllText(path, builder.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            }
            catch (Exception ex)
            {
                CMain.SaveError("FairyGUI: 导出主界面按钮绑定报告失败：" + ex.Message);
            }
        }

        private static GObject FindBestMatch(GComponent root, string[] keywords, ISet<GObject> excluded, out int bestScore, out List<MatchCandidate> candidates)
        {
            bestScore = 0;
            candidates = new List<MatchCandidate>();

            if (root == null || root._disposed)
                return null;

            if (keywords == null || keywords.Length == 0)
                return null;

            candidates = FindTopMatches(root, keywords, requireButton: true, excluded, maxResults: 6);
            if (candidates.Count > 0)
            {
                bestScore = candidates[0].Score;
                return candidates[0].Target;
            }

            candidates = FindTopMatches(root, keywords, requireButton: false, excluded, maxResults: 6);
            if (candidates.Count > 0)
            {
                bestScore = candidates[0].Score;
                return candidates[0].Target;
            }

            return null;
        }

        private static List<MatchCandidate> FindTopMatches(GComponent root, string[] keywords, bool requireButton, ISet<GObject> excluded, int maxResults)
        {
            var list = new List<MatchCandidate>();
            if (root == null || root._disposed || keywords == null || keywords.Length == 0)
                return list;

            foreach (GObject obj in Enumerate(root))
            {
                if (ReferenceEquals(obj, root))
                    continue;

                if (requireButton && obj is not GButton)
                    continue;

                if (!obj.touchable || !obj.visible)
                    continue;

                if (excluded != null && excluded.Contains(obj))
                    continue;

                int score = Score(obj, keywords);
                if (score <= 0)
                    continue;

                if (obj is GButton)
                    score += 10000;

                if (obj.packageItem?.exported == true)
                    score += 2000;

                InsertCandidate(list, new MatchCandidate { Score = score, Target = obj }, maxResults);
            }

            return list;
        }

        private static void InsertCandidate(IList<MatchCandidate> list, MatchCandidate candidate, int maxResults)
        {
            if (list == null || candidate == null || candidate.Target == null)
                return;

            if (maxResults <= 0)
                return;

            int insertAt = -1;
            for (int i = 0; i < list.Count; i++)
            {
                if (candidate.Score > list[i].Score)
                {
                    insertAt = i;
                    break;
                }
            }

            if (insertAt < 0)
            {
                if (list.Count < maxResults)
                    list.Add(candidate);
                return;
            }

            list.Insert(insertAt, candidate);
            while (list.Count > maxResults)
                list.RemoveAt(list.Count - 1);
        }

        private static IEnumerable<GObject> Enumerate(GObject root)
        {
            if (root == null)
                yield break;

            yield return root;

            if (root is not GComponent component)
                yield break;

            int count = component.numChildren;
            for (int i = 0; i < count; i++)
            {
                foreach (GObject child in Enumerate(component.GetChildAt(i)))
                    yield return child;
            }
        }

        private static int Score(GObject obj, string[] keywords)
        {
            if (obj == null || keywords == null)
                return 0;

            string name = obj.name;
            string itemName = obj.packageItem?.name;
            string url = obj.resourceURL;
            string title = (obj as GButton)?.title;

            int score = 0;
            for (int i = 0; i < keywords.Length; i++)
            {
                string keyword = keywords[i];
                if (string.IsNullOrWhiteSpace(keyword))
                    continue;

                keyword = keyword.Trim();

                int weight = Math.Clamp(keyword.Length, 1, 16);
                if (keyword.Length <= 1)
                    weight = 1;

                score += ScoreField(name, keyword, equalsWeight: 1200, startsWithWeight: 900, containsWeight: 700) * weight;
                score += ScoreField(itemName, keyword, equalsWeight: 1100, startsWithWeight: 800, containsWeight: 600) * weight;
                score += ScoreField(title, keyword, equalsWeight: 900, startsWithWeight: 600, containsWeight: 400) * weight;
                score += ScoreField(url, keyword, equalsWeight: 600, startsWithWeight: 450, containsWeight: 250) * weight;
            }

            return score;
        }

        private static int ScoreField(string value, string keyword, int equalsWeight, int startsWithWeight, int containsWeight)
        {
            if (string.IsNullOrWhiteSpace(value) || string.IsNullOrWhiteSpace(keyword))
                return 0;

            if (value.Equals(keyword, StringComparison.OrdinalIgnoreCase))
                return equalsWeight;

            if (value.StartsWith(keyword, StringComparison.OrdinalIgnoreCase))
                return startsWithWeight;

            return value.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0 ? containsWeight : 0;
        }

        private static GObject FindByOverride(GComponent root, string overrideSpec)
        {
            if (root == null || root._disposed || string.IsNullOrWhiteSpace(overrideSpec))
                return null;

            string spec = overrideSpec.Trim();

            if (TryParsePrefixedValue(spec, "path", out string value))
                return FindByPath(root, value);

            if (TryParsePrefixedValue(spec, "idx", out value) || TryParsePrefixedValue(spec, "index", out value))
                return FindByIndexPath(root, value);

            if (TryParsePrefixedValue(spec, "name", out value))
                return FindByExact(root, value, matchName: true, matchItem: false, matchUrl: false, matchTitle: false);

            if (TryParsePrefixedValue(spec, "item", out value))
                return FindByExact(root, value, matchName: false, matchItem: true, matchUrl: false, matchTitle: false);

            if (TryParsePrefixedValue(spec, "url", out value))
                return FindByExact(root, value, matchName: false, matchItem: false, matchUrl: true, matchTitle: false);

            if (TryParsePrefixedValue(spec, "title", out value))
                return FindByExact(root, value, matchName: false, matchItem: false, matchUrl: false, matchTitle: true);

            string[] keywords = SplitKeywords(spec);
            return FindBestMatch(root, keywords, excluded: null, out _, out _);
        }

        private static bool LooksLikeExplicitSpec(string overrideSpec)
        {
            if (string.IsNullOrWhiteSpace(overrideSpec))
                return false;

            string spec = overrideSpec.Trim();

            return TryParsePrefixedValue(spec, "path", out _)
                   || TryParsePrefixedValue(spec, "idx", out _)
                   || TryParsePrefixedValue(spec, "index", out _)
                   || TryParsePrefixedValue(spec, "name", out _)
                   || TryParsePrefixedValue(spec, "item", out _)
                   || TryParsePrefixedValue(spec, "url", out _)
                   || TryParsePrefixedValue(spec, "title", out _);
        }

        private static bool TryParsePrefixedValue(string spec, string prefix, out string value)
        {
            value = null;
            if (string.IsNullOrWhiteSpace(spec) || string.IsNullOrWhiteSpace(prefix))
                return false;

            string head = prefix.Trim() + ":";
            if (!spec.StartsWith(head, StringComparison.OrdinalIgnoreCase))
                return false;

            value = spec.Substring(head.Length).Trim();
            return !string.IsNullOrWhiteSpace(value);
        }

        private static string[] SplitKeywords(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return Array.Empty<string>();

            return input.Split(new[] { '|', ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }

        private static GObject FindByPath(GComponent root, string path)
        {
            if (root == null || root._disposed || string.IsNullOrWhiteSpace(path))
                return null;

            string[] parts = path.Split(new[] { '/', '\\', '>' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length == 0)
                return null;

            GObject current = root;
            for (int i = 0; i < parts.Length; i++)
            {
                if (current is not GComponent component)
                    return null;

                current = component.GetChild(parts[i]);
                if (current == null)
                    return null;
            }

            return current;
        }

        private static GObject FindByIndexPath(GComponent root, string path)
        {
            if (root == null || root._disposed || string.IsNullOrWhiteSpace(path))
                return null;

            string[] parts = path.Split(new[] { '/', '\\', '>' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length == 0)
                return null;

            GObject current = root;
            for (int i = 0; i < parts.Length; i++)
            {
                if (current is not GComponent component)
                    return null;

                if (!int.TryParse(parts[i], out int index))
                    return null;

                if (index < 0 || index >= component.numChildren)
                    return null;

                current = component.GetChildAt(index);
                if (current == null)
                    return null;
            }

            return current;
        }

        private static GObject FindByExact(GComponent root, string value, bool matchName, bool matchItem, bool matchUrl, bool matchTitle)
        {
            if (root == null || root._disposed || string.IsNullOrWhiteSpace(value))
                return null;

            string expected = value.Trim();
            if (string.IsNullOrWhiteSpace(expected))
                return null;

            GObject best = null;
            int bestScore = int.MinValue;

            foreach (GObject obj in Enumerate(root))
            {
                if (obj == null || obj._disposed)
                    continue;

                if (ReferenceEquals(obj, root))
                    continue;

                int score = 0;
                bool matched = false;

                if (matchName && string.Equals(obj.name, expected, StringComparison.OrdinalIgnoreCase))
                {
                    matched = true;
                    score += 120;
                }

                if (matchItem && string.Equals(obj.packageItem?.name, expected, StringComparison.OrdinalIgnoreCase))
                {
                    matched = true;
                    score += 110;
                }

                if (matchUrl && string.Equals(obj.resourceURL, expected, StringComparison.OrdinalIgnoreCase))
                {
                    matched = true;
                    score += 100;
                }

                if (matchTitle && obj is GButton button && string.Equals(button.title, expected, StringComparison.OrdinalIgnoreCase))
                {
                    matched = true;
                    score += 105;
                }

                if (!matched)
                    continue;

                if (obj is GButton)
                    score += 40;
                else if (obj is GComponent)
                    score += 20;

                try
                {
                    if (obj.touchable)
                        score += 15;
                    if (obj.enabled)
                        score += 12;
                    if (obj.visible)
                        score += 10;
                    if (obj.inContainer)
                        score += 8;

                    float area = Math.Max(0F, obj.width) * Math.Max(0F, obj.height);
                    score += Math.Min(18, (int)Math.Round(area / 900F));
                }
                catch
                {
                }

                if (obj.packageItem?.exported == true)
                    score += 3;

                if (score <= bestScore)
                    continue;

                best = obj;
                bestScore = score;
            }

            return best;
        }

        private static string Describe(GComponent root, GObject obj)
        {
            if (obj == null)
                return "(null)";

            string name = obj.name;
            string item = obj.packageItem?.name;
            string title = (obj as GButton)?.title;
            if (!string.IsNullOrWhiteSpace(title))
                title = title.Replace("\r", string.Empty).Replace("\n", "\\n");

            string url = obj.resourceURL;
            string exported = obj.packageItem?.exported == true ? "exported" : "internal";
            string idxPath = GetIndexPath(root, obj);
            string namePath = GetNamePath(root, obj);
            string rect = "(unknown)";
            try
            {
                var global = obj.LocalToGlobal(new System.Drawing.RectangleF(0, 0, obj.width, obj.height));
                rect = $"{global.X:0.##},{global.Y:0.##},{global.Width:0.##},{global.Height:0.##}";
            }
            catch
            {
            }

            return "idx=" + idxPath +
                   " path=" + namePath +
                   " name=" + (name ?? "(null)") +
                   " item=" + (item ?? "(null)") +
                   " title=" + (title ?? "(null)") +
                   " url=" + (url ?? "(null)") +
                   " rect=" + rect +
                   " " + exported +
                   " touchable=" + obj.touchable +
                   " visible=" + obj.visible;
        }

        private static string GetNamePath(GComponent root, GObject obj)
        {
            if (root == null || obj == null)
                return "(null)";

            var parts = new Stack<string>();
            GObject current = obj;

            while (current != null && !ReferenceEquals(current, root))
            {
                string segment = current.name ?? current.packageItem?.name ?? current.GetType().Name;
                parts.Push(string.IsNullOrWhiteSpace(segment) ? "(null)" : segment);
                current = current.parent;
            }

            return parts.Count > 0 ? string.Join("/", parts) : string.Empty;
        }

        private static string GetIndexPath(GComponent root, GObject obj)
        {
            if (root == null || obj == null)
                return "(null)";

            var parts = new Stack<string>();
            GObject current = obj;

            while (current != null && !ReferenceEquals(current, root))
            {
                GComponent parent = current.parent;
                if (parent == null)
                    break;

                int index = parent.GetChildIndex(current);
                if (index < 0)
                    break;

                parts.Push(index.ToString());
                current = parent;
            }

            return parts.Count > 0 ? string.Join("/", parts) : string.Empty;
        }
    }
}
