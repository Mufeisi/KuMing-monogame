using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using C = ClientPackets;
using FairyGUI;
using Microsoft.Xna.Framework;
using MonoShare.MirScenes;

namespace MonoShare
{
    internal static partial class FairyGuiHost
    {
        private const string MobileNpcNameConfigKey = "MobileNpc.Name";
        private const string MobileNpcContentConfigKey = "MobileNpc.Content";
        private const string MobileNpcOptionListConfigKey = "MobileNpc.OptionList";

        private const string MobileNpcFallbackRootName = "__codex_mobile_npc_fallback_root";
        private const string MobileNpcFallbackDimName = "__codex_mobile_npc_fallback_dim";
        private const string MobileNpcFallbackPanelName = "__codex_mobile_npc_fallback_panel";
        private const string MobileNpcFallbackPanelBgName = "__codex_mobile_npc_fallback_panel_bg";
        private const string MobileNpcFallbackNameFieldName = "__codex_mobile_npc_fallback_name";
        private const string MobileNpcFallbackContentClipName = "__codex_mobile_npc_fallback_content_clip";
        private const string MobileNpcFallbackContentFieldName = "__codex_mobile_npc_fallback_content";
        private const string MobileNpcFallbackOptionsRootName = "__codex_mobile_npc_fallback_options";
        private const string MobileNpcFallbackCloseName = "CloseBtn";

        private static readonly string[] DefaultNpcNameKeywords = { "npc", "name", "title", "对话", "NPC", "名字", "名称" };
        private static readonly string[] DefaultNpcContentKeywords = { "content", "text", "dialog", "对话", "内容", "说明", "msg", "message" };
        private static readonly string[] DefaultNpcOptionListKeywords = { "option", "options", "btn", "button", "list", "grid", "选项", "按钮", "列表" };
        private static readonly string[] DefaultNpcOptionTextKeywords = { "text", "title", "name", "option", "选项", "按钮", "内容" };

        private static readonly Regex NpcInlineActionRegex = new Regex(@"<((.*?)\/(\@.*?))>", RegexOptions.Compiled);
        private static readonly Regex NpcColorRegex = new Regex(@"{((.*?)\/(.*?))}", RegexOptions.Compiled);
        private static readonly Regex NpcLinkRegex = new Regex(@"\(((.*?)\/(.*?))\)", RegexOptions.Compiled);
        private static readonly Regex NpcBigButtonRegex = new Regex(@"<<((.*?)\/(\@.*?))>>", RegexOptions.Compiled);
        private static readonly Regex NpcHtmlAnchorRegex = new Regex(@"<a\s+[^>]*?href\s*=\s*['""]?([^'""\s>]+)['""]?[^>]*>(.*?)</a>", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex NpcHtmlTagRegex = new Regex(@"<.*?>", RegexOptions.Compiled);

        private sealed class MobileNpcOptionEntry
        {
            public string Text;
            public string Action;
            public string Link;
        }

        private sealed class MobileNpcOptionItemView
        {
            public int Index;
            public GComponent Root;
            public GTextField Label;
            public EventCallback1 ClickCallback;
        }

        private sealed class MobileNpcWindowBinding
        {
            public string WindowKey;
            public GComponent Window;
            public string ResolveInfo;

            public GComponent FallbackRoot;
            public GComponent FallbackPanel;
            public GComponent FallbackOptionsRoot;
            public GComponent FallbackContentClip;
            public GGraph FallbackDim;
            public GGraph FallbackPanelBg;
            public GObject FallbackCloseButton;

            public GTextField Name;
            public string NameResolveInfo;
            public string NameOverrideSpec;
            public string[] NameOverrideKeywords;

            public GTextField Content;
            public string ContentResolveInfo;
            public string ContentOverrideSpec;
            public string[] ContentOverrideKeywords;

            public GList OptionList;
            public string OptionListResolveInfo;
            public string OptionListOverrideSpec;
            public string[] OptionListOverrideKeywords;
            public ListItemRenderer OptionItemRenderer;
        }

        private static MobileNpcWindowBinding _mobileNpcBinding;
        private static DateTime _nextMobileNpcBindAttemptUtc = DateTime.MinValue;
        private static bool _mobileNpcBindingsDumped;
        private static bool _mobileNpcDirty;
        private static DateTime _mobileNpcForceRefreshUntilUtc = DateTime.MinValue;

        private static uint _mobileNpcObjectId;
        private static string _mobileNpcName = string.Empty;
        private static string _mobileNpcContentText = string.Empty;
        private static readonly List<MobileNpcOptionEntry> MobileNpcOptions = new List<MobileNpcOptionEntry>(64);

        private static bool TryOpenExternalUrl(string url)
        {
            url = (url ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(url))
                return false;

            try
            {
#if REAL_ANDROID && ANDROID
                Android.Net.Uri uri = Android.Net.Uri.Parse(url);
                var intent = new Android.Content.Intent(Android.Content.Intent.ActionView, uri);
                intent.AddFlags(Android.Content.ActivityFlags.NewTask);
                Android.App.Application.Context.StartActivity(intent);
                return true;
#elif REAL_IOS && IOS
                var nsUrl = new Foundation.NSUrl(url);
                return UIKit.UIApplication.SharedApplication.OpenUrl(nsUrl);
#else
                Process.Start(new ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true,
                });
                return true;
#endif
            }
            catch
            {
                return false;
            }
        }

        private static bool TryCreateMobileNpcFallbackWindow(out GComponent component, out string resolveInfo)
        {
            component = null;
            resolveInfo = null;

            try
            {
                component = new GComponent
                {
                    name = "__codex_mobile_npc_window",
                    touchable = true,
                };

                try
                {
                    component.SetSize(GRoot.inst.width, GRoot.inst.height);
                }
                catch
                {
                }

                resolveInfo = "fallback";
                return true;
            }
            catch
            {
                component = null;
                resolveInfo = null;
                return false;
            }
        }

        public static void UpdateMobileNpcPage(uint npcObjectId, string npcName, IList<string> pageLines)
        {
            _mobileNpcObjectId = npcObjectId;
            _mobileNpcName = npcName ?? string.Empty;

            ParseNpcPage(pageLines, out string contentText, out List<MobileNpcOptionEntry> options);

            _mobileNpcContentText = contentText ?? string.Empty;

            try
            {
                MobileNpcOptions.Clear();
                if (options != null && options.Count > 0)
                    MobileNpcOptions.AddRange(options);
            }
            catch
            {
                MobileNpcOptions.Clear();
            }

            try
            {
                CMain.SaveLog($"MobileNpc: page npcId={npcObjectId} name={_mobileNpcName} lines={pageLines?.Count ?? 0} options={MobileNpcOptions.Count} contentLen={_mobileNpcContentText?.Length ?? 0}");
            }
            catch
            {
            }

            MarkMobileNpcDirty();
        }

        public static void MarkMobileNpcDirty()
        {
            try
            {
                _mobileNpcDirty = true;
                _mobileNpcForceRefreshUntilUtc = DateTime.UtcNow.AddMilliseconds(800);
            }
            catch
            {
            }

            TryRefreshMobileNpcIfDue(force: false);
        }

        private static void ResetMobileNpcBindings()
        {
            try
            {
                MobileNpcWindowBinding binding = _mobileNpcBinding;
                if (binding != null)
                {
                    try
                    {
                        if (binding.OptionList != null && !binding.OptionList._disposed && binding.OptionItemRenderer != null)
                            binding.OptionList.itemRenderer = null;
                    }
                    catch
                    {
                    }
                }
            }
            catch
            {
            }

            _mobileNpcBinding = null;
            _nextMobileNpcBindAttemptUtc = DateTime.MinValue;
            _mobileNpcBindingsDumped = false;
            _mobileNpcDirty = false;
            _mobileNpcForceRefreshUntilUtc = DateTime.MinValue;
        }

        private static void OnMobileNpcOptionClicked(MobileNpcOptionItemView view)
        {
            if (view == null)
                return;

            int index = view.Index;
            if (index < 0 || index >= MobileNpcOptions.Count)
                return;

            MobileNpcOptionEntry option = MobileNpcOptions[index];
            if (option == null)
                return;

            string action = option.Action ?? string.Empty;
            string link = option.Link ?? string.Empty;

            if (!string.IsNullOrWhiteSpace(link))
            {
                try
                {
                    CMain.SaveLog($"MobileNpc: option external link={link}");
                    if (link.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || link.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                    {
                        TryOpenExternalUrl(link);
                    }
                }
                catch
                {
                }

                return;
            }

            if (string.Equals(action, "@Exit", StringComparison.OrdinalIgnoreCase))
            {
                TryHideMobileWindow("Npc");
                return;
            }

            if (string.IsNullOrWhiteSpace(action))
                return;

            uint objectId = _mobileNpcObjectId != 0 ? _mobileNpcObjectId : GameScene.NPCID;
            try
            {
                CMain.SaveLog($"MobileNpc: option-click action={action} objectId={objectId} sceneNpcId={GameScene.NPCID}");
            }
            catch
            {
            }

            if (CMain.Time <= GameScene.NPCTime)
            {
                CMain.SaveLog($"MobileNpc: option-click blocked npctime={GameScene.NPCTime} now={CMain.Time}");
                return;
            }

            GameScene.NPCTime = CMain.Time + 5000;

            if (objectId == 0)
            {
                CMain.SaveLog("MobileNpc: option-click aborted objectId=0");
                return;
            }

            try
            {
                CMain.SaveLog($"MobileNpc: send CallNPC key=[{action}] objectId={objectId}");
                MonoShare.MirNetwork.Network.Enqueue(new C.CallNPC
                {
                    ObjectID = objectId,
                    Key = "[" + action + "]",
                });
            }
            catch (Exception ex)
            {
                CMain.SaveError("FairyGUI: CallNPC 发送失败：" + ex.Message);
            }
        }

        private static void OnMobileNpcContentLinkClicked(EventContext context)
        {
            // 某些 UI 包会在窗口背景/面板上绑定“点击关闭”，导致点击富文本链接时事件冒泡把 NPC 窗口关掉。
            // 这里优先阻止冒泡，保证链接点击能正常触发跳页而不是关闭窗口。
            try
            {
                context?.StopPropagation();
                context?.PreventDefault();
            }
            catch
            {
            }

            string rawHref = string.Empty;
            try { rawHref = context?.data?.ToString() ?? string.Empty; } catch { rawHref = string.Empty; }
            string href = rawHref;

            href = (href ?? string.Empty).Trim();
            try { href = WebUtility.HtmlDecode(href); } catch { }
            if (href.StartsWith("event:", StringComparison.OrdinalIgnoreCase))
                href = href.Substring("event:".Length).Trim();
            if (href.StartsWith("url:", StringComparison.OrdinalIgnoreCase))
                href = href.Substring("url:".Length).Trim();
            if (string.IsNullOrWhiteSpace(href))
            {
                CMain.SaveLog("MobileNpc: content-link ignored empty href");
                return;
            }

            // 兼容：有些富文本会传回 [@XXX]（已带外层 []），避免发出 "[[@XXX]]"。
            if (href.StartsWith("[", StringComparison.Ordinal) && href.EndsWith("]", StringComparison.Ordinal) && href.Length >= 3)
                href = href.Substring(1, href.Length - 2).Trim();

            uint objectId = _mobileNpcObjectId != 0 ? _mobileNpcObjectId : GameScene.NPCID;
            try
            {
                CMain.SaveLog($"MobileNpc: content-link raw={rawHref} href={href} objectId={objectId} sceneNpcId={GameScene.NPCID}");
            }
            catch
            {
            }

            if (href.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || href.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    TryOpenExternalUrl(href);
                }
                catch
                {
                }

                return;
            }

            if (string.Equals(href, "@Exit", StringComparison.OrdinalIgnoreCase))
            {
                TryHideMobileWindow("Npc");
                return;
            }

            if (CMain.Time <= GameScene.NPCTime)
            {
                CMain.SaveLog($"MobileNpc: content-link blocked npctime={GameScene.NPCTime} now={CMain.Time}");
                return;
            }

            GameScene.NPCTime = CMain.Time + 5000;

            if (objectId == 0)
            {
                CMain.SaveLog("MobileNpc: content-link aborted objectId=0");
                return;
            }

            try
            {
                CMain.SaveLog($"MobileNpc: send CallNPC key=[{href}] objectId={objectId}");
                MonoShare.MirNetwork.Network.Enqueue(new C.CallNPC
                {
                    ObjectID = objectId,
                    Key = "[" + href + "]",
                });
            }
            catch (Exception ex)
            {
                CMain.SaveError("FairyGUI: CallNPC 发送失败：" + ex.Message);
            }
        }

        private static MobileNpcOptionItemView GetOrCreateMobileNpcOptionItemView(GComponent itemRoot)
        {
            if (itemRoot == null || itemRoot._disposed)
                return null;

            if (itemRoot.data is MobileNpcOptionItemView existing && existing.Root != null && !existing.Root._disposed)
                return existing;

            var view = new MobileNpcOptionItemView
            {
                Root = itemRoot,
                Index = -1,
            };

            try
            {
                List<(int Score, GObject Target)> candidates =
                    CollectMobileChatCandidates(itemRoot, obj => obj is GTextField && obj is not GTextInput, DefaultNpcOptionTextKeywords, ScoreMobileShopTextCandidate);
                view.Label = SelectMobileChatCandidate<GTextField>(candidates, minScore: 15);
            }
            catch
            {
            }

            try
            {
                if (view.ClickCallback == null)
                {
                    view.ClickCallback = context =>
                    {
                        // 某些 publish 包会在窗口背景/面板上绑定“点击关闭”，这里阻止冒泡，避免点选项变成关窗口。
                        try { context?.StopPropagation(); } catch { }
                        try { context?.PreventDefault(); } catch { }

                        OnMobileNpcOptionClicked(view);
                    };

                    // publish 里选项项可能默认不可点（touchable=false / grayed=true / enabled=false）
                    try
                    {
                        itemRoot.touchable = true;
                        if (itemRoot is GButton button)
                        {
                            button.enabled = true;
                            button.grayed = false;
                            button.changeStateOnClick = false;
                        }
                    }
                    catch
                    {
                    }

                    try { EnsureMobileInteractiveChain(itemRoot); } catch { }
                    try { EnsureMobileInteractiveChain(itemRoot); } catch { }
                    itemRoot.onClick.Add(view.ClickCallback);
                }
            }
            catch
            {
            }

            try
            {
                itemRoot.data = view;
            }
            catch
            {
            }

            return view;
        }

        private static void RenderMobileNpcOptionListItem(int index, GObject itemObject)
        {
            if (itemObject == null || itemObject._disposed)
                return;

            if (itemObject is not GComponent itemRoot || itemRoot._disposed)
                return;

            MobileNpcOptionItemView view = GetOrCreateMobileNpcOptionItemView(itemRoot);
            if (view == null)
                return;

            view.Index = index;

            MobileNpcOptionEntry option = index >= 0 && index < MobileNpcOptions.Count ? MobileNpcOptions[index] : null;
            string text = option?.Text ?? string.Empty;

            try
            {
                if (itemObject is GButton button && !button._disposed)
                    button.title = text;
            }
            catch
            {
            }

            try
            {
                if (view.Label != null && !view.Label._disposed)
                {
                    view.Label.text = text;

                    // 以“黄字下划线”显示（与 PC 端 NPC 黄字链接风格接近）
                    try
                    {
                        TextFormat tf = view.Label.textFormat;
                        tf.color = new Color(255, 210, 0, 255);
                        tf.underline = true;
                        // FairyGUI TextFormat.size 存在上限（日志里出现过 “240 cannot be greater than 192”），这里做一次保护性夹断
                        tf.size = Math.Clamp(Math.Max(tf.size, 22), 1, 192);
                        view.Label.textFormat = tf;
                        if (view.Label.stroke < 1)
                            view.Label.stroke = 1;
                        view.Label.strokeColor = new Color(0, 0, 0, 200);
                    }
                    catch
                    {
                    }
                }
            }
            catch
            {
            }
        }

        private static MobileNpcOptionItemView GetOrCreateMobileNpcFallbackOptionView(GComponent itemRoot)
        {
            if (itemRoot == null || itemRoot._disposed)
                return null;

            if (itemRoot.data is MobileNpcOptionItemView existing && existing.Root != null && !existing.Root._disposed)
                return existing;

            var view = new MobileNpcOptionItemView
            {
                Root = itemRoot,
                Index = -1,
            };

            try
            {
                view.Label = itemRoot.GetChild("title") as GTextField;
            }
            catch
            {
                view.Label = null;
            }

            if (view.Label == null || view.Label._disposed)
            {
                try
                {
                    view.Label = new GTextField
                    {
                        name = "title",
                        touchable = false,
                        text = string.Empty,
                        align = AlignType.Center,
                        verticalAlign = VertAlignType.Middle,
                        autoSize = AutoSizeType.None,
                    };

                    try
                    {
                        view.Label.textFormat.size = 20;
                        view.Label.textFormat.color = new Color(255, 210, 0, 255);
                        view.Label.textFormat.bold = true;
                        view.Label.textFormat.underline = true;
                        view.Label.stroke = 1;
                        view.Label.strokeColor = new Color(0, 0, 0, 200);
                    }
                    catch
                    {
                    }

                    itemRoot.AddChild(view.Label);
                }
                catch
                {
                    view.Label = null;
                }
            }

            try
            {
                if (view.ClickCallback == null)
                {
                    view.ClickCallback = context =>
                    {
                        try { context?.StopPropagation(); } catch { }
                        try { context?.PreventDefault(); } catch { }

                        OnMobileNpcOptionClicked(view);
                    };

                    try
                    {
                        itemRoot.touchable = true;
                        if (itemRoot is GButton button)
                        {
                            button.enabled = true;
                            button.grayed = false;
                            button.changeStateOnClick = false;
                        }
                    }
                    catch
                    {
                    }

                    itemRoot.onClick.Add(view.ClickCallback);
                }
            }
            catch
            {
            }

            try
            {
                itemRoot.data = view;
            }
            catch
            {
            }

            return view;
        }

        private static bool TryEnsureMobileNpcFallbackLayout(MobileNpcWindowBinding binding)
        {
            if (binding == null || binding.Window == null || binding.Window._disposed)
                return false;

            GComponent window = binding.Window;

            // Root
            GComponent root = binding.FallbackRoot;
            if (root == null || root._disposed || !ReferenceEquals(root.parent, window))
            {
                root = null;
                try { root = window.GetChild(MobileNpcFallbackRootName) as GComponent; } catch { root = null; }

                if (root == null || root._disposed)
                {
                    root = new GComponent { name = MobileNpcFallbackRootName, touchable = true };
                    try { window.AddChild(root); } catch { }
                }

                binding.FallbackRoot = root;
                try { root.AddRelation(window, RelationType.Size); } catch { }
                try { root.SetSize(window.width, window.height); } catch { }
                try { window.SetChildIndex(root, window.numChildren - 1); } catch { }
            }

            // Dim background
            GGraph dim = binding.FallbackDim;
            if (dim == null || dim._disposed || !ReferenceEquals(dim.parent, root))
            {
                dim = null;
                try { dim = root.GetChild(MobileNpcFallbackDimName) as GGraph; } catch { dim = null; }

                if (dim == null || dim._disposed)
                {
                    dim = new GGraph { name = MobileNpcFallbackDimName, touchable = false };
                    try { dim.DrawRect(root.width, root.height, 0, new Color(0, 0, 0, 0), new Color(0, 0, 0, 0)); } catch { }
                    try { root.AddChild(dim); } catch { }
                }

                binding.FallbackDim = dim;
                try { dim.AddRelation(root, RelationType.Size); } catch { }
                try { dim.SetSize(root.width, root.height); } catch { }

                try
                {
                    // 仅允许右上角关闭按钮关闭：遮罩点击不关闭弹框
                    dim.onClick.Clear();
                }
                catch
                {
                }
            }

            // 去掉遮罩层：不绘制、不拦截触摸
            try
            {
                if (dim != null && !dim._disposed)
                {
                    dim.touchable = false;
                    dim.visible = false;
                    dim.alpha = 0f;
                }
            }
            catch
            {
            }

            // Panel
            GComponent panel = binding.FallbackPanel;
            if (panel == null || panel._disposed || !ReferenceEquals(panel.parent, root))
            {
                panel = null;
                try { panel = root.GetChild(MobileNpcFallbackPanelName) as GComponent; } catch { panel = null; }

                if (panel == null || panel._disposed)
                {
                    panel = new GComponent { name = MobileNpcFallbackPanelName, touchable = true };
                    try { root.AddChild(panel); } catch { }
                }

                binding.FallbackPanel = panel;
            }

            // Panel background
            GGraph panelBg = binding.FallbackPanelBg;
            if (panelBg == null || panelBg._disposed || !ReferenceEquals(panelBg.parent, panel))
            {
                panelBg = null;
                try { panelBg = panel.GetChild(MobileNpcFallbackPanelBgName) as GGraph; } catch { panelBg = null; }

                if (panelBg == null || panelBg._disposed)
                {
                    panelBg = new GGraph { name = MobileNpcFallbackPanelBgName, touchable = false };
                    try { panel.AddChild(panelBg); } catch { }
                }

                binding.FallbackPanelBg = panelBg;
            }

            // Close button
            GObject close = binding.FallbackCloseButton;
            if (close == null || close._disposed || !ReferenceEquals(close.parent, panel))
            {
                close = null;
                try { close = panel.GetChild(MobileNpcFallbackCloseName); } catch { close = null; }

                if (close == null || close._disposed)
                {
                    // 注意：GComponent 默认 opaque=false，且其子节点（bg/label）为 touchable=false 时会导致无法命中点击。
                    // 这里显式设置 opaque=true，确保右上角关闭按钮可点击。
                    var closeRoot = new GComponent { name = MobileNpcFallbackCloseName, touchable = true, opaque = true };

                    try
                    {
                        var closeBg = new GGraph { name = "bg", touchable = false };
                        closeBg.DrawRoundRect(34, 34, new Color(255, 255, 255, 25), new[] { 6F, 6F, 6F, 6F });
                        closeRoot.AddChild(closeBg);
                    }
                    catch
                    {
                    }

                    try
                    {
                        var closeText = new GTextField
                        {
                            name = "label",
                            touchable = false,
                            text = "X",
                            align = AlignType.Center,
                            verticalAlign = VertAlignType.Middle,
                            autoSize = AutoSizeType.None,
                        };
                        closeText.SetSize(34, 34);
                        closeText.SetPosition(0, 0);

                        try
                        {
                            closeText.textFormat.size = 22;
                            closeText.textFormat.color = Color.White;
                            closeText.textFormat.bold = true;
                            closeText.stroke = 1;
                            closeText.strokeColor = new Color(0, 0, 0, 200);
                        }
                        catch
                        {
                        }

                        closeRoot.AddChild(closeText);
                    }
                    catch
                    {
                    }

                    try { panel.AddChild(closeRoot); } catch { }
                    close = closeRoot;
                }

                binding.FallbackCloseButton = close;

                try
                {
                    if (!ReferenceEquals(close.data, MobileNpcFallbackCloseName))
                    {
                        close.onClick.Add(() =>
                        {
                            try { TryHideMobileWindow("Npc"); } catch { }
                        });
                        close.data = MobileNpcFallbackCloseName;
                    }
                }
                catch
                {
                }
            }

            // Name field
            try
            {
                if (binding.Name == null || binding.Name._disposed || !ReferenceEquals(binding.Name.parent, panel))
                {
                    binding.Name = panel.GetChild(MobileNpcFallbackNameFieldName) as GTextField;
                    if (binding.Name == null || binding.Name._disposed)
                    {
                        binding.Name = new GTextField
                        {
                            name = MobileNpcFallbackNameFieldName,
                            touchable = false,
                            text = string.Empty,
                            align = AlignType.Left,
                            verticalAlign = VertAlignType.Middle,
                            autoSize = AutoSizeType.None,
                        };

                        try
                        {
                            binding.Name.textFormat.size = 22;
                            binding.Name.textFormat.color = Color.White;
                            binding.Name.textFormat.bold = true;
                            binding.Name.stroke = 1;
                            binding.Name.strokeColor = new Color(0, 0, 0, 200);
                        }
                        catch
                        {
                        }

                        panel.AddChild(binding.Name);
                    }
                }
            }
            catch
            {
            }

            // Content field
            try
            {
                if (binding.Content == null || binding.Content._disposed)
                {
                    binding.Content = panel.GetChild(MobileNpcFallbackContentFieldName) as GTextField;
                    if (binding.Content == null || binding.Content._disposed)
                    {
                        binding.Content = TryFindChildByNameRecursive(panel, MobileNpcFallbackContentFieldName) as GTextField;
                    }
                    if (binding.Content == null || binding.Content._disposed)
                    {
                        GRichTextField rich = new GRichTextField
                        {
                            name = MobileNpcFallbackContentFieldName,
                            touchable = true,
                            text = string.Empty,
                            align = AlignType.Left,
                            verticalAlign = VertAlignType.Top,
                        };

                        try
                        {
                            rich.textFormat.size = 20;
                            rich.textFormat.color = Color.White;
                            rich.stroke = 1;
                            rich.strokeColor = new Color(0, 0, 0, 200);
                            rich.autoSize = AutoSizeType.Height;
                            rich.singleLine = false;
                            rich.maxWidth = 0;
                            rich.UBBEnabled = true;
                        }
                        catch
                        {
                        }

                        panel.AddChild(rich);
                        binding.Content = rich;
                    }
                }
            }
            catch
            {
            }

            // Content clip（用于裁剪正文，防止文字溢出面板；也避免超长对话把文字绘制到面板外）
            try
            {
                GComponent clip = binding.FallbackContentClip;
                if (clip == null || clip._disposed || !ReferenceEquals(clip.parent, panel))
                {
                    clip = null;
                    try { clip = panel.GetChild(MobileNpcFallbackContentClipName) as GComponent; } catch { clip = null; }
                    if (clip == null || clip._disposed)
                    {
                        clip = new GComponent
                        {
                            name = MobileNpcFallbackContentClipName,
                            touchable = true,
                            opaque = false,
                        };
                        try { panel.AddChild(clip); } catch { }
                    }

                    binding.FallbackContentClip = clip;
                }

                if (binding.Content != null && !binding.Content._disposed &&
                    clip != null && !clip._disposed &&
                    !ReferenceEquals(binding.Content.parent, clip))
                {
                    try { binding.Content.RemoveFromParent(); } catch { }
                    try { clip.AddChild(binding.Content); } catch { }
                }
            }
            catch
            {
            }

            // Options root
            GComponent options = binding.FallbackOptionsRoot;
            if (options == null || options._disposed || !ReferenceEquals(options.parent, panel))
            {
                options = null;
                try { options = panel.GetChild(MobileNpcFallbackOptionsRootName) as GComponent; } catch { options = null; }

                if (options == null || options._disposed)
                {
                    options = new GComponent { name = MobileNpcFallbackOptionsRootName, touchable = true };
                    try { panel.AddChild(options); } catch { }
                }

                binding.FallbackOptionsRoot = options;
            }

            try
            {
                if (root != null && !root._disposed)
                    window.SetChildIndex(root, window.numChildren - 1);
            }
            catch
            {
            }

            try
            {
                if (panelBg != null && !panelBg._disposed)
                    panel.SetChildIndex(panelBg, 0);
            }
            catch
            {
            }

            return true;
        }

        private static void TryRefreshMobileNpcFallbackIfDue(MobileNpcWindowBinding binding)
        {
            if (binding == null || binding.Window == null || binding.Window._disposed)
                return;

            if (!TryEnsureMobileNpcFallbackLayout(binding))
                return;

            GComponent window = binding.Window;
            GComponent root = binding.FallbackRoot;
            GComponent panel = binding.FallbackPanel;
            GComponent optionsRoot = binding.FallbackOptionsRoot;
            GGraph panelBg = binding.FallbackPanelBg;
            GObject close = binding.FallbackCloseButton;

            if (root == null || root._disposed || panel == null || panel._disposed)
                return;

            try
            {
                if (window != null && !window._disposed && root.parent == window)
                    window.SetChildIndex(root, window.numChildren - 1);
            }
            catch
            {
            }

            float screenW = Math.Max(1F, window.width);
            float screenH = Math.Max(1F, window.height);

            float panelWMax = Math.Clamp(screenW - 40f, 1f, 780f);
            float panelWMin = Math.Min(320f, panelWMax);
            float panelW = Math.Clamp(screenW * 0.86f, panelWMin, panelWMax);

            float panelHMax = Math.Max(1f, screenH - 8f);
            float panelHMin = Math.Min(260f, panelHMax);
            float panelH = Math.Clamp(screenH * 0.8f, panelHMin, panelHMax);
            float panelX = (screenW - panelW) / 2f;
            float panelY = (screenH - panelH) / 2f;

            float padding = 20f;
            float headerH = 40f;

            try
            {
                panel.SetPosition(panelX, panelY);
                panel.SetSize(panelW, panelH);
            }
            catch
            {
            }

            try
            {
                // 背景尽量不透明，避免底层 UI 的默认文字透出造成重叠观感
                panelBg?.DrawRoundRect(panelW, panelH, new Color(0, 0, 0, 230), new[] { 12F, 12F, 12F, 12F });
            }
            catch
            {
            }

            try
            {
                if (close != null && !close._disposed)
                {
                    close.SetSize(34, 34);
                    close.SetPosition(panelW - padding - 34, padding);
                }
            }
            catch
            {
            }

            try
            {
                if (binding.Name != null && !binding.Name._disposed)
                {
                    binding.Name.SetPosition(padding, padding);
                    binding.Name.SetSize(Math.Max(10f, panelW - padding * 2f - 34f - 8f), headerH);
                }
            }
            catch
            {
            }

            // 需求：底部自动生成的“选项按钮”会与正文内的黄字链接重复；移动端只保留正文黄字链接即可。
            bool renderOptionButtons = false;

            int optionCount = 0;
            try { optionCount = MobileNpcOptions.Count; } catch { optionCount = 0; }
            int renderedOptionCount = renderOptionButtons ? optionCount : 0;
            string fallbackContentText = BuildMobileNpcFallbackDisplayContentText(renderOptionButtons);

            // 根据正文文本高度自适应 NPC 面板高度（黑底背景板跟随拉伸），避免文字溢出面板。
            float contentW = Math.Max(10f, panelW - padding * 2f);
            float desiredContentTextH = 0f;
            float measuredLineHeight = 30f;
            try
            {
                if (binding.Content != null && !binding.Content._disposed)
                {
                    try { binding.Content.text = fallbackContentText ?? string.Empty; } catch { }

                    // 先设置宽度，确保 textHeight 的换行计算正确
                    try
                    {
                        if (binding.Content is GRichTextField rich && rich != null && !rich._disposed)
                        {
                            try { rich.maxWidth = Math.Max(0, (int)Math.Ceiling(contentW)); } catch { }
                        }
                    }
                    catch
                    {
                    }

                    try
                    {
                        TextFormat tf = binding.Content.textFormat;
                        measuredLineHeight = Math.Max(measuredLineHeight, tf.size + Math.Max(0f, tf.lineSpacing) + 8f);
                    }
                    catch
                    {
                    }

                    AutoSizeType oldAutoSize = AutoSizeType.None;
                    try { oldAutoSize = binding.Content.autoSize; } catch { oldAutoSize = AutoSizeType.None; }
                    try { binding.Content.autoSize = AutoSizeType.Height; } catch { }

                    binding.Content.SetSize(contentW, Math.Max(10f, binding.Content.height));

                    // FairyGUI 的文本高度计算可能是懒更新（尤其是富文本/UBB），这里尝试强制刷新一次，避免测量偏小导致面板高度不足。
                    try { binding.Content.displayObject?.EnsureSizeCorrect(); } catch { }

                    float h1 = 0f;
                    float h2 = 0f;
                    try { h1 = binding.Content.textHeight; } catch { h1 = 0f; }
                    try { h2 = binding.Content.height; } catch { h2 = 0f; }
                    desiredContentTextH = Math.Max(0f, Math.Max(h1, h2));

                    try { binding.Content.autoSize = oldAutoSize; } catch { }
                }
            }
            catch
            {
                desiredContentTextH = 0f;
            }

            try
            {
                // 富文本高度在移动端偶尔会低估，这里再用“按宽度估算换行数”的方式兜底，
                // 目标是优先拉高背景面板，而不是裁掉正文。
                if (binding.Content != null && !binding.Content._disposed)
                {
                    int estimatedLines = EstimateMobileNpcRenderedLineCount(fallbackContentText, contentW, binding.Content);
                    TextFormat tf = binding.Content.textFormat;
                    float estimatedLineHeight = Math.Max(22f, tf.size + Math.Max(0, tf.lineSpacing) + 6f);
                    measuredLineHeight = Math.Max(measuredLineHeight, estimatedLineHeight);
                    float estimatedTextH = estimatedLines * estimatedLineHeight + 8f;
                    desiredContentTextH = Math.Max(desiredContentTextH, estimatedTextH);
                }
            }
            catch
            {
            }

            // 额外留出更充足的底部缓冲，避免富文本在移动端测量偏小时正文被自身高度裁掉。
            float desiredContentH = Math.Max(96f, desiredContentTextH + Math.Max(64f, measuredLineHeight * 2.25f));
            float desiredOptionsH = renderOptionButtons
                ? Math.Min(46f * renderedOptionCount + 10f * Math.Max(0, renderedOptionCount - 1), Math.Min(260f, screenH * 0.36f))
                : 0f;
            float panelHNeeded = padding + headerH + padding + desiredContentH + (desiredOptionsH > 0f ? padding + desiredOptionsH : 0f) + padding;
            panelH = Math.Clamp(panelHNeeded, panelHMin, panelHMax);
            panelY = (screenH - panelH) / 2f;

            try
            {
                panel.SetPosition(panelX, panelY);
                panel.SetSize(panelW, panelH);
            }
            catch
            {
            }

            try
            {
                // 背景尽量不透明，避免底层 UI 的默认文字透出造成重叠观感
                panelBg?.DrawRoundRect(panelW, panelH, new Color(0, 0, 0, 230), new[] { 12F, 12F, 12F, 12F });
            }
            catch
            {
            }

            float usableH = Math.Max(0, panelH - padding * 3f - headerH);
            float maxOptionsH = usableH * 0.48f;
            float minOptionsH = renderedOptionCount > 0 ? Math.Min(usableH * 0.28f, 180f) : 0f;
            float optionsH = renderedOptionCount > 0 ? Math.Clamp(46f * renderedOptionCount, minOptionsH, maxOptionsH) : 0f;
            float contentH = Math.Max(60f, usableH - optionsH);

            try
            {
                if (binding.FallbackContentClip != null && !binding.FallbackContentClip._disposed)
                {
                    binding.FallbackContentClip.SetPosition(padding, padding + headerH + padding);
                    binding.FallbackContentClip.SetSize(contentW, contentH);

                    // 更新裁剪区域（clipRect 使用本地坐标）
                    try { binding.FallbackContentClip.rootContainer.clipRect = new System.Drawing.RectangleF(0f, 0f, contentW, contentH); } catch { }
                }

                if (binding.Content != null && !binding.Content._disposed)
                {
                    float finalContentTextH = Math.Max(contentH, desiredContentTextH + Math.Max(12f, measuredLineHeight * 0.5f));

                    try
                    {
                        binding.Content.autoSize = AutoSizeType.Height;
                    }
                    catch
                    {
                    }

                    try
                    {
                        if (binding.Content is GRichTextField rich && rich != null && !rich._disposed)
                            rich.maxWidth = Math.Max(0, (int)Math.Ceiling(contentW));
                    }
                    catch
                    {
                    }

                    binding.Content.SetPosition(0f, 0f);
                    binding.Content.SetSize(contentW, finalContentTextH);
                    try { binding.Content.displayObject?.EnsureSizeCorrect(); } catch { }
                }
            }
            catch
            {
            }

            try
            {
                if (optionsRoot != null && !optionsRoot._disposed)
                {
                    optionsRoot.SetPosition(padding, padding + headerH + padding + contentH + padding);
                    optionsRoot.SetSize(Math.Max(10f, panelW - padding * 2f), Math.Max(0f, optionsH));
                    optionsRoot.visible = renderOptionButtons;
                    optionsRoot.touchable = renderOptionButtons;
                }
            }
            catch
            {
            }

            try
            {
                for (int childIndex = 0; childIndex < window.numChildren; childIndex++)
                {
                    GObject child = window.GetChildAt(childIndex);
                    if (child == null || child._disposed || ReferenceEquals(child, root))
                        continue;

                    child.visible = false;
                    child.touchable = false;
                }

                root.visible = true;
                root.touchable = true;
                panel.visible = true;
                panel.touchable = true;
            }
            catch
            {
            }

            if (optionsRoot == null || optionsRoot._disposed)
            {
                TrySuppressMobileNpcUnderlyingDefaultText(binding);
                return;
            }

            if (!renderOptionButtons)
            {
                // 清理已生成的旧按钮，避免残留/重复
                try
                {
                    while (optionsRoot.numChildren > 0)
                    {
                        GObject child = optionsRoot.GetChildAt(optionsRoot.numChildren - 1);
                        optionsRoot.RemoveChild(child, dispose: true);
                    }
                }
                catch
                {
                }

                TrySuppressMobileNpcUnderlyingDefaultText(binding);
                return;
            }

            float optionGap = 10f;
            float optAreaH = Math.Max(0f, optionsRoot.height);
            float optH = optionCount > 0 ? (optAreaH - optionGap * Math.Max(0, optionCount - 1)) / Math.Max(1, optionCount) : 0f;
            optH = Math.Clamp(optH, 28f, 46f);

            // Ensure children count
            try
            {
                while (optionsRoot.numChildren > optionCount)
                {
                    GObject extra = optionsRoot.GetChildAt(optionsRoot.numChildren - 1);
                    optionsRoot.RemoveChild(extra, dispose: true);
                }
            }
            catch
            {
            }

            for (int i = 0; i < optionCount; i++)
            {
                MobileNpcOptionEntry option = i >= 0 && i < MobileNpcOptions.Count ? MobileNpcOptions[i] : null;
                string text = option?.Text ?? string.Empty;

                GComponent item = null;
                try
                {
                    item = optionsRoot.GetChildAt(i) as GComponent;
                }
                catch
                {
                    item = null;
                }

                if (item == null || item._disposed)
                {
                    try
                    {
                        item = new GComponent { name = "__opt_" + i, touchable = true };
                        optionsRoot.AddChild(item);

                        try
                        {
                            var bg = new GGraph { name = "bg", touchable = false };
                            bg.DrawRoundRect(10, 10, new Color(255, 255, 255, 20), new[] { 10F, 10F, 10F, 10F });
                            item.AddChild(bg);
                        }
                        catch
                        {
                        }
                    }
                    catch
                    {
                        item = null;
                    }
                }

                if (item == null || item._disposed)
                    continue;

                MobileNpcOptionItemView view = GetOrCreateMobileNpcFallbackOptionView(item);
                if (view == null)
                    continue;

                view.Index = i;

                try
                {
                    item.SetPosition(0, i * (optH + optionGap));
                    item.SetSize(Math.Max(0f, optionsRoot.width), optH);
                }
                catch
                {
                }

                try
                {
                    if (item.GetChild("bg") is GGraph bg && bg != null && !bg._disposed)
                        bg.DrawRoundRect(item.width, item.height, new Color(255, 255, 255, 20), new[] { 10F, 10F, 10F, 10F });
                }
                catch
                {
                }

            try
            {
                if (view.Label != null && !view.Label._disposed)
                {
                    view.Label.text = text;
                    view.Label.SetPosition(0, 0);
                    view.Label.SetSize(item.width, item.height);

                    try
                    {
                        TextFormat tf = view.Label.textFormat;
                        tf.color = new Color(255, 210, 0, 255);
                        tf.underline = true;
                        view.Label.textFormat = tf;
                    }
                    catch
                    {
                    }
                }
            }
            catch
            {
            }

            }

            // 避免底层窗口自带的默认文字透出/重叠（兜底布局盖在原窗口之上时尤为明显）
            TrySuppressMobileNpcUnderlyingDefaultText(binding);
        }

        private static int EstimateMobileNpcRenderedLineCount(string contentText, float contentWidth, GTextField template)
        {
            if (contentWidth <= 1f)
                return 1;

            string text = contentText ?? string.Empty;
            if (text.Length == 0)
                return 1;

            try { text = WebUtility.HtmlDecode(text); } catch { }
            text = StripMobileNpcMeasureTags(text);
            if (string.IsNullOrWhiteSpace(text))
                return 1;

            float fontSize = 22f;
            float lineSpacing = 0f;
            try
            {
                if (template != null && !template._disposed)
                {
                    TextFormat tf = template.textFormat;
                    if (tf.size > 0)
                        fontSize = tf.size;
                    lineSpacing = Math.Max(0f, tf.lineSpacing);
                }
            }
            catch
            {
                fontSize = 22f;
                lineSpacing = 0f;
            }

            fontSize = Math.Max(12f, fontSize);

            float asciiWidth = Math.Max(6f, fontSize * 0.56f);
            float upperWidth = Math.Max(asciiWidth, fontSize * 0.62f);
            float digitWidth = Math.Max(6f, fontSize * 0.52f);
            float spaceWidth = Math.Max(4f, fontSize * 0.34f);
            float punctWidth = Math.Max(5f, fontSize * 0.42f);
            float cjkWidth = Math.Max(asciiWidth + 2f, fontSize * 0.96f);
            float extraWrapPadding = Math.Max(2f, lineSpacing * 0.15f);

            int totalLines = 0;
            string[] logicalLines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            for (int i = 0; i < logicalLines.Length; i++)
            {
                string logicalLine = logicalLines[i] ?? string.Empty;
                if (logicalLine.Length == 0)
                {
                    totalLines++;
                    continue;
                }

                float currentWidth = 0f;
                int wrappedLines = 1;

                for (int j = 0; j < logicalLine.Length; j++)
                {
                    char ch = logicalLine[j];
                    float charWidth = EstimateMobileNpcMeasureCharWidth(ch, asciiWidth, upperWidth, digitWidth, spaceWidth, punctWidth, cjkWidth);

                    if (currentWidth > 0f && currentWidth + charWidth + extraWrapPadding > contentWidth)
                    {
                        wrappedLines++;
                        currentWidth = 0f;

                        if (char.IsWhiteSpace(ch))
                            continue;
                    }

                    currentWidth += charWidth;
                }

                totalLines += Math.Max(1, wrappedLines);
            }

            return Math.Max(1, totalLines);
        }

        private static string StripMobileNpcMeasureTags(string text)
        {
            if (string.IsNullOrEmpty(text))
                return string.Empty;

            var builder = new StringBuilder(text.Length);
            int length = text.Length;

            for (int i = 0; i < length; i++)
            {
                char ch = text[i];

                if (ch == '[')
                {
                    int close = text.IndexOf(']', i + 1);
                    if (close > i)
                    {
                        string tag = text.Substring(i + 1, close - i - 1);
                        if (!string.IsNullOrWhiteSpace(tag))
                        {
                            string trimmed = tag.Trim();
                            if (trimmed.Equals("br", StringComparison.OrdinalIgnoreCase) ||
                                trimmed.Equals("p", StringComparison.OrdinalIgnoreCase) ||
                                trimmed.Equals("/p", StringComparison.OrdinalIgnoreCase))
                            {
                                builder.Append('\n');
                            }

                            i = close;
                            continue;
                        }
                    }
                }

                if (ch == '<')
                {
                    int close = text.IndexOf('>', i + 1);
                    if (close > i)
                    {
                        string tag = text.Substring(i + 1, close - i - 1);
                        if (!string.IsNullOrWhiteSpace(tag))
                        {
                            string trimmed = tag.Trim();
                            if (trimmed.StartsWith("br", StringComparison.OrdinalIgnoreCase) ||
                                trimmed.StartsWith("p", StringComparison.OrdinalIgnoreCase) ||
                                trimmed.StartsWith("/p", StringComparison.OrdinalIgnoreCase))
                            {
                                builder.Append('\n');
                            }

                            i = close;
                            continue;
                        }
                    }
                }

                builder.Append(ch);
            }

            return builder.ToString();
        }

        private static string BuildMobileNpcFallbackDisplayContentText(bool renderOptionButtons)
        {
            string text = _mobileNpcContentText ?? string.Empty;
            if (!renderOptionButtons || string.IsNullOrWhiteSpace(text) || MobileNpcOptions.Count == 0)
                return text;

            var optionTexts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                for (int i = 0; i < MobileNpcOptions.Count; i++)
                {
                    MobileNpcOptionEntry option = MobileNpcOptions[i];
                    string optionText = StripMobileNpcMeasureTags(option?.Text ?? string.Empty).Trim();
                    if (!string.IsNullOrWhiteSpace(optionText))
                        optionTexts.Add(optionText);
                }
            }
            catch
            {
            }

            if (optionTexts.Count == 0)
                return text;

            string[] lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            var kept = new List<string>(lines.Length);

            for (int i = 0; i < lines.Length; i++)
            {
                string rawLine = lines[i] ?? string.Empty;
                string visibleLine = StripMobileNpcMeasureTags(rawLine).Trim();

                bool looksLikeOptionLink =
                    rawLine.IndexOf("[url=", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    rawLine.IndexOf("<a ", StringComparison.OrdinalIgnoreCase) >= 0;

                if (looksLikeOptionLink && !string.IsNullOrWhiteSpace(visibleLine) && optionTexts.Contains(visibleLine))
                    continue;

                kept.Add(rawLine.TrimEnd());
            }

            return string.Join("\n", kept);
        }

        private static float EstimateMobileNpcMeasureCharWidth(char ch, float asciiWidth, float upperWidth, float digitWidth, float spaceWidth, float punctWidth, float cjkWidth)
        {
            if (ch == '\t')
                return spaceWidth * 2f;

            if (char.IsWhiteSpace(ch))
                return spaceWidth;

            if ((ch >= 0x4E00 && ch <= 0x9FFF) ||
                (ch >= 0x3400 && ch <= 0x4DBF) ||
                (ch >= 0xF900 && ch <= 0xFAFF))
                return cjkWidth;

            if ((ch >= 0x3040 && ch <= 0x30FF) ||
                (ch >= 0xAC00 && ch <= 0xD7AF))
                return cjkWidth * 0.94f;

            if (char.IsDigit(ch))
                return digitWidth;

            if (ch >= 'A' && ch <= 'Z')
                return upperWidth;

            if ((ch >= 'a' && ch <= 'z') || ch == '_' || ch == '-')
                return asciiWidth;

            return punctWidth;
        }

        private static void TrySuppressMobileNpcUnderlyingDefaultText(MobileNpcWindowBinding binding)
        {
            if (binding == null)
                return;

            GComponent window = binding.Window;
            GComponent fallbackRoot = binding.FallbackRoot;
            GComponent panel = binding.FallbackPanel;

            if (window == null || window._disposed || fallbackRoot == null || fallbackRoot._disposed || panel == null || panel._disposed)
                return;

            System.Drawing.RectangleF panelRect;
            try
            {
                panelRect = panel.LocalToGlobal(new System.Drawing.RectangleF(0, 0, panel.width, panel.height));
            }
            catch
            {
                return;
            }

            foreach (GObject obj in Enumerate(window))
            {
                if (obj == null || obj._disposed)
                    continue;

                if (ReferenceEquals(obj, fallbackRoot) || IsMobileNpcDescendantOf(obj, fallbackRoot))
                    continue;

                if (obj is not GTextField tf || tf is GTextInput)
                    continue;

                string text = string.Empty;
                try { text = tf.text ?? string.Empty; } catch { text = string.Empty; }

                if (string.IsNullOrWhiteSpace(text))
                    continue;

                System.Drawing.RectangleF rect;
                try
                {
                    rect = tf.LocalToGlobal(new System.Drawing.RectangleF(0, 0, tf.width, tf.height));
                }
                catch
                {
                    continue;
                }

                if (!IsMobileNpcRectOverlapSignificant(panelRect, rect, threshold: 0.55f))
                    continue;

                try { tf.text = string.Empty; } catch { }
                try { tf.visible = false; } catch { }
            }
        }

        private static bool IsMobileNpcDescendantOf(GObject obj, GComponent ancestor)
        {
            if (obj == null || obj._disposed || ancestor == null || ancestor._disposed)
                return false;

            try
            {
                GComponent parent = obj.parent;
                while (parent != null && !parent._disposed)
                {
                    if (ReferenceEquals(parent, ancestor))
                        return true;

                    parent = parent.parent;
                }
            }
            catch
            {
            }

            return false;
        }

        private static bool IsMobileNpcRectOverlapSignificant(System.Drawing.RectangleF a, System.Drawing.RectangleF b, float threshold)
        {
            if (threshold <= 0f)
                threshold = 0.5f;

            if (a.Width <= 0 || a.Height <= 0 || b.Width <= 0 || b.Height <= 0)
                return false;

            float x1 = Math.Max(a.Left, b.Left);
            float y1 = Math.Max(a.Top, b.Top);
            float x2 = Math.Min(a.Right, b.Right);
            float y2 = Math.Min(a.Bottom, b.Bottom);

            float iw = x2 - x1;
            float ih = y2 - y1;
            if (iw <= 0f || ih <= 0f)
                return false;

            double inter = (double)iw * ih;
            double areaA = (double)a.Width * a.Height;
            double areaB = (double)b.Width * b.Height;
            double min = Math.Min(areaA, areaB);

            if (min <= 1d)
                return false;

            return inter / min >= threshold;
        }

        private static void TryBindMobileNpcWindowIfDue(string windowKey, GComponent window, string resolveInfo)
        {
            if (window == null || window._disposed)
                return;

            MobileNpcWindowBinding binding = _mobileNpcBinding;
            if (binding != null && (binding.Window == null || binding.Window._disposed))
            {
                ResetMobileNpcBindings();
                binding = null;
            }

            if (binding == null || !ReferenceEquals(binding.Window, window))
            {
                ResetMobileNpcBindings();
                binding = new MobileNpcWindowBinding
                {
                    WindowKey = windowKey,
                    Window = window,
                    ResolveInfo = resolveInfo,
                };

                _mobileNpcBinding = binding;
                _mobileNpcBindingsDumped = false;
                _nextMobileNpcBindAttemptUtc = DateTime.MinValue;
            }

            bool optionListOk =
                (binding.OptionList != null && !binding.OptionList._disposed && binding.OptionItemRenderer != null)
                || (binding.FallbackOptionsRoot != null && !binding.FallbackOptionsRoot._disposed);
            bool textOk =
                binding.Name != null && !binding.Name._disposed &&
                binding.Content != null && !binding.Content._disposed;

            if (optionListOk && textOk)
                return;

            if (DateTime.UtcNow < _nextMobileNpcBindAttemptUtc)
                return;

            _nextMobileNpcBindAttemptUtc = DateTime.UtcNow.AddSeconds(2);

            string nameSpec = string.Empty;
            string contentSpec = string.Empty;
            string optionListSpec = string.Empty;

            try
            {
                InIReader reader = TryCreateConfigReader();
                if (reader != null)
                {
                    nameSpec = reader.ReadString(FairyGuiConfigSectionName, MobileNpcNameConfigKey, string.Empty, writeWhenNull: false);
                    contentSpec = reader.ReadString(FairyGuiConfigSectionName, MobileNpcContentConfigKey, string.Empty, writeWhenNull: false);
                    optionListSpec = reader.ReadString(FairyGuiConfigSectionName, MobileNpcOptionListConfigKey, string.Empty, writeWhenNull: false);
                }
            }
            catch
            {
                nameSpec = string.Empty;
                contentSpec = string.Empty;
                optionListSpec = string.Empty;
            }

            nameSpec = nameSpec?.Trim() ?? string.Empty;
            contentSpec = contentSpec?.Trim() ?? string.Empty;
            optionListSpec = optionListSpec?.Trim() ?? string.Empty;

            binding.NameOverrideSpec = nameSpec;
            binding.NameOverrideKeywords = null;
            binding.ContentOverrideSpec = contentSpec;
            binding.ContentOverrideKeywords = null;
            binding.OptionListOverrideSpec = optionListSpec;
            binding.OptionListOverrideKeywords = null;

            GTextField ResolveTextField(string spec, string[] defaultKeywords, out string resolveInfoOut, out string[] overrideKeywords)
            {
                resolveInfoOut = null;
                overrideKeywords = null;

                if (!string.IsNullOrWhiteSpace(spec))
                {
                    if (TryResolveMobileMainHudObjectBySpec(window, spec, out GObject resolved, out string[] keywords))
                    {
                        if (resolved is GTextField resolvedField && !resolvedField._disposed && resolved is not GTextInput)
                        {
                            resolveInfoOut = DescribeObject(window, resolvedField) + " (override)";
                            return resolvedField;
                        }

                        if (keywords != null && keywords.Length > 0)
                            overrideKeywords = keywords;
                    }
                    else
                    {
                        overrideKeywords = SplitKeywords(spec);
                    }
                }

                string[] keywordsUsed = overrideKeywords != null && overrideKeywords.Length > 0 ? overrideKeywords : defaultKeywords;
                List<(int Score, GObject Target)> candidates = CollectMobileChatCandidates(window, obj => obj is GTextField && obj is not GTextInput, keywordsUsed, ScoreMobileShopTextCandidate);
                int minScore = overrideKeywords != null && overrideKeywords.Length > 0 ? 20 : 25;
                GTextField selected = SelectMobileChatCandidate<GTextField>(candidates, minScore);
                if (selected != null && !selected._disposed)
                    resolveInfoOut = DescribeObject(window, selected) + (overrideKeywords != null && overrideKeywords.Length > 0 ? " (keywords)" : " (auto)");
                return selected;
            }

            if (binding.Name == null || binding.Name._disposed)
            {
                binding.Name = ResolveTextField(nameSpec, DefaultNpcNameKeywords, out string resolved, out string[] overrideKeywords);
                binding.NameResolveInfo = resolved;
                binding.NameOverrideKeywords = overrideKeywords;
            }

            if (binding.Content == null || binding.Content._disposed)
            {
                binding.Content = ResolveTextField(contentSpec, DefaultNpcContentKeywords, out string resolved, out string[] overrideKeywords);
                binding.ContentResolveInfo = resolved;
                binding.ContentOverrideKeywords = overrideKeywords;
            }

            // 若 Name/Content 无法解析，则直接启用兜底布局，保证至少能显示对话文本与选项
            bool useFallbackLayout = false;

            try
            {
                useFallbackLayout = TryEnsureMobileNpcFallbackLayout(binding);
            }
            catch
            {
                useFallbackLayout = false;
            }

            // 内容区富文本链接点击（<a href=...> / [url=...]）
            try
            {
                if (binding.Content != null && !binding.Content._disposed)
                {
                    // 兼容：publish 资源包把内容做成普通 GTextField 时，UBB 链接不会生成可点击形状，onClickLink 不触发。
                    if (binding.Content is not GRichTextField)
                    {
                        binding.Content = TryUpgradeNpcContentToRichText(window, binding.Content);
                        if (binding.Content != null && !binding.Content._disposed)
                            binding.ContentResolveInfo = DescribeObject(window, binding.Content) + " (upgrade:richtext)";
                    }

                    binding.Content.touchable = true;
                    binding.Content.onClickLink.Remove(OnMobileNpcContentLinkClicked);
                    binding.Content.onClickLink.Add(OnMobileNpcContentLinkClicked);

                    // 某些 publish 包会把父级容器设为 touchable=false，导致 onClickLink 不触发。
                    try { EnsureMobileInteractiveChain(binding.Content, window); } catch { }
                }
            }
            catch
            {
            }

            if (useFallbackLayout)
            {
                binding.OptionList = null;
                binding.OptionListResolveInfo = "fallback";
                binding.OptionItemRenderer = null;
                _mobileNpcDirty = true;
                return;
            }

            if (binding.OptionList == null || binding.OptionList._disposed)
            {
                binding.OptionList = null;
            }

            GList list = binding.OptionList;
            string listResolveInfo = null;
            List<(int Score, GObject Target)> listCandidates = null;

            if (list == null)
            {
                if (!string.IsNullOrWhiteSpace(optionListSpec))
                {
                    if (TryResolveMobileMainHudObjectBySpec(window, optionListSpec, out GObject resolved, out string[] keywords))
                    {
                        if (resolved is GList resolvedList && !resolvedList._disposed)
                        {
                            list = resolvedList;
                            listResolveInfo = DescribeObject(window, resolvedList) + " (override)";
                        }
                        else if (keywords != null && keywords.Length > 0)
                        {
                            binding.OptionListOverrideKeywords = keywords;
                        }
                    }
                    else
                    {
                        binding.OptionListOverrideKeywords = SplitKeywords(optionListSpec);
                    }
                }

                string[] keywordsUsed = binding.OptionListOverrideKeywords != null && binding.OptionListOverrideKeywords.Length > 0
                    ? binding.OptionListOverrideKeywords
                    : DefaultNpcOptionListKeywords;

                int minScore = binding.OptionListOverrideKeywords != null && binding.OptionListOverrideKeywords.Length > 0 ? 35 : 40;
                listCandidates = CollectMobileChatCandidates(window, obj => obj is GList, keywordsUsed, ScoreMobileShopListCandidate);
                list = SelectMobileChatCandidate<GList>(listCandidates, minScore);
                if (list != null && !list._disposed)
                    listResolveInfo = DescribeObject(window, list) + (binding.OptionListOverrideKeywords != null && binding.OptionListOverrideKeywords.Length > 0 ? " (keywords)" : " (auto)");
            }

            if (list == null || list._disposed)
            {
                CMain.SaveError("FairyGUI: NPC 对话窗口未找到选项列表（Npc）。可在 Mir2Config.ini 设置 [" + FairyGuiConfigSectionName + "] " +
                                MobileNpcOptionListConfigKey + "=idx:... 指定选项列表（或 path:/name:/item:/url:/title: / 关键字列表 a|b|c）。");
                if (TryEnsureMobileNpcFallbackLayout(binding))
                {
                    binding.OptionList = null;
                    binding.OptionListResolveInfo = null;
                    binding.OptionItemRenderer = null;
                    _mobileNpcDirty = true;
                    return;
                }

                return;
            }

            binding.OptionList = list;
            binding.OptionListResolveInfo = listResolveInfo;

            if (binding.OptionItemRenderer == null)
                binding.OptionItemRenderer = RenderMobileNpcOptionListItem;

            try
            {
                binding.OptionList.touchable = true;
                binding.OptionList.itemRenderer = binding.OptionItemRenderer;
            }
            catch
            {
            }

            _mobileNpcDirty = true;
            TryDumpMobileNpcBindingsReportIfDue(binding, listCandidates ?? new List<(int Score, GObject Target)>());
        }

        private static GTextField TryUpgradeNpcContentToRichText(GComponent window, GTextField content)
        {
            if (window == null || window._disposed)
                return content;

            if (content == null || content._disposed)
                return content;

            if (content is GRichTextField)
                return content;

            if (content.parent is not GComponent parent || parent == null || parent._disposed)
                return content;

            try
            {
                var rich = new GRichTextField
                {
                    name = content.name,
                    touchable = true,
                };

                try { rich.visible = content.visible; } catch { }
                try { rich.enabled = content.enabled; } catch { }
                try { rich.grayed = content.grayed; } catch { }
                try { rich.alpha = content.alpha; } catch { }
                try { rich.rotation = content.rotation; } catch { }

                try { rich.SetPivot(content.pivotX, content.pivotY, content.pivotAsAnchor); } catch { }
                try { rich.SetScale(content.scaleX, content.scaleY); } catch { }

                try { rich.align = AlignType.Left; } catch { }
                try { rich.verticalAlign = VertAlignType.Top; } catch { }
                try { rich.autoSize = AutoSizeType.None; } catch { }
                try { rich.singleLine = false; } catch { }
                try { rich.maxWidth = 0; } catch { }
                try { rich.UBBEnabled = true; } catch { }
                try { rich.stroke = content.stroke; } catch { }
                try { rich.strokeColor = content.strokeColor; } catch { }
                try { rich.textFormat = content.textFormat; } catch { }

                try
                {
                    rich.SetPosition(content.x, content.y);
                    rich.SetSize(content.width, content.height);
                }
                catch
                {
                }

                int idx = -1;
                try { idx = parent.GetChildIndex(content); } catch { idx = -1; }

                if (idx >= 0)
                    parent.AddChildAt(rich, idx);
                else
                    parent.AddChild(rich);

                try { rich.relations.CopyFrom(content.relations); } catch { }

                try { parent.RemoveChild(content, dispose: true); } catch { }

                return rich;
            }
            catch
            {
                return content;
            }
        }

        private static void TryRefreshMobileNpcIfDue(bool force)
        {
            if (_stage == null || !_initialized || !_packagesLoaded)
                return;

            if (!MobileWindows.TryGetValue("Npc", out GComponent window) || window == null || window._disposed)
            {
                if (_mobileNpcBinding != null)
                    ResetMobileNpcBindings();
                return;
            }

            if (!window.visible)
                return;

            TryBindMobileNpcWindowIfDue("Npc", window, resolveInfo: null);

            MobileNpcWindowBinding binding = _mobileNpcBinding;
            if (binding == null || binding.Window == null || binding.Window._disposed)
            {
                ResetMobileNpcBindings();
                return;
            }

            if (!force && !_mobileNpcDirty && DateTime.UtcNow >= _mobileNpcForceRefreshUntilUtc)
                return;

            _mobileNpcDirty = false;

            try
            {
                ApplyMobileNpcTextPadding(binding);
            }
            catch
            {
            }

            try
            {
                if (binding.Name != null && !binding.Name._disposed)
                {
                    try
                    {
                        TextFormat tf = binding.Name.textFormat;
                        tf.size = Math.Clamp(Math.Max(tf.size, 24), 1, 192);
                        binding.Name.textFormat = tf;
                        if (binding.Name.stroke < 1)
                            binding.Name.stroke = 1;
                        binding.Name.strokeColor = new Color(0, 0, 0, 200);
                    }
                    catch
                    {
                    }

                    binding.Name.text = _mobileNpcName ?? string.Empty;
                }
            }
            catch
            {
            }

            try
            {
                if (binding.Content != null && !binding.Content._disposed)
                {
                    try
                    {
                        binding.Content.touchable = true;
                        binding.Content.UBBEnabled = true;
                    }
                    catch
                    {
                    }

                    try
                    {
                        TextFormat tf = binding.Content.textFormat;
                        tf.size = Math.Clamp(Math.Max(tf.size, 22), 1, 192);
                        binding.Content.textFormat = tf;
                        if (binding.Content.stroke < 1)
                            binding.Content.stroke = 1;
                        binding.Content.strokeColor = new Color(0, 0, 0, 200);
                    }
                    catch
                    {
                    }

                    binding.Content.text = _mobileNpcContentText ?? string.Empty;
                }
            }
            catch
            {
            }

            try
            {
                if (binding.OptionList != null && !binding.OptionList._disposed)
                {
                    binding.OptionList.numItems = MobileNpcOptions.Count;
                }
                else
                {
                    TryRefreshMobileNpcFallbackIfDue(binding);
                }
            }
            catch (Exception ex)
            {
                // 之前只记录 Message，定位不到真实抛错点；这里改为 ToString 输出堆栈
                CMain.SaveError("FairyGUI: 刷新 NPC 对话窗口失败：" + ex);
                _nextMobileNpcBindAttemptUtc = DateTime.MinValue;
                _mobileNpcDirty = true;
            }
        }

        private static void ApplyMobileNpcTextPadding(MobileNpcWindowBinding binding)
        {
            if (binding == null)
                return;

            const float desired = 20f;

            void Apply(GTextField field, bool expandWidth)
            {
                if (field == null || field._disposed)
                    return;

                float x;
                float y;
                float w;
                float h;

                try
                {
                    x = field.x;
                    y = field.y;
                    w = field.width;
                    h = field.height;
                }
                catch
                {
                    return;
                }

                float newX = x < desired ? desired : x;
                float newY = y < desired ? desired : y;
                float newW = w;
                float newH = h;

                try
                {
                    if (field.parent is GComponent parent && parent != null && !parent._disposed)
                    {
                        float availableWidth = Math.Max(10f, parent.width - newX - desired);
                        float availableHeight = Math.Max(10f, parent.height - newY - desired);

                        if (expandWidth && availableWidth > 10f)
                            newW = availableWidth;
                        else if (w > availableWidth)
                            newW = availableWidth;

                        if (h > availableHeight)
                            newH = availableHeight;
                    }
                }
                catch
                {
                }

                if (Math.Abs(newX - x) > 0.1f || Math.Abs(newY - y) > 0.1f)
                {
                    try { field.SetPosition(newX, newY); } catch { }
                }

                if (Math.Abs(newW - w) > 0.1f || Math.Abs(newH - h) > 0.1f)
                {
                    try { field.SetSize(Math.Max(10f, newW), Math.Max(10f, newH)); } catch { }
                }
            }

            Apply(binding.Name, expandWidth: false);
            Apply(binding.Content, expandWidth: true);
        }

        private static void TryDumpMobileNpcBindingsReportIfDue(MobileNpcWindowBinding binding, List<(int Score, GObject Target)> optionListCandidates)
        {
            if (!Settings.DebugMode || _mobileNpcBindingsDumped)
                return;

            if (binding == null || binding.Window == null || binding.Window._disposed)
                return;

            try
            {
                Directory.CreateDirectory(ClientResourceLayout.RuntimeRoot);
                string path = Path.Combine(ClientResourceLayout.RuntimeRoot, "FairyGui-MobileNpcBindings.txt");

                var builder = new StringBuilder(10 * 1024);
                builder.AppendLine("FairyGUI NPC 对话窗口绑定报告（用于排障/补充映射）");
                builder.AppendLine($"GeneratedAtUtc={DateTime.UtcNow:o}");
                builder.AppendLine($"WindowKey={binding.WindowKey}");
                if (!string.IsNullOrWhiteSpace(binding.ResolveInfo))
                    builder.AppendLine($"Resolved={binding.ResolveInfo}");
                builder.AppendLine();
                builder.AppendLine("配置覆盖（可选）：Mir2Config.ini -> [FairyGUI]");
                builder.AppendLine($"  {MobileNpcNameConfigKey}=idx:...（NPC 名称文本）");
                builder.AppendLine($"  {MobileNpcContentConfigKey}=idx:...（对话内容文本）");
                builder.AppendLine($"  {MobileNpcOptionListConfigKey}=idx:...（选项列表 GList）");
                builder.AppendLine("说明：idx/path 均相对 NPC 对话窗口 Root。建议先查看窗口树（FairyGui-MobileWindow-Npc-Tree.txt），再填入精确覆盖。");
                builder.AppendLine();

                builder.AppendLine("绑定结果：");
                builder.AppendLine($"Name={DescribeObject(binding.Window, binding.Name)} OverrideSpec={binding.NameOverrideSpec ?? "-"}");
                builder.AppendLine($"Content={DescribeObject(binding.Window, binding.Content)} OverrideSpec={binding.ContentOverrideSpec ?? "-"}");
                builder.AppendLine($"OptionList={DescribeObject(binding.Window, binding.OptionList)} OverrideSpec={binding.OptionListOverrideSpec ?? "-"}");
                builder.AppendLine();

                builder.AppendLine("OptionList Candidates(top 12):");
                int top = Math.Min(12, optionListCandidates?.Count ?? 0);
                for (int i = 0; i < top; i++)
                {
                    (int score, GObject target) = optionListCandidates[i];
                    builder.AppendLine($"  - score={score} obj={DescribeObject(binding.Window, target)}");
                }

                if ((optionListCandidates?.Count ?? 0) > top)
                    builder.AppendLine($"  ... ({optionListCandidates.Count - top} more)");

                File.WriteAllText(path, builder.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                _mobileNpcBindingsDumped = true;
                CMain.SaveLog($"FairyGUI: 已导出 NPC 对话窗口绑定报告到 {path}");
            }
            catch (Exception ex)
            {
                CMain.SaveError("FairyGUI: 导出 NPC 对话窗口绑定报告失败：" + ex.Message);
            }
        }

        private static void ParseNpcPage(IList<string> pageLines, out string contentText, out List<MobileNpcOptionEntry> options)
        {
            var optionList = new List<MobileNpcOptionEntry>(32);
            var content = new List<string>(32);

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            string BuildRichLink(string text, string href)
            {
                text = (text ?? string.Empty).Trim();
                href = (href ?? string.Empty).Trim();

                if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(href))
                    return text;

                // 输出 UBB：最终会转成 <a href="..."> 并由 TextField 解析成可点击的 onClickLink（黄字+下划线）。
                return "[color=#FFD200][u][url=" + href + "]" + text + "[/url][/u][/color]";
            }

            void AddOption(string text, string action, string link)
            {
                text = (text ?? string.Empty).Trim();
                action = (action ?? string.Empty).Trim();
                link = (link ?? string.Empty).Trim();

                if (string.IsNullOrWhiteSpace(text))
                    return;

                string key = text + "\n" + action + "\n" + link;
                if (!seen.Add(key))
                    return;

                optionList.Add(new MobileNpcOptionEntry
                {
                    Text = text,
                    Action = action,
                    Link = link,
                });
            }

            if (pageLines != null)
            {
                for (int i = 0; i < pageLines.Count; i++)
                {
                    string line = pageLines[i] ?? string.Empty;

                    // 兼容 HTML 风格的 <a href='@Main'>xxx</a>（某些脚本/服务端组件可能输出该格式）
                    try
                    {
                        line = NpcHtmlAnchorRegex.Replace(line, m =>
                        {
                            string href = m.Groups.Count > 1 ? m.Groups[1].Value : string.Empty;
                            string txt = m.Groups.Count > 2 ? m.Groups[2].Value : string.Empty;

                            try { href = WebUtility.HtmlDecode(href); } catch { }
                            try { txt = WebUtility.HtmlDecode(txt); } catch { }

                            try { txt = NpcHtmlTagRegex.Replace(txt ?? string.Empty, string.Empty); } catch { }

                            href = (href ?? string.Empty).Trim();
                            if (href.StartsWith("event:", StringComparison.OrdinalIgnoreCase))
                                href = href.Substring("event:".Length).Trim();

                            if (href.StartsWith("[", StringComparison.Ordinal) && href.EndsWith("]", StringComparison.Ordinal) && href.Length >= 3)
                                href = href.Substring(1, href.Length - 2).Trim();

                            if (string.IsNullOrWhiteSpace(txt))
                                txt = href;

                            if (string.IsNullOrWhiteSpace(href))
                                return txt;

                            if (href.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || href.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                            {
                                AddOption(txt, action: null, link: href);
                                return BuildRichLink(txt, href);
                            }

                            // 默认按“脚本页跳转”处理（通常为 @xxx）。注意：PC 端不会截断 action，
                            // 因此这里保留原始字符串（包含可能的 / 参数），避免点击无反应。
                            string action = href;
                            AddOption(txt, action, link: null);
                            return BuildRichLink(txt, action);
                        });
                    }
                    catch
                    {
                    }

                    try
                    {
                        line = NpcBigButtonRegex.Replace(line, m =>
                        {
                            string txt = m.Groups.Count > 2 ? m.Groups[2].Value : string.Empty;
                            string actionRaw = m.Groups.Count > 3 ? m.Groups[3].Value : string.Empty;
                            // 对齐 PC：不截断 actionRaw（可能包含 / 参数），避免点击无反应。
                            string action = (actionRaw ?? string.Empty).Trim();

                            // 旧格式的“大按钮”在 PC 端通常渲染在底部选项区。移动端这里直接输出为正文黄字链接，
                            // 避免与底部按钮重复，同时保证仍可点击跳转。
                            return BuildRichLink(txt, action);
                        });
                    }
                    catch
                    {
                    }

                    try
                    {
                        line = NpcInlineActionRegex.Replace(line, m =>
                        {
                            string txt = m.Groups.Count > 2 ? m.Groups[2].Value : string.Empty;
                            string actionRaw = m.Groups.Count > 3 ? m.Groups[3].Value : string.Empty;
                            // 对齐 PC：不截断 actionRaw（可能包含 / 参数）。
                            string action = actionRaw;
                            AddOption(txt, action, link: null);
                            return BuildRichLink(txt, action);
                        });
                    }
                    catch
                    {
                    }

                    try
                    {
                        line = NpcLinkRegex.Replace(line, m =>
                        {
                            string txt = m.Groups.Count > 2 ? m.Groups[2].Value : string.Empty;
                            string link = m.Groups.Count > 3 ? m.Groups[3].Value : string.Empty;
                            link = (link ?? string.Empty).Trim();
                            if (string.IsNullOrWhiteSpace(link))
                                return txt;

                            // 兼容脚本写法：(文字/@Main) 这种也应当是“黄字跳转”
                            if (link.StartsWith("@", StringComparison.Ordinal))
                            {
                                // 对齐 PC：不截断 actionRaw（可能包含 / 参数）。
                                string action = link;
                                AddOption(txt, action, link: null);
                                return BuildRichLink(txt, action);
                            }

                            if (link.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || link.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                            {
                                AddOption(txt, action: null, link: link);
                                return BuildRichLink(txt, link);
                            }

                            return txt;
                        });
                    }
                    catch
                    {
                    }

                    try
                    {
                        line = NpcColorRegex.Replace(line, m =>
                        {
                            string txt = m.Groups.Count > 2 ? m.Groups[2].Value : string.Empty;
                            return txt;
                        });
                    }
                    catch
                    {
                    }

                    // 剥离剩余 HTML 标签（如 <font ...>），避免在精简 TextField 中以纯文本形式展示。
                    try
                    {
                        line = NpcHtmlTagRegex.Replace(line, string.Empty);
                    }
                    catch
                    {
                    }

                    line = (line ?? string.Empty).TrimEnd();
                    if (!string.IsNullOrWhiteSpace(line))
                        content.Add(line);
                }
            }

            options = optionList;
            contentText = string.Join("\n", content);
        }
    }
}
