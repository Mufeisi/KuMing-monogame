using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Globalization;
using C = ClientPackets;
using DrawingRectangle = System.Drawing.Rectangle;
using FairyGUI;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using MonoShare.MirGraphics;
using MonoShare.MirScenes;

namespace MonoShare
{
    internal static partial class FairyGuiHost
    {
        private const string FairyGuiConfigSectionName = "FairyGUI";
        private const string MobileMainHudMiniMapConfigKey = "MobileMainHud.MiniMap";
        private const string MobileWindowComponentConfigKeyPrefix = "MobileWindow.";
        private const string MobileWindowCloseButtonConfigKeyPrefix = "MobileWindowClose.";
        private const string MobileChatInputConfigKey = "MobileChat.Input";
        private const string MobileChatSendConfigKey = "MobileChat.Send";
        private const string MobileChatLogConfigKey = "MobileChat.Log";
        private const int MobileChatMaxLines = 120;
        private const string MobileInventoryGridConfigKey = "MobileInventory.Grid";
        private const string MobileInventoryWarehouseButtonConfigKey = "MobileInventory.WarehouseButton";
        private const string MobileInventorySortButtonConfigKey = "MobileInventory.SortButton";
        private const string MobileStorageInventoryGridConfigKey = "MobileStorage.InventoryGrid";
        private const string MobileStorageStorageGridConfigKey = "MobileStorage.StorageGrid";
        private const string MobileMagicGridConfigKey = "MobileMagic.Grid";
        private const string MobileShopListConfigKey = "MobileShop.List";
        private const string MobileFriendListConfigKey = "MobileFriend.List";
        private const string MobileFriendAddInputConfigKey = "MobileFriend.AddInput";
        private const string MobileFriendAddButtonConfigKey = "MobileFriend.AddButton";
        private const string MobileFriendRemoveButtonConfigKey = "MobileFriend.RemoveButton";
        private const string MobileFriendBlockButtonConfigKey = "MobileFriend.BlockButton";
        private const string MobileFriendMemoInputConfigKey = "MobileFriend.MemoInput";
        private const string MobileFriendMemoButtonConfigKey = "MobileFriend.MemoButton";
        private const string MobileFriendRefreshButtonConfigKey = "MobileFriend.RefreshButton";
        private const string MobileGuildNoticeConfigKey = "MobileGuild.Notice";
        private const string MobileGuildMemberListConfigKey = "MobileGuild.MemberList";
        private const string MobileGuildInviteInputConfigKey = "MobileGuild.InviteInput";
        private const string MobileGuildInviteButtonConfigKey = "MobileGuild.InviteButton";
        private const string MobileGuildKickButtonConfigKey = "MobileGuild.KickButton";
        private const string MobileGuildNameConfigKey = "MobileGuild.Name";
        private const string MobileGuildRankConfigKey = "MobileGuild.Rank";
        private const string MobileGuildLevelConfigKey = "MobileGuild.Level";
        private const string MobileGuildExpConfigKey = "MobileGuild.Exp";
        private const string MobileGuildExpBarConfigKey = "MobileGuild.ExpBar";
        private const string MobileGuildMemberCountConfigKey = "MobileGuild.MemberCount";
        private const string MobileGuildGoldConfigKey = "MobileGuild.Gold";
        private const string MobileGuildPointsConfigKey = "MobileGuild.Points";
        private const string MobileGuildOptionsConfigKey = "MobileGuild.Options";
        private const string MobileGroupMemberListConfigKey = "MobileGroup.MemberList";
        private const string MobileGroupInviteInputConfigKey = "MobileGroup.InviteInput";
        private const string MobileGroupInviteButtonConfigKey = "MobileGroup.InviteButton";
        private const string MobileGroupKickButtonConfigKey = "MobileGroup.KickButton";
        private const string MobileGroupLeaveButtonConfigKey = "MobileGroup.LeaveButton";
        private const string MobileSystemVolumeConfigKey = "MobileSystem.Volume";
        private const string MobileSystemMusicConfigKey = "MobileSystem.Music";
        private const string MobileSystemEffectConfigKey = "MobileSystem.Effect";
        private const string MobileSystemLevelEffectConfigKey = "MobileSystem.LevelEffect";
        private const string MobileSystemDropViewConfigKey = "MobileSystem.DropView";
        private const string MobileSystemNameViewConfigKey = "MobileSystem.NameView";
        private const string MobileSystemHpViewConfigKey = "MobileSystem.HPView";
        private const string MobileSystemTransparentChatConfigKey = "MobileSystem.TransparentChat";
        private const string MobileSystemDisplayDamageConfigKey = "MobileSystem.DisplayDamage";
        private const string MobileSystemTargetDeadConfigKey = "MobileSystem.TargetDead";
        private const string MobileSystemExpandedBuffWindowConfigKey = "MobileSystem.ExpandedBuffWindow";

        private const string MobileMainHudPackageName = "UI";
        private const string MobileMainHudComponentNameNormal = "主界面_MainUI";
        private const string MobileMainHudComponentNameNotch = "主界面刘海屏_MainUI";

        private static readonly object InitGate = new object();
        private static Stage _stage;
        private static FairyGuiPublishResourceLoader _loader;
        private static FairyGuiUiManager _uiManager;
        private static bool _initialized;
        private static string _initError;

        private static readonly string[] DefaultPackageNames =
        {
            "BaseRes",
            "UIRes",
            "Font",
            "FormId",
            "Sounds",
            "UILoading",
            "UILoadingRes",
            "UI",
            "自定义组件",
        };

        private const string MobileDefaultPackageBootstrapName = "fui-retro";
        private static readonly string MobileDefaultPackageProbeDirectory = Path.Combine("Assets", "UI", "\u590d\u53e4");
        private static readonly string[] MobileDefaultPackageProbeFiles =
        {
            "BaseRes_fui.bytes",
            "UIRes_fui.bytes",
            "Font_fui.bytes",
            "FormId_fui.bytes",
            "Sounds_fui.bytes",
            "UILoading_fui.bytes",
            "UILoadingRes_fui.bytes",
            "UI_fui.bytes",
            "\u81ea\u5b9a\u4e49\u7ec4\u4ef6_fui.bytes",
        };

        private static bool _packagesLoaded;
        private static DateTime _nextPackageLoadAttemptUtc = DateTime.MinValue;
        private static string _lastPackageLoadError;
        private static bool _packageComponentListDumped;
        private static DateTime _nextMobileDefaultPackageWaitLogUtc = DateTime.MinValue;
        private static string _lastMobileDefaultPackageWaitReason;

        private static GComponent _mobileMainHudSafeAreaRoot;
        private static GComponent _mobileMainHud;
        private static MobileMainHudController _mobileMainHudController;
        private static DrawingRectangle _mobileMainHudSafeAreaBounds;
        private static bool _mobileMainHudTreeDumped;
        private static bool _mobileMainHudClickLoggerInstalled;
        private static readonly EventCallback1 MobileMainHudClickLogger = OnStageClickForDebug;
        private static int _mobileMainHudCreateTraceDepth;

        internal static bool MobileMainHudCreateTraceActive => _mobileMainHudCreateTraceDepth > 0;

        private static GComponent _mobileOverlaySafeAreaRoot;
        private static DrawingRectangle _mobileOverlaySafeAreaBounds;

        private static readonly string[] DefaultMiniMapKeywords = { "小地图", "minimap", "mini_map", "mini-map", "MiniMap" };
        private static bool _mobileMiniMapLocatorInitialized;
        private static string _mobileMiniMapOverrideSpec;
        private static string[] _mobileMiniMapKeywords;
        private static GObject _mobileMiniMapRoot;
        private static bool _mobileMiniMapLocatorLogged;

        private static readonly string[] DefaultWindowCloseButtonKeywords =
        {
            "closeButton",
            "CloseButton",
            "CloseBtn",
            "BtnClose2",
            "BtnClose3",
            "BtnClose",
            "BtnClose1",
            "BtnColose",
            "DBtnStateColse",
            "DBagClose",
            "QEquipBtnClose",
            "NewNpcDCloseBtn",
            "关闭",
            "叉",
            "close",
        };

        private readonly struct MobileChatEntry
        {
            public readonly DateTime TimeUtc;
            public readonly ChatType Type;
            public readonly string Message;

            public MobileChatEntry(DateTime timeUtc, ChatType type, string message)
            {
                TimeUtc = timeUtc;
                Type = type;
                Message = message;
            }
        }

        private static readonly List<MobileChatEntry> MobileChatEntries = new List<MobileChatEntry>(256);
        private static bool _mobileChatDirty;

        private static GComponent _mobileChatWindow;
        private static GTextInput _mobileChatInput;
        private static GButton _mobileChatSendButton;
        private static GTextField _mobileChatLogField;
        private static string _mobileChatWindowResolveInfo;

        private static string _mobileChatInputOverrideSpec;
        private static string _mobileChatSendOverrideSpec;
        private static string _mobileChatLogOverrideSpec;

        private static EventCallback0 _mobileChatSendClickCallback;
        private static EventCallback0 _mobileChatSubmitCallback;
        private static DateTime _nextMobileChatBindAttemptUtc = DateTime.MinValue;
        private static bool _mobileChatBindingsDumped;

        private static readonly Dictionary<ChatType, string> ChatTypeTagCache = new Dictionary<ChatType, string>();
        private static readonly string[] DefaultChatLogKeywords = { "聊天", "chat", "msg", "message", "记录", "log", "history", "content", "内容" };
        private static readonly string[] DefaultChatInputKeywords = { "输入", "input", "edit", "文本", "text" };
        private static readonly string[] DefaultChatSendKeywords = { "发送", "send", "btnsend", "sendbtn" };

        private sealed class MobileItemSlotBinding
        {
            public int SlotIndex;
            public GComponent Root;
            public GLoader Icon;
            public GImage IconImage;
            public GTextField Count;
            public GObject LockedMarker;
            public EventCallback0 ClickCallback;
            public EventCallback1 DropCallback;
            public MobileLongPressTipBinding LongPressTipBinding;
            public MobileLongPressDragBinding LongPressDragBinding;
            public float OriginalAlpha;
            public bool OriginalAlphaCaptured;
            public bool OriginalGrayed;
            public bool OriginalGrayedCaptured;

            public bool HasItem;
            public bool IsLocked;
            public ushort LastIcon;
            public ushort LastCountDisplayed;
        }

        private sealed class MobileItemGridBinding
        {
            public string WindowKey;
            public GComponent Window;
            public GComponent GridRoot;
            public string ResolveInfo;
            public string GridResolveInfo;
            public string OverrideSpec;
            public string[] OverrideKeywords;

            public int SlotIndexOffset;
            public int CurrentPage;
            public int PageSize;
            public readonly GButton[] PageTabs = new GButton[5];
            public readonly EventCallback0[] PageTabClickCallbacks = new EventCallback0[5];

            public GButton WarehouseButton;
            public string WarehouseButtonResolveInfo;
            public string WarehouseButtonOverrideSpec;
            public string[] WarehouseButtonOverrideKeywords;
            public EventCallback0 WarehouseButtonClickCallback;

            public GButton SortButton;
            public string SortButtonResolveInfo;
            public string SortButtonOverrideSpec;
            public string[] SortButtonOverrideKeywords;
            public EventCallback0 SortButtonClickCallback;

            public GTextField GoldText;
            public string GoldTextResolveInfo;

            public readonly List<MobileItemSlotBinding> Slots = new List<MobileItemSlotBinding>(96);
        }

        private static MobileItemGridBinding _mobileInventoryBinding;
        private static DateTime _nextMobileInventoryBindAttemptUtc = DateTime.MinValue;
        private static bool _mobileInventoryBindingsDumped;
        private static DateTime _nextMobileInventoryStorageSideBySideLayoutUtc = DateTime.MinValue;
        private static bool _mobileInventoryStorageSideBySideLayoutApplied;
        private static readonly Dictionary<int, NTexture> ItemIconTextureCache = new Dictionary<int, NTexture>();
        private static readonly Dictionary<int, NTexture> MagicIconTextureCache = new Dictionary<int, NTexture>();

        private const byte MobileItemLockFlagMail = 1;
        private const byte MobileItemLockFlagAwakening = 2;
        private static readonly object MobileItemLockGate = new object();
        private static readonly Dictionary<ulong, byte> MobileItemLocks = new Dictionary<ulong, byte>();
        private static DateTime _mobileMailLockAutoClearUtc = DateTime.MinValue;

        private static readonly string[] DefaultInventoryLockedMarkerKeywords = { "lock", "locked", "锁", "锁定", "maillock" };
        private static readonly string[] DefaultInventorySlotComponentKeywords = { "GameItemCell", "ItemCell", "GridItem", "bagItem", "DBagItem", "DItem", "WHouseItem", "DWHouseItem", "HouseItem", "StorageItem" };
        private static readonly string[] DefaultInventoryGridKeywords = { "bag", "inventory", "item", "grid", "cell", "物品", "格子", "背包" };
        private static readonly string[] DefaultInventoryWarehouseButtonKeywords = { "storage", "warehouse", "bank", "stash", "vault", "仓库", "存放", "寄存", "DRBStorage" };
        private static readonly string[] DefaultInventorySortButtonKeywords = { "sort", "arrange", "refresh", "整理", "整理包裹", "DBtnQueryBagItems", "query", "bag" };
        private static readonly string[] DefaultStorageInventoryGridKeywords = { "bag", "inventory", "背包", "物品", "格子" };
        private static readonly string[] DefaultStorageStorageGridKeywords = { "storage", "store", "stash", "bank", "warehouse", "box", "vault", "仓库", "存放", "寄存" };
        private static readonly string[] DefaultInventoryIconKeywords = { "icon", "img", "image", "item", "goods", "物品", "道具", "图标" };
        private static readonly string[] DefaultInventoryCountKeywords = { "num", "count", "cnt", "数量", "个数", "叠加" };
        private static readonly string[] DefaultMagicGridKeywords = { "skill", "magic", "spell", "技能", "法术", "列表", "list", "grid" };
        private static readonly string[] DefaultMagicSlotComponentKeywords = { "SkillCell", "MagicCell", "SpellCell", "SkillItem", "SkillBtn", "SkillButton" };
        private static readonly string[] DefaultMagicNameKeywords = { "name", "skill", "magic", "spell", "技能", "法术", "名称", "名字" };
        private static readonly string[] DefaultMagicLevelKeywords = { "lv", "level", "lvl", "等级", "级别" };
        private static readonly string[] DefaultShopListKeywords = { "shop", "store", "mall", "goods", "item", "list", "grid", "商品", "道具", "列表", "商城", "商店" };
        private static readonly string[] DefaultShopItemIconKeywords = { "icon", "img", "image", "item", "goods", "商品", "道具", "图标" };
        private static readonly string[] DefaultShopItemNameKeywords = { "name", "item", "goods", "商品", "道具", "名称", "名字" };
        private static readonly string[] DefaultShopItemCountKeywords = { "count", "cnt", "num", "数量", "个数", "叠加", "x" };
        private static readonly string[] DefaultShopItemStockKeywords = { "stock", "库存", "剩余", "余量", "limit", "限制" };
        private static readonly string[] DefaultShopItemPriceGoldKeywords = { "gold", "元宝", "金币", "金", "goldprice", "pricegold" };
        private static readonly string[] DefaultShopItemPriceCreditKeywords = { "credit", "点券", "钻石", "充值", "money", "rmb", "creditprice", "pricecredit" };
        private static readonly string[] DefaultShopBuyKeywords = { "buy", "purchase", "购买", "兑换", "buybtn", "btnbuy", "shopbuy" };
        private static readonly string[] DefaultShopBuyGoldKeywords = { "gold", "元宝", "金币", "金" };
        private static readonly string[] DefaultShopBuyCreditKeywords = { "credit", "点券", "钻石", "充值", "money", "rmb" };
        private static readonly string[] DefaultFriendListKeywords = { "friend", "friends", "好友", "列表", "list", "grid" };
        private static readonly string[] DefaultFriendAddInputKeywords = { "add", "name", "friend", "好友", "输入", "角色名" };
        private static readonly string[] DefaultFriendAddButtonKeywords = { "add", "添加", "新增", "确认", "ok" };
        private static readonly string[] DefaultFriendRemoveButtonKeywords = { "remove", "delete", "del", "删除", "移除" };
        private static readonly string[] DefaultFriendBlockButtonKeywords = { "block", "black", "blocked", "拉黑", "黑名单", "屏蔽" };
        private static readonly string[] DefaultFriendMemoInputKeywords = { "memo", "note", "备注", "签名" };
        private static readonly string[] DefaultFriendMemoButtonKeywords = { "memo", "note", "save", "保存", "确认" };
        private static readonly string[] DefaultFriendRefreshButtonKeywords = { "refresh", "update", "刷新", "更新" };
        private static readonly string[] DefaultFriendItemNameKeywords = { "name", "friend", "好友", "名称", "名字" };
        private static readonly string[] DefaultFriendItemMemoKeywords = { "memo", "note", "备注", "签名" };
        private static readonly string[] DefaultFriendItemStatusKeywords = { "online", "offline", "status", "state", "在线", "离线", "状态" };
        private static readonly string[] DefaultFriendItemBlockedKeywords = { "block", "black", "blocked", "黑名单", "拉黑", "屏蔽" };
        private static readonly string[] DefaultGuildNoticeKeywords = { "notice", "公告", "通知", "介绍", "宣言" };
        private static readonly string[] DefaultGuildMemberListKeywords = { "member", "members", "guild", "成员", "列表", "list", "grid", "工会" };
        private static readonly string[] DefaultGuildInviteInputKeywords = { "invite", "recruit", "name", "输入", "角色名", "邀请", "招募" };
        private static readonly string[] DefaultGuildInviteButtonKeywords = { "invite", "recruit", "add", "邀请", "招募", "添加" };
        private static readonly string[] DefaultGuildKickButtonKeywords = { "kick", "remove", "delete", "踢", "开除", "除名", "删除" };
        private static readonly string[] DefaultGuildMemberNameKeywords = { "name", "member", "成员", "名字", "名称" };
        private static readonly string[] DefaultGuildMemberRankKeywords = { "rank", "职位", "阶位", "官阶" };
        private static readonly string[] DefaultGuildMemberStatusKeywords = { "online", "offline", "status", "state", "在线", "离线", "状态" };
        private static readonly string[] DefaultGuildNameKeywords = { "guildname", "guild", "行会", "工会", "名称", "名字", "name" };
        private static readonly string[] DefaultGuildRankNameKeywords = { "guildrank", "rank", "title", "职位", "官阶", "称号" };
        private static readonly string[] DefaultGuildLevelKeywords = { "guildlevel", "level", "lv", "lvl", "等级", "级别" };
        private static readonly string[] DefaultGuildExpKeywords = { "guildexp", "exp", "experience", "经验", "进度", "progress" };
        private static readonly string[] DefaultGuildExpBarKeywords = { "guildexp", "exp", "experience", "经验", "进度", "progress" };
        private static readonly string[] DefaultGuildMemberCountKeywords = { "membercount", "members", "人数", "成员数", "上限", "数量" };
        private static readonly string[] DefaultGuildGoldKeywords = { "guildgold", "gold", "fund", "money", "资金", "基金", "金币" };
        private static readonly string[] DefaultGuildPointsKeywords = { "point", "points", "spare", "点数", "积分", "剩余" };
        private static readonly string[] DefaultGuildOptionsKeywords = { "option", "options", "perm", "permission", "权限", "权力", "操作权限" };
        private static readonly string[] DefaultGroupMemberListKeywords = { "group", "team", "member", "成员", "队伍", "组队", "列表", "list", "grid" };
        private static readonly string[] DefaultGroupInviteInputKeywords = { "name", "add", "invite", "输入", "角色名", "邀请" };
        private static readonly string[] DefaultGroupInviteButtonKeywords = { "add", "invite", "邀请", "添加", "组队" };
        private static readonly string[] DefaultGroupKickButtonKeywords = { "kick", "del", "remove", "踢", "移除", "删除" };
        private static readonly string[] DefaultGroupLeaveButtonKeywords = { "leave", "退出", "离开", "解散", "关闭组队" };
        private static readonly string[] DefaultGroupMemberNameKeywords = { "name", "member", "成员", "名字", "名称" };
        private static readonly string[] DefaultGroupMemberMapKeywords = { "map", "地图" };
        private static readonly string[] DefaultGroupMemberLocationKeywords = { "loc", "pos", "location", "坐标", "位置", "x", "y" };
        private static readonly string[] DefaultSystemVolumeKeywords = { "sound", "snd", "sfx", "音效", "音量", "声音" };
        private static readonly string[] DefaultSystemMusicKeywords = { "music", "bgm", "musc", "音乐", "背景", "背景音" };
        private static readonly string[] DefaultSystemEffectKeywords = { "effect", "fx", "特效", "效果" };
        private static readonly string[] DefaultSystemLevelEffectKeywords = { "level", "level_effect", "等级", "等级特效" };
        private static readonly string[] DefaultSystemDropViewKeywords = { "drop", "dropview", "掉落", "物品显示" };
        private static readonly string[] DefaultSystemNameViewKeywords = { "name", "nameview", "名字", "姓名", "名称" };
        private static readonly string[] DefaultSystemHpViewKeywords = { "hp", "mp", "hpmp", "hpview", "血条", "蓝条", "血量", "魔法" };
        private static readonly string[] DefaultSystemTransparentChatKeywords = { "transparent", "chat", "透明", "聊天透明" };
        private static readonly string[] DefaultSystemDisplayDamageKeywords = { "damage", "dmg", "伤害", "飘字", "伤害显示" };
        private static readonly string[] DefaultSystemTargetDeadKeywords = { "targetdead", "目标死亡", "死亡提示", "目标死" };
        private static readonly string[] DefaultSystemExpandedBuffWindowKeywords = { "buff", "expanded", "buffwindow", "状态", "buff窗口", "扩展buff" };

        private sealed class MobileStorageWindowBinding
        {
            public GComponent Window;
            public string ResolveInfo;

            public MobileItemGridBinding InventoryGrid;
            public MobileItemGridBinding StorageGrid;

            public int SelectedIndex = -1;
            public MirGridType SelectedGrid = MirGridType.Inventory;
        }

        private static MobileStorageWindowBinding _mobileStorageBinding;
        private static DateTime _nextMobileStorageBindAttemptUtc = DateTime.MinValue;
        private static bool _mobileStorageBindingsDumped;

        private sealed class MobileSystemToggleBinding
        {
            public string Key;
            public string DisplayName;
            public string ConfigKey;
            public string[] DefaultKeywords;

            public GButton Button;
            public string ResolveInfo;
            public string OverrideSpec;
            public string[] OverrideKeywords;

            public EventCallback0 ChangedCallback;
        }

        private sealed class MobileSystemWindowBinding
        {
            public string WindowKey;
            public GComponent Window;
            public string ResolveInfo;

            public bool Syncing;

            public GSlider VolumeSlider;
            public string VolumeResolveInfo;
            public string VolumeOverrideSpec;
            public string[] VolumeOverrideKeywords;
            public EventCallback0 VolumeChangedCallback;
            public EventCallback0 VolumeGripTouchEndCallback;

            public GSlider MusicSlider;
            public string MusicResolveInfo;
            public string MusicOverrideSpec;
            public string[] MusicOverrideKeywords;
            public EventCallback0 MusicChangedCallback;
            public EventCallback0 MusicGripTouchEndCallback;

            public readonly List<MobileSystemToggleBinding> Toggles = new List<MobileSystemToggleBinding>(12);
        }

        private static MobileSystemWindowBinding _mobileSystemBinding;
        private static DateTime _nextMobileSystemBindAttemptUtc = DateTime.MinValue;
        private static DateTime _nextMobileSystemSyncUtc = DateTime.MinValue;
        private static bool _mobileSystemBindingsDumped;
        private static DateTime _nextMobileSystemSaveUtc = DateTime.MinValue;

        private sealed class MobileMagicSlotBinding
        {
            public int SlotIndex;
            public GComponent Root;
            public GLoader Icon;
            public GImage IconImage;
            public GTextField Name;
            public GTextField Level;
            public EventCallback0 ClickCallback;
            public EventCallback1 DropCallback;
            public MobileLongPressMagicDragBinding LongPressDragBinding;

            public bool HasMagic;
            public byte LastIcon;
            public byte LastLevel;
            public string LastName;
        }

        private sealed class MobileMagicWindowBinding
        {
            public string WindowKey;
            public GComponent Window;
            public GComponent GridRoot;
            public string ResolveInfo;
            public string GridResolveInfo;
            public string OverrideSpec;
            public string[] OverrideKeywords;

            public readonly List<MobileMagicSlotBinding> Slots = new List<MobileMagicSlotBinding>(64);
        }

        private static MobileMagicWindowBinding _mobileMagicBinding;
        private static DateTime _nextMobileMagicBindAttemptUtc = DateTime.MinValue;
        private static bool _mobileMagicBindingsDumped;

        private sealed class MobileShopItemView
        {
            public GComponent Root;

            public GLoader Icon;
            public GTextField Name;
            public GTextField Count;
            public GTextField Stock;
            public GTextField GoldPrice;
            public GTextField CreditPrice;

            public bool HasItem;
            public ushort LastIcon;

            public GButton BuyGoldButton;
            public EventCallback0 BuyGoldCallback;

            public GButton BuyCreditButton;
            public EventCallback0 BuyCreditCallback;

            public GButton BuyFallbackButton;
            public EventCallback0 BuyFallbackCallback;
        }

        private sealed class MobileShopWindowBinding
        {
            public string WindowKey;
            public GComponent Window;
            public string ResolveInfo;

            public GList ItemList;
            public string ItemListResolveInfo;
            public string ItemListOverrideSpec;
            public string[] ItemListOverrideKeywords;

            public ListItemRenderer ItemRenderer;
        }

        private static MobileShopWindowBinding _mobileShopBinding;
        private static DateTime _nextMobileShopBindAttemptUtc = DateTime.MinValue;
        private static bool _mobileShopBindingsDumped;
        private static bool _mobileShopDirty;

        private static readonly List<ClientFriend> MobileFriendEntries = new List<ClientFriend>(128);

        private sealed class MobileFriendItemView
        {
            public GComponent Root;
            public int Index;

            public GTextField Name;
            public GTextField Memo;
            public GTextField Status;
            public GTextField Blocked;

            public EventCallback0 ClickCallback;
        }

        private sealed class MobileFriendWindowBinding
        {
            public string WindowKey;
            public GComponent Window;
            public string ResolveInfo;

            public GList FriendList;
            public string FriendListResolveInfo;
            public string FriendListOverrideSpec;
            public string[] FriendListOverrideKeywords;
            public ListItemRenderer FriendItemRenderer;

            public int SelectedIndex = -1;
            public int SelectedFriendCharacterIndex = -1;

            public GTextInput AddInput;
            public string AddInputOverrideSpec;
            public string[] AddInputOverrideKeywords;
            public GButton AddButton;
            public string AddButtonOverrideSpec;
            public string[] AddButtonOverrideKeywords;
            public EventCallback0 AddClickCallback;

            public GButton RemoveButton;
            public string RemoveButtonOverrideSpec;
            public string[] RemoveButtonOverrideKeywords;
            public EventCallback0 RemoveClickCallback;

            public GButton BlockButton;
            public string BlockButtonOverrideSpec;
            public string[] BlockButtonOverrideKeywords;
            public EventCallback0 BlockClickCallback;

            public GTextInput MemoInput;
            public string MemoInputOverrideSpec;
            public string[] MemoInputOverrideKeywords;
            public GButton MemoButton;
            public string MemoButtonOverrideSpec;
            public string[] MemoButtonOverrideKeywords;
            public EventCallback0 MemoClickCallback;

            public GButton RefreshButton;
            public string RefreshButtonOverrideSpec;
            public string[] RefreshButtonOverrideKeywords;
            public EventCallback0 RefreshClickCallback;
        }

        private static MobileFriendWindowBinding _mobileFriendBinding;
        private static DateTime _nextMobileFriendBindAttemptUtc = DateTime.MinValue;
        private static bool _mobileFriendBindingsDumped;
        private static bool _mobileFriendDirty;

        private static readonly List<string> MobileGuildNoticeLines = new List<string>(64);
        private static readonly List<GuildRank> MobileGuildRanks = new List<GuildRank>(32);

        private sealed class MobileGuildMemberEntry
        {
            public string Name;
            public string RankName;
            public bool Online;
            public DateTime LastLogin;
            public int Id;
        }

        private static readonly List<MobileGuildMemberEntry> MobileGuildMembers = new List<MobileGuildMemberEntry>(128);

        private sealed class MobileGuildMemberItemView
        {
            public GComponent Root;
            public int Index;

            public GTextField Name;
            public GTextField Rank;
            public GTextField Status;
            public GTextField LastLogin;

            public EventCallback0 ClickCallback;
        }

        private sealed class MobileGuildWindowBinding
        {
            public string WindowKey;
            public GComponent Window;
            public string ResolveInfo;

            public GTextField NoticeField;
            public string NoticeResolveInfo;
            public string NoticeOverrideSpec;
            public string[] NoticeOverrideKeywords;

            public GList MemberList;
            public string MemberListResolveInfo;
            public string MemberListOverrideSpec;
            public string[] MemberListOverrideKeywords;
            public ListItemRenderer MemberItemRenderer;

            public int SelectedIndex = -1;

            public GTextInput InviteInput;
            public string InviteInputOverrideSpec;
            public string[] InviteInputOverrideKeywords;
            public GButton InviteButton;
            public string InviteButtonOverrideSpec;
            public string[] InviteButtonOverrideKeywords;
            public EventCallback0 InviteClickCallback;

            public GButton KickButton;
            public string KickButtonOverrideSpec;
            public string[] KickButtonOverrideKeywords;
            public EventCallback0 KickClickCallback;

            public GTextField StatusGuildName;
            public string StatusGuildNameResolveInfo;
            public string StatusGuildNameOverrideSpec;
            public string[] StatusGuildNameOverrideKeywords;

            public GTextField StatusGuildRank;
            public string StatusGuildRankResolveInfo;
            public string StatusGuildRankOverrideSpec;
            public string[] StatusGuildRankOverrideKeywords;

            public GTextField StatusLevel;
            public string StatusLevelResolveInfo;
            public string StatusLevelOverrideSpec;
            public string[] StatusLevelOverrideKeywords;

            public GTextField StatusExp;
            public string StatusExpResolveInfo;
            public string StatusExpOverrideSpec;
            public string[] StatusExpOverrideKeywords;

            public GProgressBar StatusExpBar;
            public string StatusExpBarResolveInfo;
            public string StatusExpBarOverrideSpec;
            public string[] StatusExpBarOverrideKeywords;

            public GTextField StatusMembers;
            public string StatusMembersResolveInfo;
            public string StatusMembersOverrideSpec;
            public string[] StatusMembersOverrideKeywords;

            public GTextField StatusGold;
            public string StatusGoldResolveInfo;
            public string StatusGoldOverrideSpec;
            public string[] StatusGoldOverrideKeywords;

            public GTextField StatusPoints;
            public string StatusPointsResolveInfo;
            public string StatusPointsOverrideSpec;
            public string[] StatusPointsOverrideKeywords;

            public GTextField StatusOptions;
            public string StatusOptionsResolveInfo;
            public string StatusOptionsOverrideSpec;
            public string[] StatusOptionsOverrideKeywords;
        }

        private static MobileGuildWindowBinding _mobileGuildBinding;
        private static DateTime _nextMobileGuildBindAttemptUtc = DateTime.MinValue;
        private static bool _mobileGuildBindingsDumped;
        private static bool _mobileGuildDirty;

        private sealed class MobileGroupMemberItemView
        {
            public GComponent Root;
            public int Index;

            public GTextField Name;
            public GTextField Map;
            public GTextField Location;

            public EventCallback0 ClickCallback;
        }

        private sealed class MobileGroupWindowBinding
        {
            public string WindowKey;
            public GComponent Window;
            public string ResolveInfo;

            public GList MemberList;
            public string MemberListResolveInfo;
            public string MemberListOverrideSpec;
            public string[] MemberListOverrideKeywords;
            public ListItemRenderer MemberItemRenderer;

            public readonly List<string> MemberNames = new List<string>(16);
            public int SelectedIndex = -1;

            public GTextInput InviteInput;
            public string InviteInputOverrideSpec;
            public string[] InviteInputOverrideKeywords;
            public GButton InviteButton;
            public string InviteButtonOverrideSpec;
            public string[] InviteButtonOverrideKeywords;
            public EventCallback0 InviteClickCallback;

            public GButton KickButton;
            public string KickButtonOverrideSpec;
            public string[] KickButtonOverrideKeywords;
            public EventCallback0 KickClickCallback;

            public GButton LeaveButton;
            public string LeaveButtonOverrideSpec;
            public string[] LeaveButtonOverrideKeywords;
            public EventCallback0 LeaveClickCallback;
        }

        private static MobileGroupWindowBinding _mobileGroupBinding;
        private static DateTime _nextMobileGroupBindAttemptUtc = DateTime.MinValue;
        private static bool _mobileGroupBindingsDumped;
        private static bool _mobileGroupDirty;

        private static readonly Dictionary<string, GComponent> MobileWindows = new Dictionary<string, GComponent>(StringComparer.OrdinalIgnoreCase);

#if ANDROID || IOS
        private static bool _pointerCapturedByFairyGui;
#endif

#if ANDROID || IOS
        private static InputTextField _lastSoftKeyboardFocus;
#endif

        public static bool Initialized => _initialized;
        public static bool PackagesLoaded => _packagesLoaded;
        public static string InitError => _initError;
        public static string LastPackageLoadError => _lastPackageLoadError;
        public static bool MobileMainHudAttached => _mobileMainHud != null && !_mobileMainHud._disposed;

        public static bool IsAnyMobileWindowVisible
        {
            get
            {
                foreach (KeyValuePair<string, GComponent> pair in MobileWindows)
                {
                    GComponent component = pair.Value;
                    if (component != null && !component._disposed && component.visible)
                        return true;
                }

                return false;
            }
        }

        public static bool IsAnyMobileOverlayVisible
        {
            get
            {
                if (IsAnyMobileWindowVisible)
                    return true;

                if (IsMobileTextPromptVisible)
                    return true;

                try
                {
                    return GRoot.inst != null && GRoot.inst.hasAnyPopup;
                }
                catch
                {
                    return false;
                }
            }
        }

        public static bool IsMobileWindowVisible(string windowKey)
        {
            if (string.IsNullOrWhiteSpace(windowKey))
                return false;

            if (!MobileWindows.TryGetValue(windowKey, out GComponent component))
                return false;

            return component != null && !component._disposed && component.visible;
        }

        public static void AppendMobileChatMessage(string message, ChatType type)
        {
            if (string.IsNullOrWhiteSpace(message))
                return;

            string cleaned = message.Trim();
            if (cleaned.Length == 0)
                return;

            try
            {
                MobileChatEntries.Add(new MobileChatEntry(DateTime.UtcNow, type, cleaned));

                int overflow = MobileChatEntries.Count - MobileChatMaxLines;
                if (overflow > 0)
                    MobileChatEntries.RemoveRange(0, overflow);

                _mobileChatDirty = true;
            }
            catch
            {
            }

            TryRefreshMobileChatLogIfDue(force: false);
            MarkMobileMainHudMessageBarDirty();
        }

        public static void MarkMobileShopDirty()
        {
            try
            {
                _mobileShopDirty = true;
            }
            catch
            {
            }

            TryRefreshMobileShopIfDue(force: false);
        }

        public static void UpdateMobileFriends(IList<ClientFriend> friends)
        {
            try
            {
                MobileFriendEntries.Clear();
                if (friends != null)
                {
                    for (int i = 0; i < friends.Count; i++)
                    {
                        ClientFriend friend = friends[i];
                        if (friend != null)
                            MobileFriendEntries.Add(friend);
                    }
                }

                MobileFriendEntries.Sort((a, b) =>
                {
                    if (a == null && b == null) return 0;
                    if (a == null) return 1;
                    if (b == null) return -1;

                    int blocked = a.Blocked.CompareTo(b.Blocked);
                    if (blocked != 0) return blocked;

                    int online = b.Online.CompareTo(a.Online);
                    if (online != 0) return online;

                    bool aHasMemo = !string.IsNullOrWhiteSpace(a.Memo);
                    bool bHasMemo = !string.IsNullOrWhiteSpace(b.Memo);
                    int memoHas = bHasMemo.CompareTo(aHasMemo);
                    if (memoHas != 0) return memoHas;

                    string memoA = aHasMemo ? (a.Memo ?? string.Empty) : string.Empty;
                    string memoB = bHasMemo ? (b.Memo ?? string.Empty) : string.Empty;
                    int memo = string.Compare(memoA, memoB, StringComparison.OrdinalIgnoreCase);
                    if (memo != 0) return memo;

                    string nameA = a.Name ?? string.Empty;
                    string nameB = b.Name ?? string.Empty;
                    return string.Compare(nameA, nameB, StringComparison.OrdinalIgnoreCase);
                });
            }
            catch
            {
            }

            try
            {
                _mobileFriendDirty = true;
            }
            catch
            {
            }

            TryRefreshMobileFriendIfDue(force: false);
        }

        public static void MarkMobileFriendDirty()
        {
            try
            {
                _mobileFriendDirty = true;
            }
            catch
            {
            }

            TryRefreshMobileFriendIfDue(force: false);
        }

        public static void UpdateMobileGuildNotice(IList<string> noticeLines)
        {
            try
            {
                MobileGuildNoticeLines.Clear();
                if (noticeLines != null)
                {
                    for (int i = 0; i < noticeLines.Count; i++)
                    {
                        string line = noticeLines[i];
                        if (!string.IsNullOrWhiteSpace(line))
                            MobileGuildNoticeLines.Add(line.TrimEnd());
                    }
                }
            }
            catch
            {
            }

            MarkMobileGuildDirty();
        }

        public static void UpdateMobileGuildRanks(IList<GuildRank> ranks)
        {
            try
            {
                MobileGuildRanks.Clear();
                MobileGuildMembers.Clear();

                if (ranks != null)
                {
                    for (int i = 0; i < ranks.Count; i++)
                    {
                        GuildRank rank = ranks[i];
                        if (rank == null)
                            continue;

                        MobileGuildRanks.Add(rank);

                        string rankName = rank.Name ?? string.Empty;
                        List<GuildMember> members = rank.Members;
                        if (members == null)
                            continue;

                        for (int j = 0; j < members.Count; j++)
                        {
                            GuildMember member = members[j];
                            if (member == null)
                                continue;

                            MobileGuildMembers.Add(new MobileGuildMemberEntry
                            {
                                Name = member.Name ?? string.Empty,
                                RankName = rankName,
                                Online = member.Online,
                                LastLogin = member.LastLogin,
                                Id = member.Id,
                            });
                        }
                    }
                }

                MobileGuildMembers.Sort((a, b) =>
                {
                    if (a == null && b == null) return 0;
                    if (a == null) return 1;
                    if (b == null) return -1;

                    int online = b.Online.CompareTo(a.Online);
                    if (online != 0) return online;

                    int rank = string.Compare(a.RankName, b.RankName, StringComparison.OrdinalIgnoreCase);
                    if (rank != 0) return rank;

                    return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
                });
            }
            catch
            {
            }

            MarkMobileGuildDirty();
        }

        public static void SetMobileGuildMemberOnline(string name, bool online)
        {
            if (string.IsNullOrWhiteSpace(name))
                return;

            bool changed = false;

            try
            {
                for (int i = 0; i < MobileGuildMembers.Count; i++)
                {
                    MobileGuildMemberEntry member = MobileGuildMembers[i];
                    if (member == null)
                        continue;

                    if (!string.Equals(member.Name, name, StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (member.Online == online)
                        break;

                    member.Online = online;
                    changed = true;
                    break;
                }
            }
            catch
            {
            }

            if (changed)
                MarkMobileGuildDirty();
        }

        public static void MarkMobileGuildDirty()
        {
            try
            {
                _mobileGuildDirty = true;
            }
            catch
            {
            }

            TryRefreshMobileGuildIfDue(force: false);
        }

        public static void MarkMobileGroupDirty()
        {
            try
            {
                _mobileGroupDirty = true;
            }
            catch
            {
            }

            TryRefreshMobileGroupIfDue(force: false);
        }

        public static bool TryAttachMobileMainHud()
        {
            if (_stage == null)
                return false;

            if (!_packagesLoaded)
                return false;

            if (_mobileMainHud != null && !_mobileMainHud._disposed)
            {
                EnsureMobileMainHudLayout(force: false);
                TryInstallMobileMainHudDebugClickLogger();
                TryBindMobileMainHudButtonsIfDue();
                return true;
            }

            try
            {
                var totalSw = Stopwatch.StartNew();
                string componentName = ChooseMobileMainHudComponentName();

                if (Settings.LogErrors)
                    CMain.SaveLog($"FairyGUI: 开始创建主界面（{MobileMainHudPackageName}/{componentName}）");

                var stepSw = Stopwatch.StartNew();
                if (Settings.LogErrors)
                    CMain.SaveLog("FairyGUI: CreateObject 前置：Stopwatch 已启动。");

                System.Threading.Interlocked.Increment(ref _mobileMainHudCreateTraceDepth);
                if (Settings.LogErrors)
                    CMain.SaveLog($"FairyGUI: CreateObject 前置：TraceDepth 已递增到 {_mobileMainHudCreateTraceDepth}。");

                GObject created;
                System.Threading.CancellationTokenSource createHudWatchdogCts = null;
                try
                {
                    if (Settings.LogErrors)
                        CMain.SaveLog($"FairyGUI: 即将调用 UIPackage.CreateObject（{MobileMainHudPackageName}/{componentName}）。");

                    try
                    {
                        createHudWatchdogCts = new System.Threading.CancellationTokenSource();
                        _ = System.Threading.Tasks.Task.Run(async () =>
                        {
                            int[] marks = { 3, 6, 10, 20, 40, 60 };
                            int last = 0;
                            for (int i = 0; i < marks.Length; i++)
                            {
                                int seconds = marks[i];
                                int delaySeconds = Math.Max(0, seconds - last);
                                last = seconds;

                                try
                                {
                                    await System.Threading.Tasks.Task.Delay(TimeSpan.FromSeconds(delaySeconds), createHudWatchdogCts.Token);
                                }
                                catch
                                {
                                    return;
                                }

                                try
                                {
                                    if (Settings.LogErrors)
                                        CMain.SaveLog($"FairyGUI: CreateObject 仍在执行（{seconds}s）({MobileMainHudPackageName}/{componentName})。");
                                }
                                catch
                                {
                                }
                            }
                        }, createHudWatchdogCts.Token);
                    }
                    catch
                    {
                        createHudWatchdogCts = null;
                    }

                    created = UIPackage.CreateObject(MobileMainHudPackageName, componentName);
                }
                finally
                {
                    try
                    {
                        createHudWatchdogCts?.Cancel();
                    }
                    catch
                    {
                    }

                    try
                    {
                        createHudWatchdogCts?.Dispose();
                    }
                    catch
                    {
                    }

                    System.Threading.Interlocked.Decrement(ref _mobileMainHudCreateTraceDepth);
                    if (Settings.LogErrors)
                        CMain.SaveLog($"FairyGUI: CreateObject 后置：TraceDepth 已递减到 {_mobileMainHudCreateTraceDepth}。");
                }
                if (Settings.LogErrors)
                    CMain.SaveLog($"FairyGUI: CreateObject 完成（{stepSw.ElapsedMilliseconds}ms）");

                if (created is not GComponent mainHud)
                {
                    created?.Dispose();
                    throw new InvalidOperationException($"未找到或无法创建 FairyGUI 主界面组件：{MobileMainHudPackageName}/{componentName}");
                }

                stepSw.Restart();
                _mobileMainHudSafeAreaRoot = new GComponent
                {
                    name = "MobileMainHudSafeAreaRoot",
                    opaque = false,
                };

                (_uiManager?.HudLayer ?? GRoot.inst).AddChild(_mobileMainHudSafeAreaRoot);
                _mobileMainHudSafeAreaRoot.AddChild(mainHud);
                _mobileMainHud = mainHud;
                if (Settings.LogErrors)
                    CMain.SaveLog($"FairyGUI: 主界面挂载完成（{stepSw.ElapsedMilliseconds}ms）");

                stepSw.Restart();
                EnsureMobileMainHudLayout(force: true);
                try
                {
                    mainHud.SetPosition(0, 0);
                }
                catch
                {
                }

                mainHud.AddRelation(_mobileMainHudSafeAreaRoot, RelationType.Left_Left);
                mainHud.AddRelation(_mobileMainHudSafeAreaRoot, RelationType.Top_Top);
                if (Settings.LogErrors)
                    CMain.SaveLog($"FairyGUI: EnsureMobileMainHudLayout 完成（{stepSw.ElapsedMilliseconds}ms）");

                TryApplyMobileMainHudDefaultVisibilityIfDue();

                TryInstallMobileMainHudDebugClickLogger();

                if (Settings.DebugMode || Settings.LogErrors)
                {
                    stepSw.Restart();
                    TryDumpMobileMainHudTreeIfDue(componentName);
                    if (Settings.LogErrors)
                        CMain.SaveLog($"FairyGUI: TryDumpMobileMainHudTreeIfDue 返回（{stepSw.ElapsedMilliseconds}ms）");
                }

                stepSw.Restart();
                TryBindMobileMainHudButtonsIfDue();
                if (Settings.LogErrors)
                    CMain.SaveLog($"FairyGUI: TryBindMobileMainHudButtonsIfDue 返回（{stepSw.ElapsedMilliseconds}ms）");

                MarkMobileQuestTrackingDirty();

                CMain.SaveLog($"FairyGUI: 主界面已创建（{MobileMainHudPackageName}/{componentName}）");
                if (Settings.LogErrors)
                    CMain.SaveLog($"FairyGUI: 主界面创建总耗时 {totalSw.ElapsedMilliseconds}ms");
                return true;
            }
            catch (Exception ex)
            {
                CMain.SaveError("FairyGUI: 主界面创建失败：" + ex);
                TryDetachMobileMainHud();
                return false;
            }
        }

        public static void TryDetachMobileMainHud()
        {
            try
            {
                CloseAllMobileWindows();
            }
            catch
            {
            }

            try
            {
                if (_stage != null && _mobileMainHudClickLoggerInstalled)
                {
                    _stage.onClick.RemoveCapture(MobileMainHudClickLogger);
                    _mobileMainHudClickLoggerInstalled = false;
                }
            }
            catch
            {
            }

            try
            {
                ResetMobileMainHudHotbars();
            }
            catch
            {
            }

            try
            {
                ResetMobileQuestTrackingBindings();
            }
            catch
            {
            }

            try
            {
                _mobileMainHudController?.Dispose();
            }
            catch
            {
            }

            _mobileMainHudController = null;

            try
            {
                _mobileMainHudSafeAreaRoot?.Dispose();
            }
            catch
            {
            }

            _mobileMainHudSafeAreaRoot = null;
            _mobileMainHud = null;
            _mobileMainHudSafeAreaBounds = default;
            _mobileMainHudTreeDumped = false;

            ResetMobileMainHudDefaults();

            ResetMobileMainHudMiniMapBindings();
            ResetMobileMainHudMessageBarBindings();
            ResetMobileMainHudBottomStatsBindings();
            ResetMobileMiniMapLocator();
        }

        public static void HideAllMobileWindowsExcept(string windowKey)
        {
            if (string.IsNullOrWhiteSpace(windowKey))
                return;

            foreach (KeyValuePair<string, GComponent> pair in MobileWindows)
            {
                string key = pair.Key;
                if (string.Equals(key, windowKey, StringComparison.OrdinalIgnoreCase))
                    continue;

                GComponent component = pair.Value;
                if (component == null || component._disposed)
                    continue;

                component.visible = false;
            }
        }

        public static void HideAllMobileWindows()
        {
            foreach (KeyValuePair<string, GComponent> pair in MobileWindows)
            {
                GComponent component = pair.Value;
                if (component == null || component._disposed)
                    continue;

                component.visible = false;
            }
        }

        public static bool TryHideMobileWindow(string windowKey)
        {
            if (string.IsNullOrWhiteSpace(windowKey))
                return false;

            if (!MobileWindows.TryGetValue(windowKey, out GComponent component))
                return false;

            if (component == null || component._disposed)
                return false;

            component.visible = false;
            return true;
        }

        public static bool TryHandleMobileBackRequested()
        {
            if (_stage == null || !_initialized)
                return false;

            try
            {
                if (GRoot.inst != null && GRoot.inst.hasAnyPopup)
                {
                    GRoot.inst.HidePopup();
                    return true;
                }
            }
            catch
            {
            }

            if (TryCancelMobileTextPrompt(invokeCancel: true))
                return true;

            return TryHideTopmostVisibleMobileWindow();
        }

        private static bool TryHideTopmostVisibleMobileWindow()
        {
            string topKey = null;
            GComponent topWindow = null;
            int topIndex = int.MinValue;

            foreach (KeyValuePair<string, GComponent> pair in MobileWindows)
            {
                string key = pair.Key;
                GComponent component = pair.Value;

                if (component == null || component._disposed || !component.visible)
                    continue;

                int index = -1;
                try
                {
                    GComponent parent = component.parent;
                    if (parent != null && !parent._disposed)
                        index = parent.GetChildIndex(component);
                }
                catch
                {
                    index = -1;
                }

                if (topWindow == null || index > topIndex)
                {
                    topKey = key;
                    topWindow = component;
                    topIndex = index;
                }
            }

            if (topWindow == null || topWindow._disposed)
                return false;

            try
            {
                topWindow.visible = false;

                if (Settings.DebugMode && !string.IsNullOrWhiteSpace(topKey))
                    CMain.SaveLog("FairyGUI: Back -> HideWindow " + topKey);
            }
            catch
            {
            }

            return true;
        }

        public static void CloseAllMobileWindows()
        {
            TryCancelMobileTextPrompt(invokeCancel: true);

            ResetMobileChatBindings();
            ResetMobileInventoryBindings();
            ResetMobileMailBindings();
            ResetMobileQuestBindings();
            ResetMobileTradeBindings();
            ResetMobileNpcBindings();
            ResetMobileNpcGoodsBindings();
            ResetMobileTrustMerchantBindings();
            ResetMobileBigMapBindings();
            ResetMobileStorageBindings();
            ResetMobileSystemBindings();
            ResetMobileNoticeBindings();
            ResetMobileMagicBindings();
            ResetMobileShopBindings();
            ResetMobileFriendBindings();
            ResetMobileGuildBindings();
            ResetMobileGroupBindings();

            foreach (KeyValuePair<string, GComponent> pair in MobileWindows)
            {
                GComponent component = pair.Value;
                try
                {
                    component?.Dispose();
                }
                catch
                {
                }
            }

            MobileWindows.Clear();
        }

        private static bool TryCreateMobileWindowComponent(string windowKey, string[] keywords, out GComponent component, out string resolveInfo)
        {
            component = null;
            resolveInfo = null;

            if (string.IsNullOrWhiteSpace(windowKey))
                return false;

            string overrideSpec = string.Empty;
            try
            {
                InIReader reader = TryCreateConfigReader();
                if (reader != null)
                {
                    overrideSpec = reader.ReadString(
                        FairyGuiConfigSectionName,
                        MobileWindowComponentConfigKeyPrefix + windowKey.Trim(),
                        string.Empty,
                        writeWhenNull: false);
                }
            }
            catch
            {
                overrideSpec = string.Empty;
            }

            overrideSpec = overrideSpec?.Trim() ?? string.Empty;

            string packageName = null;
            string componentName = null;
            string url = null;
            string[] effectiveKeywords = keywords;

            if (!string.IsNullOrWhiteSpace(overrideSpec))
            {
                if (TryParsePrefixedValue(overrideSpec, "url", out string urlValue))
                {
                    url = urlValue;
                    resolveInfo = "override url=" + url;
                }
                else
                {
                    string normalized = overrideSpec.Replace('\\', '/');
                    int slashIndex = normalized.IndexOf('/');
                    if (slashIndex > 0 && slashIndex < normalized.Length - 1)
                    {
                        string pkg = normalized.Substring(0, slashIndex).Trim();
                        string comp = normalized.Substring(slashIndex + 1).Trim();
                        if (!string.IsNullOrWhiteSpace(pkg) && !string.IsNullOrWhiteSpace(comp))
                        {
                            packageName = pkg;
                            componentName = comp;
                            resolveInfo = pkg + "/" + comp + " (override)";
                        }
                    }

                    if (packageName == null)
                        effectiveKeywords = SplitKeywords(normalized);
                }
            }

            try
            {
                if (!string.IsNullOrWhiteSpace(url))
                {
                    GObject created = UIPackage.CreateObjectFromURL(url);
                    if (created is not GComponent componentByUrl)
                    {
                        created?.Dispose();
                        CMain.SaveError("FairyGUI: 创建窗口失败（" + windowKey + "）：未找到或无法从 url 创建组件 url=" + url);
                        return false;
                    }

                    component = componentByUrl;
                    return true;
                }

                if (string.IsNullOrWhiteSpace(packageName) || string.IsNullOrWhiteSpace(componentName))
                {
                    if (effectiveKeywords == null || effectiveKeywords.Length == 0)
                    {
                        CMain.SaveError("FairyGUI: 未找到窗口组件（" + windowKey + "）：keywords 为空（可用 Mir2Config.ini 覆盖 [" + FairyGuiConfigSectionName + "] " +
                                        MobileWindowComponentConfigKeyPrefix + windowKey + "=UI/组件名 或 url:...）");
                        return false;
                    }

                    if (!TryFindBestComponentByKeywords(effectiveKeywords, out packageName, out componentName, out string foundResolveInfo))
                    {
                        CMain.SaveError("FairyGUI: 未找到窗口组件（" + windowKey + "），keywords=" +
                                        string.Join("|", effectiveKeywords ?? Array.Empty<string>()) +
                                        (string.IsNullOrWhiteSpace(overrideSpec) ? string.Empty : " override=" + overrideSpec));
                        return false;
                    }

                    resolveInfo = string.IsNullOrWhiteSpace(resolveInfo)
                        ? foundResolveInfo
                        : foundResolveInfo + " (" + resolveInfo + ")";
                }

                GObject createdByName = UIPackage.CreateObject(packageName, componentName);
                if (createdByName is not GComponent componentByName)
                {
                    createdByName?.Dispose();
                    CMain.SaveError("FairyGUI: 创建窗口失败（" + windowKey + "）：未找到或无法创建组件 " + packageName + "/" + componentName);
                    return false;
                }

                component = componentByName;
                return true;
            }
            catch (Exception ex)
            {
                string extra = string.Empty;
                try
                {
                    var tags = new List<string>(4);
                    if (!string.IsNullOrWhiteSpace(resolveInfo))
                        tags.Add("resolve=" + resolveInfo);
                    if (!string.IsNullOrWhiteSpace(url))
                        tags.Add("url=" + url);
                    if (!string.IsNullOrWhiteSpace(packageName) && !string.IsNullOrWhiteSpace(componentName))
                        tags.Add("component=" + packageName + "/" + componentName);
                    if (!string.IsNullOrWhiteSpace(overrideSpec))
                        tags.Add("override=" + overrideSpec);
                    if (tags.Count > 0)
                        extra = " [" + string.Join(" | ", tags) + "]";
                }
                catch
                {
                    extra = string.Empty;
                }

                CMain.SaveError("FairyGUI: 创建窗口异常（" + windowKey + "）：" + ex + extra);
                return false;
            }
        }

        public static bool TryShowMobileWindowByKeywords(string windowKey, string[] keywords)
        {
            if (_stage == null || !_initialized)
                return false;

            if (!_packagesLoaded)
                return false;

            if (string.IsNullOrWhiteSpace(windowKey))
                return false;

            try
            {
                if (MobileWindows.TryGetValue(windowKey, out GComponent existing) && existing != null && !existing._disposed)
                {
                    existing.visible = true;
                    BringToFront(existing);
                    TryBindMobileWindowCloseButton(windowKey, existing);
                    TryBindMobileWindowIfDue(windowKey, existing, resolveInfo: null);
                    return true;
                }

                if (!TryCreateMobileWindowComponent(windowKey, keywords, out GComponent component, out string resolveInfo))
                {
                    // NPC 对话框：某些资源包可能缺失/命名不一致，提供兜底窗口保证交互链路可用
                    if (string.Equals(windowKey, "Npc", StringComparison.OrdinalIgnoreCase))
                    {
                        if (!TryCreateMobileNpcFallbackWindow(out component, out resolveInfo))
                            return false;
                    }
                    else
                    {
                        return false;
                    }
                }

                GComponent layer = _mobileOverlaySafeAreaRoot != null && !_mobileOverlaySafeAreaRoot._disposed
                    ? _mobileOverlaySafeAreaRoot
                    : (_uiManager?.OverlayLayer ?? GRoot.inst);
                layer.AddChild(component);
                component.AddRelation(layer, RelationType.Size);

                MobileWindows[windowKey] = component;
                component.visible = true;

                if (Settings.DebugMode && !string.IsNullOrWhiteSpace(resolveInfo))
                    CMain.SaveLog("FairyGUI: 窗口已创建（" + windowKey + "） -> " + resolveInfo);

                TryDumpMobileWindowTreeIfDue(windowKey, resolveInfo, component);
                TryBindMobileWindowCloseButton(windowKey, component);
                TryBindMobileWindowIfDue(windowKey, component, resolveInfo);

                return true;
            }
            catch (Exception ex)
            {
                CMain.SaveError("FairyGUI: ShowWindow 异常（" + windowKey + "）：" + ex.Message);
                return false;
            }
        }

        public static bool TryToggleMobileWindowByKeywords(string windowKey, string[] keywords, out bool nowVisible)
        {
            nowVisible = false;

            if (_stage == null || !_initialized)
                return false;

            if (!_packagesLoaded)
                return false;

            if (string.IsNullOrWhiteSpace(windowKey))
                return false;

            try
            {
                if (MobileWindows.TryGetValue(windowKey, out GComponent existing) && existing != null && !existing._disposed)
                {
                    existing.visible = !existing.visible;
                    nowVisible = existing.visible;
                    if (nowVisible)
                        BringToFront(existing);
                    if (nowVisible)
                        TryBindMobileWindowCloseButton(windowKey, existing);
                    if (nowVisible)
                        TryBindMobileWindowIfDue(windowKey, existing, resolveInfo: null);
                    if (nowVisible)
                        TryApplyMobileWindowLayoutIfNeeded(windowKey, existing, existing.parent, resolveInfo: null);
                    if (Settings.LogErrors && string.Equals(windowKey, "State", StringComparison.OrdinalIgnoreCase))
                    {
                        try
                        {
                            string pkg = existing.packageItem?.owner?.name ?? "(null)";
                            string item = existing.packageItem?.name ?? existing.name ?? "(null)";
                            CMain.SaveLog("FairyGUI: State 窗口切换：" +
                                          " nowVisible=" + nowVisible +
                                          " component=" + pkg + "/" + item +
                                          $" pos=({existing.x:0.##},{existing.y:0.##}) size=({existing.width:0.##},{existing.height:0.##}) alpha={existing.alpha:0.##}");
                        }
                        catch
                        {
                        }
                    }
                    return true;
                }

                if (!TryCreateMobileWindowComponent(windowKey, keywords, out GComponent component, out string resolveInfo))
                    return false;

                GComponent layer = _mobileOverlaySafeAreaRoot != null && !_mobileOverlaySafeAreaRoot._disposed
                    ? _mobileOverlaySafeAreaRoot
                    : (_uiManager?.OverlayLayer ?? GRoot.inst);
                layer.AddChild(component);
                if (!string.Equals(windowKey, "State", StringComparison.OrdinalIgnoreCase))
                    component.AddRelation(layer, RelationType.Size);
                TryApplyMobileWindowLayoutIfNeeded(windowKey, component, layer, resolveInfo);

                MobileWindows[windowKey] = component;
                component.visible = true;
                nowVisible = true;

                if (Settings.LogErrors && string.Equals(windowKey, "State", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        string pkg = component.packageItem?.owner?.name ?? "(null)";
                        string item = component.packageItem?.name ?? component.name ?? "(null)";
                        CMain.SaveLog("FairyGUI: State 窗口已创建并显示：" +
                                      " resolve=" + (resolveInfo ?? "(null)") +
                                      " component=" + pkg + "/" + item +
                                      $" pos=({component.x:0.##},{component.y:0.##}) size=({component.width:0.##},{component.height:0.##}) alpha={component.alpha:0.##}");
                    }
                    catch
                    {
                    }
                }

                if (Settings.DebugMode && !string.IsNullOrWhiteSpace(resolveInfo))
                    CMain.SaveLog("FairyGUI: 窗口已创建（" + windowKey + "） -> " + resolveInfo);

                TryDumpMobileWindowTreeIfDue(windowKey, resolveInfo, component);
                TryBindMobileWindowCloseButton(windowKey, component);
                TryBindMobileWindowIfDue(windowKey, component, resolveInfo);

                return true;
            }
            catch (Exception ex)
            {
                CMain.SaveError("FairyGUI: ToggleWindow 异常（" + windowKey + "）：" + ex.Message);
                return false;
            }
        }

        public static bool TryToggleMobileWindowByOverrideSpecOnly(string windowKey, out bool nowVisible)
        {
            nowVisible = false;

            if (!TryGetMobileWindowOverrideSpec(windowKey, out _))
                return false;

            return TryToggleMobileWindowByKeywords(windowKey, Array.Empty<string>(), out nowVisible);
        }

        public static bool TryGetMobileWindowOverrideSpec(string windowKey, out string overrideSpec)
        {
            overrideSpec = string.Empty;

            if (string.IsNullOrWhiteSpace(windowKey))
                return false;

            try
            {
                InIReader reader = TryCreateConfigReader();
                if (reader != null)
                {
                    overrideSpec = reader.ReadString(
                        FairyGuiConfigSectionName,
                        MobileWindowComponentConfigKeyPrefix + windowKey.Trim(),
                        string.Empty,
                        writeWhenNull: false);
                }
            }
            catch
            {
                overrideSpec = string.Empty;
            }

            overrideSpec = overrideSpec?.Trim() ?? string.Empty;
            return !string.IsNullOrWhiteSpace(overrideSpec);
        }

        public static bool IsPointOverFairyGuiUI(Vector2 position)
        {
            Stage stage = _stage;
            if (stage == null)
                return false;

            try
            {
                DisplayObject target = stage.HitTest(position, forTouch: true);
                return target != null && !ReferenceEquals(target, stage);
            }
            catch
            {
                return false;
            }
        }

        public static bool IsPointOverMobileMainHudMiniMap(Vector2 position)
        {
            Stage stage = _stage;
            if (stage == null)
                return false;

            if (_mobileMainHud == null || _mobileMainHud._disposed)
                return false;

            EnsureMobileMiniMapLocatorInitialized();

            if (_mobileMiniMapRoot != null && _mobileMiniMapRoot._disposed)
                _mobileMiniMapRoot = null;

            try
            {
                DisplayObject target = stage.HitTest(position, forTouch: false);
                if (target == null || ReferenceEquals(target, stage))
                    return false;

                GObject owner = target.gOwner;
                if (owner == null)
                    return false;

                if (_mobileMiniMapRoot != null)
                {
                    while (owner != null)
                    {
                        if (ReferenceEquals(owner, _mobileMiniMapRoot))
                            return true;

                        owner = owner.parent;
                    }

                    return false;
                }

                string[] keywords = _mobileMiniMapKeywords;
                if (keywords == null || keywords.Length == 0)
                    keywords = DefaultMiniMapKeywords;

                while (owner != null)
                {
                    if (IsAnyFieldMatchKeywords(owner, keywords))
                        return true;

                    owner = owner.parent;
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        private static void BringToFront(GComponent component)
        {
            if (component == null || component._disposed)
                return;

            GComponent parent = component.parent;
            if (parent == null || parent._disposed)
                return;

            try
            {
                parent.SetChildIndex(component, Math.Max(0, parent.numChildren - 1));
            }
            catch
            {
            }
        }

        private static void TryBindMobileWindowIfDue(string windowKey, GComponent window, string resolveInfo)
        {
            if (string.IsNullOrWhiteSpace(windowKey))
                return;

            if (window == null || window._disposed)
                return;

            if (string.Equals(windowKey, "Chat", StringComparison.OrdinalIgnoreCase))
            {
                TryBindMobileChatWindowIfDue(windowKey, window, resolveInfo);
                TryRefreshMobileChatLogIfDue(force: false);
            }
            else if (string.Equals(windowKey, "Mail", StringComparison.OrdinalIgnoreCase))
            {
                TryBindMobileMailWindowIfDue(windowKey, window, resolveInfo);
                TryRefreshMobileMailIfDue(force: true);
            }
            else if (string.Equals(windowKey, "Inventory", StringComparison.OrdinalIgnoreCase))
            {
                TryBindMobileInventoryWindowIfDue(windowKey, window, resolveInfo);
                TryRefreshMobileInventoryIfDue(force: true);
            }
            else if (string.Equals(windowKey, "Magic", StringComparison.OrdinalIgnoreCase))
            {
                TryBindMobileMagicWindowIfDue(windowKey, window, resolveInfo);
                TryRefreshMobileMagicIfDue(force: true);
            }
            else if (string.Equals(windowKey, "Storage", StringComparison.OrdinalIgnoreCase))
            {
                TryBindMobileStorageWindowIfDue(windowKey, window, resolveInfo);
                TryRefreshMobileStorageIfDue(force: true);
            }
            else if (string.Equals(windowKey, "System", StringComparison.OrdinalIgnoreCase))
            {
                TryBindMobileSystemWindowIfDue(windowKey, window, resolveInfo);
                TryRefreshMobileSystemIfDue(force: true);
            }
            else if (string.Equals(windowKey, "Notice", StringComparison.OrdinalIgnoreCase))
            {
                TryBindMobileNoticeWindowIfDue(windowKey, window, resolveInfo);
                TryRefreshMobileNoticeIfDue(force: true);
            }
            else if (string.Equals(windowKey, "BigMap", StringComparison.OrdinalIgnoreCase))
            {
                TryBindMobileBigMapWindowIfDue(windowKey, window, resolveInfo);
                TryRefreshMobileBigMapIfDue(force: true);
            }
            else if (string.Equals(windowKey, "Shop", StringComparison.OrdinalIgnoreCase))
            {
                TryBindMobileShopWindowIfDue(windowKey, window, resolveInfo);
                TryRefreshMobileShopIfDue(force: true);
            }
            else if (string.Equals(windowKey, "Friend", StringComparison.OrdinalIgnoreCase))
            {
                TryBindMobileFriendWindowIfDue(windowKey, window, resolveInfo);
                TryRefreshMobileFriendIfDue(force: true);
            }
            else if (string.Equals(windowKey, "Guild", StringComparison.OrdinalIgnoreCase))
            {
                TryBindMobileGuildWindowIfDue(windowKey, window, resolveInfo);
                TryRefreshMobileGuildIfDue(force: true);
            }
            else if (string.Equals(windowKey, "Group", StringComparison.OrdinalIgnoreCase))
            {
                TryBindMobileGroupWindowIfDue(windowKey, window, resolveInfo);
                TryRefreshMobileGroupIfDue(force: true);
            }
            else if (string.Equals(windowKey, "Trade", StringComparison.OrdinalIgnoreCase))
            {
                TryBindMobileTradeWindowIfDue(windowKey, window, resolveInfo);
                TryRefreshMobileTradeIfDue(force: true);
            }
            else if (string.Equals(windowKey, "State", StringComparison.OrdinalIgnoreCase))
            {
                TryBindMobileStateWindowIfDue(windowKey, window, resolveInfo);
                TryRefreshMobileStateIfDue(force: true);
            }
            else if (string.Equals(windowKey, "Npc", StringComparison.OrdinalIgnoreCase))
            {
                TryBindMobileNpcWindowIfDue(windowKey, window, resolveInfo);
                TryRefreshMobileNpcIfDue(force: true);
            }
            else if (string.Equals(windowKey, "NpcGoods", StringComparison.OrdinalIgnoreCase))
            {
                TryBindMobileNpcGoodsWindowIfDue(windowKey, window, resolveInfo);
                TryRefreshMobileNpcGoodsIfDue(force: true);
            }
            else if (string.Equals(windowKey, "Quest", StringComparison.OrdinalIgnoreCase))
            {
                TryBindMobileQuestWindowIfDue(windowKey, window, resolveInfo);
                TryRefreshMobileQuestIfDue(force: true);
            }
            else if (string.Equals(windowKey, "TrustMerchant", StringComparison.OrdinalIgnoreCase))
            {
                TryBindMobileTrustMerchantWindowIfDue(windowKey, window, resolveInfo);
                TryRefreshMobileTrustMerchantIfDue(force: true);
            }
        }

        private static void TryApplyMobileWindowLayoutIfNeeded(string windowKey, GComponent window, GComponent layer, string resolveInfo)
        {
            if (string.IsNullOrWhiteSpace(windowKey))
                return;

            if (window == null || window._disposed)
                return;

            if (layer == null || layer._disposed)
                layer = window.parent;

            if (layer == null || layer._disposed)
                return;

            if (!string.Equals(windowKey, "State", StringComparison.OrdinalIgnoreCase))
                return;

            TryFitMobileFloatingWindow(windowKey, window, layer, marginRatio: 0.02F, resolveInfo);
        }

        private static void TryFitMobileFloatingWindow(string windowKey, GComponent window, GComponent layer, float marginRatio, string resolveInfo)
        {
            if (window == null || window._disposed || layer == null || layer._disposed)
                return;

            try
            {
                // 角色状态等“PC 风格窗体”不应该强行 Size 贴满屏幕，否则会触发 overflow/clip 导致内容被裁掉。
                window.RemoveRelation(layer, RelationType.Size);
            }
            catch
            {
            }

            float parentW = 0F;
            float parentH = 0F;
            float baseW = 0F;
            float baseH = 0F;

            try
            {
                parentW = Math.Max(1F, layer.width);
                parentH = Math.Max(1F, layer.height);
                baseW = Math.Max(1F, window.width);
                baseH = Math.Max(1F, window.height);
            }
            catch
            {
                return;
            }

            float margin = Math.Max(0F, Math.Min(parentW, parentH) * Math.Clamp(marginRatio, 0F, 0.15F));
            float availW = Math.Max(1F, parentW - margin * 2F);
            float availH = Math.Max(1F, parentH - margin * 2F);

            float scale = 1F;
            try
            {
                scale = Math.Min(1F, Math.Min(availW / baseW, availH / baseH));
                if (float.IsNaN(scale) || float.IsInfinity(scale) || scale <= 0.01F)
                    scale = 1F;
            }
            catch
            {
                scale = 1F;
            }

            try
            {
                // 使用 pivotAsAnchor：Position 表示 pivot 点位置，与 scale 无关，便于稳定居中。
                window.SetPivot(0.5F, 0.5F, asAnchor: true);
            }
            catch
            {
            }

            try
            {
                window.SetScale(scale, scale);
            }
            catch
            {
            }

            try
            {
                window.SetPosition(parentW * 0.5F, parentH * 0.5F);
            }
            catch
            {
            }

            try
            {
                window.AddRelation(layer, RelationType.Center_Center);
                window.AddRelation(layer, RelationType.Middle_Middle);
            }
            catch
            {
            }

            if (Settings.LogErrors && string.Equals(windowKey, "State", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    string info = string.IsNullOrWhiteSpace(resolveInfo) ? string.Empty : (" resolve=" + resolveInfo);
                    CMain.SaveLog("FairyGUI: State 窗口布局已调整：" +
                                  " parent=" + $"{parentW:0.##}x{parentH:0.##}" +
                                  " base=" + $"{baseW:0.##}x{baseH:0.##}" +
                                  $" scale={scale:0.###} margin={margin:0.##}" +
                                  $" pos=({window.x:0.##},{window.y:0.##})" + info);
                }
                catch
                {
                }
            }
        }

        private static void ResetMobileChatBindings()
        {
            try
            {
                if (_mobileChatSendButton != null && !_mobileChatSendButton._disposed && _mobileChatSendClickCallback != null)
                    _mobileChatSendButton.onClick.Remove(_mobileChatSendClickCallback);
            }
            catch
            {
            }

            try
            {
                if (_mobileChatInput != null && !_mobileChatInput._disposed && _mobileChatSubmitCallback != null)
                    _mobileChatInput.onSubmit.Remove(_mobileChatSubmitCallback);
            }
            catch
            {
            }

            _mobileChatWindow = null;
            _mobileChatInput = null;
            _mobileChatSendButton = null;
            _mobileChatLogField = null;
            _mobileChatWindowResolveInfo = null;

            _mobileChatInputOverrideSpec = null;
            _mobileChatSendOverrideSpec = null;
            _mobileChatLogOverrideSpec = null;

            _nextMobileChatBindAttemptUtc = DateTime.MinValue;
            _mobileChatBindingsDumped = false;
        }

        private static void ApplyMobileChatFontSizes()
        {
            const int desiredSize = 20;

            try
            {
                if (_mobileChatLogField != null && !_mobileChatLogField._disposed)
                {
                    TextFormat tf = _mobileChatLogField.textFormat;
                    if (tf.size != desiredSize)
                    {
                        tf.size = desiredSize;
                        _mobileChatLogField.textFormat = tf;
                    }
                }
            }
            catch
            {
            }

            try
            {
                if (_mobileChatInput != null && !_mobileChatInput._disposed)
                {
                    TextFormat tf = _mobileChatInput.textFormat;
                    if (tf.size != desiredSize)
                    {
                        tf.size = desiredSize;
                        _mobileChatInput.textFormat = tf;
                    }
                }
            }
            catch
            {
            }
        }

        private static void TryBindMobileChatWindowIfDue(string windowKey, GComponent window, string resolveInfo)
        {
            if (window == null || window._disposed)
                return;

            if (_mobileChatWindow != null && _mobileChatWindow._disposed)
                ResetMobileChatBindings();

            if (_mobileChatWindow == null || !ReferenceEquals(_mobileChatWindow, window))
            {
                ResetMobileChatBindings();
                _mobileChatWindow = window;
                _mobileChatWindowResolveInfo = resolveInfo;
            }

            if (DateTime.UtcNow < _nextMobileChatBindAttemptUtc)
                return;

            if (_mobileChatLogField != null && !_mobileChatLogField._disposed && _mobileChatInput != null && !_mobileChatInput._disposed && _mobileChatSendButton != null && !_mobileChatSendButton._disposed)
                return;

            _nextMobileChatBindAttemptUtc = DateTime.UtcNow.AddSeconds(2);

            string inputSpec = string.Empty;
            string sendSpec = string.Empty;
            string logSpec = string.Empty;

            try
            {
                InIReader reader = TryCreateConfigReader();
                if (reader != null)
                {
                    inputSpec = reader.ReadString(FairyGuiConfigSectionName, MobileChatInputConfigKey, string.Empty, writeWhenNull: false);
                    sendSpec = reader.ReadString(FairyGuiConfigSectionName, MobileChatSendConfigKey, string.Empty, writeWhenNull: false);
                    logSpec = reader.ReadString(FairyGuiConfigSectionName, MobileChatLogConfigKey, string.Empty, writeWhenNull: false);
                }
            }
            catch
            {
                inputSpec = string.Empty;
                sendSpec = string.Empty;
                logSpec = string.Empty;
            }

            inputSpec = inputSpec?.Trim() ?? string.Empty;
            sendSpec = sendSpec?.Trim() ?? string.Empty;
            logSpec = logSpec?.Trim() ?? string.Empty;

            _mobileChatInputOverrideSpec = inputSpec;
            _mobileChatSendOverrideSpec = sendSpec;
            _mobileChatLogOverrideSpec = logSpec;

            string[] inputKeywords = DefaultChatInputKeywords;
            string[] sendKeywords = DefaultChatSendKeywords;
            string[] logKeywords = DefaultChatLogKeywords;

            GTextInput input = null;
            GButton sendButton = null;
            GTextField logField = null;

            var inputCandidates = new List<(int Score, GObject Target)>();
            var sendCandidates = new List<(int Score, GObject Target)>();
            var logCandidates = new List<(int Score, GObject Target)>();

            if (!string.IsNullOrWhiteSpace(inputSpec))
            {
                if (TryResolveMobileMainHudObjectBySpec(window, inputSpec, out GObject resolved, out string[] keywords))
                {
                    if (resolved is GTextInput resolvedInput && !resolvedInput._disposed)
                        input = resolvedInput;
                    else if (keywords != null && keywords.Length > 0)
                        inputKeywords = keywords;
                }
            }

            if (!string.IsNullOrWhiteSpace(sendSpec))
            {
                if (TryResolveMobileMainHudObjectBySpec(window, sendSpec, out GObject resolved, out string[] keywords))
                {
                    if (resolved is GButton resolvedButton && !resolvedButton._disposed)
                        sendButton = resolvedButton;
                    else if (keywords != null && keywords.Length > 0)
                        sendKeywords = keywords;
                }
            }

            if (!string.IsNullOrWhiteSpace(logSpec))
            {
                if (TryResolveMobileMainHudObjectBySpec(window, logSpec, out GObject resolved, out string[] keywords))
                {
                    if (resolved is GTextField resolvedText && resolved is not GTextInput && !resolvedText._disposed)
                        logField = resolvedText;
                    else if (keywords != null && keywords.Length > 0)
                        logKeywords = keywords;
                }
            }

            if (input == null)
            {
                inputCandidates = CollectMobileChatCandidates(window, obj => obj is GTextInput && obj.touchable, inputKeywords, ScoreMobileChatInputCandidate);
                input = SelectMobileChatCandidate<GTextInput>(inputCandidates, minScore: 40);
            }

            if (sendButton == null)
            {
                sendCandidates = CollectMobileChatCandidates(window, obj => obj is GButton && obj.touchable, sendKeywords, ScoreMobileChatSendCandidate);
                sendButton = SelectMobileChatCandidate<GButton>(sendCandidates, minScore: 40);
            }

            if (logField == null)
            {
                logCandidates = CollectMobileChatCandidates(window, obj => obj is GTextField && obj is not GTextInput, logKeywords, ScoreMobileChatLogCandidate);
                logField = SelectMobileChatCandidate<GTextField>(logCandidates, minScore: 60);
            }

            if (input != null && ( _mobileChatInput == null || _mobileChatInput._disposed || !ReferenceEquals(_mobileChatInput, input)))
            {
                try
                {
                    if (_mobileChatInput != null && !_mobileChatInput._disposed && _mobileChatSubmitCallback != null)
                        _mobileChatInput.onSubmit.Remove(_mobileChatSubmitCallback);
                }
                catch
                {
                }

                _mobileChatInput = input;
            }

            if (sendButton != null && (_mobileChatSendButton == null || _mobileChatSendButton._disposed || !ReferenceEquals(_mobileChatSendButton, sendButton)))
            {
                try
                {
                    if (_mobileChatSendButton != null && !_mobileChatSendButton._disposed && _mobileChatSendClickCallback != null)
                        _mobileChatSendButton.onClick.Remove(_mobileChatSendClickCallback);
                }
                catch
                {
                }

                _mobileChatSendButton = sendButton;
            }

            if (logField != null)
                _mobileChatLogField = logField;

            // 放大移动端聊天字体（聊天记录 + 输入框），避免默认字号过小看不清。
            ApplyMobileChatFontSizes();

            try
            {
                if (_mobileChatSendClickCallback == null)
                    _mobileChatSendClickCallback = OnMobileChatSendClicked;

                if (_mobileChatSubmitCallback == null)
                    _mobileChatSubmitCallback = OnMobileChatSubmitClicked;

                if (_mobileChatSendButton != null && !_mobileChatSendButton._disposed)
                {
                    _mobileChatSendButton.onClick.Remove(_mobileChatSendClickCallback);
                    _mobileChatSendButton.onClick.Add(_mobileChatSendClickCallback);
                }

                if (_mobileChatInput != null && !_mobileChatInput._disposed)
                {
                    _mobileChatInput.onSubmit.Remove(_mobileChatSubmitCallback);
                    _mobileChatInput.onSubmit.Add(_mobileChatSubmitCallback);
                }
            }
            catch
            {
            }

            TryApplyPendingMobileChatPrefillIfDue();
            TryDumpMobileChatBindingsReportIfDue(windowKey, window, resolveInfo, inputKeywords, sendKeywords, logKeywords, inputCandidates, sendCandidates, logCandidates);
            TryRefreshMobileChatLogIfDue(force: true);
        }

        private static List<(int Score, GObject Target)> CollectMobileChatCandidates(GComponent root, Func<GObject, bool> filter, string[] keywords, Func<GObject, string[], int> scorer)
        {
            var list = new List<(int Score, GObject Target)>();

            if (root == null || root._disposed)
                return list;

            if (filter == null || scorer == null)
                return list;

            foreach (GObject obj in Enumerate(root))
            {
                if (obj == null || obj._disposed)
                    continue;

                if (ReferenceEquals(obj, root))
                    continue;

                if (!filter(obj))
                    continue;

                int score = 0;
                try
                {
                    score = scorer(obj, keywords);
                }
                catch
                {
                    score = 0;
                }

                list.Add((score, obj));
            }

            list.Sort((a, b) => b.Score.CompareTo(a.Score));
            return list;
        }

        private static T SelectMobileChatCandidate<T>(List<(int Score, GObject Target)> candidates, int minScore) where T : GObject
        {
            if (candidates == null || candidates.Count == 0)
                return null;

            (int score, GObject target) = candidates[0];
            if (score < minScore)
                return null;

            return target as T;
        }

        private static int ScoreMobileChatInputCandidate(GObject obj, string[] keywords)
        {
            int score = ScoreAnyField(obj, keywords, equalsWeight: 140, startsWithWeight: 70, containsWeight: 30);
            score += ScoreRect(obj, preferLower: true, areaDivisor: 1500, maxAreaScore: 140);
            if (obj.packageItem?.exported == true)
                score += 20;
            return score;
        }

        private static int ScoreMobileChatSendCandidate(GObject obj, string[] keywords)
        {
            int score = ScoreAnyField(obj, keywords, equalsWeight: 160, startsWithWeight: 80, containsWeight: 35);
            score += ScoreRect(obj, preferLower: true, areaDivisor: 900, maxAreaScore: 120);
            if (obj.packageItem?.exported == true)
                score += 15;
            return score;
        }

        private static int ScoreMobileChatLogCandidate(GObject obj, string[] keywords)
        {
            int score = ScoreAnyField(obj, keywords, equalsWeight: 140, startsWithWeight: 70, containsWeight: 25);
            score += ScoreRect(obj, preferLower: false, areaDivisor: 1200, maxAreaScore: 220);

            if (obj is GRichTextField)
                score += 30;

            if (obj.packageItem?.exported == true)
                score += 15;

            return score;
        }

        private static int ScoreAnyField(GObject obj, string[] keywords, int equalsWeight, int startsWithWeight, int containsWeight)
        {
            if (obj == null || keywords == null || keywords.Length == 0)
                return 0;

            string name = obj.name;
            string item = obj.packageItem?.name;
            string url = obj.resourceURL;
            string title = (obj as GButton)?.title;

            if (!string.IsNullOrWhiteSpace(title))
                title = title.Replace("\r", string.Empty).Replace("\n", string.Empty);

            int score = 0;

            for (int i = 0; i < keywords.Length; i++)
            {
                string keyword = keywords[i];
                if (string.IsNullOrWhiteSpace(keyword))
                    continue;

                keyword = keyword.Trim();
                if (keyword.Length == 0)
                    continue;

                score += ScoreField(name, keyword, equalsWeight, startsWithWeight, containsWeight);
                score += ScoreField(item, keyword, equalsWeight, startsWithWeight, containsWeight);
                score += ScoreField(url, keyword, equalsWeight - 20, startsWithWeight - 20, Math.Max(10, containsWeight - 10));
                score += ScoreField(title, keyword, equalsWeight + 20, startsWithWeight + 10, containsWeight);
            }

            return score;
        }

        private static int ScoreRect(GObject obj, bool preferLower, float areaDivisor, int maxAreaScore)
        {
            if (obj == null || obj._disposed)
                return 0;

            float area = Math.Max(0, obj.width) * Math.Max(0, obj.height);
            int score = (int)Math.Min(maxAreaScore, areaDivisor <= 1 ? area : area / areaDivisor);

            if (!preferLower)
                return score;

            try
            {
                var global = obj.LocalToGlobal(new System.Drawing.RectangleF(0, 0, obj.width, obj.height));
                score += (int)Math.Min(80, Math.Max(0, global.Y) / 40f);
            }
            catch
            {
            }

            return score;
        }

        private static void OnMobileChatSendClicked()
        {
            TrySendMobileChatMessageFromInput();
        }

        private static void OnMobileChatSubmitClicked()
        {
            TrySendMobileChatMessageFromInput();
        }

        private static void TrySendMobileChatMessageFromInput()
        {
            if (_mobileChatInput == null || _mobileChatInput._disposed)
                return;

            string text;
            try
            {
                text = _mobileChatInput.text ?? string.Empty;
            }
            catch
            {
                text = string.Empty;
            }

            text = text.Trim();
            if (text.Length == 0)
                return;

            try
            {
                _mobileChatInput.text = string.Empty;
            }
            catch
            {
            }

            try
            {
                MonoShare.MirNetwork.Network.Enqueue(new C.Chat { Message = text });
            }
            catch (Exception ex)
            {
                CMain.SaveError("FairyGUI: 发送聊天失败：" + ex.Message);
            }
        }

        private static void TryRefreshMobileChatLogIfDue(bool force)
        {
            if (!force && !_mobileChatDirty)
                return;

            if (_mobileChatLogField == null || _mobileChatLogField._disposed)
                return;

            try
            {
                string text = BuildMobileChatLogText(maxLines: 80);
                _mobileChatLogField.text = text;
                ApplyMobileChatFontSizes();
                _mobileChatDirty = false;
            }
            catch
            {
            }
        }

        private static string BuildMobileChatLogText(int maxLines)
        {
            if (maxLines <= 0)
                maxLines = 1;

            int count = MobileChatEntries.Count;
            if (count == 0)
                return string.Empty;

            int start = Math.Max(0, count - maxLines);
            var builder = new StringBuilder(4 * 1024);

            for (int i = start; i < count; i++)
            {
                MobileChatEntry entry = MobileChatEntries[i];
                string line = entry.Message ?? string.Empty;
                if (line.Contains('\n'))
                    line = line.Replace("\r", string.Empty).Replace("\n", " ");

                builder.Append(GetChatTypeTag(entry.Type));
                builder.Append(' ');
                builder.Append(line);

                if (i < count - 1)
                    builder.AppendLine();
            }

            return builder.ToString();
        }

        private static string GetChatTypeTag(ChatType type)
        {
            if (ChatTypeTagCache.TryGetValue(type, out string cached) && !string.IsNullOrWhiteSpace(cached))
                return cached;

            string value = null;

            try
            {
                FieldInfo field = typeof(ChatType).GetField(type.ToString());
                DescriptionAttribute attr = field?.GetCustomAttribute<DescriptionAttribute>();
                value = attr?.Description;
            }
            catch
            {
            }

            if (string.IsNullOrWhiteSpace(value))
                value = "[" + type + "]";

            ChatTypeTagCache[type] = value;
            return value;
        }

        private static void TryDumpMobileChatBindingsReportIfDue(
            string windowKey,
            GComponent window,
            string resolveInfo,
            string[] inputKeywords,
            string[] sendKeywords,
            string[] logKeywords,
            List<(int Score, GObject Target)> inputCandidates,
            List<(int Score, GObject Target)> sendCandidates,
            List<(int Score, GObject Target)> logCandidates)
        {
            if (!Settings.DebugMode && !Settings.LogErrors)
                return;

            if (_mobileChatBindingsDumped)
                return;

            if (window == null || window._disposed)
                return;

            try
            {
                Directory.CreateDirectory(ClientResourceLayout.RuntimeRoot);

                string path = Path.Combine(ClientResourceLayout.RuntimeRoot, "FairyGui-MobileChatBindings.txt");

                var builder = new StringBuilder(8 * 1024);
                builder.AppendLine("FairyGUI 聊天窗口绑定报告（用于排障/补充映射）");
                builder.AppendLine($"GeneratedAtUtc={DateTime.UtcNow:o}");
                builder.AppendLine($"WindowKey={windowKey}");
                if (!string.IsNullOrWhiteSpace(resolveInfo))
                    builder.AppendLine($"Resolved={resolveInfo}");
                builder.AppendLine();
                builder.AppendLine("配置覆盖（可选）：Mir2Config.ini -> [FairyGUI]");
                builder.AppendLine("  MobileChat.Log=<Spec>");
                builder.AppendLine("  MobileChat.Input=<Spec>");
                builder.AppendLine("  MobileChat.Send=<Spec>");
                builder.AppendLine("  Spec 支持：path:... / idx:... / name:... / item:... / url:... / title:... / 或者关键字列表(a|b|c)");
                builder.AppendLine();
                builder.AppendLine($"Override.Log={(string.IsNullOrWhiteSpace(_mobileChatLogOverrideSpec) ? "-" : _mobileChatLogOverrideSpec)}");
                builder.AppendLine($"Override.Input={(string.IsNullOrWhiteSpace(_mobileChatInputOverrideSpec) ? "-" : _mobileChatInputOverrideSpec)}");
                builder.AppendLine($"Override.Send={(string.IsNullOrWhiteSpace(_mobileChatSendOverrideSpec) ? "-" : _mobileChatSendOverrideSpec)}");
                builder.AppendLine();
                builder.AppendLine($"Keywords.Log={(logKeywords == null ? "-" : string.Join("|", logKeywords))}");
                builder.AppendLine($"Keywords.Input={(inputKeywords == null ? "-" : string.Join("|", inputKeywords))}");
                builder.AppendLine($"Keywords.Send={(sendKeywords == null ? "-" : string.Join("|", sendKeywords))}");
                builder.AppendLine();
                builder.AppendLine($"Selected.Log={DescribeObject(window, _mobileChatLogField)}");
                builder.AppendLine($"Selected.Input={DescribeObject(window, _mobileChatInput)}");
                builder.AppendLine($"Selected.Send={DescribeObject(window, _mobileChatSendButton)}");
                builder.AppendLine();

                AppendCandidateList(builder, window, "Candidates.Log(top)", logCandidates);
                AppendCandidateList(builder, window, "Candidates.Input(top)", inputCandidates);
                AppendCandidateList(builder, window, "Candidates.Send(top)", sendCandidates);

                File.WriteAllText(path, builder.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                _mobileChatBindingsDumped = true;
                CMain.SaveLog($"FairyGUI: 已导出聊天窗口绑定报告到 {path}");
            }
            catch (Exception ex)
            {
                CMain.SaveError("FairyGUI: 导出聊天窗口绑定报告失败：" + ex.Message);
            }
        }

        private static void AppendCandidateList(StringBuilder builder, GComponent root, string title, List<(int Score, GObject Target)> candidates)
        {
            if (builder == null)
                return;

            builder.AppendLine(title + ":");
            if (candidates == null || candidates.Count == 0)
            {
                builder.AppendLine("  (none)");
                builder.AppendLine();
                return;
            }

            int top = Math.Min(8, candidates.Count);
            for (int i = 0; i < top; i++)
            {
                (int score, GObject target) = candidates[i];
                builder.Append("  score=").Append(score).Append(' ');
                builder.AppendLine(DescribeObject(root, target));
            }

            builder.AppendLine();
        }

        private static void DetachMobileItemGridSlotCallbacks(MobileItemGridBinding binding)
        {
            if (binding == null)
                return;

            for (int i = 0; i < binding.Slots.Count; i++)
            {
                MobileItemSlotBinding slot = binding.Slots[i];
                if (slot == null)
                    continue;

                try
                {
                    UnbindMobileLongPressItemTips(slot.LongPressTipBinding);
                }
                catch
                {
                }

                slot.LongPressTipBinding = null;

                try
                {
                    UnbindMobileLongPressItemDrag(slot.LongPressDragBinding);
                }
                catch
                {
                }

                slot.LongPressDragBinding = null;

                try
                {
                    if (slot.Root != null && !slot.Root._disposed && slot.ClickCallback != null)
                        slot.Root.onClick.Remove(slot.ClickCallback);
                }
                catch
                {
                }

                try
                {
                    if (slot.Root != null && !slot.Root._disposed && slot.DropCallback != null)
                        slot.Root.RemoveEventListener("onDrop", slot.DropCallback);
                }
                catch
                {
                }

                try
                {
                    if (slot.Root != null && !slot.Root._disposed && slot.OriginalAlphaCaptured)
                        slot.Root.alpha = slot.OriginalAlpha;
                }
                catch
                {
                }

                slot.ClickCallback = null;
                slot.DropCallback = null;
            }
        }

        private static void ResetMobileInventoryBindings()
        {
            try
            {
                MobileItemGridBinding binding = _mobileInventoryBinding;
                if (binding != null)
                {
                    DetachMobileItemGridSlotCallbacks(binding);

                    try
                    {
                        if (binding.WarehouseButton != null && !binding.WarehouseButton._disposed && binding.WarehouseButtonClickCallback != null)
                            binding.WarehouseButton.onClick.Remove(binding.WarehouseButtonClickCallback);
                    }
                    catch
                    {
                    }

                    try
                    {
                        if (binding.SortButton != null && !binding.SortButton._disposed && binding.SortButtonClickCallback != null)
                            binding.SortButton.onClick.Remove(binding.SortButtonClickCallback);
                    }
                    catch
                    {
                    }

                    try
                    {
                        for (int i = 0; i < binding.PageTabs.Length; i++)
                        {
                            GButton tab = binding.PageTabs[i];
                            EventCallback0 tabCallback = binding.PageTabClickCallbacks[i];

                            if (tab != null && !tab._disposed && tabCallback != null)
                                tab.onClick.Remove(tabCallback);

                            binding.PageTabs[i] = null;
                            binding.PageTabClickCallbacks[i] = null;
                        }
                    }
                    catch
                    {
                    }
                }
            }
            catch
            {
            }

            _mobileInventoryBinding = null;
            _nextMobileInventoryBindAttemptUtc = DateTime.MinValue;
            _mobileInventoryBindingsDumped = false;
        }

        private static void ResetMobileStorageBindings()
        {
            try
            {
                if (_mobileStorageBinding != null)
                {
                    DetachMobileItemGridSlotCallbacks(_mobileStorageBinding.InventoryGrid);
                    DetachMobileItemGridSlotCallbacks(_mobileStorageBinding.StorageGrid);
                }
            }
            catch
            {
            }

            _mobileStorageBinding = null;
            _nextMobileStorageBindAttemptUtc = DateTime.MinValue;
            _mobileStorageBindingsDumped = false;
        }

        private static void ResetMobileSystemBindings()
        {
            try
            {
                MobileSystemWindowBinding binding = _mobileSystemBinding;
                if (binding != null)
                {
                    try
                    {
                        if (binding.VolumeSlider != null && !binding.VolumeSlider._disposed && binding.VolumeChangedCallback != null)
                            binding.VolumeSlider.onChanged.Remove(binding.VolumeChangedCallback);
                    }
                    catch
                    {
                    }

                    try
                    {
                        if (binding.VolumeSlider != null && !binding.VolumeSlider._disposed && binding.VolumeGripTouchEndCallback != null)
                            binding.VolumeSlider.onGripTouchEnd.Remove(binding.VolumeGripTouchEndCallback);
                    }
                    catch
                    {
                    }

                    try
                    {
                        if (binding.MusicSlider != null && !binding.MusicSlider._disposed && binding.MusicChangedCallback != null)
                            binding.MusicSlider.onChanged.Remove(binding.MusicChangedCallback);
                    }
                    catch
                    {
                    }

                    try
                    {
                        if (binding.MusicSlider != null && !binding.MusicSlider._disposed && binding.MusicGripTouchEndCallback != null)
                            binding.MusicSlider.onGripTouchEnd.Remove(binding.MusicGripTouchEndCallback);
                    }
                    catch
                    {
                    }

                    try
                    {
                        for (int i = 0; i < binding.Toggles.Count; i++)
                        {
                            MobileSystemToggleBinding toggle = binding.Toggles[i];
                            if (toggle == null)
                                continue;

                            if (toggle.Button != null && !toggle.Button._disposed && toggle.ChangedCallback != null)
                                toggle.Button.onChanged.Remove(toggle.ChangedCallback);
                        }
                    }
                    catch
                    {
                    }
                }
            }
            catch
            {
            }

            _mobileSystemBinding = null;
            _nextMobileSystemBindAttemptUtc = DateTime.MinValue;
            _nextMobileSystemSyncUtc = DateTime.MinValue;
            _mobileSystemBindingsDumped = false;
        }

        private static void ResetMobileMagicBindings()
        {
            try
            {
                MobileMagicWindowBinding binding = _mobileMagicBinding;
                if (binding != null)
                {
                    try
                    {
                        for (int i = 0; i < binding.Slots.Count; i++)
                        {
                            MobileMagicSlotBinding slot = binding.Slots[i];
                            if (slot == null)
                                continue;

                            try
                            {
                                if (slot.LongPressDragBinding != null)
                                    UnbindMobileLongPressMagicDrag(slot.LongPressDragBinding);
                            }
                            catch
                            {
                            }

                            slot.LongPressDragBinding = null;

                            if (slot.Root == null || slot.Root._disposed || slot.ClickCallback == null)
                                continue;

                            slot.Root.onClick.Remove(slot.ClickCallback);
                            slot.ClickCallback = null;
                        }
                    }
                    catch
                    {
                    }
                }
            }
            catch
            {
            }

            _mobileMagicBinding = null;
            _nextMobileMagicBindAttemptUtc = DateTime.MinValue;
            _mobileMagicBindingsDumped = false;
        }

        private static void ResetMobileShopBindings()
        {
            try
            {
                MobileShopWindowBinding binding = _mobileShopBinding;
                if (binding != null)
                {
                    try
                    {
                        if (binding.ItemList != null && !binding.ItemList._disposed)
                            binding.ItemList.itemRenderer = null;
                    }
                    catch
                    {
                    }
                }
            }
            catch
            {
            }

            _mobileShopBinding = null;
            _nextMobileShopBindAttemptUtc = DateTime.MinValue;
            _mobileShopBindingsDumped = false;
            _mobileShopDirty = false;
        }

        private static void ResetMobileFriendBindings()
        {
            try
            {
                MobileFriendWindowBinding binding = _mobileFriendBinding;
                if (binding != null)
                {
                    try
                    {
                        if (binding.FriendList != null && !binding.FriendList._disposed)
                            binding.FriendList.itemRenderer = null;
                    }
                    catch
                    {
                    }

                    try
                    {
                        if (binding.AddButton != null && !binding.AddButton._disposed && binding.AddClickCallback != null)
                            binding.AddButton.onClick.Remove(binding.AddClickCallback);
                    }
                    catch
                    {
                    }

                    try
                    {
                        if (binding.RemoveButton != null && !binding.RemoveButton._disposed && binding.RemoveClickCallback != null)
                            binding.RemoveButton.onClick.Remove(binding.RemoveClickCallback);
                    }
                    catch
                    {
                    }

                    try
                    {
                        if (binding.BlockButton != null && !binding.BlockButton._disposed && binding.BlockClickCallback != null)
                            binding.BlockButton.onClick.Remove(binding.BlockClickCallback);
                    }
                    catch
                    {
                    }

                    try
                    {
                        if (binding.MemoButton != null && !binding.MemoButton._disposed && binding.MemoClickCallback != null)
                            binding.MemoButton.onClick.Remove(binding.MemoClickCallback);
                    }
                    catch
                    {
                    }

                    try
                    {
                        if (binding.RefreshButton != null && !binding.RefreshButton._disposed && binding.RefreshClickCallback != null)
                            binding.RefreshButton.onClick.Remove(binding.RefreshClickCallback);
                    }
                    catch
                    {
                    }
                }
            }
            catch
            {
            }

            _mobileFriendBinding = null;
            _nextMobileFriendBindAttemptUtc = DateTime.MinValue;
            _mobileFriendBindingsDumped = false;
            _mobileFriendDirty = false;
        }

        private static void ResetMobileGuildBindings()
        {
            try
            {
                MobileGuildWindowBinding binding = _mobileGuildBinding;
                if (binding != null)
                {
                    try
                    {
                        if (binding.MemberList != null && !binding.MemberList._disposed)
                            binding.MemberList.itemRenderer = null;
                    }
                    catch
                    {
                    }

                    try
                    {
                        if (binding.InviteButton != null && !binding.InviteButton._disposed && binding.InviteClickCallback != null)
                            binding.InviteButton.onClick.Remove(binding.InviteClickCallback);
                    }
                    catch
                    {
                    }

                    try
                    {
                        if (binding.KickButton != null && !binding.KickButton._disposed && binding.KickClickCallback != null)
                            binding.KickButton.onClick.Remove(binding.KickClickCallback);
                    }
                    catch
                    {
                    }
                }
            }
            catch
            {
            }

            _mobileGuildBinding = null;
            _nextMobileGuildBindAttemptUtc = DateTime.MinValue;
            _mobileGuildBindingsDumped = false;
            _mobileGuildDirty = false;
        }

        private static void ResetMobileGroupBindings()
        {
            try
            {
                MobileGroupWindowBinding binding = _mobileGroupBinding;
                if (binding != null)
                {
                    try
                    {
                        if (binding.MemberList != null && !binding.MemberList._disposed)
                            binding.MemberList.itemRenderer = null;
                    }
                    catch
                    {
                    }

                    try
                    {
                        if (binding.InviteButton != null && !binding.InviteButton._disposed && binding.InviteClickCallback != null)
                            binding.InviteButton.onClick.Remove(binding.InviteClickCallback);
                    }
                    catch
                    {
                    }

                    try
                    {
                        if (binding.KickButton != null && !binding.KickButton._disposed && binding.KickClickCallback != null)
                            binding.KickButton.onClick.Remove(binding.KickClickCallback);
                    }
                    catch
                    {
                    }

                    try
                    {
                        if (binding.LeaveButton != null && !binding.LeaveButton._disposed && binding.LeaveClickCallback != null)
                            binding.LeaveButton.onClick.Remove(binding.LeaveClickCallback);
                    }
                    catch
                    {
                    }
                }
            }
            catch
            {
            }

            _mobileGroupBinding = null;
            _nextMobileGroupBindAttemptUtc = DateTime.MinValue;
            _mobileGroupBindingsDumped = false;
            _mobileGroupDirty = false;
        }

        private static void TryBindMobileSystemWindowIfDue(string windowKey, GComponent window, string resolveInfo)
        {
            if (window == null || window._disposed)
                return;

            MobileSystemWindowBinding binding = _mobileSystemBinding;
            if (binding != null && (binding.Window == null || binding.Window._disposed))
            {
                ResetMobileSystemBindings();
                binding = null;
            }

            if (binding == null || !ReferenceEquals(binding.Window, window))
            {
                ResetMobileSystemBindings();

                binding = new MobileSystemWindowBinding
                {
                    WindowKey = windowKey,
                    Window = window,
                    ResolveInfo = resolveInfo,
                };

                EnsureMobileSystemToggleSpecs(binding);

                _mobileSystemBinding = binding;
                _nextMobileSystemBindAttemptUtc = DateTime.MinValue;
                _nextMobileSystemSyncUtc = DateTime.MinValue;
                _mobileSystemBindingsDumped = false;
            }

            if (DateTime.UtcNow < _nextMobileSystemBindAttemptUtc)
                return;

            bool volumeBound = binding.VolumeSlider != null && !binding.VolumeSlider._disposed;
            bool musicBound = binding.MusicSlider != null && !binding.MusicSlider._disposed;

            bool allTogglesBound = true;
            for (int i = 0; i < binding.Toggles.Count; i++)
            {
                MobileSystemToggleBinding toggle = binding.Toggles[i];
                if (toggle == null)
                    continue;

                if (toggle.Button == null || toggle.Button._disposed)
                {
                    allTogglesBound = false;
                    break;
                }
            }

            if (volumeBound && musicBound && allTogglesBound)
                return;

            _nextMobileSystemBindAttemptUtc = DateTime.UtcNow.AddSeconds(2);

            string volumeOverrideSpec = string.Empty;
            string musicOverrideSpec = string.Empty;

            try
            {
                InIReader reader = TryCreateConfigReader();
                if (reader != null)
                {
                    volumeOverrideSpec = reader.ReadString(FairyGuiConfigSectionName, MobileSystemVolumeConfigKey, string.Empty, writeWhenNull: false);
                    musicOverrideSpec = reader.ReadString(FairyGuiConfigSectionName, MobileSystemMusicConfigKey, string.Empty, writeWhenNull: false);

                    for (int i = 0; i < binding.Toggles.Count; i++)
                    {
                        MobileSystemToggleBinding toggle = binding.Toggles[i];
                        if (toggle == null || string.IsNullOrWhiteSpace(toggle.ConfigKey))
                            continue;

                        toggle.OverrideSpec = reader.ReadString(FairyGuiConfigSectionName, toggle.ConfigKey, string.Empty, writeWhenNull: false);
                    }
                }
            }
            catch
            {
                volumeOverrideSpec = string.Empty;
                musicOverrideSpec = string.Empty;
            }

            binding.VolumeOverrideSpec = volumeOverrideSpec?.Trim() ?? string.Empty;
            binding.MusicOverrideSpec = musicOverrideSpec?.Trim() ?? string.Empty;
            binding.VolumeOverrideKeywords = null;
            binding.MusicOverrideKeywords = null;

            for (int i = 0; i < binding.Toggles.Count; i++)
            {
                MobileSystemToggleBinding toggle = binding.Toggles[i];
                if (toggle == null)
                    continue;

                toggle.OverrideSpec = toggle.OverrideSpec?.Trim() ?? string.Empty;
                toggle.OverrideKeywords = null;
            }

            var used = new HashSet<GObject>();

            // Volume slider
            if (binding.VolumeSlider == null || binding.VolumeSlider._disposed)
            {
                binding.VolumeSlider = null;
                binding.VolumeResolveInfo = null;

                if (!string.IsNullOrWhiteSpace(binding.VolumeOverrideSpec))
                {
                    if (TryResolveMobileMainHudObjectBySpec(window, binding.VolumeOverrideSpec, out GObject resolved, out string[] keywords))
                    {
                        if (resolved is GSlider slider && !slider._disposed)
                        {
                            binding.VolumeSlider = slider;
                            binding.VolumeResolveInfo = DescribeObject(window, slider) + " (override)";
                            used.Add(slider);
                        }
                        else if (keywords != null && keywords.Length > 0)
                        {
                            binding.VolumeOverrideKeywords = keywords;
                        }
                    }
                    else
                    {
                        binding.VolumeOverrideKeywords = SplitKeywords(binding.VolumeOverrideSpec);
                    }
                }

                if (binding.VolumeSlider == null)
                {
                    string[] keywords = binding.VolumeOverrideKeywords != null && binding.VolumeOverrideKeywords.Length > 0
                        ? binding.VolumeOverrideKeywords
                        : DefaultSystemVolumeKeywords;

                    int minScore = binding.VolumeOverrideKeywords != null && binding.VolumeOverrideKeywords.Length > 0 ? 40 : 60;
                    List<(int Score, GObject Target)> candidates = CollectMobileChatCandidates(window, obj => obj is GSlider && obj.touchable, keywords, ScoreMobileSystemSliderCandidate);
                    GSlider selected = SelectBestUnusedCandidate<GSlider>(candidates, minScore, used);
                    if (selected != null && !selected._disposed)
                    {
                        binding.VolumeSlider = selected;
                        binding.VolumeResolveInfo = DescribeObject(window, selected) + (binding.VolumeOverrideKeywords != null && binding.VolumeOverrideKeywords.Length > 0 ? " (keywords)" : " (auto)");
                        used.Add(selected);
                    }
                }

                if (binding.VolumeSlider != null && !binding.VolumeSlider._disposed && binding.VolumeChangedCallback == null)
                {
                    EventCallback0 callback = () =>
                    {
                        OnMobileSystemVolumeSliderChanged(binding);
                        if (!binding.Syncing)
                            TrySaveSettingsIfDue();
                    };

                    binding.VolumeChangedCallback = callback;
                    binding.VolumeSlider.onChanged.Add(callback);
                }

                if (binding.VolumeSlider != null && !binding.VolumeSlider._disposed && binding.VolumeGripTouchEndCallback == null)
                {
                    EventCallback0 callback = () =>
                    {
                        OnMobileSystemVolumeSliderChanged(binding);
                        if (!binding.Syncing)
                            TrySaveSettingsIfDue();
                    };

                    binding.VolumeGripTouchEndCallback = callback;
                    binding.VolumeSlider.onGripTouchEnd.Add(callback);
                }
            }
            else
            {
                used.Add(binding.VolumeSlider);
            }

            // Music slider
            if (binding.MusicSlider == null || binding.MusicSlider._disposed)
            {
                binding.MusicSlider = null;
                binding.MusicResolveInfo = null;

                if (!string.IsNullOrWhiteSpace(binding.MusicOverrideSpec))
                {
                    if (TryResolveMobileMainHudObjectBySpec(window, binding.MusicOverrideSpec, out GObject resolved, out string[] keywords))
                    {
                        if (resolved is GSlider slider && !slider._disposed)
                        {
                            binding.MusicSlider = slider;
                            binding.MusicResolveInfo = DescribeObject(window, slider) + " (override)";
                            used.Add(slider);
                        }
                        else if (keywords != null && keywords.Length > 0)
                        {
                            binding.MusicOverrideKeywords = keywords;
                        }
                    }
                    else
                    {
                        binding.MusicOverrideKeywords = SplitKeywords(binding.MusicOverrideSpec);
                    }
                }

                if (binding.MusicSlider == null)
                {
                    string[] keywords = binding.MusicOverrideKeywords != null && binding.MusicOverrideKeywords.Length > 0
                        ? binding.MusicOverrideKeywords
                        : DefaultSystemMusicKeywords;

                    int minScore = binding.MusicOverrideKeywords != null && binding.MusicOverrideKeywords.Length > 0 ? 40 : 60;
                    List<(int Score, GObject Target)> candidates = CollectMobileChatCandidates(window, obj => obj is GSlider && obj.touchable, keywords, ScoreMobileSystemSliderCandidate);
                    GSlider selected = SelectBestUnusedCandidate<GSlider>(candidates, minScore, used);
                    if (selected != null && !selected._disposed)
                    {
                        binding.MusicSlider = selected;
                        binding.MusicResolveInfo = DescribeObject(window, selected) + (binding.MusicOverrideKeywords != null && binding.MusicOverrideKeywords.Length > 0 ? " (keywords)" : " (auto)");
                        used.Add(selected);
                    }
                }

                if (binding.MusicSlider != null && !binding.MusicSlider._disposed && binding.MusicChangedCallback == null)
                {
                    EventCallback0 callback = () =>
                    {
                        OnMobileSystemMusicSliderChanged(binding);
                        if (!binding.Syncing)
                            TrySaveSettingsIfDue();
                    };

                    binding.MusicChangedCallback = callback;
                    binding.MusicSlider.onChanged.Add(callback);
                }

                if (binding.MusicSlider != null && !binding.MusicSlider._disposed && binding.MusicGripTouchEndCallback == null)
                {
                    EventCallback0 callback = () =>
                    {
                        OnMobileSystemMusicSliderChanged(binding);
                        if (!binding.Syncing)
                            TrySaveSettingsIfDue();
                    };

                    binding.MusicGripTouchEndCallback = callback;
                    binding.MusicSlider.onGripTouchEnd.Add(callback);
                }
            }
            else
            {
                used.Add(binding.MusicSlider);
            }

            // Toggles
            for (int i = 0; i < binding.Toggles.Count; i++)
            {
                MobileSystemToggleBinding toggle = binding.Toggles[i];
                if (toggle == null)
                    continue;

                if (toggle.Button != null && !toggle.Button._disposed)
                {
                    used.Add(toggle.Button);
                    continue;
                }

                toggle.Button = null;
                toggle.ResolveInfo = null;

                if (!string.IsNullOrWhiteSpace(toggle.OverrideSpec))
                {
                    if (TryResolveMobileMainHudObjectBySpec(window, toggle.OverrideSpec, out GObject resolved, out string[] keywords))
                    {
                        if (resolved is GButton button && !button._disposed)
                        {
                            toggle.Button = button;
                            toggle.ResolveInfo = DescribeObject(window, button) + " (override)";
                            used.Add(button);
                        }
                        else if (keywords != null && keywords.Length > 0)
                        {
                            toggle.OverrideKeywords = keywords;
                        }
                    }
                    else
                    {
                        toggle.OverrideKeywords = SplitKeywords(toggle.OverrideSpec);
                    }
                }

                if (toggle.Button == null)
                {
                    string[] keywords = toggle.OverrideKeywords != null && toggle.OverrideKeywords.Length > 0
                        ? toggle.OverrideKeywords
                        : (toggle.DefaultKeywords ?? Array.Empty<string>());

                    int minScore = toggle.OverrideKeywords != null && toggle.OverrideKeywords.Length > 0 ? 40 : 60;
                    List<(int Score, GObject Target)> candidates = CollectMobileChatCandidates(window, obj => obj is GButton && obj.touchable, keywords, ScoreMobileSystemToggleCandidate);
                    GButton selected = SelectBestUnusedCandidate<GButton>(candidates, minScore, used);
                    if (selected != null && !selected._disposed)
                    {
                        toggle.Button = selected;
                        toggle.ResolveInfo = DescribeObject(window, selected) + (toggle.OverrideKeywords != null && toggle.OverrideKeywords.Length > 0 ? " (keywords)" : " (auto)");
                        used.Add(selected);
                    }
                }

                if (toggle.Button != null && !toggle.Button._disposed && toggle.ChangedCallback == null)
                {
                    EventCallback0 callback = () => OnMobileSystemToggleChanged(binding, toggle);
                    toggle.ChangedCallback = callback;
                    toggle.Button.onChanged.Add(callback);
                }
            }

            TryDumpMobileSystemBindingsReportIfDue(binding);
        }

        private static void TryDumpMobileSystemBindingsReportIfDue(MobileSystemWindowBinding binding)
        {
            if (!Settings.DebugMode || _mobileSystemBindingsDumped)
                return;

            if (binding == null || binding.Window == null || binding.Window._disposed)
                return;

            try
            {
                Directory.CreateDirectory(ClientResourceLayout.RuntimeRoot);

                string path = Path.Combine(ClientResourceLayout.RuntimeRoot, "FairyGui-MobileSystemBindings.txt");

                var builder = new StringBuilder(16 * 1024);
                builder.AppendLine("FairyGUI 系统设置窗口绑定报告（System）");
                builder.AppendLine($"GeneratedAtUtc={DateTime.UtcNow:o}");
                builder.AppendLine($"WindowKey={binding.WindowKey ?? "System"}");
                builder.AppendLine($"WindowResolveInfo={binding.ResolveInfo ?? "-"}");
                builder.AppendLine();

                builder.AppendLine("Sliders:");
                builder.AppendLine($"  Volume={DescribeObject(binding.Window, binding.VolumeSlider)}");
                builder.AppendLine($"  VolumeResolveInfo={binding.VolumeResolveInfo ?? "-"}");
                builder.AppendLine($"  VolumeOverrideSpec={binding.VolumeOverrideSpec ?? "-"}");
                builder.AppendLine($"  VolumeOverrideKeywords={(binding.VolumeOverrideKeywords == null ? "-" : string.Join("|", binding.VolumeOverrideKeywords))}");
                builder.AppendLine($"  Music={DescribeObject(binding.Window, binding.MusicSlider)}");
                builder.AppendLine($"  MusicResolveInfo={binding.MusicResolveInfo ?? "-"}");
                builder.AppendLine($"  MusicOverrideSpec={binding.MusicOverrideSpec ?? "-"}");
                builder.AppendLine($"  MusicOverrideKeywords={(binding.MusicOverrideKeywords == null ? "-" : string.Join("|", binding.MusicOverrideKeywords))}");
                builder.AppendLine();

                builder.AppendLine($"Toggles({binding.Toggles.Count}):");
                for (int i = 0; i < binding.Toggles.Count; i++)
                {
                    MobileSystemToggleBinding toggle = binding.Toggles[i];
                    if (toggle == null)
                        continue;

                    builder.AppendLine($"  - {toggle.Key}({toggle.DisplayName}) => {DescribeObject(binding.Window, toggle.Button)}");
                    builder.AppendLine($"    ResolveInfo={toggle.ResolveInfo ?? "-"}");
                    builder.AppendLine($"    OverrideSpec={toggle.OverrideSpec ?? "-"}");
                    builder.AppendLine($"    OverrideKeywords={(toggle.OverrideKeywords == null ? "-" : string.Join("|", toggle.OverrideKeywords))}");
                    builder.AppendLine($"    DefaultKeywords={(toggle.DefaultKeywords == null ? "-" : string.Join("|", toggle.DefaultKeywords))}");
                }

                builder.AppendLine();
                builder.AppendLine("配置覆盖（可选）：Mir2Config.ini -> [FairyGUI]");
                builder.AppendLine($"  {MobileSystemVolumeConfigKey}=idx:...  或 path:... 或 name:/item:/url:/title:...");
                builder.AppendLine($"  {MobileSystemMusicConfigKey}=idx:...  或 path:... 或 name:/item:/url:/title:...");
                for (int i = 0; i < binding.Toggles.Count; i++)
                {
                    MobileSystemToggleBinding toggle = binding.Toggles[i];
                    if (toggle == null || string.IsNullOrWhiteSpace(toggle.ConfigKey))
                        continue;

                    builder.AppendLine($"  {toggle.ConfigKey}=idx:...  （绑定 {toggle.DisplayName}）");
                }
                builder.AppendLine("说明：idx/path 均相对系统设置窗口 Root。建议先查看窗口树（FairyGui-MobileWindow-System-Tree.txt），再填入精确覆盖。");

                File.WriteAllText(path, builder.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                _mobileSystemBindingsDumped = true;
                CMain.SaveLog($"FairyGUI: 已导出系统设置窗口绑定报告到 {path}");
            }
            catch (Exception ex)
            {
                CMain.SaveError("FairyGUI: 导出系统设置窗口绑定报告失败：" + ex.Message);
            }
        }

        private static void TryRefreshMobileSystemIfDue(bool force)
        {
            if (_stage == null || !_initialized || !_packagesLoaded)
                return;

            if (!MobileWindows.TryGetValue("System", out GComponent window) || window == null || window._disposed)
            {
                if (_mobileSystemBinding != null)
                    ResetMobileSystemBindings();
                return;
            }

            if (!window.visible)
                return;

            TryBindMobileSystemWindowIfDue("System", window, resolveInfo: null);

            MobileSystemWindowBinding binding = _mobileSystemBinding;
            if (binding == null || binding.Window == null || binding.Window._disposed)
            {
                ResetMobileSystemBindings();
                return;
            }

            if (!force && DateTime.UtcNow < _nextMobileSystemSyncUtc)
                return;

            _nextMobileSystemSyncUtc = DateTime.UtcNow.AddMilliseconds(450);

            binding.Syncing = true;
            try
            {
                if (binding.VolumeSlider != null && !binding.VolumeSlider._disposed)
                {
                    try
                    {
                        double max = binding.VolumeSlider.max;
                        if (max <= 0.0001)
                            max = 100;

                        int volume = Math.Clamp(Settings.Volume, (byte)0, (byte)100);
                        binding.VolumeSlider.value = volume / 100d * max;
                    }
                    catch
                    {
                    }
                }

                if (binding.MusicSlider != null && !binding.MusicSlider._disposed)
                {
                    try
                    {
                        double max = binding.MusicSlider.max;
                        if (max <= 0.0001)
                            max = 100;

                        int music = Math.Clamp(Settings.MusicVolume, (byte)0, (byte)100);
                        binding.MusicSlider.value = music / 100d * max;
                    }
                    catch
                    {
                    }
                }

                for (int i = 0; i < binding.Toggles.Count; i++)
                {
                    MobileSystemToggleBinding toggle = binding.Toggles[i];
                    if (toggle == null)
                        continue;

                    if (toggle.Button == null || toggle.Button._disposed)
                        continue;

                    if (!TryGetMobileSystemSetting(toggle.Key, out bool selected))
                        continue;

                    try
                    {
                        toggle.Button.selected = selected;
                    }
                    catch
                    {
                    }
                }
            }
            finally
            {
                binding.Syncing = false;
            }
        }

        private static int ScoreMobileShopListCandidate(GObject obj, string[] keywords)
        {
            int score = ScoreAnyField(obj, keywords, equalsWeight: 140, startsWithWeight: 70, containsWeight: 25);
            score += ScoreRect(obj, preferLower: false, areaDivisor: 900, maxAreaScore: 260);

            if (obj is GList list)
            {
                if (list.scrollPane != null)
                    score += 30;

                if (!string.IsNullOrWhiteSpace(list.defaultItem))
                    score += 10;
            }

            if (obj.packageItem?.exported == true)
                score += 10;

            return score;
        }

        private static int ScoreMobileShopButtonCandidate(GObject obj, string[] keywords)
        {
            int score = ScoreAnyField(obj, keywords, equalsWeight: 160, startsWithWeight: 80, containsWeight: 35);
            score += ScoreRect(obj, preferLower: true, areaDivisor: 450, maxAreaScore: 90);
            if (obj.packageItem?.exported == true)
                score += 10;
            return score;
        }

        private static int ScoreMobileShopTextCandidate(GObject obj, string[] keywords)
        {
            int score = ScoreAnyField(obj, keywords, equalsWeight: 140, startsWithWeight: 70, containsWeight: 25);
            score += ScoreRect(obj, preferLower: false, areaDivisor: 800, maxAreaScore: 90);
            if (obj.packageItem?.exported == true)
                score += 10;
            return score;
        }

        private static MobileShopItemView GetOrCreateMobileShopItemView(GComponent itemRoot)
        {
            if (itemRoot == null || itemRoot._disposed)
                return null;

            if (itemRoot.data is MobileShopItemView existing && existing.Root != null && !existing.Root._disposed)
                return existing;

            var view = new MobileShopItemView
            {
                Root = itemRoot,
                HasItem = false,
                LastIcon = 0,
            };

            try
            {
                // Icon
                {
                    List<(int Score, GObject Target)> candidates = CollectMobileChatCandidates(itemRoot, obj => obj is GLoader, DefaultShopItemIconKeywords, ScoreMobileShopTextCandidate);
                    view.Icon = SelectMobileChatCandidate<GLoader>(candidates, minScore: 10);
                }

                // Name
                {
                    List<(int Score, GObject Target)> candidates = CollectMobileChatCandidates(itemRoot, obj => obj is GTextField && obj is not GTextInput, DefaultShopItemNameKeywords, ScoreMobileShopTextCandidate);
                    view.Name = SelectMobileChatCandidate<GTextField>(candidates, minScore: 20);
                }

                // Count
                {
                    List<(int Score, GObject Target)> candidates = CollectMobileChatCandidates(itemRoot, obj => obj is GTextField && obj is not GTextInput, DefaultShopItemCountKeywords, ScoreMobileShopTextCandidate);
                    view.Count = SelectMobileChatCandidate<GTextField>(candidates, minScore: 20);
                }

                // Stock
                {
                    List<(int Score, GObject Target)> candidates = CollectMobileChatCandidates(itemRoot, obj => obj is GTextField && obj is not GTextInput, DefaultShopItemStockKeywords, ScoreMobileShopTextCandidate);
                    view.Stock = SelectMobileChatCandidate<GTextField>(candidates, minScore: 20);
                }

                // Prices
                {
                    List<(int Score, GObject Target)> goldCandidates = CollectMobileChatCandidates(itemRoot, obj => obj is GTextField && obj is not GTextInput, DefaultShopItemPriceGoldKeywords, ScoreMobileShopTextCandidate);
                    view.GoldPrice = SelectMobileChatCandidate<GTextField>(goldCandidates, minScore: 20);

                    List<(int Score, GObject Target)> creditCandidates = CollectMobileChatCandidates(itemRoot, obj => obj is GTextField && obj is not GTextInput, DefaultShopItemPriceCreditKeywords, ScoreMobileShopTextCandidate);
                    view.CreditPrice = SelectMobileChatCandidate<GTextField>(creditCandidates, minScore: 20);
                }

                // Buy buttons
                {
                    var buyGoldKeywords = new List<string>(DefaultShopBuyKeywords.Length + DefaultShopBuyGoldKeywords.Length);
                    buyGoldKeywords.AddRange(DefaultShopBuyKeywords);
                    buyGoldKeywords.AddRange(DefaultShopBuyGoldKeywords);
                    string[] buyGoldComposite = buyGoldKeywords.ToArray();

                    var buyCreditKeywords = new List<string>(DefaultShopBuyKeywords.Length + DefaultShopBuyCreditKeywords.Length);
                    buyCreditKeywords.AddRange(DefaultShopBuyKeywords);
                    buyCreditKeywords.AddRange(DefaultShopBuyCreditKeywords);
                    string[] buyCreditComposite = buyCreditKeywords.ToArray();

                    List<(int Score, GObject Target)> goldButtonCandidates = CollectMobileChatCandidates(itemRoot, obj => obj is GButton && obj.touchable, buyGoldComposite, ScoreMobileShopButtonCandidate);
                    view.BuyGoldButton = SelectMobileChatCandidate<GButton>(goldButtonCandidates, minScore: 25);

                    List<(int Score, GObject Target)> creditButtonCandidates = CollectMobileChatCandidates(itemRoot, obj => obj is GButton && obj.touchable, buyCreditComposite, ScoreMobileShopButtonCandidate);
                    view.BuyCreditButton = SelectMobileChatCandidate<GButton>(creditButtonCandidates, minScore: 25);

                    if (view.BuyGoldButton == null && view.BuyCreditButton == null)
                    {
                        List<(int Score, GObject Target)> genericButtonCandidates = CollectMobileChatCandidates(itemRoot, obj => obj is GButton && obj.touchable, DefaultShopBuyKeywords, ScoreMobileShopButtonCandidate);
                        view.BuyFallbackButton = SelectMobileChatCandidate<GButton>(genericButtonCandidates, minScore: 25);
                    }
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

        private static void ClearMobileShopItemView(MobileShopItemView view)
        {
            if (view == null)
                return;

            view.HasItem = false;
            view.LastIcon = 0;

            try
            {
                if (view.Icon != null && !view.Icon._disposed)
                {
                    view.Icon.texture = null;
                    view.Icon.url = string.Empty;
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
                if (view.Count != null && !view.Count._disposed)
                    view.Count.text = string.Empty;
            }
            catch
            {
            }

            try
            {
                if (view.Stock != null && !view.Stock._disposed)
                    view.Stock.text = string.Empty;
            }
            catch
            {
            }

            try
            {
                if (view.GoldPrice != null && !view.GoldPrice._disposed)
                    view.GoldPrice.text = string.Empty;
            }
            catch
            {
            }

            try
            {
                if (view.CreditPrice != null && !view.CreditPrice._disposed)
                    view.CreditPrice.text = string.Empty;
            }
            catch
            {
            }
        }

        private static void TrySendMobileShopBuy(GameShopItem item, byte quantity, int pType)
        {
            if (item == null)
                return;

            if (quantity < 1)
                quantity = 1;

            try
            {
                MonoShare.MirNetwork.Network.Enqueue(new C.GameshopBuy
                {
                    GIndex = item.GIndex,
                    Quantity = quantity,
                    PType = pType,
                });
            }
            catch (Exception ex)
            {
                CMain.SaveError("FairyGUI: 发送商店购买失败：" + ex.Message);
            }
        }

        private static void RenderMobileShopListItem(int index, GObject itemObject)
        {
            if (itemObject == null || itemObject._disposed)
                return;

            if (itemObject is not GComponent itemRoot || itemRoot._disposed)
                return;

            List<GameShopItem> list = null;
            try
            {
                list = GameScene.GameShopInfoList;
            }
            catch
            {
                list = null;
            }

            GameShopItem shopItem = null;
            if (list != null && index >= 0 && index < list.Count)
                shopItem = list[index];

            MobileShopItemView view = GetOrCreateMobileShopItemView(itemRoot);
            if (view == null)
                return;

            if (shopItem == null || shopItem.Info == null)
            {
                ClearMobileShopItemView(view);
                return;
            }

            ushort iconIndex = shopItem.Info.Image;
            string name = shopItem.Info.FriendlyName ?? shopItem.Info.Name ?? string.Empty;
            ushort count = shopItem.Count;
            uint goldPrice = shopItem.GoldPrice;
            uint creditPrice = shopItem.CreditPrice;

            bool canBuyGold = shopItem.CanBuyGold && goldPrice > 0;
            bool canBuyCredit = shopItem.CanBuyCredit && creditPrice > 0;

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
                if (view.Count != null && !view.Count._disposed)
                    view.Count.text = count > 1 ? count.ToString() : string.Empty;
            }
            catch
            {
            }

            try
            {
                if (view.Stock != null && !view.Stock._disposed)
                    view.Stock.text = shopItem.Stock > 0 ? shopItem.Stock.ToString() : string.Empty;
            }
            catch
            {
            }

            try
            {
                if (view.GoldPrice != null && !view.GoldPrice._disposed)
                {
                    if (canBuyGold)
                        view.GoldPrice.text = goldPrice.ToString();
                    else if (!canBuyCredit && canBuyGold == false && canBuyCredit == false)
                        view.GoldPrice.text = string.Empty;
                    else if (!canBuyGold && view.CreditPrice == null)
                        view.GoldPrice.text = canBuyCredit ? $"点券 {creditPrice}" : string.Empty;
                    else if (!canBuyGold)
                        view.GoldPrice.text = string.Empty;
                }
            }
            catch
            {
            }

            try
            {
                if (view.CreditPrice != null && !view.CreditPrice._disposed)
                {
                    if (canBuyCredit)
                        view.CreditPrice.text = creditPrice.ToString();
                    else if (!canBuyCredit && view.GoldPrice == null)
                        view.CreditPrice.text = canBuyGold ? $"元宝 {goldPrice}" : string.Empty;
                    else
                        view.CreditPrice.text = string.Empty;
                }
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
                    Texture2D native = current?.nativeTexture;
                    if (current == null || native == null || native.IsDisposed)
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
                    Libraries.Items.Touch(iconIndex);
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

            // Bind buy buttons
            if (view.BuyGoldButton != null && !view.BuyGoldButton._disposed)
            {
                try
                {
                    view.BuyGoldButton.visible = canBuyGold;
                    view.BuyGoldButton.touchable = canBuyGold;

                    if (view.BuyGoldCallback != null)
                        view.BuyGoldButton.onClick.Remove(view.BuyGoldCallback);

                    if (canBuyGold)
                    {
                        view.BuyGoldCallback = () => TrySendMobileShopBuy(shopItem, quantity: 1, pType: 1);
                        view.BuyGoldButton.onClick.Add(view.BuyGoldCallback);
                    }
                    else
                    {
                        view.BuyGoldCallback = null;
                    }
                }
                catch
                {
                }
            }

            if (view.BuyCreditButton != null && !view.BuyCreditButton._disposed)
            {
                try
                {
                    view.BuyCreditButton.visible = canBuyCredit;
                    view.BuyCreditButton.touchable = canBuyCredit;

                    if (view.BuyCreditCallback != null)
                        view.BuyCreditButton.onClick.Remove(view.BuyCreditCallback);

                    if (canBuyCredit)
                    {
                        view.BuyCreditCallback = () => TrySendMobileShopBuy(shopItem, quantity: 1, pType: 0);
                        view.BuyCreditButton.onClick.Add(view.BuyCreditCallback);
                    }
                    else
                    {
                        view.BuyCreditCallback = null;
                    }
                }
                catch
                {
                }
            }

            if (view.BuyFallbackButton != null && !view.BuyFallbackButton._disposed)
            {
                try
                {
                    bool canBuy = canBuyCredit || canBuyGold;
                    view.BuyFallbackButton.visible = canBuy;
                    view.BuyFallbackButton.touchable = canBuy;

                    if (view.BuyFallbackCallback != null)
                        view.BuyFallbackButton.onClick.Remove(view.BuyFallbackCallback);

                    if (canBuy)
                    {
                        int pType = canBuyCredit ? 0 : 1;
                        view.BuyFallbackCallback = () => TrySendMobileShopBuy(shopItem, quantity: 1, pType: pType);
                        view.BuyFallbackButton.onClick.Add(view.BuyFallbackCallback);
                    }
                    else
                    {
                        view.BuyFallbackCallback = null;
                    }
                }
                catch
                {
                }
            }
        }

        private static void TryDumpMobileShopBindingsReportIfDue(
            MobileShopWindowBinding binding,
            string[] keywordsUsed,
            List<(int Score, GObject Target)> listCandidates)
        {
            if (!Settings.DebugMode || _mobileShopBindingsDumped)
                return;

            if (binding == null || binding.Window == null || binding.Window._disposed)
                return;

            try
            {
                Directory.CreateDirectory(ClientResourceLayout.RuntimeRoot);

                string path = Path.Combine(ClientResourceLayout.RuntimeRoot, "FairyGui-MobileShopBindings.txt");

                var builder = new StringBuilder(14 * 1024);
                builder.AppendLine("FairyGUI 商店窗口绑定报告（Shop）");
                builder.AppendLine($"GeneratedAtUtc={DateTime.UtcNow:o}");
                builder.AppendLine($"WindowKey={binding.WindowKey ?? "Shop"}");
                builder.AppendLine($"WindowResolveInfo={binding.ResolveInfo ?? "-"}");
                builder.AppendLine();

                builder.AppendLine($"ItemList={DescribeObject(binding.Window, binding.ItemList)}");
                builder.AppendLine($"ItemListResolveInfo={binding.ItemListResolveInfo ?? "-"}");
                builder.AppendLine($"OverrideSpec={binding.ItemListOverrideSpec ?? "-"}");
                builder.AppendLine($"OverrideKeywords={(binding.ItemListOverrideKeywords == null ? "-" : string.Join("|", binding.ItemListOverrideKeywords))}");
                builder.AppendLine($"KeywordsUsed={(keywordsUsed == null ? "-" : string.Join("|", keywordsUsed))}");
                builder.AppendLine($"Products={GameScene.GameShopInfoList?.Count ?? 0}");
                builder.AppendLine();

                builder.AppendLine("ItemList Candidates(top 12):");
                int top = Math.Min(12, listCandidates?.Count ?? 0);
                for (int i = 0; i < top; i++)
                {
                    (int score, GObject target) = listCandidates[i];
                    builder.AppendLine($"  - score={score} obj={DescribeObject(binding.Window, target)}");
                }

                if ((listCandidates?.Count ?? 0) > top)
                    builder.AppendLine($"  ... ({listCandidates.Count - top} more)");

                builder.AppendLine();
                builder.AppendLine("配置覆盖（可选）：Mir2Config.ini -> [FairyGUI]");
                builder.AppendLine($"  {MobileShopListConfigKey}=idx:...  或 path:... 或 name:/item:/url:/title:...");
                builder.AppendLine("  或者关键字列表：a|b|c（不推荐，容易误命中；优先 idx/path）。");
                builder.AppendLine("说明：idx/path 均相对商店窗口 Root。建议先查看窗口树（FairyGui-MobileWindow-Shop-Tree.txt），再填入精确覆盖。");

                File.WriteAllText(path, builder.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                _mobileShopBindingsDumped = true;
                CMain.SaveLog($"FairyGUI: 已导出商店窗口绑定报告到 {path}");
            }
            catch (Exception ex)
            {
                CMain.SaveError("FairyGUI: 导出商店窗口绑定报告失败：" + ex.Message);
            }
        }

        private static void TryBindMobileShopWindowIfDue(string windowKey, GComponent window, string resolveInfo)
        {
            if (window == null || window._disposed)
                return;

            MobileShopWindowBinding binding = _mobileShopBinding;
            if (binding != null && (binding.Window == null || binding.Window._disposed))
            {
                ResetMobileShopBindings();
                binding = null;
            }

            if (binding == null || !ReferenceEquals(binding.Window, window))
            {
                ResetMobileShopBindings();
                binding = new MobileShopWindowBinding
                {
                    WindowKey = windowKey,
                    Window = window,
                    ResolveInfo = resolveInfo,
                };

                _mobileShopBinding = binding;
                _mobileShopBindingsDumped = false;
                _nextMobileShopBindAttemptUtc = DateTime.MinValue;
            }

            if (binding.ItemList != null && !binding.ItemList._disposed && binding.ItemRenderer != null)
                return;

            if (DateTime.UtcNow < _nextMobileShopBindAttemptUtc)
                return;

            _nextMobileShopBindAttemptUtc = DateTime.UtcNow.AddSeconds(2);

            string overrideSpec = string.Empty;
            try
            {
                InIReader reader = TryCreateConfigReader();
                if (reader != null)
                {
                    overrideSpec = reader.ReadString(
                        FairyGuiConfigSectionName,
                        MobileShopListConfigKey,
                        string.Empty,
                        writeWhenNull: false);
                }
            }
            catch
            {
                overrideSpec = string.Empty;
            }

            overrideSpec = overrideSpec?.Trim() ?? string.Empty;
            binding.ItemListOverrideSpec = overrideSpec;
            binding.ItemListOverrideKeywords = null;

            GComponent searchRoot = window;
            GList list = null;
            string listResolveInfo = null;

            if (!string.IsNullOrWhiteSpace(overrideSpec))
            {
                if (TryResolveMobileMainHudObjectBySpec(window, overrideSpec, out GObject resolved, out string[] keywords))
                {
                    if (resolved is GList resolvedList && !resolvedList._disposed)
                    {
                        list = resolvedList;
                        listResolveInfo = DescribeObject(window, resolvedList) + " (override)";
                    }
                    else if (resolved is GComponent resolvedComponent && !resolvedComponent._disposed)
                    {
                        searchRoot = resolvedComponent;
                        listResolveInfo = DescribeObject(window, resolvedComponent) + " (searchRoot override)";
                    }
                    else if (keywords != null && keywords.Length > 0)
                    {
                        binding.ItemListOverrideKeywords = keywords;
                    }
                }
                else
                {
                    binding.ItemListOverrideKeywords = SplitKeywords(overrideSpec);
                }
            }

            string[] keywordsUsed = binding.ItemListOverrideKeywords != null && binding.ItemListOverrideKeywords.Length > 0
                ? binding.ItemListOverrideKeywords
                : DefaultShopListKeywords;

            List<(int Score, GObject Target)> candidates = null;

            if (list == null)
            {
                int minScore = binding.ItemListOverrideKeywords != null && binding.ItemListOverrideKeywords.Length > 0 ? 40 : 60;
                candidates = CollectMobileChatCandidates(searchRoot, obj => obj is GList && obj.touchable, keywordsUsed, ScoreMobileShopListCandidate);
                list = SelectMobileChatCandidate<GList>(candidates, minScore);
                if (list != null && !list._disposed)
                {
                    listResolveInfo = DescribeObject(window, list) + (binding.ItemListOverrideKeywords != null && binding.ItemListOverrideKeywords.Length > 0 ? " (keywords)" : " (auto)");
                }
            }

            if (list == null || list._disposed)
            {
                CMain.SaveError("FairyGUI: 商店窗口未找到商品列表（Shop）。可在 Mir2Config.ini 设置 [" + FairyGuiConfigSectionName + "] " +
                                MobileShopListConfigKey + "=idx:... 指定商品列表（或 path:/name:/item:/url:/title: / 关键字列表 a|b|c）。");
                return;
            }

            binding.ItemList = list;
            binding.ItemListResolveInfo = listResolveInfo;

            if (binding.ItemRenderer == null)
                binding.ItemRenderer = RenderMobileShopListItem;

            try
            {
                binding.ItemList.itemRenderer = binding.ItemRenderer;
            }
            catch
            {
            }

            TryDumpMobileShopBindingsReportIfDue(binding, keywordsUsed, candidates ?? new List<(int Score, GObject Target)>());
            _mobileShopDirty = true;
        }

        private static void TryRefreshMobileShopIfDue(bool force)
        {
            if (_stage == null || !_initialized || !_packagesLoaded)
                return;

            if (!MobileWindows.TryGetValue("Shop", out GComponent window) || window == null || window._disposed)
            {
                if (_mobileShopBinding != null)
                    ResetMobileShopBindings();
                return;
            }

            if (!window.visible)
                return;

            TryBindMobileShopWindowIfDue("Shop", window, resolveInfo: null);

            MobileShopWindowBinding binding = _mobileShopBinding;
            if (binding == null || binding.Window == null || binding.Window._disposed)
            {
                ResetMobileShopBindings();
                return;
            }

            if (binding.ItemList == null || binding.ItemList._disposed)
                return;

            if (!force && !_mobileShopDirty)
                return;

            _mobileShopDirty = false;

            int count = 0;
            try
            {
                count = GameScene.GameShopInfoList?.Count ?? 0;
            }
            catch
            {
                count = 0;
            }

            try
            {
                if (binding.ItemRenderer == null)
                    binding.ItemRenderer = RenderMobileShopListItem;

                binding.ItemList.itemRenderer = binding.ItemRenderer;
                binding.ItemList.numItems = count;
            }
            catch (Exception ex)
            {
                CMain.SaveError("FairyGUI: 刷新商店窗口失败：" + ex.Message);
                _nextMobileShopBindAttemptUtc = DateTime.MinValue;
                _mobileShopDirty = true;
            }
        }

        private static void TryBindMobileMagicWindowIfDue(string windowKey, GComponent window, string resolveInfo)
        {
            if (window == null || window._disposed)
                return;

            MobileMagicWindowBinding binding = _mobileMagicBinding;
            if (binding != null && (binding.Window == null || binding.Window._disposed))
            {
                ResetMobileMagicBindings();
                binding = null;
            }

            if (binding == null || !ReferenceEquals(binding.Window, window))
            {
                ResetMobileMagicBindings();

                binding = new MobileMagicWindowBinding
                {
                    WindowKey = windowKey,
                    Window = window,
                    ResolveInfo = resolveInfo,
                };

                _mobileMagicBinding = binding;
                _mobileMagicBindingsDumped = false;
                _nextMobileMagicBindAttemptUtc = DateTime.MinValue;
            }

            if (binding.Slots.Count > 0)
                return;

            if (DateTime.UtcNow < _nextMobileMagicBindAttemptUtc)
                return;

            _nextMobileMagicBindAttemptUtc = DateTime.UtcNow.AddSeconds(2);

            string overrideSpec = string.Empty;
            try
            {
                InIReader reader = TryCreateConfigReader();
                if (reader != null)
                {
                    overrideSpec = reader.ReadString(
                        FairyGuiConfigSectionName,
                        MobileMagicGridConfigKey,
                        string.Empty,
                        writeWhenNull: false);
                }
            }
            catch
            {
                overrideSpec = string.Empty;
            }

            overrideSpec = overrideSpec?.Trim() ?? string.Empty;
            binding.OverrideSpec = overrideSpec;
            binding.OverrideKeywords = null;

            GComponent gridRoot = window;
            string gridResolveInfo = DescribeObject(window, window);

            if (!string.IsNullOrWhiteSpace(overrideSpec))
            {
                if (TryResolveMobileMainHudObjectBySpec(window, overrideSpec, out GObject resolved, out string[] keywords))
                {
                    if (resolved is GComponent resolvedComponent && !resolvedComponent._disposed)
                    {
                        gridRoot = resolvedComponent;
                        gridResolveInfo = DescribeObject(window, resolvedComponent) + " (override)";
                    }
                    else if (keywords != null && keywords.Length > 0)
                    {
                        binding.OverrideKeywords = keywords;
                    }
                }
                else
                {
                    binding.OverrideKeywords = SplitKeywords(overrideSpec);
                }
            }

            if (binding.OverrideKeywords != null && binding.OverrideKeywords.Length > 0)
            {
                List<(int Score, GObject Target)> candidates = CollectMobileChatCandidates(window, obj => obj is GComponent, binding.OverrideKeywords, ScoreMobileInventoryGridCandidate);
                GComponent selected = SelectMobileChatCandidate<GComponent>(candidates, minScore: 40);
                if (selected != null && !selected._disposed)
                {
                    gridRoot = selected;
                    gridResolveInfo = DescribeObject(window, selected) + " (keywords)";
                }
            }
            else
            {
                List<(int Score, GObject Target)> candidates = CollectMobileChatCandidates(window, obj => obj is GComponent, DefaultMagicGridKeywords, ScoreMobileInventoryGridCandidate);
                GComponent selected = SelectMobileChatCandidate<GComponent>(candidates, minScore: 60);
                if (selected != null && !selected._disposed)
                {
                    gridRoot = selected;
                    gridResolveInfo = DescribeObject(window, selected) + " (auto)";
                }
            }

            binding.GridRoot = gridRoot;
            binding.GridResolveInfo = gridResolveInfo;

            List<GComponent> slotCandidates = CollectMagicSlotCandidates(gridRoot);
            if (slotCandidates.Count == 0)
            {
                CMain.SaveError("FairyGUI: 技能窗口未找到技能格子（Magic）。可在 Mir2Config.ini 设置 [" + FairyGuiConfigSectionName + "] " +
                                MobileMagicGridConfigKey + "=idx:... 指定格子根节点（或 path:/name:/item:/url:/title: / 关键字列表 a|b|c）。");
                return;
            }

            SortGComponentsByGlobalPosition(slotCandidates);

            int desiredSlots = GameScene.User?.Magics?.Count ?? slotCandidates.Count;
            if (desiredSlots <= 0)
                desiredSlots = slotCandidates.Count;

            int slotCount = Math.Min(desiredSlots, slotCandidates.Count);
            binding.Slots.Clear();

            for (int i = 0; i < slotCount; i++)
            {
                GComponent slotRoot = slotCandidates[i];
                var slot = new MobileMagicSlotBinding
                {
                    SlotIndex = i,
                    Root = slotRoot,
                    Icon = FindBestInventorySlotIcon(slotRoot),
                    IconImage = FindBestInventorySlotIconImage(slotRoot),
                    Name = FindBestMagicSlotName(slotRoot),
                    Level = FindBestMagicSlotLevel(slotRoot),
                    HasMagic = false,
                    LastIcon = 0,
                    LastLevel = 0,
                    LastName = null,
                };

                try
                {
                    if (slotRoot != null && !slotRoot._disposed)
                    {
                        EnsureMobileInteractiveChain(slotRoot, window);

                        int slotIndex = i;
                        EventCallback0 callback = () => OnMobileMagicSlotClicked(slotIndex);
                        slot.ClickCallback = callback;
                        slotRoot.onClick.Add(callback);

                        try
                        {
                            slot.LongPressDragBinding = BindMobileLongPressMagicDrag(
                                slotRoot,
                                resolvePayload: () =>
                                {
                                    try
                                    {
                                        List<ClientMagic> magics = GameScene.User?.Magics;
                                        if (magics == null || slotIndex < 0 || slotIndex >= magics.Count)
                                            return null;

                                        ClientMagic magic = magics[slotIndex];
                                        if (magic == null)
                                            return null;

                                        return new MobileMagicDragPayload
                                        {
                                            HotKey = magic.Key,
                                            Icon = magic.Icon,
                                            Spell = magic.Spell,
                                        };
                                    }
                                    catch
                                    {
                                        return null;
                                    }
                                });
                        }
                        catch
                        {
                            slot.LongPressDragBinding = null;
                        }
                    }
                }
                catch
                {
                }

                binding.Slots.Add(slot);
            }

            TryDumpMobileMagicBindingsReportIfDue(binding, desiredSlots, slotCandidates);

            CMain.SaveLog($"FairyGUI: 技能窗口绑定完成：Slots={binding.Slots.Count} GridRoot={binding.GridResolveInfo}");
        }

        private static void TryDumpMobileMagicBindingsReportIfDue(MobileMagicWindowBinding binding, int desiredSlots, List<GComponent> slotCandidates)
        {
            if (!Settings.DebugMode || _mobileMagicBindingsDumped)
                return;

            if (binding == null || binding.Window == null || binding.Window._disposed)
                return;

            try
            {
                Directory.CreateDirectory(ClientResourceLayout.RuntimeRoot);

                string path = Path.Combine(ClientResourceLayout.RuntimeRoot, "FairyGui-MobileMagicBindings.txt");

                var builder = new StringBuilder(14 * 1024);
                builder.AppendLine("FairyGUI 技能窗口绑定报告（Magic）");
                builder.AppendLine($"GeneratedAtUtc={DateTime.UtcNow:o}");
                builder.AppendLine($"WindowKey={binding.WindowKey}");
                builder.AppendLine($"WindowResolveInfo={binding.ResolveInfo ?? "-"}");
                builder.AppendLine($"GridRoot={DescribeObject(binding.Window, binding.GridRoot)}");
                builder.AppendLine($"GridResolveInfo={binding.GridResolveInfo ?? "-"}");
                builder.AppendLine($"OverrideSpec={binding.OverrideSpec ?? "-"}");
                builder.AppendLine($"OverrideKeywords={(binding.OverrideKeywords == null ? "-" : string.Join("|", binding.OverrideKeywords))}");
                builder.AppendLine($"DesiredSlots={desiredSlots}");
                builder.AppendLine($"SlotCandidates={slotCandidates?.Count ?? 0}");
                builder.AppendLine($"SlotsBound={binding.Slots.Count}");
                builder.AppendLine();

                int top = Math.Min(binding.Slots.Count, 24);
                for (int i = 0; i < top; i++)
                {
                    MobileMagicSlotBinding slot = binding.Slots[i];
                    builder.Append("Slot[").Append(i).Append("] root=").Append(DescribeObject(binding.Window, slot.Root));
                    builder.Append(" icon=").Append(DescribeObject(binding.Window, slot.Icon));
                    builder.Append(" name=").Append(DescribeObject(binding.Window, slot.Name));
                    builder.Append(" level=").AppendLine(DescribeObject(binding.Window, slot.Level));
                }

                if (binding.Slots.Count > top)
                    builder.AppendLine($"... ({binding.Slots.Count - top} more)");

                builder.AppendLine();
                builder.AppendLine("配置覆盖（可选）：Mir2Config.ini -> [FairyGUI]");
                builder.AppendLine($"  {MobileMagicGridConfigKey}=idx:...  或 path:... 或 name:/item:/url:/title:...");
                builder.AppendLine("  或者关键字列表：a|b|c（不推荐，容易误命中；优先 idx/path）。");
                builder.AppendLine("说明：idx/path 均相对技能窗口 Root。建议先查看窗口树（FairyGui-MobileWindow-Magic-Tree.txt），再填入精确覆盖。");

                File.WriteAllText(path, builder.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                _mobileMagicBindingsDumped = true;
                CMain.SaveLog($"FairyGUI: 已导出技能窗口绑定报告到 {path}");
            }
            catch (Exception ex)
            {
                CMain.SaveError("FairyGUI: 导出技能窗口绑定报告失败：" + ex.Message);
            }
        }

        private static void TryRefreshMobileMagicIfDue(bool force)
        {
            if (_stage == null || !_initialized || !_packagesLoaded)
                return;

            if (!MobileWindows.TryGetValue("Magic", out GComponent window) || window == null || window._disposed)
            {
                if (_mobileMagicBinding != null)
                    ResetMobileMagicBindings();
                return;
            }

            if (!window.visible)
                return;

            TryBindMobileMagicWindowIfDue("Magic", window, resolveInfo: null);

            MobileMagicWindowBinding binding = _mobileMagicBinding;
            if (binding == null || binding.Window == null || binding.Window._disposed)
            {
                ResetMobileMagicBindings();
                return;
            }

            if (binding.Slots.Count == 0)
                return;

            var user = GameScene.User;
            if (user == null || user.Magics == null)
                return;

            List<ClientMagic> magics = user.Magics;
            int totalSlots = binding.Slots.Count;

            for (int i = 0; i < totalSlots; i++)
            {
                MobileMagicSlotBinding slot = binding.Slots[i];
                if (slot == null || slot.Root == null || slot.Root._disposed)
                {
                    binding.Slots.Clear();
                    _nextMobileMagicBindAttemptUtc = DateTime.MinValue;
                    return;
                }

                ClientMagic magic = i < magics.Count ? magics[i] : null;
                if (magic == null)
                {
                    if (slot.HasMagic)
                        ClearMagicSlot(slot);
                    continue;
                }

                byte iconByte = magic.Icon;
                byte level = magic.Level;
                string name = magic.Name ?? string.Empty;

                int primaryIndex = iconByte * 2;
                Libraries.MagIcon2.Touch(primaryIndex);
                Libraries.MagIcon2.Touch(iconByte);

                bool needsIconRefresh = force || !slot.HasMagic || slot.LastIcon != iconByte;
                if (!needsIconRefresh)
                {
                    bool textureOk = false;

                    if (slot.Icon != null && !slot.Icon._disposed)
                    {
                        try
                        {
                            NTexture current = slot.Icon.texture;
                            Texture2D native = current?.nativeTexture;
                            textureOk = current != null && native != null && !native.IsDisposed;
                        }
                        catch
                        {
                            textureOk = false;
                        }
                    }
                    else if (slot.IconImage != null && !slot.IconImage._disposed)
                    {
                        try
                        {
                            NTexture current = slot.IconImage.texture;
                            Texture2D native = current?.nativeTexture;
                            textureOk = current != null && native != null && !native.IsDisposed;
                        }
                        catch
                        {
                            textureOk = false;
                        }
                    }

                    if (!textureOk)
                        needsIconRefresh = true;
                }

                if (needsIconRefresh)
                {
                    NTexture texture = GetOrCreateMagicIconTexture(iconByte);

                    if (slot.Icon != null && !slot.Icon._disposed)
                    {
                        slot.Icon.showErrorSign = false;
                        slot.Icon.url = string.Empty;
                        slot.Icon.texture = texture;
                    }
                    else if (slot.IconImage != null && !slot.IconImage._disposed)
                    {
                        slot.IconImage.texture = texture;
                    }

                    slot.LastIcon = iconByte;
                }

                if (force || !slot.HasMagic || slot.LastLevel != level)
                {
                    if (slot.Level != null && !slot.Level._disposed)
                        slot.Level.text = level.ToString();

                    slot.LastLevel = level;
                }

                if (force || !slot.HasMagic || !string.Equals(slot.LastName, name, StringComparison.Ordinal))
                {
                    if (slot.Name != null && !slot.Name._disposed)
                        slot.Name.text = name;

                    slot.LastName = name;
                }

                slot.HasMagic = true;
            }
        }

        private static void OnMobileMagicSlotClicked(int slotIndex)
        {
            if (slotIndex < 0)
                return;

            var user = GameScene.User;
            if (user == null || user.Magics == null)
                return;

            if (slotIndex >= user.Magics.Count)
                return;

            ClientMagic magic = user.Magics[slotIndex];
            if (magic == null)
                return;

            if (magic.Key <= 0)
            {
                try
                {
                    GameScene.Scene?.OutputMessage($"该技能未绑定快捷键：{magic.Name}");
                }
                catch
                {
                }

                return;
            }

            try
            {
                GameScene.Scene?.UseSpell(magic.Key, fromUI: true);
            }
            catch (Exception ex)
            {
                CMain.SaveError("FairyGUI: 触发技能失败：" + ex.Message);
            }
        }

        private static void ClearMagicSlot(MobileMagicSlotBinding slot)
        {
            if (slot == null)
                return;

            try
            {
                if (slot.Icon != null && !slot.Icon._disposed)
                {
                    slot.Icon.showErrorSign = false;
                    slot.Icon.texture = null;
                    slot.Icon.url = string.Empty;
                    if (string.Equals(slot.Icon.name, MobileMainHudAttackButtonIconName, StringComparison.OrdinalIgnoreCase))
                        slot.Icon.visible = false;
                }
            }
            catch
            {
            }

            try
            {
                if (slot.IconImage != null && !slot.IconImage._disposed)
                    slot.IconImage.texture = null;
            }
            catch
            {
            }

            try
            {
                if (slot.Name != null && !slot.Name._disposed)
                    slot.Name.text = string.Empty;
            }
            catch
            {
            }

            try
            {
                if (slot.Level != null && !slot.Level._disposed)
                    slot.Level.text = string.Empty;
            }
            catch
            {
            }

            slot.HasMagic = false;
            slot.LastIcon = 0;
            slot.LastLevel = 0;
            slot.LastName = null;
        }

        private static NTexture GetOrCreateMagicIconTexture(byte icon)
        {
            int primaryIndex = icon * 2;

            Texture2D texture = null;
            try
            {
                texture = Libraries.MagIcon2.GetTexture(primaryIndex);
            }
            catch
            {
                texture = null;
            }

            int resolvedIndex = primaryIndex;
            if (texture == null || texture.IsDisposed)
            {
                try
                {
                    texture = Libraries.MagIcon2.GetTexture(icon);
                }
                catch
                {
                    texture = null;
                }

                resolvedIndex = icon;
            }

            if (texture == null || texture.IsDisposed)
                return null;

            if (MagicIconTextureCache.TryGetValue(resolvedIndex, out NTexture cached) && cached != null)
            {
                Texture2D native = cached.nativeTexture;
                if (native != null && !native.IsDisposed && ReferenceEquals(native, texture))
                    return cached;
            }

            NTexture created = new NTexture(texture);
            MagicIconTextureCache[resolvedIndex] = created;
            return created;
        }

        private static int ScoreMagicSlotNameCandidate(GObject obj, string[] keywords)
        {
            int score = ScoreAnyField(obj, keywords, equalsWeight: 160, startsWithWeight: 80, containsWeight: 35);
            score += ScoreRect(obj, preferLower: false, areaDivisor: 500, maxAreaScore: 80);
            if (obj is GRichTextField)
                score += 10;
            if (obj.packageItem?.exported == true)
                score += 5;
            return score;
        }

        private static int ScoreMagicSlotLevelCandidate(GObject obj, string[] keywords)
        {
            int score = ScoreAnyField(obj, keywords, equalsWeight: 170, startsWithWeight: 85, containsWeight: 40);
            score += ScoreRect(obj, preferLower: true, areaDivisor: 260, maxAreaScore: 60);
            if (obj.packageItem?.exported == true)
                score += 5;
            return score;
        }

        private static GTextField FindBestMagicSlotName(GComponent slotRoot)
        {
            if (slotRoot == null || slotRoot._disposed)
                return null;

            List<(int Score, GObject Target)> candidates = CollectMobileChatCandidates(slotRoot, obj => obj is GTextField && obj is not GTextInput, DefaultMagicNameKeywords, ScoreMagicSlotNameCandidate);
            return SelectMobileChatCandidate<GTextField>(candidates, minScore: 20);
        }

        private static GTextField FindBestMagicSlotLevel(GComponent slotRoot)
        {
            if (slotRoot == null || slotRoot._disposed)
                return null;

            List<(int Score, GObject Target)> candidates = CollectMobileChatCandidates(slotRoot, obj => obj is GTextField && obj is not GTextInput, DefaultMagicLevelKeywords, ScoreMagicSlotLevelCandidate);
            return SelectMobileChatCandidate<GTextField>(candidates, minScore: 20);
        }

        private static List<GComponent> CollectMagicSlotCandidates(GComponent root)
        {
            var list = new List<GComponent>(64);

            if (root == null || root._disposed)
                return list;

            foreach (GObject obj in Enumerate(root))
            {
                if (obj == null || obj._disposed)
                    continue;

                if (ReferenceEquals(obj, root))
                    continue;

                if (obj is not GComponent component || component._disposed)
                    continue;

                string itemName = component.packageItem?.name ?? string.Empty;
                if (string.IsNullOrWhiteSpace(itemName))
                    continue;

                bool match = false;
                for (int i = 0; i < DefaultMagicSlotComponentKeywords.Length; i++)
                {
                    string keyword = DefaultMagicSlotComponentKeywords[i];
                    if (!string.IsNullOrWhiteSpace(keyword) && itemName.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        match = true;
                        break;
                    }
                }

                if (!match)
                    continue;

                list.Add(component);
            }

            if (list.Count > 0)
                return list;

            foreach (GObject obj in Enumerate(root))
            {
                if (obj == null || obj._disposed)
                    continue;

                if (ReferenceEquals(obj, root))
                    continue;

                if (obj is not GComponent component || component._disposed)
                    continue;

                if (component.width <= 6F || component.height <= 6F)
                    continue;

                if (!component.touchable)
                {
                    try
                    {
                        float rootArea = Math.Max(0F, root.width) * Math.Max(0F, root.height);
                        float area = Math.Max(0F, component.width) * Math.Max(0F, component.height);
                        if (rootArea > 1F && area > rootArea * 0.35F)
                            continue;
                    }
                    catch
                    {
                    }
                }

                bool hasIcon = false;
                foreach (GObject child in Enumerate(component))
                {
                    if (child is GLoader || child is GImage)
                    {
                        hasIcon = true;
                        break;
                    }
                }

                if (!hasIcon)
                    continue;

                list.Add(component);
            }

            return list;
        }

        private static void TryBindMobileInventoryButtonsIfDue(MobileItemGridBinding binding)
        {
            if (binding == null || binding.Window == null || binding.Window._disposed)
                return;

            GComponent window = binding.Window;

            string warehouseSpec = string.Empty;
            string sortSpec = string.Empty;

            try
            {
                InIReader reader = TryCreateConfigReader();
                if (reader != null)
                {
                    warehouseSpec = reader.ReadString(FairyGuiConfigSectionName, MobileInventoryWarehouseButtonConfigKey, string.Empty, writeWhenNull: false);
                    sortSpec = reader.ReadString(FairyGuiConfigSectionName, MobileInventorySortButtonConfigKey, string.Empty, writeWhenNull: false);
                }
            }
            catch
            {
                warehouseSpec = string.Empty;
                sortSpec = string.Empty;
            }

            warehouseSpec = warehouseSpec?.Trim() ?? string.Empty;
            sortSpec = sortSpec?.Trim() ?? string.Empty;

            binding.WarehouseButtonOverrideSpec = warehouseSpec;
            binding.SortButtonOverrideSpec = sortSpec;

            if (binding.WarehouseButton == null || binding.WarehouseButton._disposed)
            {
                binding.WarehouseButtonOverrideKeywords = null;
                binding.WarehouseButton = ResolveMobileInventoryButton(window, warehouseSpec, preferName: "DRBStorage", DefaultInventoryWarehouseButtonKeywords,
                    out binding.WarehouseButtonResolveInfo, out binding.WarehouseButtonOverrideKeywords);
                binding.WarehouseButtonClickCallback = null;
            }

            if (binding.SortButton == null || binding.SortButton._disposed)
            {
                binding.SortButtonOverrideKeywords = null;
                binding.SortButton = ResolveMobileInventoryButton(window, sortSpec, preferName: "DBtnQueryBagItems", DefaultInventorySortButtonKeywords,
                    out binding.SortButtonResolveInfo, out binding.SortButtonOverrideKeywords);
                binding.SortButtonClickCallback = null;
            }

            // 绑定点击事件（Remove 再 Add，避免重复叠加）
            if (binding.WarehouseButton != null && !binding.WarehouseButton._disposed)
            {
                try
                {
                    binding.WarehouseButton.touchable = true;
                    binding.WarehouseButton.enabled = true;
                    binding.WarehouseButton.grayed = false;
                    binding.WarehouseButton.changeStateOnClick = false;
                }
                catch
                {
                }

                if (binding.WarehouseButtonClickCallback == null)
                {
                    binding.WarehouseButtonClickCallback = () => OnMobileInventoryWarehouseButtonClicked(binding.WarehouseButton);
                    try { binding.WarehouseButton.onClick.Add(binding.WarehouseButtonClickCallback); } catch { }
                }
            }

            if (binding.SortButton != null && !binding.SortButton._disposed)
            {
                try
                {
                    binding.SortButton.touchable = true;
                    binding.SortButton.enabled = true;
                    binding.SortButton.grayed = false;
                    binding.SortButton.changeStateOnClick = false;
                }
                catch
                {
                }

                if (binding.SortButtonClickCallback == null)
                {
                    binding.SortButtonClickCallback = () => OnMobileInventorySortButtonClicked(binding.SortButton);
                    try { binding.SortButton.onClick.Add(binding.SortButtonClickCallback); } catch { }
                }
            }

            // 需求：背包组件 Bag_00792 内的 bagRecoveryBtn、btn3 需要显示（部分发布资源里默认隐藏）。
            try
            {
                if (TryFindChildByNameRecursive(window, "Bag_00792") is GComponent bag792 && bag792 != null && !bag792._disposed)
                {
                    string[] showNames = { "bagRecoveryBtn", "btn3" };
                    for (int i = 0; i < showNames.Length; i++)
                    {
                        string name = showNames[i];
                        if (string.IsNullOrWhiteSpace(name))
                            continue;

                        GObject obj = null;
                        try { obj = bag792.GetChild(name) ?? TryFindChildByNameRecursive(bag792, name); } catch { obj = null; }
                        if (obj == null || obj._disposed)
                            continue;

                        try { obj.visible = true; } catch { }
                        try { obj.touchable = true; } catch { }

                        try
                        {
                            if (obj is GButton b && b != null && !b._disposed)
                            {
                                b.enabled = true;
                                b.grayed = false;
                                b.changeStateOnClick = false;
                            }
                        }
                        catch
                        {
                        }
                    }
                }
            }
            catch
            {
            }
        }

        private static bool TryResolveMobileInventoryPageTabs(GComponent window, out GButton[] tabs, out int tabCount)
        {
            tabs = null;
            tabCount = 0;

            if (window == null || window._disposed)
                return false;

            static int? TryParseTabIndex(string text)
            {
                if (string.IsNullOrWhiteSpace(text))
                    return null;

                string t = text.Trim();
                if (t.Length == 0)
                    return null;

                if (int.TryParse(t, out int numericIndex) && numericIndex >= 1 && numericIndex <= 5)
                    return numericIndex - 1;

                // 常见页签：1/2/3、①②③、ⅠⅡⅢ
                if (t == "1" || t == "①" || t.Equals("I", StringComparison.OrdinalIgnoreCase) || t == "Ⅰ")
                    return 0;
                if (t == "2" || t == "②" || t.Equals("II", StringComparison.OrdinalIgnoreCase) || t == "Ⅱ")
                    return 1;
                if (t == "3" || t == "③" || t.Equals("III", StringComparison.OrdinalIgnoreCase) || t == "Ⅲ")
                    return 2;

                if (t == "4" || t == "④" || t == "四" || t.Equals("IV", StringComparison.OrdinalIgnoreCase))
                    return 3;
                if (t == "5" || t == "⑤" || t == "五" || t.Equals("V", StringComparison.OrdinalIgnoreCase))
                    return 4;

                return null;
            }

            static int ScoreCandidate(GButton button, Vector2 globalPos, float right, float stageW)
            {
                int score = 0;
                if (button == null || button._disposed)
                    return score;

                string name = button.name ?? string.Empty;
                string item = button.packageItem?.name ?? string.Empty;
                string title = button.title ?? string.Empty;

                int? idx = TryParseTabIndex(title) ?? TryParseTabIndex(name) ?? TryParseTabIndex(item);
                if (idx != null)
                    score += 220;

                if (name.IndexOf("BgRadioBtn", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    item.IndexOf("BgRadioBtn", StringComparison.OrdinalIgnoreCase) >= 0)
                    score += 180;

                if (name.IndexOf("Radio", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    item.IndexOf("Radio", StringComparison.OrdinalIgnoreCase) >= 0)
                    score += 110;

                if (name.IndexOf("Tab", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    item.IndexOf("Tab", StringComparison.OrdinalIgnoreCase) >= 0)
                    score += 110;

                if (name.IndexOf("Page", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    item.IndexOf("Page", StringComparison.OrdinalIgnoreCase) >= 0)
                    score += 70;

                try
                {
                    if (button.mode == ButtonMode.Radio)
                        score += 20;
                    else if (button.mode == ButtonMode.Check)
                        score += 10;
                }
                catch
                {
                }

                try
                {
                    float w = button.width;
                    float h = button.height;
                    if (w > 0 && h > 0)
                    {
                        if (w <= 140 && h <= 140)
                            score += 35;
                        else if (w <= 240 && h <= 240)
                            score += 15;
                        else
                            score -= 30;
                    }
                }
                catch
                {
                }

                if (stageW > 0)
                {
                    try
                    {
                        float ratio = Math.Clamp(right / stageW, 0F, 1F);
                        score += (int)(ratio * 60F);
                    }
                    catch
                    {
                    }
                }

                return score;
            }

            try
            {
                tabs = new GButton[5];

                // 1) 优先按常见命名匹配（保持向后兼容）
                string[][] knownNameSets =
                {
                    new[] {"BgRadioBtn1", "BgRadioBtn2", "BgRadioBtn3", "BgRadioBtn4", "BgRadioBtn5"},
                    new[] {"BgRadioBtn0", "BgRadioBtn1", "BgRadioBtn2", "BgRadioBtn3", "BgRadioBtn4"},
                    new[] {"RadioBtn1", "RadioBtn2", "RadioBtn3", "RadioBtn4", "RadioBtn5"},
                    new[] {"BagTab1", "BagTab2", "BagTab3", "BagTab4", "BagTab5"},
                    new[] {"Tab1", "Tab2", "Tab3", "Tab4", "Tab5"},
                    new[] {"Page1", "Page2", "Page3", "Page4", "Page5"},
                    new[] {"BagCustomBtn1", "BagCustomBtn2", "BagCustomBtn3", "BagCustomBtn4", "BagCustomBtn5"},
                };

                GButton[] bestKnown = null;
                int bestKnownCount = 0;
                int bestKnownSetIndex = int.MaxValue;

                for (int s = 0; s < knownNameSets.Length; s++)
                {
                    string[] names = knownNameSets[s];
                    if (names == null || names.Length != 5)
                        continue;

                    var tmp = new GButton[5];
                    int count = 0;

                    for (int i = 0; i < 5; i++)
                    {
                        string name = names[i];
                        if (string.IsNullOrWhiteSpace(name))
                            continue;

                        GObject obj = null;
                        try { obj = window.GetChild(name) ?? TryFindChildByNameRecursive(window, name); } catch { obj = null; }

                        if (obj is GButton button && button != null && !button._disposed)
                        {
                            tmp[i] = button;
                            count++;
                        }
                    }

                    // 优先级：先比命中数量，再比命名集合的顺序（越靠前越优先）。
                    // 这样可确保在 BgRadioBtn 与 BagCustomBtn 同时存在时，始终优先使用 BgRadioBtn。
                    if (count > bestKnownCount || (count == bestKnownCount && s < bestKnownSetIndex))
                    {
                        bestKnown = tmp;
                        bestKnownCount = count;
                        bestKnownSetIndex = s;

                        // 已经满配 5 个页签，按优先级顺序直接停止扫描。
                        if (bestKnownCount >= 5)
                            break;
                    }
                }

                if (bestKnownCount > 0 && bestKnown != null)
                {
                    for (int i = 0; i < 5; i++)
                    {
                        if (bestKnown[i] != null && !bestKnown[i]._disposed)
                        {
                            tabs[i] = bestKnown[i];
                            tabCount++;
                        }
                    }

                    return tabCount > 0;
                }

                // 2) 兜底：按标题/位置启发式识别（右侧竖排 1/2/3）
                float stageW = 0F;
                try { stageW = _stage?.width ?? 0F; } catch { stageW = 0F; }
                if (stageW <= 0F)
                {
                    try { stageW = GRoot.inst.width; } catch { stageW = 0F; }
                }

                var candidates = new List<(GButton Button, Vector2 Pos, float Right, int Score)>(32);
                foreach (GObject obj in Enumerate(window))
                {
                    if (obj is not GButton button || button == null || button._disposed)
                        continue;

                    Vector2 pos;
                    try { pos = button.LocalToGlobal(Vector2.Zero); } catch { pos = Vector2.Zero; }

                    float right;
                    try { right = pos.X + button.width; } catch { right = pos.X; }

                    int score = 0;
                    try { score = ScoreCandidate(button, pos, right, stageW); } catch { score = 0; }

                    candidates.Add((button, pos, right, score));
                }

                if (candidates.Count == 0)
                    return false;

                // 2.1) 优先按标题 1/2/3 精确命中
                var byIndex = new GButton[5];
                var byIndexScore = new int[5];
                for (int i = 0; i < candidates.Count; i++)
                {
                    (GButton b, _, _, int score) = candidates[i];
                    int? idx = TryParseTabIndex(b?.title) ?? TryParseTabIndex(b?.name) ?? TryParseTabIndex(b?.packageItem?.name);
                    if (idx == null)
                        continue;

                    int index = Math.Clamp(idx.Value, 0, 4);
                    if (byIndex[index] == null || score > byIndexScore[index])
                    {
                        byIndex[index] = b;
                        byIndexScore[index] = score;
                    }
                }

                for (int i = 0; i < 5; i++)
                {
                    if (byIndex[i] != null && !byIndex[i]._disposed)
                    {
                        tabs[i] = byIndex[i];
                        tabCount++;
                    }
                }

                if (tabCount >= 5)
                    return true;

                // 2.2) 取“最靠右”的高分按钮，按 y 排序后取前三
                float maxRight = float.MinValue;
                for (int i = 0; i < candidates.Count; i++)
                {
                    if (candidates[i].Right > maxRight)
                        maxRight = candidates[i].Right;
                }

                var nearRight = new List<(GButton Button, Vector2 Pos, float Right, int Score)>(candidates.Count);
                float threshold = 160F;
                for (int pass = 0; pass < 2; pass++)
                {
                    nearRight.Clear();
                    for (int i = 0; i < candidates.Count; i++)
                    {
                        var c = candidates[i];
                        if (c.Right >= maxRight - threshold)
                            nearRight.Add(c);
                    }

                    if (nearRight.Count >= 3)
                        break;

                    threshold = 260F;
                }

                if (nearRight.Count < 3 && tabCount == 0)
                    return false;

                nearRight.Sort((a, b) =>
                {
                    int s = b.Score.CompareTo(a.Score);
                    if (s != 0)
                        return s;
                    int y = a.Pos.Y.CompareTo(b.Pos.Y);
                    if (y != 0)
                        return y;
                    return a.Pos.X.CompareTo(b.Pos.X);
                });

                int topN = Math.Min(nearRight.Count, 10);
                var pick = new List<(GButton Button, Vector2 Pos, float Right, int Score)>(topN);
                for (int i = 0; i < topN; i++)
                    pick.Add(nearRight[i]);

                pick.Sort((a, b) =>
                {
                    int y = a.Pos.Y.CompareTo(b.Pos.Y);
                    if (y != 0)
                        return y;
                    return a.Pos.X.CompareTo(b.Pos.X);
                });

                for (int i = 0; i < pick.Count && tabCount < 5; i++)
                {
                    if (pick[i].Button == null || pick[i].Button._disposed)
                        continue;

                    bool alreadyUsed = false;
                    for (int j = 0; j < tabs.Length; j++)
                    {
                        if (ReferenceEquals(tabs[j], pick[i].Button))
                        {
                            alreadyUsed = true;
                            break;
                        }
                    }

                    if (alreadyUsed)
                        continue;

                    int targetIndex = -1;
                    for (int j = 0; j < tabs.Length; j++)
                    {
                        if (tabs[j] == null || tabs[j]._disposed)
                        {
                            targetIndex = j;
                            break;
                        }
                    }

                    if (targetIndex < 0)
                        break;

                    tabs[targetIndex] = pick[i].Button;
                    tabCount++;
                }

                return tabCount > 0;
            }
            catch
            {
                tabs = null;
                tabCount = 0;
                return false;
            }
        }

        private static void HideMobileInventoryBagCustomPageTabsIfPresent(GComponent window, GButton[] activeTabs)
        {
            if (window == null || window._disposed)
                return;

            // 如果当前就使用 BagCustomBtn，则不要隐藏，避免无页签可点。
            bool usingBagCustom = false;
            try
            {
                if (activeTabs != null)
                {
                    for (int i = 0; i < activeTabs.Length; i++)
                    {
                        GButton t = activeTabs[i];
                        if (t == null || t._disposed)
                            continue;

                        string n = t.name ?? t.packageItem?.name ?? string.Empty;
                        if (n.IndexOf("BagCustomBtn", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            usingBagCustom = true;
                            break;
                        }
                    }
                }
            }
            catch
            {
                usingBagCustom = false;
            }

            if (usingBagCustom)
                return;

            string[] names = { "BagCustomBtn1", "BagCustomBtn2", "BagCustomBtn3", "BagCustomBtn4", "BagCustomBtn5" };
            for (int i = 0; i < names.Length; i++)
            {
                GObject obj = null;
                try { obj = window.GetChild(names[i]) ?? TryFindChildByNameRecursive(window, names[i]); } catch { obj = null; }
                if (obj == null || obj._disposed)
                    continue;

                try
                {
                    obj.visible = false;
                    obj.touchable = false;
                }
                catch
                {
                }

                try
                {
                    if (obj is GButton b && !b._disposed)
                    {
                        b.enabled = false;
                        b.grayed = true;
                        b.changeStateOnClick = false;
                    }
                }
                catch
                {
                }
            }
        }

        private static int GuessMobileInventoryPageSize(GList list, int currentSlotCandidateCount, int inventorySlots, int beltIdx, int tabCount)
        {
            int pageSize = 0;

            // 1) 若 UI 资源本身使用 Pagination 布局，可直接用 lineCount*columnCount 推导出每页格子数
            try
            {
                if (list != null && !list._disposed && list.layout == ListLayoutType.Pagination && list.lineCount > 0 && list.columnCount > 0)
                    pageSize = list.lineCount * list.columnCount;
            }
            catch
            {
                pageSize = 0;
            }

            // 2) 若已经能枚举到格子，则优先使用当前格子数量作为每页大小
            if (pageSize <= 0 && currentSlotCandidateCount > 0 && currentSlotCandidateCount <= 96)
                pageSize = currentSlotCandidateCount;

            // 3) 若背包已扩展且能整除页数，则用 (Inventory - BeltIdx) / 页数 作为每页大小
            if (pageSize <= 0 && tabCount > 0)
            {
                int bagSlots = Math.Max(0, inventorySlots - beltIdx);
                if (bagSlots > 0 && bagSlots % tabCount == 0)
                {
                    int per = bagSlots / tabCount;
                    if (per > 0 && per <= 96)
                        pageSize = per;
                }
            }

            // 4) 兜底：按默认 46 - BeltIdx（通常为 40 格）
            if (pageSize <= 0)
                pageSize = Math.Max(1, 46 - Math.Max(0, beltIdx));

            return Math.Clamp(pageSize, 1, 96);
        }

        private static void TryBindMobileInventoryPageTabsIfDue(MobileItemGridBinding binding)
        {
            if (binding == null || binding.Window == null || binding.Window._disposed)
                return;

            if (!TryResolveMobileInventoryPageTabs(binding.Window, out GButton[] tabs, out int tabCount))
                return;

            // 需求：背包分页应使用 BgRadioBtn1~5；BagCustomBtn1~5 仅用于兼容，且应隐藏。
            try { HideMobileInventoryBagCustomPageTabsIfPresent(binding.Window, tabs); } catch { }

            for (int i = tabCount; i < binding.PageTabs.Length; i++)
            {
                try
                {
                    if (binding.PageTabs[i] != null && !binding.PageTabs[i]._disposed && binding.PageTabClickCallbacks[i] != null)
                        binding.PageTabs[i].onClick.Remove(binding.PageTabClickCallbacks[i]);
                }
                catch
                {
                }

                binding.PageTabs[i] = null;
                binding.PageTabClickCallbacks[i] = null;
            }

            for (int i = 0; i < binding.PageTabs.Length && i < tabs.Length; i++)
            {
                GButton tab = tabs[i];
                if (tab == null || tab._disposed)
                    continue;

                // 如果按钮对象发生变化，先解绑旧回调再重新绑定
                try
                {
                    if (binding.PageTabs[i] != null && !binding.PageTabs[i]._disposed && binding.PageTabClickCallbacks[i] != null)
                        binding.PageTabs[i].onClick.Remove(binding.PageTabClickCallbacks[i]);
                }
                catch
                {
                }

                binding.PageTabs[i] = tab;
                binding.PageTabClickCallbacks[i] = null;

                try
                {
                    tab.touchable = true;
                    tab.enabled = true;
                    tab.grayed = false;
                    tab.opaque = true;
                    tab.changeStateOnClick = true;
                    tab.visible = true;
                }
                catch
                {
                }

                // 某些资源包会把页签所在容器默认设置为 touchable=false，导致 onClick 不触发；这里做一次祖先链启用兜底
                try { SetTouchableRecursive(tab, touchable: true); } catch { }
                try
                {
                    GObject p = tab.parent;
                    int guard = 0;
                    while (p != null && !ReferenceEquals(p, binding.Window) && guard++ < 10)
                    {
                        try
                        {
                            p.touchable = true;
                            if (p is GButton pb)
                            {
                                pb.enabled = true;
                                pb.grayed = false;
                                pb.changeStateOnClick = true;
                            }
                        }
                        catch
                        {
                        }

                        p = p.parent;
                    }
                }
                catch
                {
                }

                int pageIndex = i;
                binding.PageTabClickCallbacks[i] = () => OnMobileInventoryPageTabClicked(binding, pageIndex, tabCount);
                try { tab.onClick.Add(binding.PageTabClickCallbacks[i]); } catch { }
            }

            // 初次绑定时同步一次选中态
            ApplyMobileInventoryPageTabVisuals(binding, binding.CurrentPage);
        }

        private static void ApplyMobileInventoryPageTabVisuals(MobileItemGridBinding binding, int currentPage)
        {
            if (binding == null)
                return;

            currentPage = Math.Clamp(currentPage, 0, binding.PageTabs.Length - 1);

            for (int i = 0; i < binding.PageTabs.Length; i++)
            {
                GButton tab = binding.PageTabs[i];
                if (tab == null || tab._disposed)
                    continue;

                try
                {
                    tab.selected = i == currentPage;
                }
                catch
                {
                }
            }
        }

        private static void OnMobileInventoryPageTabClicked(MobileItemGridBinding binding, int pageIndex, int tabCount)
        {
            if (binding == null)
                return;

            pageIndex = Math.Clamp(pageIndex, 0, Math.Max(0, tabCount - 1));

            ApplyMobileInventoryPageTabVisuals(binding, pageIndex);

            // 切页时隐藏 Tips，避免悬浮框指向错误物品
            try { HideMobileItemTips(); } catch { }

            var user = GameScene.User;
            UserItem[] inventory = user?.Inventory;

            int beltIdx = 0;
            try { beltIdx = user?.BeltIdx ?? 0; } catch { beltIdx = 0; }
            beltIdx = inventory != null ? Math.Clamp(beltIdx, 0, inventory.Length) : Math.Max(0, beltIdx);

            int pageSize = binding.PageSize > 0 ? binding.PageSize : binding.Slots.Count;
            if (pageSize <= 0)
                return;

            int bagSlots = 0;
            try { bagSlots = Math.Max(0, (inventory?.Length ?? 0) - beltIdx); } catch { bagSlots = 0; }

            // 移动端：页签按钮用于“分页背包”。即便当前总槽位不足以填满多页，也应允许切换（空页显示为空即可），
            // 避免出现“BgRadioBtn1~5 点击无效”的观感问题。
            bool treatAsInventoryPagination = tabCount > 1;

            binding.CurrentPage = pageIndex;

            if (treatAsInventoryPagination)
            {
                // 更新 slot -> InventoryIndex 映射
                binding.SlotIndexOffset = beltIdx;
                binding.PageSize = pageSize;

                for (int i = 0; i < binding.Slots.Count; i++)
                {
                    MobileItemSlotBinding slot = binding.Slots[i];
                    if (slot == null)
                        continue;

                    slot.SlotIndex = beltIdx + pageIndex * pageSize + i;
                }

                TryRefreshMobileInventoryIfDue(force: true);
                return;
            }

            // 非真正分页：优先转发到资源包自带的 BagCustomBtnX（它可能绑定了筛选/切换逻辑）。
            try
            {
                GComponent window = binding.Window;
                if (window != null && !window._disposed)
                {
                    string customName = "BagCustomBtn" + (pageIndex + 1);
                    GObject custom = window.GetChild(customName) ?? TryFindChildByNameRecursive(window, customName);
                    if (custom != null && !custom._disposed)
                    {
                        custom.onClick.Call();
                    }
                }
            }
            catch
            {
            }

            // 若 UI 切换导致格子树重建（slot disposed），强制下一帧重新绑定
            try
            {
                binding.Slots.Clear();
                _nextMobileInventoryBindAttemptUtc = DateTime.MinValue;
            }
            catch
            {
            }

            TryBindMobileInventoryWindowIfDue(binding.WindowKey, binding.Window, binding.ResolveInfo);
            TryRefreshMobileInventoryIfDue(force: true);
        }

        private static GButton ResolveMobileInventoryButton(
            GComponent window,
            string overrideSpec,
            string preferName,
            string[] defaultKeywords,
            out string resolveInfo,
            out string[] overrideKeywords)
        {
            resolveInfo = null;
            overrideKeywords = null;

            if (window == null || window._disposed)
                return null;

            overrideSpec = overrideSpec?.Trim() ?? string.Empty;

            if (!string.IsNullOrWhiteSpace(overrideSpec))
            {
                try
                {
                    if (TryResolveMobileMainHudObjectBySpec(window, overrideSpec, out GObject resolved, out string[] keywords))
                    {
                        if (resolved is GButton resolvedButton && !resolvedButton._disposed)
                        {
                            resolveInfo = DescribeObject(window, resolvedButton) + " (override)";
                            return resolvedButton;
                        }

                        if (keywords != null && keywords.Length > 0)
                            overrideKeywords = keywords;
                    }
                    else
                    {
                        overrideKeywords = SplitKeywords(overrideSpec);
                    }
                }
                catch
                {
                }
            }

            if (!string.IsNullOrWhiteSpace(preferName))
            {
                try
                {
                    // packageItem.name 通常更稳定；递归查找能兼容被包在容器里的情况
                    GObject byName = window.GetChild(preferName) ?? TryFindChildByNameRecursive(window, preferName);
                    if (byName is GButton button && !button._disposed)
                    {
                        resolveInfo = DescribeObject(window, button) + " (name:" + preferName + ")";
                        return button;
                    }
                }
                catch
                {
                }
            }

            string[] keywordsUsed = overrideKeywords != null && overrideKeywords.Length > 0
                ? overrideKeywords
                : defaultKeywords;

            if (keywordsUsed == null || keywordsUsed.Length == 0)
                return null;

            try
            {
                List<(int Score, GObject Target)> candidates = CollectMobileChatCandidates(window, obj => obj is GButton, keywordsUsed, ScoreMobileShopButtonCandidate);
                int minScore = overrideKeywords != null && overrideKeywords.Length > 0 ? 35 : 50;
                GButton selected = SelectMobileChatCandidate<GButton>(candidates, minScore);
                if (selected != null && !selected._disposed)
                {
                    resolveInfo = DescribeObject(window, selected) + (overrideKeywords != null && overrideKeywords.Length > 0 ? " (keywords)" : " (auto)");
                    return selected;
                }
            }
            catch
            {
            }

            return null;
        }

        private static void OnMobileInventoryWarehouseButtonClicked(GButton button)
        {
            try { ApplyMobileButtonCooldown(button, seconds: 0.9f); } catch { }

            if (TryShowMobileWindowByKeywords("Storage", new[] { "仓库_StorageUI", "仓库", "Storage" }))
            {
                TryApplyMobileInventoryStorageSideBySideLayoutIfDue(force: true);
                return;
            }

            MobileHint("仓库界面未找到或未加载。");
        }

        internal static void RequestMobileInventoryStorageSideBySideLayout()
        {
            try
            {
                TryApplyMobileInventoryStorageSideBySideLayoutIfDue(force: true);
            }
            catch
            {
            }
        }

        private static void TryApplyMobileInventoryStorageSideBySideLayoutIfDue(bool force)
        {
            if (_stage == null || !_initialized || !_packagesLoaded)
                return;

            if (!force && DateTime.UtcNow < _nextMobileInventoryStorageSideBySideLayoutUtc)
                return;

            _nextMobileInventoryStorageSideBySideLayoutUtc = DateTime.UtcNow.AddMilliseconds(force ? 80 : 180);

            if (!MobileWindows.TryGetValue("Inventory", out GComponent inventoryWindow) || inventoryWindow == null || inventoryWindow._disposed)
            {
                _mobileInventoryStorageSideBySideLayoutApplied = false;
                return;
            }

            if (!MobileWindows.TryGetValue("Storage", out GComponent storageWindow) || storageWindow == null || storageWindow._disposed)
            {
                if (_mobileInventoryStorageSideBySideLayoutApplied)
                    TryRestoreMobileOverlayWindowLayout(inventoryWindow);

                _mobileInventoryStorageSideBySideLayoutApplied = false;
                return;
            }

            bool inventoryVisible = false;
            bool storageVisible = false;
            try { inventoryVisible = inventoryWindow.visible; } catch { inventoryVisible = false; }
            try { storageVisible = storageWindow.visible; } catch { storageVisible = false; }

            if (!inventoryVisible || !storageVisible)
            {
                if (_mobileInventoryStorageSideBySideLayoutApplied)
                {
                    if (inventoryVisible)
                        TryRestoreMobileOverlayWindowLayout(inventoryWindow);
                    if (storageVisible)
                        TryRestoreMobileOverlayWindowLayout(storageWindow);
                }

                _mobileInventoryStorageSideBySideLayoutApplied = false;
                return;
            }

            GComponent layer = storageWindow.parent ?? inventoryWindow.parent;
            if (layer == null || layer._disposed)
                return;

            float parentW;
            float parentH;
            try
            {
                parentW = Math.Max(1F, layer.width);
                parentH = Math.Max(1F, layer.height);
            }
            catch
            {
                return;
            }

            float targetW = parentW * 0.5F;
            float margin = Math.Max(0F, Math.Min(parentW, parentH) * 0.006F);

            ApplySideBySide(inventoryWindow, left: true);
            ApplySideBySide(storageWindow, left: false);

            _mobileInventoryStorageSideBySideLayoutApplied = true;

            void ApplySideBySide(GComponent window, bool left)
            {
                if (window == null || window._disposed)
                    return;

                float baseW;
                float baseH;
                try
                {
                    baseW = Math.Max(1F, window.width);
                    baseH = Math.Max(1F, window.height);
                }
                catch
                {
                    baseW = 1F;
                    baseH = 1F;
                }

                float scaleW = (targetW - margin * 2F) / baseW;
                float scaleH = (parentH - margin * 2F) / baseH;
                float scale = Math.Min(1F, Math.Min(scaleW, scaleH));
                if (float.IsNaN(scale) || float.IsInfinity(scale) || scale <= 0.02F)
                    scale = 1F;

                try { window.SetScale(scale, scale); } catch { }

                try
                {
                    float x = left ? margin : Math.Max(margin, parentW - baseW * scale - margin);
                    float y = margin;
                    window.SetPosition(x, y);
                }
                catch
                {
                }
            }
        }

        private static void TryRestoreMobileOverlayWindowLayout(GComponent window)
        {
            if (window == null || window._disposed)
                return;

            try { window.SetScale(1F, 1F); } catch { }
            try { window.SetPosition(0F, 0F); } catch { }
        }

        private static void OnMobileInventorySortButtonClicked(GButton button)
        {
            try { ApplyMobileButtonCooldown(button, seconds: 1.5f); } catch { }

            int skippedLocked;
            int moveCount = EnqueueMobileInventoryCompactMoves(out skippedLocked);

            if (moveCount <= 0)
            {
                if (skippedLocked > 0)
                    MobileHint($"包裹无需整理（已跳过锁定物品 {skippedLocked} 个）。");
                else
                    MobileHint("包裹无需整理。");

                return;
            }

            if (skippedLocked > 0)
                MobileHint($"已整理包裹：移动 {moveCount} 次（跳过锁定物品 {skippedLocked} 个）。");
            else
                MobileHint($"已整理包裹：移动 {moveCount} 次。");

            // 若背包存在分页，整理后通常物品会集中到前几页；自动回到第一页避免出现“空格很多”的错觉。
            try { TryReturnMobileInventoryToFirstPageAfterSort(); } catch { }
        }

        private static void TryReturnMobileInventoryToFirstPageAfterSort()
        {
            MobileItemGridBinding binding = _mobileInventoryBinding;
            if (binding == null || binding.Window == null || binding.Window._disposed)
                return;

            if (!TryResolveMobileInventoryPageTabs(binding.Window, out _, out int tabCount) || tabCount <= 1)
                return;

            // 使用统一的切页逻辑，确保 slot -> InventoryIndex 映射同步刷新
            OnMobileInventoryPageTabClicked(binding, pageIndex: 0, tabCount: tabCount);
        }

        private static int EnqueueMobileInventoryCompactMoves(out int skippedLocked)
        {
            skippedLocked = 0;

            UserItem[] inventory = null;
            try { inventory = GameScene.User?.Inventory; } catch { inventory = null; }

            if (inventory == null || inventory.Length == 0)
                return 0;

            var working = new UserItem[inventory.Length];
            try { Array.Copy(inventory, working, inventory.Length); } catch { }

            var moves = new List<(int From, int To)>(inventory.Length);

            // 移动端默认不整理快捷栏（腰带栏），避免把普通物品挤到主界面快捷栏里。
            int startIndex = 0;
            try { startIndex = GameScene.User?.BeltIdx ?? 0; } catch { startIndex = 0; }
            startIndex = Math.Clamp(startIndex, 0, working.Length);

            int empty = startIndex;
            while (empty < working.Length && working[empty] != null)
                empty++;

            if (empty >= working.Length)
                return 0;

            for (int i = empty + 1; i < working.Length; i++)
            {
                if (empty >= working.Length)
                    break;

                UserItem item = working[i];
                if (item == null)
                    continue;

                if (IsMobileItemLocked(item.UniqueID))
                {
                    skippedLocked++;
                    continue;
                }

                // 确保 empty 指向空位
                while (empty < working.Length && working[empty] != null)
                    empty++;

                if (empty >= working.Length || empty >= i)
                    continue;

                moves.Add((i, empty));

                // MoveItem 在服务端是 swap：目标位为空时即为“移动并留下空位”
                working[empty] = item;
                working[i] = null;
            }

            for (int i = 0; i < moves.Count; i++)
            {
                (int from, int to) = moves[i];

                try
                {
                    MonoShare.MirNetwork.Network.Enqueue(new C.MoveItem
                    {
                        Grid = MirGridType.Inventory,
                        From = from,
                        To = to,
                    });
                }
                catch
                {
                }
            }

            return moves.Count;
        }

        private static void TryBindMobileInventoryWindowIfDue(string windowKey, GComponent window, string resolveInfo)
        {
            if (window == null || window._disposed)
                return;

            MobileItemGridBinding binding = _mobileInventoryBinding;
            if (binding != null && (binding.Window == null || binding.Window._disposed))
            {
                ResetMobileInventoryBindings();
                binding = null;
            }

            if (binding == null || !ReferenceEquals(binding.Window, window))
            {
                binding = new MobileItemGridBinding
                {
                    WindowKey = windowKey,
                    Window = window,
                    ResolveInfo = resolveInfo,
                };

                _mobileInventoryBinding = binding;
                _mobileInventoryBindingsDumped = false;
                _nextMobileInventoryBindAttemptUtc = DateTime.MinValue;
            }

            if (binding.Slots.Count > 0)
            {
                bool needsButtons =
                    binding.WarehouseButton == null || binding.WarehouseButton._disposed || binding.WarehouseButtonClickCallback == null ||
                    binding.SortButton == null || binding.SortButton._disposed || binding.SortButtonClickCallback == null;

                if (needsButtons)
                    TryBindMobileInventoryButtonsIfDue(binding);

                // 分页标签（右侧 1/2/3）
                TryBindMobileInventoryPageTabsIfDue(binding);

                return;
            }

            if (DateTime.UtcNow < _nextMobileInventoryBindAttemptUtc)
                return;

            _nextMobileInventoryBindAttemptUtc = DateTime.UtcNow.AddSeconds(2);

            string overrideSpec = string.Empty;
            try
            {
                InIReader reader = TryCreateConfigReader();
                if (reader != null)
                {
                    overrideSpec = reader.ReadString(
                        FairyGuiConfigSectionName,
                        MobileInventoryGridConfigKey,
                        string.Empty,
                        writeWhenNull: false);
                }
            }
            catch
            {
                overrideSpec = string.Empty;
            }

            overrideSpec = overrideSpec?.Trim() ?? string.Empty;
            binding.OverrideSpec = overrideSpec;
            binding.OverrideKeywords = null;

            GComponent gridRoot = window;
            string gridResolveInfo = DescribeObject(window, window);

            if (!string.IsNullOrWhiteSpace(overrideSpec))
            {
                if (TryResolveMobileMainHudObjectBySpec(window, overrideSpec, out GObject resolved, out string[] keywords))
                {
                    if (resolved is GComponent resolvedComponent && !resolvedComponent._disposed)
                    {
                        gridRoot = resolvedComponent;
                        gridResolveInfo = DescribeObject(window, resolvedComponent) + " (override)";
                    }
                    else if (keywords != null && keywords.Length > 0)
                    {
                        binding.OverrideKeywords = keywords;
                    }
                }
                else
                {
                    binding.OverrideKeywords = SplitKeywords(overrideSpec);
                }
            }

            if (binding.OverrideKeywords != null && binding.OverrideKeywords.Length > 0)
            {
                List<(int Score, GObject Target)> candidates = CollectMobileChatCandidates(window, obj => obj is GComponent, binding.OverrideKeywords, ScoreMobileInventoryGridCandidate);
                GComponent selected = SelectMobileChatCandidate<GComponent>(candidates, minScore: 40);
                if (selected != null && !selected._disposed)
                {
                    gridRoot = selected;
                    gridResolveInfo = DescribeObject(window, selected) + " (keywords)";
                }
            }
            else
            {
                List<(int Score, GObject Target)> candidates = CollectMobileChatCandidates(window, obj => obj is GComponent, DefaultInventoryGridKeywords, ScoreMobileInventoryGridCandidate);
                GComponent selected = SelectMobileChatCandidate<GComponent>(candidates, minScore: 60);
                if (selected != null && !selected._disposed)
                {
                    gridRoot = selected;
                    gridResolveInfo = DescribeObject(window, selected) + " (auto)";
                }
            }

            binding.GridRoot = gridRoot;
            binding.GridResolveInfo = gridResolveInfo;

            int expectedInventorySlots = GameScene.User?.Inventory?.Length ?? 46;
            int expectedBeltIdx = 0;
            try { expectedBeltIdx = GameScene.User?.BeltIdx ?? 0; } catch { expectedBeltIdx = 0; }
            expectedBeltIdx = Math.Clamp(expectedBeltIdx, 0, Math.Max(0, expectedInventorySlots));

            if (string.IsNullOrWhiteSpace(overrideSpec))
            {
                try
                {
                    GComponent knownGridRoot = FindBestKnownInventoryGridRoot(window, expectedInventorySlots, expectedBeltIdx, out string knownResolveInfo);
                    if (knownGridRoot != null && !knownGridRoot._disposed)
                    {
                        gridRoot = knownGridRoot;
                        gridResolveInfo = knownResolveInfo;
                        binding.GridRoot = gridRoot;
                        binding.GridResolveInfo = gridResolveInfo;
                    }
                }
                catch
                {
                }
            }

            int desiredSlots = expectedInventorySlots;

            // 更稳健的自动选择：优先按“物品格子数量接近背包容量”挑选 gridRoot，避免误命中顶部快捷栏/装备用格。
            if (string.IsNullOrWhiteSpace(overrideSpec) && (binding.OverrideKeywords == null || binding.OverrideKeywords.Length == 0))
            {
                try
                {
                    GComponent autoRoot = AutoSelectMainHudGridRootFromItemSlots(window, desiredSlots, out string autoInfo);
                    if (autoRoot != null && !autoRoot._disposed && !ReferenceEquals(autoRoot, window))
                    {
                        gridRoot = autoRoot;
                        gridResolveInfo = autoInfo + " (auto:slots)";
                        binding.GridRoot = gridRoot;
                        binding.GridResolveInfo = gridResolveInfo;
                    }
                }
                catch
                {
                }
            }

            // 特判：背包窗口通常存在名为 DBagGrid 的 GList，优先使用它避免误命中其它区域（如顶部栏/按钮面板）。
            if (string.Equals(windowKey, "Inventory", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    GObject exactInventoryGrid =
                        window.GetChild("DBagGrid") ?? TryFindChildByNameRecursive(window, "DBagGrid") ??
                        window.GetChild("GameItemGrid") ?? TryFindChildByNameRecursive(window, "GameItemGrid") ??
                        window.GetChild("DBag") ?? TryFindChildByNameRecursive(window, "DBag") ??
                        window.GetChild("DBagUI") ?? TryFindChildByNameRecursive(window, "DBagUI");

                    GComponent bagGrid = exactInventoryGrid as GComponent;
                    if (bagGrid != null && !bagGrid._disposed && !ReferenceEquals(bagGrid, gridRoot))
                    {
                        gridRoot = bagGrid;
                        gridResolveInfo = DescribeObject(window, bagGrid) + " (known-name)";
                        binding.GridRoot = gridRoot;
                        binding.GridResolveInfo = gridResolveInfo;
                    }
                }
                catch
                {
                }
            }

            int beltIdx = expectedBeltIdx;

            bool hasPageTabs = false;
            int pageTabCount = 0;
            if (TryResolveMobileInventoryPageTabs(window, out GButton[] resolvedTabs, out int resolvedTabCount))
            {
                hasPageTabs = true;
                pageTabCount = resolvedTabCount;

                // 先缓存 tab 引用，便于后续绑定 click 回调（不要在这里 Add，避免重复叠加）
                try
                {
                    for (int i = 0; i < binding.PageTabs.Length; i++)
                        binding.PageTabs[i] = i < resolvedTabs.Length ? resolvedTabs[i] : null;
                }
                catch
                {
                }

                try
                {
                    for (int i = 0; i < resolvedTabs.Length; i++)
                    {
                        GButton tab = resolvedTabs[i];
                        if (tab == null || tab._disposed)
                            continue;

                        try
                        {
                            tab.visible = true;
                            tab.touchable = true;
                            tab.enabled = true;
                            tab.grayed = false;
                            tab.changeStateOnClick = true;
                        }
                        catch
                        {
                        }

                        try { SetTouchableRecursive(tab, touchable: true); } catch { }

                        try
                        {
                            GObject parent = tab.parent;
                            int guard = 0;
                            while (parent != null && !ReferenceEquals(parent, window) && guard++ < 10)
                            {
                                try
                                {
                                    parent.visible = true;
                                    parent.touchable = true;
                                    if (parent is GButton parentButton)
                                    {
                                        parentButton.enabled = true;
                                        parentButton.grayed = false;
                                        parentButton.changeStateOnClick = true;
                                    }
                                }
                                catch
                                {
                                }

                                parent = parent.parent;
                            }
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

            List<GComponent> slotCandidates = CollectInventorySlotCandidates(gridRoot);

            if (slotCandidates.Count == 0 && gridRoot is GList inventoryList && inventoryList != null && !inventoryList._disposed)
            {
                try
                {
                    int desiredBagSlots = Math.Max(1, desiredSlots - Math.Max(0, beltIdx));
                    bool adjusted = false;

                    try
                    {
                        if (inventoryList.numItems != desiredBagSlots)
                        {
                            inventoryList.numItems = desiredBagSlots;
                            adjusted = true;
                        }
                    }
                    catch
                    {
                        adjusted = false;
                    }

                    if (adjusted)
                        slotCandidates = CollectInventorySlotCandidates(gridRoot);
                }
                catch
                {
                }
            }

            if (slotCandidates.Count <= 1)
            {
                try
                {
                    GComponent knownGridRoot = FindBestKnownInventoryGridRoot(window, desiredSlots, beltIdx, out string knownResolveInfo);
                    if (knownGridRoot != null && !knownGridRoot._disposed && !ReferenceEquals(knownGridRoot, gridRoot))
                    {
                        List<GComponent> knownSlotCandidates = CollectInventorySlotCandidates(knownGridRoot);
                        if (knownSlotCandidates.Count > slotCandidates.Count)
                        {
                            gridRoot = knownGridRoot;
                            gridResolveInfo = knownResolveInfo;
                            binding.GridRoot = gridRoot;
                            binding.GridResolveInfo = gridResolveInfo;
                            slotCandidates = knownSlotCandidates;
                        }
                    }
                }
                catch
                {
                }
            }

            // 兜底：若 gridRoot 误选导致 0 格，退回到窗口根节点再试一次
            if (slotCandidates.Count == 0 && !ReferenceEquals(gridRoot, window))
            {
                try
                {
                    gridRoot = window;
                    gridResolveInfo = DescribeObject(window, window) + " (fallback:window)";
                    binding.GridRoot = gridRoot;
                    binding.GridResolveInfo = gridResolveInfo;
                }
                catch
                {
                }

                slotCandidates = CollectInventorySlotCandidates(gridRoot);
            }

            if (slotCandidates.Count == 0)
            {
                CMain.SaveError("FairyGUI: 背包窗口未找到物品格子（Inventory）。可在 Mir2Config.ini 设置 [" + FairyGuiConfigSectionName + "] " +
                                MobileInventoryGridConfigKey + "=idx:... 指定格子根节点（或 path:/name:/item:/url:/title: / 关键字列表 a|b|c）。");
                return;
            }

            SortGComponentsByGlobalPosition(slotCandidates);

            // 计算当前 UI 的“每页容量”（优先使用 Pagination 布局推导，否则按默认 46-BeltIdx）。
            int slotCount = slotCandidates.Count;
            int uiPageSize = 0;
            try
            {
                if (gridRoot is GList pageList && pageList != null && !pageList._disposed)
                {
                    // 这里不使用 tabCount 参与推导，避免出现“40格背包 + 5个页签 => 每页8格”的误判。
                    uiPageSize = GuessMobileInventoryPageSize(pageList, slotCandidates.Count, desiredSlots, beltIdx, 0);
                }
            }
            catch
            {
                uiPageSize = 0;
            }

            if (uiPageSize > 0)
                slotCount = Math.Min(slotCount, uiPageSize);

            // 经典移动端背包是“总 46 格，前 6 格为腰带栏，不应在包裹网格中重复显示”。
            // 这里额外按“非腰带背包容量”收口，避免把腰带映射格或被裁剪的额外分页格一起绑定进来。
            int maxBagSlotsToDisplay = Math.Max(1, desiredSlots - Math.Max(0, beltIdx));
            if (maxBagSlotsToDisplay > 0)
                slotCount = Math.Min(slotCount, maxBagSlotsToDisplay);

            slotCount = Math.Clamp(slotCount, 1, 96);

            // 移动端背包：始终跳过腰带栏（Inventory[0..BeltIdx)），页签用于分页（允许切到空页）。
            bool treatPageTabsAsInventoryPagination = hasPageTabs && pageTabCount > 1;

            binding.PageSize = slotCount;
            binding.SlotIndexOffset = beltIdx;
            binding.CurrentPage = treatPageTabsAsInventoryPagination
                ? Math.Clamp(binding.CurrentPage, 0, Math.Max(0, pageTabCount - 1))
                : 0;

            int slotIndexOffset = beltIdx + binding.CurrentPage * Math.Max(1, slotCount);

            binding.Slots.Clear();
            int fallbackIconSlots = 0;

            for (int i = 0; i < slotCount; i++)
            {
                GComponent slotRoot = slotCandidates[i];
                int inventoryIndex = i + slotIndexOffset;
                var slot = new MobileItemSlotBinding
                {
                    SlotIndex = inventoryIndex,
                    Root = slotRoot,
                    Icon = FindBestInventorySlotIcon(slotRoot),
                    IconImage = FindBestInventorySlotIconImage(slotRoot),
                    Count = FindBestInventorySlotCount(slotRoot),
                    LockedMarker = FindBestInventorySlotLockedMarker(slotRoot),
                    HasItem = false,
                    LastIcon = 0,
                    LastCountDisplayed = 0,
                };

                // 兜底：某些 publish 包的背包格子 icon 组件结构不稳定（可能找到了非“物品图标”的 Loader，
                // 或者不同格子 icon 默认 visible=false/被遮挡）。这里统一创建一个覆盖层 AutoItemIcon，
                // 并以它作为最终渲染目标，避免出现“只显示第一个物品”的问题。
                try
                {
                    GLoader originalLoader = slot.Icon;
                    GImage originalImage = slot.IconImage;

                    GLoader autoIcon = null;
                    try { autoIcon = slotRoot.GetChild("AutoItemIcon") as GLoader; } catch { autoIcon = null; }

                    if (autoIcon == null || autoIcon._disposed)
                    {
                        autoIcon = new GLoader
                        {
                            name = "AutoItemIcon",
                            touchable = false,
                            visible = true,
                            showErrorSign = false,
                            align = AlignType.Center,
                            verticalAlign = VertAlignType.Middle,
                            fill = FillType.Scale,
                        };

                        try
                        {
                            autoIcon.SetPosition(0f, 0f);
                            autoIcon.SetSize(Math.Max(10f, slotRoot.width), Math.Max(10f, slotRoot.height));
                        }
                        catch
                        {
                        }

                        try
                        {
                            autoIcon.AddRelation(slotRoot, RelationType.Size);
                        }
                        catch
                        {
                        }

                        try
                        {
                            slotRoot.AddChild(autoIcon);
                        }
                        catch
                        {
                        }
                    }

                    slot.Icon = autoIcon;
                    slot.IconImage = null;
                    fallbackIconSlots++;

                    // 隐藏资源包自带“物品图标”层（若存在），避免重复叠加/遮挡
                    try
                    {
                        if (originalLoader != null && !originalLoader._disposed && !ReferenceEquals(originalLoader, autoIcon))
                        {
                            string n = originalLoader.name ?? originalLoader.packageItem?.name ?? string.Empty;
                            if (n.IndexOf("icon", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                n.IndexOf("item", StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                originalLoader.visible = false;
                            }
                        }
                    }
                    catch
                    {
                    }
                    try
                    {
                        if (originalImage != null && !originalImage._disposed)
                        {
                            string n = originalImage.name ?? originalImage.packageItem?.name ?? string.Empty;
                            if (n.IndexOf("icon", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                n.IndexOf("item", StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                originalImage.visible = false;
                            }
                        }
                    }
                    catch
                    {
                    }
                }
                catch
                {
                }

                try
                {
                    if (slotRoot != null && !slotRoot._disposed)
                    {
                        EventCallback0 callback = () => OnMobileInventorySlotClicked(slot.SlotIndex);
                        slot.ClickCallback = callback;
                        slotRoot.onClick.Add(callback);

                        // 长按弹出物品 Tips（点击留给消耗/穿戴等交互）
                        try
                        {
                            slot.LongPressDragBinding = BindMobileLongPressItemDrag(
                                slotRoot,
                                resolveItem: () =>
                                {
                                    try
                                    {
                                        UserItem[] inventory = GameScene.User?.Inventory;
                                        int idx = slot.SlotIndex;
                                        if (inventory != null && idx >= 0 && idx < inventory.Length)
                                            return inventory[idx];
                                    }
                                    catch
                                    {
                                    }

                                    return null;
                                },
                                resolvePayload: () =>
                                {
                                    UserItem t = null;
                                    try
                                    {
                                        UserItem[] inventory = GameScene.User?.Inventory;
                                        int idx = slot.SlotIndex;
                                        if (inventory != null && idx >= 0 && idx < inventory.Length)
                                            t = inventory[idx];
                                    }
                                    catch
                                    {
                                        t = null;
                                    }

                                    if (t == null)
                                        return null;

                                    if (t.UniqueID == 0)
                                        return null;

                                    return new MobileItemDragPayload
                                    {
                                        Grid = MirGridType.Inventory,
                                        SlotIndex = slot.SlotIndex,
                                        UniqueId = t.UniqueID,
                                    };
                                });
                        }
                        catch
                        {
                            slot.LongPressDragBinding = null;
                        }

                        try
                        {
                            slot.DropCallback = context => OnMobileItemDroppedOnInventorySlot(slot.SlotIndex, context);
                            slotRoot.AddEventListener("onDrop", slot.DropCallback);
                        }
                        catch
                        {
                            slot.DropCallback = null;
                        }
                    }
                }
                catch
                {
                }

                binding.Slots.Add(slot);
            }

            // 金币文本（背包窗口常见为 DGoldText2）
            try
            {
                if (binding.GoldText == null || binding.GoldText._disposed)
                {
                    try { binding.GoldText = window.GetChild("DGoldText2") as GTextField; } catch { binding.GoldText = null; }
                    if (binding.GoldText == null || binding.GoldText._disposed)
                    {
                        try { binding.GoldText = window.GetChild("DGoldText") as GTextField; } catch { binding.GoldText = null; }
                    }

                    if (binding.GoldText == null || binding.GoldText._disposed)
                    {
                        var candidates = CollectMobileChatCandidates(
                            window,
                            obj => obj is GTextField && obj is not GTextInput,
                            new[] { "gold", "money", "金币", "DGold" },
                            ScoreMobileShopTextCandidate);
                        binding.GoldText = SelectMobileChatCandidate<GTextField>(candidates, minScore: 25);
                    }

                    if (binding.GoldText != null && !binding.GoldText._disposed)
                        binding.GoldTextResolveInfo = DescribeObject(window, binding.GoldText) + " (auto)";
                }
            }
            catch
            {
            }

            TryBindMobileInventoryButtonsIfDue(binding);
            TryBindMobileInventoryPageTabsIfDue(binding);
            TryDumpMobileInventoryBindingsReportIfDue(binding, desiredSlots, slotCandidates);

            int firstSlotIndex = binding.Slots.Count > 0 ? binding.Slots[0]?.SlotIndex ?? -1 : -1;
            int lastSlotIndex = binding.Slots.Count > 0 ? binding.Slots[binding.Slots.Count - 1]?.SlotIndex ?? -1 : -1;
            CMain.SaveLog($"FairyGUI: 背包窗口绑定完成：Slots={binding.Slots.Count} Candidates={slotCandidates.Count} GridRoot={binding.GridResolveInfo} " +
                          $"FallbackIcons={fallbackIconSlots} PageTabs={(hasPageTabs ? pageTabCount : 0)} " +
                          $"Pagination={(treatPageTabsAsInventoryPagination ? 1 : 0)} Page={binding.CurrentPage} PageSize={binding.PageSize} " +
                          $"BeltIdx={beltIdx} SlotIndexRange={firstSlotIndex}..{lastSlotIndex}");
        }

        private static void TryRefreshMobileInventoryIfDue(bool force)
        {
            if (_stage == null || !_initialized || !_packagesLoaded)
                return;

            if (!MobileWindows.TryGetValue("Inventory", out GComponent window) || window == null || window._disposed)
            {
                if (_mobileInventoryBinding != null)
                    ResetMobileInventoryBindings();
                return;
            }

            if (!window.visible)
                return;

            TryBindMobileInventoryWindowIfDue("Inventory", window, resolveInfo: null);

            MobileItemGridBinding binding = _mobileInventoryBinding;
            if (binding == null || binding.Slots.Count == 0)
                return;

            if (binding.Window == null || binding.Window._disposed)
            {
                ResetMobileInventoryBindings();
                return;
            }

            var user = GameScene.User;
            if (user == null || user.Inventory == null)
                return;

            UserItem[] items = user.Inventory;
            int totalSlots = binding.Slots.Count;

            for (int i = 0; i < totalSlots; i++)
            {
                MobileItemSlotBinding slot = binding.Slots[i];
                if (slot == null || slot.Root == null || slot.Root._disposed)
                {
                    binding.Slots.Clear();
                    _nextMobileInventoryBindAttemptUtc = DateTime.MinValue;
                    return;
                }

                int itemIndex = slot.SlotIndex;
                UserItem item = itemIndex >= 0 && itemIndex < items.Length ? items[itemIndex] : null;
                if (item == null)
                {
                    if (slot.HasItem)
                        ClearInventorySlot(slot);
                    continue;
                }

                // 兼容：部分服务端/链路下 Inventory 里会出现 Info 未绑定的物品（PC 端仍可显示图标），移动端这里尝试补绑。
                if (item.Info == null)
                {
                    try { GameScene.Bind(item); } catch { }
                }

                ushort iconIndex = 0;
                try { iconIndex = item.Image; } catch { iconIndex = 0; }
                ushort countToShow = item.Count > 1 ? item.Count : (ushort)0;

                if (iconIndex != 0)
                    Libraries.Items.Touch(iconIndex);

                // 某些背包格子里 icon Loader 默认是隐藏的，仅设置 texture 不会显示
                try
                {
                    if (slot.Icon != null && !slot.Icon._disposed)
                    {
                        slot.Icon.visible = true;
                    }
                    else if (slot.IconImage != null && !slot.IconImage._disposed)
                    {
                        slot.IconImage.visible = true;
                    }
                }
                catch
                {
                }

                bool needsIconRefresh = !slot.HasItem || slot.LastIcon != iconIndex;
                if (!needsIconRefresh)
                {
                    bool textureOk = false;

                    if (slot.Icon != null && !slot.Icon._disposed)
                    {
                        try
                        {
                            NTexture current = slot.Icon.texture;
                            Texture2D native = current?.nativeTexture;
                            textureOk = current != null && native != null && !native.IsDisposed;
                        }
                        catch
                        {
                            textureOk = false;
                        }
                    }
                    else if (slot.IconImage != null && !slot.IconImage._disposed)
                    {
                        try
                        {
                            NTexture current = slot.IconImage.texture;
                            Texture2D native = current?.nativeTexture;
                            textureOk = current != null && native != null && !native.IsDisposed;
                        }
                        catch
                        {
                            textureOk = false;
                        }
                    }

                    if (!textureOk)
                        needsIconRefresh = true;
                }

                if (needsIconRefresh)
                {
                    NTexture texture = GetOrCreateItemIconTexture(iconIndex);

                    if (slot.Icon != null && !slot.Icon._disposed)
                    {
                        slot.Icon.showErrorSign = false;
                        slot.Icon.url = string.Empty;
                        slot.Icon.texture = texture;
                    }
                    else if (slot.IconImage != null && !slot.IconImage._disposed)
                    {
                        slot.IconImage.texture = texture;
                    }

                    slot.LastIcon = iconIndex;
                }

                if (!slot.HasItem || slot.LastCountDisplayed != countToShow)
                {
                    if (slot.Count != null && !slot.Count._disposed)
                        slot.Count.text = countToShow > 0 ? countToShow.ToString() : string.Empty;

                    slot.LastCountDisplayed = countToShow;
                }

                ApplyMobileItemSlotLockVisual(slot, IsMobileItemLocked(item.UniqueID));
                slot.HasItem = true;
            }

            // 金币显示
            try
            {
                if (binding.GoldText != null && !binding.GoldText._disposed)
                {
                    binding.GoldText.text = GameScene.Gold.ToString("#,##0");

                    try
                    {
                        TextFormat tf = binding.GoldText.textFormat;
                        tf.size = Math.Max(tf.size, 20);
                        binding.GoldText.textFormat = tf;
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

        private static void TryBindMobileStorageWindowIfDue(string windowKey, GComponent window, string resolveInfo)
        {
            if (window == null || window._disposed)
                return;

            MobileStorageWindowBinding binding = _mobileStorageBinding;
            if (binding != null && (binding.Window == null || binding.Window._disposed))
            {
                ResetMobileStorageBindings();
                binding = null;
            }

            if (binding == null || !ReferenceEquals(binding.Window, window))
            {
                ResetMobileStorageBindings();

                binding = new MobileStorageWindowBinding
                {
                    Window = window,
                    ResolveInfo = resolveInfo,
                    SelectedIndex = -1,
                    SelectedGrid = MirGridType.Inventory,
                };

                _mobileStorageBinding = binding;
                _mobileStorageBindingsDumped = false;
                _nextMobileStorageBindAttemptUtc = DateTime.MinValue;
            }

            if (DateTime.UtcNow < _nextMobileStorageBindAttemptUtc)
                return;

            bool inventoryBound = binding.InventoryGrid != null && binding.InventoryGrid.Slots.Count > 0;
            bool storageBound = binding.StorageGrid != null && binding.StorageGrid.Slots.Count > 0;
            if (inventoryBound && storageBound)
                return;

            _nextMobileStorageBindAttemptUtc = DateTime.UtcNow.AddSeconds(2);

            int desiredInventorySlots = GameScene.User?.Inventory?.Length ?? 46;
            int desiredStorageSlots = GameScene.Storage?.Length ?? 80;

            string inventoryOverrideSpec = string.Empty;
            string storageOverrideSpec = string.Empty;

            try
            {
                InIReader reader = TryCreateConfigReader();
                if (reader != null)
                {
                    inventoryOverrideSpec = reader.ReadString(FairyGuiConfigSectionName, MobileStorageInventoryGridConfigKey, string.Empty, writeWhenNull: false);
                    storageOverrideSpec = reader.ReadString(FairyGuiConfigSectionName, MobileStorageStorageGridConfigKey, string.Empty, writeWhenNull: false);
                }
            }
            catch
            {
                inventoryOverrideSpec = string.Empty;
                storageOverrideSpec = string.Empty;
            }

            inventoryOverrideSpec = inventoryOverrideSpec?.Trim() ?? string.Empty;
            storageOverrideSpec = storageOverrideSpec?.Trim() ?? string.Empty;

            GComponent inventoryGridRoot = window;
            string inventoryGridResolveInfo = DescribeObject(window, window);
            string[] inventoryOverrideKeywords = null;

            if (string.IsNullOrWhiteSpace(inventoryOverrideSpec))
            {
                try
                {
                    GObject exactInventory = window.GetChild("DBagGrid") ?? TryFindChildByNameRecursive(window, "DBagGrid") ??
                                             window.GetChild("DBag") ?? TryFindChildByNameRecursive(window, "DBag") ??
                                             window.GetChild("GameItemGrid") ?? TryFindChildByNameRecursive(window, "GameItemGrid");
                    if (exactInventory is GComponent exactInventoryComponent && !exactInventoryComponent._disposed)
                    {
                        inventoryGridRoot = exactInventoryComponent;
                        inventoryGridResolveInfo = DescribeObject(window, exactInventoryComponent) + " (name)";
                    }
                }
                catch
                {
                }
            }

            if (!string.IsNullOrWhiteSpace(inventoryOverrideSpec))
            {
                if (TryResolveMobileMainHudObjectBySpec(window, inventoryOverrideSpec, out GObject resolved, out string[] keywords))
                {
                    if (resolved is GComponent resolvedComponent && !resolvedComponent._disposed)
                    {
                        inventoryGridRoot = resolvedComponent;
                        inventoryGridResolveInfo = DescribeObject(window, resolvedComponent) + " (override)";
                    }
                    else if (keywords != null && keywords.Length > 0)
                    {
                        inventoryOverrideKeywords = keywords;
                    }
                }
                else
                {
                    inventoryOverrideKeywords = SplitKeywords(inventoryOverrideSpec);
                }
            }

            if (inventoryOverrideKeywords != null && inventoryOverrideKeywords.Length > 0)
            {
                List<(int Score, GObject Target)> candidates = CollectMobileChatCandidates(window, obj => obj is GComponent, inventoryOverrideKeywords, ScoreMobileInventoryGridCandidate);
                for (int i = 0; i < candidates.Count; i++)
                {
                    (int score, GObject target) = candidates[i];
                    if (score < 40)
                        break;
                    if (target is GComponent selected && !selected._disposed)
                    {
                        inventoryGridRoot = selected;
                        inventoryGridResolveInfo = DescribeObject(window, selected) + " (keywords)";
                        break;
                    }
                }
            }
            else
            {
                List<(int Score, GObject Target)> candidates = CollectMobileChatCandidates(window, obj => obj is GComponent, DefaultStorageInventoryGridKeywords, ScoreMobileInventoryGridCandidate);
                GComponent selected = SelectMobileChatCandidate<GComponent>(candidates, minScore: 60);
                if (selected != null && !selected._disposed)
                {
                    inventoryGridRoot = selected;
                    inventoryGridResolveInfo = DescribeObject(window, selected) + " (auto)";
                }
            }

            GComponent storageGridRoot = window;
            string storageGridResolveInfo = DescribeObject(window, window);
            string[] storageOverrideKeywords = null;

            if (string.IsNullOrWhiteSpace(storageOverrideSpec))
            {
                try
                {
                    GObject exactStorage = window.GetChild("StorageGrid") ?? TryFindChildByNameRecursive(window, "StorageGrid") ??
                                           window.GetChild("DWHousetemList") ?? TryFindChildByNameRecursive(window, "DWHousetemList") ??
                                           window.GetChild("DWHouseItemList") ?? TryFindChildByNameRecursive(window, "DWHouseItemList") ??
                                           window.GetChild("DWHouseGrid") ?? TryFindChildByNameRecursive(window, "DWHouseGrid") ??
                                           window.GetChild("StorageList") ?? TryFindChildByNameRecursive(window, "StorageList");
                    if (exactStorage is GComponent exactStorageComponent && !exactStorageComponent._disposed)
                    {
                        storageGridRoot = exactStorageComponent;
                        storageGridResolveInfo = DescribeObject(window, exactStorageComponent) + " (name)";
                    }
                }
                catch
                {
                }
            }

            if (!string.IsNullOrWhiteSpace(storageOverrideSpec))
            {
                if (TryResolveMobileMainHudObjectBySpec(window, storageOverrideSpec, out GObject resolved, out string[] keywords))
                {
                    if (resolved is GComponent resolvedComponent && !resolvedComponent._disposed)
                    {
                        storageGridRoot = resolvedComponent;
                        storageGridResolveInfo = DescribeObject(window, resolvedComponent) + " (override)";
                    }
                    else if (keywords != null && keywords.Length > 0)
                    {
                        storageOverrideKeywords = keywords;
                    }
                }
                else
                {
                    storageOverrideKeywords = SplitKeywords(storageOverrideSpec);
                }
            }

            string[] effectiveStorageKeywords = storageOverrideKeywords != null && storageOverrideKeywords.Length > 0
                ? storageOverrideKeywords
                : DefaultStorageStorageGridKeywords;

            List<(int Score, GObject Target)> storageCandidates = CollectMobileChatCandidates(window, obj => obj is GComponent, effectiveStorageKeywords, ScoreMobileInventoryGridCandidate);
            for (int i = 0; i < storageCandidates.Count; i++)
            {
                (int score, GObject target) = storageCandidates[i];
                if (score < 60)
                    break;
                if (target is not GComponent selected || selected._disposed)
                    continue;
                if (ReferenceEquals(selected, inventoryGridRoot))
                    continue;

                storageGridRoot = selected;
                storageGridResolveInfo = DescribeObject(window, selected) + (storageOverrideKeywords != null && storageOverrideKeywords.Length > 0 ? " (keywords)" : " (auto)");
                break;
            }

            if (ReferenceEquals(storageGridRoot, inventoryGridRoot))
            {
                for (int i = 0; i < storageCandidates.Count; i++)
                {
                    (int score, GObject target) = storageCandidates[i];
                    if (target is not GComponent selected || selected._disposed)
                        continue;
                    if (ReferenceEquals(selected, inventoryGridRoot))
                        continue;

                    storageGridRoot = selected;
                    storageGridResolveInfo = DescribeObject(window, selected) + " (fallback)";
                    break;
                }
            }

            List<GComponent> inventorySlotCandidates = null;
            List<GComponent> storageSlotCandidates = null;

            if (!inventoryBound)
            {
                MobileItemGridBinding gridBinding = binding.InventoryGrid;
                if (gridBinding != null)
                    DetachMobileItemGridSlotCallbacks(gridBinding);

                gridBinding = new MobileItemGridBinding
                {
                    WindowKey = windowKey + ".Inventory",
                    Window = window,
                    GridRoot = inventoryGridRoot,
                    ResolveInfo = resolveInfo,
                    GridResolveInfo = inventoryGridResolveInfo,
                    OverrideSpec = inventoryOverrideSpec,
                    OverrideKeywords = inventoryOverrideKeywords,
                };

                inventorySlotCandidates = CollectInventorySlotCandidates(inventoryGridRoot);
                if (inventorySlotCandidates.Count == 0)
                {
                    CMain.SaveError("FairyGUI: 仓库窗口未找到背包格子（InventoryGrid）。可在 Mir2Config.ini 设置 [" + FairyGuiConfigSectionName + "] " +
                                    MobileStorageInventoryGridConfigKey + "=idx:... 指定格子根节点（或 path:/name:/item:/url:/title: / 关键字列表 a|b|c）。");
                }
                else
                {
                    SortGComponentsByGlobalPosition(inventorySlotCandidates);
                    int slotCount = Math.Min(desiredInventorySlots, inventorySlotCandidates.Count);
                    gridBinding.Slots.Clear();

                    int beltIdx = 0;
                    try { beltIdx = GameScene.User?.BeltIdx ?? 0; } catch { beltIdx = 0; }
                    beltIdx = Math.Clamp(beltIdx, 0, desiredInventorySlots);

                    // publish 的仓库窗口里，“背包格子”可能不包含腰带栏（Inventory[0..BeltIdx)），需做一次偏移映射。
                    int inventorySlotIndexOffset = 0;
                    if (beltIdx > 0 && slotCount == Math.Max(0, desiredInventorySlots - beltIdx))
                        inventorySlotIndexOffset = beltIdx;

                    for (int i = 0; i < slotCount; i++)
                    {
                        GComponent slotRoot = inventorySlotCandidates[i];
                        int slotIndex = i + inventorySlotIndexOffset;
                        var slot = new MobileItemSlotBinding
                        {
                            SlotIndex = slotIndex,
                            Root = slotRoot,
                            Icon = FindBestInventorySlotIcon(slotRoot),
                            IconImage = FindBestInventorySlotIconImage(slotRoot),
                            Count = FindBestInventorySlotCount(slotRoot),
                            LockedMarker = FindBestInventorySlotLockedMarker(slotRoot),
                            HasItem = false,
                            LastIcon = 0,
                            LastCountDisplayed = 0,
                        };

                        // 兜底：某些 publish 包格子内的 icon 结构不稳定/默认隐藏，使用统一的 AutoItemIcon 作为渲染目标
                        try
                        {
                            GLoader originalLoader = slot.Icon;
                            GImage originalImage = slot.IconImage;

                            GLoader autoIcon = null;
                            try { autoIcon = slotRoot.GetChild("AutoItemIcon") as GLoader; } catch { autoIcon = null; }

                            if (autoIcon == null || autoIcon._disposed)
                            {
                                autoIcon = new GLoader
                                {
                                    name = "AutoItemIcon",
                                    touchable = false,
                                    visible = true,
                                    showErrorSign = false,
                                    align = AlignType.Center,
                                    verticalAlign = VertAlignType.Middle,
                                    fill = FillType.Scale,
                                };

                                try
                                {
                                    autoIcon.SetPosition(0f, 0f);
                                    autoIcon.SetSize(Math.Max(10f, slotRoot.width), Math.Max(10f, slotRoot.height));
                                }
                                catch
                                {
                                }

                                try { autoIcon.AddRelation(slotRoot, RelationType.Size); } catch { }
                                try { slotRoot.AddChild(autoIcon); } catch { }
                            }

                            slot.Icon = autoIcon;
                            slot.IconImage = null;

                            try
                            {
                                if (originalLoader != null && !originalLoader._disposed && !ReferenceEquals(originalLoader, autoIcon))
                                {
                                    string n = originalLoader.name ?? originalLoader.packageItem?.name ?? string.Empty;
                                    if (n.IndexOf("icon", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                        n.IndexOf("item", StringComparison.OrdinalIgnoreCase) >= 0)
                                    {
                                        originalLoader.visible = false;
                                    }
                                }
                            }
                            catch
                            {
                            }

                            try
                            {
                                if (originalImage != null && !originalImage._disposed)
                                {
                                    string n = originalImage.name ?? originalImage.packageItem?.name ?? string.Empty;
                                    if (n.IndexOf("icon", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                        n.IndexOf("item", StringComparison.OrdinalIgnoreCase) >= 0)
                                    {
                                        originalImage.visible = false;
                                    }
                                }
                            }
                            catch
                            {
                            }
                        }
                        catch
                        {
                        }

                        try
                        {
                            if (slotRoot != null && !slotRoot._disposed)
                            {
                                try { DisableMobileDescendantTouch(slotRoot); } catch { }
                                try { EnsureMobileInteractiveChain(slotRoot, window); } catch { }

                                slot.OriginalAlpha = slotRoot.alpha;
                                slot.OriginalAlphaCaptured = true;

                                EventCallback0 callback = () => OnMobileStorageSlotClicked(MirGridType.Inventory, slotIndex);
                                slot.ClickCallback = callback;
                                slotRoot.onClick.Add(callback);

                                try
                                {
                                    slot.LongPressDragBinding = BindMobileLongPressItemDrag(
                                        slotRoot,
                                        resolveItem: () =>
                                        {
                                            try
                                            {
                                                UserItem[] inventory = GameScene.User?.Inventory;
                                                int idx = slot.SlotIndex;
                                                if (inventory != null && idx >= 0 && idx < inventory.Length)
                                                    return inventory[idx];
                                            }
                                            catch
                                            {
                                            }

                                            return null;
                                        },
                                        resolvePayload: () =>
                                        {
                                            UserItem t = null;
                                            try
                                            {
                                                UserItem[] inventory = GameScene.User?.Inventory;
                                                int idx = slot.SlotIndex;
                                                if (inventory != null && idx >= 0 && idx < inventory.Length)
                                                    t = inventory[idx];
                                            }
                                            catch
                                            {
                                                t = null;
                                            }

                                            if (t == null || t.Info == null)
                                                return null;

                                            return new MobileItemDragPayload
                                            {
                                                Grid = MirGridType.Inventory,
                                                SlotIndex = slot.SlotIndex,
                                                UniqueId = t.UniqueID,
                                            };
                                        });
                                }
                                catch
                                {
                                    slot.LongPressDragBinding = null;
                                }

                                try
                                {
                                    slot.DropCallback = context => OnMobileItemDroppedOnInventorySlot(slot.SlotIndex, context);
                                    slotRoot.AddEventListener("onDrop", slot.DropCallback);
                                }
                                catch
                                {
                                    slot.DropCallback = null;
                                }
                            }
                        }
                        catch
                        {
                        }

                        gridBinding.Slots.Add(slot);
                    }
                }

                binding.InventoryGrid = gridBinding;
            }

            if (!storageBound)
            {
                MobileItemGridBinding gridBinding = binding.StorageGrid;
                if (gridBinding != null)
                    DetachMobileItemGridSlotCallbacks(gridBinding);

                gridBinding = new MobileItemGridBinding
                {
                    WindowKey = windowKey + ".Storage",
                    Window = window,
                    GridRoot = storageGridRoot,
                    ResolveInfo = resolveInfo,
                    GridResolveInfo = storageGridResolveInfo,
                    OverrideSpec = storageOverrideSpec,
                    OverrideKeywords = storageOverrideKeywords,
                };

                storageSlotCandidates = CollectInventorySlotCandidates(storageGridRoot);
                // 若 storageGridRoot 是 GList，确保 numItems 足够（部分 publish 包默认只创建一页格子，导致缺格/渲染不对）
                try
                {
                    if (desiredStorageSlots > 0 && storageGridRoot is GList storageList && storageList != null && !storageList._disposed)
                    {
                        bool adjusted = false;
                        try
                        {
                            if (storageList.numItems != desiredStorageSlots)
                            {
                                storageList.numItems = desiredStorageSlots;
                                adjusted = true;
                            }
                        }
                        catch
                        {
                            adjusted = false;
                        }

                        if (adjusted)
                        {
                            try { storageSlotCandidates = CollectInventorySlotCandidates(storageGridRoot); } catch { }
                        }
                    }
                }
                catch
                {
                }
                if (storageSlotCandidates.Count == 0)
                {
                    CMain.SaveError("FairyGUI: 仓库窗口未找到仓库格子（StorageGrid）。可在 Mir2Config.ini 设置 [" + FairyGuiConfigSectionName + "] " +
                                    MobileStorageStorageGridConfigKey + "=idx:... 指定格子根节点（或 path:/name:/item:/url:/title: / 关键字列表 a|b|c）。");
                }
                else
                {
                    SortGComponentsByGlobalPosition(storageSlotCandidates);
                    int slotCount = Math.Min(desiredStorageSlots, storageSlotCandidates.Count);
                    gridBinding.Slots.Clear();

                    for (int i = 0; i < slotCount; i++)
                    {
                        GComponent slotRoot = storageSlotCandidates[i];
                        int slotIndex = i;
                        var slot = new MobileItemSlotBinding
                        {
                            SlotIndex = slotIndex,
                            Root = slotRoot,
                            Icon = FindBestInventorySlotIcon(slotRoot),
                            IconImage = FindBestInventorySlotIconImage(slotRoot),
                            Count = FindBestInventorySlotCount(slotRoot),
                            LockedMarker = FindBestInventorySlotLockedMarker(slotRoot),
                            HasItem = false,
                            LastIcon = 0,
                            LastCountDisplayed = 0,
                        };

                        // 兜底：某些 publish 包格子内的 icon 结构不稳定/默认隐藏，使用统一的 AutoItemIcon 作为渲染目标
                        try
                        {
                            GLoader originalLoader = slot.Icon;
                            GImage originalImage = slot.IconImage;

                            GLoader autoIcon = null;
                            try { autoIcon = slotRoot.GetChild("AutoItemIcon") as GLoader; } catch { autoIcon = null; }

                            if (autoIcon == null || autoIcon._disposed)
                            {
                                autoIcon = new GLoader
                                {
                                    name = "AutoItemIcon",
                                    touchable = false,
                                    visible = true,
                                    showErrorSign = false,
                                    align = AlignType.Center,
                                    verticalAlign = VertAlignType.Middle,
                                    fill = FillType.Scale,
                                };

                                try
                                {
                                    autoIcon.SetPosition(0f, 0f);
                                    autoIcon.SetSize(Math.Max(10f, slotRoot.width), Math.Max(10f, slotRoot.height));
                                }
                                catch
                                {
                                }

                                try { autoIcon.AddRelation(slotRoot, RelationType.Size); } catch { }
                                try { slotRoot.AddChild(autoIcon); } catch { }
                            }

                            slot.Icon = autoIcon;
                            slot.IconImage = null;

                            try
                            {
                                if (originalLoader != null && !originalLoader._disposed && !ReferenceEquals(originalLoader, autoIcon))
                                {
                                    string n = originalLoader.name ?? originalLoader.packageItem?.name ?? string.Empty;
                                    if (n.IndexOf("icon", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                        n.IndexOf("item", StringComparison.OrdinalIgnoreCase) >= 0)
                                    {
                                        originalLoader.visible = false;
                                    }
                                }
                            }
                            catch
                            {
                            }

                            try
                            {
                                if (originalImage != null && !originalImage._disposed)
                                {
                                    string n = originalImage.name ?? originalImage.packageItem?.name ?? string.Empty;
                                    if (n.IndexOf("icon", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                        n.IndexOf("item", StringComparison.OrdinalIgnoreCase) >= 0)
                                    {
                                        originalImage.visible = false;
                                    }
                                }
                            }
                            catch
                            {
                            }
                        }
                        catch
                        {
                        }

                        try
                        {
                            if (slotRoot != null && !slotRoot._disposed)
                            {
                                try { DisableMobileDescendantTouch(slotRoot); } catch { }
                                try { EnsureMobileInteractiveChain(slotRoot, window); } catch { }

                                slot.OriginalAlpha = slotRoot.alpha;
                                slot.OriginalAlphaCaptured = true;

                                EventCallback0 callback = () => OnMobileStorageSlotClicked(MirGridType.Storage, slotIndex);
                                slot.ClickCallback = callback;
                                slotRoot.onClick.Add(callback);

                                try
                                {
                                    slot.LongPressDragBinding = BindMobileLongPressItemDrag(
                                        slotRoot,
                                        resolveItem: () =>
                                        {
                                            try
                                            {
                                                UserItem[] storage = GameScene.Storage;
                                                int idx = slot.SlotIndex;
                                                if (storage != null && idx >= 0 && idx < storage.Length)
                                                    return storage[idx];
                                            }
                                            catch
                                            {
                                            }

                                            return null;
                                        },
                                        resolvePayload: () =>
                                        {
                                            UserItem t = null;
                                            try
                                            {
                                                UserItem[] storage = GameScene.Storage;
                                                int idx = slot.SlotIndex;
                                                if (storage != null && idx >= 0 && idx < storage.Length)
                                                    t = storage[idx];
                                            }
                                            catch
                                            {
                                                t = null;
                                            }

                                            if (t == null || t.Info == null)
                                                return null;

                                            return new MobileItemDragPayload
                                            {
                                                Grid = MirGridType.Storage,
                                                SlotIndex = slot.SlotIndex,
                                                UniqueId = t.UniqueID,
                                            };
                                        });
                                }
                                catch
                                {
                                    slot.LongPressDragBinding = null;
                                }

                                try
                                {
                                    slot.DropCallback = context => OnMobileItemDroppedOnStorageSlot(slot.SlotIndex, context);
                                    slotRoot.AddEventListener("onDrop", slot.DropCallback);
                                }
                                catch
                                {
                                    slot.DropCallback = null;
                                }
                            }
                        }
                        catch
                        {
                        }

                        gridBinding.Slots.Add(slot);
                    }
                }

                binding.StorageGrid = gridBinding;
            }

            if (binding.InventoryGrid != null && binding.InventoryGrid.Slots.Count > 0 && binding.StorageGrid != null && binding.StorageGrid.Slots.Count > 0)
            {
                TryDumpMobileStorageBindingsReportIfDue(binding, desiredInventorySlots, desiredStorageSlots, inventorySlotCandidates, storageSlotCandidates);
                CMain.SaveLog($"FairyGUI: 仓库窗口绑定完成：InvSlots={binding.InventoryGrid.Slots.Count} StorageSlots={binding.StorageGrid.Slots.Count}");
            }
        }

        private static void TryDumpMobileStorageBindingsReportIfDue(MobileStorageWindowBinding binding, int desiredInventorySlots, int desiredStorageSlots, List<GComponent> inventorySlotCandidates, List<GComponent> storageSlotCandidates)
        {
            if (!Settings.DebugMode || _mobileStorageBindingsDumped)
                return;

            if (binding == null || binding.Window == null || binding.Window._disposed)
                return;

            try
            {
                Directory.CreateDirectory(ClientResourceLayout.RuntimeRoot);

                string path = Path.Combine(ClientResourceLayout.RuntimeRoot, "FairyGui-MobileStorageBindings.txt");

                var builder = new StringBuilder(14 * 1024);
                builder.AppendLine("FairyGUI 仓库窗口绑定报告（Storage）");
                builder.AppendLine($"GeneratedAtUtc={DateTime.UtcNow:o}");
                builder.AppendLine($"WindowResolveInfo={binding.ResolveInfo ?? "-"}");
                builder.AppendLine($"InventoryGridRoot={DescribeObject(binding.Window, binding.InventoryGrid?.GridRoot)}");
                builder.AppendLine($"InventoryGridResolveInfo={binding.InventoryGrid?.GridResolveInfo ?? "-"}");
                builder.AppendLine($"InventoryOverrideSpec={binding.InventoryGrid?.OverrideSpec ?? "-"}");
                builder.AppendLine($"InventoryOverrideKeywords={(binding.InventoryGrid?.OverrideKeywords == null ? "-" : string.Join("|", binding.InventoryGrid.OverrideKeywords))}");
                builder.AppendLine($"StorageGridRoot={DescribeObject(binding.Window, binding.StorageGrid?.GridRoot)}");
                builder.AppendLine($"StorageGridResolveInfo={binding.StorageGrid?.GridResolveInfo ?? "-"}");
                builder.AppendLine($"StorageOverrideSpec={binding.StorageGrid?.OverrideSpec ?? "-"}");
                builder.AppendLine($"StorageOverrideKeywords={(binding.StorageGrid?.OverrideKeywords == null ? "-" : string.Join("|", binding.StorageGrid.OverrideKeywords))}");
                builder.AppendLine($"DesiredInventorySlots={desiredInventorySlots}");
                builder.AppendLine($"DesiredStorageSlots={desiredStorageSlots}");
                builder.AppendLine($"InventorySlotCandidates={inventorySlotCandidates?.Count ?? 0}");
                builder.AppendLine($"StorageSlotCandidates={storageSlotCandidates?.Count ?? 0}");
                builder.AppendLine($"InventorySlotsBound={binding.InventoryGrid?.Slots.Count ?? 0}");
                builder.AppendLine($"StorageSlotsBound={binding.StorageGrid?.Slots.Count ?? 0}");
                builder.AppendLine();

                void AppendSlotPreview(string title, MobileItemGridBinding grid)
                {
                    builder.AppendLine(title + ":");
                    if (grid == null || grid.Slots.Count == 0)
                    {
                        builder.AppendLine("  (none)");
                        builder.AppendLine();
                        return;
                    }

                    int top = Math.Min(grid.Slots.Count, 16);
                    for (int i = 0; i < top; i++)
                    {
                        MobileItemSlotBinding slot = grid.Slots[i];
                        builder.Append("  Slot[").Append(i).Append("] root=").Append(DescribeObject(binding.Window, slot.Root));
                        builder.Append(" icon=").Append(DescribeObject(binding.Window, slot.Icon));
                        builder.Append(" count=").AppendLine(DescribeObject(binding.Window, slot.Count));
                    }

                    if (grid.Slots.Count > top)
                        builder.AppendLine($"  ... ({grid.Slots.Count - top} more)");

                    builder.AppendLine();
                }

                AppendSlotPreview("InventoryGrid.Slots(preview)", binding.InventoryGrid);
                AppendSlotPreview("StorageGrid.Slots(preview)", binding.StorageGrid);

                builder.AppendLine("配置覆盖（可选）：Mir2Config.ini -> [FairyGUI]");
                builder.AppendLine($"  {MobileStorageInventoryGridConfigKey}=idx:...  或 path:... 或 name:/item:/url:/title:...");
                builder.AppendLine($"  {MobileStorageStorageGridConfigKey}=idx:...  或 path:... 或 name:/item:/url:/title:...");
                builder.AppendLine("说明：idx/path 均相对仓库窗口 Root。建议先查看窗口树（FairyGui-MobileWindow-Storage-Tree.txt），再填入精确覆盖。");

                File.WriteAllText(path, builder.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                _mobileStorageBindingsDumped = true;
                CMain.SaveLog($"FairyGUI: 已导出仓库窗口绑定报告到 {path}");
            }
            catch (Exception ex)
            {
                CMain.SaveError("FairyGUI: 导出仓库窗口绑定报告失败：" + ex.Message);
            }
        }

        private static void TryRefreshMobileStorageIfDue(bool force)
        {
            if (_stage == null || !_initialized || !_packagesLoaded)
                return;

            if (!MobileWindows.TryGetValue("Storage", out GComponent window) || window == null || window._disposed)
            {
                if (_mobileStorageBinding != null)
                    ResetMobileStorageBindings();
                return;
            }

            if (!window.visible)
                return;

            TryBindMobileStorageWindowIfDue("Storage", window, resolveInfo: null);

            MobileStorageWindowBinding storageBinding = _mobileStorageBinding;
            if (storageBinding == null || storageBinding.Window == null || storageBinding.Window._disposed)
            {
                ResetMobileStorageBindings();
                return;
            }

            if (storageBinding.StorageGrid == null || storageBinding.StorageGrid.Slots.Count == 0)
                return;

            var user = GameScene.User;
            if (user == null)
                return;

            UserItem[] inventory = user.Inventory ?? Array.Empty<UserItem>();
            UserItem[] storage = GameScene.Storage;
            if (storage == null)
                return;

            if (force)
                storageBinding.SelectedIndex = -1;

            if (storageBinding.SelectedIndex >= 0)
            {
                if (storageBinding.SelectedGrid == MirGridType.Inventory)
                {
                    if (storageBinding.InventoryGrid == null || storageBinding.InventoryGrid.Slots.Count == 0 ||
                        storageBinding.SelectedIndex >= inventory.Length || inventory[storageBinding.SelectedIndex] == null)
                        storageBinding.SelectedIndex = -1;
                }
                else if (storageBinding.SelectedGrid == MirGridType.Storage)
                {
                    if (storageBinding.SelectedIndex >= storage.Length || storage[storageBinding.SelectedIndex] == null)
                        storageBinding.SelectedIndex = -1;
                }
            }

            if (storageBinding.InventoryGrid != null && storageBinding.InventoryGrid.Slots.Count > 0)
            {
                RefreshMobileItemGridSlots(storageBinding.InventoryGrid, inventory, out bool inventoryInvalidated);
                if (inventoryInvalidated)
                {
                    _nextMobileStorageBindAttemptUtc = DateTime.MinValue;
                    return;
                }
            }

            RefreshMobileItemGridSlots(storageBinding.StorageGrid, storage, out bool storageInvalidated);
            if (storageInvalidated)
            {
                _nextMobileStorageBindAttemptUtc = DateTime.MinValue;
                return;
            }

            ApplyMobileStorageSelectionVisuals(storageBinding);
        }

        private static void RefreshMobileItemGridSlots(MobileItemGridBinding binding, UserItem[] items, out bool invalidated)
        {
            invalidated = false;

            if (binding == null || items == null)
                return;

            int totalSlots = binding.Slots.Count;
            for (int i = 0; i < totalSlots; i++)
            {
                MobileItemSlotBinding slot = binding.Slots[i];
                if (slot == null || slot.Root == null || slot.Root._disposed)
                {
                    binding.Slots.Clear();
                    invalidated = true;
                    return;
                }

                int itemIndex = slot.SlotIndex;
                UserItem item = itemIndex >= 0 && itemIndex < items.Length ? items[itemIndex] : null;
                if (item == null)
                {
                    if (slot.HasItem)
                        ClearInventorySlot(slot);
                    continue;
                }

                if (item.Info == null)
                {
                    try { GameScene.Bind(item); } catch { }
                }

                ushort iconIndex = item.Image;
                ushort countToShow = item.Count > 1 ? item.Count : (ushort)0;

                if (iconIndex != 0)
                    Libraries.Items.Touch(iconIndex);

                // 某些格子里 icon Loader/Image 默认是隐藏的，仅设置 texture 不会显示
                try
                {
                    if (slot.Icon != null && !slot.Icon._disposed)
                    {
                        slot.Icon.visible = true;
                    }
                    else if (slot.IconImage != null && !slot.IconImage._disposed)
                    {
                        slot.IconImage.visible = true;
                    }
                }
                catch
                {
                }

                bool needsIconRefresh = !slot.HasItem || slot.LastIcon != iconIndex;
                if (!needsIconRefresh)
                {
                    bool textureOk = false;

                    if (slot.Icon != null && !slot.Icon._disposed)
                    {
                        try
                        {
                            NTexture current = slot.Icon.texture;
                            Texture2D native = current?.nativeTexture;
                            textureOk = current != null && native != null && !native.IsDisposed;
                        }
                        catch
                        {
                            textureOk = false;
                        }
                    }
                    else if (slot.IconImage != null && !slot.IconImage._disposed)
                    {
                        try
                        {
                            NTexture current = slot.IconImage.texture;
                            Texture2D native = current?.nativeTexture;
                            textureOk = current != null && native != null && !native.IsDisposed;
                        }
                        catch
                        {
                            textureOk = false;
                        }
                    }

                    if (!textureOk)
                        needsIconRefresh = true;
                }

                if (needsIconRefresh)
                {
                    NTexture texture = GetOrCreateItemIconTexture(iconIndex);

                    if (slot.Icon != null && !slot.Icon._disposed)
                    {
                        slot.Icon.showErrorSign = false;
                        slot.Icon.url = string.Empty;
                        slot.Icon.texture = texture;
                    }
                    else if (slot.IconImage != null && !slot.IconImage._disposed)
                    {
                        slot.IconImage.texture = texture;
                    }

                    slot.LastIcon = iconIndex;
                }

                if (!slot.HasItem || slot.LastCountDisplayed != countToShow)
                {
                    if (slot.Count != null && !slot.Count._disposed)
                        slot.Count.text = countToShow > 0 ? countToShow.ToString() : string.Empty;

                    slot.LastCountDisplayed = countToShow;
                }

                ApplyMobileItemSlotLockVisual(slot, IsMobileItemLocked(item.UniqueID));
                slot.HasItem = true;
            }
        }

        private static void ApplyMobileStorageSelectionVisuals(MobileStorageWindowBinding binding)
        {
            if (binding == null)
                return;

            bool hasSelection = binding.SelectedIndex >= 0;
            int selectedIndex = binding.SelectedIndex;
            MirGridType selectedGrid = binding.SelectedGrid;

            ApplyMobileStorageSelectionVisualsToGrid(binding.InventoryGrid, hasSelection, selectedGrid == MirGridType.Inventory ? selectedIndex : -1);
            ApplyMobileStorageSelectionVisualsToGrid(binding.StorageGrid, hasSelection, selectedGrid == MirGridType.Storage ? selectedIndex : -1);
        }

        private static void ApplyMobileStorageSelectionVisualsToGrid(MobileItemGridBinding grid, bool hasSelection, int selectedIndex)
        {
            if (grid == null)
                return;

            for (int i = 0; i < grid.Slots.Count; i++)
            {
                MobileItemSlotBinding slot = grid.Slots[i];
                if (slot == null || slot.Root == null || slot.Root._disposed)
                    continue;

                try
                {
                    if (!slot.OriginalAlphaCaptured)
                    {
                        slot.OriginalAlpha = slot.Root.alpha;
                        slot.OriginalAlphaCaptured = true;
                    }

                    if (!hasSelection)
                    {
                        slot.Root.alpha = slot.OriginalAlpha;
                        continue;
                    }

                    bool isSelected = slot.SlotIndex == selectedIndex;
                    slot.Root.alpha = isSelected ? slot.OriginalAlpha : Math.Max(0.15f, slot.OriginalAlpha * 0.6f);
                }
                catch
                {
                }
            }
        }

        private static void OnMobileStorageSlotClicked(MirGridType gridType, int slotIndex)
        {
            MobileStorageWindowBinding binding = _mobileStorageBinding;
            if (binding == null)
                return;

            if (slotIndex < 0)
                return;

            var user = GameScene.User;
            if (user == null || user.Inventory == null)
                return;

            UserItem[] inventory = user.Inventory;
            UserItem[] storage = GameScene.Storage;
            if (storage == null)
                return;

            bool clickedHasItem = false;
            ulong clickedUniqueId = 0;
            if (gridType == MirGridType.Inventory)
            {
                if (slotIndex < inventory.Length)
                {
                    UserItem item = inventory[slotIndex];
                    clickedHasItem = item != null && item.Info != null;
                    clickedUniqueId = item?.UniqueID ?? 0;
                }
            }
            else if (gridType == MirGridType.Storage)
            {
                if (slotIndex < storage.Length)
                {
                    UserItem item = storage[slotIndex];
                    clickedHasItem = item != null && item.Info != null;
                    clickedUniqueId = item?.UniqueID ?? 0;
                }
            }
            else
            {
                return;
            }

            if (clickedUniqueId > 0 && IsMobileItemLocked(clickedUniqueId))
            {
                try { GameScene.Scene?.OutputMessage("物品已锁定，无法操作。"); } catch { }
                return;
            }

            int selectedIndex = binding.SelectedIndex;
            MirGridType selectedGrid = binding.SelectedGrid;

            if (selectedIndex < 0)
            {
                if (clickedHasItem)
                {
                    binding.SelectedGrid = gridType;
                    binding.SelectedIndex = slotIndex;
                }
                return;
            }

            if (selectedGrid == gridType)
            {
                if (selectedIndex == slotIndex)
                {
                    binding.SelectedIndex = -1;
                    return;
                }

                if (clickedHasItem)
                {
                    binding.SelectedIndex = slotIndex;
                    return;
                }

                binding.SelectedIndex = -1;
                return;
            }

            if (selectedGrid == MirGridType.Inventory && gridType == MirGridType.Storage)
            {
                if (selectedIndex >= 0 && selectedIndex < inventory.Length && inventory[selectedIndex] != null && inventory[selectedIndex].Info != null)
                {
                    if (slotIndex < storage.Length)
                    {
                        ulong selectedUniqueId = inventory[selectedIndex]?.UniqueID ?? 0;
                        if (selectedUniqueId > 0 && IsMobileItemLocked(selectedUniqueId))
                        {
                            try { GameScene.Scene?.OutputMessage("物品已锁定，无法存入仓库。"); } catch { }
                            binding.SelectedIndex = -1;
                            return;
                        }

                        TryMoveInventoryItemToStorage(inventory, selectedIndex, storage, slotIndex);
                        binding.SelectedIndex = -1;
                    }
                }

                return;
            }

            if (selectedGrid == MirGridType.Storage && gridType == MirGridType.Inventory)
            {
                if (selectedIndex >= 0 && selectedIndex < storage.Length && storage[selectedIndex] != null && storage[selectedIndex].Info != null)
                {
                    if (slotIndex < inventory.Length)
                    {
                        ulong selectedUniqueId = storage[selectedIndex]?.UniqueID ?? 0;
                        if (selectedUniqueId > 0 && IsMobileItemLocked(selectedUniqueId))
                        {
                            try { GameScene.Scene?.OutputMessage("物品已锁定，无法取回背包。"); } catch { }
                            binding.SelectedIndex = -1;
                            return;
                        }

                        int beltIdx = 0;
                        try { beltIdx = GameScene.User?.BeltIdx ?? 0; } catch { beltIdx = 0; }
                        TryMoveStorageItemToInventory(storage, selectedIndex, inventory, slotIndex, beltIdx);
                        binding.SelectedIndex = -1;
                    }
                }

                return;
            }
        }

        private static void TrySendStoreItem(int fromInventoryIndex, int toStorageIndex)
        {
            try
            {
                CMain.SaveLog($"MobileStorage: send StoreItem fromInv={fromInventoryIndex} toStorage={toStorageIndex}");
                MonoShare.MirNetwork.Network.Enqueue(new C.StoreItem { From = fromInventoryIndex, To = toStorageIndex });
            }
            catch (Exception ex)
            {
                CMain.SaveError("FairyGUI: 发送存入仓库失败：" + ex.Message);
            }
        }

        private static void TrySendTakeBackItem(int fromStorageIndex, int toInventoryIndex)
        {
            try
            {
                CMain.SaveLog($"MobileStorage: send TakeBackItem fromStorage={fromStorageIndex} toInv={toInventoryIndex}");
                MonoShare.MirNetwork.Network.Enqueue(new C.TakeBackItem { From = fromStorageIndex, To = toInventoryIndex });
            }
            catch (Exception ex)
            {
                CMain.SaveError("FairyGUI: 发送取回仓库物品失败：" + ex.Message);
            }
        }

        private static void TrySendMergeItem(MirGridType fromGrid, MirGridType toGrid, ulong fromUniqueId, ulong toUniqueId)
        {
            if (fromUniqueId == 0 || toUniqueId == 0)
                return;

            try
            {
                CMain.SaveLog($"MobileStorage: send MergeItem from={fromGrid}:{fromUniqueId} to={toGrid}:{toUniqueId}");
                MonoShare.MirNetwork.Network.Enqueue(new C.MergeItem
                {
                    GridFrom = fromGrid,
                    GridTo = toGrid,
                    IDFrom = fromUniqueId,
                    IDTo = toUniqueId,
                });
            }
            catch (Exception ex)
            {
                CMain.SaveError("FairyGUI: 发送物品叠加失败：" + ex.Message);
            }
        }

        private static bool CanMergeMobileItems(UserItem source, UserItem target)
        {
            if (source == null || target == null || source.Info == null || target.Info == null)
                return false;

            if (!ReferenceEquals(source.Info, target.Info) && source.Info.Index != target.Info.Index)
                return false;

            return target.Info.StackSize > 1 && target.Count < target.Info.StackSize;
        }

        private static int FindFirstEmptyMobileSlot(UserItem[] items, int startIndex, Func<int, bool> extraPredicate = null)
        {
            if (items == null)
                return -1;

            startIndex = Math.Clamp(startIndex, 0, items.Length);
            for (int i = startIndex; i < items.Length; i++)
            {
                if (items[i] != null)
                    continue;

                if (extraPredicate != null && !extraPredicate(i))
                    continue;

                return i;
            }

            return -1;
        }

        private static bool TryMoveInventoryItemToStorage(UserItem[] inventory, int fromInventoryIndex, UserItem[] storage, int preferredStorageIndex)
        {
            if (inventory == null || storage == null)
                return false;

            if (fromInventoryIndex < 0 || fromInventoryIndex >= inventory.Length)
                return false;

            UserItem moving = inventory[fromInventoryIndex];
            if (moving == null || moving.Info == null)
                return false;

            if (preferredStorageIndex >= 0 && preferredStorageIndex < storage.Length)
            {
                UserItem target = storage[preferredStorageIndex];
                if (target == null)
                {
                    TrySendStoreItem(fromInventoryIndex, preferredStorageIndex);
                    return true;
                }

                if (CanMergeMobileItems(moving, target))
                {
                    TrySendMergeItem(MirGridType.Inventory, MirGridType.Storage, moving.UniqueID, target.UniqueID);
                    return true;
                }
            }

            int emptyIndex = FindFirstEmptyMobileSlot(storage, 0);
            if (emptyIndex < 0)
            {
                CMain.SaveLog($"MobileStorage: no empty storage slot for inv={fromInventoryIndex} preferred={preferredStorageIndex}");
                return false;
            }

            TrySendStoreItem(fromInventoryIndex, emptyIndex);
            return true;
        }

        private static bool TryMoveStorageItemToInventory(UserItem[] storage, int fromStorageIndex, UserItem[] inventory, int preferredInventoryIndex, int beltIdx)
        {
            if (storage == null || inventory == null)
                return false;

            if (fromStorageIndex < 0 || fromStorageIndex >= storage.Length)
                return false;

            UserItem moving = storage[fromStorageIndex];
            if (moving == null || moving.Info == null)
                return false;

            if (preferredInventoryIndex >= 0 && preferredInventoryIndex < inventory.Length)
            {
                UserItem target = inventory[preferredInventoryIndex];
                if (target == null)
                {
                    TrySendTakeBackItem(fromStorageIndex, preferredInventoryIndex);
                    return true;
                }

                if (CanMergeMobileItems(moving, target))
                {
                    TrySendMergeItem(MirGridType.Storage, MirGridType.Inventory, moving.UniqueID, target.UniqueID);
                    return true;
                }
            }

            int startIndex = Math.Max(0, beltIdx);
            int emptyIndex = FindFirstEmptyMobileSlot(
                inventory,
                startIndex,
                idx => idx >= beltIdx || IsMobileBeltAllowedItem(moving));

            if (emptyIndex < 0)
            {
                CMain.SaveLog($"MobileStorage: no empty inventory slot for storage={fromStorageIndex} preferred={preferredInventoryIndex}");
                return false;
            }

            TrySendTakeBackItem(fromStorageIndex, emptyIndex);
            return true;
        }

        private static int ScoreMobileSystemSliderCandidate(GObject obj, string[] keywords)
        {
            int score = ScoreAnyField(obj, keywords, equalsWeight: 160, startsWithWeight: 80, containsWeight: 35);
            score += ScoreRect(obj, preferLower: true, areaDivisor: 900, maxAreaScore: 140);
            if (obj.packageItem?.exported == true)
                score += 10;
            return score;
        }

        private static int ScoreMobileSystemToggleCandidate(GObject obj, string[] keywords)
        {
            int score = ScoreAnyField(obj, keywords, equalsWeight: 160, startsWithWeight: 80, containsWeight: 35);
            score += ScoreRect(obj, preferLower: false, areaDivisor: 900, maxAreaScore: 120);

            if (obj is GButton button)
            {
                if (button.mode == ButtonMode.Check)
                    score += 30;
                else if (button.mode == ButtonMode.Radio)
                    score += 15;
            }

            if (obj.packageItem?.exported == true)
                score += 10;

            return score;
        }

        private static T SelectBestUnusedCandidate<T>(List<(int Score, GObject Target)> candidates, int minScore, ISet<GObject> used) where T : GObject
        {
            if (candidates == null || candidates.Count == 0)
                return null;

            for (int i = 0; i < candidates.Count; i++)
            {
                (int score, GObject target) = candidates[i];
                if (score < minScore)
                    break;

                if (target == null || target._disposed)
                    continue;

                if (used != null && used.Contains(target))
                    continue;

                if (target is T typed)
                    return typed;
            }

            return null;
        }

        private static bool TrySetMobileSystemSetting(string key, bool value)
        {
            if (string.IsNullOrWhiteSpace(key))
                return false;

            switch (key.Trim())
            {
                case "Effect":
                    Settings.Effect = value;
                    return true;
                case "LevelEffect":
                    Settings.LevelEffect = value;
                    return true;
                case "DropView":
                    Settings.DropView = value;
                    return true;
                case "NameView":
                    Settings.NameView = value;
                    return true;
                case "HPView":
                    Settings.HPView = value;
                    return true;
                case "TransparentChat":
                    Settings.TransparentChat = value;
                    return true;
                case "DisplayDamage":
                    Settings.DisplayDamage = value;
                    return true;
                case "TargetDead":
                    Settings.TargetDead = value;
                    return true;
                case "ExpandedBuffWindow":
                    Settings.ExpandedBuffWindow = value;
                    return true;
                default:
                    return false;
            }
        }

        private static bool TryGetMobileSystemSetting(string key, out bool value)
        {
            value = false;

            if (string.IsNullOrWhiteSpace(key))
                return false;

            switch (key.Trim())
            {
                case "Effect":
                    value = Settings.Effect;
                    return true;
                case "LevelEffect":
                    value = Settings.LevelEffect;
                    return true;
                case "DropView":
                    value = Settings.DropView;
                    return true;
                case "NameView":
                    value = Settings.NameView;
                    return true;
                case "HPView":
                    value = Settings.HPView;
                    return true;
                case "TransparentChat":
                    value = Settings.TransparentChat;
                    return true;
                case "DisplayDamage":
                    value = Settings.DisplayDamage;
                    return true;
                case "TargetDead":
                    value = Settings.TargetDead;
                    return true;
                case "ExpandedBuffWindow":
                    value = Settings.ExpandedBuffWindow;
                    return true;
                default:
                    return false;
            }
        }

        private static void TrySaveSettingsIfDue()
        {
            if (DateTime.UtcNow < _nextMobileSystemSaveUtc)
                return;

            _nextMobileSystemSaveUtc = DateTime.UtcNow.AddMilliseconds(350);

            try
            {
                Settings.Save();
            }
            catch
            {
            }
        }

        private static void OnMobileSystemVolumeSliderChanged(MobileSystemWindowBinding binding)
        {
            if (binding == null || binding.Syncing)
                return;

            GSlider slider = binding.VolumeSlider;
            if (slider == null || slider._disposed)
                return;

            try
            {
                double max = slider.max;
                if (max <= 0.0001)
                    max = 100;

                int value = (int)Math.Round(slider.value / max * 100d);
                value = Math.Clamp(value, 0, 100);
                Settings.Volume = (byte)value;
            }
            catch
            {
            }
        }

        private static void OnMobileSystemMusicSliderChanged(MobileSystemWindowBinding binding)
        {
            if (binding == null || binding.Syncing)
                return;

            GSlider slider = binding.MusicSlider;
            if (slider == null || slider._disposed)
                return;

            try
            {
                double max = slider.max;
                if (max <= 0.0001)
                    max = 100;

                int value = (int)Math.Round(slider.value / max * 100d);
                value = Math.Clamp(value, 0, 100);
                Settings.MusicVolume = (byte)value;
            }
            catch
            {
            }
        }

        private static void OnMobileSystemToggleChanged(MobileSystemWindowBinding binding, MobileSystemToggleBinding toggle)
        {
            if (binding == null || binding.Syncing || toggle == null)
                return;

            GButton button = toggle.Button;
            if (button == null || button._disposed)
                return;

            bool selected;
            try
            {
                selected = button.selected;
            }
            catch
            {
                return;
            }

            if (!TrySetMobileSystemSetting(toggle.Key, selected))
                return;

            TrySaveSettingsIfDue();
        }

        private static void EnsureMobileSystemToggleSpecs(MobileSystemWindowBinding binding)
        {
            if (binding == null)
                return;

            if (binding.Toggles.Count > 0)
                return;

            binding.Toggles.Add(new MobileSystemToggleBinding
            {
                Key = "Effect",
                DisplayName = "特效",
                ConfigKey = MobileSystemEffectConfigKey,
                DefaultKeywords = DefaultSystemEffectKeywords,
            });

            binding.Toggles.Add(new MobileSystemToggleBinding
            {
                Key = "LevelEffect",
                DisplayName = "等级特效",
                ConfigKey = MobileSystemLevelEffectConfigKey,
                DefaultKeywords = DefaultSystemLevelEffectKeywords,
            });

            binding.Toggles.Add(new MobileSystemToggleBinding
            {
                Key = "DropView",
                DisplayName = "掉落显示",
                ConfigKey = MobileSystemDropViewConfigKey,
                DefaultKeywords = DefaultSystemDropViewKeywords,
            });

            binding.Toggles.Add(new MobileSystemToggleBinding
            {
                Key = "NameView",
                DisplayName = "名称显示",
                ConfigKey = MobileSystemNameViewConfigKey,
                DefaultKeywords = DefaultSystemNameViewKeywords,
            });

            binding.Toggles.Add(new MobileSystemToggleBinding
            {
                Key = "HPView",
                DisplayName = "血蓝显示",
                ConfigKey = MobileSystemHpViewConfigKey,
                DefaultKeywords = DefaultSystemHpViewKeywords,
            });

            binding.Toggles.Add(new MobileSystemToggleBinding
            {
                Key = "TransparentChat",
                DisplayName = "聊天透明",
                ConfigKey = MobileSystemTransparentChatConfigKey,
                DefaultKeywords = DefaultSystemTransparentChatKeywords,
            });

            binding.Toggles.Add(new MobileSystemToggleBinding
            {
                Key = "DisplayDamage",
                DisplayName = "伤害显示",
                ConfigKey = MobileSystemDisplayDamageConfigKey,
                DefaultKeywords = DefaultSystemDisplayDamageKeywords,
            });

            binding.Toggles.Add(new MobileSystemToggleBinding
            {
                Key = "TargetDead",
                DisplayName = "目标死亡提示",
                ConfigKey = MobileSystemTargetDeadConfigKey,
                DefaultKeywords = DefaultSystemTargetDeadKeywords,
            });

            binding.Toggles.Add(new MobileSystemToggleBinding
            {
                Key = "ExpandedBuffWindow",
                DisplayName = "扩展Buff窗口",
                ConfigKey = MobileSystemExpandedBuffWindowConfigKey,
                DefaultKeywords = DefaultSystemExpandedBuffWindowKeywords,
            });
        }

        private static void ClearInventorySlot(MobileItemSlotBinding slot)
        {
            if (slot == null)
                return;

            ApplyMobileItemSlotLockVisual(slot, locked: false);

            try
            {
                if (slot.Icon != null && !slot.Icon._disposed)
                {
                    slot.Icon.showErrorSign = false;
                    slot.Icon.texture = null;
                    slot.Icon.url = string.Empty;
                }
            }
            catch
            {
            }

            try
            {
                if (slot.IconImage != null && !slot.IconImage._disposed)
                    slot.IconImage.texture = null;
            }
            catch
            {
            }

            try
            {
                if (slot.Count != null && !slot.Count._disposed)
                    slot.Count.text = string.Empty;
            }
            catch
            {
            }

            slot.HasItem = false;
            slot.IsLocked = false;
            slot.LastIcon = 0;
            slot.LastCountDisplayed = 0;
        }

        internal static void SetMobileMailItemLocked(ulong uniqueId, bool locked)
        {
            SetMobileItemLocked(uniqueId, locked, MobileItemLockFlagMail);

            if (locked)
            {
                _mobileMailLockAutoClearUtc = DateTime.UtcNow.AddSeconds(12);
                return;
            }

            if (!HasMobileItemLocks(MobileItemLockFlagMail))
                _mobileMailLockAutoClearUtc = DateTime.MinValue;
        }
        internal static void SetMobileAwakeningItemLocked(ulong uniqueId, bool locked) => SetMobileItemLocked(uniqueId, locked, MobileItemLockFlagAwakening);

        internal static void ClearMobileMailItemLocks()
        {
            ClearMobileItemLocks(MobileItemLockFlagMail);
            _mobileMailLockAutoClearUtc = DateTime.MinValue;
        }

        private static void ClearMobileItemLocks(byte flag)
        {
            lock (MobileItemLockGate)
            {
                if (MobileItemLocks.Count == 0)
                    return;

                List<ulong> keys = null;
                foreach (var kvp in MobileItemLocks)
                {
                    if ((kvp.Value & flag) == 0)
                        continue;

                    keys ??= new List<ulong>(8);
                    keys.Add(kvp.Key);
                }

                if (keys == null || keys.Count == 0)
                    return;

                for (int i = 0; i < keys.Count; i++)
                {
                    ulong key = keys[i];
                    if (!MobileItemLocks.TryGetValue(key, out byte flags))
                        continue;

                    flags = (byte)(flags & ~flag);
                    if (flags == 0)
                        MobileItemLocks.Remove(key);
                    else
                        MobileItemLocks[key] = flags;
                }
            }
        }

        private static void SetMobileItemLocked(ulong uniqueId, bool locked, byte flag)
        {
            if (uniqueId < 1)
                return;

            lock (MobileItemLockGate)
            {
                if (locked)
                {
                    if (MobileItemLocks.TryGetValue(uniqueId, out byte flags))
                        MobileItemLocks[uniqueId] = (byte)(flags | flag);
                    else
                        MobileItemLocks[uniqueId] = flag;

                    return;
                }

                if (!MobileItemLocks.TryGetValue(uniqueId, out byte existing))
                    return;

                byte updated = (byte)(existing & ~flag);
                if (updated == 0)
                    MobileItemLocks.Remove(uniqueId);
                else
                    MobileItemLocks[uniqueId] = updated;
            }
        }

        private static bool IsMobileItemLocked(ulong uniqueId)
        {
            if (uniqueId < 1)
                return false;

            lock (MobileItemLockGate)
            {
                return MobileItemLocks.ContainsKey(uniqueId);
            }
        }

        private static bool IsMobileConsumableItem(UserItem item)
        {
            if (item == null || item.Info == null)
                return false;

            try
            {
                if (item.Info.IsConsumable)
                    return true;

                return item.Info.Type == ItemType.Book
                       || item.Info.Type == ItemType.Meat;
            }
            catch
            {
                return false;
            }
        }

        private static bool IsMobileQuestItem(UserItem item)
        {
            if (item == null || item.Info == null)
                return false;

            try
            {
                return item.Info.Type == ItemType.Quest;
            }
            catch
            {
                return false;
            }
        }

        private static bool IsMobileBeltAllowedItem(UserItem item)
        {
            // 需求：
            // - 任务物品不能显示/进入腰带栏
            // - 腰带栏仅允许“真正的消耗品”（避免肉/书等被当作可放入快捷栏导致 UI 显示异常）
            if (item == null || item.Info == null)
                return false;

            if (IsMobileQuestItem(item))
                return false;

            try
            {
                // 明确排除：肉类/书籍不应进入腰带栏（仍可在包裹里双击使用）
                if (item.Info.Type == ItemType.Meat || item.Info.Type == ItemType.Book)
                    return false;

                return item.Info.IsConsumable;
            }
            catch
            {
                return false;
            }
        }

        private const int MobileItemDoubleTapThresholdMs = 800;
        private static readonly object MobileItemDoubleTapGate = new object();
        private static int _mobileInventoryLastTapSlotIndex = -1;
        private static long _mobileInventoryLastTapAtMs;
        private static int _mobileBeltLastTapSlotIndex = -1;
        private static long _mobileBeltLastTapAtMs;

        private static long GetMobileNowMs()
        {
            try
            {
                return Environment.TickCount64;
            }
            catch
            {
                try { return DateTime.UtcNow.Ticks / TimeSpan.TicksPerMillisecond; } catch { return 0; }
            }
        }

        private static bool IsMobileDoubleTapCore(ref int lastIndex, ref long lastAtMs, int index)
        {
            long now = GetMobileNowMs();
            bool isDouble = index >= 0 && index == lastIndex && now - lastAtMs <= MobileItemDoubleTapThresholdMs;

            lastIndex = index;
            lastAtMs = now;

            // 避免三连击被再次识别为双击
            if (isDouble)
            {
                lastIndex = -1;
                lastAtMs = 0;
            }

            return isDouble;
        }

        private static bool IsMobileInventorySlotDoubleTap(int slotIndex)
        {
            lock (MobileItemDoubleTapGate)
            {
                return IsMobileDoubleTapCore(ref _mobileInventoryLastTapSlotIndex, ref _mobileInventoryLastTapAtMs, slotIndex);
            }
        }

        private static bool IsMobileBeltSlotDoubleTap(int slotIndex)
        {
            lock (MobileItemDoubleTapGate)
            {
                return IsMobileDoubleTapCore(ref _mobileBeltLastTapSlotIndex, ref _mobileBeltLastTapAtMs, slotIndex);
            }
        }

        private static bool TryResolveMobileEquipmentSlot(UserItem item, out int toSlot)
        {
            toSlot = -1;

            if (item == null || item.Info == null)
                return false;

            ItemType type;
            try { type = item.Info.Type; }
            catch { return false; }

            UserItem[] equipment = null;
            try { equipment = GameScene.User?.Equipment; }
            catch { equipment = null; }

            switch (type)
            {
                case ItemType.Weapon:
                    toSlot = (int)EquipmentSlot.Weapon;
                    return true;
                case ItemType.Armour:
                    toSlot = (int)EquipmentSlot.Armour;
                    return true;
                case ItemType.Helmet:
                    toSlot = (int)EquipmentSlot.Helmet;
                    return true;
                case ItemType.Torch:
                    toSlot = (int)EquipmentSlot.Torch;
                    return true;
                case ItemType.Necklace:
                    toSlot = (int)EquipmentSlot.Necklace;
                    return true;
                case ItemType.Bracelet:
                    {
                        int right = (int)EquipmentSlot.BraceletR;
                        int left = (int)EquipmentSlot.BraceletL;

                        bool rightEmpty = false;
                        try
                        {
                            rightEmpty = equipment != null
                                         && right >= 0
                                         && right < equipment.Length
                                         && (equipment[right] == null || equipment[right].Info == null);
                        }
                        catch
                        {
                            rightEmpty = false;
                        }

                        toSlot = rightEmpty ? right : left;
                        return true;
                    }
                case ItemType.Ring:
                    {
                        int right = (int)EquipmentSlot.RingR;
                        int left = (int)EquipmentSlot.RingL;

                        bool rightEmpty = false;
                        try
                        {
                            rightEmpty = equipment != null
                                         && right >= 0
                                         && right < equipment.Length
                                         && (equipment[right] == null || equipment[right].Info == null);
                        }
                        catch
                        {
                            rightEmpty = false;
                        }

                        toSlot = rightEmpty ? right : left;
                        return true;
                    }
                case ItemType.Amulet:
                    toSlot = (int)EquipmentSlot.Amulet;
                    return true;
                case ItemType.Belt:
                    toSlot = (int)EquipmentSlot.Belt;
                    return true;
                case ItemType.Boots:
                    toSlot = (int)EquipmentSlot.Boots;
                    return true;
                case ItemType.Stone:
                    toSlot = (int)EquipmentSlot.Stone;
                    return true;
                case ItemType.Mount:
                    toSlot = (int)EquipmentSlot.Mount;
                    return true;
                default:
                    return false;
            }
        }

        private static bool TryEquipMobileInventoryItem(UserItem item)
        {
            if (item == null || item.Info == null)
                return false;

            if (!TryResolveMobileEquipmentSlot(item, out int toSlot))
                return false;

            try
            {
                if (CMain.Time < GameScene.UseItemTime)
                    return true;
                GameScene.UseItemTime = CMain.Time + 250;
            }
            catch
            {
            }

            try
            {
                MonoShare.MirNetwork.Network.Enqueue(new C.EquipItem
                {
                    Grid = MirGridType.Inventory,
                    UniqueID = item.UniqueID,
                    To = toSlot,
                });
            }
            catch (Exception ex)
            {
                CMain.SaveError("FairyGUI: 穿戴物品失败：" + ex.Message);
            }

            return true;
        }

        private static bool TryUseMobileInventoryItem(UserItem item)
        {
            if (item == null || item.Info == null)
                return false;

            if (!IsMobileConsumableItem(item))
                return false;

            try
            {
                if (CMain.Time < GameScene.UseItemTime)
                    return true;
                GameScene.UseItemTime = CMain.Time + 250;
            }
            catch
            {
            }

            try
            {
                MonoShare.MirNetwork.Network.Enqueue(new C.UseItem
                {
                    UniqueID = item.UniqueID,
                    Grid = MirGridType.Inventory,
                });
            }
            catch (Exception ex)
            {
                CMain.SaveError("FairyGUI: 使用物品失败：" + ex.Message);
            }

            return true;
        }

        private static bool HasMobileItemLocks(byte flag)
        {
            lock (MobileItemLockGate)
            {
                foreach (byte flags in MobileItemLocks.Values)
                {
                    if ((flags & flag) != 0)
                        return true;
                }
            }

            return false;
        }

        private static void TryAutoClearMobileMailItemLocksIfDue()
        {
            DateTime due = _mobileMailLockAutoClearUtc;
            if (due == DateTime.MinValue || DateTime.UtcNow < due)
                return;

            _mobileMailLockAutoClearUtc = DateTime.MinValue;

            if (!HasMobileItemLocks(MobileItemLockFlagMail))
                return;

            ClearMobileItemLocks(MobileItemLockFlagMail);
            try { GameScene.Scene?.OutputMessage("邮件物品锁定超时，已自动解锁。"); } catch { }
        }

        private static void ApplyMobileItemSlotLockVisual(MobileItemSlotBinding slot, bool locked)
        {
            if (slot == null || slot.Root == null || slot.Root._disposed)
                return;

            slot.IsLocked = locked;

            try
            {
                if (!slot.OriginalGrayedCaptured)
                {
                    slot.OriginalGrayed = slot.Root.grayed;
                    slot.OriginalGrayedCaptured = true;
                }

                slot.Root.grayed = slot.OriginalGrayed || locked;
            }
            catch
            {
            }

            try
            {
                GObject marker = slot.LockedMarker;
                if (marker != null && marker._disposed)
                {
                    marker = null;
                    slot.LockedMarker = null;
                }

                if (marker != null && !marker._disposed)
                    marker.visible = locked;
            }
            catch
            {
            }
        }

        private static NTexture GetOrCreateItemIconTexture(ushort iconIndex)
        {
            if (iconIndex == 0)
                return null;

            Texture2D texture;
            try
            {
                texture = Libraries.Items.GetTexture(iconIndex);
            }
            catch
            {
                texture = null;
            }

            if (texture == null || texture.IsDisposed)
                return null;

            int key = iconIndex;
            if (ItemIconTextureCache.TryGetValue(key, out NTexture cached) && cached != null)
            {
                Texture2D native = cached.nativeTexture;
                if (native != null && !native.IsDisposed && ReferenceEquals(native, texture))
                    return cached;
            }

            NTexture created = new NTexture(texture);
            ItemIconTextureCache[key] = created;
            return created;
        }

        private static int ScoreMobileInventoryGridCandidate(GObject obj, string[] keywords)
        {
            int score = ScoreAnyField(obj, keywords, equalsWeight: 160, startsWithWeight: 80, containsWeight: 35);
            score += ScoreRect(obj, preferLower: false, areaDivisor: 900, maxAreaScore: 240);
            if (obj is GList)
                score += 40;
            if (obj.packageItem?.exported == true)
                score += 10;
            return score;
        }

        private static bool IsExactNameOrItem(GObject obj, string expected)
        {
            if (obj == null || obj._disposed || string.IsNullOrWhiteSpace(expected))
                return false;

            return string.Equals(obj.name, expected, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(obj.packageItem?.name, expected, StringComparison.OrdinalIgnoreCase);
        }

        private static GComponent FindFirstComponentByNameOrItem(GComponent root, params string[] names)
        {
            if (root == null || root._disposed || names == null || names.Length == 0)
                return null;

            for (int i = 0; i < names.Length; i++)
            {
                string expected = names[i];
                if (string.IsNullOrWhiteSpace(expected))
                    continue;

                foreach (GObject obj in Enumerate(root))
                {
                    if (obj is not GComponent component || component._disposed)
                        continue;

                    if (IsExactNameOrItem(component, expected))
                        return component;
                }
            }

            return null;
        }

        private static GComponent FindBestKnownInventoryGridRoot(GComponent window, int desiredSlots, int beltIdx, out string resolveInfo)
        {
            resolveInfo = DescribeObject(window, window) + " (known-root:none)";

            if (window == null || window._disposed)
                return null;

            int desiredBagSlots = Math.Max(1, desiredSlots - Math.Max(0, beltIdx));
            GComponent best = null;
            int bestScore = int.MinValue;

            foreach (GObject obj in Enumerate(window))
            {
                if (obj is not GComponent component || component._disposed)
                    continue;

                int kindScore = 0;
                if (IsExactNameOrItem(component, "DBagGrid"))
                    kindScore = 4000;
                else if (IsExactNameOrItem(component, "DBagUI"))
                    kindScore = 3200;
                else if (IsExactNameOrItem(component, "GameItemGrid"))
                    kindScore = 2600;
                else if (IsExactNameOrItem(component, "DBag"))
                    kindScore = 2200;

                if (kindScore == 0)
                    continue;

                int slotCount = 0;
                try
                {
                    slotCount = CollectInventorySlotCandidates(component).Count;
                }
                catch
                {
                    slotCount = 0;
                }

                int score = kindScore;
                if (slotCount > 0)
                {
                    score += 2200 - Math.Min(1800, Math.Abs(slotCount - desiredBagSlots) * 90);
                    if (slotCount >= Math.Min(desiredBagSlots, 6))
                        score += 450;
                }

                if (component is GList)
                    score += 250;
                if (component.packageItem?.exported == true)
                    score += 100;

                score += ScoreRect(component, preferLower: false, areaDivisor: 900, maxAreaScore: 140);

                if (score > bestScore)
                {
                    best = component;
                    bestScore = score;
                }
            }

            if (best == null || best._disposed)
                return null;

            resolveInfo = DescribeObject(window, best) + " (known-root)";
            return best;
        }

        private static bool IsInventorySlotLikeComponent(GComponent component)
        {
            if (component == null || component._disposed)
                return false;

            if (IsExactNameOrItem(component, "bagItem") ||
                IsExactNameOrItem(component, "DBagItem") ||
                IsExactNameOrItem(component, "DItem") ||
                IsExactNameOrItem(component, "GameItemCell") ||
                IsExactNameOrItem(component, "ItemCell") ||
                IsExactNameOrItem(component, "GridItem") ||
                IsExactNameOrItem(component, "WHouseItem") ||
                IsExactNameOrItem(component, "DWHouseItem") ||
                IsExactNameOrItem(component, "HouseItem") ||
                IsExactNameOrItem(component, "StorageItem"))
            {
                return true;
            }

            string name = component.name ?? string.Empty;
            string itemName = component.packageItem?.name ?? string.Empty;

            for (int i = 0; i < DefaultInventorySlotComponentKeywords.Length; i++)
            {
                string keyword = DefaultInventorySlotComponentKeywords[i];
                if (string.IsNullOrWhiteSpace(keyword))
                    continue;

                if ((!string.IsNullOrWhiteSpace(name) && name.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (!string.IsNullOrWhiteSpace(itemName) && itemName.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsLikelyInventoryUtilityComponent(GComponent component)
        {
            if (component == null || component._disposed)
                return false;

            bool exactSlotName = IsInventorySlotLikeComponent(component);

            if (component is GButton && !exactSlotName)
                return true;

            string composite = (component.name ?? string.Empty) + "|" +
                               (component.packageItem?.name ?? string.Empty) + "|" +
                               (component is GButton button ? button.title ?? string.Empty : string.Empty);

            string[] utilityKeywords =
            {
                "btn", "button", "radio", "query", "storage", "warehouse", "close", "scroll", "page",
                "custom", "recovery", "money", "gold", "yuanbao", "jinpiao", "整理", "仓库", "关闭", "分页", "页签"
            };

            for (int i = 0; i < utilityKeywords.Length; i++)
            {
                string keyword = utilityKeywords[i];
                if (!string.IsNullOrWhiteSpace(keyword) &&
                    composite.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return !exactSlotName;
                }
            }

            return false;
        }

        private static int ScoreInventorySlotIconCandidate(GObject obj, string[] keywords)
        {
            int score = ScoreAnyField(obj, keywords, equalsWeight: 140, startsWithWeight: 70, containsWeight: 30);
            score += ScoreRect(obj, preferLower: false, areaDivisor: 500, maxAreaScore: 90);
            if (obj.packageItem?.exported == true)
                score += 5;
            return score;
        }

        private static int ScoreInventorySlotCountCandidate(GObject obj, string[] keywords)
        {
            int score = ScoreAnyField(obj, keywords, equalsWeight: 170, startsWithWeight: 85, containsWeight: 40);
            score += ScoreRect(obj, preferLower: true, areaDivisor: 260, maxAreaScore: 60);
            if (obj is GRichTextField)
                score += 10;
            if (obj.packageItem?.exported == true)
                score += 5;
            return score;
        }

        private static GLoader FindBestInventorySlotIcon(GComponent slotRoot)
        {
            if (slotRoot == null || slotRoot._disposed)
                return null;

            List<(int Score, GObject Target)> candidates = CollectMobileChatCandidates(slotRoot, obj => obj is GLoader, DefaultInventoryIconKeywords, ScoreInventorySlotIconCandidate);
            GLoader selected = SelectMobileChatCandidate<GLoader>(candidates, minScore: 20);
            if (selected != null && !selected._disposed)
                return selected;

            // 兜底：关键字不命中时，取 slot 内面积最大的 Loader 作为图标位
            GLoader best = null;
            float bestArea = 0F;
            foreach (GObject obj in Enumerate(slotRoot))
            {
                if (obj == null || obj._disposed)
                    continue;

                if (obj is not GLoader loader || loader._disposed)
                    continue;

                float area = Math.Max(0F, loader.width) * Math.Max(0F, loader.height);
                if (area <= bestArea)
                    continue;

                bestArea = area;
                best = loader;
            }

            return best;
        }

        private static GImage FindBestInventorySlotIconImage(GComponent slotRoot)
        {
            if (slotRoot == null || slotRoot._disposed)
                return null;

            List<(int Score, GObject Target)> candidates = CollectMobileChatCandidates(slotRoot, obj => obj is GImage, DefaultInventoryIconKeywords, ScoreInventorySlotIconCandidate);
            GImage selected = SelectMobileChatCandidate<GImage>(candidates, minScore: 20);
            if (selected != null && !selected._disposed)
                return selected;

            // 兜底：关键字不命中时，取 slot 内面积最大的 Image 作为图标位
            GImage best = null;
            float bestArea = 0F;
            foreach (GObject obj in Enumerate(slotRoot))
            {
                if (obj == null || obj._disposed)
                    continue;

                if (obj is not GImage image || image._disposed)
                    continue;

                float area = Math.Max(0F, image.width) * Math.Max(0F, image.height);
                if (area <= bestArea)
                    continue;

                bestArea = area;
                best = image;
            }

            return best;
        }

        private static GTextField FindBestInventorySlotCount(GComponent slotRoot)
        {
            if (slotRoot == null || slotRoot._disposed)
                return null;

            List<(int Score, GObject Target)> candidates = CollectMobileChatCandidates(slotRoot, obj => obj is GTextField && obj is not GTextInput, DefaultInventoryCountKeywords, ScoreInventorySlotCountCandidate);
            GTextField selected = SelectMobileChatCandidate<GTextField>(candidates, minScore: 25);
            if (selected != null && !selected._disposed)
                return selected;

            // 兜底：取 slot 内“更靠右下角”的文本作为数量位
            GTextField best = null;
            float bestScore = float.MinValue;
            foreach (GObject obj in Enumerate(slotRoot))
            {
                if (obj == null || obj._disposed)
                    continue;

                if (obj is not GTextField tf || tf._disposed || obj is GTextInput)
                    continue;

                float score = (tf.x + tf.width) + (tf.y + tf.height) * 0.6F;
                if (score <= bestScore)
                    continue;

                bestScore = score;
                best = tf;
            }

            return best;
        }

        private static GObject FindBestInventorySlotLockedMarker(GComponent slotRoot)
        {
            if (slotRoot == null || slotRoot._disposed)
                return null;

            GObject best = null;
            int bestScore = 0;

            foreach (GObject obj in Enumerate(slotRoot))
            {
                if (obj == null || obj._disposed)
                    continue;

                if (ReferenceEquals(obj, slotRoot))
                    continue;

                int score = ScoreAnyField(obj, DefaultInventoryLockedMarkerKeywords, equalsWeight: 160, startsWithWeight: 80, containsWeight: 35);
                if (score <= bestScore)
                    continue;

                bestScore = score;
                best = obj;
            }

            return bestScore >= 80 ? best : null;
        }

        private static bool IsVisibleInventorySlotCandidate(GComponent root, GComponent component, GComponent requiredParent = null)
        {
            if (root == null || root._disposed || component == null || component._disposed)
                return false;

            if (requiredParent != null && !ReferenceEquals(component.parent, requiredParent))
                return false;

            try
            {
                if (!component.visible || component.width <= 6F || component.height <= 6F || component.alpha <= 0.01F)
                    return false;

                if (!component.inContainer || !component.internalVisible || !component.internalVisible2)
                    return false;

                if (component.displayObject != null && !component.displayObject.visible)
                    return false;
            }
            catch
            {
                return false;
            }

            GObject current = component;
            for (int depth = 0; current != null && depth < 12; depth++)
            {
                try
                {
                    if (current._disposed || !current.visible || current.alpha <= 0.01F)
                        return false;

                    if (!current.inContainer || !current.internalVisible || !current.internalVisible2)
                        return false;

                    if (current.displayObject != null && !current.displayObject.visible)
                        return false;
                }
                catch
                {
                    return false;
                }

                if (ReferenceEquals(current, root))
                    break;

                current = current.parent;
            }

            // GList/Pagination 下，列表会保留被裁剪页的 child；仅凭 visible/internalVisible 不足以判断“当前页可见”。
            // 这里先要求格子与 root 的全局可视矩形有交集，再要求它在 root 本地坐标系中的矩形仍与当前视口相交，
            // 避免把另一页或视口外的格子一起当成候选。
            try
            {
                if (TryGetGlobalRect(root, out System.Drawing.RectangleF rootRect) &&
                    TryGetGlobalRect(component, out System.Drawing.RectangleF componentRect))
                {
                    const float tolerance = 2F;
                    if (componentRect.Right <= rootRect.Left + tolerance ||
                        componentRect.Left >= rootRect.Right - tolerance ||
                        componentRect.Bottom <= rootRect.Top + tolerance ||
                        componentRect.Top >= rootRect.Bottom - tolerance)
                    {
                        return false;
                    }
                }

                if (TryGetRectInRootSpace(root, component, out System.Drawing.RectangleF componentRootRect))
                {
                    const float tolerance = 2F;
                    float viewportLeft = 0F;
                    float viewportTop = 0F;
                    float viewportRight = Math.Max(root.width, 0F);
                    float viewportBottom = Math.Max(root.height, 0F);

                    if (componentRootRect.Right <= viewportLeft + tolerance ||
                        componentRootRect.Left >= viewportRight - tolerance ||
                        componentRootRect.Bottom <= viewportTop + tolerance ||
                        componentRootRect.Top >= viewportBottom - tolerance)
                    {
                        return false;
                    }
                }
            }
            catch
            {
                return false;
            }

            return true;
        }

        private static bool TryGetGlobalRect(GObject obj, out System.Drawing.RectangleF rect)
        {
            rect = default;

            if (obj == null || obj._disposed)
                return false;

            try
            {
                rect = obj.LocalToGlobal(new System.Drawing.RectangleF(0, 0, obj.width, obj.height));
                return rect.Width > 1F && rect.Height > 1F;
            }
            catch
            {
                rect = default;
                return false;
            }
        }

        private static bool TryGetRectInRootSpace(GComponent root, GObject obj, out System.Drawing.RectangleF rect)
        {
            rect = default;

            if (root == null || root._disposed || obj == null || obj._disposed)
                return false;

            try
            {
                System.Drawing.RectangleF globalRect = obj.LocalToGlobal(new System.Drawing.RectangleF(0, 0, obj.width, obj.height));
                rect = root.GlobalToLocal(globalRect);

                if (rect.Width < 0F)
                {
                    rect.X += rect.Width;
                    rect.Width = -rect.Width;
                }

                if (rect.Height < 0F)
                {
                    rect.Y += rect.Height;
                    rect.Height = -rect.Height;
                }

                return rect.Width > 1F && rect.Height > 1F;
            }
            catch
            {
                rect = default;
                return false;
            }
        }

        private static List<GComponent> CollectInventorySlotCandidates(GComponent root)
        {
            var list = new List<GComponent>(96);

            if (root == null || root._disposed)
                return list;

            if (root is GList listRoot && !listRoot._disposed)
            {
                try
                {
                    int childCount = listRoot.numChildren;
                    for (int i = 0; i < childCount; i++)
                    {
                        GObject child = listRoot.GetChildAt(i);
                        if (child == null || child._disposed)
                            continue;

                        if (child is not GComponent component || component._disposed)
                            continue;

                        if (!IsVisibleInventorySlotCandidate(root, component, listRoot))
                            continue;

                        if (IsLikelyInventoryUtilityComponent(component))
                            continue;

                        string itemName = component.packageItem?.name ?? string.Empty;
                        bool match = false;
                        if (!string.IsNullOrWhiteSpace(itemName))
                        {
                            for (int j = 0; j < DefaultInventorySlotComponentKeywords.Length; j++)
                            {
                                string keyword = DefaultInventorySlotComponentKeywords[j];
                                if (!string.IsNullOrWhiteSpace(keyword) &&
                                    itemName.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)
                                {
                                    match = true;
                                    break;
                                }
                            }
                        }

                        if (!match)
                        {
                            bool hasLoader = false;
                            foreach (GObject descendant in Enumerate(component))
                            {
                                if (descendant == null || descendant._disposed)
                                    continue;

                                if (descendant is GLoader || descendant is GImage)
                                {
                                    hasLoader = true;
                                    break;
                                }
                            }

                            if (!hasLoader)
                                continue;
                        }

                        list.Add(component);
                    }
                }
                catch
                {
                }

                if (list.Count > 0)
                    return list;
            }

            foreach (GObject obj in Enumerate(root))
            {
                if (obj == null || obj._disposed)
                    continue;

                if (ReferenceEquals(obj, root))
                    continue;

                if (obj is not GComponent component || component._disposed)
                    continue;

                if (!IsVisibleInventorySlotCandidate(root, component))
                    continue;

                if (IsLikelyInventoryUtilityComponent(component))
                    continue;

                string itemName = component.packageItem?.name ?? string.Empty;
                if (string.IsNullOrWhiteSpace(itemName))
                    continue;

                bool match = false;
                for (int i = 0; i < DefaultInventorySlotComponentKeywords.Length; i++)
                {
                    string keyword = DefaultInventorySlotComponentKeywords[i];
                    if (!string.IsNullOrWhiteSpace(keyword) && itemName.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        match = true;
                        break;
                    }
                }

                if (!match)
                    continue;

                list.Add(component);
            }

            // 兜底：publish 组件名不匹配时，按“包含 GLoader 的可点击格子组件”判定
            if (list.Count > 0)
                return list;

            foreach (GObject obj in Enumerate(root))
            {
                if (obj == null || obj._disposed)
                    continue;

                if (ReferenceEquals(obj, root))
                    continue;

                if (obj is not GComponent component || component._disposed)
                    continue;

                if (!IsVisibleInventorySlotCandidate(root, component))
                    continue;

                if (IsLikelyInventoryUtilityComponent(component))
                    continue;

                bool hasLoader = false;
                foreach (GObject child in Enumerate(component))
                {
                    if (child is GLoader || child is GImage)
                    {
                        hasLoader = true;
                        break;
                    }
                }

                if (!hasLoader)
                    continue;

                list.Add(component);
            }

            return list;
        }

        private static Vector2 GetGlobalPositionOrZero(GObject obj)
        {
            if (obj == null || obj._disposed)
                return Vector2.Zero;

            try
            {
                return obj.LocalToGlobal(Vector2.Zero);
            }
            catch
            {
                return Vector2.Zero;
            }
        }

        private static bool TrySortGComponentsBySharedParentChildIndex(List<GComponent> list)
        {
            if (list == null || list.Count <= 1)
                return false;

            GComponent sharedParent = null;
            for (int i = 0; i < list.Count; i++)
            {
                GComponent component = list[i];
                if (component == null || component._disposed)
                    continue;

                if (component.parent is not GComponent parent || parent._disposed)
                    return false;

                if (sharedParent == null)
                {
                    sharedParent = parent;
                    continue;
                }

                if (!ReferenceEquals(sharedParent, parent))
                    return false;
            }

            if (sharedParent is not GList)
                return false;

            list.Sort((a, b) =>
            {
                if (a == null || a._disposed)
                    return 1;
                if (b == null || b._disposed)
                    return -1;

                try
                {
                    int ia = sharedParent.GetChildIndex(a);
                    int ib = sharedParent.GetChildIndex(b);
                    int idx = ia.CompareTo(ib);
                    if (idx != 0)
                        return idx;
                }
                catch
                {
                }

                Vector2 pa = GetGlobalPositionOrZero(a);
                Vector2 pb = GetGlobalPositionOrZero(b);

                int y = pa.Y.CompareTo(pb.Y);
                if (y != 0)
                    return y;

                int x = pa.X.CompareTo(pb.X);
                if (x != 0)
                    return x;

                return 0;
            });

            return true;
        }

        private static void SortGComponentsByGlobalPosition(List<GComponent> list)
        {
            if (list == null || list.Count <= 1)
                return;

            if (TrySortGComponentsBySharedParentChildIndex(list))
                return;

            var positions = new Dictionary<GComponent, Vector2>(list.Count);
            var originalIndexes = new Dictionary<GComponent, int>(list.Count);
            var heights = new List<float>(list.Count);

            for (int i = 0; i < list.Count; i++)
            {
                GComponent component = list[i];
                if (component == null || component._disposed)
                    continue;

                positions[component] = GetGlobalPositionOrZero(component);
                originalIndexes[component] = i;

                try
                {
                    if (component.height > 1f)
                        heights.Add(component.height);
                }
                catch
                {
                }
            }

            float representativeHeight = 0f;
            if (heights.Count > 0)
            {
                heights.Sort();
                representativeHeight = heights[heights.Count / 2];
            }

            float rowTolerance = Math.Clamp(representativeHeight * 0.45f, 6f, 24f);

            list.Sort((a, b) =>
            {
                if (a == null || a._disposed)
                    return 1;
                if (b == null || b._disposed)
                    return -1;

                Vector2 pa = positions.TryGetValue(a, out Vector2 cachedA) ? cachedA : GetGlobalPositionOrZero(a);
                Vector2 pb = positions.TryGetValue(b, out Vector2 cachedB) ? cachedB : GetGlobalPositionOrZero(b);

                int y = pa.Y.CompareTo(pb.Y);
                if (y != 0)
                    return y;

                int x = pa.X.CompareTo(pb.X);
                if (x != 0)
                    return x;

                int ia = originalIndexes.TryGetValue(a, out int cachedIa) ? cachedIa : 0;
                int ib = originalIndexes.TryGetValue(b, out int cachedIb) ? cachedIb : 0;
                return ia.CompareTo(ib);
            });

            var rowIndexes = new Dictionary<GComponent, int>(list.Count);
            float currentRowY = float.NaN;
            int currentRow = -1;

            for (int i = 0; i < list.Count; i++)
            {
                GComponent component = list[i];
                if (component == null || component._disposed)
                    continue;

                Vector2 pos = positions.TryGetValue(component, out Vector2 cachedPos) ? cachedPos : GetGlobalPositionOrZero(component);
                if (currentRow < 0 || Math.Abs(pos.Y - currentRowY) > rowTolerance)
                {
                    currentRow++;
                    currentRowY = pos.Y;
                }
                else
                {
                    currentRowY = (currentRowY + pos.Y) * 0.5f;
                }

                rowIndexes[component] = currentRow;
            }

            list.Sort((a, b) =>
            {
                if (a == null || a._disposed)
                    return 1;
                if (b == null || b._disposed)
                    return -1;

                int rowA = rowIndexes.TryGetValue(a, out int cachedRowA) ? cachedRowA : 0;
                int rowB = rowIndexes.TryGetValue(b, out int cachedRowB) ? cachedRowB : 0;
                int row = rowA.CompareTo(rowB);
                if (row != 0)
                    return row;

                Vector2 pa = positions.TryGetValue(a, out Vector2 cachedA) ? cachedA : GetGlobalPositionOrZero(a);
                Vector2 pb = positions.TryGetValue(b, out Vector2 cachedB) ? cachedB : GetGlobalPositionOrZero(b);

                int x = pa.X.CompareTo(pb.X);
                if (x != 0)
                    return x;

                int y = pa.Y.CompareTo(pb.Y);
                if (y != 0)
                    return y;

                int ia = originalIndexes.TryGetValue(a, out int cachedIa) ? cachedIa : 0;
                int ib = originalIndexes.TryGetValue(b, out int cachedIb) ? cachedIb : 0;
                return ia.CompareTo(ib);
            });
        }

        private static void TryDumpMobileInventoryBindingsReportIfDue(MobileItemGridBinding binding, int desiredSlots, List<GComponent> slotCandidates)
        {
            if (!Settings.DebugMode || _mobileInventoryBindingsDumped)
                return;

            if (binding == null || binding.Window == null || binding.Window._disposed)
                return;

            try
            {
                Directory.CreateDirectory(ClientResourceLayout.RuntimeRoot);

                string path = Path.Combine(ClientResourceLayout.RuntimeRoot, "FairyGui-MobileInventoryBindings.txt");

                var builder = new StringBuilder(12 * 1024);
                builder.AppendLine("FairyGUI 背包窗口绑定报告（Inventory）");
                builder.AppendLine($"GeneratedAtUtc={DateTime.UtcNow:o}");
                builder.AppendLine($"WindowKey={binding.WindowKey}");
                builder.AppendLine($"WindowResolveInfo={binding.ResolveInfo ?? "-"}");
                builder.AppendLine($"GridRoot={DescribeObject(binding.Window, binding.GridRoot)}");
                builder.AppendLine($"GridResolveInfo={binding.GridResolveInfo ?? "-"}");
                builder.AppendLine($"OverrideSpec={binding.OverrideSpec ?? "-"}");
                builder.AppendLine($"OverrideKeywords={(binding.OverrideKeywords == null ? "-" : string.Join("|", binding.OverrideKeywords))}");
                builder.AppendLine($"DesiredSlots={desiredSlots}");
                builder.AppendLine($"SlotCandidates={slotCandidates?.Count ?? 0}");
                builder.AppendLine($"SlotsBound={binding.Slots.Count}");
                builder.AppendLine();

                int top = Math.Min(binding.Slots.Count, 24);
                for (int i = 0; i < top; i++)
                {
                    MobileItemSlotBinding slot = binding.Slots[i];
                    builder.Append("Slot[").Append(i).Append("] root=").Append(DescribeObject(binding.Window, slot.Root));
                    builder.Append(" icon=").Append(DescribeObject(binding.Window, slot.Icon));
                    builder.Append(" count=").AppendLine(DescribeObject(binding.Window, slot.Count));
                }

                if (binding.Slots.Count > top)
                    builder.AppendLine($"... ({binding.Slots.Count - top} more)");

                builder.AppendLine();
                builder.AppendLine("配置覆盖（可选）：Mir2Config.ini -> [FairyGUI]");
                builder.AppendLine($"  {MobileInventoryGridConfigKey}=idx:...  或 path:... 或 name:/item:/url:/title:...");
                builder.AppendLine("  或者关键字列表：a|b|c（不推荐，容易误命中；优先 idx/path）。");
                builder.AppendLine("说明：idx/path 均相对窗口 Root。建议先查看窗口树，再填入精确覆盖。");

                File.WriteAllText(path, builder.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                _mobileInventoryBindingsDumped = true;
                CMain.SaveLog($"FairyGUI: 已导出背包窗口绑定报告到 {path}");
            }
            catch (Exception ex)
            {
                CMain.SaveError("FairyGUI: 导出背包窗口绑定报告失败：" + ex.Message);
            }
        }

        private static void TryBindMobileWindowCloseButton(string windowKey, GComponent window)
        {
            if (string.IsNullOrWhiteSpace(windowKey))
                return;

            if (window == null || window._disposed)
                return;

            try
            {
                string overrideSpec = string.Empty;
                try
                {
                    InIReader reader = TryCreateConfigReader();
                    if (reader != null)
                    {
                        overrideSpec = reader.ReadString(
                            FairyGuiConfigSectionName,
                            MobileWindowCloseButtonConfigKeyPrefix + windowKey.Trim(),
                            string.Empty,
                            writeWhenNull: false);
                    }
                }
                catch
                {
                    overrideSpec = string.Empty;
                }

                overrideSpec = overrideSpec?.Trim() ?? string.Empty;

                string[] keywords = DefaultWindowCloseButtonKeywords;
                GObject closeButton = null;

                if (!string.IsNullOrWhiteSpace(overrideSpec))
                {
                    if (TryResolveMobileMainHudObjectBySpec(window, overrideSpec, out GObject resolved, out string[] overrideKeywords))
                    {
                        if (resolved != null && !resolved._disposed)
                        {
                            closeButton = resolved;
                        }
                        else if (overrideKeywords != null && overrideKeywords.Length > 0)
                        {
                            keywords = overrideKeywords;
                        }
                    }
                }

                if (closeButton == null)
                    closeButton = FindBestCloseButton(window, keywords);

                TryDumpMobileWindowCloseBindingReportIfDue(windowKey, window, overrideSpec, keywords, closeButton);

                if (closeButton == null || closeButton._disposed)
                {
                    // 关闭按钮没找到：大地图窗口仍然补一个“右上角点击热区”，避免用户卡住无法关闭。
                    TryEnsureMobileWindowCloseHitArea(windowKey, window, closeButton: null);
                    return;
                }

                try
                {
                    // 一些 publish 的红叉/关闭图标可能被设置为 enabled=false 或 touchable=false，导致点击无效。
                    // 这里强制启用（enabled 会同时设置 grayed/touchable）。
                    closeButton.enabled = true;
                }
                catch
                {
                }

                closeButton.onClick.Add(() =>
                {
                    try
                    {
                        TryHideMobileWindow(windowKey);
                    }
                    catch
                    {
                    }
                });

                // 兜底：部分 publish 的红叉图片虽然可见，但点击事件容易被遮罩/透明层吞掉。
                // 这里额外叠加一个透明点击热区，覆盖在关闭按钮上方，保证可关闭（尤其是大地图）。
                TryEnsureMobileWindowCloseHitArea(windowKey, window, closeButton);

                if (Settings.DebugMode)
                    CMain.SaveLog($"FairyGUI: 窗口({windowKey}) 已绑定关闭按钮 name={closeButton.name ?? "(null)"} item={closeButton.packageItem?.name ?? "(null)"}");
            }
            catch (Exception ex)
            {
                CMain.SaveError("FairyGUI: 绑定窗口关闭按钮失败（" + windowKey + "）：" + ex.Message);
            }
        }

        private const string MobileWindowCloseHitAreaNamePrefix = "__codex_mobile_close_hit_";

        private static void TryEnsureMobileWindowCloseHitArea(string windowKey, GComponent window, GObject closeButton)
        {
            if (string.IsNullOrWhiteSpace(windowKey))
                return;

            if (window == null || window._disposed)
                return;

            // 仅在找不到关闭按钮时，对大地图做默认热区；其他窗口避免误伤（没有明确关闭按钮时不乱盖）。
            bool allowDefault = string.Equals(windowKey, "BigMap", StringComparison.OrdinalIgnoreCase)
                                || string.Equals(windowKey, "Npc", StringComparison.OrdinalIgnoreCase);

            string hitName = MobileWindowCloseHitAreaNamePrefix + windowKey.Trim();
            GGraph hit = null;

            try
            {
                hit = window.GetChild(hitName) as GGraph;
            }
            catch
            {
                hit = null;
            }

            bool created = false;
            if (hit == null || hit._disposed)
            {
                try
                {
                hit = new GGraph
                {
                    name = hitName,
                    touchable = true,
                };
                    window.AddChild(hit);
                    created = true;
                }
                catch
                {
                    return;
                }
            }

            float x = 0F;
            float y = 0F;
            float w = 0F;
            float h = 0F;

            if (closeButton != null && !closeButton._disposed)
            {
                bool ok = false;

                try
                {
                    var rectGlobal = closeButton.LocalToGlobal(new System.Drawing.RectangleF(0, 0, closeButton.width, closeButton.height));
                    Vector2 tl = window.GlobalToLocal(new Vector2(rectGlobal.X, rectGlobal.Y));
                    Vector2 br = window.GlobalToLocal(new Vector2(rectGlobal.Right, rectGlobal.Bottom));

                    x = tl.X;
                    y = tl.Y;
                    w = Math.Max(0F, br.X - tl.X);
                    h = Math.Max(0F, br.Y - tl.Y);

                    float pad = Math.Min(14F, Math.Min(w, h) * 0.3F);
                    x -= pad;
                    y -= pad;
                    w += pad * 2F;
                    h += pad * 2F;

                    ok = w > 1F && h > 1F;
                }
                catch
                {
                    ok = false;
                }

                // 如果无法计算出关闭按钮的有效区域，则仅对允许默认热区的窗口使用右上角热区兜底；否则不创建，避免误遮挡。
                if (!ok)
                {
                    if (allowDefault)
                    {
                        // 默认：右上角一个 96x96 的热区（UI 坐标）。
                        w = 96F;
                        h = 96F;
                        try
                        {
                            x = Math.Max(0F, window.width - w);
                            y = 0F;
                        }
                        catch
                        {
                            x = 0F;
                            y = 0F;
                        }
                    }
                    else
                    {
                        if (created)
                        {
                            try
                            {
                                hit.Dispose();
                            }
                            catch
                            {
                            }
                        }
                        return;
                    }
                }
            }
            else if (allowDefault)
            {
                // 默认：右上角一个 96x96 的热区（UI 坐标）。
                w = 96F;
                h = 96F;
                try
                {
                    x = Math.Max(0F, window.width - w);
                    y = 0F;
                }
                catch
                {
                    x = 0F;
                    y = 0F;
                }
            }
            else
            {
                // 非大地图且没有明确关闭按钮：不创建默认热区，避免遮挡窗口右上角其它按钮。
                if (created)
                {
                    try
                    {
                        hit.Dispose();
                    }
                    catch
                    {
                    }
                }
                return;
            }

            w = Math.Max(48F, w);
            h = Math.Max(48F, h);

            try
            {
                // 限制在窗口范围内，避免越界导致点击区域不可预期。
                float maxX = Math.Max(0F, window.width - w);
                float maxY = Math.Max(0F, window.height - h);
                x = Math.Clamp(x, 0F, maxX);
                y = Math.Clamp(y, 0F, maxY);
            }
            catch
            {
            }

            try
            {
                hit.DrawRect(w, h, 0, Microsoft.Xna.Framework.Color.Transparent, Microsoft.Xna.Framework.Color.Transparent);
                hit.SetPosition(x, y);
                hit.visible = true;
                hit.touchable = true;
                hit.enabled = true;
                hit.onClick.Set(() =>
                {
                    try
                    {
                        TryHideMobileWindow(windowKey);
                    }
                    catch
                    {
                    }
                });

                // 确保热区在最上层
                try
                {
                    window.SetChildIndex(hit, window.numChildren - 1);
                }
                catch
                {
                }
            }
            catch
            {
            }
        }

        private static GObject FindBestCloseButton(GComponent window, string[] keywords)
        {
            if (window == null || window._disposed)
                return null;

            try
            {
                if (window.GetChild("frame") is GComponent frame)
                {
                    GObject close = frame.GetChild("closeButton") ?? frame.GetChild("CloseButton") ?? frame.GetChild("CloseBtn") ??
                                    frame.GetChild("BtnClose") ?? frame.GetChild("BtnClose1") ?? frame.GetChild("BtnClose2") ?? frame.GetChild("BtnClose3") ??
                                    frame.GetChild("BtnColose") ?? frame.GetChild("DBagClose") ?? frame.GetChild("QEquipBtnClose") ?? frame.GetChild("NewNpcDCloseBtn");
                    if (close != null && close.touchable)
                        return close;
                }

                {
                    GObject close = window.GetChild("closeButton") ?? window.GetChild("CloseButton") ?? window.GetChild("CloseBtn") ??
                                    window.GetChild("BtnClose") ?? window.GetChild("BtnClose1") ?? window.GetChild("BtnClose2") ?? window.GetChild("BtnClose3") ??
                                    window.GetChild("BtnColose") ?? window.GetChild("DBagClose") ?? window.GetChild("QEquipBtnClose") ?? window.GetChild("NewNpcDCloseBtn");
                    if (close != null && close.touchable)
                        return close;
                }
            }
            catch
            {
            }

            if (keywords == null || keywords.Length == 0)
                return null;

            GObject best = null;
            int bestScore = 0;
            System.Drawing.RectangleF windowRect = default;
            bool hasWindowRect = false;

            try
            {
                windowRect = window.LocalToGlobal(new System.Drawing.RectangleF(0, 0, window.width, window.height));
                hasWindowRect = windowRect.Width > 1F && windowRect.Height > 1F;
            }
            catch
            {
                hasWindowRect = false;
            }

            foreach (GObject obj in Enumerate(window))
            {
                if (obj == null || obj._disposed)
                    continue;

                if (ReferenceEquals(obj, window))
                    continue;

                if (!obj.visible)
                    continue;

                int score = ScoreCloseCandidate(obj, keywords);

                // 位置兜底：更偏好“右上角的小控件”作为关闭按钮（尤其是 NPC 对话框的红叉）。
                if (hasWindowRect)
                {
                    try
                    {
                        System.Drawing.RectangleF rect = obj.LocalToGlobal(new System.Drawing.RectangleF(0, 0, obj.width, obj.height));
                        if (rect.Width > 1F && rect.Height > 1F)
                        {
                            float dxRight = Math.Abs(windowRect.Right - rect.Right);
                            float dyTop = Math.Abs(rect.Top - windowRect.Top);
                            bool inTopHalf = rect.Top <= windowRect.Top + windowRect.Height * 0.55F;
                            bool inRightHalf = rect.Right >= windowRect.Left + windowRect.Width * 0.45F;
                            bool smallEnough = rect.Width <= windowRect.Width * 0.35F && rect.Height <= windowRect.Height * 0.35F;

                            if (inTopHalf && inRightHalf && smallEnough)
                                score += 40;

                            if (dxRight <= 90F)
                                score += 15;
                            if (dyTop <= 90F)
                                score += 15;
                        }
                    }
                    catch
                    {
                    }
                }

                if (obj is GButton)
                    score += 10;
                else if (!obj.touchable)
                    score -= 15;
                if (score <= bestScore)
                    continue;

                bestScore = score;
                best = obj;
            }

            return bestScore >= 90 ? best : null;
        }

        private static void TryDumpMobileWindowTreeIfDue(string windowKey, string resolveInfo, GComponent window)
        {
            if (!Settings.DebugMode)
                return;

            if (string.IsNullOrWhiteSpace(windowKey))
                return;

            if (window == null || window._disposed)
                return;

            try
            {
                Directory.CreateDirectory(ClientResourceLayout.RuntimeRoot);

                string safeKey = SanitizeFileName(windowKey);
                string path = Path.Combine(ClientResourceLayout.RuntimeRoot, $"FairyGui-MobileWindow-{safeKey}-Tree.txt");

                var builder = new StringBuilder(8 * 1024);
                builder.AppendLine("FairyGUI 窗口树（用于关闭按钮/排障）");
                builder.AppendLine($"WindowKey={windowKey}");
                if (!string.IsNullOrWhiteSpace(resolveInfo))
                    builder.AppendLine($"Resolved={resolveInfo}");
                builder.AppendLine($"GeneratedAtUtc={DateTime.UtcNow:o}");
                builder.AppendLine("说明：idx/path 均相对窗口 Root，可用于 Mir2Config.ini 的 [FairyGUI] MobileWindowClose.<WindowKey> 覆盖关闭按钮。");
                builder.AppendLine();
                AppendGObjectTree(builder, window, depth: 0, maxDepth: 12, idxPath: string.Empty, namePath: string.Empty);

                File.WriteAllText(path, builder.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                CMain.SaveLog($"FairyGUI: 已导出窗口({windowKey})树到 {path}");
            }
            catch (Exception ex)
            {
                CMain.SaveError("FairyGUI: 导出窗口树失败（" + windowKey + "）：" + ex.Message);
            }
        }

        private static void TryDumpMobileWindowCloseBindingReportIfDue(string windowKey, GComponent window, string overrideSpec, string[] keywords, GObject selected)
        {
            if (!Settings.DebugMode)
                return;

            if (string.IsNullOrWhiteSpace(windowKey))
                return;

            if (window == null || window._disposed)
                return;

            try
            {
                Directory.CreateDirectory(ClientResourceLayout.RuntimeRoot);

                string safeKey = SanitizeFileName(windowKey);
                string path = Path.Combine(ClientResourceLayout.RuntimeRoot, $"FairyGui-MobileWindow-{safeKey}-CloseBinding.txt");

                string[] keywordList = keywords != null && keywords.Length > 0 ? keywords : DefaultWindowCloseButtonKeywords;

                var candidates = new List<(int Score, GObject Target)>();
                foreach (GObject obj in Enumerate(window))
                {
                    if (obj == null || obj._disposed)
                        continue;

                    if (ReferenceEquals(obj, window))
                        continue;

                    if (obj is not GButton)
                        continue;

                    if (!obj.touchable)
                        continue;

                    int score = ScoreCloseCandidate(obj, keywordList);
                    candidates.Add((score, obj));
                }

                candidates.Sort((a, b) => b.Score.CompareTo(a.Score));

                var builder = new StringBuilder(8 * 1024);
                builder.AppendLine("FairyGUI 窗口关闭按钮绑定报告（用于排障/补充覆盖）");
                builder.AppendLine($"GeneratedAtUtc={DateTime.UtcNow:o}");
                builder.AppendLine($"WindowKey={windowKey}");
                builder.AppendLine();
                builder.AppendLine("配置覆盖（可选）：Mir2Config.ini -> [FairyGUI]");
                builder.AppendLine("  MobileWindowClose.<WindowKey>=<Spec>");
                builder.AppendLine("  Spec 支持：path:... / idx:... / name:... / item:... / url:... / title:... / 或者关键字列表(a|b|c)");
                builder.AppendLine();
                builder.AppendLine($"OverrideSpec={(string.IsNullOrWhiteSpace(overrideSpec) ? "-" : overrideSpec)}");
                builder.AppendLine($"Keywords={(keywordList == null ? "-" : string.Join("|", keywordList))}");
                builder.AppendLine($"Selected={DescribeObject(window, selected)}");
                builder.AppendLine();
                builder.AppendLine("Candidates(top):");

                int top = Math.Min(12, candidates.Count);
                for (int i = 0; i < top; i++)
                {
                    (int score, GObject target) = candidates[i];
                    builder.Append("  score=").Append(score).Append(' ');
                    builder.AppendLine(DescribeObject(window, target));
                }

                File.WriteAllText(path, builder.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            }
            catch (Exception ex)
            {
                CMain.SaveError("FairyGUI: 导出窗口关闭按钮绑定报告失败（" + windowKey + "）：" + ex.Message);
            }
        }

        private static string DescribeObject(GComponent root, GObject obj)
        {
            if (obj == null || obj._disposed)
                return "(null)";

            string idxPath = BuildIndexPath(root, obj);
            string namePath = BuildNamePath(root, obj);

            string name = obj.name;
            string item = obj.packageItem?.name;
            string title = (obj as GButton)?.title;
            if (!string.IsNullOrWhiteSpace(title))
                title = title.Replace("\r", string.Empty).Replace("\n", "\\n");

            string url = obj.resourceURL;
            string exported = obj.packageItem?.exported == true ? "exported" : "internal";
            string rect = "(unknown)";
            try
            {
                var global = obj.LocalToGlobal(new System.Drawing.RectangleF(0, 0, obj.width, obj.height));
                rect = $"{global.X:0.##},{global.Y:0.##},{global.Width:0.##},{global.Height:0.##}";
            }
            catch
            {
            }

            return "idx=" + (string.IsNullOrWhiteSpace(idxPath) ? "-" : idxPath) +
                   " path=" + (string.IsNullOrWhiteSpace(namePath) ? "-" : namePath) +
                   " name=" + (name ?? "(null)") +
                   " item=" + (item ?? "(null)") +
                   " title=" + (title ?? "(null)") +
                   " url=" + (url ?? "(null)") +
                   " rect=" + rect +
                   " " + exported +
                   " gfx=" + DescribeObjectTextureState(obj) +
                    " touchable=" + obj.touchable +
                    " visible=" + obj.visible;
        }

        private static string DescribeObjectTextureState(GObject obj)
        {
            if (obj == null || obj._disposed)
                return "(null)";

            try
            {
                NTexture graphicsTexture = obj.displayObject?.graphics?.texture;
                NTexture packageTexture = obj.packageItem?.texture;
                string graphics = DescribeTextureState(graphicsTexture);
                string package = DescribeTextureState(packageTexture);

                if (string.Equals(graphics, package, StringComparison.Ordinal))
                    return graphics;

                return "gfx:" + graphics + "/pkg:" + package;
            }
            catch
            {
                return "(error)";
            }
        }

        private static string DescribeTextureState(NTexture texture)
        {
            if (texture == null)
                return "null";

            try
            {
                Texture2D native = texture.nativeTexture;
                string nativeSize = native != null && !native.IsDisposed
                    ? $"{native.Width}x{native.Height}"
                    : "null";

                return $"{texture.width}x{texture.height}/root={nativeSize}/rot={(texture.rotated ? 1 : 0)}";
            }
            catch
            {
                return "(error)";
            }
        }

        private static string SanitizeFileName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return "Unknown";

            string input = name.Trim();
            char[] invalid = Path.GetInvalidFileNameChars();
            var builder = new StringBuilder(input.Length);
            for (int i = 0; i < input.Length; i++)
            {
                char ch = input[i];
                bool ok = true;
                for (int j = 0; j < invalid.Length; j++)
                {
                    if (ch == invalid[j])
                    {
                        ok = false;
                        break;
                    }
                }

                builder.Append(ok ? ch : '_');
            }

            string result = builder.ToString();
            return string.IsNullOrWhiteSpace(result) ? "Unknown" : result;
        }

        private static int ScoreCloseCandidate(GObject obj, string[] keywords)
        {
            if (obj == null || keywords == null || keywords.Length == 0)
                return 0;

            string name = obj.name;
            string item = obj.packageItem?.name;
            string title = (obj as GButton)?.title;
            if (!string.IsNullOrWhiteSpace(title))
                title = title.Replace("\r", string.Empty).Replace("\n", string.Empty);

            int score = 0;

            for (int i = 0; i < keywords.Length; i++)
            {
                string keyword = keywords[i];
                if (string.IsNullOrWhiteSpace(keyword))
                    continue;

                keyword = keyword.Trim();
                if (keyword.Length == 0)
                    continue;

                score += ScoreField(name, keyword, equalsWeight: 200, startsWithWeight: 80, containsWeight: 30);
                score += ScoreField(item, keyword, equalsWeight: 200, startsWithWeight: 80, containsWeight: 30);
                score += ScoreField(title, keyword, equalsWeight: 200, startsWithWeight: 80, containsWeight: 30);
            }

            if (obj.packageItem?.exported == true)
                score += 10;

            // 轻微偏好更具体的名字
            string basis = item ?? name ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(basis))
                score -= Math.Min(12, basis.Length / 6);

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

        private static void ResetMobileMiniMapLocator()
        {
            _mobileMiniMapLocatorInitialized = false;
            _mobileMiniMapLocatorLogged = false;
            _mobileMiniMapOverrideSpec = null;
            _mobileMiniMapRoot = null;
            _mobileMiniMapKeywords = null;
        }

        private static void EnsureMobileMiniMapLocatorInitialized()
        {
            if (_mobileMiniMapLocatorInitialized)
                return;

            _mobileMiniMapLocatorInitialized = true;
            _mobileMiniMapOverrideSpec = null;
            _mobileMiniMapRoot = null;
            _mobileMiniMapKeywords = DefaultMiniMapKeywords;

            try
            {
                InIReader reader = TryCreateConfigReader();
                if (reader == null)
                    return;

                string spec = reader.ReadString(FairyGuiConfigSectionName, MobileMainHudMiniMapConfigKey, string.Empty, writeWhenNull: false);
                spec = spec?.Trim();
                if (string.IsNullOrWhiteSpace(spec))
                    return;

                _mobileMiniMapOverrideSpec = spec;

                if (TryResolveMobileMainHudObjectBySpec(_mobileMainHud, spec, out GObject resolved, out string[] keywords))
                {
                    if (resolved != null && !resolved._disposed)
                        _mobileMiniMapRoot = resolved;

                    if (keywords != null && keywords.Length > 0)
                        _mobileMiniMapKeywords = keywords;
                }
            }
            catch
            {
            }

            if (!Settings.DebugMode || _mobileMiniMapLocatorLogged)
                return;

            _mobileMiniMapLocatorLogged = true;

            if (_mobileMiniMapRoot != null)
                CMain.SaveLog($"FairyGUI: 小地图命中节点已解析：idx={BuildIndexPath(_mobileMainHud, _mobileMiniMapRoot)} path={BuildNamePath(_mobileMainHud, _mobileMiniMapRoot)} name={_mobileMiniMapRoot.name} item={_mobileMiniMapRoot.packageItem?.name}");
            else
                CMain.SaveLog($"FairyGUI: 小地图命中节点未指定或未解析，将使用关键字匹配：{string.Join("|", _mobileMiniMapKeywords ?? Array.Empty<string>())}（可在 Mir2Config.ini 设置 [{FairyGuiConfigSectionName}] {MobileMainHudMiniMapConfigKey}=idx:...）");
        }

        private static InIReader TryCreateConfigReader()
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

        private static bool TryResolveMobileMainHudObjectBySpec(GComponent root, string spec, out GObject resolved, out string[] keywords)
        {
            resolved = null;
            keywords = null;

            if (root == null || root._disposed || string.IsNullOrWhiteSpace(spec))
                return false;

            string input = spec.Trim();

            if (TryParsePrefixedValue(input, "path", out string value))
            {
                resolved = FindByPath(root, value);
                return resolved != null;
            }

            if (TryParsePrefixedValue(input, "idx", out value) || TryParsePrefixedValue(input, "index", out value))
            {
                resolved = FindByIndexPath(root, value);
                return resolved != null;
            }

            if (TryParsePrefixedValue(input, "name", out value))
            {
                resolved = FindByExact(root, value, matchName: true, matchItem: false, matchUrl: false, matchTitle: false);
                return resolved != null;
            }

            if (TryParsePrefixedValue(input, "item", out value))
            {
                resolved = FindByExact(root, value, matchName: false, matchItem: true, matchUrl: false, matchTitle: false);
                return resolved != null;
            }

            if (TryParsePrefixedValue(input, "url", out value))
            {
                resolved = FindByExact(root, value, matchName: false, matchItem: false, matchUrl: true, matchTitle: false);
                return resolved != null;
            }

            if (TryParsePrefixedValue(input, "title", out value))
            {
                resolved = FindByExact(root, value, matchName: false, matchItem: false, matchUrl: false, matchTitle: true);
                return resolved != null;
            }

            keywords = SplitKeywords(input);
            return keywords.Length > 0;
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

            foreach (GObject obj in Enumerate(root))
            {
                if (ReferenceEquals(obj, root))
                    continue;

                if (matchName && string.Equals(obj.name, expected, StringComparison.OrdinalIgnoreCase))
                    return obj;

                if (matchItem && string.Equals(obj.packageItem?.name, expected, StringComparison.OrdinalIgnoreCase))
                    return obj;

                if (matchUrl && string.Equals(obj.resourceURL, expected, StringComparison.OrdinalIgnoreCase))
                    return obj;

                if (matchTitle && obj is GButton button && string.Equals(button.title, expected, StringComparison.OrdinalIgnoreCase))
                    return obj;
            }

            return null;
        }

        private static IEnumerable<GObject> Enumerate(GComponent root)
        {
            if (root == null || root._disposed)
                yield break;

            var stack = new Stack<GObject>();
            stack.Push(root);

            while (stack.Count > 0)
            {
                GObject current = stack.Pop();
                if (current == null)
                    continue;

                yield return current;

                if (current is not GComponent component)
                    continue;

                int count = component.numChildren;
                for (int i = count - 1; i >= 0; i--)
                {
                    GObject child = component.GetChildAt(i);
                    if (child != null)
                        stack.Push(child);
                }
            }
        }

        private static bool IsAnyFieldMatchKeywords(GObject obj, string[] keywords)
        {
            if (obj == null || keywords == null || keywords.Length == 0)
                return false;

            string name = obj.name;
            string item = obj.packageItem?.name;
            string url = obj.resourceURL;
            string title = obj is GButton button ? button.title : null;

            for (int i = 0; i < keywords.Length; i++)
            {
                string keyword = keywords[i];
                if (string.IsNullOrWhiteSpace(keyword))
                    continue;

                keyword = keyword.Trim();
                if (keyword.Length == 0)
                    continue;

                if (!string.IsNullOrWhiteSpace(name) && name.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;

                if (!string.IsNullOrWhiteSpace(item) && item.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;

                if (!string.IsNullOrWhiteSpace(url) && url.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;

                if (!string.IsNullOrWhiteSpace(title) && title.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }

            return false;
        }

        private static bool TryFindBestComponentByKeywords(string[] keywords, out string packageName, out string componentName, out string resolveInfo)
        {
            packageName = null;
            componentName = null;
            resolveInfo = null;

            if (keywords == null || keywords.Length == 0)
                return false;

            List<UIPackage> packages = UIPackage.GetPackages();
            if (packages == null || packages.Count == 0)
                return false;

            // 支持关键字里直接写 "Pkg/Component"：用于显式指定组件，避免“他人角色_DStateUI”等同名/相似命名误命中。
            for (int i = 0; i < keywords.Length; i++)
            {
                string kw = keywords[i];
                if (string.IsNullOrWhiteSpace(kw))
                    continue;

                kw = kw.Trim().Replace('\\', '/');
                int slashIndex = kw.IndexOf('/');
                if (slashIndex <= 0 || slashIndex >= kw.Length - 1)
                    continue;

                string pkgName = kw.Substring(0, slashIndex).Trim();
                string compName = kw.Substring(slashIndex + 1).Trim();
                if (string.IsNullOrWhiteSpace(pkgName) || string.IsNullOrWhiteSpace(compName))
                    continue;

                for (int p = 0; p < packages.Count; p++)
                {
                    UIPackage pkg = packages[p];
                    if (pkg == null)
                        continue;

                    if (!string.Equals(pkg.name, pkgName, StringComparison.OrdinalIgnoreCase))
                        continue;

                    List<PackageItem> items = pkg.GetItems();
                    if (items == null)
                        continue;

                    for (int j = 0; j < items.Count; j++)
                    {
                        PackageItem item = items[j];
                        if (item == null || item.type != PackageItemType.Component)
                            continue;

                        if (!string.Equals(item.name, compName, StringComparison.OrdinalIgnoreCase))
                            continue;

                        packageName = pkg.name;
                        componentName = item.name;
                        resolveInfo = pkg.name + "/" + item.name + (item.exported ? " (exported)" : " (internal)") + " (spec)";
                        return true;
                    }
                }
            }

            int bestScore = -1;
            string bestPackage = null;
            string bestComponent = null;
            bool bestExported = false;

            for (int i = 0; i < packages.Count; i++)
            {
                UIPackage pkg = packages[i];
                if (pkg == null)
                    continue;

                List<PackageItem> items = pkg.GetItems();
                if (items == null)
                    continue;

                for (int j = 0; j < items.Count; j++)
                {
                    PackageItem item = items[j];
                    if (item == null || item.type != PackageItemType.Component)
                        continue;

                    string name = item.name ?? string.Empty;
                    int score = 0;
                    int hitCount = 0;

                    for (int k = 0; k < keywords.Length; k++)
                    {
                        string kw = keywords[k];
                        if (string.IsNullOrWhiteSpace(kw))
                            continue;

                        kw = kw.Trim();

                        if (string.Equals(name, kw, StringComparison.OrdinalIgnoreCase))
                        {
                            score += 200;
                            hitCount++;
                            continue;
                        }

                        if (name.StartsWith(kw, StringComparison.OrdinalIgnoreCase))
                        {
                            score += 80;
                            hitCount++;
                            continue;
                        }

                        if (name.IndexOf(kw, StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            score += 30;
                            hitCount++;
                        }
                    }

                    if (hitCount == 0)
                        continue;

                    if (item.exported)
                        score += 20;

                    // 轻微偏好“更短/更具体”的名字，避免命中过于泛化的组件
                    score -= Math.Min(12, name.Length / 6);

                    if (score <= bestScore)
                        continue;

                    bestScore = score;
                    bestPackage = pkg.name;
                    bestComponent = item.name;
                    bestExported = item.exported;
                }
            }

            if (bestScore < 0 || string.IsNullOrWhiteSpace(bestPackage) || string.IsNullOrWhiteSpace(bestComponent))
                return false;

            packageName = bestPackage;
            componentName = bestComponent;
            resolveInfo = bestPackage + "/" + bestComponent + (bestExported ? " (exported)" : " (internal)") + " score=" + bestScore;
            return true;
        }

        public static void TryInitialize(CMain game)
        {
            if (game == null)
                return;

            if (_initialized)
                return;

            lock (InitGate)
            {
                if (_initialized)
                    return;

                try
                {
                _loader = new FairyGuiPublishResourceLoader("复古");
                FairyResourceLoader.Current = _loader;

                _stage = new Stage(game, handler: null);
                Stage.MouseStateProvider = GetMouseStateForFairyGui;
                _stage.Initialize();

                _uiManager = new FairyGuiUiManager();

                try
                {
                    _mobileOverlaySafeAreaRoot = new GComponent
                    {
                        name = "MobileOverlaySafeAreaRoot",
                        opaque = false,
                    };

                    (_uiManager?.OverlayLayer ?? GRoot.inst).AddChild(_mobileOverlaySafeAreaRoot);
                    EnsureMobileOverlaySafeAreaLayout(force: true);
                }
                catch
                {
                    _mobileOverlaySafeAreaRoot = null;
                    _mobileOverlaySafeAreaBounds = default;
                }

                _initialized = true;
                _initError = null;
                CMain.SaveLog("FairyGUI: Stage 初始化完成（移动端 UI 接入 POC）");
            }
                catch (Exception ex)
                {
                _initialized = true;
                _initError = ex.ToString();
                _stage = null;
                _uiManager = null;
                CMain.SaveError("FairyGUI: Stage 初始化失败：" + ex);
            }
        }
        }

        public static void Update(GameTime gameTime)
        {
            try
            {
                _stage?.Update(gameTime);
                TryApplyPendingMobileDoubleJoystickVisualIfDue();
                 TryLoadDefaultPackagesIfDue();
                   TryDumpPackageComponentListIfDue();
                   EnsureMobileMainHudLayout(force: false);
                   TryBindMobileMainHudFunButtonsToggleIfDue();
                    TryRefreshMobileMainHudMiniMapIfDue(force: false);
                    TryRefreshMobileMainHudHotbarsIfDue(force: false);
                    TryRefreshMobileMainHudCurrencyIfDue(force: false);
                    TryRefreshMobileMainHudAttackCircleIfDue(force: false);
                    TryRefreshMobileMainHudNearbyPanelIfDue(force: false);
                 TryRefreshMobileMainHudMessageBarIfDue(force: false);
                 TryEnforceMobileMainHudFixedFontSizesIfDue();
                 TryRefreshMobileMainHudBottomStatsIfDue(force: false);
                    TryRefreshMobileQuestTrackingIfDue(force: false);
                    TryEnforceMobileMainHudPersistentHiddenPartsIfDue(force: false);
                    EnsureMobileOverlaySafeAreaLayout(force: false);
                    TryRefreshMobileCenterToastIfDue();
                     TryRefreshMobileNoticeIfDue(force: false);
                     TryRefreshMobileMailIfDue(force: false);
                   TryRefreshMobileInventoryIfDue(force: false);
                   TryRefreshMobileStateIfDue(force: false);
                  TryRefreshMobileBigMapIfDue(force: false);
                  TryAutoClearMobileMailItemLocksIfDue();
                  TryRefreshMobileMagicIfDue(force: false);
                  TryRefreshMobileStorageIfDue(force: false);
                  TryApplyMobileInventoryStorageSideBySideLayoutIfDue(force: false);
                 TryRefreshMobileSystemIfDue(force: false);
                TryRefreshMobileShopIfDue(force: false);
                TryRefreshMobileFriendIfDue(force: false);
                TryRefreshMobileGuildIfDue(force: false);
                TryRefreshMobileGroupIfDue(force: false);
                TryRefreshMobileTradeIfDue(force: false);
                TryRefreshMobileNpcIfDue(force: false);
                TryRefreshMobileNpcGoodsIfDue(force: false);
                TryRefreshMobileTrustMerchantIfDue(force: false);
#if ANDROID || IOS
                TrySyncSoftKeyboardWithFairyFocus();
#endif
            }
            catch (Exception ex)
            {
                CMain.SaveError("FairyGUI: Update 异常：" + ex);
            }
        }

        public static void Draw(GameTime gameTime)
        {
            try
            {
                _stage?.Draw(gameTime);
            }
            catch (Exception ex)
            {
                CMain.SaveError("FairyGUI: Draw 异常：" + ex);
            }
        }

#if ANDROID || IOS
        private static MouseState GetMouseStateForFairyGui()
        {
            if (Environment.OSVersion.Platform == PlatformID.Win32NT)
                return CMain.currentMouseState;

            Vector2 position = CMain.PointerTouchPosition;

            if (CMain.PointerTouchStarted && CMain.PointerTouchActive)
            {
                _pointerCapturedByFairyGui = IsPointOverFairyGuiUI(position);
            }
            else if (CMain.PointerTouchEnded || !CMain.PointerTouchActive)
            {
                _pointerCapturedByFairyGui = false;
            }

            ButtonState left = _pointerCapturedByFairyGui && CMain.PointerTouchActive ? ButtonState.Pressed : ButtonState.Released;

            return new MouseState(
                (int)position.X,
                (int)position.Y,
                0,
                left,
                ButtonState.Released,
                ButtonState.Released,
                ButtonState.Released,
                ButtonState.Released);
        }
#else
        private static MouseState GetMouseStateForFairyGui() => CMain.currentMouseState;
#endif

        private static void TryLoadDefaultPackagesIfDue()
        {
            if (_packagesLoaded)
                return;

            if (_stage == null)
                return;

            if (DateTime.UtcNow < _nextPackageLoadAttemptUtc)
                return;

            if (!CanLoadDefaultPackagesNow(out string waitReason))
            {
                _lastPackageLoadError = null;
                _nextPackageLoadAttemptUtc = DateTime.UtcNow.AddSeconds(1);
                LogDefaultPackageWaitIfDue(waitReason);
                return;
            }

            _nextPackageLoadAttemptUtc = DateTime.UtcNow.AddSeconds(2);

            var errors = new List<string>();
            for (int i = 0; i < DefaultPackageNames.Length; i++)
            {
                string packageName = DefaultPackageNames[i];
                if (string.IsNullOrWhiteSpace(packageName))
                    continue;

                try
                {
                    UIPackage.AddPackage(packageName);
                }
                catch (Exception ex)
                {
                    errors.Add($"{packageName}: {ex.Message}");
                }
            }

            if (errors.Count == 0)
            {
                _packagesLoaded = true;
                _lastPackageLoadError = null;
                _lastMobileDefaultPackageWaitReason = null;
                CMain.SaveLog("FairyGUI: publish 包加载完成（复古）");
                return;
            }

            _lastPackageLoadError = string.Join(" | ", errors);
        }

        private static bool CanLoadDefaultPackagesNow(out string waitReason)
        {
            waitReason = null;

            if (Environment.OSVersion.Platform == PlatformID.Win32NT)
                return true;

            List<string> missingProbeFiles = null;
            for (int i = 0; i < MobileDefaultPackageProbeFiles.Length; i++)
            {
                string probeFile = MobileDefaultPackageProbeFiles[i];
                if (string.IsNullOrWhiteSpace(probeFile))
                    continue;

                string probePath = Path.Combine(MobileDefaultPackageProbeDirectory, probeFile);
                if (ClientResourceLayout.TryHydrateFileFromPackage(probePath, out string availablePath) && File.Exists(availablePath))
                    continue;

                missingProbeFiles ??= new List<string>();
                missingProbeFiles.Add(probeFile);
            }

            if (missingProbeFiles == null || missingProbeFiles.Count == 0)
                return true;

            var builder = new StringBuilder(192);
            if (MonoShare.MirControls.MirScene.ActiveScene is PreLoginUpdateScene)
                builder.Append("预登录更新阶段等待默认 FGUI 包资源同步");
            else
                builder.Append("默认 FGUI 包尚未就绪");

            try
            {
                BootstrapPackageStateView packageState = BootstrapPackageRuntime.FindPackage(MobileDefaultPackageBootstrapName);
                if (packageState != null)
                {
                    builder.Append("，")
                           .Append(MobileDefaultPackageBootstrapName)
                           .Append('=')
                           .Append(string.IsNullOrWhiteSpace(packageState.Status) ? "unknown" : packageState.Status);

                    if (packageState.AssetCount > 0)
                    {
                        builder.Append(" (hydrated=")
                               .Append(packageState.HydratedAssetCount)
                               .Append('/')
                               .Append(packageState.AssetCount)
                               .Append(", staged=")
                               .Append(packageState.StagedAssetCount)
                               .Append(')');
                    }
                }
            }
            catch
            {
            }

            builder.Append("，缺少探针=");
            int sampleCount = Math.Min(3, missingProbeFiles.Count);
            for (int i = 0; i < sampleCount; i++)
            {
                if (i > 0)
                    builder.Append(", ");

                builder.Append(missingProbeFiles[i]);
            }

            if (missingProbeFiles.Count > sampleCount)
                builder.Append(" 等");

            waitReason = builder.ToString();
            return false;
        }

        private static void LogDefaultPackageWaitIfDue(string waitReason)
        {
            if (string.IsNullOrWhiteSpace(waitReason))
                return;

            DateTime now = DateTime.UtcNow;
            if (string.Equals(waitReason, _lastMobileDefaultPackageWaitReason, StringComparison.Ordinal) &&
                now < _nextMobileDefaultPackageWaitLogUtc)
            {
                return;
            }

            _lastMobileDefaultPackageWaitReason = waitReason;
            _nextMobileDefaultPackageWaitLogUtc = now.AddSeconds(5);
            CMain.SaveLog("FairyGUI: " + waitReason);
        }

        private static void TryDumpPackageComponentListIfDue()
        {
            if (!_packagesLoaded)
                return;

            if (_packageComponentListDumped)
                return;

            try
            {
                Directory.CreateDirectory(ClientResourceLayout.RuntimeRoot);

                List<UIPackage> packages = UIPackage.GetPackages();
                packages.Sort((a, b) => string.Compare(a?.name, b?.name, StringComparison.OrdinalIgnoreCase));

                var builder = new StringBuilder(32 * 1024);
                builder.AppendLine("FairyGUI 包/组件清单（用于 Overlay -> FairyGUI 组件映射审计）");
                builder.AppendLine($"GeneratedAtUtc={DateTime.UtcNow:o}");
                builder.AppendLine();

                for (int i = 0; i < packages.Count; i++)
                {
                    UIPackage pkg = packages[i];
                    if (pkg == null)
                        continue;

                    List<PackageItem> items = pkg.GetItems();
                    builder.AppendLine($"Package={pkg.name} Id={pkg.id} Items={items?.Count ?? 0}");

                    if (items != null)
                    {
                        for (int j = 0; j < items.Count; j++)
                        {
                            PackageItem item = items[j];
                            if (item == null)
                                continue;

                            if (item.type != PackageItemType.Component)
                                continue;

                            string exported = item.exported ? "exported" : "internal";
                            builder.AppendLine($"  Component {item.name} ({exported})");
                        }
                    }

                    builder.AppendLine();
                }

                string path = Path.Combine(ClientResourceLayout.RuntimeRoot, "FairyGui-PackageComponentList.txt");
                File.WriteAllText(path, builder.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

                _packageComponentListDumped = true;
                CMain.SaveLog($"FairyGUI: 已导出包组件清单到 {path}");
            }
            catch (Exception ex)
            {
                _packageComponentListDumped = false;
                CMain.SaveError("FairyGUI: 导出包组件清单失败：" + ex.Message);
            }
        }

        private static void EnsureMobileMainHudLayout(bool force)
        {
            if (_mobileMainHudSafeAreaRoot == null || _mobileMainHudSafeAreaRoot._disposed)
                return;

            DrawingRectangle safeArea = Settings.GetMobileSafeAreaBounds();

            if (!force && safeArea == _mobileMainHudSafeAreaBounds)
                return;

            _mobileMainHudSafeAreaBounds = safeArea;

            try
            {
                float scale = UIContentScaler.scaleFactor;
                if (scale <= 0.01F)
                    scale = 1F;

                int x = (int)Math.Round(safeArea.Left / scale);
                int y = (int)Math.Round(safeArea.Top / scale);
                int w = (int)Math.Round(safeArea.Width / scale);
                int h = (int)Math.Round(safeArea.Height / scale);

                _mobileMainHudSafeAreaRoot.SetPosition(x, y);
                _mobileMainHudSafeAreaRoot.SetSize(w, h);

                try
                {
                    if (_mobileMainHud != null && !_mobileMainHud._disposed)
                    {
                        float designW = _mobileMainHud.initWidth > 0 ? _mobileMainHud.initWidth : _mobileMainHud.width;
                        float designH = _mobileMainHud.initHeight > 0 ? _mobileMainHud.initHeight : _mobileMainHud.height;
                        designW = Math.Max(1F, designW);
                        designH = Math.Max(1F, designH);

                        float scaleX = w / designW;
                        float scaleY = h / designH;
                        float uiScale = Math.Min(scaleX, scaleY);
                        if (uiScale <= 0.01F || float.IsNaN(uiScale) || float.IsInfinity(uiScale))
                            uiScale = 1F;

                        float scaledW = designW * uiScale;
                        float scaledH = designH * uiScale;
                        float offsetX = (w - scaledW) * 0.5F;
                        float offsetY = (h - scaledH) * 0.5F;
                        if (float.IsNaN(offsetX) || float.IsInfinity(offsetX))
                            offsetX = 0F;
                        if (float.IsNaN(offsetY) || float.IsInfinity(offsetY))
                            offsetY = 0F;

                        _mobileMainHud.SetSize(designW, designH, ignorePivot: true);
                        _mobileMainHud.SetScale(uiScale, uiScale);
                        _mobileMainHud.SetPosition(offsetX, offsetY);
                    }
                }
                catch
                {
                }

                if (Settings.LogErrors)
                    CMain.SaveLog($"FairyGUI: SafeArea(MainHud) px={safeArea.Left},{safeArea.Top},{safeArea.Width},{safeArea.Height} scaleFactor={scale:0.###} -> ui={x},{y},{w},{h}");
            }
            catch
            {
            }
        }

        private static void EnsureMobileOverlaySafeAreaLayout(bool force)
        {
            if (_mobileOverlaySafeAreaRoot == null || _mobileOverlaySafeAreaRoot._disposed)
                return;

            DrawingRectangle safeArea = Settings.GetMobileSafeAreaBounds();

            if (!force && safeArea == _mobileOverlaySafeAreaBounds)
                return;

            _mobileOverlaySafeAreaBounds = safeArea;

            try
            {
                float scale = UIContentScaler.scaleFactor;
                if (scale <= 0.01F)
                    scale = 1F;

                int x = (int)Math.Round(safeArea.Left / scale);
                int y = (int)Math.Round(safeArea.Top / scale);
                int w = (int)Math.Round(safeArea.Width / scale);
                int h = (int)Math.Round(safeArea.Height / scale);

                _mobileOverlaySafeAreaRoot.SetPosition(x, y);
                _mobileOverlaySafeAreaRoot.SetSize(w, h);

                if (Settings.LogErrors)
                    CMain.SaveLog($"FairyGUI: SafeArea(Overlay) px={safeArea.Left},{safeArea.Top},{safeArea.Width},{safeArea.Height} scaleFactor={scale:0.###} -> ui={x},{y},{w},{h}");
            }
            catch
            {
            }
        }

        private static string ChooseMobileMainHudComponentName()
        {
            DrawingRectangle safeArea = Settings.GetMobileSafeAreaBounds();
            int width = Math.Max(1, Settings.ScreenWidth);
            int height = Math.Max(1, Settings.ScreenHeight);

            int leftInset = Math.Max(0, safeArea.Left);
            int topInset = Math.Max(0, safeArea.Top);
            int rightInset = Math.Max(0, width - safeArea.Right);
            int bottomInset = Math.Max(0, height - safeArea.Bottom);

            bool hasInsets = leftInset > 0 || topInset > 0 || rightInset > 0 || bottomInset > 0;
            return hasInsets ? MobileMainHudComponentNameNotch : MobileMainHudComponentNameNormal;
        }

        private static void TryInstallMobileMainHudDebugClickLogger()
        {
            if (_stage == null)
                return;

            if (!Settings.DebugMode)
                return;

            if (_mobileMainHudClickLoggerInstalled)
                return;

            try
            {
                _stage.onClick.AddCapture(MobileMainHudClickLogger);
                _mobileMainHudClickLoggerInstalled = true;
            }
            catch
            {
            }
        }

        private static void TryBindMobileMainHudButtonsIfDue()
        {
            if (_mobileMainHud == null || _mobileMainHud._disposed)
                return;

            if (_mobileMainHudController == null)
                _mobileMainHudController = new MobileMainHudController(_mobileMainHud);

            try
            {
                _mobileMainHudController.BindIfNeeded();
            }
            catch (Exception ex)
            {
                CMain.SaveError("FairyGUI: 主界面按钮绑定异常：" + ex.Message);
            }
        }

        private static void OnStageClickForDebug(EventContext context)
        {
            if (context == null)
                return;

            try
            {
                object initiator = context.initiator;

                if (initiator is DisplayObject display)
                {
                    GObject owner = display.gOwner;
                    if (owner != null)
                    {
                        string name = owner.name ?? display.name ?? "(null)";
                        string itemName = owner.packageItem?.name ?? "(null)";
                        string title = (owner as GButton)?.title ?? "(null)";
                        if (title.Contains('\n'))
                            title = title.Replace("\r", string.Empty).Replace("\n", "\\n");

                        string idxPath = BuildIndexPath(_mobileMainHud, owner);
                        string namePath = BuildNamePath(_mobileMainHud, owner);
                        CMain.DebugText = $"FairyGUI Click: idx={idxPath} path={namePath} name={name} item={itemName} title={title}";
                        return;
                    }

                    string fallbackName = display.name ?? "(null)";
                    CMain.DebugText = $"FairyGUI Click: {fallbackName}";
                    return;
                }

                if (initiator is GObject gObject)
                {
                    string name = gObject.name ?? "(null)";
                    string itemName = gObject.packageItem?.name ?? "(null)";
                    string title = (gObject as GButton)?.title ?? "(null)";
                    if (title.Contains('\n'))
                        title = title.Replace("\r", string.Empty).Replace("\n", "\\n");

                    string idxPath = BuildIndexPath(_mobileMainHud, gObject);
                    string namePath = BuildNamePath(_mobileMainHud, gObject);
                    CMain.DebugText = $"FairyGUI Click: idx={idxPath} path={namePath} name={name} item={itemName} title={title}";
                    return;
                }

                CMain.DebugText = $"FairyGUI Click: {(initiator?.GetType().Name ?? "(null)")}";
            }
            catch
            {
            }
        }

        private static string BuildNamePath(GComponent root, GObject obj)
        {
            if (obj == null)
                return string.Empty;

            var parts = new Stack<string>();
            GObject current = obj;
            int depth = 0;

            while (current != null && !ReferenceEquals(current, root) && depth < 24)
            {
                string segment = current.name ?? current.packageItem?.name ?? current.GetType().Name;
                parts.Push(string.IsNullOrWhiteSpace(segment) ? "(null)" : segment);
                current = current.parent;
                depth++;
            }

            return parts.Count > 0 ? string.Join("/", parts) : string.Empty;
        }

        private static string BuildIndexPath(GComponent root, GObject obj)
        {
            if (obj == null)
                return string.Empty;

            var parts = new Stack<string>();
            GObject current = obj;
            int depth = 0;

            while (current != null && !ReferenceEquals(current, root) && depth < 24)
            {
                GComponent parent = current.parent;
                if (parent == null)
                    break;

                int index = parent.GetChildIndex(current);
                if (index < 0)
                    break;

                parts.Push(index.ToString());
                current = parent;
                depth++;
            }

            return parts.Count > 0 ? string.Join("/", parts) : string.Empty;
        }

        private static void TryDumpMobileMainHudTreeIfDue(string componentName)
        {
            if (_mobileMainHud == null || _mobileMainHud._disposed)
                return;

            if (!Settings.DebugMode)
                return;

            if (_mobileMainHudTreeDumped)
                return;

            try
            {
                Directory.CreateDirectory(ClientResourceLayout.RuntimeRoot);

                var builder = new StringBuilder(8 * 1024);
                builder.AppendLine("FairyGUI 主界面树（用于绑定按钮/排障）");
                builder.AppendLine($"Package={MobileMainHudPackageName} Component={componentName}");
                builder.AppendLine($"GeneratedAtUtc={DateTime.UtcNow:o}");
                builder.AppendLine("说明：idx/path 均相对主界面 Root，可用于 Mir2Config.ini 的 [FairyGUI] MobileMainHud.<ActionKey> 覆盖绑定。");
                builder.AppendLine();

                AppendGObjectTree(builder, _mobileMainHud, depth: 0, maxDepth: 12, idxPath: string.Empty, namePath: string.Empty);

                string path = Path.Combine(ClientResourceLayout.RuntimeRoot, "FairyGui-MobileMainHudTree.txt");
                File.WriteAllText(path, builder.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

                _mobileMainHudTreeDumped = true;
                CMain.SaveLog($"FairyGUI: 已导出主界面树到 {path}");
            }
            catch (Exception ex)
            {
                CMain.SaveError("FairyGUI: 导出主界面树失败：" + ex.Message);
            }
        }

        private static void AppendGObjectTree(StringBuilder builder, GObject obj, int depth, int maxDepth, string idxPath, string namePath)
        {
            if (builder == null || obj == null)
                return;

            builder.Append(' ', Math.Max(0, depth) * 2);
            builder.Append("idx=").Append(string.IsNullOrWhiteSpace(idxPath) ? "-" : idxPath);
            builder.Append(" path=").Append(string.IsNullOrWhiteSpace(namePath) ? "-" : namePath);
            builder.Append(' ');
            builder.Append(obj.GetType().Name);
            builder.Append(" name=").Append(obj.name ?? "(null)");

            string url = obj.resourceURL;
            if (!string.IsNullOrWhiteSpace(url))
                builder.Append(" url=").Append(url);

            string packageItemName = obj.packageItem?.name;
            if (!string.IsNullOrWhiteSpace(packageItemName))
                builder.Append(" item=").Append(packageItemName);

            if (obj is GButton button)
            {
                string title = button.title;
                if (!string.IsNullOrWhiteSpace(title))
                {
                    if (title.Contains('\n'))
                        title = title.Replace("\r", string.Empty).Replace("\n", "\\n");

                    builder.Append(" title=").Append(title);
                }
            }

            if (!obj.touchable)
                builder.Append(" touchable=false");

            if (!obj.visible)
                builder.Append(" visible=false");

            if (!IsEffectivelyVisible(obj))
                builder.Append(" effectiveVisible=false");

            try
            {
                if (Math.Abs(obj.alpha - 1F) > 0.001F)
                    builder.Append(" alpha=").Append(obj.alpha.ToString("0.###", CultureInfo.InvariantCulture));
            }
            catch
            {
            }

            if (obj.packageItem?.exported == true)
                builder.Append(" exported=true");

            if (obj is GComponent componentWithControllers)
                AppendControllerSnapshot(builder, componentWithControllers);

            builder.AppendLine();

            if (depth >= maxDepth)
                return;

            if (obj is not GComponent component)
                return;

            int count = component.numChildren;
            for (int i = 0; i < count; i++)
            {
                GObject child = component.GetChildAt(i);
                if (child == null)
                    continue;

                string childIdxPath = string.IsNullOrWhiteSpace(idxPath) ? i.ToString() : idxPath + "/" + i;
                string segment = child.name ?? child.packageItem?.name ?? child.GetType().Name ?? "(null)";
                string childNamePath = string.IsNullOrWhiteSpace(namePath) ? segment : namePath + "/" + segment;

                AppendGObjectTree(builder, child, depth + 1, maxDepth, childIdxPath, childNamePath);
            }
        }

        private static bool IsEffectivelyVisible(GObject obj, int maxDepth = 32)
        {
            if (obj == null || obj._disposed)
                return false;

            GObject current = obj;
            int guard = 0;
            while (current != null && !current._disposed && guard++ < maxDepth)
            {
                try
                {
                    if (!current.visible)
                        return false;
                }
                catch
                {
                    return false;
                }

                current = current.parent;
            }

            return true;
        }

        private static void AppendControllerSnapshot(StringBuilder builder, GComponent component, int maxControllers = 16)
        {
            if (builder == null || component == null || component._disposed)
                return;

            var parts = new List<string>(4);
            for (int i = 0; i < maxControllers; i++)
            {
                Controller controller = null;
                try { controller = component.GetControllerAt(i); } catch { controller = null; }
                if (controller == null)
                    break;

                string name = string.IsNullOrWhiteSpace(controller.name) ? ("#" + i.ToString(CultureInfo.InvariantCulture)) : controller.name;
                string selectedPage = string.Empty;
                string selectedIndex = "?";

                try { selectedPage = controller.selectedPage ?? string.Empty; } catch { selectedPage = string.Empty; }
                try { selectedIndex = controller.selectedIndex.ToString(CultureInfo.InvariantCulture); } catch { selectedIndex = "?"; }

                parts.Add(name + "=" + selectedIndex + ":" + (string.IsNullOrWhiteSpace(selectedPage) ? "-" : selectedPage));
            }

            if (parts.Count > 0)
                builder.Append(" controllers=").Append(string.Join(",", parts));
        }

#if ANDROID || IOS
        private static void TrySyncSoftKeyboardWithFairyFocus()
        {
            Stage stage = _stage;
            if (stage == null)
                return;

            InputTextField focused = stage.focus as InputTextField;
            if (ReferenceEquals(focused, _lastSoftKeyboardFocus))
                return;

            _lastSoftKeyboardFocus = focused;

            if (focused == null)
            {
                if (CMain.SoftKeyboardOwnedByFairyGui)
                    CMain.RequestSoftKeyboard(false);

                return;
            }

            if (CMain.SoftKeyboardOwnedByFairyGui)
                CMain.RequestSoftKeyboard(false);

            CMain.RequestSoftKeyboard(true);
        }
#endif
    }
}
