using System;
using System.Collections.Generic;
using C = ClientPackets;
using FairyGUI;
using MonoShare.MirNetwork;
using MonoShare.MirScenes;

namespace MonoShare;

internal static partial class FairyGuiHost
{
    private static readonly string[] MobileHeroListKeywords =
        { "hero", "herolist", "managehero", "avatar", "slot", "英雄", "列表", "头像", "槽位" };
    private static readonly string[] MobileHeroNameKeywords =
        { "name", "hero", "avatar", "名称", "名字", "英雄" };
    private static readonly string[] MobileHeroLevelKeywords =
        { "level", "lv", "等级", "级" };
    private static readonly string[] MobileHeroCurrentKeywords =
        { "current", "active", "selected", "当前", "激活", "出战" };
    private static readonly string[] MobileHeroSummaryKeywords =
        { "count", "total", "maximum", "max", "数量", "上限", "总数" };
    private static readonly string[] MobileHeroErrorKeywords =
        { "error", "fail", "message", "错误", "失败", "提示" };

    private sealed class MobileHeroItemView
    {
        public GComponent Root;
        public GTextField Name;
        public GTextField Level;
        public EventCallback0 ClickCallback;
        public int Index = -1;
    }

    private sealed class MobileHeroWindowBinding
    {
        public string WindowKey;
        public GComponent Window;
        public GList List;
        public GTextField Current;
        public GTextField Summary;
        public GTextField Error;
        public ListItemRenderer ItemRenderer;
    }

    private static MobileHeroWindowBinding _mobileHeroBinding;
    private static DateTime _nextMobileHeroBindAttemptUtc = DateTime.MinValue;
    private static bool _mobileHeroDirty = true;

    public static void MarkMobileHeroDirty()
    {
        _mobileHeroDirty = true;
        TryRefreshMobileHeroIfDue(force: false);
    }

    private static void ResetMobileHeroBindings()
    {
        MobileHeroWindowBinding binding = _mobileHeroBinding;
        if (binding != null && binding.List != null && !binding.List._disposed && binding.ItemRenderer != null)
        {
            try
            {
                binding.List.itemRenderer = null;
            }
            catch
            {
            }
        }

        _mobileHeroBinding = null;
        _nextMobileHeroBindAttemptUtc = DateTime.MinValue;
        _mobileHeroDirty = true;
    }

    private static MobileHeroItemView GetOrCreateMobileHeroItemView(GComponent itemRoot)
    {
        if (itemRoot == null || itemRoot._disposed)
            return null;

        if (itemRoot.data is MobileHeroItemView existing && existing.Root != null && !existing.Root._disposed)
            return existing;

        var view = new MobileHeroItemView { Root = itemRoot };

        try
        {
            List<(int Score, GObject Target)> nameCandidates = CollectMobileChatCandidates(
                itemRoot,
                obj => obj is GTextField && obj is not GTextInput,
                MobileHeroNameKeywords,
                ScoreMobileShopTextCandidate);
            view.Name = SelectMobileChatCandidate<GTextField>(nameCandidates, minScore: 12);
        }
        catch
        {
        }

        try
        {
            List<(int Score, GObject Target)> levelCandidates = CollectMobileChatCandidates(
                itemRoot,
                obj => obj is GTextField && obj is not GTextInput,
                MobileHeroLevelKeywords,
                ScoreMobileShopTextCandidate);
            view.Level = SelectMobileChatCandidate<GTextField>(levelCandidates, minScore: 12);
        }
        catch
        {
        }

        try
        {
            view.ClickCallback = () => OnMobileHeroItemClicked(view);
            itemRoot.onClick.Add(view.ClickCallback);
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

    private static void ClearMobileHeroItemView(MobileHeroItemView view)
    {
        if (view == null)
            return;

        try { if (view.Name != null && !view.Name._disposed) view.Name.text = string.Empty; } catch { }
        try { if (view.Level != null && !view.Level._disposed) view.Level.text = string.Empty; } catch { }
        try { if (view.Root != null && !view.Root._disposed && view.Root.asButton != null) view.Root.asButton.title = string.Empty; } catch { }
    }

    private static void RenderMobileHeroListItem(int index, GObject itemObject)
    {
        if (itemObject is not GComponent itemRoot || itemRoot._disposed)
            return;

        MobileHeroItemView view = GetOrCreateMobileHeroItemView(itemRoot);
        if (view == null)
            return;

        view.Index = index;
        ClientHeroInformation hero = null;
        try
        {
            IReadOnlyList<ClientHeroInformation> heroes = GameScene.MobileHeroState.Heroes;
            if (heroes != null && index >= 0 && index < heroes.Count)
                hero = heroes[index];
        }
        catch
        {
        }

        if (hero == null)
        {
            ClearMobileHeroItemView(view);
            return;
        }

        string name = hero.Name ?? string.Empty;
        string level = $"等级 {hero.Level}";
        try { if (view.Name != null && !view.Name._disposed) view.Name.text = name; } catch { }
        try { if (view.Level != null && !view.Level._disposed) view.Level.text = level; } catch { }
        try { if (view.Root != null && !view.Root._disposed && view.Root.asButton != null && view.Name == null) view.Root.asButton.title = name + " " + level; } catch { }
    }

    private static void OnMobileHeroItemClicked(MobileHeroItemView view)
    {
        if (view == null || view.Index < 0)
            return;

        try
        {
            IReadOnlyList<ClientHeroInformation> heroes = GameScene.MobileHeroState.Heroes;
            if (heroes == null || view.Index >= heroes.Count || heroes[view.Index] == null)
                return;

            Network.Enqueue(new C.ChangeHero { ListIndex = view.Index + 1 });
        }
        catch (Exception ex)
        {
            CMain.SaveError("FairyGUI: 英雄切换请求失败：" + ex.Message);
        }
    }

    private static void TryBindMobileHeroWindowIfDue(string windowKey, GComponent window, string resolveInfo)
    {
        if (window == null || window._disposed)
            return;

        MobileHeroWindowBinding binding = _mobileHeroBinding;
        if (binding != null && (binding.Window == null || binding.Window._disposed || !ReferenceEquals(binding.Window, window)))
        {
            ResetMobileHeroBindings();
            binding = null;
        }

        if (binding == null)
        {
            binding = new MobileHeroWindowBinding
            {
                WindowKey = windowKey,
                Window = window,
            };
            _mobileHeroBinding = binding;
            _nextMobileHeroBindAttemptUtc = DateTime.MinValue;
        }

        bool complete = binding.List != null && !binding.List._disposed &&
                        binding.Current != null && !binding.Current._disposed &&
                        binding.Summary != null && !binding.Summary._disposed;
        if (complete)
            return;

        if (DateTime.UtcNow < _nextMobileHeroBindAttemptUtc)
            return;
        _nextMobileHeroBindAttemptUtc = DateTime.UtcNow.AddSeconds(2);

        if (binding.List == null || binding.List._disposed)
        {
            try
            {
                List<(int Score, GObject Target)> candidates = CollectMobileChatCandidates(
                    window,
                    obj => obj is GList && obj.touchable,
                    MobileHeroListKeywords,
                    ScoreMobileShopListCandidate);
                binding.List = SelectMobileChatCandidate<GList>(candidates, minScore: 20);
            }
            catch
            {
            }
        }

        GTextField ResolveTextField(string[] keywords, GList excludeList, int minScore)
        {
            try
            {
                List<(int Score, GObject Target)> candidates = CollectMobileChatCandidates(
                    window,
                    obj => obj is GTextField && obj is not GTextInput &&
                           (excludeList == null || !IsMobileHeroDescendantOf(obj, excludeList)),
                    keywords,
                    ScoreMobileShopTextCandidate);
                return SelectMobileChatCandidate<GTextField>(candidates, minScore);
            }
            catch
            {
                return null;
            }
        }

        if (binding.Current == null || binding.Current._disposed)
            binding.Current = ResolveTextField(MobileHeroCurrentKeywords, binding.List, 15);
        if (binding.Summary == null || binding.Summary._disposed)
            binding.Summary = ResolveTextField(MobileHeroSummaryKeywords, binding.List, 15);
        if (binding.Error == null || binding.Error._disposed)
            binding.Error = ResolveTextField(MobileHeroErrorKeywords, binding.List, 15);

        if (binding.List != null && !binding.List._disposed)
        {
            try
            {
                if (!binding.List.isVirtual && binding.List.scrollPane != null)
                    binding.List.SetVirtual();
            }
            catch
            {
            }

            if (binding.ItemRenderer == null)
                binding.ItemRenderer = RenderMobileHeroListItem;

            try { binding.List.itemRenderer = binding.ItemRenderer; } catch { }
        }
    }

    private static bool IsMobileHeroDescendantOf(GObject child, GObject ancestor)
    {
        if (child == null || ancestor == null)
            return false;

        try
        {
            GObject current = child;
            while (current != null && !current._disposed)
            {
                if (ReferenceEquals(current, ancestor))
                    return true;
                current = current.parent;
            }
        }
        catch
        {
        }

        return false;
    }

    private static void TryRefreshMobileHeroIfDue(bool force)
    {
        if (_stage == null || !_initialized || !_packagesLoaded)
            return;

        if (!MobileWindows.TryGetValue("Hero", out GComponent window) || window == null || window._disposed)
        {
            if (_mobileHeroBinding != null)
                ResetMobileHeroBindings();
            return;
        }

        if (!window.visible)
            return;

        TryBindMobileHeroWindowIfDue("Hero", window, resolveInfo: null);

        MobileHeroWindowBinding binding = _mobileHeroBinding;
        if (binding == null || binding.Window == null || binding.Window._disposed)
        {
            ResetMobileHeroBindings();
            return;
        }

        bool bindingComplete = binding.List != null && !binding.List._disposed &&
                               binding.Current != null && !binding.Current._disposed &&
                               binding.Summary != null && !binding.Summary._disposed;
        if (!bindingComplete)
        {
            // Keep the dirty bit set so a later bind attempt retries and then
            // fills the newly completed binding instead of losing the update.
            _mobileHeroDirty = true;
            return;
        }

        if (!force && !_mobileHeroDirty)
            return;
        _mobileHeroDirty = false;

        MobileHeroState state = GameScene.MobileHeroState;
        try
        {
            if (binding.Current != null && !binding.Current._disposed)
            {
                ClientHeroInformation current = state.CurrentHero;
                binding.Current.text = current == null
                    ? "当前英雄：无"
                    : $"当前英雄：{current.Name ?? string.Empty}  等级 {current.Level}";
            }

            if (binding.Summary != null && !binding.Summary._disposed)
            {
                int filled = 0;
                IReadOnlyList<ClientHeroInformation> heroes = state.Heroes;
                if (heroes != null)
                    for (int i = 0; i < heroes.Count; i++)
                        if (heroes[i] != null) filled++;
                binding.Summary.text = $"英雄：{filled}/{Math.Max(0, state.MaximumCount - 1)}";
            }

            if (binding.Error != null && !binding.Error._disposed)
                binding.Error.text = state.Error ?? string.Empty;
        }
        catch
        {
        }

        try
        {
            if (binding.List != null && !binding.List._disposed)
            {
                int count = state.Heroes?.Count ?? 0;
                if (binding.List.numItems != count)
                    binding.List.numItems = count;
                else if (binding.List.isVirtual)
                    binding.List.RefreshVirtualList();
            }
        }
        catch
        {
        }
    }
}
