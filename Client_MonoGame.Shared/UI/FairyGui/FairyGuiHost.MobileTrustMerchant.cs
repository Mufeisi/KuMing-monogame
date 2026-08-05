using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using C = ClientPackets;
using FairyGUI;
using MonoShare.MirControls;
using MonoShare.MirNetwork;
using MonoShare.MirScenes;

namespace MonoShare
{
    internal static partial class FairyGuiHost
    {
        private const string MobileTrustMerchantListConfigKey = "MobileTrustMerchant.List";
        private const string MobileTrustMerchantSearchInputConfigKey = "MobileTrustMerchant.SearchInput";
        private const string MobileTrustMerchantSearchButtonConfigKey = "MobileTrustMerchant.SearchButton";
        private const string MobileTrustMerchantRefreshButtonConfigKey = "MobileTrustMerchant.RefreshButton";
        private const string MobileTrustMerchantPrevPageButtonConfigKey = "MobileTrustMerchant.PrevPageButton";
        private const string MobileTrustMerchantNextPageButtonConfigKey = "MobileTrustMerchant.NextPageButton";
        private const string MobileTrustMerchantPageLabelConfigKey = "MobileTrustMerchant.PageLabel";
        private const string MobileTrustMerchantTabMarketConfigKey = "MobileTrustMerchant.TabMarket";
        private const string MobileTrustMerchantTabConsignConfigKey = "MobileTrustMerchant.TabConsign";
        private const string MobileTrustMerchantTabAuctionConfigKey = "MobileTrustMerchant.TabAuction";
        private const string MobileTrustMerchantPrimaryButtonConfigKey = "MobileTrustMerchant.PrimaryButton";
        private const string MobileTrustMerchantSellNowButtonConfigKey = "MobileTrustMerchant.SellNowButton";
        private const string MobileTrustMerchantCollectSoldButtonConfigKey = "MobileTrustMerchant.CollectSoldButton";
        private const string MobileTrustMerchantPutAwayButtonConfigKey = "MobileTrustMerchant.PutAwayButton";

        private static readonly string[] DefaultTrustMerchantListKeywords =
            { "trust", "merchant", "market", "auction", "goods", "item", "list", "grid", "信任商人", "交易行", "拍卖行", "拍卖", "寄售", "列表", "商品" };

        private static readonly string[] DefaultTrustMerchantItemIconKeywords = { "icon", "img", "image", "item", "goods", "物品", "道具", "图标" };
        private static readonly string[] DefaultTrustMerchantItemNameKeywords = { "name", "item", "goods", "物品", "道具", "名称", "名字" };
        private static readonly string[] DefaultTrustMerchantItemSellerKeywords = { "seller", "from", "卖", "卖方", "卖家", "摊主", "出售者" };
        private static readonly string[] DefaultTrustMerchantItemPriceKeywords = { "price", "gold", "money", "金币", "价格", "出价", "竞价", "元宝" };
        private static readonly string[] DefaultTrustMerchantItemDateKeywords = { "date", "time", "expire", "end", "截至", "日期", "时间", "有效期", "剩余" };

        private static readonly string[] DefaultTrustMerchantSearchInputKeywords = { "search", "find", "match", "查找", "搜索", "查询", "输入" };
        private static readonly string[] DefaultTrustMerchantSearchButtonKeywords = { "search", "find", "查找", "搜索", "查询", "确认" };
        private static readonly string[] DefaultTrustMerchantRefreshButtonKeywords = { "refresh", "update", "刷新", "更新" };
        private static readonly string[] DefaultTrustMerchantPrevPageButtonKeywords = { "prev", "back", "left", "上一页", "上页", "上一", "返回" };
        private static readonly string[] DefaultTrustMerchantNextPageButtonKeywords = { "next", "right", "下", "下一页", "下页", "下一" };
        private static readonly string[] DefaultTrustMerchantPageLabelKeywords = { "page", "页", "分页" };
        private static readonly string[] DefaultTrustMerchantTabMarketKeywords = { "market", "交易", "交易行", "市场", "寄售市场" };
        private static readonly string[] DefaultTrustMerchantTabConsignKeywords = { "consign", "stall", "寄售", "摆摊", "我的寄售" };
        private static readonly string[] DefaultTrustMerchantTabAuctionKeywords = { "auction", "拍卖", "竞拍", "我的拍卖" };
        private static readonly string[] DefaultTrustMerchantPrimaryButtonKeywords = { "buy", "购买", "竞拍", "取回", "回收", "提取", "确定", "操作" };
        private static readonly string[] DefaultTrustMerchantSellNowButtonKeywords = { "sellnow", "mouth", "一口价", "立即购买", "直接购买" };
        private static readonly string[] DefaultTrustMerchantCollectSoldButtonKeywords = { "collect", "sold", "已售", "收取", "领取", "提取" };
        private static readonly string[] DefaultTrustMerchantPutAwayButtonKeywords = { "put", "sell", "list", "上架", "寄售", "拍卖", "出售" };

        private sealed class MobileTrustMerchantListingView
        {
            public GComponent Root;
            public GLoader Icon;
            public GTextField Name;
            public GTextField Seller;
            public GTextField Price;
            public GTextField Date;
            public EventCallback0 ClickCallback;
            public float OriginalAlpha;
            public bool OriginalAlphaCaptured;
            public bool HasItem;
            public ushort LastIcon;
        }

        private sealed class MobileTrustMerchantWindowBinding
        {
            public string WindowKey;
            public GComponent Window;
            public string ResolveInfo;

            public GList List;
            public string ListResolveInfo;
            public string ListOverrideSpec;
            public string[] ListOverrideKeywords;
            public ListItemRenderer ItemRenderer;

            public GTextInput SearchInput;
            public string SearchInputResolveInfo;
            public string SearchInputOverrideSpec;
            public string[] SearchInputOverrideKeywords;

            public GButton SearchButton;
            public EventCallback0 SearchClickCallback;
            public string SearchButtonResolveInfo;
            public string SearchButtonOverrideSpec;
            public string[] SearchButtonOverrideKeywords;

            public GButton RefreshButton;
            public EventCallback0 RefreshClickCallback;
            public string RefreshButtonResolveInfo;
            public string RefreshButtonOverrideSpec;
            public string[] RefreshButtonOverrideKeywords;

            public GButton PrevPageButton;
            public EventCallback0 PrevPageClickCallback;

            public GButton NextPageButton;
            public EventCallback0 NextPageClickCallback;

            public GTextField PageLabel;

            public GButton TabMarketButton;
            public EventCallback0 TabMarketClickCallback;

            public GButton TabConsignButton;
            public EventCallback0 TabConsignClickCallback;

            public GButton TabAuctionButton;
            public EventCallback0 TabAuctionClickCallback;

            public GButton PrimaryButton;
            public EventCallback0 PrimaryClickCallback;

            public GButton SellNowButton;
            public EventCallback0 SellNowClickCallback;

            public GButton CollectSoldButton;
            public EventCallback0 CollectSoldClickCallback;

            public GButton PutAwayButton;
            public EventCallback0 PutAwayClickCallback;
        }

        private static MobileTrustMerchantWindowBinding _mobileTrustMerchantBinding;
        private static DateTime _nextMobileTrustMerchantBindAttemptUtc = DateTime.MinValue;
        private static bool _mobileTrustMerchantBindingsDumped;
        private static bool _mobileTrustMerchantDirty;

        private static readonly object TrustMerchantGate = new object();
        private static readonly List<ClientAuction> MobileTrustMerchantListings = new List<ClientAuction>(128);
        private static MarketPanelType _mobileTrustMerchantPanelType = MarketPanelType.Market;
        private static bool _mobileTrustMerchantUserMode;
        private static int _mobileTrustMerchantPages;
        private static int _mobileTrustMerchantPage;
        private static int _mobileTrustMerchantSelectedListingIndex = -1;

        private static bool _mobileMarketListingSelectionActive;
        private static MarketPanelType _mobileMarketListingSelectionType = MarketPanelType.Consign;

        public static void BeginMobileMarketListingSelection(MarketPanelType type)
        {
            lock (TrustMerchantGate)
            {
                _mobileMarketListingSelectionActive = true;
                _mobileMarketListingSelectionType = type;
            }

            try
            {
                GameScene.Scene?.OutputMessage("请选择要上架的物品。");
            }
            catch
            {
            }
        }

        public static void UpdateMobileTrustMerchant(MarketPanelType panelType, IList<ClientAuction> listings, int pages, bool userMode)
        {
            lock (TrustMerchantGate)
            {
                _mobileTrustMerchantPanelType = panelType;
                _mobileTrustMerchantUserMode = userMode;
                _mobileTrustMerchantPages = pages;
                _mobileTrustMerchantPage = 0;
                _mobileTrustMerchantSelectedListingIndex = -1;

                MobileTrustMerchantListings.Clear();
                if (listings != null)
                {
                    for (int i = 0; i < listings.Count; i++)
                    {
                        ClientAuction listing = listings[i];
                        if (listing != null)
                            MobileTrustMerchantListings.Add(listing);
                    }
                }

                _mobileTrustMerchantDirty = true;
            }

            TryRefreshMobileTrustMerchantIfDue(force: false);
        }

        public static void AppendMobileTrustMerchantPage(IList<ClientAuction> listings)
        {
            lock (TrustMerchantGate)
            {
                if (listings != null)
                {
                    for (int i = 0; i < listings.Count; i++)
                    {
                        ClientAuction listing = listings[i];
                        if (listing != null)
                            MobileTrustMerchantListings.Add(listing);
                    }
                }

                int page = (MobileTrustMerchantListings.Count - 1) / 10;
                if (page < 0)
                    page = 0;

                _mobileTrustMerchantPage = page;
                _mobileTrustMerchantDirty = true;
            }

            TryRefreshMobileTrustMerchantIfDue(force: false);
        }

        private static void ResetMobileTrustMerchantBindings()
        {
            try
            {
                if (_mobileTrustMerchantBinding != null)
                {
                    DetachButtonCallback(_mobileTrustMerchantBinding.SearchButton, _mobileTrustMerchantBinding.SearchClickCallback);
                    DetachButtonCallback(_mobileTrustMerchantBinding.RefreshButton, _mobileTrustMerchantBinding.RefreshClickCallback);
                    DetachButtonCallback(_mobileTrustMerchantBinding.PrevPageButton, _mobileTrustMerchantBinding.PrevPageClickCallback);
                    DetachButtonCallback(_mobileTrustMerchantBinding.NextPageButton, _mobileTrustMerchantBinding.NextPageClickCallback);
                    DetachButtonCallback(_mobileTrustMerchantBinding.TabMarketButton, _mobileTrustMerchantBinding.TabMarketClickCallback);
                    DetachButtonCallback(_mobileTrustMerchantBinding.TabConsignButton, _mobileTrustMerchantBinding.TabConsignClickCallback);
                    DetachButtonCallback(_mobileTrustMerchantBinding.TabAuctionButton, _mobileTrustMerchantBinding.TabAuctionClickCallback);
                    DetachButtonCallback(_mobileTrustMerchantBinding.PrimaryButton, _mobileTrustMerchantBinding.PrimaryClickCallback);
                    DetachButtonCallback(_mobileTrustMerchantBinding.SellNowButton, _mobileTrustMerchantBinding.SellNowClickCallback);
                    DetachButtonCallback(_mobileTrustMerchantBinding.CollectSoldButton, _mobileTrustMerchantBinding.CollectSoldClickCallback);
                    DetachButtonCallback(_mobileTrustMerchantBinding.PutAwayButton, _mobileTrustMerchantBinding.PutAwayClickCallback);
                }
            }
            catch
            {
            }

            _mobileTrustMerchantBinding = null;
            _nextMobileTrustMerchantBindAttemptUtc = DateTime.MinValue;
            _mobileTrustMerchantBindingsDumped = false;
            _mobileTrustMerchantDirty = true;
        }

        private static void DetachButtonCallback(GButton button, EventCallback0 callback)
        {
            if (button == null || button._disposed || callback == null)
                return;

            try
            {
                button.onClick.Remove(callback);
            }
            catch
            {
            }
        }

        private static void TryBindMobileTrustMerchantWindowIfDue(string windowKey, GComponent window, string resolveInfo)
        {
            if (window == null || window._disposed)
                return;

            MobileTrustMerchantWindowBinding binding = _mobileTrustMerchantBinding;
            if (binding != null && (binding.Window == null || binding.Window._disposed))
            {
                ResetMobileTrustMerchantBindings();
                binding = null;
            }

            if (binding == null || !ReferenceEquals(binding.Window, window))
            {
                ResetMobileTrustMerchantBindings();

                binding = new MobileTrustMerchantWindowBinding
                {
                    WindowKey = windowKey,
                    Window = window,
                    ResolveInfo = resolveInfo,
                };

                _mobileTrustMerchantBinding = binding;
            }

            if (DateTime.UtcNow < _nextMobileTrustMerchantBindAttemptUtc)
                return;

            bool hasList = binding.List != null && !binding.List._disposed;
            if (hasList)
                return;

            _nextMobileTrustMerchantBindAttemptUtc = DateTime.UtcNow.AddSeconds(2);

            string listOverrideSpec = string.Empty;
            string searchInputOverrideSpec = string.Empty;
            string searchButtonOverrideSpec = string.Empty;
            string refreshButtonOverrideSpec = string.Empty;
            string prevOverrideSpec = string.Empty;
            string nextOverrideSpec = string.Empty;
            string pageLabelOverrideSpec = string.Empty;
            string tabMarketOverrideSpec = string.Empty;
            string tabConsignOverrideSpec = string.Empty;
            string tabAuctionOverrideSpec = string.Empty;
            string primaryOverrideSpec = string.Empty;
            string sellNowOverrideSpec = string.Empty;
            string collectOverrideSpec = string.Empty;
            string putAwayOverrideSpec = string.Empty;

            try
            {
                InIReader reader = TryCreateConfigReader();
                if (reader != null)
                {
                    listOverrideSpec = reader.ReadString(FairyGuiConfigSectionName, MobileTrustMerchantListConfigKey, string.Empty, writeWhenNull: false);
                    searchInputOverrideSpec = reader.ReadString(FairyGuiConfigSectionName, MobileTrustMerchantSearchInputConfigKey, string.Empty, writeWhenNull: false);
                    searchButtonOverrideSpec = reader.ReadString(FairyGuiConfigSectionName, MobileTrustMerchantSearchButtonConfigKey, string.Empty, writeWhenNull: false);
                    refreshButtonOverrideSpec = reader.ReadString(FairyGuiConfigSectionName, MobileTrustMerchantRefreshButtonConfigKey, string.Empty, writeWhenNull: false);
                    prevOverrideSpec = reader.ReadString(FairyGuiConfigSectionName, MobileTrustMerchantPrevPageButtonConfigKey, string.Empty, writeWhenNull: false);
                    nextOverrideSpec = reader.ReadString(FairyGuiConfigSectionName, MobileTrustMerchantNextPageButtonConfigKey, string.Empty, writeWhenNull: false);
                    pageLabelOverrideSpec = reader.ReadString(FairyGuiConfigSectionName, MobileTrustMerchantPageLabelConfigKey, string.Empty, writeWhenNull: false);
                    tabMarketOverrideSpec = reader.ReadString(FairyGuiConfigSectionName, MobileTrustMerchantTabMarketConfigKey, string.Empty, writeWhenNull: false);
                    tabConsignOverrideSpec = reader.ReadString(FairyGuiConfigSectionName, MobileTrustMerchantTabConsignConfigKey, string.Empty, writeWhenNull: false);
                    tabAuctionOverrideSpec = reader.ReadString(FairyGuiConfigSectionName, MobileTrustMerchantTabAuctionConfigKey, string.Empty, writeWhenNull: false);
                    primaryOverrideSpec = reader.ReadString(FairyGuiConfigSectionName, MobileTrustMerchantPrimaryButtonConfigKey, string.Empty, writeWhenNull: false);
                    sellNowOverrideSpec = reader.ReadString(FairyGuiConfigSectionName, MobileTrustMerchantSellNowButtonConfigKey, string.Empty, writeWhenNull: false);
                    collectOverrideSpec = reader.ReadString(FairyGuiConfigSectionName, MobileTrustMerchantCollectSoldButtonConfigKey, string.Empty, writeWhenNull: false);
                    putAwayOverrideSpec = reader.ReadString(FairyGuiConfigSectionName, MobileTrustMerchantPutAwayButtonConfigKey, string.Empty, writeWhenNull: false);
                }
            }
            catch
            {
            }

            binding.ListOverrideSpec = listOverrideSpec?.Trim() ?? string.Empty;
            binding.SearchInputOverrideSpec = searchInputOverrideSpec?.Trim() ?? string.Empty;
            binding.SearchButtonOverrideSpec = searchButtonOverrideSpec?.Trim() ?? string.Empty;
            binding.RefreshButtonOverrideSpec = refreshButtonOverrideSpec?.Trim() ?? string.Empty;

            GList list = null;
            string listResolveInfo = null;
            List<(int Score, GObject Target)> listCandidates = null;

            try
            {
                if (!string.IsNullOrWhiteSpace(binding.ListOverrideSpec))
                {
                    if (TryResolveMobileMainHudObjectBySpec(window, binding.ListOverrideSpec, out GObject resolved, out string[] keywords))
                    {
                        if (resolved is GList resolvedList && !resolvedList._disposed)
                        {
                            list = resolvedList;
                            listResolveInfo = DescribeObject(window, list) + " (override)";
                        }
                        else if (keywords != null && keywords.Length > 0)
                        {
                            binding.ListOverrideKeywords = keywords;
                        }
                    }
                    else
                    {
                        binding.ListOverrideKeywords = SplitKeywords(binding.ListOverrideSpec);
                    }
                }

                if (list == null)
                {
                    string[] keywords = binding.ListOverrideKeywords != null && binding.ListOverrideKeywords.Length > 0
                        ? binding.ListOverrideKeywords
                        : DefaultTrustMerchantListKeywords;

                    listCandidates = CollectMobileChatCandidates(window, obj => obj is GList, keywords, ScoreMobileShopListCandidate);
                    list = SelectMobileChatCandidate<GList>(listCandidates, minScore: 50);
                    if (list != null && !list._disposed)
                        listResolveInfo = DescribeObject(window, list) + (binding.ListOverrideKeywords != null ? " (keywords)" : " (auto)");
                }
            }
            catch
            {
                list = null;
            }

            if (list == null || list._disposed)
            {
                CMain.SaveError("FairyGUI: 信任商人窗口未找到列表（TrustMerchant）。可在 Mir2Config.ini 设置 [" + FairyGuiConfigSectionName + "] " +
                                MobileTrustMerchantListConfigKey + "=idx:... 指定列表（或 path:/name:/item:/url:/title: / 关键字列表 a|b|c）。");
                return;
            }

            binding.List = list;
            binding.ListResolveInfo = listResolveInfo;
            if (binding.ItemRenderer == null)
                binding.ItemRenderer = RenderMobileTrustMerchantListItem;

            try
            {
                binding.List.itemRenderer = binding.ItemRenderer;
            }
            catch
            {
            }

            // Optional controls - best effort
            binding.SearchInput = ResolveMobileTrustMerchantTextInput(window, binding.SearchInputOverrideSpec, DefaultTrustMerchantSearchInputKeywords, out binding.SearchInputResolveInfo);
            binding.SearchButton = ResolveMobileTrustMerchantButton(window, binding.SearchButtonOverrideSpec, DefaultTrustMerchantSearchButtonKeywords, out binding.SearchButtonResolveInfo);
            binding.RefreshButton = ResolveMobileTrustMerchantButton(window, binding.RefreshButtonOverrideSpec, DefaultTrustMerchantRefreshButtonKeywords, out binding.RefreshButtonResolveInfo);
            binding.PrevPageButton = ResolveMobileTrustMerchantButton(window, prevOverrideSpec, DefaultTrustMerchantPrevPageButtonKeywords, out _);
            binding.NextPageButton = ResolveMobileTrustMerchantButton(window, nextOverrideSpec, DefaultTrustMerchantNextPageButtonKeywords, out _);
            binding.PageLabel = ResolveMobileTrustMerchantText(window, pageLabelOverrideSpec, DefaultTrustMerchantPageLabelKeywords);
            binding.TabMarketButton = ResolveMobileTrustMerchantButton(window, tabMarketOverrideSpec, DefaultTrustMerchantTabMarketKeywords, out _);
            binding.TabConsignButton = ResolveMobileTrustMerchantButton(window, tabConsignOverrideSpec, DefaultTrustMerchantTabConsignKeywords, out _);
            binding.TabAuctionButton = ResolveMobileTrustMerchantButton(window, tabAuctionOverrideSpec, DefaultTrustMerchantTabAuctionKeywords, out _);
            binding.PrimaryButton = ResolveMobileTrustMerchantButton(window, primaryOverrideSpec, DefaultTrustMerchantPrimaryButtonKeywords, out _);
            binding.SellNowButton = ResolveMobileTrustMerchantButton(window, sellNowOverrideSpec, DefaultTrustMerchantSellNowButtonKeywords, out _);
            binding.CollectSoldButton = ResolveMobileTrustMerchantButton(window, collectOverrideSpec, DefaultTrustMerchantCollectSoldButtonKeywords, out _);
            binding.PutAwayButton = ResolveMobileTrustMerchantButton(window, putAwayOverrideSpec, DefaultTrustMerchantPutAwayButtonKeywords, out _);

            InstallTrustMerchantCallbacks(binding);

            TryDumpMobileTrustMerchantBindingsReportIfDue(binding, listCandidates ?? new List<(int Score, GObject Target)>());
            _mobileTrustMerchantDirty = true;
        }

        private static void InstallTrustMerchantCallbacks(MobileTrustMerchantWindowBinding binding)
        {
            if (binding == null)
                return;

            try
            {
                if (binding.SearchButton != null && !binding.SearchButton._disposed)
                {
                    if (binding.SearchClickCallback == null)
                        binding.SearchClickCallback = () => TrySendTrustMerchantSearch(matchOverride: null);

                    binding.SearchButton.onClick.Remove(binding.SearchClickCallback);
                    binding.SearchButton.onClick.Add(binding.SearchClickCallback);
                }
            }
            catch
            {
            }

            try
            {
                if (binding.RefreshButton != null && !binding.RefreshButton._disposed)
                {
                    if (binding.RefreshClickCallback == null)
                        binding.RefreshClickCallback = () => Network.Enqueue(new C.MarketRefresh());

                    binding.RefreshButton.onClick.Remove(binding.RefreshClickCallback);
                    binding.RefreshButton.onClick.Add(binding.RefreshClickCallback);
                }
            }
            catch
            {
            }

            try
            {
                if (binding.PrevPageButton != null && !binding.PrevPageButton._disposed)
                {
                    if (binding.PrevPageClickCallback == null)
                        binding.PrevPageClickCallback = () => TryChangeTrustMerchantPage(delta: -1);

                    binding.PrevPageButton.onClick.Remove(binding.PrevPageClickCallback);
                    binding.PrevPageButton.onClick.Add(binding.PrevPageClickCallback);
                }
            }
            catch
            {
            }

            try
            {
                if (binding.NextPageButton != null && !binding.NextPageButton._disposed)
                {
                    if (binding.NextPageClickCallback == null)
                        binding.NextPageClickCallback = () => TryChangeTrustMerchantPage(delta: 1);

                    binding.NextPageButton.onClick.Remove(binding.NextPageClickCallback);
                    binding.NextPageButton.onClick.Add(binding.NextPageClickCallback);
                }
            }
            catch
            {
            }

            try
            {
                if (binding.TabMarketButton != null && !binding.TabMarketButton._disposed)
                {
                    if (binding.TabMarketClickCallback == null)
                        binding.TabMarketClickCallback = () => TrySwitchTrustMerchantPanel(MarketPanelType.Market);

                    binding.TabMarketButton.onClick.Remove(binding.TabMarketClickCallback);
                    binding.TabMarketButton.onClick.Add(binding.TabMarketClickCallback);
                }
            }
            catch
            {
            }

            try
            {
                if (binding.TabConsignButton != null && !binding.TabConsignButton._disposed)
                {
                    if (binding.TabConsignClickCallback == null)
                        binding.TabConsignClickCallback = () => TrySwitchTrustMerchantPanel(MarketPanelType.Consign);

                    binding.TabConsignButton.onClick.Remove(binding.TabConsignClickCallback);
                    binding.TabConsignButton.onClick.Add(binding.TabConsignClickCallback);
                }
            }
            catch
            {
            }

            try
            {
                if (binding.TabAuctionButton != null && !binding.TabAuctionButton._disposed)
                {
                    if (binding.TabAuctionClickCallback == null)
                        binding.TabAuctionClickCallback = () => TrySwitchTrustMerchantPanel(MarketPanelType.Auction);

                    binding.TabAuctionButton.onClick.Remove(binding.TabAuctionClickCallback);
                    binding.TabAuctionButton.onClick.Add(binding.TabAuctionClickCallback);
                }
            }
            catch
            {
            }

            try
            {
                if (binding.PrimaryButton != null && !binding.PrimaryButton._disposed)
                {
                    if (binding.PrimaryClickCallback == null)
                        binding.PrimaryClickCallback = () => TryExecuteTrustMerchantPrimaryAction();

                    binding.PrimaryButton.onClick.Remove(binding.PrimaryClickCallback);
                    binding.PrimaryButton.onClick.Add(binding.PrimaryClickCallback);
                }
            }
            catch
            {
            }

            try
            {
                if (binding.SellNowButton != null && !binding.SellNowButton._disposed)
                {
                    if (binding.SellNowClickCallback == null)
                        binding.SellNowClickCallback = () => TryExecuteTrustMerchantSellNow();

                    binding.SellNowButton.onClick.Remove(binding.SellNowClickCallback);
                    binding.SellNowButton.onClick.Add(binding.SellNowClickCallback);
                }
            }
            catch
            {
            }

            try
            {
                if (binding.CollectSoldButton != null && !binding.CollectSoldButton._disposed)
                {
                    if (binding.CollectSoldClickCallback == null)
                        binding.CollectSoldClickCallback = () =>
                        {
                            Network.Enqueue(new C.MarketGetBack { Mode = MarketCollectionMode.Sold, AuctionID = 0 });
                            Network.Enqueue(new C.MarketRefresh());
                        };

                    binding.CollectSoldButton.onClick.Remove(binding.CollectSoldClickCallback);
                    binding.CollectSoldButton.onClick.Add(binding.CollectSoldClickCallback);
                }
            }
            catch
            {
            }

            try
            {
                if (binding.PutAwayButton != null && !binding.PutAwayButton._disposed)
                {
                    if (binding.PutAwayClickCallback == null)
                        binding.PutAwayClickCallback = () =>
                        {
                            MarketPanelType type;
                            lock (TrustMerchantGate)
                            {
                                type = _mobileTrustMerchantPanelType == MarketPanelType.Auction ? MarketPanelType.Auction : MarketPanelType.Consign;
                            }

                            GameScene.Scene?.BeginMobileMarketListing(type);
                        };

                    binding.PutAwayButton.onClick.Remove(binding.PutAwayClickCallback);
                    binding.PutAwayButton.onClick.Add(binding.PutAwayClickCallback);
                }
            }
            catch
            {
            }
        }

        private static void TryRefreshMobileTrustMerchantIfDue(bool force)
        {
            if (_stage == null || !_initialized || !_packagesLoaded)
                return;

            if (!MobileWindows.TryGetValue("TrustMerchant", out GComponent window) || window == null || window._disposed)
            {
                if (_mobileTrustMerchantBinding != null)
                    ResetMobileTrustMerchantBindings();
                return;
            }

            if (!window.visible)
                return;

            TryBindMobileTrustMerchantWindowIfDue("TrustMerchant", window, resolveInfo: null);

            MobileTrustMerchantWindowBinding binding = _mobileTrustMerchantBinding;
            if (binding == null || binding.Window == null || binding.Window._disposed)
            {
                ResetMobileTrustMerchantBindings();
                return;
            }

            if (binding.List == null || binding.List._disposed)
                return;

            bool dirty;
            MarketPanelType panelType;
            bool userMode;
            int pages;
            int page;
            int selectedIndex;
            int total;

            lock (TrustMerchantGate)
            {
                dirty = _mobileTrustMerchantDirty;
                panelType = _mobileTrustMerchantPanelType;
                userMode = _mobileTrustMerchantUserMode;
                pages = _mobileTrustMerchantPages;
                page = _mobileTrustMerchantPage;
                selectedIndex = _mobileTrustMerchantSelectedListingIndex;
                total = MobileTrustMerchantListings.Count;
            }

            if (!force && !dirty)
                return;

            lock (TrustMerchantGate)
            {
                _mobileTrustMerchantDirty = false;
            }

            int perPage = 10;
            int computedPages = pages > 0 ? pages : Math.Max(1, (total + perPage - 1) / perPage);
            if (page < 0) page = 0;
            if (page > computedPages - 1) page = computedPages - 1;

            int start = page * perPage;
            int count = Math.Max(0, Math.Min(perPage, total - start));

            try
            {
                if (binding.ItemRenderer == null)
                    binding.ItemRenderer = RenderMobileTrustMerchantListItem;

                binding.List.itemRenderer = binding.ItemRenderer;
                binding.List.numItems = count;
            }
            catch (Exception ex)
            {
                CMain.SaveError("FairyGUI: 刷新信任商人窗口失败：" + ex.Message);
                _nextMobileTrustMerchantBindAttemptUtc = DateTime.MinValue;
                lock (TrustMerchantGate)
                {
                    _mobileTrustMerchantDirty = true;
                }
            }

            try
            {
                if (binding.PageLabel != null && !binding.PageLabel._disposed)
                    binding.PageLabel.text = $"{page + 1}/{computedPages}";
            }
            catch
            {
            }

            try
            {
                if (binding.CollectSoldButton != null && !binding.CollectSoldButton._disposed)
                {
                    binding.CollectSoldButton.visible = userMode;
                    binding.CollectSoldButton.touchable = userMode;
                }
            }
            catch
            {
            }

            try
            {
                if (binding.SellNowButton != null && !binding.SellNowButton._disposed)
                {
                    bool show = !userMode && panelType == MarketPanelType.Market;
                    binding.SellNowButton.visible = show;
                    binding.SellNowButton.touchable = show;
                }
            }
            catch
            {
            }

            lock (TrustMerchantGate)
            {
                _mobileTrustMerchantPanelType = panelType;
                _mobileTrustMerchantUserMode = userMode;
                _mobileTrustMerchantPages = pages;
                _mobileTrustMerchantPage = page;
                _mobileTrustMerchantSelectedListingIndex = selectedIndex;
            }
        }

        private static void RenderMobileTrustMerchantListItem(int index, GObject itemObject)
        {
            if (itemObject == null || itemObject._disposed)
                return;

            if (itemObject is not GComponent itemRoot || itemRoot._disposed)
                return;

            int page;
            int selected;
            ClientAuction listing;
            int listingIndex;

            lock (TrustMerchantGate)
            {
                page = _mobileTrustMerchantPage;
                selected = _mobileTrustMerchantSelectedListingIndex;
                listingIndex = page * 10 + index;
                listing = listingIndex >= 0 && listingIndex < MobileTrustMerchantListings.Count ? MobileTrustMerchantListings[listingIndex] : null;
            }

            MobileTrustMerchantListingView view = GetOrCreateTrustMerchantListingView(itemRoot);
            if (view == null)
                return;

            if (listing == null || listing.Item == null || listing.Item.Info == null)
            {
                ClearTrustMerchantListingView(view);
                return;
            }

            ushort iconIndex = listing.Item.Image;
            string name = listing.Item.FriendlyName ?? listing.Item.Info.FriendlyName ?? listing.Item.Info.Name ?? string.Empty;
            string seller = listing.Seller ?? string.Empty;
            string price = listing.Price.ToString();
            string date = listing.ConsignmentDate == DateTime.MinValue ? string.Empty : listing.ConsignmentDate.ToString("yyyy-MM-dd");

            try
            {
                if (view.Name != null && !view.Name._disposed)
                    view.Name.text = name;
            }
            catch
            {
            }

            try
            {
                if (view.Seller != null && !view.Seller._disposed)
                    view.Seller.text = seller;
            }
            catch
            {
            }

            try
            {
                if (view.Price != null && !view.Price._disposed)
                    view.Price.text = price;
            }
            catch
            {
            }

            try
            {
                if (view.Date != null && !view.Date._disposed)
                    view.Date.text = date;
            }
            catch
            {
            }

            bool needsIconRefresh = !view.HasItem || view.LastIcon != iconIndex;
            if (!needsIconRefresh && view.Icon != null && !view.Icon._disposed)
            {
                try
                {
                    NTexture current = view.Icon.texture;
                    if (current == null || current.nativeTexture == null || current.nativeTexture.IsDisposed)
                        needsIconRefresh = true;
                }
                catch
                {
                    needsIconRefresh = true;
                }
            }

            if (needsIconRefresh && view.Icon != null && !view.Icon._disposed)
            {
                try
                {
                    view.Icon.showErrorSign = false;
                    view.Icon.url = string.Empty;
                    view.Icon.texture = GetOrCreateItemIconTexture(iconIndex);
                    view.LastIcon = iconIndex;
                }
                catch
                {
                }
            }

            view.HasItem = true;

            bool isSelected = listingIndex == selected;
            try
            {
                if (!view.OriginalAlphaCaptured)
                {
                    view.OriginalAlpha = view.Root.alpha;
                    view.OriginalAlphaCaptured = true;
                }

                view.Root.alpha = isSelected ? Math.Max(0.35f, view.OriginalAlpha * 0.7f) : view.OriginalAlpha;
            }
            catch
            {
            }

            try
            {
                if (view.ClickCallback != null)
                    view.Root.onClick.Remove(view.ClickCallback);

                view.ClickCallback = () => OnMobileTrustMerchantListingClicked(listingIndex);
                view.Root.onClick.Add(view.ClickCallback);
            }
            catch
            {
                view.ClickCallback = null;
            }
        }

        private static MobileTrustMerchantListingView GetOrCreateTrustMerchantListingView(GComponent itemRoot)
        {
            if (itemRoot == null || itemRoot._disposed)
                return null;

            if (itemRoot.data is MobileTrustMerchantListingView existing && existing.Root != null && !existing.Root._disposed)
                return existing;

            var view = new MobileTrustMerchantListingView
            {
                Root = itemRoot,
                HasItem = false,
                LastIcon = 0,
                OriginalAlphaCaptured = false,
            };

            try
            {
                List<(int Score, GObject Target)> iconCandidates = CollectMobileChatCandidates(itemRoot, obj => obj is GLoader, DefaultTrustMerchantItemIconKeywords, ScoreMobileShopTextCandidate);
                view.Icon = SelectMobileChatCandidate<GLoader>(iconCandidates, minScore: 10);
            }
            catch
            {
            }

            try
            {
                List<(int Score, GObject Target)> candidates = CollectMobileChatCandidates(itemRoot, obj => obj is GTextField && obj is not GTextInput, DefaultTrustMerchantItemNameKeywords, ScoreMobileShopTextCandidate);
                view.Name = SelectMobileChatCandidate<GTextField>(candidates, minScore: 20);
            }
            catch
            {
            }

            try
            {
                List<(int Score, GObject Target)> candidates = CollectMobileChatCandidates(itemRoot, obj => obj is GTextField && obj is not GTextInput, DefaultTrustMerchantItemSellerKeywords, ScoreMobileShopTextCandidate);
                view.Seller = SelectMobileChatCandidate<GTextField>(candidates, minScore: 18);
            }
            catch
            {
            }

            try
            {
                List<(int Score, GObject Target)> candidates = CollectMobileChatCandidates(itemRoot, obj => obj is GTextField && obj is not GTextInput, DefaultTrustMerchantItemPriceKeywords, ScoreMobileShopTextCandidate);
                view.Price = SelectMobileChatCandidate<GTextField>(candidates, minScore: 18);
            }
            catch
            {
            }

            try
            {
                List<(int Score, GObject Target)> candidates = CollectMobileChatCandidates(itemRoot, obj => obj is GTextField && obj is not GTextInput, DefaultTrustMerchantItemDateKeywords, ScoreMobileShopTextCandidate);
                view.Date = SelectMobileChatCandidate<GTextField>(candidates, minScore: 16);
            }
            catch
            {
            }

            itemRoot.data = view;
            return view;
        }

        private static void ClearTrustMerchantListingView(MobileTrustMerchantListingView view)
        {
            if (view == null || view.Root == null || view.Root._disposed)
                return;

            try
            {
                if (view.ClickCallback != null)
                    view.Root.onClick.Remove(view.ClickCallback);
            }
            catch
            {
            }

            view.ClickCallback = null;
            view.HasItem = false;
            view.LastIcon = 0;

            try
            {
                if (view.Icon != null && !view.Icon._disposed)
                {
                    view.Icon.showErrorSign = false;
                    view.Icon.url = string.Empty;
                    view.Icon.texture = null;
                }
            }
            catch
            {
            }

            try
            {
                if (view.Name != null && !view.Name._disposed)
                    view.Name.text = string.Empty;
            }
            catch
            {
            }

            try
            {
                if (view.Seller != null && !view.Seller._disposed)
                    view.Seller.text = string.Empty;
            }
            catch
            {
            }

            try
            {
                if (view.Price != null && !view.Price._disposed)
                    view.Price.text = string.Empty;
            }
            catch
            {
            }

            try
            {
                if (view.Date != null && !view.Date._disposed)
                    view.Date.text = string.Empty;
            }
            catch
            {
            }

            try
            {
                if (view.OriginalAlphaCaptured)
                    view.Root.alpha = view.OriginalAlpha;
            }
            catch
            {
            }
        }

        private static void OnMobileTrustMerchantListingClicked(int listingIndex)
        {
            lock (TrustMerchantGate)
            {
                _mobileTrustMerchantSelectedListingIndex = listingIndex;
                _mobileTrustMerchantDirty = true;
            }

            MobileTrustMerchantWindowBinding binding = _mobileTrustMerchantBinding;
            if (binding?.PrimaryButton == null || binding.PrimaryButton._disposed)
            {
                TryExecuteTrustMerchantPrimaryAction();
            }
            else
            {
                TryRefreshMobileTrustMerchantIfDue(force: false);
            }
        }

        private static void TryExecuteTrustMerchantPrimaryAction()
        {
            ClientAuction listing;
            bool userMode;

            lock (TrustMerchantGate)
            {
                int index = _mobileTrustMerchantSelectedListingIndex;
                listing = index >= 0 && index < MobileTrustMerchantListings.Count ? MobileTrustMerchantListings[index] : null;
                userMode = _mobileTrustMerchantUserMode;
            }

            if (listing == null)
            {
                GameScene.Scene?.OutputMessage("请先选择一件物品。");
                return;
            }

            if (userMode)
            {
                TryExecuteTrustMerchantGetBack(listing);
                return;
            }

            if (listing.ItemType == MarketItemType.Auction)
            {
                TryExecuteTrustMerchantBid(listing);
                return;
            }

            TryExecuteTrustMerchantBuy(listing);
        }

        private static void TryExecuteTrustMerchantBuy(ClientAuction listing)
        {
            if (listing == null)
                return;

            string name = listing.Item?.FriendlyName ?? "物品";
            uint price = listing.Price;

            var box = new MirMessageBox($"确定花费 {price:#,##0} 金币购买 {name} 吗？", MirMessageBoxButtons.YesNo);
            if (box.YesButton != null)
                box.YesButton.Click += (o, e) => Network.Enqueue(new C.MarketBuy { AuctionID = listing.AuctionID });
            box.Show();
        }

        private static void TryExecuteTrustMerchantBid(ClientAuction listing)
        {
            if (listing == null)
                return;

            uint minBid = listing.Price + 1;

            GameScene.Scene?.PromptMobileText(
                title: "竞拍出价",
                message: $"请输入竞拍价格（至少 {minBid:#,##0}）",
                initialText: minBid.ToString(),
                maxLength: 10,
                numericOnly: true,
                onOk: raw =>
                {
                    if (!uint.TryParse(raw, out uint bidPrice))
                        return;

                    if (bidPrice < minBid)
                        bidPrice = minBid;

                    string name = listing.Item?.FriendlyName ?? "物品";
                    var box = new MirMessageBox($"是否竞价 {name} 并支付定金 {bidPrice:#,##0} 金币？", MirMessageBoxButtons.YesNo);
                    if (box.YesButton != null)
                        box.YesButton.Click += (o, e) => Network.Enqueue(new C.MarketBuy { AuctionID = listing.AuctionID, BidPrice = bidPrice });
                    box.Show();
                });
        }

        private static void TryExecuteTrustMerchantGetBack(ClientAuction listing)
        {
            if (listing == null)
                return;

            bool needsConfirm = false;
            if (listing.ItemType == MarketItemType.Consign && string.Equals(listing.Seller, "For Sale", StringComparison.OrdinalIgnoreCase))
                needsConfirm = true;
            if (listing.ItemType == MarketItemType.Auction && string.Equals(listing.Seller, "No Bid", StringComparison.OrdinalIgnoreCase))
                needsConfirm = true;

            if (!needsConfirm)
            {
                Network.Enqueue(new C.MarketGetBack { AuctionID = listing.AuctionID });
                return;
            }

            string name = listing.Item?.FriendlyName ?? "物品";
            var box = new MirMessageBox($"{name} 尚未售出，确定要取回吗？", MirMessageBoxButtons.YesNo);
            if (box.YesButton != null)
                box.YesButton.Click += (o, e) => Network.Enqueue(new C.MarketGetBack { AuctionID = listing.AuctionID });
            box.Show();
        }

        private static void TryExecuteTrustMerchantSellNow()
        {
            ClientAuction listing;

            lock (TrustMerchantGate)
            {
                int index = _mobileTrustMerchantSelectedListingIndex;
                listing = index >= 0 && index < MobileTrustMerchantListings.Count ? MobileTrustMerchantListings[index] : null;
            }

            if (listing == null)
                return;

            Network.Enqueue(new C.MarketSellNow { AuctionID = listing.AuctionID });
        }

        private static void TrySwitchTrustMerchantPanel(MarketPanelType type)
        {
            lock (TrustMerchantGate)
            {
                _mobileTrustMerchantPanelType = type;
                _mobileTrustMerchantUserMode = type != MarketPanelType.Market;
                _mobileTrustMerchantPage = 0;
                _mobileTrustMerchantSelectedListingIndex = -1;
                MobileTrustMerchantListings.Clear();
                _mobileTrustMerchantDirty = true;
            }

            TrySendTrustMerchantSearch(matchOverride: string.Empty);
            TryRefreshMobileTrustMerchantIfDue(force: false);
        }

        private static void TrySendTrustMerchantSearch(string matchOverride)
        {
            MarketPanelType type;
            bool userMode;
            string match;

            lock (TrustMerchantGate)
            {
                type = _mobileTrustMerchantPanelType;
                userMode = type != MarketPanelType.Market;
                match = matchOverride;
            }

            if (match == null)
            {
                try
                {
                    match = _mobileTrustMerchantBinding?.SearchInput?.text ?? string.Empty;
                }
                catch
                {
                    match = string.Empty;
                }
            }

            try
            {
                Network.Enqueue(new C.MarketSearch
                {
                    Match = match ?? string.Empty,
                    Type = ItemType.杂物,
                    Usermode = userMode,
                    MinShape = 0,
                    MaxShape = 5000,
                    MarketType = type,
                });
            }
            catch (Exception ex)
            {
                CMain.SaveError("FairyGUI: 发送 MarketSearch 失败：" + ex.Message);
            }
        }

        private static void TryChangeTrustMerchantPage(int delta)
        {
            int page;
            int pages;
            int loadedPages;

            lock (TrustMerchantGate)
            {
                page = _mobileTrustMerchantPage;
                pages = _mobileTrustMerchantPages;
                loadedPages = Math.Max(1, (MobileTrustMerchantListings.Count + 9) / 10);
            }

            int next = page + delta;
            if (next < 0)
                return;

            int maxPages = pages > 0 ? pages : loadedPages;
            if (next > maxPages - 1)
                return;

            if (next < loadedPages)
            {
                lock (TrustMerchantGate)
                {
                    _mobileTrustMerchantPage = next;
                    _mobileTrustMerchantSelectedListingIndex = -1;
                    _mobileTrustMerchantDirty = true;
                }

                TryRefreshMobileTrustMerchantIfDue(force: false);
                return;
            }

            Network.Enqueue(new C.MarketPage { Page = next });
        }

        private static GTextInput ResolveMobileTrustMerchantTextInput(GComponent window, string overrideSpec, string[] defaultKeywords, out string resolveInfo)
        {
            resolveInfo = null;
            if (window == null || window._disposed)
                return null;

            try
            {
                if (!string.IsNullOrWhiteSpace(overrideSpec))
                {
                    if (TryResolveMobileMainHudObjectBySpec(window, overrideSpec, out GObject resolved, out string[] keywords))
                    {
                        if (resolved is GTextInput input && !input._disposed)
                        {
                            resolveInfo = DescribeObject(window, input) + " (override)";
                            return input;
                        }

                        if (keywords != null && keywords.Length > 0)
                            defaultKeywords = keywords;
                    }
                    else
                    {
                        defaultKeywords = SplitKeywords(overrideSpec);
                    }
                }

                List<(int Score, GObject Target)> candidates = CollectMobileChatCandidates(window, obj => obj is GTextInput, defaultKeywords, ScoreMobileShopTextCandidate);
                GTextInput selected = SelectMobileChatCandidate<GTextInput>(candidates, minScore: 40);
                if (selected != null && !selected._disposed)
                {
                    resolveInfo = DescribeObject(window, selected) + " (auto)";
                    return selected;
                }
            }
            catch
            {
            }

            return null;
        }

        private static GTextField ResolveMobileTrustMerchantText(GComponent window, string overrideSpec, string[] defaultKeywords)
        {
            if (window == null || window._disposed)
                return null;

            try
            {
                if (!string.IsNullOrWhiteSpace(overrideSpec))
                {
                    if (TryResolveMobileMainHudObjectBySpec(window, overrideSpec, out GObject resolved, out string[] keywords))
                    {
                        if (resolved is GTextField text && !text._disposed)
                            return text;

                        if (keywords != null && keywords.Length > 0)
                            defaultKeywords = keywords;
                    }
                    else
                    {
                        defaultKeywords = SplitKeywords(overrideSpec);
                    }
                }

                List<(int Score, GObject Target)> candidates = CollectMobileChatCandidates(window, obj => obj is GTextField && obj is not GTextInput, defaultKeywords, ScoreMobileShopTextCandidate);
                return SelectMobileChatCandidate<GTextField>(candidates, minScore: 30);
            }
            catch
            {
                return null;
            }
        }

        private static GButton ResolveMobileTrustMerchantButton(GComponent window, string overrideSpec, string[] defaultKeywords, out string resolveInfo)
        {
            resolveInfo = null;
            if (window == null || window._disposed)
                return null;

            try
            {
                if (!string.IsNullOrWhiteSpace(overrideSpec))
                {
                    if (TryResolveMobileMainHudObjectBySpec(window, overrideSpec, out GObject resolved, out string[] keywords))
                    {
                        if (resolved is GButton button && !button._disposed)
                        {
                            resolveInfo = DescribeObject(window, button) + " (override)";
                            return button;
                        }

                        if (keywords != null && keywords.Length > 0)
                            defaultKeywords = keywords;
                    }
                    else
                    {
                        defaultKeywords = SplitKeywords(overrideSpec);
                    }
                }

                List<(int Score, GObject Target)> candidates = CollectMobileChatCandidates(window, obj => obj is GButton, defaultKeywords, ScoreMobileShopButtonCandidate);
                GButton selected = SelectMobileChatCandidate<GButton>(candidates, minScore: 50);
                if (selected != null && !selected._disposed)
                    resolveInfo = DescribeObject(window, selected) + " (auto)";
                return selected;
            }
            catch
            {
                return null;
            }
        }

        private static void TryDumpMobileTrustMerchantBindingsReportIfDue(
            MobileTrustMerchantWindowBinding binding,
            List<(int Score, GObject Target)> listCandidates)
        {
            if (!Settings.DebugMode || _mobileTrustMerchantBindingsDumped)
                return;

            if (binding == null || binding.Window == null || binding.Window._disposed)
                return;

            try
            {
                Directory.CreateDirectory(ClientResourceLayout.RuntimeRoot);

                string path = Path.Combine(ClientResourceLayout.RuntimeRoot, "FairyGui-MobileTrustMerchantBindings.txt");

                var builder = new StringBuilder(16 * 1024);
                builder.AppendLine("FairyGUI 信任商人窗口绑定报告（TrustMerchant）");
                builder.AppendLine($"GeneratedAtUtc={DateTime.UtcNow:o}");
                builder.AppendLine($"WindowKey={binding.WindowKey ?? "TrustMerchant"}");
                builder.AppendLine($"WindowResolveInfo={binding.ResolveInfo ?? "-"}");
                builder.AppendLine();

                builder.AppendLine($"List={DescribeObject(binding.Window, binding.List)}");
                builder.AppendLine($"ListResolveInfo={binding.ListResolveInfo ?? "-"}");
                builder.AppendLine($"OverrideSpec={binding.ListOverrideSpec ?? "-"}");
                builder.AppendLine($"OverrideKeywords={(binding.ListOverrideKeywords == null ? "-" : string.Join("|", binding.ListOverrideKeywords))}");
                builder.AppendLine();

                builder.AppendLine($"SearchInput={DescribeObject(binding.Window, binding.SearchInput)}");
                builder.AppendLine($"SearchButton={DescribeObject(binding.Window, binding.SearchButton)}");
                builder.AppendLine($"RefreshButton={DescribeObject(binding.Window, binding.RefreshButton)}");
                builder.AppendLine($"PrevPageButton={DescribeObject(binding.Window, binding.PrevPageButton)}");
                builder.AppendLine($"NextPageButton={DescribeObject(binding.Window, binding.NextPageButton)}");
                builder.AppendLine($"PageLabel={DescribeObject(binding.Window, binding.PageLabel)}");
                builder.AppendLine($"TabMarket={DescribeObject(binding.Window, binding.TabMarketButton)}");
                builder.AppendLine($"TabConsign={DescribeObject(binding.Window, binding.TabConsignButton)}");
                builder.AppendLine($"TabAuction={DescribeObject(binding.Window, binding.TabAuctionButton)}");
                builder.AppendLine($"PrimaryButton={DescribeObject(binding.Window, binding.PrimaryButton)}");
                builder.AppendLine($"SellNowButton={DescribeObject(binding.Window, binding.SellNowButton)}");
                builder.AppendLine($"CollectSoldButton={DescribeObject(binding.Window, binding.CollectSoldButton)}");
                builder.AppendLine($"PutAwayButton={DescribeObject(binding.Window, binding.PutAwayButton)}");
                builder.AppendLine();

                builder.AppendLine("List Candidates(top 12):");
                int top = Math.Min(12, listCandidates?.Count ?? 0);
                for (int i = 0; i < top; i++)
                {
                    (int score, GObject target) = listCandidates[i];
                    builder.AppendLine($"  - score={score} obj={DescribeObject(binding.Window, target)}");
                }

                File.WriteAllText(path, builder.ToString(), Encoding.UTF8);
                _mobileTrustMerchantBindingsDumped = true;
                CMain.SaveLog("FairyGUI: 信任商人绑定报告已生成：" + path);
            }
            catch (Exception ex)
            {
                CMain.SaveError("FairyGUI: 写入信任商人绑定报告失败：" + ex.Message);
            }
        }

        private static void OnMobileInventorySlotClicked(int slotIndex)
        {
            MarketPanelType listingType;
            bool active;

            lock (TrustMerchantGate)
            {
                active = _mobileMarketListingSelectionActive;
                listingType = _mobileMarketListingSelectionType;
            }

            UserItem item = null;
            try
            {
                UserItem[] inventory = GameScene.User?.Inventory;
                if (inventory != null && slotIndex >= 0 && slotIndex < inventory.Length)
                    item = inventory[slotIndex];
            }
            catch
            {
                item = null;
            }

            if (!active)
            {
                if (TryHandleMobileMailAttachmentSelection(slotIndex))
                    return;

                if (item == null || item.Info == null)
                {
                    HideMobileItemTips();
                    return;
                }

                if (IsMobileItemLocked(item.UniqueID))
                {
                    GameScene.Scene?.OutputMessage("物品已锁定，无法操作。");
                    return;
                }

                // 点击使用：消耗品
                bool storageVisible = false;
                try { storageVisible = IsMobileWindowVisible("Storage"); } catch { storageVisible = false; }
                if (storageVisible)
                {
                    if (!IsMobileInventorySlotDoubleTap(slotIndex))
                    {
                        ShowMobileItemTips(item);
                        return;
                    }

                    try
                    {
                        UserItem[] inventory = GameScene.User?.Inventory;
                        UserItem[] storage = GameScene.Storage;

                        int preferredStorageIndex = -1;
                        try
                        {
                            MobileStorageWindowBinding storageBinding = _mobileStorageBinding;
                            if (storageBinding != null && storageBinding.SelectedGrid == MirGridType.Storage && storageBinding.SelectedIndex >= 0)
                                preferredStorageIndex = storageBinding.SelectedIndex;
                        }
                        catch
                        {
                            preferredStorageIndex = -1;
                        }

                        if (inventory != null && storage != null)
                        {
                            if (TryMoveInventoryItemToStorage(inventory, slotIndex, storage, preferredStorageIndex))
                            {
                                HideMobileItemTips();
                                return;
                            }
                        }
                    }
                    catch
                    {
                    }

                    try { GameScene.Scene?.OutputMessage("无法存入仓库。"); } catch { }
                    return;
                }

                if (!IsMobileInventorySlotDoubleTap(slotIndex))
                {
                    ShowMobileItemTips(item);
                    return;
                }

                if (TryUseMobileInventoryItem(item))
                {
                    HideMobileItemTips();
                    return;
                }

                // 点击穿戴：装备/饰品
                if (TryEquipMobileInventoryItem(item))
                {
                    HideMobileItemTips();
                    return;
                }

                // 其他物品：显示 Tips
                ShowMobileItemTips(item);
                return;
            }

            if (item == null || item.Info == null)
            {
                GameScene.Scene?.OutputMessage("请选择背包中的物品。");
                return;
            }

            if (IsMobileItemLocked(item.UniqueID))
            {
                GameScene.Scene?.OutputMessage("物品已锁定，无法上架。");
                return;
            }

            MarketPanelType typeToSend = listingType == MarketPanelType.Auction ? MarketPanelType.Auction : MarketPanelType.Consign;
            uint min = typeToSend == MarketPanelType.Auction ? Globals.MinStartingBid : Globals.MinConsignment;
            uint max = typeToSend == MarketPanelType.Auction ? Globals.MaxStartingBid : Globals.MaxConsignment;
            string title = typeToSend == MarketPanelType.Auction ? "拍卖起拍价" : "寄售价格";
            ulong uniqueId = item.UniqueID;

            GameScene.Scene?.PromptMobileText(
                title: title,
                message: $"请输入价格（{min:#,##0}-{max:#,##0}）",
                initialText: min.ToString(),
                maxLength: 10,
                numericOnly: true,
                onOk: raw =>
                {
                    if (!uint.TryParse(raw, out uint price))
                        return;

                    if (price < min)
                        price = min;
                    if (price > max)
                        price = max;

                    try
                    {
                        Network.Enqueue(new C.ConsignItem { UniqueID = uniqueId, Price = price, Type = typeToSend });
                        lock (TrustMerchantGate)
                        {
                            _mobileMarketListingSelectionActive = false;
                        }
                    }
                    catch (Exception ex)
                    {
                        CMain.SaveError("FairyGUI: 发送上架请求失败：" + ex.Message);
                    }
                });
        }

        private static bool IsMobileTipsEquipmentItem(UserItem item)
        {
            if (item == null || item.Info == null)
                return false;

            try
            {
                return item.Info.Type == ItemType.Weapon
                       || item.Info.Type == ItemType.Armour
                       || item.Info.Type == ItemType.Helmet
                       || item.Info.Type == ItemType.Torch
                       || item.Info.Type == ItemType.Necklace
                       || item.Info.Type == ItemType.Bracelet
                       || item.Info.Type == ItemType.Ring
                       || item.Info.Type == ItemType.Amulet
                       || item.Info.Type == ItemType.Belt
                       || item.Info.Type == ItemType.Boots
                       || item.Info.Type == ItemType.Stone
                       || item.Info.Type == ItemType.Mount
                       || item.Info.Type == ItemType.Mask
                       || item.Info.Type == ItemType.Deco;
            }
            catch
            {
                return false;
            }
        }
    }
}
