using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Diagnostics;
using System.Threading.Tasks;
using MonoShare.MirControls;
using MonoShare.MirGraphics;
using MonoShare.MirNetwork;
using MonoShare.MirObjects;
using MonoShare.MirSounds;
using S = ServerPackets;
using C = ClientPackets;
using Effect = MonoShare.MirObjects.Effect;
//using MonoShare.MirScenes.Dialogs;
using System.Drawing.Imaging;
using FontStashSharp;
using Microsoft.Xna.Framework.Input;
using Microsoft.Xna.Framework.Graphics;
using MonoShare.Share.Extensions;
using MonoShare.UI;
//using MonoShare.Utils;

namespace MonoShare.MirScenes
{
    public sealed class GameScene : MirScene
    {
        public static GameScene Scene;

        public static UserObject User
        {
            get { return MapObject.User; }
            set { MapObject.User = value; }
        }

        public static long MoveTime, AttackTime, NextRunTime, LogTime, LastRunTime;
        public static bool CanMove, CanRun;

        public MapControl MapControl;

        private DateTime _nextFairyGuiMainHudEnsureUtc = DateTime.MinValue;

        private readonly Dictionary<string, Point> _mobileGroupMemberLocations = new Dictionary<string, Point>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _mobileGroupMemberMaps = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly List<MobilePendingGroundItem> _mobilePendingGroundItems = new List<MobilePendingGroundItem>(16);
        private static uint _nextMobilePendingGroundObjectId = uint.MaxValue;

        private bool _mobileGroupActive;
        private string _mobileGroupLeaderName = string.Empty;

        private sealed class MobilePendingGroundItem
        {
            public uint TempObjectId;
            public string Name;
            public ushort Image;
            public Point Location;
            public long CreatedAtMs;
            public long ExpireAtMs;
        }

        internal IReadOnlyDictionary<string, Point> MobileGroupMemberLocations => _mobileGroupMemberLocations;
        internal IReadOnlyDictionary<string, string> MobileGroupMemberMaps => _mobileGroupMemberMaps;
        internal bool MobileGroupActive => _mobileGroupActive;
        internal string MobileGroupLeaderName => _mobileGroupLeaderName ?? string.Empty;

        internal void SetMobileGroupActive(bool active)
        {
            _mobileGroupActive = active;
            if (!active)
                _mobileGroupLeaderName = string.Empty;
        }

        internal void SetMobileGroupLeaderName(string leaderName)
        {
            _mobileGroupLeaderName = leaderName?.Trim() ?? string.Empty;
        }

        internal void AssumeMobileGroupLeaderIsSelf()
        {
            string myName = User?.Name ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(myName))
                _mobileGroupLeaderName = myName;
        }

        private long _nextGuildMemberRefreshAllowedAtMs;
        private MarketPanelType _lastMobileMarketListingType = MarketPanelType.Consign;

        public bool IsMobileTradeActive =>
            Environment.OSVersion.Platform != PlatformID.Win32NT &&
            MonoShare.FairyGuiHost.IsMobileWindowVisible("Trade");
        //public MainDialog MainDialog;
        //public ChatDialog ChatDialog;
        //public ChatControlBar ChatControl;
        //public InventoryDialog InventoryDialog;
        //public CharacterDialog CharacterDialog;
        //public CraftDialog CraftDialog;
        //public StorageDialog StorageDialog;
        //public BeltDialog BeltDialog;
        //public MiniMapDialog MiniMapDialog;
        //public InspectDialog InspectDialog;
        //public OptionDialog OptionDialog;
        //public MenuDialog MenuDialog;
        //public NPCDialog NPCDialog;
        //public NPCGoodsDialog NPCGoodsDialog;
        //public NPCGoodsDialog NPCSubGoodsDialog;
        //public NPCGoodsDialog NPCCraftGoodsDialog;
        //public NPCDropDialog NPCDropDialog;
        //public NPCAwakeDialog NPCAwakeDialog;
        //public HelpDialog HelpDialog;
        //public MountDialog MountDialog;
        //public FishingDialog FishingDialog;
        //public FishingStatusDialog FishingStatusDialog;
        //public RefineDialog RefineDialog;

        //public GroupDialog GroupDialog;
        //public GuildDialog GuildDialog;

        //public BigMapDialog BigMapDialog;
        //public TrustMerchantDialog TrustMerchantDialog;
        //public CharacterDuraPanel CharacterDuraPanel;
        //public DuraStatusDialog DuraStatusPanel;
        //public TradeDialog TradeDialog;
        //public GuestTradeDialog GuestTradeDialog;

        //public CustomPanel1 CustomPanel1;
        //public SocketDialog SocketDialog;

        ////public SkillBarDialog SkillBarDialog;
        //public List<SkillBarDialog> SkillBarDialogs = new List<SkillBarDialog>();
        //public ChatOptionDialog ChatOptionDialog;
        //public ChatNoticeDialog ChatNoticeDialog;

        //public QuestListDialog QuestListDialog;
        //public QuestDetailDialog QuestDetailDialog;
        //public QuestDiaryDialog QuestLogDialog;
        //public QuestTrackingDialog QuestTrackingDialog;

        //public RankingDialog RankingDialog;

        //public MailListDialog MailListDialog;
        //public MailComposeLetterDialog MailComposeLetterDialog;
        //public MailComposeParcelDialog MailComposeParcelDialog;
        //public MailReadLetterDialog MailReadLetterDialog;
        //public MailReadParcelDialog MailReadParcelDialog;

        //public IntelligentCreatureDialog IntelligentCreatureDialog;
        //public IntelligentCreatureOptionsDialog IntelligentCreatureOptionsDialog;
        //public IntelligentCreatureOptionsGradeDialog IntelligentCreatureOptionsGradeDialog;

        //public FriendDialog FriendDialog;
        //public MemoDialog MemoDialog;
        //public RelationshipDialog RelationshipDialog;
        //public MentorDialog MentorDialog;
        //public GameShopDialog GameShopDialog;

        //public ReportDialog ReportDialog;

        //public ItemRentingDialog ItemRentingDialog;
        //public ItemRentDialog ItemRentDialog;
        //public GuestItemRentingDialog GuestItemRentingDialog;
        //public GuestItemRentDialog GuestItemRentDialog;
        //public ItemRentalDialog ItemRentalDialog;

        //public BuffDialog BuffsDialog;

        //public KeyboardLayoutDialog KeyboardLayoutDialog;
        //public NoticeDialog NoticeDialog;

        //public TimerDialog TimerControl;
        //public CompassDialog CompassControl;


        public static List<ItemInfo> ItemInfoList = new List<ItemInfo>();
        public static List<UserId> UserIdList = new List<UserId>();
        public static List<UserItem> ChatItemList = new List<UserItem>();
        public static List<ClientQuestInfo> QuestInfoList = new List<ClientQuestInfo>();
        public static List<GameShopItem> GameShopInfoList = new List<GameShopItem>();
        public static List<ClientRecipeInfo> RecipeInfoList = new List<ClientRecipeInfo>();

        public List<ClientBuff> Buffs = new List<ClientBuff>();

        public static UserItem[] Storage = new UserItem[80];
        public static UserItem[] GuildStorage = new UserItem[112];
        public static UserItem[] Refine = new UserItem[16];
        public static UserItem HoverItem, SelectedItem;
        //public static MirItemCell SelectedCell;

        public static bool PickedUpGold;
        public MirControl ItemLabel, MailLabel, MemoLabel, GuildBuffLabel;
        public static long UseItemTime, PickUpTime, DropViewTime, TargetDeadTime;
        public static uint Gold, Credit;
        public static long InspectTime;
        public bool ShowReviveMessage;


        public bool NewMail;
        public int NewMailCounter = 0;


        public AttackMode AMode;
        public PetMode PMode;
        public LightSetting Lights;

        public static long NPCTime;
        public static uint NPCID;
        public static float NPCRate;
        public static float NPCSellRate = 0.5F;
        public static uint DefaultNPCID;
        public static bool HideAddedStoreStats;

        public long ToggleTime;
        public static bool Slaying, Thrusting, HalfMoon, CrossHalfMoon, DoubleSlash, TwinDrakeBlade, FlamingSword;
        public static long SpellTime;

        //public MirLabel[] OutputLines = new MirLabel[10];
        public List<OutPutMessage> OutputMessages = new List<OutPutMessage>();

        public long OutputDelay;

        public GameScene()
        {
            if (Settings.LogErrors && Environment.OSVersion.Platform != PlatformID.Win32NT)
                CMain.SaveLog("进入地图：GameScene 构造开始（v20260328-asynclog）。");

            MapControl.AutoRun = false;
            MapControl.AutoHit = false;
            Slaying = false;
            Thrusting = false;
            HalfMoon = false;
            CrossHalfMoon = false;
            DoubleSlash = false;
            TwinDrakeBlade = false;
            FlamingSword = false;

            Scene = this;
            //BackColour = Color.Transparent;
            MoveTime = CMain.Time;

            if (Settings.LogErrors)
                CMain.SaveLog("进入地图：GameScene 已创建。");

            if (Environment.OSVersion.Platform != PlatformID.Win32NT)
                CMain.SaveLog("进入地图：开始初始化移动端 Overlay（FairyGUI 主界面）。");

            InitializeMobileOverlay();

            if (Environment.OSVersion.Platform != PlatformID.Win32NT)
                CMain.SaveLog("进入地图：移动端 Overlay 初始化调用已返回。");

            //KeyDown += GameScene_KeyDown;

            //MainDialog = new MainDialog { Parent = this };
            //ChatDialog = new ChatDialog { Parent = this };
            //ChatControl = new ChatControlBar { Parent = this };
            //InventoryDialog = new InventoryDialog { Parent = this };
            //CharacterDialog = new CharacterDialog { Parent = this, Visible = false };
            //BeltDialog = new BeltDialog { Parent = this };
            //StorageDialog = new StorageDialog { Parent = this, Visible = false };
            //CraftDialog = new CraftDialog { Parent = this, Visible = false };
            //MiniMapDialog = new MiniMapDialog { Parent = this };
            //InspectDialog = new InspectDialog { Parent = this, Visible = false };
            //OptionDialog = new OptionDialog { Parent = this, Visible = false };
            //MenuDialog = new MenuDialog { Parent = this, Visible = false };
            //NPCDialog = new NPCDialog { Parent = this, Visible = false };
            //NPCGoodsDialog = new NPCGoodsDialog(PanelType.Buy) { Parent = this, Visible = false };
            //NPCSubGoodsDialog = new NPCGoodsDialog(PanelType.BuySub) { Parent = this, Visible = false };
            //NPCCraftGoodsDialog = new NPCGoodsDialog(PanelType.Craft) { Parent = this, Visible = false };
            //NPCDropDialog = new NPCDropDialog { Parent = this, Visible = false };
            //NPCAwakeDialog = new NPCAwakeDialog { Parent = this, Visible = false };

            //HelpDialog = new HelpDialog { Parent = this, Visible = false };
            //KeyboardLayoutDialog = new KeyboardLayoutDialog { Parent = this, Visible = false };
            //NoticeDialog = new NoticeDialog { Parent = this, Visible = false };

            //MountDialog = new MountDialog { Parent = this, Visible = false };
            //FishingDialog = new FishingDialog { Parent = this, Visible = false };
            //FishingStatusDialog = new FishingStatusDialog { Parent = this, Visible = false };

            //GroupDialog = new GroupDialog { Parent = this, Visible = false };
            //GuildDialog = new GuildDialog { Parent = this, Visible = false };

            //BigMapDialog = new BigMapDialog { Parent = this, Visible = false };
            //TrustMerchantDialog = new TrustMerchantDialog { Parent = this, Visible = false };
            //CharacterDuraPanel = new CharacterDuraPanel { Parent = this, Visible = false };
            //DuraStatusPanel = new DuraStatusDialog { Parent = this, Visible = true };
            //TradeDialog = new TradeDialog { Parent = this, Visible = false };
            //GuestTradeDialog = new GuestTradeDialog { Parent = this, Visible = false };

            //CustomPanel1 = new CustomPanel1(this) { Visible = false };
            //SocketDialog = new SocketDialog { Parent = this, Visible = false };

            //SkillBarDialog Bar1 = new SkillBarDialog { Parent = this, Visible = false, BarIndex = 0 };
            //SkillBarDialogs.Add(Bar1);
            //SkillBarDialog Bar2 = new SkillBarDialog { Parent = this, Visible = false, BarIndex = 1 };
            //SkillBarDialogs.Add(Bar2);
            //ChatOptionDialog = new ChatOptionDialog { Parent = this, Visible = false };
            //ChatNoticeDialog = new ChatNoticeDialog { Parent = this, Visible = false };

            //QuestListDialog = new QuestListDialog { Parent = this, Visible = false };
            //QuestDetailDialog = new QuestDetailDialog { Parent = this, Visible = false };
            //QuestTrackingDialog = new QuestTrackingDialog { Parent = this, Visible = false };
            //QuestLogDialog = new QuestDiaryDialog { Parent = this, Visible = false };

            //RankingDialog = new RankingDialog { Parent = this, Visible = false };

            //MailListDialog = new MailListDialog { Parent = this, Visible = false };
            //MailComposeLetterDialog = new MailComposeLetterDialog { Parent = this, Visible = false };
            //MailComposeParcelDialog = new MailComposeParcelDialog { Parent = this, Visible = false };
            //MailReadLetterDialog = new MailReadLetterDialog { Parent = this, Visible = false };
            //MailReadParcelDialog = new MailReadParcelDialog { Parent = this, Visible = false };

            //IntelligentCreatureDialog = new IntelligentCreatureDialog { Parent = this, Visible = false };
            //IntelligentCreatureOptionsDialog = new IntelligentCreatureOptionsDialog { Parent = this, Visible = false };
            //IntelligentCreatureOptionsGradeDialog = new IntelligentCreatureOptionsGradeDialog { Parent = this, Visible = false };

            //RefineDialog = new RefineDialog { Parent = this, Visible = false };
            //RelationshipDialog = new RelationshipDialog { Parent = this, Visible = false };
            //FriendDialog = new FriendDialog { Parent = this, Visible = false };
            //MemoDialog = new MemoDialog { Parent = this, Visible = false };
            //MentorDialog = new MentorDialog { Parent = this, Visible = false };
            //GameShopDialog = new GameShopDialog { Parent = this, Visible = false };
            //ReportDialog = new ReportDialog { Parent = this, Visible = false };

            //ItemRentingDialog = new ItemRentingDialog { Parent = this, Visible = false };
            //ItemRentDialog = new ItemRentDialog { Parent = this, Visible = false };
            //GuestItemRentingDialog = new GuestItemRentingDialog { Parent = this, Visible = false };
            //GuestItemRentDialog = new GuestItemRentDialog { Parent = this, Visible = false };
            //ItemRentalDialog = new ItemRentalDialog { Parent = this, Visible = false };

            //BuffsDialog = new BuffDialog { Parent = this, Visible = true };
            //KeyboardLayoutDialog = new KeyboardLayoutDialog { Parent = this, Visible = false };

            //TimerControl = new TimerDialog { Parent = this, Visible = false };
            //CompassControl = new CompassDialog { Parent = this, Visible = false };

            //for (int i = 0; i < OutputLines.Length; i++)
            //    OutputLines[i] = new MirLabel
            //    {
            //        AutoSize = true,
            //        BackColour = Color.Transparent,
            //        Font = new Font(Settings.FontName, 8F),
            //        ForeColour = Color.LimeGreen,
            //        Location = new Point(5, 30 + i * 18),
            //        OutLine = true,
            //    };
        }

        private void InitializeMobileOverlay()
        {
            // 移动端彻底切换 FairyGUI：不再创建任何旧 Mobile* MirControl（线条/像素块绘制 UI）。
            if (Environment.OSVersion.Platform == PlatformID.Win32NT)
                return;

            TryEnsureFairyGuiMobileMainHud();
        }

        private void TryEnsureFairyGuiMobileMainHud()
        {
            if (Environment.OSVersion.Platform == PlatformID.Win32NT)
                return;

            if (MonoShare.FairyGuiHost.MobileMainHudAttached)
                return;

            if (!MonoShare.FairyGuiHost.PackagesLoaded)
                return;

            if (DateTime.UtcNow < _nextFairyGuiMainHudEnsureUtc)
                return;

            _nextFairyGuiMainHudEnsureUtc = DateTime.UtcNow.AddSeconds(2);

            if (!MonoShare.FairyGuiHost.TryAttachMobileMainHud())
            {
                string reason = MonoShare.FairyGuiHost.InitError ?? MonoShare.FairyGuiHost.LastPackageLoadError ?? string.Empty;
                reason = (reason ?? string.Empty).Trim();
                if (!string.IsNullOrWhiteSpace(reason))
                    CMain.SaveError("FairyGUI: 主界面 attach 失败：" + reason);
                else
                    CMain.SaveError("FairyGUI: 主界面 attach 失败。");
            }
        }

        public bool IsMobileOverlayBlockingJoystick()
        {
            if (Environment.OSVersion.Platform == PlatformID.Win32NT)
                return false;

            return MonoShare.FairyGuiHost.IsAnyMobileOverlayVisible;
        }

        internal void BeginNpcConversation(uint npcObjectId, string npcName)
        {
            if (Environment.OSVersion.Platform == PlatformID.Win32NT)
                return;

            MapControl?.CancelMagicLocationSelection(showMessage: false);

            if (MonoShare.FairyGuiHost.TryShowMobileWindowByKeywords("Npc", new[] { "DNpcDlg", "NewNpcDlg", "CustomNpcDlg", "NPC", "Npc", "对话", "Dialog", "Talk" }))
                MonoShare.FairyGuiHost.HideAllMobileWindowsExcept("Npc");
        }

        internal void ShowMobileQuestListOverlay(uint npcObjectId, string npcName)
        {
            if (Environment.OSVersion.Platform == PlatformID.Win32NT)
                return;

            MapControl?.CancelMagicLocationSelection(showMessage: false);

            MonoShare.FairyGuiHost.UpdateMobileQuestContext(npcObjectId, npcName);
            if (MonoShare.FairyGuiHost.TryShowMobileWindowByKeywords("Quest", new[] { "任务_DA2EWindow1UI", "任务", "Quest", "Diary" }))
                MonoShare.FairyGuiHost.HideAllMobileWindowsExcept("Quest");
        }

        internal void ShowMobileQuestDetailOverlay(uint npcObjectId, string npcName, ClientQuestProgress quest)
        {
            if (Environment.OSVersion.Platform == PlatformID.Win32NT)
                return;

            MapControl?.CancelMagicLocationSelection(showMessage: false);

            MonoShare.FairyGuiHost.UpdateMobileQuestContext(npcObjectId, npcName);
            MonoShare.FairyGuiHost.BeginMobileQuestDetail(quest);
            if (MonoShare.FairyGuiHost.TryShowMobileWindowByKeywords("Quest", new[] { "任务_DA2EWindow1UI", "任务", "Quest", "Diary" }))
                MonoShare.FairyGuiHost.HideAllMobileWindowsExcept("Quest");
        }

        internal void RefreshMobileQuestTrackingOverlay()
        {
            if (Environment.OSVersion.Platform == PlatformID.Win32NT)
                return;

            MonoShare.FairyGuiHost.MarkMobileQuestTrackingDirty();
        }

        internal void MobileReceiveChat(string message, ChatType type)
        {
            if (string.IsNullOrWhiteSpace(message))
                return;

            string cleaned = message;
            try
            {
                cleaned = RegexFunctions.CleanChatString(message);
            }
            catch
            {
                cleaned = message;
            }

            MonoShare.FairyGuiHost.AppendMobileChatMessage(cleaned, type);
        }

        public bool IsPointOverMobileHud(Microsoft.Xna.Framework.Vector2 position)
        {
            return MonoShare.FairyGuiHost.IsPointOverFairyGuiUI(position);
        }

        public bool IsPointOverMobileMiniMap(Microsoft.Xna.Framework.Vector2 position)
        {
            if (Environment.OSVersion.Platform == PlatformID.Win32NT)
                return false;

            return MonoShare.FairyGuiHost.IsPointOverMobileMainHudMiniMap(position);
        }

        public void ToggleMobileInventoryOverlay()
        {
            MapControl?.CancelMagicLocationSelection(showMessage: false);

            if (Environment.OSVersion.Platform == PlatformID.Win32NT)
                return;

            if (MonoShare.FairyGuiHost.TryToggleMobileWindowByKeywords("Inventory", new[] { "背包_DBagUI", "背包", "Bag", "Inventory" }, out bool nowVisible) && nowVisible)
                MonoShare.FairyGuiHost.HideAllMobileWindowsExcept("Inventory");
        }

        public void ToggleMobileMagicOverlay()
        {
            MapControl?.CancelMagicLocationSelection(showMessage: false);

            if (Environment.OSVersion.Platform == PlatformID.Win32NT)
                return;

            if (MonoShare.FairyGuiHost.TryToggleMobileWindowByKeywords("Magic", new[] { "SkillWinLJ", "Skill", "技能", "Magic" }, out bool nowVisible) && nowVisible)
                MonoShare.FairyGuiHost.HideAllMobileWindowsExcept("Magic");
        }

        public void ToggleMobileChatOverlay()
        {
            MapControl?.CancelMagicLocationSelection(showMessage: false);

            if (Environment.OSVersion.Platform == PlatformID.Win32NT)
                return;

            if (MonoShare.FairyGuiHost.TryToggleMobileWindowByKeywords("Chat", new[] { "聊天_DCharUI", "聊天", "Chat", "Char" }, out bool nowVisible) && nowVisible)
                MonoShare.FairyGuiHost.HideAllMobileWindowsExcept("Chat");
        }

        public void ToggleMobileGameShopOverlay()
        {
            MapControl?.CancelMagicLocationSelection(showMessage: false);

            if (Environment.OSVersion.Platform == PlatformID.Win32NT)
                return;

            if (MonoShare.FairyGuiHost.TryToggleMobileWindowByKeywords("Shop", new[] { "商店_DShopUI", "商店", "商城", "Shop", "Mall" }, out bool nowVisible) && nowVisible)
                MonoShare.FairyGuiHost.HideAllMobileWindowsExcept("Shop");
        }

        public void ToggleMobileSystemMenuOverlay()
        {
            MapControl?.CancelMagicLocationSelection(showMessage: false);

            if (Environment.OSVersion.Platform == PlatformID.Win32NT)
                return;

            if (MonoShare.FairyGuiHost.TryToggleMobileWindowByKeywords("System", new[] { "设置_DSetUI", "系统", "设置", "System", "Set" }, out bool nowVisible) && nowVisible)
                MonoShare.FairyGuiHost.HideAllMobileWindowsExcept("System");
        }

        public void ToggleMobileBigMapOverlay()
        {
            MapControl?.CancelMagicLocationSelection(showMessage: false);

            if (Environment.OSVersion.Platform == PlatformID.Win32NT)
                return;

            if (MonoShare.FairyGuiHost.TryToggleMobileWindowByKeywords("BigMap", new[] { "地图_DBigMapWindowUI", "大地图", "地图", "BigMap", "DBigMap", "Map" }, out bool nowVisible) && nowVisible)
                MonoShare.FairyGuiHost.HideAllMobileWindowsExcept("BigMap");
        }

        public void ToggleMobileStateOverlay()
        {
            MapControl?.CancelMagicLocationSelection(showMessage: false);

            if (Environment.OSVersion.Platform == PlatformID.Win32NT)
                return;

            bool toggled = MonoShare.FairyGuiHost.TryToggleMobileWindowByKeywords(
                "State",
                new[] { "UI/角色_DStateUI", "角色_DStateUI", "DStateUI", "UIRes/StateWinLJ", "StateWinLJ", "StateWin", "角色", "State" },
                out bool nowVisible);

            if (!toggled)
            {
                CMain.SaveError("FairyGUI: 打开角色状态窗口失败（State）。可在 Mir2Config.ini [FairyGUI] MobileWindow.State=UI/角色_DStateUI 覆盖指定组件。");
                return;
            }

            if (nowVisible)
            {
                MonoShare.FairyGuiHost.HideAllMobileWindowsExcept("State");
            }
        }

        public void ToggleMobileGuildOverlay()
        {
            MapControl?.CancelMagicLocationSelection(showMessage: false);

            if (Environment.OSVersion.Platform == PlatformID.Win32NT)
                return;

            if (MonoShare.FairyGuiHost.TryToggleMobileWindowByKeywords("Guild", new[] { "工会_WinGuild_MainUI", "工会", "Guild", "WinGuild" }, out bool nowVisible) && nowVisible)
                MonoShare.FairyGuiHost.HideAllMobileWindowsExcept("Guild");
        }

        public void ToggleMobileFriendOverlay()
        {
            MapControl?.CancelMagicLocationSelection(showMessage: false);

            if (Environment.OSVersion.Platform == PlatformID.Win32NT)
                return;

            if (MonoShare.FairyGuiHost.TryToggleMobileWindowByKeywords("Friend", new[] { "好友_Friends_MainUI", "好友", "Friend", "Friends" }, out bool nowVisible) && nowVisible)
                MonoShare.FairyGuiHost.HideAllMobileWindowsExcept("Friend");
        }

        public void ToggleMobileGroupOverlay()
        {
            MapControl?.CancelMagicLocationSelection(showMessage: false);

            if (Environment.OSVersion.Platform == PlatformID.Win32NT)
                return;

            if (MonoShare.FairyGuiHost.TryToggleMobileWindowByKeywords("Group", new[] { "组队_DWindow_MainUI", "组队", "队伍", "Group", "Team" }, out bool nowVisible) && nowVisible)
                MonoShare.FairyGuiHost.HideAllMobileWindowsExcept("Group");
        }

        public void ToggleMobileRelationshipOverlay()
        {
            MapControl?.CancelMagicLocationSelection(showMessage: false);

            if (Environment.OSVersion.Platform == PlatformID.Win32NT)
                return;

            if (MonoShare.FairyGuiHost.TryToggleMobileWindowByOverrideSpecOnly("Relationship", out bool nowVisible))
            {
                if (nowVisible)
                    MonoShare.FairyGuiHost.HideAllMobileWindowsExcept("Relationship");
                return;
            }

            OutputMessage("当前 FairyGUI publish 未包含【关系】窗口，请在 UI 工程补做后重新发布，或在 Mir2Config.ini [FairyGUI] MobileWindow.Relationship=UI/组件名 覆盖指定。");
        }

        public void ToggleMobileMentorOverlay()
        {
            MapControl?.CancelMagicLocationSelection(showMessage: false);

            if (Environment.OSVersion.Platform == PlatformID.Win32NT)
                return;

            if (MonoShare.FairyGuiHost.TryToggleMobileWindowByOverrideSpecOnly("Mentor", out bool nowVisible))
            {
                if (nowVisible)
                    MonoShare.FairyGuiHost.HideAllMobileWindowsExcept("Mentor");
                return;
            }

            OutputMessage("当前 FairyGUI publish 未包含【师徒】窗口，请在 UI 工程补做后重新发布，或在 Mir2Config.ini [FairyGUI] MobileWindow.Mentor=UI/组件名 覆盖指定。");
        }
 
        internal void ShowMobileGroupOverlay()
        {
            MapControl?.CancelMagicLocationSelection(showMessage: false);

            if (Environment.OSVersion.Platform == PlatformID.Win32NT)
                return;

            if (MonoShare.FairyGuiHost.TryShowMobileWindowByKeywords("Group", new[] { "组队_DWindow_MainUI", "组队", "队伍", "Group", "Team" }))
                MonoShare.FairyGuiHost.HideAllMobileWindowsExcept("Group");
        }
 
        public void ToggleMobileMailOverlay()
        {
            MapControl?.CancelMagicLocationSelection(showMessage: false);

            if (Environment.OSVersion.Platform == PlatformID.Win32NT)
                return;

            if (MonoShare.FairyGuiHost.TryToggleMobileWindowByKeywords("Mail", new[] { "MailWinStay1", "MailWinStay2", "邮件", "信件", "Mail", "Inbox" }, out bool nowVisible) && nowVisible)
            {
                MonoShare.FairyGuiHost.HideAllMobileWindowsExcept("Mail");
                MonoShare.FairyGuiHost.MarkMobileMailDirty();
            }
        }

        internal void ShowMobileMailReadOverlay(ulong mailId)
        {
            MapControl?.CancelMagicLocationSelection(showMessage: false);

            if (Environment.OSVersion.Platform == PlatformID.Win32NT)
                return;

            MonoShare.FairyGuiHost.BeginMobileMailRead(mailId);

            if (MonoShare.FairyGuiHost.TryShowMobileWindowByKeywords("Mail", new[] { "MailWinStay1", "MailWinStay2", "邮件", "信件", "Mail", "Inbox" }))
                MonoShare.FairyGuiHost.HideAllMobileWindowsExcept("Mail");
        }

        internal void ShowMobileMailComposeOverlay(string recipientName, bool preferParcel)
        {
            MapControl?.CancelMagicLocationSelection(showMessage: false);

            if (Environment.OSVersion.Platform == PlatformID.Win32NT)
                return;

            MonoShare.FairyGuiHost.BeginMobileMailCompose(recipientName, preferParcel);

            if (MonoShare.FairyGuiHost.TryShowMobileWindowByKeywords("Mail", new[] { "MailWinStay1", "MailWinStay2", "邮件", "信件", "Mail", "Inbox" }))
                MonoShare.FairyGuiHost.HideAllMobileWindowsExcept("Mail");
        }

        internal void BeginMobileMailAttachmentSelection(int slotIndex)
        {
            MapControl?.CancelMagicLocationSelection(showMessage: false);

            if (Environment.OSVersion.Platform == PlatformID.Win32NT)
                return;

            MonoShare.FairyGuiHost.BeginMobileMailAttachmentSelection(slotIndex);
            if (MonoShare.FairyGuiHost.TryShowMobileWindowByKeywords("Inventory", new[] { "背包_DBagUI", "背包", "Bag", "Inventory" }))
                MonoShare.FairyGuiHost.HideAllMobileWindowsExcept("Inventory");
        }

        internal void HandleMobileMailAttachmentSelected(int slotIndex, ulong uniqueId)
        {
            if (Environment.OSVersion.Platform == PlatformID.Win32NT)
                return;

            MonoShare.FairyGuiHost.HandleMobileMailAttachmentSelected(slotIndex, uniqueId);

            if (MonoShare.FairyGuiHost.TryShowMobileWindowByKeywords("Mail", new[] { "MailWinStay1", "MailWinStay2", "邮件", "信件", "Mail", "Inbox" }))
                MonoShare.FairyGuiHost.HideAllMobileWindowsExcept("Mail");
        }

        internal void CancelMobileMailAttachmentSelection()
        {
            if (Environment.OSVersion.Platform == PlatformID.Win32NT)
                return;

            MonoShare.FairyGuiHost.CancelMobileMailAttachmentSelection();

            if (MonoShare.FairyGuiHost.TryShowMobileWindowByKeywords("Mail", new[] { "MailWinStay1", "MailWinStay2", "邮件", "信件", "Mail", "Inbox" }))
                MonoShare.FairyGuiHost.HideAllMobileWindowsExcept("Mail");
        }

        internal void BackToMobileMailList(ulong focusMailId)
        {
            MapControl?.CancelMagicLocationSelection(showMessage: false);

            if (Environment.OSVersion.Platform == PlatformID.Win32NT)
                return;

            MonoShare.FairyGuiHost.BeginMobileMailRead(focusMailId);

            if (MonoShare.FairyGuiHost.TryShowMobileWindowByKeywords("Mail", new[] { "MailWinStay1", "MailWinStay2", "邮件", "信件", "Mail", "Inbox" }))
                MonoShare.FairyGuiHost.HideAllMobileWindowsExcept("Mail");
        }

        internal void ShowMobileFriendOverlayPreserveState()
        {
            if (Environment.OSVersion.Platform == PlatformID.Win32NT)
                return;

            MonoShare.FairyGuiHost.TryShowMobileWindowByKeywords("Friend", new[] { "好友_Friends_MainUI", "好友", "Friend", "Friends" });
        }

        internal void BeginWhisperTo(string target)
        {
            if (string.IsNullOrWhiteSpace(target))
                return;

            MapControl?.CancelMagicLocationSelection(showMessage: false);

            if (Environment.OSVersion.Platform == PlatformID.Win32NT)
                return;

            MonoShare.FairyGuiHost.BeginMobileChatWhisperTo(target);
            if (MonoShare.FairyGuiHost.TryShowMobileWindowByKeywords("Chat", new[] { "聊天_DCharUI", "聊天", "Chat", "Char" }))
                MonoShare.FairyGuiHost.HideAllMobileWindowsExcept("Chat");
        }

        internal void PromptMobileText(string title, string message, string initialText, int maxLength, Action<string> onOk, Action onCancel = null, bool numericOnly = false)
        {
            MapControl?.CancelMagicLocationSelection(showMessage: false);

            if (Environment.OSVersion.Platform == PlatformID.Win32NT)
                return;

            if (onOk == null)
                return;

            string safeTitle = string.IsNullOrWhiteSpace(title) ? "输入" : title.Trim();
            string safeMessage = message ?? string.Empty;
            string safeInitialText = initialText ?? string.Empty;

            if (maxLength > 0 && safeInitialText.Length > maxLength)
                safeInitialText = safeInitialText.Substring(0, maxLength);

            try
            {
                if (!MonoShare.FairyGuiHost.TryShowMobileTextPrompt(
                        title: safeTitle,
                        message: safeMessage,
                        initialText: safeInitialText,
                        maxLength: maxLength,
                        onOk: onOk,
                        onCancel: onCancel,
                        numericOnly: numericOnly))
                {
                    CMain.SaveError("FairyGUI: PromptMobileText 创建提示窗口失败。");
                    onCancel?.Invoke();
                }
            }
            catch (Exception ex)
            {
                CMain.SaveError("FairyGUI: PromptMobileText 异常：" + ex);
            }
        }

        internal void BeginMobileMarketListing(MarketPanelType type)
        {
            _lastMobileMarketListingType = type;

            MapControl?.CancelMagicLocationSelection(showMessage: false);

            if (Environment.OSVersion.Platform == PlatformID.Win32NT)
                return;

            MonoShare.FairyGuiHost.BeginMobileMarketListingSelection(type);

            if (MonoShare.FairyGuiHost.TryShowMobileWindowByKeywords("Inventory", new[] { "背包_DBagUI", "背包", "Bag", "Inventory" }))
                MonoShare.FairyGuiHost.HideAllMobileWindowsExcept("Inventory");
        }

        internal void NotifyMobileMarketListingRequested(MarketPanelType type)
        {
            _lastMobileMarketListingType = type;
        }
 
        private void UpdateMouseCursor()
        {
            if (!Settings.UseMouseCursors) return;

            //if (GameScene.HoverItem != null)
            //{
            //    if (GameScene.SelectedCell != null && GameScene.SelectedCell.Item != null && GameScene.SelectedCell.Item.Info.Type == ItemType.Gem && CMain.Ctrl)
            //    {
            //        CMain.SetMouseCursor(MouseCursor.Upgrade);
            //    }
            //    else
            //    {
            //        CMain.SetMouseCursor(MouseCursor.Default);
            //    }
            //}
            //else if (MapObject.MouseObject != null)
            //{
            //    switch (MapObject.MouseObject.Race)
            //    {
            //        case ObjectType.Monster:
            //            CMain.SetMouseCursor(MouseCursor.Attack);
            //            break;
            //        case ObjectType.Merchant:
            //            CMain.SetMouseCursor(MouseCursor.NPCTalk);
            //            break;
            //        case ObjectType.Player:
            //            if (CMain.Shift)
            //            {
            //                CMain.SetMouseCursor(MouseCursor.AttackRed);
            //            }
            //            else
            //            {
            //                CMain.SetMouseCursor(MouseCursor.Default);
            //            }
            //            break;
            //        default:
            //            CMain.SetMouseCursor(MouseCursor.Default);
            //            break;
            //    }
            //}
            //else
            //{
            //    CMain.SetMouseCursor(MouseCursor.Default);
            //}

        }

        public void OutputMessage(string message, OutputMessageType type = OutputMessageType.Normal)
        {
            OutputMessages.Add(new OutPutMessage { Message = message, ExpireTime = CMain.Time + 5000, Type = type });
            if (OutputMessages.Count > 10)
                OutputMessages.RemoveAt(0);

            // 移动端：把“输出消息”同步到 HUD 消息栏，保证能看到服务端提示/系统信息。
            if (Environment.OSVersion.Platform != PlatformID.Win32NT)
            {
                try
                {
                    ChatType chatType = ChatType.Hint;
                    switch (type)
                    {
                        case OutputMessageType.Quest:
                            chatType = ChatType.System;
                            break;
                        case OutputMessageType.Guild:
                            chatType = ChatType.Guild;
                            break;
                        default:
                            chatType = ChatType.Hint;
                            break;
                    }

                    MobileReceiveChat(message, chatType);
                }
                catch
                {
                }
            }
        }

        private void ProcessOuput()
        {
            for (int i = 0; i < OutputMessages.Count; i++)
            {
                if (CMain.Time >= OutputMessages[i].ExpireTime)
                    OutputMessages.RemoveAt(i);
            }

            //for (int i = 0; i < OutputLines.Length; i++)
            //{
            //    if (OutputMessages.Count > i)
            //    {
            //        Color color;
            //        switch (OutputMessages[i].Type)
            //        {
            //            case OutputMessageType.Quest:
            //                color = Color.Gold;
            //                break;
            //            case OutputMessageType.Guild:
            //                color = Color.DeepPink;
            //                break;
            //            default:
            //                color = Color.LimeGreen;
            //                break;
            //        }

            //        OutputLines[i].Text = OutputMessages[i].Message;
            //        OutputLines[i].ForeColour = color;
            //        OutputLines[i].Visible = true;
            //    }
            //    else
            //    {
            //        OutputLines[i].Text = string.Empty;
            //        OutputLines[i].Visible = false;
            //    }
            //}
        }
        //private void GameScene_KeyDown(object sender, KeyEventArgs e)
        //{
        //    if (GameScene.Scene.KeyboardLayoutDialog.WaitingForBind != null)
        //    {
        //        GameScene.Scene.KeyboardLayoutDialog.CheckNewInput(e);
        //        return;
        //    }

        //    foreach (KeyBind KeyCheck in CMain.InputKeys.Keylist)
        //    {
        //        if (KeyCheck.Key == Keys.None)
        //            continue;
        //        if (KeyCheck.Key != e.KeyCode)
        //            continue;
        //        if ((e.KeyCode == Keys.D1 || e.KeyCode == Keys.D2) && ((e.Modifiers & Keys.Shift) != 0))
        //            continue;
        //        if ((KeyCheck.RequireAlt != 2) && (KeyCheck.RequireAlt != (CMain.Alt ? 1 : 0)))
        //            continue;
        //        if ((KeyCheck.RequireShift != 2) && (KeyCheck.RequireShift != (CMain.Shift ? 1 : 0)))
        //            continue;
        //        if ((KeyCheck.RequireCtrl != 2) && (KeyCheck.RequireCtrl != (CMain.Ctrl ? 1 : 0)))
        //            continue;
        //        if ((KeyCheck.RequireTilde != 2) && (KeyCheck.RequireTilde != (CMain.Tilde ? 1 : 0)))
        //            continue;
        //        //now run the real code
        //        switch (KeyCheck.function)
        //        {
        //            case KeybindOptions.Bar1Skill1: UseSpell(1); break;
        //            case KeybindOptions.Bar1Skill2: UseSpell(2); break;
        //            case KeybindOptions.Bar1Skill3: UseSpell(3); break;
        //            case KeybindOptions.Bar1Skill4: UseSpell(4); break;
        //            case KeybindOptions.Bar1Skill5: UseSpell(5); break;
        //            case KeybindOptions.Bar1Skill6: UseSpell(6); break;
        //            case KeybindOptions.Bar1Skill7: UseSpell(7); break;
        //            case KeybindOptions.Bar1Skill8: UseSpell(8); break;
        //            case KeybindOptions.Bar2Skill1: UseSpell(9); break;
        //            case KeybindOptions.Bar2Skill2: UseSpell(10); break;
        //            case KeybindOptions.Bar2Skill3: UseSpell(11); break;
        //            case KeybindOptions.Bar2Skill4: UseSpell(12); break;
        //            case KeybindOptions.Bar2Skill5: UseSpell(13); break;
        //            case KeybindOptions.Bar2Skill6: UseSpell(14); break;
        //            case KeybindOptions.Bar2Skill7: UseSpell(15); break;
        //            case KeybindOptions.Bar2Skill8: UseSpell(16); break;
        //            case KeybindOptions.Inventory:
        //            case KeybindOptions.Inventory2:
        //                if (!InventoryDialog.Visible) InventoryDialog.Show();
        //                else InventoryDialog.Hide();
        //                break;
        //            case KeybindOptions.Equipment:
        //            case KeybindOptions.Equipment2:
        //                if (!CharacterDialog.Visible || !CharacterDialog.CharacterPage.Visible)
        //                {
        //                    CharacterDialog.Show();
        //                    CharacterDialog.ShowCharacterPage();
        //                }
        //                else CharacterDialog.Hide();
        //                break;
        //            case KeybindOptions.Skills:
        //            case KeybindOptions.Skills2:
        //                if (!CharacterDialog.Visible || !CharacterDialog.SkillPage.Visible)
        //                {
        //                    CharacterDialog.Show();
        //                    CharacterDialog.ShowSkillPage();
        //                }
        //                else CharacterDialog.Hide();
        //                break;
        //            case KeybindOptions.Creature:
        //                if (!IntelligentCreatureDialog.Visible) IntelligentCreatureDialog.Show();
        //                else IntelligentCreatureDialog.Hide();
        //                break;
        //            case KeybindOptions.MountWindow:
        //                if (!MountDialog.Visible) MountDialog.Show();
        //                else MountDialog.Hide();
        //                break;

        //            case KeybindOptions.GameShop:
        //                if (!GameShopDialog.Visible) GameShopDialog.Show();
        //                else GameShopDialog.Hide();
        //                break;
        //            case KeybindOptions.Fishing:
        //                if (!FishingDialog.Visible) FishingDialog.Show();
        //                else FishingDialog.Hide();
        //                break;
        //            case KeybindOptions.Skillbar:
        //                if (!Settings.SkillBar)
        //                    foreach (SkillBarDialog Bar in SkillBarDialogs)
        //                        Bar.Show();
        //                else
        //                    foreach (SkillBarDialog Bar in SkillBarDialogs)
        //                        Bar.Hide();
        //                break;
        //            case KeybindOptions.Mount:
        //                if (GameScene.Scene.MountDialog.CanRide())
        //                    GameScene.Scene.MountDialog.Ride();
        //                break;
        //            case KeybindOptions.Mentor:
        //                if (!MentorDialog.Visible) MentorDialog.Show();
        //                else MentorDialog.Hide();
        //                break;
        //            case KeybindOptions.Relationship:
        //                if (!RelationshipDialog.Visible) RelationshipDialog.Show();
        //                else RelationshipDialog.Hide();
        //                break;
        //            case KeybindOptions.Friends:
        //                if (!FriendDialog.Visible) FriendDialog.Show();
        //                else FriendDialog.Hide();
        //                break;
        //            case KeybindOptions.Guilds:
        //                if (!GuildDialog.Visible) GuildDialog.Show();
        //                else
        //                {
        //                    GuildDialog.Hide();
        //                }
        //                break;

        //            case KeybindOptions.Ranking:
        //                if (!RankingDialog.Visible) RankingDialog.Show();
        //                else RankingDialog.Hide();
        //                break;
        //            case KeybindOptions.Quests:
        //                if (!QuestLogDialog.Visible) QuestLogDialog.Show();
        //                else QuestLogDialog.Hide();
        //                break;
        //            case KeybindOptions.Exit:
        //                QuitGame();
        //                return;

        //            case KeybindOptions.Closeall:
        //                InventoryDialog.Hide();
        //                CharacterDialog.Hide();
        //                OptionDialog.Hide();
        //                MenuDialog.Hide();
        //                if (NPCDialog.Visible) NPCDialog.Hide();
        //                HelpDialog.Hide();
        //                KeyboardLayoutDialog.Hide();
        //                RankingDialog.Hide();
        //                IntelligentCreatureDialog.Hide();
        //                IntelligentCreatureOptionsDialog.Hide();
        //                IntelligentCreatureOptionsGradeDialog.Hide();
        //                MountDialog.Hide();
        //                FishingDialog.Hide();
        //                FriendDialog.Hide();
        //                RelationshipDialog.Hide();
        //                MentorDialog.Hide();
        //                GameShopDialog.Hide();
        //                GroupDialog.Hide();
        //                GuildDialog.Hide();
        //                InspectDialog.Hide();
        //                StorageDialog.Hide();
        //                TrustMerchantDialog.Hide();
        //                //CharacterDuraPanel.Hide();
        //                QuestListDialog.Hide();
        //                QuestDetailDialog.Hide();
        //                QuestLogDialog.Hide();
        //                NPCAwakeDialog.Hide();
        //                RefineDialog.Hide();
        //                BigMapDialog.Hide();
        //                if (FishingStatusDialog.bEscExit) FishingStatusDialog.Cancel();
        //                MailComposeLetterDialog.Hide();
        //                MailComposeParcelDialog.Hide();
        //                MailListDialog.Hide();
        //                MailReadLetterDialog.Hide();
        //                MailReadParcelDialog.Hide();
        //                ItemRentalDialog.Hide();
        //                NoticeDialog.Hide();



        //                GameScene.Scene.DisposeItemLabel();
        //                break;
        //            case KeybindOptions.Options:
        //            case KeybindOptions.Options2:
        //                if (!OptionDialog.Visible) OptionDialog.Show();
        //                else OptionDialog.Hide();
        //                break;
        //            case KeybindOptions.Group:
        //                if (!GroupDialog.Visible) GroupDialog.Show();
        //                else GroupDialog.Hide();
        //                break;
        //            case KeybindOptions.Belt:
        //                if (!BeltDialog.Visible) BeltDialog.Show();
        //                else BeltDialog.Hide();
        //                break;
        //            case KeybindOptions.BeltFlip:
        //                BeltDialog.Flip();
        //                break;
        //            case KeybindOptions.Pickup:
        //                if (CMain.Time > PickUpTime)
        //                {
        //                    PickUpTime = CMain.Time + 200;
        //                    Network.Enqueue(new C.PickUp());
        //                }
        //                break;
        //            case KeybindOptions.Belt1:
        //            case KeybindOptions.Belt1Alt:
        //                BeltDialog.Grid[0].UseItem();
        //                break;
        //            case KeybindOptions.Belt2:
        //            case KeybindOptions.Belt2Alt:
        //                BeltDialog.Grid[1].UseItem();
        //                break;
        //            case KeybindOptions.Belt3:
        //            case KeybindOptions.Belt3Alt:
        //                BeltDialog.Grid[2].UseItem();
        //                break;
        //            case KeybindOptions.Belt4:
        //            case KeybindOptions.Belt4Alt:
        //                BeltDialog.Grid[3].UseItem();
        //                break;
        //            case KeybindOptions.Belt5:
        //            case KeybindOptions.Belt5Alt:
        //                BeltDialog.Grid[4].UseItem();
        //                break;
        //            case KeybindOptions.Belt6:
        //            case KeybindOptions.Belt6Alt:
        //                BeltDialog.Grid[5].UseItem();
        //                break;
        //            case KeybindOptions.Logout:
        //                LogOut();
        //                break;
        //            case KeybindOptions.Minimap:
        //                MiniMapDialog.Toggle();
        //                break;
        //            case KeybindOptions.Bigmap:
        //                BigMapDialog.Toggle();
        //                break;
        //            case KeybindOptions.Trade:
        //                Network.Enqueue(new C.TradeRequest());
        //                break;
        //            case KeybindOptions.Rental:
        //                ItemRentalDialog.Toggle();
        //                break;
        //            case KeybindOptions.ChangePetmode:
        //                ChangePetMode();
        //                break;
        //            case KeybindOptions.PetmodeBoth:
        //                Network.Enqueue(new C.ChangePMode { Mode = PetMode.Both });
        //                return;
        //            case KeybindOptions.PetmodeMoveonly:
        //                Network.Enqueue(new C.ChangePMode { Mode = PetMode.MoveOnly });
        //                return;
        //            case KeybindOptions.PetmodeAttackonly:
        //                Network.Enqueue(new C.ChangePMode { Mode = PetMode.AttackOnly });
        //                return;
        //            case KeybindOptions.PetmodeNone:
        //                Network.Enqueue(new C.ChangePMode { Mode = PetMode.None });
        //                return;
        //            case KeybindOptions.CreatureAutoPickup://semiauto!
        //                Network.Enqueue(new C.IntelligentCreaturePickup { MouseMode = false, Location = MapControl.MapLocation });
        //                break;
        //            case KeybindOptions.CreaturePickup:
        //                Network.Enqueue(new C.IntelligentCreaturePickup { MouseMode = true, Location = MapControl.MapLocation });
        //                break;
        //            case KeybindOptions.ChangeAttackmode:
        //                ChangeAttackMode();
        //                break;
        //            case KeybindOptions.AttackmodePeace:
        //                Network.Enqueue(new C.ChangeAMode { Mode = AttackMode.Peace });
        //                return;
        //            case KeybindOptions.AttackmodeGroup:
        //                Network.Enqueue(new C.ChangeAMode { Mode = AttackMode.Group });
        //                return;
        //            case KeybindOptions.AttackmodeGuild:
        //                Network.Enqueue(new C.ChangeAMode { Mode = AttackMode.Guild });
        //                return;
        //            case KeybindOptions.AttackmodeEnemyguild:
        //                Network.Enqueue(new C.ChangeAMode { Mode = AttackMode.EnemyGuild });
        //                return;
        //            case KeybindOptions.AttackmodeRedbrown:
        //                Network.Enqueue(new C.ChangeAMode { Mode = AttackMode.RedBrown });
        //                return;
        //            case KeybindOptions.AttackmodeAll:
        //                Network.Enqueue(new C.ChangeAMode { Mode = AttackMode.All });
        //                return;

        //            case KeybindOptions.Help:
        //                if (!HelpDialog.Visible) HelpDialog.Show();
        //                else HelpDialog.Hide();
        //                break;
        //            case KeybindOptions.Keybind:
        //                if (!KeyboardLayoutDialog.Visible) KeyboardLayoutDialog.Show();
        //                else KeyboardLayoutDialog.Hide();
        //                break;
        //            case KeybindOptions.Autorun:
        //                MapControl.AutoRun = !MapControl.AutoRun;
        //                break;
        //            case KeybindOptions.Cameramode:

        //                if (!MainDialog.Visible)
        //                {
        //                    MainDialog.Show();
        //                    ChatDialog.Show();
        //                    BeltDialog.Show();
        //                    ChatControl.Show();
        //                    MiniMapDialog.Show();
        //                    CharacterDuraPanel.Show();
        //                    DuraStatusPanel.Show();
        //                }
        //                else
        //                {
        //                    MainDialog.Hide();
        //                    ChatDialog.Hide();
        //                    BeltDialog.Hide();
        //                    ChatControl.Hide();
        //                    MiniMapDialog.Hide();
        //                    CharacterDuraPanel.Hide();
        //                    DuraStatusPanel.Hide();
        //                }
        //                break;
        //            case KeybindOptions.DropView:
        //                if (CMain.Time > DropViewTime)
        //                    DropViewTime = CMain.Time + 5000;
        //                break;
        //            case KeybindOptions.TargetDead:
        //                if (CMain.Time > TargetDeadTime)
        //                    TargetDeadTime = CMain.Time + 5000;
        //                break;
        //            case KeybindOptions.AddGroupMember:
        //                if (MapObject.MouseObject == null) break;
        //                if (MapObject.MouseObject.Race != ObjectType.Player) break;

        //                GameScene.Scene.GroupDialog.AddMember(MapObject.MouseObject.Name);
        //                break;
        //        }
        //    }
        //}

        //public void ChangeSkillMode(bool? ctrl)
        //{
        //    if (Settings.SkillMode || ctrl == true)
        //    {
        //        Settings.SkillMode = false;
        //        GameScene.Scene.ChatDialog.ReceiveChat("[技能模式 Ctrl]", ChatType.Hint);
        //        GameScene.Scene.OptionDialog.ToggleSkillButtons(true);
        //    }
        //    else if (!Settings.SkillMode || ctrl == false)
        //    {
        //        Settings.SkillMode = true;
        //        GameScene.Scene.ChatDialog.ReceiveChat("[技能模式 ~]", ChatType.Hint);
        //        GameScene.Scene.OptionDialog.ToggleSkillButtons(false);
        //    }
        //}

        public void ChangePetMode()
        {
            switch (PMode)
            {
                case PetMode.Both:
                    Network.Enqueue(new C.ChangePMode { Mode = PetMode.MoveOnly });
                    return;
                case PetMode.MoveOnly:
                    Network.Enqueue(new C.ChangePMode { Mode = PetMode.AttackOnly });
                    return;
                case PetMode.AttackOnly:
                    Network.Enqueue(new C.ChangePMode { Mode = PetMode.None });
                    return;
                case PetMode.None:
                    Network.Enqueue(new C.ChangePMode { Mode = PetMode.Both });
                    return;
            }
        }

        public void ChangeAttackMode()
        {
            switch (AMode)
            {
                case AttackMode.Peace:
                    Network.Enqueue(new C.ChangeAMode { Mode = AttackMode.Group });
                    return;
                case AttackMode.Group:
                    Network.Enqueue(new C.ChangeAMode { Mode = AttackMode.Guild });
                    return;
                case AttackMode.Guild:
                    Network.Enqueue(new C.ChangeAMode { Mode = AttackMode.EnemyGuild });
                    return;
                case AttackMode.EnemyGuild:
                    Network.Enqueue(new C.ChangeAMode { Mode = AttackMode.RedBrown });
                    return;
                case AttackMode.RedBrown:
                    Network.Enqueue(new C.ChangeAMode { Mode = AttackMode.All });
                    return;
                case AttackMode.All:
                    Network.Enqueue(new C.ChangeAMode { Mode = AttackMode.Peace });
                    return;
            }
        }

        public void UseSpell(int key, bool fromUI = false)
        {
            if (User.RidingMount || User.Fishing) return;

            if (!User.HasClassWeapon && User.Weapon >= 0)
            {
                //ChatDialog.ReceiveChat("你必须佩戴合适的武器才能完成这项技能", ChatType.System);
                return;
            }

            if (CMain.Time < User.BlizzardStopTime || CMain.Time < User.ReincarnationStopTime) return;

            ClientMagic magic = null;

            for (int i = 0; i < User.Magics.Count; i++)
            {
                if (User.Magics[i].Key != key) continue;
                magic = User.Magics[i];
                break;
            }

            if (magic == null) return;

            if (fromUI && MapControl != null && MapControl.HasPendingMagicLocation)
            {
                if (MapControl.IsMagicLocationSelectionFor(magic.Spell))
                {
                    MapControl.CancelMagicLocationSelection();
                    return;
                }

                MapControl.CancelMagicLocationSelection(showMessage: false);
            }

            switch (magic.Spell)
            {
                case Spell.CounterAttack:
                    if ((CMain.Time < magic.CastTime + magic.Delay) && magic.CastTime != 0)
                    {
                        if (CMain.Time >= OutputDelay)
                        {
                            OutputDelay = CMain.Time + 1000;
                            GameScene.Scene.OutputMessage(string.Format("你不能释放技能{0}需要等待{1}秒.", magic.Name.ToString(), ((magic.CastTime + magic.Delay) - CMain.Time - 1) / 1000 + 1));
                        }

                        return;
                    }
                    magic.CastTime = CMain.Time;
                    break;
            }

            int cost;
            switch (magic.Spell)
            {
                case Spell.Fencing:
                case Spell.FatalSword:
                case Spell.MPEater:
                case Spell.Hemorrhage:
                case Spell.SpiritSword:
                case Spell.Slaying:
                case Spell.Focus:
                case Spell.Meditation:
                    return;
                case Spell.Thrusting:
                    if (CMain.Time < ToggleTime) return;
                    Thrusting = !Thrusting;
                    //ChatDialog.ReceiveChat(Thrusting ? "启用刺杀剑术." : "关闭刺杀剑术.", ChatType.Hint);
                    ToggleTime = CMain.Time + 1000;
                    Network.Enqueue(new C.SpellToggle { Spell = magic.Spell, CanUse = Thrusting });
                    break;
                case Spell.HalfMoon:
                    if (CMain.Time < ToggleTime) return;
                    HalfMoon = !HalfMoon;
                    //ChatDialog.ReceiveChat(HalfMoon ? "启用半月弯刀." : "关闭半月弯刀.", ChatType.Hint);
                    ToggleTime = CMain.Time + 1000;
                    Network.Enqueue(new C.SpellToggle { Spell = magic.Spell, CanUse = HalfMoon });
                    break;
                case Spell.CrossHalfMoon:
                    if (CMain.Time < ToggleTime) return;
                    CrossHalfMoon = !CrossHalfMoon;
                    //ChatDialog.ReceiveChat(CrossHalfMoon ? "启用圆月弯刀." : "关闭圆月弯刀.", ChatType.Hint);
                    ToggleTime = CMain.Time + 1000;
                    Network.Enqueue(new C.SpellToggle { Spell = magic.Spell, CanUse = CrossHalfMoon });
                    break;
                case Spell.DoubleSlash:
                    if (CMain.Time < ToggleTime) return;
                    DoubleSlash = !DoubleSlash;
                    //ChatDialog.ReceiveChat(DoubleSlash ? "启用双刀术." : "关闭双刀术.", ChatType.Hint);
                    ToggleTime = CMain.Time + 1000;
                    Network.Enqueue(new C.SpellToggle { Spell = magic.Spell, CanUse = DoubleSlash });
                    break;
                case Spell.TwinDrakeBlade:
                    if (CMain.Time < ToggleTime) return;
                    ToggleTime = CMain.Time + 500;

                    cost = magic.Level * magic.LevelCost + magic.BaseCost;
                    if (cost > MapObject.User.MP)
                    {
                        Scene.OutputMessage(GameLanguage.LowMana);
                        return;
                    }
                    TwinDrakeBlade = true;
                    Network.Enqueue(new C.SpellToggle { Spell = magic.Spell, CanUse = true });
                    User.Effects.Add(new Effect(Libraries.Magic2, 210, 6, 500, User));
                    break;
                case Spell.FlamingSword:
                    if (CMain.Time < ToggleTime) return;
                    ToggleTime = CMain.Time + 500;

                    cost = magic.Level * magic.LevelCost + magic.BaseCost;
                    if (cost > MapObject.User.MP)
                    {
                        Scene.OutputMessage(GameLanguage.LowMana);
                        return;
                    }
                    Network.Enqueue(new C.SpellToggle { Spell = magic.Spell, CanUse = true });
                    break;
                case Spell.CounterAttack:
                    cost = magic.Level * magic.LevelCost + magic.BaseCost;
                    if (cost > MapObject.User.MP)
                    {
                        Scene.OutputMessage(GameLanguage.LowMana);
                        return;
                    }

                    SoundManager.PlaySound(20000 + (ushort)Spell.CounterAttack * 10);
                    Network.Enqueue(new C.SpellToggle { Spell = magic.Spell, CanUse = true });
                    break;
                case Spell.MentalState:
                    if (CMain.Time < ToggleTime) return;
                    ToggleTime = CMain.Time + 500;
                    Network.Enqueue(new C.SpellToggle { Spell = magic.Spell, CanUse = true });
                    break;
                default:
                    if (fromUI && MapControl != null && MagicNeedsGroundLocationSelection(magic.Spell))
                    {
                        MapControl.BeginMagicLocationSelection(magic);

                        if (CMain.Time >= OutputDelay)
                        {
                            OutputDelay = CMain.Time + 1000;
                            OutputMessage($"请选择施法位置：{magic.Name}（点地图确认，再次点技能取消）");
                        }

                        return;
                    }

                    User.NextMagic = magic;
                    if (fromUI)
                    {
                        MapObject target = MapObject.TargetObject;
                        if (target != null && target.Dead)
                            target = null;

                        User.NextMagicObject = target;
                        User.NextMagicLocation = target != null ? target.CurrentLocation : User.CurrentLocation;
                        User.NextMagicDirection = User.Direction;
                    }
                    else
                    {
                        User.NextMagicLocation = MapControl.MapLocation;
                        User.NextMagicObject = MapObject.MouseObject;
                        User.NextMagicDirection = MapControl.MouseDirection();
                    }
                    break;
            }

        }

        private static bool MagicNeedsGroundLocationSelection(Spell spell)
        {
            switch (spell)
            {
                case Spell.FireBang:
                case Spell.FireWall:
                case Spell.TrapHexagon:
                case Spell.PoisonCloud:
                case Spell.Blizzard:
                case Spell.MeteorStrike:
                case Spell.Trap:
                case Spell.MassHiding:
                    return true;
                default:
                    return false;
            }
        }

        public void QuitGame()
        {
            if (CMain.Time >= LogTime)
            {
                //If Last Combat < 10 CANCEL
                //MirMessageBox messageBox = new MirMessageBox(GameLanguage.ExitTip, MirMessageBoxButtons.YesNo);
                //messageBox.YesButton.Click += (o, e) => Program.Form.Close();
                //messageBox.Show();
            }
            else
            {
                //ChatDialog.ReceiveChat(string.Format(GameLanguage.CannotLeaveGame, (LogTime - CMain.Time) / 1000), ChatType.System);
            }
        }
        public void LogOut()
        {
            if (CMain.Time >= LogTime)
            {
                //If Last Combat < 10 CANCEL
                //MirMessageBox messageBox = new MirMessageBox(GameLanguage.LogOutTip, MirMessageBoxButtons.YesNo);
                //messageBox.YesButton.Click += (o, e) =>
                //{
                //    Network.Enqueue(new C.LogOut());
                //    Enabled = false;
                //};
                //messageBox.Show();
            }
            else
            {
                //ChatDialog.ReceiveChat(string.Format(GameLanguage.CannotLeaveGame, (LogTime - CMain.Time) / 1000), ChatType.System);
            }
        }

        protected internal override void DrawControl()
        {
            if (MapControl != null && !MapControl.IsDisposed)
                MapControl.DrawControl();
            base.DrawControl();


            //if (PickedUpGold || (SelectedCell != null && SelectedCell.Item != null))
            //{
            //    int image = PickedUpGold ? 116 : SelectedCell.Item.Image;
            //    Size imgSize = Libraries.Items.GetTrueSize(image);
            //    Point p = CMain.MPoint.Add(-imgSize.Width / 2, -imgSize.Height / 2);

            //    if (p.X + imgSize.Width >= Settings.ScreenWidth)
            //        p.X = Settings.ScreenWidth - imgSize.Width;

            //    if (p.Y + imgSize.Height >= Settings.ScreenHeight)
            //        p.Y = Settings.ScreenHeight - imgSize.Height;

            //    Libraries.Items.Draw(image, p.X, p.Y);
            //}

            //for (int i = 0; i < OutputLines.Length; i++)
            //    OutputLines[i].Draw();
        }
        public override void Process()
        {
            if (MapControl == null || User == null)
                return;

            if (Environment.OSVersion.Platform != PlatformID.Win32NT)
                TryExpireMobilePendingGroundItemsIfDue();

            if (CMain.Time >= MoveTime)
            {
                MoveTime += 100; //Move Speed
                CanMove = true;
                MapControl.AnimationCount++;
                MapControl.TextureValid = false;
            }
            else
                CanMove = false;

            if (CMain.Time >= CMain.NextPing)
            {
                CMain.NextPing = CMain.Time + 60000;
                Network.Enqueue(new C.KeepAlive() { Time = CMain.Time });
            }

            //TimerControl.Process();
            //CompassControl.Process();

            //MirItemCell cell = MouseControl as MirItemCell;

            //if (cell != null && HoverItem != cell.Item && HoverItem != cell.ShadowItem)
            //{
            //    DisposeItemLabel();
            //    HoverItem = null;
            //    CreateItemLabel(cell.Item);
            //}

            //if (ItemLabel != null && !ItemLabel.IsDisposed)
            //{
            //    ItemLabel.BringToFront();

            //    int x = CMain.MPoint.X + 15, y = CMain.MPoint.Y;
            //    if (x + ItemLabel.Size.Width > Settings.ScreenWidth)
            //        x = Settings.ScreenWidth - ItemLabel.Size.Width;

            //    if (y + ItemLabel.Size.Height > Settings.ScreenHeight)
            //        y = Settings.ScreenHeight - ItemLabel.Size.Height;
            //    ItemLabel.Location = new Point(x, y);
            //}

            //if (MailLabel != null && !MailLabel.IsDisposed)
            //{
            //    MailLabel.BringToFront();

            //    int x = CMain.MPoint.X + 15, y = CMain.MPoint.Y;
            //    if (x + MailLabel.Size.Width > Settings.ScreenWidth)
            //        x = Settings.ScreenWidth - MailLabel.Size.Width;

            //    if (y + MailLabel.Size.Height > Settings.ScreenHeight)
            //        y = Settings.ScreenHeight - MailLabel.Size.Height;
            //    MailLabel.Location = new Point(x, y);
            //}

            //if (MemoLabel != null && !MemoLabel.IsDisposed)
            //{
            //    MemoLabel.BringToFront();

            //    int x = CMain.MPoint.X + 15, y = CMain.MPoint.Y;
            //    if (x + MemoLabel.Size.Width > Settings.ScreenWidth)
            //        x = Settings.ScreenWidth - MemoLabel.Size.Width;

            //    if (y + MemoLabel.Size.Height > Settings.ScreenHeight)
            //        y = Settings.ScreenHeight - MemoLabel.Size.Height;
            //    MemoLabel.Location = new Point(x, y);
            //}

            //if (GuildBuffLabel != null && !GuildBuffLabel.IsDisposed)
            //{
            //    GuildBuffLabel.BringToFront();

            //    int x = CMain.MPoint.X + 15, y = CMain.MPoint.Y;
            //    if (x + GuildBuffLabel.Size.Width > Settings.ScreenWidth)
            //        x = Settings.ScreenWidth - GuildBuffLabel.Size.Width;

            //    if (y + GuildBuffLabel.Size.Height > Settings.ScreenHeight)
            //        y = Settings.ScreenHeight - GuildBuffLabel.Size.Height;
            //    GuildBuffLabel.Location = new Point(x, y);
            //}

            //if (!User.Dead) ShowReviveMessage = false;

            //if (ShowReviveMessage && CMain.Time > User.DeadTime && User.CurrentAction == MirAction.Dead)
            //{
            //    ShowReviveMessage = false;
            //    MirMessageBox messageBox = new MirMessageBox(GameLanguage.DiedTip, MirMessageBoxButtons.YesNo, false);

            //    messageBox.YesButton.Click += (o, e) =>
            //    {
            //        if (User.Dead) Network.Enqueue(new C.TownRevive());
            //    };

            //    messageBox.AfterDraw += (o, e) =>
            //    {
            //        if (!User.Dead) messageBox.Dispose();
            //    };

            //    messageBox.Show();
            //}

            //BuffsDialog.Process();

            MapControl.Process();
            //MainDialog.Process();
            //InventoryDialog.Process();
            //CustomPanel1.Process();
            //GameShopDialog.Process();
            //MiniMapDialog.Process();

            //foreach (SkillBarDialog Bar in Scene.SkillBarDialogs)
            //    Bar.Process();

            DialogProcess();

            ProcessOuput();

            UpdateMouseCursor();
        }

        public void DialogProcess()
        {
            //if (Settings.SkillBar)
            //{
            //    foreach (SkillBarDialog Bar in Scene.SkillBarDialogs)
            //        Bar.Show();
            //}
            //else
            //{
            //    foreach (SkillBarDialog Bar in Scene.SkillBarDialogs)
            //        Bar.Hide();
            //}

            //for (int i = 0; i < Scene.SkillBarDialogs.Count; i++)
            //{
            //    if (i * 2 > Settings.SkillbarLocation.Length) break;
            //    if ((Settings.SkillbarLocation[i, 0] > Settings.Resolution - 100) || (Settings.SkillbarLocation[i, 1] > 700)) continue;//in theory you'd want the y coord to be validated based on resolution, but since client only allows for wider screens and not higher :(
            //    Scene.SkillBarDialogs[i].Location = new Point(Settings.SkillbarLocation[i, 0], Settings.SkillbarLocation[i, 1]);
            //}

            //if (Settings.DuraView)
            //    CharacterDuraPanel.Show();
            //else
            //    CharacterDuraPanel.Hide();

            TryEnsureFairyGuiMobileMainHud();
        }

        public override void ProcessPacket(Packet p)
        {
            switch (p.Index)
            {
                case (short)ServerPacketIds.KeepAlive:
                    KeepAlive((S.KeepAlive)p);
                    break;
                case (short)ServerPacketIds.MapInformation: //MapInfo
                    MapInformation((S.MapInformation)p);
                    break;
                case (short)ServerPacketIds.UserInformation:
                    UserInformation((S.UserInformation)p);
                    break;
                case (short)ServerPacketIds.UserLocation:
                    UserLocation((S.UserLocation)p);
                    break;
                case (short)ServerPacketIds.ObjectPlayer:
                    ObjectPlayer((S.ObjectPlayer)p);
                    break;
                case (short)ServerPacketIds.ObjectRemove:
                    ObjectRemove((S.ObjectRemove)p);
                    break;
                case (short)ServerPacketIds.ObjectTurn:
                    ObjectTurn((S.ObjectTurn)p);
                    break;
                case (short)ServerPacketIds.ObjectWalk:
                    ObjectWalk((S.ObjectWalk)p);
                    break;
                case (short)ServerPacketIds.ObjectRun:
                    ObjectRun((S.ObjectRun)p);
                    break;
                case (short)ServerPacketIds.Chat:
                    ReceiveChat((S.Chat)p);
                    break;
                case (short)ServerPacketIds.ObjectChat:
                    ObjectChat((S.ObjectChat)p);
                    break;
                case (short)ServerPacketIds.MoveItem:
                    MoveItem((S.MoveItem)p);
                    break;
                case (short)ServerPacketIds.EquipItem:
                    EquipItem((S.EquipItem)p);
                    break;
                case (short)ServerPacketIds.MergeItem:
                    MergeItem((S.MergeItem)p);
                    break;
                case (short)ServerPacketIds.RemoveItem:
                    RemoveItem((S.RemoveItem)p);
                    break;
                case (short)ServerPacketIds.RemoveSlotItem:
                    RemoveSlotItem((S.RemoveSlotItem)p);
                    break;
                case (short)ServerPacketIds.TakeBackItem:
                    TakeBackItem((S.TakeBackItem)p);
                    break;
                case (short)ServerPacketIds.StoreItem:
                    StoreItem((S.StoreItem)p);
                    break;
                case (short)ServerPacketIds.DepositRefineItem:
                    DepositRefineItem((S.DepositRefineItem)p);
                    break;
                case (short)ServerPacketIds.RetrieveRefineItem:
                    RetrieveRefineItem((S.RetrieveRefineItem)p);
                    break;
                case (short)ServerPacketIds.RefineCancel:
                    RefineCancel((S.RefineCancel)p);
                    break;
                case (short)ServerPacketIds.RefineItem:
                    RefineItem((S.RefineItem)p);
                    break;
                case (short)ServerPacketIds.DepositTradeItem:
                    DepositTradeItem((S.DepositTradeItem)p);
                    break;
                case (short)ServerPacketIds.RetrieveTradeItem:
                    RetrieveTradeItem((S.RetrieveTradeItem)p);
                    break;
                case (short)ServerPacketIds.SplitItem:
                    SplitItem((S.SplitItem)p);
                    break;
                case (short)ServerPacketIds.SplitItem1:
                    SplitItem1((S.SplitItem1)p);
                    break;
                case (short)ServerPacketIds.UseItem:
                    UseItem((S.UseItem)p);
                    break;
                case (short)ServerPacketIds.DropItem:
                    DropItem((S.DropItem)p);
                    break;
                case (short)ServerPacketIds.PlayerUpdate:
                    PlayerUpdate((S.PlayerUpdate)p);
                    break;
                case (short)ServerPacketIds.PlayerInspect:
                    PlayerInspect((S.PlayerInspect)p);
                    break;
                case (short)ServerPacketIds.LogOutSuccess:
                    LogOutSuccess((S.LogOutSuccess)p);
                    break;
                case (short)ServerPacketIds.LogOutFailed:
                    LogOutFailed((S.LogOutFailed)p);
                    break;
                case (short)ServerPacketIds.TimeOfDay:
                    TimeOfDay((S.TimeOfDay)p);
                    break;
                case (short)ServerPacketIds.ChangeAMode:
                    ChangeAMode((S.ChangeAMode)p);
                    break;
                case (short)ServerPacketIds.ChangePMode:
                    ChangePMode((S.ChangePMode)p);
                    break;
                case (short)ServerPacketIds.ObjectItem:
                    ObjectItem((S.ObjectItem)p);
                    break;
                case (short)ServerPacketIds.ObjectGold:
                    ObjectGold((S.ObjectGold)p);
                    break;
                case (short)ServerPacketIds.GainedItem:
                    GainedItem((S.GainedItem)p);
                    break;
                case (short)ServerPacketIds.GainedGold:
                    GainedGold((S.GainedGold)p);
                    break;
                case (short)ServerPacketIds.LoseGold:
                    LoseGold((S.LoseGold)p);
                    break;
                case (short)ServerPacketIds.GainedCredit:
                    GainedCredit((S.GainedCredit)p);
                    break;
                case (short)ServerPacketIds.LoseCredit:
                    LoseCredit((S.LoseCredit)p);
                    break;
                case (short)ServerPacketIds.ObjectMonster:
                    ObjectMonster((S.ObjectMonster)p);
                    break;
                case (short)ServerPacketIds.ObjectAttack:
                    ObjectAttack((S.ObjectAttack)p);
                    break;
                case (short)ServerPacketIds.Struck:
                    Struck((S.Struck)p);
                    break;
                case (short)ServerPacketIds.DamageIndicator:
                    DamageIndicator((S.DamageIndicator)p);
                    break;
                case (short)ServerPacketIds.ObjectStruck:
                    ObjectStruck((S.ObjectStruck)p);
                    break;
                case (short)ServerPacketIds.DuraChanged:
                    DuraChanged((S.DuraChanged)p);
                    break;
                case (short)ServerPacketIds.HealthChanged:
                    HealthChanged((S.HealthChanged)p);
                    break;
                case (short)ServerPacketIds.DeleteItem:
                    DeleteItem((S.DeleteItem)p);
                    break;
                case (short)ServerPacketIds.Death:
                    Death((S.Death)p);
                    break;
                case (short)ServerPacketIds.ObjectDied:
                    ObjectDied((S.ObjectDied)p);
                    break;
                case (short)ServerPacketIds.ColourChanged:
                    ColourChanged((S.ColourChanged)p);
                    break;
                case (short)ServerPacketIds.ObjectColourChanged:
                    ObjectColourChanged((S.ObjectColourChanged)p);
                    break;
                case (short)ServerPacketIds.ObjectGuildNameChanged:
                    ObjectGuildNameChanged((S.ObjectGuildNameChanged)p);
                    break;
                case (short)ServerPacketIds.GainExperience:
                    GainExperience((S.GainExperience)p);
                    break;
                case (short)ServerPacketIds.LevelChanged:
                    LevelChanged((S.LevelChanged)p);
                    break;
                case (short)ServerPacketIds.ObjectLeveled:
                    ObjectLeveled((S.ObjectLeveled)p);
                    break;
                case (short)ServerPacketIds.ObjectHarvest:
                    ObjectHarvest((S.ObjectHarvest)p);
                    break;
                case (short)ServerPacketIds.ObjectHarvested:
                    ObjectHarvested((S.ObjectHarvested)p);
                    break;
                case (short)ServerPacketIds.ObjectNpc:
                    ObjectNPC((S.ObjectNPC)p);
                    break;
                case (short)ServerPacketIds.NPCResponse:
                    NPCResponse((S.NPCResponse)p);
                    break;
                case (short)ServerPacketIds.ObjectHide:
                    ObjectHide((S.ObjectHide)p);
                    break;
                case (short)ServerPacketIds.ObjectShow:
                    ObjectShow((S.ObjectShow)p);
                    break;
                case (short)ServerPacketIds.Poisoned:
                    Poisoned((S.Poisoned)p);
                    break;
                case (short)ServerPacketIds.ObjectPoisoned:
                    ObjectPoisoned((S.ObjectPoisoned)p);
                    break;
                case (short)ServerPacketIds.MapChanged:
                    MapChanged((S.MapChanged)p);
                    break;
                case (short)ServerPacketIds.ObjectTeleportOut:
                    ObjectTeleportOut((S.ObjectTeleportOut)p);
                    break;
                case (short)ServerPacketIds.ObjectTeleportIn:
                    ObjectTeleportIn((S.ObjectTeleportIn)p);
                    break;
                case (short)ServerPacketIds.TeleportIn:
                    TeleportIn();
                    break;
                case (short)ServerPacketIds.NPCGoods:
                    NPCGoods((S.NPCGoods)p);
                    break;
                case (short)ServerPacketIds.NPCSell:
                    NPCSell((S.NPCSell)p);
                    break;
                case (short)ServerPacketIds.NPCRepair:
                    NPCRepair((S.NPCRepair)p);
                    break;
                case (short)ServerPacketIds.NPCSRepair:
                    NPCSRepair((S.NPCSRepair)p);
                    break;
                case (short)ServerPacketIds.NPCRefine:
                    NPCRefine((S.NPCRefine)p);
                    break;
                case (short)ServerPacketIds.NPCCheckRefine:
                    NPCCheckRefine((S.NPCCheckRefine)p);
                    break;
                case (short)ServerPacketIds.NPCCollectRefine:
                    NPCCollectRefine((S.NPCCollectRefine)p);
                    break;
                case (short)ServerPacketIds.NPCReplaceWedRing:
                    NPCReplaceWedRing((S.NPCReplaceWedRing)p);
                    break;
                case (short)ServerPacketIds.NPCStorage:
                    NPCStorage();
                    break;
                case (short)ServerPacketIds.NPCRequestInput:
                    NPCRequestInput((S.NPCRequestInput)p);
                    break;
                case (short)ServerPacketIds.SellItem:
                    SellItem((S.SellItem)p);
                    break;
                case (short)ServerPacketIds.CraftItem:
                    CraftItem((S.CraftItem)p);
                    break;
                case (short)ServerPacketIds.RepairItem:
                    RepairItem((S.RepairItem)p);
                    break;
                case (short)ServerPacketIds.ItemRepaired:
                    ItemRepaired((S.ItemRepaired)p);
                    break;
                case (short)ServerPacketIds.ItemSlotSizeChanged:
                    ItemSlotSizeChanged((S.ItemSlotSizeChanged)p);
                    break;
                case (short)ServerPacketIds.NewMagic:
                    NewMagic((S.NewMagic)p);
                    break;
                case (short)ServerPacketIds.MagicLeveled:
                    MagicLeveled((S.MagicLeveled)p);
                    break;
                case (short)ServerPacketIds.Magic:
                    Magic((S.Magic)p);
                    break;
                case (short)ServerPacketIds.MagicDelay:
                    MagicDelay((S.MagicDelay)p);
                    break;
                case (short)ServerPacketIds.MagicCast:
                    MagicCast((S.MagicCast)p);
                    break;
                case (short)ServerPacketIds.ObjectMagic:
                    ObjectMagic((S.ObjectMagic)p);
                    break;
                case (short)ServerPacketIds.ObjectProjectile:
                    ObjectProjectile((S.ObjectProjectile)p);
                    break;
                case (short)ServerPacketIds.ObjectEffect:
                    ObjectEffect((S.ObjectEffect)p);
                    break;
                case (short)ServerPacketIds.RangeAttack:
                    RangeAttack((S.RangeAttack)p);
                    break;
                case (short)ServerPacketIds.Pushed:
                    Pushed((S.Pushed)p);
                    break;
                case (short)ServerPacketIds.ObjectPushed:
                    ObjectPushed((S.ObjectPushed)p);
                    break;
                case (short)ServerPacketIds.ObjectName:
                    ObjectName((S.ObjectName)p);
                    break;
                case (short)ServerPacketIds.UserStorage:
                    UserStorage((S.UserStorage)p);
                    break;
                case (short)ServerPacketIds.SwitchGroup:
                    SwitchGroup((S.SwitchGroup)p);
                    break;
                case (short)ServerPacketIds.DeleteGroup:
                    DeleteGroup();
                    break;
                case (short)ServerPacketIds.DeleteMember:
                    DeleteMember((S.DeleteMember)p);
                    break;
                case (short)ServerPacketIds.GroupInvite:
                    GroupInvite((S.GroupInvite)p);
                    break;
                case (short)ServerPacketIds.AddMember:
                    AddMember((S.AddMember)p);
                    break;
                case (short)ServerPacketIds.GroupMembersMap:
                    GroupMembersMap((S.GroupMembersMap)p);
                    break;
                case (short)ServerPacketIds.SendMemberLocation:
                    SendMemberLocation((S.SendMemberLocation)p);
                    break;
                case (short)ServerPacketIds.Revived:
                    Revived();
                    break;
                case (short)ServerPacketIds.ObjectRevived:
                    ObjectRevived((S.ObjectRevived)p);
                    break;
                case (short)ServerPacketIds.SpellToggle:
                    SpellToggle((S.SpellToggle)p);
                    break;
                case (short)ServerPacketIds.ObjectHealth:
                    ObjectHealth((S.ObjectHealth)p);
                    break;
                case (short)ServerPacketIds.MapEffect:
                    MapEffect((S.MapEffect)p);
                    break;
                case (short)ServerPacketIds.ObjectRangeAttack:
                    ObjectRangeAttack((S.ObjectRangeAttack)p);
                    break;
                case (short)ServerPacketIds.AddBuff:
                    AddBuff((S.AddBuff)p);
                    break;
                case (short)ServerPacketIds.RemoveBuff:
                    RemoveBuff((S.RemoveBuff)p);
                    break;
                case (short)ServerPacketIds.ObjectHidden:
                    ObjectHidden((S.ObjectHidden)p);
                    break;
                case (short)ServerPacketIds.RefreshItem:
                    RefreshItem((S.RefreshItem)p);
                    break;
                case (short)ServerPacketIds.ObjectSpell:
                    ObjectSpell((S.ObjectSpell)p);
                    break;
                case (short)ServerPacketIds.UserDash:
                    UserDash((S.UserDash)p);
                    break;
                case (short)ServerPacketIds.ObjectDash:
                    ObjectDash((S.ObjectDash)p);
                    break;
                case (short)ServerPacketIds.UserDashFail:
                    UserDashFail((S.UserDashFail)p);
                    break;
                case (short)ServerPacketIds.ObjectDashFail:
                    ObjectDashFail((S.ObjectDashFail)p);
                    break;
                case (short)ServerPacketIds.NPCConsign:
                    NPCConsign();
                    break;
                case (short)ServerPacketIds.NPCMarket:
                    NPCMarket((S.NPCMarket)p);
                    break;
                case (short)ServerPacketIds.NPCMarketPage:
                    NPCMarketPage((S.NPCMarketPage)p);
                    break;
                case (short)ServerPacketIds.ConsignItem:
                    ConsignItem((S.ConsignItem)p);
                    break;
                case (short)ServerPacketIds.MarketFail:
                    MarketFail((S.MarketFail)p);
                    break;
                case (short)ServerPacketIds.MarketSuccess:
                    MarketSuccess((S.MarketSuccess)p);
                    break;
                case (short)ServerPacketIds.ObjectSitDown:
                    ObjectSitDown((S.ObjectSitDown)p);
                    break;
                case (short)ServerPacketIds.InTrapRock:
                    S.InTrapRock packetdata = (S.InTrapRock)p;
                    User.InTrapRock = packetdata.Trapped;
                    break;
                case (short)ServerPacketIds.RemoveMagic:
                    RemoveMagic((S.RemoveMagic)p);
                    break;
                case (short)ServerPacketIds.BaseStatsInfo:
                    BaseStatsInfo((S.BaseStatsInfo)p);
                    break;
                case (short)ServerPacketIds.UserName:
                    UserName((S.UserName)p);
                    break;
                case (short)ServerPacketIds.ChatItemStats:
                    ChatItemStats((S.ChatItemStats)p);
                    break;
                case (short)ServerPacketIds.GuildInvite:
                    GuildInvite((S.GuildInvite)p);
                    break;
                case (short)ServerPacketIds.GuildMemberChange:
                    GuildMemberChange((S.GuildMemberChange)p);
                    break;
                case (short)ServerPacketIds.GuildNoticeChange:
                    GuildNoticeChange((S.GuildNoticeChange)p);
                    break;
                case (short)ServerPacketIds.GuildStatus:
                    GuildStatus((S.GuildStatus)p);
                    break;
                case (short)ServerPacketIds.GuildExpGain:
                    GuildExpGain((S.GuildExpGain)p);
                    break;
                case (short)ServerPacketIds.GuildNameRequest:
                    GuildNameRequest((S.GuildNameRequest)p);
                    break;
                case (short)ServerPacketIds.GuildStorageGoldChange:
                    GuildStorageGoldChange((S.GuildStorageGoldChange)p);
                    break;
                case (short)ServerPacketIds.GuildStorageItemChange:
                    GuildStorageItemChange((S.GuildStorageItemChange)p);
                    break;
                case (short)ServerPacketIds.GuildStorageList:
                    GuildStorageList((S.GuildStorageList)p);
                    break;
                case (short)ServerPacketIds.GuildRequestWar:
                    GuildRequestWar((S.GuildRequestWar)p);
                    break;
                case (short)ServerPacketIds.DefaultNPC:
                    DefaultNPC((S.DefaultNPC)p);
                    break;
                case (short)ServerPacketIds.NPCUpdate:
                    NPCUpdate((S.NPCUpdate)p);
                    break;
                case (short)ServerPacketIds.NPCImageUpdate:
                    NPCImageUpdate((S.NPCImageUpdate)p);
                    break;
                case (short)ServerPacketIds.MarriageRequest:
                    MarriageRequest((S.MarriageRequest)p);
                    break;
                case (short)ServerPacketIds.DivorceRequest:
                    DivorceRequest((S.DivorceRequest)p);
                    break;
                case (short)ServerPacketIds.MentorRequest:
                    MentorRequest((S.MentorRequest)p);
                    break;
                case (short)ServerPacketIds.TradeRequest:
                    TradeRequest((S.TradeRequest)p);
                    break;
                case (short)ServerPacketIds.TradeAccept:
                    TradeAccept((S.TradeAccept)p);
                    break;
                case (short)ServerPacketIds.TradeGold:
                    TradeGold((S.TradeGold)p);
                    break;
                case (short)ServerPacketIds.TradeItem:
                    TradeItem((S.TradeItem)p);
                    break;
                case (short)ServerPacketIds.TradeConfirm:
                    TradeConfirm();
                    break;
                case (short)ServerPacketIds.TradeCancel:
                    TradeCancel((S.TradeCancel)p);
                    break;
                case (short)ServerPacketIds.MountUpdate:
                    MountUpdate((S.MountUpdate)p);
                    break;
                case (short)ServerPacketIds.TransformUpdate:
                    TransformUpdate((S.TransformUpdate)p);
                    break;
                case (short)ServerPacketIds.EquipSlotItem:
                    EquipSlotItem((S.EquipSlotItem)p);
                    break;
                case (short)ServerPacketIds.FishingUpdate:
                    FishingUpdate((S.FishingUpdate)p);
                    break;
                case (short)ServerPacketIds.ChangeQuest:
                    ChangeQuest((S.ChangeQuest)p);
                    break;
                case (short)ServerPacketIds.CompleteQuest:
                    CompleteQuest((S.CompleteQuest)p);
                    break;
                case (short)ServerPacketIds.ShareQuest:
                    ShareQuest((S.ShareQuest)p);
                    break;
                case (short)ServerPacketIds.GainedQuestItem:
                    GainedQuestItem((S.GainedQuestItem)p);
                    break;
                case (short)ServerPacketIds.DeleteQuestItem:
                    DeleteQuestItem((S.DeleteQuestItem)p);
                    break;
                case (short)ServerPacketIds.CancelReincarnation:
                    User.ReincarnationStopTime = 0;
                    break;
                case (short)ServerPacketIds.RequestReincarnation:
                    if (!User.Dead) return;
                    RequestReincarnation();
                    break;
                case (short)ServerPacketIds.UserBackStep:
                    UserBackStep((S.UserBackStep)p);
                    break;
                case (short)ServerPacketIds.ObjectBackStep:
                    ObjectBackStep((S.ObjectBackStep)p);
                    break;
                case (short)ServerPacketIds.UserDashAttack:
                    UserDashAttack((S.UserDashAttack)p);
                    break;
                case (short)ServerPacketIds.ObjectDashAttack:
                    ObjectDashAttack((S.ObjectDashAttack)p);
                    break;
                case (short)ServerPacketIds.UserAttackMove://Warrior Skill - SlashingBurst
                    UserAttackMove((S.UserAttackMove)p);
                    break;
                case (short)ServerPacketIds.CombineItem:
                    CombineItem((S.CombineItem)p);
                    break;
                case (short)ServerPacketIds.ItemUpgraded:
                    ItemUpgraded((S.ItemUpgraded)p);
                    break;
                case (short)ServerPacketIds.SetConcentration:
                    SetConcentration((S.SetConcentration)p);
                    break;
                case (short)ServerPacketIds.SetElemental:
                    SetElemental((S.SetElemental)p);
                    break;
                case (short)ServerPacketIds.RemoveDelayedExplosion:
                    RemoveDelayedExplosion((S.RemoveDelayedExplosion)p);
                    break;
                case (short)ServerPacketIds.ObjectDeco:
                    ObjectDeco((S.ObjectDeco)p);
                    break;
                case (short)ServerPacketIds.ObjectSneaking:
                    ObjectSneaking((S.ObjectSneaking)p);
                    break;
                case (short)ServerPacketIds.ObjectLevelEffects:
                    ObjectLevelEffects((S.ObjectLevelEffects)p);
                    break;
                case (short)ServerPacketIds.SetBindingShot:
                    SetBindingShot((S.SetBindingShot)p);
                    break;
                case (short)ServerPacketIds.SendOutputMessage:
                    SendOutputMessage((S.SendOutputMessage)p);
                    break;
                case (short)ServerPacketIds.NPCAwakening:
                    NPCAwakening();
                    break;
                case (short)ServerPacketIds.NPCDisassemble:
                    NPCDisassemble();
                    break;
                case (short)ServerPacketIds.NPCDowngrade:
                    NPCDowngrade();
                    break;
                case (short)ServerPacketIds.NPCReset:
                    NPCReset();
                    break;
                case (short)ServerPacketIds.AwakeningNeedMaterials:
                    AwakeningNeedMaterials((S.AwakeningNeedMaterials)p);
                    break;
                case (short)ServerPacketIds.AwakeningLockedItem:
                    AwakeningLockedItem((S.AwakeningLockedItem)p);
                    break;
                case (short)ServerPacketIds.Awakening:
                    Awakening((S.Awakening)p);
                    break;
                case (short)ServerPacketIds.ReceiveMail:
                    ReceiveMail((S.ReceiveMail)p);
                    break;
                case (short)ServerPacketIds.MailLockedItem:
                    MailLockedItem((S.MailLockedItem)p);
                    break;
                case (short)ServerPacketIds.MailSent:
                    MailSent((S.MailSent)p);
                    break;
                case (short)ServerPacketIds.MailSendRequest:
                    MailSendRequest((S.MailSendRequest)p);
                    break;
                case (short)ServerPacketIds.ParcelCollected:
                    ParcelCollected((S.ParcelCollected)p);
                    break;
                case (short)ServerPacketIds.MailCost:
                    MailCost((S.MailCost)p);
                    break;
                case (short)ServerPacketIds.ResizeInventory:
                    ResizeInventory((S.ResizeInventory)p);
                    break;
                case (short)ServerPacketIds.ResizeStorage:
                    ResizeStorage((S.ResizeStorage)p);
                    break;
                case (short)ServerPacketIds.NewIntelligentCreature:
                    NewIntelligentCreature((S.NewIntelligentCreature)p);
                    break;
                case (short)ServerPacketIds.UpdateIntelligentCreatureList:
                    UpdateIntelligentCreatureList((S.UpdateIntelligentCreatureList)p);
                    break;
                case (short)ServerPacketIds.IntelligentCreatureEnableRename:
                    IntelligentCreatureEnableRename((S.IntelligentCreatureEnableRename)p);
                    break;
                case (short)ServerPacketIds.IntelligentCreaturePickup:
                    IntelligentCreaturePickup((S.IntelligentCreaturePickup)p);
                    break;
                case (short)ServerPacketIds.NPCPearlGoods:
                    NPCPearlGoods((S.NPCPearlGoods)p);
                    break;
                case (short)ServerPacketIds.FriendUpdate:
                    FriendUpdate((S.FriendUpdate)p);
                    break;
                case (short)ServerPacketIds.LoverUpdate:
                    LoverUpdate((S.LoverUpdate)p);
                    break;
                case (short)ServerPacketIds.MentorUpdate:
                    MentorUpdate((S.MentorUpdate)p);
                    break;
                case (short)ServerPacketIds.GuildBuffList:
                    GuildBuffList((S.GuildBuffList)p);
                    break;
                case (short)ServerPacketIds.GameShopInfo:
                    GameShopUpdate((S.GameShopInfo)p);
                    break;
                case (short)ServerPacketIds.GameShopStock:
                    GameShopStock((S.GameShopStock)p);
                    break;
                case (short)ServerPacketIds.Rankings:
                    Rankings((S.Rankings)p);
                    break;
                case (short)ServerPacketIds.Opendoor:
                    Opendoor((S.Opendoor)p);
                    break;
                case (short)ServerPacketIds.GetRentedItems:
                    RentedItems((S.GetRentedItems)p);
                    break;
                case (short)ServerPacketIds.ItemRentalRequest:
                    ItemRentalRequest((S.ItemRentalRequest)p);
                    break;
                case (short)ServerPacketIds.ItemRentalFee:
                    ItemRentalFee((S.ItemRentalFee)p);
                    break;
                case (short)ServerPacketIds.ItemRentalPeriod:
                    ItemRentalPeriod((S.ItemRentalPeriod)p);
                    break;
                case (short)ServerPacketIds.DepositRentalItem:
                    DepositRentalItem((S.DepositRentalItem)p);
                    break;
                case (short)ServerPacketIds.RetrieveRentalItem:
                    RetrieveRentalItem((S.RetrieveRentalItem)p);
                    break;
                case (short)ServerPacketIds.UpdateRentalItem:
                    UpdateRentalItem((S.UpdateRentalItem)p);
                    break;
                case (short)ServerPacketIds.CancelItemRental:
                    CancelItemRental((S.CancelItemRental)p);
                    break;
                case (short)ServerPacketIds.ItemRentalLock:
                    ItemRentalLock((S.ItemRentalLock)p);
                    break;
                case (short)ServerPacketIds.ItemRentalPartnerLock:
                    ItemRentalPartnerLock((S.ItemRentalPartnerLock)p);
                    break;
                case (short)ServerPacketIds.CanConfirmItemRental:
                    CanConfirmItemRental((S.CanConfirmItemRental)p);
                    break;
                case (short)ServerPacketIds.ConfirmItemRental:
                    ConfirmItemRental((S.ConfirmItemRental)p);
                    break;
                case (short)ServerPacketIds.OpenBrowser:
                    OpenBrowser((S.OpenBrowser)p);
                    break;
                case (short)ServerPacketIds.PlaySound:
                    PlaySound((S.PlaySound)p);
                    break;
                case (short)ServerPacketIds.SetTimer:
                    SetTimer((S.SetTimer)p);
                    break;
                case (short)ServerPacketIds.ExpireTimer:
                    ExpireTimer((S.ExpireTimer)p);
                    break;
                case (short)ServerPacketIds.UpdateNotice:
                    ShowNotice((S.UpdateNotice)p);
                    break;
                case (short)ServerPacketIds.Fg:
                    Fg((S.Fg)p);
                    break;
                default:
                    base.ProcessPacket(p);
                    break;
            }
        }

        private void KeepAlive(S.KeepAlive p)
        {
            if (p.Time == 0) return;
            CMain.PingTime = (CMain.Time - p.Time);
        }
        private void MapInformation(S.MapInformation p)
        {
            var sw = Stopwatch.StartNew();
            if (Environment.OSVersion.Platform != PlatformID.Win32NT)
                CMain.SaveLog($"进入地图：收到 MapInformation FileName={p.FileName}");

            ClearMobilePendingGroundItems();

            if (MapControl == null || MapControl.IsDisposed)
                MapControl = new MapControl();

            MapControl.FileName = Settings.ResolveMapFile(p.FileName + ".map");
            MapControl.Title = p.Title;
            MapControl.MiniMap = p.MiniMap;
            MapControl.BigMap = p.BigMap;
            MapControl.Lights = p.Lights;
            MapControl.Lightning = p.Lightning;
            MapControl.Fire = p.Fire;
            MapControl.MapDarkLight = p.MapDarkLight;
            MapControl.Music = p.Music;
            MapControl.LoadMap();
            //InsertControl(0, MapControl);

            if (Environment.OSVersion.Platform != PlatformID.Win32NT)
            {
                if (MapControl.IsMapLoading)
                    CMain.SaveLog($"进入地图：MapControl 已切换为后台加载（{sw.ElapsedMilliseconds}ms）");
                else
                    CMain.SaveLog($"进入地图：MapControl.LoadMap 完成（{sw.ElapsedMilliseconds}ms）");
            }
        }
        private void UserInformation(S.UserInformation p)
        {
            User = new UserObject(p.ObjectID);
            User.Load(p);
            //MainDialog.PModeLabel.Visible = User.Class == MirClass.Wizard || User.Class == MirClass.Taoist;
            Gold = p.Gold;
            Credit = p.Credit;

            //InventoryDialog.RefreshInventory();
            //foreach (SkillBarDialog Bar in SkillBarDialogs)
            //    Bar.Update();
        }
        private void UserLocation(S.UserLocation p)
        {
            MapControl.NextAction = 0;
            if (User.CurrentLocation == p.Location && User.Direction == p.Direction) return;

            if (Settings.DebugMode)
            {
                ReceiveChat(new S.Chat { Message = "Displacement", Type = ChatType.System });
            }

            MapControl.RemoveObject(User);
            User.CurrentLocation = p.Location;
            User.MapLocation = p.Location;
            MapControl.AddObject(User);

            MapControl.FloorValid = false;
            MapControl.InputDelay = CMain.Time + 400;

            if (User.Dead) return;

            User.ClearMagic();
            User.QueuedAction = null;

            for (int i = User.ActionFeed.Count - 1; i >= 0; i--)
            {
                if (User.ActionFeed[i].Action == MirAction.Pushed) continue;
                User.ActionFeed.RemoveAt(i);
            }

            User.SetAction();
        }
        private void ReceiveChat(S.Chat p)
        {
            MobileReceiveChat(p.Message, p.Type);
        }
        private void ObjectPlayer(S.ObjectPlayer p)
        {
            PlayerObject player = new PlayerObject(p.ObjectID);
            player.Load(p);
        }
        private void ObjectRemove(S.ObjectRemove p)
        {
            if (p.ObjectID == User.ObjectID) return;

            for (int i = MapControl.Objects.Count - 1; i >= 0; i--)
            {
                MapObject ob = MapControl.Objects[i];
                if (ob.ObjectID != p.ObjectID) continue;
                ob.Remove();
            }
        }
        private void ObjectTurn(S.ObjectTurn p)
        {
            if (p.ObjectID == User.ObjectID) return;

            for (int i = MapControl.Objects.Count - 1; i >= 0; i--)
            {
                MapObject ob = MapControl.Objects[i];
                if (ob.ObjectID != p.ObjectID) continue;
                ob.ActionFeed.Add(new QueuedAction { Action = MirAction.Standing, Direction = p.Direction, Location = p.Location });
                return;
            }
        }
        private void ObjectWalk(S.ObjectWalk p)
        {
            if (p.ObjectID == User.ObjectID) return;

            for (int i = MapControl.Objects.Count - 1; i >= 0; i--)
            {
                MapObject ob = MapControl.Objects[i];
                if (ob.ObjectID != p.ObjectID) continue;
                ob.ActionFeed.Add(new QueuedAction { Action = MirAction.Walking, Direction = p.Direction, Location = p.Location });
                return;
            }
        }
        private void ObjectRun(S.ObjectRun p)
        {
            if (p.ObjectID == User.ObjectID) return;

            for (int i = MapControl.Objects.Count - 1; i >= 0; i--)
            {
                MapObject ob = MapControl.Objects[i];
                if (ob.ObjectID != p.ObjectID) continue;
                ob.ActionFeed.Add(new QueuedAction { Action = MirAction.Running, Direction = p.Direction, Location = p.Location });
                return;
            }
        }
        private void ObjectChat(S.ObjectChat p)
        {
            string cleaned = RegexFunctions.CleanChatString(p.Text);
            MobileReceiveChat(cleaned, p.Type);

            for (int i = MapControl.Objects.Count - 1; i >= 0; i--)
            {
                MapObject ob = MapControl.Objects[i];
                if (ob.ObjectID != p.ObjectID) continue;
                ob.Chat(cleaned);
                return;
            }

        }
        private void MoveItem(S.MoveItem p)
        {
            if (!p.Success)
                return;

            UserItem[] array;
            switch (p.Grid)
            {
                case MirGridType.Inventory:
                    array = User?.Inventory;
                    break;
                case MirGridType.Storage:
                    array = Storage;
                    break;
                default:
                    return;
            }

            if (array == null)
                return;

            if (p.From < 0 || p.To < 0 || p.From >= array.Length || p.To >= array.Length)
                return;

            UserItem temp = array[p.From];
            array[p.From] = array[p.To];
            array[p.To] = temp;

            User?.RefreshStats();
        }
        private void EquipItem(S.EquipItem p)
        {
            if (!p.Success)
                return;

            UserObject user = User;
            if (user == null)
                return;

            UserItem[] fromArray;
            UserItem[] toArray;

            switch (p.Grid)
            {
                case MirGridType.Inventory:
                    fromArray = user.Inventory;
                    toArray = user.Equipment;
                    break;
                case MirGridType.Storage:
                    fromArray = Storage;
                    toArray = user.Equipment;
                    break;
                default:
                    return;
            }

            if (fromArray == null || toArray == null)
                return;

            if (p.To < 0 || p.To >= toArray.Length)
                return;

            int fromIndex = -1;
            for (int i = 0; i < fromArray.Length; i++)
            {
                if (fromArray[i] == null || fromArray[i].UniqueID != p.UniqueID) continue;
                fromIndex = i;
                break;
            }

            if (fromIndex < 0)
                return;

            UserItem temp = fromArray[fromIndex];
            fromArray[fromIndex] = toArray[p.To];
            toArray[p.To] = temp;

            user.RefreshStats();
        }
        private void EquipSlotItem(S.EquipSlotItem p)
        {
            //MirItemCell fromCell;
            //MirItemCell toCell;

            //switch (p.GridTo)
            //{
            //    case MirGridType.Socket:
            //        toCell = SocketDialog.Grid[p.To];
            //        break;
            //    case MirGridType.Mount:
            //        toCell = MountDialog.Grid[p.To];
            //        break;
            //    case MirGridType.Fishing:
            //        toCell = FishingDialog.Grid[p.To];
            //        break;
            //    default:
            //        return;
            //}

            //switch (p.Grid)
            //{
            //    case MirGridType.Inventory:
            //        fromCell = InventoryDialog.GetCell(p.UniqueID) ?? BeltDialog.GetCell(p.UniqueID);
            //        break;
            //    case MirGridType.Storage:
            //        fromCell = StorageDialog.GetCell(p.UniqueID) ?? BeltDialog.GetCell(p.UniqueID);
            //        break;
            //    default:
            //        return;
            //}

            ////if (toCell == null || fromCell == null) return;

            //toCell.Locked = false;
            //fromCell.Locked = false;

            //if (!p.Success) return;

            //UserItem i = fromCell.Item;
            //fromCell.Item = null;
            //toCell.Item = i;
            //User.RefreshStats();
        }

        private void CombineItem(S.CombineItem p)
        {
            //MirItemCell fromCell = InventoryDialog.GetCell(p.IDFrom) ?? BeltDialog.GetCell(p.IDFrom);
            //MirItemCell toCell = InventoryDialog.GetCell(p.IDTo) ?? BeltDialog.GetCell(p.IDTo);

            //if (toCell == null || fromCell == null) return;

            //toCell.Locked = false;
            //fromCell.Locked = false;

            //if (p.Destroy) toCell.Item = null;

            //if (!p.Success) return;

            //fromCell.Item = null;

            //User.RefreshStats();
        }

        private void MergeItem(S.MergeItem p)
        {
            if (p == null || !p.Success)
                return;

            UserObject user = User;
            if (user == null)
                return;

            UserItem[] fromArray = ResolveClientItemArray(p.GridFrom, user);
            UserItem[] toArray = ResolveClientItemArray(p.GridTo, user);
            if (fromArray == null || toArray == null)
                return;

            if (!TryFindClientItemByUniqueId(fromArray, p.IDFrom, out int fromIndex, out UserItem fromItem) ||
                !TryFindClientItemByUniqueId(toArray, p.IDTo, out _, out UserItem toItem))
            {
                CMain.SaveLog($"ClientMergeItem: sync-miss from={p.GridFrom}:{p.IDFrom} to={p.GridTo}:{p.IDTo}");
                return;
            }

            if (fromItem == null || toItem == null || fromItem.Info == null || toItem.Info == null)
                return;

            int remaining = Math.Max(0, toItem.Info.StackSize - toItem.Count);
            if (remaining <= 0)
                return;

            if (fromItem.Count <= remaining)
            {
                toItem.Count += fromItem.Count;
                fromArray[fromIndex] = null;
            }
            else
            {
                fromItem.Count -= (ushort)remaining;
                toItem.Count = toItem.Info.StackSize;
            }

            user.RefreshStats();
        }
        private static UserItem[] ResolveClientItemArray(MirGridType grid, UserObject user)
        {
            if (user == null)
                return null;

            return grid switch
            {
                MirGridType.Inventory => user.Inventory,
                MirGridType.Storage => Storage,
                MirGridType.Equipment => user.Equipment,
                MirGridType.Trade => user.Trade,
                _ => null,
            };
        }
        private static bool TryFindClientItemByUniqueId(UserItem[] items, ulong uniqueId, out int index, out UserItem item)
        {
            index = -1;
            item = null;

            if (items == null || uniqueId == 0)
                return false;

            for (int i = 0; i < items.Length; i++)
            {
                UserItem current = items[i];
                if (current == null || current.UniqueID != uniqueId)
                    continue;

                index = i;
                item = current;
                return true;
            }

            return false;
        }
        private void RemoveItem(S.RemoveItem p)
        {
            if (!p.Success)
                return;

            UserObject user = User;
            if (user == null)
                return;

            UserItem[] toArray;
            UserItem[] fromArray;

            switch (p.Grid)
            {
                case MirGridType.Inventory:
                    toArray = user.Inventory;
                    fromArray = user.Equipment;
                    break;
                case MirGridType.Storage:
                    toArray = Storage;
                    fromArray = user.Equipment;
                    break;
                default:
                    return;
            }

            if (toArray == null || fromArray == null)
                return;

            if (p.To < 0 || p.To >= toArray.Length)
                return;

            int fromIndex = -1;
            for (int i = 0; i < fromArray.Length; i++)
            {
                if (fromArray[i] == null || fromArray[i].UniqueID != p.UniqueID) continue;
                fromIndex = i;
                break;
            }

            if (fromIndex < 0)
                return;

            UserItem item = fromArray[fromIndex];
            fromArray[fromIndex] = null;
            toArray[p.To] = item;

            user.RefreshStats();
        }
        private void RemoveSlotItem(S.RemoveSlotItem p)
        {
            //MirItemCell fromCell;
            //MirItemCell toCell;

            //int index = -1;

            //switch (p.Grid)
            //{
            //    case MirGridType.Socket:
            //        fromCell = SocketDialog.GetCell(p.UniqueID);
            //        break;
            //    case MirGridType.Mount:
            //        fromCell = MountDialog.GetCell(p.UniqueID);
            //        break;
            //    case MirGridType.Fishing:
            //        fromCell = FishingDialog.GetCell(p.UniqueID);
            //        break;
            //    default:
            //        return;
            //}

            //switch (p.GridTo)
            //{
            //    case MirGridType.Inventory:
            //        toCell = p.To < User.BeltIdx ? BeltDialog.Grid[p.To] : InventoryDialog.Grid[p.To - User.BeltIdx];
            //        break;
            //    case MirGridType.Storage:
            //        toCell = StorageDialog.Grid[p.To];
            //        break;
            //    default:
            //        return;
            //}

            //if (toCell == null || fromCell == null) return;

            //toCell.Locked = false;
            //fromCell.Locked = false;

            //if (!p.Success) return;
            //toCell.Item = fromCell.Item;
            //fromCell.Item = null;
            //CharacterDuraPanel.GetCharacterDura();
            //User.RefreshStats();
        }
        private void TakeBackItem(S.TakeBackItem p)
        {
            if (p == null || !p.Success)
                return;

            UserObject user = User;
            if (user == null || user.Inventory == null || Storage == null)
                return;

            if (p.From < 0 || p.From >= Storage.Length)
                return;

            if (p.To < 0 || p.To >= user.Inventory.Length)
                return;

            user.Inventory[p.To] = Storage[p.From];
            Storage[p.From] = null;

            user.RefreshStats();
        }
        private void StoreItem(S.StoreItem p)
        {
            if (p == null || !p.Success)
                return;

            UserObject user = User;
            if (user == null || user.Inventory == null || Storage == null)
                return;

            if (p.From < 0 || p.From >= user.Inventory.Length)
                return;

            if (p.To < 0 || p.To >= Storage.Length)
                return;

            Storage[p.To] = user.Inventory[p.From];
            user.Inventory[p.From] = null;

            user.RefreshStats();
        }
        private void DepositRefineItem(S.DepositRefineItem p)
        {
            //MirItemCell fromCell = p.From < User.BeltIdx ? BeltDialog.Grid[p.From] : InventoryDialog.Grid[p.From - User.BeltIdx];

            //MirItemCell toCell = RefineDialog.Grid[p.To];

            //if (toCell == null || fromCell == null) return;

            //toCell.Locked = false;
            //fromCell.Locked = false;

            //if (!p.Success) return;
            //toCell.Item = fromCell.Item;
            //fromCell.Item = null;
            //User.RefreshStats();
        }

        private void RetrieveRefineItem(S.RetrieveRefineItem p)
        {
            //MirItemCell fromCell = RefineDialog.Grid[p.From];
            //MirItemCell toCell = p.To < User.BeltIdx ? BeltDialog.Grid[p.To] : InventoryDialog.Grid[p.To - User.BeltIdx];

            //if (toCell == null || fromCell == null) return;

            //toCell.Locked = false;
            //fromCell.Locked = false;

            //if (!p.Success) return;
            //toCell.Item = fromCell.Item;
            //fromCell.Item = null;
            //User.RefreshStats();
        }

        private void RefineCancel(S.RefineCancel p)
        {
            //RefineDialog.RefineReset();
        }

        private void RefineItem(S.RefineItem p)
        {
            //RefineDialog.RefineReset();
            //for (int i = 0; i < User.Inventory.Length; i++)
            //{
            //    if (User.Inventory[i] != null && User.Inventory[i].UniqueID == p.UniqueID)
            //    {
            //        User.Inventory[i] = null;
            //        break;
            //    }
            //}
            //NPCDialog.Hide();
        }


        private void DepositTradeItem(S.DepositTradeItem p)
        {
            UserObject user = User;
            if (user == null)
                return;

            user.TradeLocked = false;

            if (Environment.OSVersion.Platform != PlatformID.Win32NT)
                MonoShare.FairyGuiHost.MarkMobileTradeDirty();

            if (!p.Success)
                return;

            UserItem[] inventory = user.Inventory;
            UserItem[] trade = user.Trade;
            if (inventory == null || trade == null)
                return;

            if (p.From < 0 || p.From >= inventory.Length)
                return;

            if (p.To < 0 || p.To >= trade.Length)
                return;

            trade[p.To] = inventory[p.From];
            inventory[p.From] = null;
            user.RefreshStats();

            if (Environment.OSVersion.Platform != PlatformID.Win32NT)
                MonoShare.FairyGuiHost.MarkMobileTradeDirty();
        }
        private void RetrieveTradeItem(S.RetrieveTradeItem p)
        {
            UserObject user = User;
            if (user == null)
                return;

            user.TradeLocked = false;

            if (Environment.OSVersion.Platform != PlatformID.Win32NT)
                MonoShare.FairyGuiHost.MarkMobileTradeDirty();

            if (!p.Success)
                return;

            UserItem[] inventory = user.Inventory;
            UserItem[] trade = user.Trade;
            if (inventory == null || trade == null)
                return;

            if (p.From < 0 || p.From >= trade.Length)
                return;

            if (p.To < 0 || p.To >= inventory.Length)
                return;

            inventory[p.To] = trade[p.From];
            trade[p.From] = null;
            user.RefreshStats();

            if (Environment.OSVersion.Platform != PlatformID.Win32NT)
                MonoShare.FairyGuiHost.MarkMobileTradeDirty();
        }
        private void SplitItem(S.SplitItem p)
        {
            Bind(p.Item);

            UserItem[] array;
            switch (p.Grid)
            {
                case MirGridType.Inventory:
                    array = MapObject.User.Inventory;
                    break;
                case MirGridType.Storage:
                    array = Storage;
                    break;
                default:
                    return;
            }

            if (p.Grid == MirGridType.Inventory && (p.Item.Info.Type == ItemType.Potion || p.Item.Info.Type == ItemType.Scroll || p.Item.Info.Type == ItemType.Amulet || (p.Item.Info.Type == ItemType.Script && p.Item.Info.Effect == 1)))
            {
                if (p.Item.Info.Type == ItemType.Potion || p.Item.Info.Type == ItemType.Scroll || (p.Item.Info.Type == ItemType.Script && p.Item.Info.Effect == 1))
                {
                    for (int i = 0; i < 4; i++)
                    {
                        if (array[i] != null) continue;
                        array[i] = p.Item;
                        User.RefreshStats();
                        return;
                    }
                }
                else if (p.Item.Info.Type == ItemType.Amulet)
                {
                    for (int i = 4; i < GameScene.User.BeltIdx; i++)
                    {
                        if (array[i] != null) continue;
                        array[i] = p.Item;
                        User.RefreshStats();
                        return;
                    }
                }
            }

            for (int i = GameScene.User.BeltIdx; i < array.Length; i++)
            {
                if (array[i] != null) continue;
                array[i] = p.Item;
                User.RefreshStats();
                return;
            }

            for (int i = 0; i < GameScene.User.BeltIdx; i++)
            {
                if (array[i] != null) continue;
                array[i] = p.Item;
                User.RefreshStats();
                return;
            }
        }

        private void SplitItem1(S.SplitItem1 p)
        {
            if (!p.Success)
                return;

            UserItem[] array;
            switch (p.Grid)
            {
                case MirGridType.Inventory:
                    array = User?.Inventory;
                    break;
                case MirGridType.Storage:
                    array = Storage;
                    break;
                default:
                    return;
            }

            if (array == null)
                return;

            for (int i = 0; i < array.Length; i++)
            {
                UserItem item = array[i];
                if (item == null || item.UniqueID != p.UniqueID) continue;

                if (item.Count > p.Count)
                    item.Count -= p.Count;
                else
                    item.Count = 0;

                if (item.Count == 0)
                    array[i] = null;

                User?.RefreshStats();
                return;
            }
        }
        private void UseItem(S.UseItem p)
        {
            if (!p.Success)
                return;

            UserObject user = User;
            if (user == null)
                return;

            UserItem[] array;
            switch (p.Grid)
            {
                case MirGridType.Inventory:
                    array = user.Inventory;
                    break;
                default:
                    return;
            }

            if (array == null)
                return;

            for (int i = 0; i < array.Length; i++)
            {
                UserItem item = array[i];
                if (item == null || item.UniqueID != p.UniqueID) continue;

                if (item.Count > 1)
                    item.Count--;
                else
                    array[i] = null;

                user.RefreshStats();
                return;
            }
        }
        private void DropItem(S.DropItem p)
        {
            if (!p.Success)
                return;

            if (p.HeroItem)
                return;

            UserObject user = User;
            if (user?.Inventory == null)
                return;

            for (int i = 0; i < user.Inventory.Length; i++)
            {
                UserItem item = user.Inventory[i];
                if (item == null || item.UniqueID != p.UniqueID) continue;

                TryCreateMobilePendingGroundItem(item, p.Count);

                if (p.Count >= item.Count)
                    user.Inventory[i] = null;
                else
                    item.Count -= p.Count;

                user.RefreshStats();
                return;
            }
        }


        private void MountUpdate(S.MountUpdate p)
        {
            for (int i = MapControl.Objects.Count - 1; i >= 0; i--)
            {
                if (MapControl.Objects[i].ObjectID != p.ObjectID) continue;

                PlayerObject player = MapControl.Objects[i] as PlayerObject;
                if (player != null)
                {
                    player.MountUpdate(p);
                }
                break;
            }

            if (p.ObjectID != User.ObjectID) return;

            CanRun = false;

            User.RefreshStats();
            //if (p.RidingMount)

            //    GameScene.Scene.MountDialog.RefreshDialog();
            //GameScene.Scene.Redraw();
        }

        private void TransformUpdate(S.TransformUpdate p)
        {
            for (int i = MapControl.Objects.Count - 1; i >= 0; i--)
            {
                if (MapControl.Objects[i].ObjectID != p.ObjectID) continue;

                PlayerObject player = MapControl.Objects[i] as PlayerObject;
                if (player != null)
                {
                    player.TransformType = p.TransformType;
                    player.SetLibraries();
                    player.SetEffects();
                }
                break;
            }
        }

        private void FishingUpdate(S.FishingUpdate p)
        {
            for (int i = MapControl.Objects.Count - 1; i >= 0; i--)
            {
                if (MapControl.Objects[i].ObjectID != p.ObjectID) continue;

                PlayerObject player = MapControl.Objects[i] as PlayerObject;
                if (player != null)
                {
                    player.FishingUpdate(p);

                }
                break;
            }

            if (p.ObjectID != User.ObjectID) return;

            //GameScene.Scene.FishingStatusDialog.ProgressPercent = p.ProgressPercent;
            //GameScene.Scene.FishingStatusDialog.ChancePercent = p.ChancePercent;

            //GameScene.Scene.FishingStatusDialog.ChanceLabel.Text = string.Format("{0}%", GameScene.Scene.FishingStatusDialog.ChancePercent);

            //if (p.Fishing)
            //    GameScene.Scene.FishingStatusDialog.Show();
            //else
            //    GameScene.Scene.FishingStatusDialog.Hide();

            //Redraw();
        }

        private void CompleteQuest(S.CompleteQuest p)
        {
            User.CompletedQuests = p.CompletedQuests;

            PruneMobileTrackedQuestsIfNeeded();
            RefreshMobileQuestTrackingOverlay();
            MonoShare.FairyGuiHost.MarkMobileQuestDirty();
        }

        private void ShareQuest(S.ShareQuest p)
        {
            ClientQuestInfo quest = GameScene.QuestInfoList.FirstOrDefault(e => e.Index == p.QuestIndex);

            if (quest == null) return;

            //MirMessageBox messageBox = new MirMessageBox(string.Format("{0},想和你分享一个任务。你接受吗?", p.SharerName), MirMessageBoxButtons.YesNo);

            //messageBox.YesButton.Click += (o, e) => Network.Enqueue(new C.AcceptQuest { NPCIndex = 0, QuestIndex = quest.Index });

            //messageBox.Show();
        }

        private void ChangeQuest(S.ChangeQuest p)
        {
            if (p?.Quest != null)
                BindQuest(p.Quest);

            switch (p.QuestState)
            {
                case QuestState.Add:
                    User.CurrentQuests.Add(p.Quest);

                    foreach (ClientQuestProgress quest in User.CurrentQuests)
                        BindQuest(quest);
                    if (Settings.TrackedQuests.Contains(p.Quest.Id))
                    {
                        //GameScene.Scene.QuestTrackingDialog.AddQuest(p.Quest, true);
                    }

                    if (p.TrackQuest)
                    {
                        //GameScene.Scene.QuestTrackingDialog.AddQuest(p.Quest);
                    }

                    break;
                case QuestState.Update:
                    for (int i = 0; i < User.CurrentQuests.Count; i++)
                    {
                        if (User.CurrentQuests[i].Id != p.Quest.Id) continue;

                        User.CurrentQuests[i] = p.Quest;
                    }

                    foreach (ClientQuestProgress quest in User.CurrentQuests)
                        BindQuest(quest);

                    break;
                case QuestState.Remove:

                    for (int i = User.CurrentQuests.Count - 1; i >= 0; i--)
                    {
                        if (User.CurrentQuests[i].Id != p.Quest.Id) continue;

                        User.CurrentQuests.RemoveAt(i);
                    }

                    //GameScene.Scene.QuestTrackingDialog.RemoveQuest(p.Quest);

                    break;
            }

            PruneMobileTrackedQuestsIfNeeded();
            RefreshMobileQuestTrackingOverlay();
            MonoShare.FairyGuiHost.MarkMobileQuestDirty();

            ClientQuestInfo info = p?.Quest?.QuestInfo;
            if (info != null)
            {
                string name = string.IsNullOrWhiteSpace(info.Name) ? $"任务 {info.Index}" : info.Name.Trim();
                switch (p.QuestState)
                {
                    case QuestState.Add:
                        MobileReceiveChat($"[任务] 已接取：{name}", ChatType.System);
                        break;
                    case QuestState.Remove:
                        MobileReceiveChat($"[任务] 已结束：{name}", ChatType.System);
                        break;
                }
            }

            //GameScene.Scene.QuestTrackingDialog.DisplayQuests();

            //if (Scene.QuestListDialog.Visible)
            //{
            //    Scene.QuestListDialog.DisplayInfo();
            //}

            //if (Scene.QuestLogDialog.Visible)
            //{
            //    Scene.QuestLogDialog.DisplayQuests();
            //}
        }

        private void PruneMobileTrackedQuestsIfNeeded()
        {
            if (Environment.OSVersion.Platform == PlatformID.Win32NT)
                return;

            int[] tracked = Settings.TrackedQuests;
            if (tracked == null || tracked.Length == 0)
                return;

            UserObject user = User;
            if (user?.CurrentQuests == null)
                return;

            var activeQuestIds = new HashSet<int>();
            for (int i = 0; i < user.CurrentQuests.Count; i++)
            {
                ClientQuestProgress quest = user.CurrentQuests[i];
                if (quest == null)
                    continue;

                if (quest.Id > 0)
                    activeQuestIds.Add(quest.Id);

                int questIndex = quest.QuestInfo?.Index ?? 0;
                if (questIndex > 0)
                    activeQuestIds.Add(questIndex);
            }

            var kept = new List<int>(tracked.Length);
            for (int i = 0; i < tracked.Length; i++)
            {
                int questIndex = tracked[i];
                if (questIndex < 0)
                    continue;

                if (!activeQuestIds.Contains(questIndex))
                    continue;

                if (kept.Contains(questIndex))
                    continue;

                kept.Add(questIndex);
                if (kept.Count >= tracked.Length)
                    break;
            }

            bool changed = false;
            for (int i = 0; i < tracked.Length; i++)
            {
                int next = i < kept.Count ? kept[i] : -1;
                if (tracked[i] == next)
                    continue;

                tracked[i] = next;
                changed = true;
            }

            if (!changed)
                return;

            string name = user.Name;
            if (string.IsNullOrWhiteSpace(name))
                return;

            try
            {
                Settings.SaveTrackedQuests(name);
            }
            catch
            {
            }
        }

        private void PlayerUpdate(S.PlayerUpdate p)
        {
            for (int i = MapControl.Objects.Count - 1; i >= 0; i--)
            {
                if (MapControl.Objects[i].ObjectID != p.ObjectID) continue;

                PlayerObject player = MapControl.Objects[i] as PlayerObject;
                if (player != null) player.Update(p);
                return;
            }
        }
        private void PlayerInspect(S.PlayerInspect p)
        {
            //InspectDialog.Items = p.Equipment;

            //InspectDialog.Name = p.Name;
            //InspectDialog.GuildName = p.GuildName;
            //InspectDialog.GuildRank = p.GuildRank;
            //InspectDialog.Class = p.Class;
            //InspectDialog.Gender = p.Gender;
            //InspectDialog.Hair = p.Hair;
            //InspectDialog.Level = p.Level;
            //InspectDialog.LoverName = p.LoverName;

            //InspectDialog.RefreshInferface();
            //InspectDialog.Show();
        }
        private void LogOutSuccess(S.LogOutSuccess p)
        {
            for (int i = 0; i <= 3; i++)//Fix for orbs sound
                SoundManager.StopSound(20000 + 126 * 10 + 5 + i);

            User = null;
            if (Settings.Resolution != 800)
            {
                //CMain.SetResolution(800, 600);
            }

            //ActiveScene = new SelectScene(p.Characters);

            Dispose();
        }
        private void LogOutFailed(S.LogOutFailed p)
        {
            //Enabled = true;
        }

        private void TimeOfDay(S.TimeOfDay p)
        {
            Lights = p.Lights;
            switch (Lights)
            {
                //case LightSetting.Day:
                //case LightSetting.Normal:
                //    MiniMapDialog.LightSetting.Index = 2093;
                //    break;
                //case LightSetting.Dawn:
                //    MiniMapDialog.LightSetting.Index = 2095;
                //    break;
                //case LightSetting.Evening:
                //    MiniMapDialog.LightSetting.Index = 2094;
                //    break;
                //case LightSetting.Night:
                //    MiniMapDialog.LightSetting.Index = 2092;
                //    break;
            }
        }
        private void ChangeAMode(S.ChangeAMode p)
        {
            AMode = p.Mode;

            switch (p.Mode)
            {
                //case AttackMode.Peace:
                //    ChatDialog.ReceiveChat(GameLanguage.AttackMode_Peace, ChatType.Hint);
                //    break;
                //case AttackMode.Group:
                //    ChatDialog.ReceiveChat(GameLanguage.AttackMode_Group, ChatType.Hint);
                //    break;
                //case AttackMode.Guild:
                //    ChatDialog.ReceiveChat(GameLanguage.AttackMode_Guild, ChatType.Hint);
                //    break;
                //case AttackMode.EnemyGuild:
                //    ChatDialog.ReceiveChat(GameLanguage.AttackMode_EnemyGuild, ChatType.Hint);
                //    break;
                //case AttackMode.RedBrown:
                //    ChatDialog.ReceiveChat(GameLanguage.AttackMode_RedBrown, ChatType.Hint);
                //    break;
                //case AttackMode.All:
                //    ChatDialog.ReceiveChat(GameLanguage.AttackMode_All, ChatType.Hint);
                //    break;
            }
        }
        private void ChangePMode(S.ChangePMode p)
        {
            PMode = p.Mode;
            switch (p.Mode)
            {
                //case PetMode.Both:
                //    ChatDialog.ReceiveChat(GameLanguage.PetMode_Both, ChatType.Hint);
                //    break;
                //case PetMode.MoveOnly:
                //    ChatDialog.ReceiveChat(GameLanguage.PetMode_MoveOnly, ChatType.Hint);
                //    break;
                //case PetMode.AttackOnly:
                //    ChatDialog.ReceiveChat(GameLanguage.PetMode_AttackOnly, ChatType.Hint);
                //    break;
                //case PetMode.None:
                //    ChatDialog.ReceiveChat(GameLanguage.PetMode_None, ChatType.Hint);
                //    break;
            }
        }

        private void ObjectItem(S.ObjectItem p)
        {
            TryResolveMobilePendingGroundItem(p);

            ItemObject ob = new ItemObject(p.ObjectID);
            ob.Load(p);
            /*
            string[] Warnings = new string[] {"HeroNecklace","AdamantineNecklace","8TrigramWheel","HangMaWheel","BaekTaGlove","SpiritReformer","BokMaWheel","BoundlessRing","ThunderRing","TaeGukRing","OmaSpiritRing","NobleRing"};
            if (Warnings.Contains(p.Name))
            {
                ChatDialog.ReceiveChat(string.Format("{0} at {1}", p.Name, p.Location), ChatType.Hint);
            }
            */
        }
        private void ObjectGold(S.ObjectGold p)
        {
            ItemObject ob = new ItemObject(p.ObjectID);
            ob.Load(p);
        }

        private void TryCreateMobilePendingGroundItem(UserItem item, ushort count)
        {
            if (Environment.OSVersion.Platform == PlatformID.Win32NT)
                return;

            if (item == null || item.Info == null || MapControl == null || MapControl.IsDisposed || User == null)
                return;

            Point location;
            try
            {
                location = User.CurrentLocation;
            }
            catch
            {
                return;
            }

            string name;
            try
            {
                name = count > 1
                    ? string.Format("{0} ({1})", item.Info.FriendlyName, count)
                    : item.Info.FriendlyName;
            }
            catch
            {
                name = item.Info.Name ?? string.Empty;
            }

            uint objectId = _nextMobilePendingGroundObjectId--;
            if (objectId == 0)
            {
                _nextMobilePendingGroundObjectId = uint.MaxValue - 1;
                objectId = uint.MaxValue;
            }

            try
            {
                var info = new S.ObjectItem
                {
                    ObjectID = objectId,
                    Name = name ?? string.Empty,
                    NameColour = item.IsAdded ? Color.Cyan : Color.White,
                    Location = location,
                    Image = item.Image,
                    grade = item.Info.Grade,
                };

                ItemObject ob = new ItemObject(objectId);
                ob.Load(info);

                long now = 0;
                try { now = CMain.Time; } catch { now = 0; }
                _mobilePendingGroundItems.Add(new MobilePendingGroundItem
                {
                    TempObjectId = objectId,
                    Name = info.Name ?? string.Empty,
                    Image = info.Image,
                    Location = info.Location,
                    CreatedAtMs = now,
                    ExpireAtMs = now > 0 ? now + 8000 : 0,
                });

                MapControl.FloorValid = false;
            }
            catch
            {
            }
        }

        private void TryResolveMobilePendingGroundItem(S.ObjectItem p)
        {
            if (Environment.OSVersion.Platform == PlatformID.Win32NT)
                return;

            if (p == null || _mobilePendingGroundItems.Count == 0)
                return;

            int bestIndex = -1;
            int bestDistance = int.MaxValue;
            int bestNameScore = int.MaxValue;

            string rawName = p.Name ?? string.Empty;
            string normalizedName = NormalizeMobileGroundItemName(rawName);

            for (int i = 0; i < _mobilePendingGroundItems.Count; i++)
            {
                MobilePendingGroundItem pending = _mobilePendingGroundItems[i];
                if (pending == null)
                    continue;

                if (pending.Image != p.Image)
                    continue;

                int distance = Math.Abs(pending.Location.X - p.Location.X) + Math.Abs(pending.Location.Y - p.Location.Y);
                if (distance > 2)
                    continue;

                int nameScore = 2;
                string pendingRaw = pending.Name ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(pendingRaw) && !string.IsNullOrWhiteSpace(rawName))
                {
                    if (string.Equals(pendingRaw, rawName, StringComparison.OrdinalIgnoreCase))
                    {
                        nameScore = 0;
                    }
                    else
                    {
                        string pendingNormalized = NormalizeMobileGroundItemName(pendingRaw);
                        if (!string.IsNullOrWhiteSpace(pendingNormalized) && !string.IsNullOrWhiteSpace(normalizedName))
                        {
                            if (string.Equals(pendingNormalized, normalizedName, StringComparison.OrdinalIgnoreCase))
                                nameScore = 0;
                            else if (pendingNormalized.Contains(normalizedName, StringComparison.OrdinalIgnoreCase) ||
                                     normalizedName.Contains(pendingNormalized, StringComparison.OrdinalIgnoreCase))
                                nameScore = 1;
                        }
                    }
                }
                else
                {
                    nameScore = 1;
                }

                if (bestIndex < 0 ||
                    nameScore < bestNameScore ||
                    (nameScore == bestNameScore && distance < bestDistance))
                {
                    bestNameScore = nameScore;
                    bestDistance = distance;
                    bestIndex = i;
                }
            }

            if (bestIndex < 0)
                return;

            uint tempObjectId = _mobilePendingGroundItems[bestIndex].TempObjectId;
            _mobilePendingGroundItems.RemoveAt(bestIndex);

            try
            {
                try
                {
                    CMain.SaveLog($"MobilePendingGround: resolved temp={tempObjectId} -> server={p.ObjectID} img={p.Image} loc={p.Location.X},{p.Location.Y} name={p.Name}");
                }
                catch
                {
                }

                if (MapControl != null && !MapControl.IsDisposed)
                {
                    for (int i = MapControl.Objects.Count - 1; i >= 0; i--)
                    {
                        MapObject ob = MapControl.Objects[i];
                        if (ob == null || ob.ObjectID != tempObjectId)
                            continue;

                        ob.Remove();
                        break;
                    }

                    MapControl.FloorValid = false;
                }
            }
            catch
            {
            }
        }

        private static string NormalizeMobileGroundItemName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return string.Empty;

            string value = name.Trim();
            if (value.Length <= 1)
                return value;

            // 兼容：临时地面物品名字可能拼接数量，例如“金创药 (10)”，而服务端 ObjectItem.Name 未必包含括号数量。
            if (value.EndsWith(")", StringComparison.Ordinal))
            {
                int open = value.LastIndexOf('(');
                if (open >= 0 && open < value.Length - 1)
                {
                    string inside = value.Substring(open + 1, value.Length - open - 2).Trim();
                    if (inside.Length > 0 && inside.All(char.IsDigit))
                        value = value.Substring(0, open).TrimEnd();
                }
            }

            // 兼容：某些文本可能是“xxx x10”
            int digitStart = value.Length;
            while (digitStart > 0 && char.IsDigit(value[digitStart - 1]))
                digitStart--;
            if (digitStart < value.Length)
            {
                int xPos = digitStart - 1;
                if (xPos >= 0 && (value[xPos] == 'x' || value[xPos] == 'X'))
                    value = value.Substring(0, xPos).TrimEnd();
            }

            return value.Trim();
        }

        private void TryExpireMobilePendingGroundItemsIfDue()
        {
            if (_mobilePendingGroundItems.Count == 0)
                return;

            long now = 0;
            try { now = CMain.Time; } catch { now = 0; }
            if (now <= 0)
                return;

            bool removedAny = false;

            for (int i = _mobilePendingGroundItems.Count - 1; i >= 0; i--)
            {
                MobilePendingGroundItem pending = _mobilePendingGroundItems[i];
                if (pending == null)
                {
                    _mobilePendingGroundItems.RemoveAt(i);
                    continue;
                }

                long expireAt = pending.ExpireAtMs;
                if (expireAt <= 0)
                {
                    long created = pending.CreatedAtMs;
                    expireAt = created > 0 ? created + 8000 : 0;
                }

                if (expireAt <= 0 || now <= expireAt)
                    continue;

                uint tempId = pending.TempObjectId;
                _mobilePendingGroundItems.RemoveAt(i);
                removedAny = true;

                try
                {
                    CMain.SaveLog($"MobilePendingGround: expired temp={tempId} loc={pending.Location.X},{pending.Location.Y} name={pending.Name}");
                }
                catch
                {
                }

                try
                {
                    if (MapControl != null && !MapControl.IsDisposed)
                    {
                        for (int j = MapControl.Objects.Count - 1; j >= 0; j--)
                        {
                            MapObject ob = MapControl.Objects[j];
                            if (ob == null || ob.ObjectID != tempId)
                                continue;

                            ob.Remove();
                            break;
                        }
                    }
                }
                catch
                {
                }
            }

            if (!removedAny)
                return;

            try
            {
                if (MapControl != null && !MapControl.IsDisposed)
                    MapControl.FloorValid = false;
            }
            catch
            {
            }
        }

        private void ClearMobilePendingGroundItems()
        {
            if (_mobilePendingGroundItems.Count == 0)
                return;

            try
            {
                if (MapControl != null && !MapControl.IsDisposed)
                {
                    for (int i = _mobilePendingGroundItems.Count - 1; i >= 0; i--)
                    {
                        MobilePendingGroundItem pending = _mobilePendingGroundItems[i];
                        if (pending == null || pending.TempObjectId == 0)
                            continue;

                        for (int j = MapControl.Objects.Count - 1; j >= 0; j--)
                        {
                            MapObject ob = MapControl.Objects[j];
                            if (ob == null || ob.ObjectID != pending.TempObjectId)
                                continue;

                            ob.Remove();
                            break;
                        }
                    }

                    MapControl.FloorValid = false;
                }
            }
            catch
            {
            }
            finally
            {
                _mobilePendingGroundItems.Clear();
            }
        }

        private void GainedItem(S.GainedItem p)
        {
            Bind(p.Item);
            AddItem(p.Item);
            User.RefreshStats();

            string message = string.Format(GameLanguage.YouGained, p.Item.FriendlyName);
            OutputMessage(message);

            if (Environment.OSVersion.Platform != PlatformID.Win32NT)
                MonoShare.FairyGuiHost.PushMobileCenterToast(message, MonoShare.FairyGuiHost.MobileCenterToastKind.Item);
        }
        private void GainedQuestItem(S.GainedQuestItem p)
        {
            Bind(p.Item);
            AddQuestItem(p.Item);
        }

        private void GainedGold(S.GainedGold p)
        {
            if (p.Gold == 0) return;

            Gold += p.Gold;
            SoundManager.PlaySound(SoundList.Gold);

            string message = string.Format(GameLanguage.YouGained2, p.Gold, GameLanguage.Gold);
            OutputMessage(message);

            if (Environment.OSVersion.Platform != PlatformID.Win32NT)
                MonoShare.FairyGuiHost.PushMobileCenterToast(message, MonoShare.FairyGuiHost.MobileCenterToastKind.Gold);
        }
        private void LoseGold(S.LoseGold p)
        {
            Gold -= p.Gold;
            SoundManager.PlaySound(SoundList.Gold);
        }
        private void GainedCredit(S.GainedCredit p)
        {
            if (p.Credit == 0) return;

            Credit += p.Credit;
            SoundManager.PlaySound(SoundList.Gold);
            OutputMessage(string.Format(GameLanguage.YouGained2, p.Credit, GameLanguage.Credit));
        }
        private void LoseCredit(S.LoseCredit p)
        {
            Credit -= p.Credit;
            SoundManager.PlaySound(SoundList.Gold);
        }
        private void ObjectMonster(S.ObjectMonster p)
        {
            MonsterObject mob;
            for (int i = MapControl.Objects.Count - 1; i >= 0; i--)
            {
                MapObject ob = MapControl.Objects[i];
                if (ob.ObjectID == p.ObjectID)
                {
                    mob = (MonsterObject)ob;
                    mob.Load(p, true);
                    return;
                }
            }
            mob = new MonsterObject(p.ObjectID);
            mob.Load(p);
        }
        private void ObjectAttack(S.ObjectAttack p)
        {
            if (p.ObjectID == User.ObjectID) return;

            QueuedAction action = null;

            for (int i = MapControl.Objects.Count - 1; i >= 0; i--)
            {
                MapObject ob = MapControl.Objects[i];
                if (ob.ObjectID != p.ObjectID) continue;
                if (ob.Race == ObjectType.Player)
                {
                    action = new QueuedAction { Action = MirAction.Attack1, Direction = p.Direction, Location = p.Location, Params = new List<object>() }; //FAR Close up attack
                }
                else
                {
                    switch (p.Type)
                    {
                        default:
                            {
                                action = new QueuedAction { Action = MirAction.Attack1, Direction = p.Direction, Location = p.Location, Params = new List<object>() };
                                break;
                            }
                        case 1:
                            {
                                action = new QueuedAction { Action = MirAction.Attack2, Direction = p.Direction, Location = p.Location, Params = new List<object>() };
                                break;
                            }
                        case 2:
                            {
                                action = new QueuedAction { Action = MirAction.Attack3, Direction = p.Direction, Location = p.Location, Params = new List<object>() };
                                break;
                            }
                        case 3:
                            {
                                action = new QueuedAction { Action = MirAction.Attack4, Direction = p.Direction, Location = p.Location, Params = new List<object>() };
                                break;
                            }
                    }
                }
                action.Params.Add(p.Spell);
                action.Params.Add(p.Level);
                ob.ActionFeed.Add(action);
                return;
            }
        }
        private void Struck(S.Struck p)
        {
            LogTime = CMain.Time + Globals.LogDelay;

            NextRunTime = CMain.Time + 2500;
            User.BlizzardStopTime = 0;
            User.ClearMagic();
            if (User.ReincarnationStopTime > CMain.Time)
                Network.Enqueue(new C.CancelReincarnation { });

            MirDirection dir = User.Direction;
            Point location = User.CurrentLocation;

            for (int i = 0; i < User.ActionFeed.Count; i++)
                if (User.ActionFeed[i].Action == MirAction.Struck) return;


            if (User.ActionFeed.Count > 0)
            {
                dir = User.ActionFeed[User.ActionFeed.Count - 1].Direction;
                location = User.ActionFeed[User.ActionFeed.Count - 1].Location;
            }

            if (User.Buffs.Any(a => a == BuffType.EnergyShield))
            {
                for (int j = 0; j < User.Effects.Count; j++)
                {
                    BuffEffect effect = null;
                    effect = User.Effects[j] as BuffEffect;

                    if (effect != null && effect.BuffType == BuffType.EnergyShield)
                    {
                        effect.Clear();
                        effect.Remove();

                        User.Effects.Add(effect = new BuffEffect(Libraries.Magic2, 1890, 6, 600, User, true, BuffType.EnergyShield) { Repeat = false });
                        SoundManager.PlaySound(20000 + (ushort)Spell.EnergyShield * 10 + 1);

                        effect.Complete += (o, e) =>
                        {
                            User.Effects.Add(new BuffEffect(Libraries.Magic2, 1900, 2, 800, User, true, BuffType.EnergyShield) { Repeat = true });
                        };


                        break;
                    }
                }
            }

            QueuedAction action = new QueuedAction { Action = MirAction.Struck, Direction = dir, Location = location, Params = new List<object>() };
            action.Params.Add(p.AttackerID);
            User.ActionFeed.Add(action);

        }
        private void ObjectStruck(S.ObjectStruck p)
        {
            if (p.ObjectID == User.ObjectID) return;

            for (int i = MapControl.Objects.Count - 1; i >= 0; i--)
            {
                MapObject ob = MapControl.Objects[i];
                if (ob.ObjectID != p.ObjectID) continue;

                if (ob.SkipFrames) return;
                if (ob.ActionFeed.Count > 0 && ob.ActionFeed[ob.ActionFeed.Count - 1].Action == MirAction.Struck) return;

                if (ob.Race == ObjectType.Player)
                    ((PlayerObject)ob).BlizzardStopTime = 0;
                QueuedAction action = new QueuedAction { Action = MirAction.Struck, Direction = p.Direction, Location = p.Location, Params = new List<object>() };
                action.Params.Add(p.AttackerID);
                ob.ActionFeed.Add(action);

                if (ob.Buffs.Any(a => a == BuffType.EnergyShield))
                {
                    for (int j = 0; j < ob.Effects.Count; j++)
                    {
                        BuffEffect effect = null;
                        effect = ob.Effects[j] as BuffEffect;

                        if (effect != null && effect.BuffType == BuffType.EnergyShield)
                        {
                            effect.Clear();
                            effect.Remove();

                            ob.Effects.Add(effect = new BuffEffect(Libraries.Magic2, 1890, 6, 600, ob, true, BuffType.EnergyShield) { Repeat = false });
                            SoundManager.PlaySound(20000 + (ushort)Spell.EnergyShield * 10 + 1);

                            effect.Complete += (o, e) =>
                            {
                                ob.Effects.Add(new BuffEffect(Libraries.Magic2, 1900, 2, 800, ob, true, BuffType.EnergyShield) { Repeat = true });
                            };

                            break;
                        }
                    }
                }

                return;
            }
        }

        private void DamageIndicator(S.DamageIndicator p)
        {
            if (Settings.DisplayDamage)
            {
                for (int i = MapControl.Objects.Count - 1; i >= 0; i--)
                {
                    MapObject obj = MapControl.Objects[i];
                    if (obj.ObjectID != p.ObjectID) continue;

                    if (obj.Damages.Count >= 10) return;

                    switch (p.Type)
                    {
                        case DamageType.Hit: //add damage level colours
                            obj.Damages.Add(new Damage(p.Damage.ToString("#,##0"), 1000, obj.Race == ObjectType.Player ? Color.Red : Color.White, 50));
                            break;
                        case DamageType.Miss:
                            obj.Damages.Add(new Damage("Miss", 1200, obj.Race == ObjectType.Player ? Color.LightCoral : Color.LightGray, 50));
                            break;
                        case DamageType.Critical:
                            obj.Damages.Add(new Damage("Crit", 1000, obj.Race == ObjectType.Player ? Color.DarkRed : Color.DarkRed, 50) { Offset = 15 });
                            break;
                    }
                }
            }
        }

        private void DuraChanged(S.DuraChanged p)
        {
            UserItem item = null;
            for (int i = 0; i < User.Inventory.Length; i++)
                if (User.Inventory[i] != null && User.Inventory[i].UniqueID == p.UniqueID)
                {
                    item = User.Inventory[i];
                    break;
                }


            if (item == null)
                for (int i = 0; i < User.Equipment.Length; i++)
                {
                    if (User.Equipment[i] != null && User.Equipment[i].UniqueID == p.UniqueID)
                    {
                        item = User.Equipment[i];
                        break;
                    }
                    if (User.Equipment[i] != null && User.Equipment[i].Slots != null)
                    {
                        for (int j = 0; j < User.Equipment[i].Slots.Length; j++)
                        {
                            if (User.Equipment[i].Slots[j] != null && User.Equipment[i].Slots[j].UniqueID == p.UniqueID)
                            {
                                item = User.Equipment[i].Slots[j];
                                break;
                            }
                        }

                        if (item != null) break;
                    }
                }

            if (item == null) return;

            item.CurrentDura = p.CurrentDura;

            if (item.CurrentDura == 0)
            {
                User.RefreshStats();
                switch (item.Info.Type)
                {
                    case ItemType.Mount:
                        //ChatDialog.ReceiveChat(string.Format("{0}不再对你忠诚.", item.Info.FriendlyName), ChatType.System);
                        break;
                    default:
                        //ChatDialog.ReceiveChat(string.Format("{0}的忠诚度将为了0.", item.Info.FriendlyName), ChatType.System);
                        break;
                }

            }

            if (HoverItem == item)
            {
                DisposeItemLabel();
                CreateItemLabel(item);
            }

            //CharacterDuraPanel.UpdateCharacterDura(item);
        }
        private void HealthChanged(S.HealthChanged p)
        {
            User.HP = p.HP;
            User.MP = p.MP;

            User.PercentHealth = (byte)(User.HP / (float)User.Stats[Stat.HP] * 100);
        }

        private void DeleteQuestItem(S.DeleteQuestItem p)
        {
            for (int i = 0; i < User.QuestInventory.Length; i++)
            {
                UserItem item = User.QuestInventory[i];

                if (item == null || item.UniqueID != p.UniqueID) continue;

                if (item.Count == p.Count)
                    User.QuestInventory[i] = null;
                else
                    item.Count -= p.Count;
                break;
            }
        }

        private void DeleteItem(S.DeleteItem p)
        {
            for (int i = 0; i < User.Inventory.Length; i++)
            {
                UserItem item = User.Inventory[i];

                if (item == null || item.UniqueID != p.UniqueID) continue;

                if (item.Count == p.Count)
                    User.Inventory[i] = null;
                else
                    item.Count -= p.Count;
                break;
            }

            for (int i = 0; i < User.Equipment.Length; i++)
            {
                UserItem item = User.Equipment[i];

                if (item != null && item.Slots.Length > 0)
                {
                    for (int j = 0; j < item.Slots.Length; j++)
                    {
                        UserItem slotItem = item.Slots[j];

                        if (slotItem == null || slotItem.UniqueID != p.UniqueID) continue;

                        if (slotItem.Count == p.Count)
                            item.Slots[j] = null;
                        else
                            slotItem.Count -= p.Count;
                        break;
                    }
                }

                if (item == null || item.UniqueID != p.UniqueID) continue;

                if (item.Count == p.Count)
                    User.Equipment[i] = null;
                else
                    item.Count -= p.Count;
                break;
            }
            for (int i = 0; i < Storage.Length; i++)
            {
                var item = Storage[i];
                if (item == null || item.UniqueID != p.UniqueID) continue;

                if (item.Count == p.Count)
                    Storage[i] = null;
                else
                    item.Count -= p.Count;
                break;
            }
            User.RefreshStats();
        }
        private void Death(S.Death p)
        {
            User.Dead = true;

            User.ActionFeed.Add(new QueuedAction { Action = MirAction.Die, Direction = p.Direction, Location = p.Location });
            ShowReviveMessage = true;

            LogTime = 0;
        }
        private void ObjectDied(S.ObjectDied p)
        {
            if (p.ObjectID == User.ObjectID) return;

            for (int i = MapControl.Objects.Count - 1; i >= 0; i--)
            {
                MapObject ob = MapControl.Objects[i];
                if (ob.ObjectID != p.ObjectID) continue;

                switch (p.Type)
                {
                    default:
                        ob.ActionFeed.Add(new QueuedAction { Action = MirAction.Die, Direction = p.Direction, Location = p.Location });
                        ob.Dead = true;
                        break;
                    case 1:
                        MapControl.Effects.Add(new Effect(Libraries.Magic2, 690, 10, 1000, ob.CurrentLocation));
                        ob.Remove();
                        break;
                    case 2:
                        SoundManager.PlaySound(20000 + (ushort)Spell.DarkBody * 10 + 1);
                        MapControl.Effects.Add(new Effect(Libraries.Magic2, 2600, 10, 1200, ob.CurrentLocation));
                        ob.Remove();
                        break;
                }
                return;
            }
        }
        private void ColourChanged(S.ColourChanged p)
        {
            User.NameColour = p.NameColour;
        }
        private void ObjectColourChanged(S.ObjectColourChanged p)
        {
            if (p.ObjectID == User.ObjectID) return;

            for (int i = MapControl.Objects.Count - 1; i >= 0; i--)
            {
                MapObject ob = MapControl.Objects[i];
                if (ob.ObjectID != p.ObjectID) continue;
                ob.NameColour = p.NameColour;
                return;
            }
        }

        private void ObjectGuildNameChanged(S.ObjectGuildNameChanged p)
        {
            if (p.ObjectID == User.ObjectID) return;

            for (int i = MapControl.Objects.Count - 1; i >= 0; i--)
            {
                MapObject ob = MapControl.Objects[i];
                if (ob.ObjectID != p.ObjectID) continue;
                PlayerObject obPlayer = (PlayerObject)ob;
                obPlayer.GuildName = p.GuildName;
                return;
            }
        }
        private void GainExperience(S.GainExperience p)
        {
            string message = string.Format(GameLanguage.ExperienceGained, p.Amount);
            OutputMessage(message);

            if (Environment.OSVersion.Platform != PlatformID.Win32NT)
                MonoShare.FairyGuiHost.PushMobileCenterToast(message, MonoShare.FairyGuiHost.MobileCenterToastKind.Experience);
            MapObject.User.Experience += p.Amount;
        }
        private void LevelChanged(S.LevelChanged p)
        {
            User.Level = p.Level;
            User.Experience = p.Experience;
            User.MaxExperience = p.MaxExperience;
            User.RefreshStats();
            OutputMessage(GameLanguage.LevelUp);
            User.Effects.Add(new Effect(Libraries.Magic2, 1180, 16, 2000, User));
            SoundManager.PlaySound(SoundList.LevelUp);
            //ChatDialog.ReceiveChat(GameLanguage.LevelUp, ChatType.LevelUp);
        }
        private void ObjectLeveled(S.ObjectLeveled p)
        {
            for (int i = MapControl.Objects.Count - 1; i >= 0; i--)
            {
                MapObject ob = MapControl.Objects[i];
                if (ob.ObjectID != p.ObjectID) continue;
                ob.Effects.Add(new Effect(Libraries.Magic2, 1180, 16, 2500, ob));
                SoundManager.PlaySound(SoundList.LevelUp);
                return;
            }
        }
        private void ObjectHarvest(S.ObjectHarvest p)
        {
            for (int i = MapControl.Objects.Count - 1; i >= 0; i--)
            {
                MapObject ob = MapControl.Objects[i];
                if (ob.ObjectID != p.ObjectID) continue;
                ob.ActionFeed.Add(new QueuedAction { Action = MirAction.Harvest, Direction = ob.Direction, Location = ob.CurrentLocation });
                return;
            }
        }
        private void ObjectHarvested(S.ObjectHarvested p)
        {
            for (int i = MapControl.Objects.Count - 1; i >= 0; i--)
            {
                MapObject ob = MapControl.Objects[i];
                if (ob.ObjectID != p.ObjectID) continue;
                ob.ActionFeed.Add(new QueuedAction { Action = MirAction.Skeleton, Direction = ob.Direction, Location = ob.CurrentLocation });
                return;
            }
        }
        private void ObjectNPC(S.ObjectNPC p)
        {
            NPCObject ob = new NPCObject(p.ObjectID);
            ob.Load(p);
        }
        private void NPCResponse(S.NPCResponse p)
        {
            string npcName = string.Empty;
            if (NPCID != 0)
            {
                NPCObject npc = MapControl.GetObject(NPCID) as NPCObject;
                if (npc != null)
                    npcName = npc.Name;
            }

            try
            {
                string preview = p?.Page != null && p.Page.Count > 0 ? (p.Page[0] ?? string.Empty) : string.Empty;
                preview = preview.Replace("\r", " ").Replace("\n", " ");
                if (preview.Length > 80)
                    preview = preview.Substring(0, 80);
                CMain.SaveLog($"MobileNpc: response npcId={NPCID} name={npcName} lines={p?.Page?.Count ?? 0} first={preview}");
            }
            catch
            {
            }

            if (Environment.OSVersion.Platform == PlatformID.Win32NT)
                return;

            NPCTime = 0;
            BeginNpcConversation(NPCID, npcName);
            MonoShare.FairyGuiHost.UpdateMobileNpcPage(NPCID, npcName, p.Page);
        }

        private void NPCUpdate(S.NPCUpdate p)
        {
            GameScene.NPCID = p.NPCID; //Updates the client with the correct NPC ID if it's manually called from the client
        }

        private void NPCImageUpdate(S.NPCImageUpdate p)
        {
            for (int i = MapControl.Objects.Count - 1; i >= 0; i--)
            {
                MapObject ob = MapControl.Objects[i];
                if (ob.ObjectID != p.ObjectID || ob.Race != ObjectType.Merchant) continue;

                NPCObject npc = (NPCObject)ob;
                npc.Image = p.Image;
                npc.Colour = p.Colour;

                npc.LoadLibrary();
                return;
            }
        }
        private void DefaultNPC(S.DefaultNPC p)
        {
            GameScene.DefaultNPCID = p.ObjectID; //Updates the client with the correct Default NPC ID
        }


        private void ObjectHide(S.ObjectHide p)
        {
            for (int i = MapControl.Objects.Count - 1; i >= 0; i--)
            {
                MapObject ob = MapControl.Objects[i];
                if (ob.ObjectID != p.ObjectID) continue;
                ob.ActionFeed.Add(new QueuedAction { Action = MirAction.Hide, Direction = ob.Direction, Location = ob.CurrentLocation });
                return;
            }
        }
        private void ObjectShow(S.ObjectShow p)
        {
            for (int i = MapControl.Objects.Count - 1; i >= 0; i--)
            {
                MapObject ob = MapControl.Objects[i];
                if (ob.ObjectID != p.ObjectID) continue;
                ob.ActionFeed.Add(new QueuedAction { Action = MirAction.Show, Direction = ob.Direction, Location = ob.CurrentLocation });
                return;
            }
        }
        private void Poisoned(S.Poisoned p)
        {
            User.Poison = p.Poison;
            if (p.Poison.HasFlag(PoisonType.Stun) || p.Poison.HasFlag(PoisonType.Frozen) || p.Poison.HasFlag(PoisonType.Paralysis) || p.Poison.HasFlag(PoisonType.LRParalysis))
            {
                User.ClearMagic();
            }
        }
        private void ObjectPoisoned(S.ObjectPoisoned p)
        {
            for (int i = MapControl.Objects.Count - 1; i >= 0; i--)
            {
                MapObject ob = MapControl.Objects[i];
                if (ob.ObjectID != p.ObjectID) continue;
                ob.Poison = p.Poison;
                return;
            }
        }
        private void MapChanged(S.MapChanged p)
        {
            MapControl.FileName = Settings.ResolveMapFile(p.FileName + ".map");
            MapControl.Title = p.Title;
            MapControl.MiniMap = p.MiniMap;
            MapControl.BigMap = p.BigMap;
            MapControl.Lights = p.Lights;
            MapControl.MapDarkLight = p.MapDarkLight;
            MapControl.Music = p.Music;
            MapControl.LoadMap();
            MapControl.NextAction = 0;

            User.CurrentLocation = p.Location;
            User.MapLocation = p.Location;
            MapControl.AddObject(User);

            User.Direction = p.Direction;

            User.QueuedAction = null;
            User.ActionFeed.Clear();
            User.ClearMagic();
            User.SetAction();

            GameScene.CanRun = false;

            MapControl.FloorValid = false;
            MapControl.InputDelay = CMain.Time + 400;
        }
        private void ObjectTeleportOut(S.ObjectTeleportOut p)
        {
            for (int i = MapControl.Objects.Count - 1; i >= 0; i--)
            {
                MapObject ob = MapControl.Objects[i];
                if (ob.ObjectID != p.ObjectID) continue;
                Effect effect = null;

                bool playDefaultSound = true;

                switch (p.Type)
                {
                    case 1: //Yimoogi
                        {
                            effect = new Effect(Libraries.Magic2, 1300, 10, 500, ob);
                            break;
                        }
                    case 2: //RedFoxman
                        {
                            effect = new Effect(Libraries.Monsters[(ushort)Monster.RedFoxman], 243, 10, 500, ob);
                            break;
                        }
                    case 4: //MutatedManWorm
                        {
                            effect = new Effect(Libraries.Monsters[(ushort)Monster.MutatedManworm], 272, 6, 500, ob);

                            SoundManager.PlaySound(((ushort)Monster.MutatedManworm) * 10 + 7);
                            playDefaultSound = false;
                            break;
                        }
                    case 5: //WitchDoctor
                        {
                            effect = new Effect(Libraries.Monsters[(ushort)Monster.WitchDoctor], 328, 20, 1000, ob);
                            break;
                        }
                    case 6: //TurtleKing
                        {
                            effect = new Effect(Libraries.Monsters[(ushort)Monster.TurtleKing], 946, 10, 500, ob);
                            break;
                        }
                    default:
                        {
                            effect = new Effect(Libraries.Magic, 250, 10, 500, ob);
                            break;
                        }
                }

                if (effect != null)
                {
                    effect.Complete += (o, e) => ob.Remove();
                    ob.Effects.Add(effect);
                }

                if (playDefaultSound)
                    SoundManager.PlaySound(SoundList.Teleport);

                return;
            }
        }
        private void ObjectTeleportIn(S.ObjectTeleportIn p)
        {
            for (int i = MapControl.Objects.Count - 1; i >= 0; i--)
            {
                MapObject ob = MapControl.Objects[i];
                if (ob.ObjectID != p.ObjectID) continue;

                bool playDefaultSound = true;

                switch (p.Type)
                {
                    case 1: //Yimoogi
                        {
                            ob.Effects.Add(new Effect(Libraries.Magic2, 1310, 10, 500, ob));
                            break;
                        }
                    case 2: //RedFoxman
                        {
                            ob.Effects.Add(new Effect(Libraries.Monsters[(ushort)Monster.RedFoxman], 253, 10, 500, ob));
                            break;
                        }
                    case 4: //MutatedManWorm
                        {
                            ob.Effects.Add(new Effect(Libraries.Monsters[(ushort)Monster.MutatedManworm], 278, 7, 500, ob));

                            SoundManager.PlaySound(((ushort)Monster.MutatedManworm) * 10 + 7);
                            playDefaultSound = false;
                            break;
                        }
                    case 5: //WitchDoctor
                        {
                            ob.Effects.Add(new Effect(Libraries.Monsters[(ushort)Monster.WitchDoctor], 348, 20, 1000, ob));
                            break;
                        }
                    case 6: //TurtleKing
                        {
                            ob.Effects.Add(new Effect(Libraries.Monsters[(ushort)Monster.TurtleKing], 956, 10, 500, ob));
                            break;
                        }
                    default:
                        {
                            ob.Effects.Add(new Effect(Libraries.Magic, 260, 10, 500, ob));
                            break;
                        }
                }

                if (playDefaultSound)
                    SoundManager.PlaySound(SoundList.Teleport);

                return;
            }


        }
        private void TeleportIn()
        {
            User.Effects.Add(new Effect(Libraries.Magic, 260, 10, 500, User));
            SoundManager.PlaySound(SoundList.Teleport);
        }
        private void NPCGoods(S.NPCGoods p)
        {
            for (int i = 0; i < p.List.Count; i++)
            {
                p.List[i].Info = GetInfo(p.List[i].ItemIndex);
            }

            NPCRate = p.Rate;
            HideAddedStoreStats = p.HideAddedStats;

            string npcName = string.Empty;
            if (NPCID != 0)
            {
                NPCObject npc = MapControl.GetObject(NPCID) as NPCObject;
                if (npc != null)
                    npcName = npc.Name;
            }

            if (Environment.OSVersion.Platform == PlatformID.Win32NT)
                return;

            MonoShare.FairyGuiHost.UpdateMobileNpcGoods(p.List, p.Rate, p.Type, usePearls: false);
            if (MonoShare.FairyGuiHost.TryShowMobileWindowByKeywords("NpcGoods", new[] { "购买_BuyWindUI", "NPC", "商品", "购买", "出售", "Goods", "Buy", "Sell" }))
                MonoShare.FairyGuiHost.HideAllMobileWindowsExcept("NpcGoods");

            //if (!NPCDialog.Visible) return;

            //switch (p.Type)
            //{
            //    case PanelType.Buy:
            //        NPCGoodsDialog.UsePearls = false;
            //        NPCGoodsDialog.NewGoods(p.List);
            //        NPCGoodsDialog.Show();
            //        break;
            //    case PanelType.BuySub:
            //        NPCSubGoodsDialog.UsePearls = false;
            //        NPCSubGoodsDialog.NewGoods(p.List);
            //        NPCSubGoodsDialog.Show();
            //        break;
            //    case PanelType.Craft:
            //        NPCCraftGoodsDialog.UsePearls = false;
            //        NPCCraftGoodsDialog.NewGoods(p.List);
            //        NPCCraftGoodsDialog.Show();
            //        CraftDialog.Show();
            //        break;
            //}
        }
        private void NPCPearlGoods(S.NPCPearlGoods p)
        {
            for (int i = 0; i < p.List.Count; i++)
            {
                p.List[i].Info = GetInfo(p.List[i].ItemIndex);
            }

            NPCRate = p.Rate;

            string npcName = string.Empty;
            if (NPCID != 0)
            {
                NPCObject npc = MapControl.GetObject(NPCID) as NPCObject;
                if (npc != null)
                    npcName = npc.Name;
            }

            if (Environment.OSVersion.Platform == PlatformID.Win32NT)
                return;

            MonoShare.FairyGuiHost.UpdateMobileNpcGoods(p.List, p.Rate, p.Type, usePearls: true);
            if (MonoShare.FairyGuiHost.TryShowMobileWindowByKeywords("NpcGoods", new[] { "购买_BuyWindUI", "NPC", "元宝", "Pearl", "商品", "Goods", "Buy" }))
                MonoShare.FairyGuiHost.HideAllMobileWindowsExcept("NpcGoods");

            //if (!NPCDialog.Visible) return;

            //NPCGoodsDialog.UsePearls = true;
            //NPCGoodsDialog.NewGoods(p.List);
            //NPCGoodsDialog.Show();
        }

        private void NPCSell(S.NPCSell p)
        {
            NPCSellRate = p.Rate;

            string npcName = string.Empty;
            if (NPCID != 0)
            {
                NPCObject npc = MapControl.GetObject(NPCID) as NPCObject;
                if (npc != null)
                    npcName = npc.Name;
            }

            if (Environment.OSVersion.Platform == PlatformID.Win32NT)
                return;

            MonoShare.FairyGuiHost.BeginMobileNpcSell(p.Rate);
            if (MonoShare.FairyGuiHost.TryShowMobileWindowByKeywords("NpcGoods", new[] { "购买_BuyWindUI", "NPC", "出售", "Sell", "Goods" }))
                MonoShare.FairyGuiHost.HideAllMobileWindowsExcept("NpcGoods");

            //if (!NPCDialog.Visible) return;
            //NPCDropDialog.PType = PanelType.Sell;
            //NPCDropDialog.Show();
        }
        private void NPCRepair(S.NPCRepair p)
        {
            NPCRate = p.Rate;
            //if (!NPCDialog.Visible) return;
            //NPCDropDialog.PType = PanelType.Repair;
            //NPCDropDialog.Show();
        }
        private void NPCStorage()
        {
            if (Environment.OSVersion.Platform == PlatformID.Win32NT)
                return;

            if (MonoShare.FairyGuiHost.TryShowMobileWindowByKeywords("Storage", new[] { "仓库_StorageUI", "仓库", "Storage" }))
                MonoShare.FairyGuiHost.RequestMobileInventoryStorageSideBySideLayout();
        }
        private void NPCRequestInput(S.NPCRequestInput p)
        {
            if (p == null)
                return;

            uint npcId = p.NPCID != 0 ? p.NPCID : NPCID;
            string pageName = p.PageName ?? string.Empty;

            string npcName = string.Empty;
            if (MapControl != null && npcId != 0)
            {
                NPCObject npc = MapControl.GetObject(npcId) as NPCObject;
                if (npc != null)
                    npcName = npc.Name;
            }

            string title = string.IsNullOrWhiteSpace(npcName) ? "请输入信息" : $"请输入信息：{npcName}";
            string message = "请输入所需信息。";
            if (!string.IsNullOrWhiteSpace(pageName))
                message += $"（{pageName}）";

            GameScene.Scene?.PromptMobileText(
                title,
                message,
                initialText: string.Empty,
                maxLength: 200,
                onOk: value =>
                {
                    Network.Enqueue(new C.NPCConfirmInput
                    {
                        NPCID = npcId,
                        PageName = pageName,
                        Value = (value ?? string.Empty).Trim(),
                    });
                },
                onCancel: () =>
                {
                    GameScene.Scene?.MobileReceiveChat("[NPC] 已取消输入。", ChatType.Hint);
                });
        }

        private void NPCSRepair(S.NPCSRepair p)
        {
            NPCRate = p.Rate;
            //if (!NPCDialog.Visible) return;
            //NPCDropDialog.PType = PanelType.SpecialRepair;
            //NPCDropDialog.Show();
        }

        private void NPCRefine(S.NPCRefine p)
        {
            NPCRate = p.Rate;
            //if (!NPCDialog.Visible) return;
            //NPCDropDialog.PType = PanelType.Refine;
            //if (p.Refining)
            //{
            //    NPCDropDialog.Hide();
            //    NPCDialog.Hide();
            //}
            //else
            //    NPCDropDialog.Show();
        }

        private void NPCCheckRefine(S.NPCCheckRefine p)
        {
            //if (!NPCDialog.Visible) return;
            //NPCDropDialog.PType = PanelType.CheckRefine;
            //NPCDropDialog.Show();
        }

        private void NPCCollectRefine(S.NPCCollectRefine p)
        {
            //if (!NPCDialog.Visible) return;
            //NPCDialog.Hide();
        }

        private void NPCReplaceWedRing(S.NPCReplaceWedRing p)
        {
            //if (!NPCDialog.Visible) return;
            //NPCRate = p.Rate;
            //NPCDropDialog.PType = PanelType.ReplaceWedRing;
            //NPCDropDialog.Show();
        }


        private void SellItem(S.SellItem p)
        {
            //MirItemCell cell = InventoryDialog.GetCell(p.UniqueID) ?? BeltDialog.GetCell(p.UniqueID);

            //if (cell == null) return;

            //cell.Locked = false;

            //if (!p.Success) return;

            //if (p.Count == cell.Item.Count)
            //    cell.Item = null;
            //else
            //    cell.Item.Count -= p.Count;

            User.RefreshStats();
        }
        private void RepairItem(S.RepairItem p)
        {
            //MirItemCell cell = InventoryDialog.GetCell(p.UniqueID) ?? BeltDialog.GetCell(p.UniqueID);

            //if (cell == null) return;

            //cell.Locked = false;
        }
        private void CraftItem(S.CraftItem p)
        {
            if (!p.Success) return;

            //CraftDialog.UpdateCraftCells();
            User.RefreshStats();
        }
        private void ItemRepaired(S.ItemRepaired p)
        {
            UserItem item = null;
            for (int i = 0; i < User.Inventory.Length; i++)
            {
                if (User.Inventory[i] != null && User.Inventory[i].UniqueID == p.UniqueID)
                {
                    item = User.Inventory[i];
                    break;
                }
            }

            if (item == null)
            {
                for (int i = 0; i < User.Equipment.Length; i++)
                {
                    if (User.Equipment[i] != null && User.Equipment[i].UniqueID == p.UniqueID)
                    {
                        item = User.Equipment[i];
                        break;
                    }
                }
            }

            if (item == null) return;

            item.MaxDura = p.MaxDura;
            item.CurrentDura = p.CurrentDura;

            if (HoverItem == item)
            {
                DisposeItemLabel();
                CreateItemLabel(item);
            }
        }

        private void ItemSlotSizeChanged(S.ItemSlotSizeChanged p)
        {
            UserItem item = null;
            for (int i = 0; i < User.Inventory.Length; i++)
            {
                if (User.Inventory[i] != null && User.Inventory[i].UniqueID == p.UniqueID)
                {
                    item = User.Inventory[i];
                    break;
                }
            }

            if (item == null)
            {
                for (int i = 0; i < User.Equipment.Length; i++)
                {
                    if (User.Equipment[i] != null && User.Equipment[i].UniqueID == p.UniqueID)
                    {
                        item = User.Equipment[i];
                        break;
                    }
                }
            }

            if (item == null) return;

            item.SetSlotSize(p.SlotSize);
        }

        private void ItemUpgraded(S.ItemUpgraded p)
        {
            UserItem item = null;
            for (int i = 0; i < User.Inventory.Length; i++)
            {
                if (User.Inventory[i] != null && User.Inventory[i].UniqueID == p.Item.UniqueID)
                {
                    item = User.Inventory[i];
                    break;
                }
            }

            if (item == null) return;

            item.AddedStats.Clear();
            item.AddedStats.Add(p.Item.AddedStats);

            item.MaxDura = p.Item.MaxDura;
            item.RefineAdded = p.Item.RefineAdded;

            //GameScene.Scene.InventoryDialog.DisplayItemGridEffect(item.UniqueID, 0);

            if (HoverItem == item)
            {
                DisposeItemLabel();
                CreateItemLabel(item);
            }
        }

        private void NewMagic(S.NewMagic p)
        {
            ClientMagic magic = p.Magic;

            User.Magics.Add(magic);
            User.RefreshStats();
            //foreach (SkillBarDialog Bar in SkillBarDialogs)
            //{
            //    Bar.Update();
            //}
        }

        private void RemoveMagic(S.RemoveMagic p)
        {
            User.Magics.RemoveAt(p.PlaceId);
            User.RefreshStats();
            //foreach (SkillBarDialog Bar in SkillBarDialogs)
            //{
            //    Bar.Update();
            //}
        }

        private void MagicLeveled(S.MagicLeveled p)
        {
            for (int i = 0; i < User.Magics.Count; i++)
            {
                ClientMagic magic = User.Magics[i];
                if (magic.Spell != p.Spell) continue;

                if (magic.Level != p.Level)
                {
                    magic.Level = p.Level;
                    User.RefreshStats();
                }

                magic.Experience = p.Experience;
                break;
            }


        }
        private void Magic(S.Magic p)
        {
            User.Spell = p.Spell;
            User.Cast = p.Cast;
            User.TargetID = p.TargetID;
            User.TargetPoint = p.Target;
            User.SpellLevel = p.Level;
            User.SecondaryTargetIDs = p.SecondaryTargetIDs;

            if (!p.Cast) return;

            ClientMagic magic = User.GetMagic(p.Spell);
            magic.CastTime = CMain.Time;
        }

        private void MagicDelay(S.MagicDelay p)
        {
            ClientMagic magic = User.GetMagic(p.Spell);
            magic.Delay = p.Delay;
        }

        private void MagicCast(S.MagicCast p)
        {
            ClientMagic magic = User.GetMagic(p.Spell);
            magic.CastTime = CMain.Time;
        }

        private void ObjectMagic(S.ObjectMagic p)
        {
            if (p.SelfBroadcast == false && p.ObjectID == User.ObjectID) return;

            for (int i = MapControl.Objects.Count - 1; i >= 0; i--)
            {
                MapObject ob = MapControl.Objects[i];
                if (ob.ObjectID != p.ObjectID) continue;

                QueuedAction action = new QueuedAction { Action = MirAction.Spell, Direction = p.Direction, Location = p.Location, Params = new List<object>() };
                action.Params.Add(p.Spell);
                action.Params.Add(p.TargetID);
                action.Params.Add(p.Target);
                action.Params.Add(p.Cast);
                action.Params.Add(p.Level);
                action.Params.Add(p.SecondaryTargetIDs);

                ob.ActionFeed.Add(action);
                return;
            }
        }

        private void ObjectProjectile(S.ObjectProjectile p)
        {
            MapObject source = MapControl.GetObject(p.Source);

            if (source == null) return;

            switch (p.Spell)
            {
                case Spell.FireBounce:
                    {
                        SoundManager.PlaySound(20000 + (ushort)Spell.GreatFireBall * 10 + 1);

                        Missile missile = source.CreateProjectile(410, Libraries.Magic, true, 6, 30, 4, targetID: p.Destination);

                        if (missile.Target != null)
                        {
                            missile.Complete += (o, e) =>
                            {
                                var sender = (Missile)o;

                                if (sender.Target.CurrentAction == MirAction.Dead) return;
                                sender.Target.Effects.Add(new Effect(Libraries.Magic, 570, 10, 600, sender.Target));
                                SoundManager.PlaySound(20000 + (ushort)Spell.GreatFireBall * 10 + 2);
                            };
                        }
                    }
                    break;
            }
        }

        private void ObjectEffect(S.ObjectEffect p)
        {
            for (int i = MapControl.Objects.Count - 1; i >= 0; i--)
            {
                MapObject ob = MapControl.Objects[i];
                if (ob.ObjectID != p.ObjectID) continue;
                PlayerObject player;

                switch (p.Effect)
                {
                    case SpellEffect.FatalSword:
                        ob.Effects.Add(new Effect(Libraries.Magic2, 1940, 4, 400, ob));
                        SoundManager.PlaySound(20000 + (ushort)Spell.FatalSword * 10);
                        break;
                    case SpellEffect.StormEscape:
                        ob.Effects.Add(new Effect(Libraries.Magic3, 610, 10, 600, ob));
                        SoundManager.PlaySound(SoundList.Teleport);
                        break;
                    case SpellEffect.Teleport:
                        ob.Effects.Add(new Effect(Libraries.Magic, 1600, 10, 600, ob));
                        SoundManager.PlaySound(SoundList.Teleport);
                        break;
                    case SpellEffect.Healing:
                        SoundManager.PlaySound(20000 + (ushort)Spell.Healing * 10 + 1);
                        ob.Effects.Add(new Effect(Libraries.Magic, 370, 10, 800, ob));
                        break;
                    case SpellEffect.RedMoonEvil:
                        ob.Effects.Add(new Effect(Libraries.Monsters[(ushort)Monster.RedMoonEvil], 32, 6, 400, ob) { Blend = false });
                        break;
                    case SpellEffect.TwinDrakeBlade:
                        ob.Effects.Add(new Effect(Libraries.Magic2, 380, 6, 800, ob));
                        break;
                    case SpellEffect.MPEater:
                        for (int j = MapControl.Objects.Count - 1; j >= 0; j--)
                        {
                            MapObject ob2 = MapControl.Objects[j];
                            if (ob2.ObjectID == p.EffectType)
                            {
                                ob2.Effects.Add(new Effect(Libraries.Magic2, 2411, 19, 1900, ob2));
                                break;
                            }
                        }
                        ob.Effects.Add(new Effect(Libraries.Magic2, 2400, 9, 900, ob));
                        SoundManager.PlaySound(20000 + (ushort)Spell.FatalSword * 10);
                        break;
                    case SpellEffect.Bleeding:
                        ob.Effects.Add(new Effect(Libraries.Magic3, 60, 3, 400, ob));
                        break;
                    case SpellEffect.Hemorrhage:
                        SoundManager.PlaySound(20000 + (ushort)Spell.Hemorrhage * 10);
                        ob.Effects.Add(new Effect(Libraries.Magic3, 0, 4, 400, ob));
                        ob.Effects.Add(new Effect(Libraries.Magic3, 28, 6, 600, ob));
                        ob.Effects.Add(new Effect(Libraries.Magic3, 46, 8, 800, ob));
                        break;
                    case SpellEffect.MagicShieldUp:
                        if (ob.Race != ObjectType.Player) return;
                        player = (PlayerObject)ob;
                        if (player.ShieldEffect != null)
                        {
                            player.ShieldEffect.Clear();
                            player.ShieldEffect.Remove();
                        }

                        player.MagicShield = true;
                        player.Effects.Add(player.ShieldEffect = new Effect(Libraries.Magic, 3890, 3, 600, ob) { Repeat = true });
                        break;
                    case SpellEffect.MagicShieldDown:
                        if (ob.Race != ObjectType.Player) return;
                        player = (PlayerObject)ob;
                        if (player.ShieldEffect != null)
                        {
                            player.ShieldEffect.Clear();
                            player.ShieldEffect.Remove();
                        }
                        player.ShieldEffect = null;
                        player.MagicShield = false;
                        break;
                    case SpellEffect.GreatFoxSpirit:
                        ob.Effects.Add(new Effect(Libraries.Monsters[(ushort)Monster.GreatFoxSpirit], 375 + (CMain.Random.Next(3) * 20), 20, 1400, ob));
                        SoundManager.PlaySound(((ushort)Monster.GreatFoxSpirit * 10) + 5);
                        break;
                    case SpellEffect.Entrapment:
                        ob.Effects.Add(new Effect(Libraries.Magic2, 1010, 10, 1500, ob));
                        ob.Effects.Add(new Effect(Libraries.Magic2, 1020, 8, 1200, ob));
                        break;
                    case SpellEffect.Critical:
                        //ob.Effects.Add(new Effect(Libraries.CustomEffects, 0, 12, 60, ob));
                        break;
                    case SpellEffect.Reflect:
                        ob.Effects.Add(new Effect(Libraries.Effect, 580, 10, 70, ob));
                        break;
                    case SpellEffect.ElementalBarrierUp:
                        if (ob.Race != ObjectType.Player) return;
                        player = (PlayerObject)ob;
                        if (player.ElementalBarrierEffect != null)
                        {
                            player.ElementalBarrierEffect.Clear();
                            player.ElementalBarrierEffect.Remove();
                        }

                        player.ElementalBarrier = true;
                        player.Effects.Add(player.ElementalBarrierEffect = new Effect(Libraries.Magic3, 1890, 10, 2000, ob) { Repeat = true });
                        break;
                    case SpellEffect.ElementalBarrierDown:
                        if (ob.Race != ObjectType.Player) return;
                        player = (PlayerObject)ob;
                        if (player.ElementalBarrierEffect != null)
                        {
                            player.ElementalBarrierEffect.Clear();
                            player.ElementalBarrierEffect.Remove();
                        }
                        player.ElementalBarrierEffect = null;
                        player.ElementalBarrier = false;
                        player.Effects.Add(player.ElementalBarrierEffect = new Effect(Libraries.Magic3, 1910, 7, 1400, ob));
                        SoundManager.PlaySound(20000 + 131 * 10 + 5);
                        break;
                    case SpellEffect.DelayedExplosion:
                        int effectid = DelayedExplosionEffect.GetOwnerEffectID(ob.ObjectID);
                        if (effectid < 0)
                        {
                            ob.Effects.Add(new DelayedExplosionEffect(Libraries.Magic3, 1590, 8, 1200, ob, true, 0, 0));
                        }
                        else if (effectid >= 0)
                        {
                            if (DelayedExplosionEffect.effectlist[effectid].stage < p.EffectType)
                            {
                                DelayedExplosionEffect.effectlist[effectid].Remove();
                                ob.Effects.Add(new DelayedExplosionEffect(Libraries.Magic3, 1590 + ((int)p.EffectType * 10), 8, 1200, ob, true, (int)p.EffectType, 0));
                            }
                        }

                        //else
                        //    ob.Effects.Add(new DelayedExplosionEffect(Libraries.Magic3, 1590 + ((int)p.EffectType * 10), 8, 1200, ob, true, (int)p.EffectType, 0));
                        break;
                    case SpellEffect.AwakeningSuccess:
                        {
                            Effect ef = new Effect(Libraries.Magic3, 900, 16, 1600, ob, CMain.Time + p.DelayTime);
                            ef.Played += (o, e) => SoundManager.PlaySound(50002);
                            ef.Complete += (o, e) => MapControl.AwakeningAction = false;
                            ob.Effects.Add(ef);
                            ob.Effects.Add(new Effect(Libraries.Magic3, 840, 16, 1600, ob, CMain.Time + p.DelayTime) { Blend = false });
                        }
                        break;
                    case SpellEffect.AwakeningFail:
                        {
                            Effect ef = new Effect(Libraries.Magic3, 920, 9, 900, ob, CMain.Time + p.DelayTime);
                            ef.Played += (o, e) => SoundManager.PlaySound(50003);
                            ef.Complete += (o, e) => MapControl.AwakeningAction = false;
                            ob.Effects.Add(ef);
                            ob.Effects.Add(new Effect(Libraries.Magic3, 860, 9, 900, ob, CMain.Time + p.DelayTime) { Blend = false });
                        }
                        break;
                    case SpellEffect.AwakeningHit:
                        {
                            Effect ef = new Effect(Libraries.Magic3, 880, 5, 500, ob, CMain.Time + p.DelayTime);
                            ef.Played += (o, e) => SoundManager.PlaySound(50001);
                            ob.Effects.Add(ef);
                            ob.Effects.Add(new Effect(Libraries.Magic3, 820, 5, 500, ob, CMain.Time + p.DelayTime) { Blend = false });
                        }
                        break;
                    case SpellEffect.AwakeningMiss:
                        {
                            Effect ef = new Effect(Libraries.Magic3, 890, 5, 500, ob, CMain.Time + p.DelayTime);
                            ef.Played += (o, e) => SoundManager.PlaySound(50000);
                            ob.Effects.Add(ef);
                            ob.Effects.Add(new Effect(Libraries.Magic3, 830, 5, 500, ob, CMain.Time + p.DelayTime) { Blend = false });
                        }
                        break;
                    case SpellEffect.TurtleKing:
                        {
                            Effect ef = new Effect(Libraries.Monsters[(ushort)Monster.TurtleKing], CMain.Random.Next(2) == 0 ? 922 : 934, 12, 1200, ob);
                            ef.Played += (o, e) => SoundManager.PlaySound(20000 + (ushort)Spell.HellFire * 10 + 1);
                            ob.Effects.Add(ef);
                        }
                        break;
                    case SpellEffect.Behemoth:
                        {
                            MapControl.Effects.Add(new Effect(Libraries.Monsters[(ushort)Monster.Behemoth], 788, 10, 1500, ob.CurrentLocation));
                            MapControl.Effects.Add(new Effect(Libraries.Monsters[(ushort)Monster.Behemoth], 778, 10, 1500, ob.CurrentLocation, 0, true) { Blend = false });
                        }
                        break;
                    case SpellEffect.Stunned:
                        ob.Effects.Add(new Effect(Libraries.Monsters[(ushort)Monster.StoningStatue], 632, 10, 1000, ob)
                        {
                            Repeat = p.Time > 0,
                            RepeatUntil = p.Time > 0 ? CMain.Time + p.Time : 0
                        });
                        break;
                    case SpellEffect.IcePillar:
                        ob.Effects.Add(new Effect(Libraries.Monsters[(ushort)Monster.IcePillar], 18, 8, 800, ob));
                        break;
                    case SpellEffect.KingGuard:
                        ob.Effects.Add(new Effect(Libraries.Monsters[(ushort)Monster.KingGuard], 753, 10, 1000, ob) { Blend = false });
                        break;
                }

                return;
            }
        }

        private void RangeAttack(S.RangeAttack p)
        {
            User.TargetID = p.TargetID;
            User.TargetPoint = p.Target;
            User.Spell = p.Spell;
        }

        private void Pushed(S.Pushed p)
        {
            User.ActionFeed.Add(new QueuedAction { Action = MirAction.Pushed, Direction = p.Direction, Location = p.Location });
        }

        private void ObjectPushed(S.ObjectPushed p)
        {
            if (p.ObjectID == User.ObjectID) return;

            for (int i = MapControl.Objects.Count - 1; i >= 0; i--)
            {
                MapObject ob = MapControl.Objects[i];
                if (ob.ObjectID != p.ObjectID) continue;
                ob.ActionFeed.Add(new QueuedAction { Action = MirAction.Pushed, Direction = p.Direction, Location = p.Location });

                return;
            }
        }

        private void ObjectName(S.ObjectName p)
        {
            if (p.ObjectID == User.ObjectID) return;

            for (int i = MapControl.Objects.Count - 1; i >= 0; i--)
            {
                MapObject ob = MapControl.Objects[i];
                if (ob.ObjectID != p.ObjectID) continue;
                ob.Name = p.Name;
                return;
            }
        }
        private void UserStorage(S.UserStorage p)
        {
            if (Storage.Length != p.Storage.Length)
            {
                Array.Resize(ref Storage, p.Storage.Length);
            }

            Storage = p.Storage;

            for (int i = 0; i < Storage.Length; i++)
            {
                if (Storage[i] == null) continue;
                Bind(Storage[i]);
            }
        }
        private void SwitchGroup(S.SwitchGroup p)
        {
            if (p == null)
                return;

            SetMobileGroupActive(p.AllowGroup);
            if (!p.AllowGroup)
                DeleteGroup();
        }

        private void DeleteGroup()
        {
            SetMobileGroupActive(false);
            _mobileGroupMemberLocations.Clear();
            _mobileGroupMemberMaps.Clear();
            MobileReceiveChat("离开了组", ChatType.Group);
            MonoShare.FairyGuiHost.MarkMobileGroupDirty();
        }

        private void DeleteMember(S.DeleteMember p)
        {
            if (p == null)
                return;

            if (!string.IsNullOrWhiteSpace(p.Name))
            {
                _mobileGroupMemberLocations.Remove(p.Name);
                _mobileGroupMemberMaps.Remove(p.Name);
            }
            MobileReceiveChat(string.Format("-{0} 已离开组", p.Name), ChatType.Group);
            MonoShare.FairyGuiHost.MarkMobileGroupDirty();
        }

        private void GroupInvite(S.GroupInvite p)
        {
            if (p == null)
                return;

            MirMessageBox messageBox = new MirMessageBox(string.Format("是否同意跟 {0} 组队？", p.Name), MirMessageBoxButtons.YesNo);

            messageBox.YesButton.Click += (o, e) =>
            {
                SetMobileGroupActive(true);
                SetMobileGroupLeaderName(p.Name);
                Network.Enqueue(new C.GroupInvite { AcceptInvite = true });
                ShowMobileGroupOverlay();
            };
            messageBox.NoButton.Click += (o, e) => Network.Enqueue(new C.GroupInvite { AcceptInvite = false });

            messageBox.Show();
        }
        private void AddMember(S.AddMember p)
        {
            if (p == null)
                return;

            if (!string.IsNullOrWhiteSpace(p.Name))
            {
                if (!_mobileGroupMemberLocations.ContainsKey(p.Name))
                    _mobileGroupMemberLocations[p.Name] = Point.Empty;
                if (!_mobileGroupMemberMaps.ContainsKey(p.Name))
                    _mobileGroupMemberMaps[p.Name] = string.Empty;
            }
            MobileReceiveChat(string.Format("-{0} 已加入组", p.Name), ChatType.Group);
            MonoShare.FairyGuiHost.MarkMobileGroupDirty();
        }

        private void GroupMembersMap(S.GroupMembersMap p)
        {
            if (p == null)
                return;

            if (!string.IsNullOrWhiteSpace(p.PlayerName))
                _mobileGroupMemberMaps[p.PlayerName] = p.PlayerMap ?? string.Empty;

            MonoShare.FairyGuiHost.MarkMobileGroupDirty();
        }

        private void SendMemberLocation(S.SendMemberLocation p)
        {
            if (p == null)
                return;

            if (!string.IsNullOrWhiteSpace(p.MemberName))
                _mobileGroupMemberLocations[p.MemberName] = p.MemberLocation;

            MonoShare.FairyGuiHost.MarkMobileGroupDirty();
        }
        private void Revived()
        {
            User.SetAction();
            User.Dead = false;
            User.Effects.Add(new Effect(Libraries.Magic2, 1220, 20, 2000, User));
            SoundManager.PlaySound(SoundList.Revive);
        }
        private void ObjectRevived(S.ObjectRevived p)
        {
            for (int i = MapControl.Objects.Count - 1; i >= 0; i--)
            {
                MapObject ob = MapControl.Objects[i];
                if (ob.ObjectID != p.ObjectID) continue;
                if (p.Effect)
                {
                    ob.Effects.Add(new Effect(Libraries.Magic2, 1220, 20, 2000, ob));
                    SoundManager.PlaySound(SoundList.Revive);
                }
                ob.Dead = false;
                ob.ActionFeed.Clear();
                ob.ActionFeed.Add(new QueuedAction { Action = MirAction.Revive, Direction = ob.Direction, Location = ob.CurrentLocation });
                return;
            }
        }
        private void SpellToggle(S.SpellToggle p)
        {
            switch (p.Spell)
            {
                //Warrior
                //case Spell.Slaying:
                //    Slaying = p.CanUse;
                //    break;
                //case Spell.Thrusting:
                //    Thrusting = p.CanUse;
                //    ChatDialog.ReceiveChat(Thrusting ? "启用刺杀剑术." : "关闭刺杀剑术.", ChatType.Hint);
                //    break;
                //case Spell.HalfMoon:
                //    HalfMoon = p.CanUse;
                //    ChatDialog.ReceiveChat(HalfMoon ? "启用半月弯刀." : "关闭半月弯刀.", ChatType.Hint);
                //    break;
                //case Spell.CrossHalfMoon:
                //    CrossHalfMoon = p.CanUse;
                //    ChatDialog.ReceiveChat(CrossHalfMoon ? "启用圆月弯刀." : "关闭圆月弯刀.", ChatType.Hint);
                //    break;
                //case Spell.DoubleSlash:
                //    DoubleSlash = p.CanUse;
                //    ChatDialog.ReceiveChat(DoubleSlash ? "启用双刀术." : "关闭双刀术.", ChatType.Hint);
                //    break;
                //case Spell.FlamingSword:
                //    FlamingSword = p.CanUse;
                //    if (FlamingSword)
                //        ChatDialog.ReceiveChat(GameLanguage.WeaponSpiritFire, ChatType.Hint);
                //    else
                //        ChatDialog.ReceiveChat(GameLanguage.SpiritsFireDisappeared, ChatType.System);
                //    break;
            }
        }

        private void ObjectHealth(S.ObjectHealth p)
        {
            for (int i = MapControl.Objects.Count - 1; i >= 0; i--)
            {
                MapObject ob = MapControl.Objects[i];
                if (ob.ObjectID != p.ObjectID) continue;
                ob.PercentHealth = p.Percent;
                ob.HealthTime = CMain.Time + p.Expire * 1000;
                return;
            }
        }

        private void MapEffect(S.MapEffect p)
        {
            switch (p.Effect)
            {
                case SpellEffect.Mine:
                    SoundManager.PlaySound(10091);
                    Effect HitWall = new Effect(Libraries.Effect, 8 * p.Value, 3, 240, p.Location) { Light = 0 };
                    MapControl.Effects.Add(HitWall);
                    break;
            }
        }

        private void ObjectRangeAttack(S.ObjectRangeAttack p)
        {
            if (p.ObjectID == User.ObjectID) return;

            for (int i = MapControl.Objects.Count - 1; i >= 0; i--)
            {
                MapObject ob = MapControl.Objects[i];
                if (ob.ObjectID != p.ObjectID) continue;
                QueuedAction action = null;
                if (ob.Race == ObjectType.Player)
                {
                    switch (p.Type)
                    {
                        default:
                            {
                                action = new QueuedAction { Action = MirAction.AttackRange1, Direction = p.Direction, Location = p.Location, Params = new List<object>() };
                                break;
                            }
                    }
                }
                else
                {
                    switch (p.Type)
                    {
                        case 1:
                            {
                                action = new QueuedAction { Action = MirAction.AttackRange2, Direction = p.Direction, Location = p.Location, Params = new List<object>() };
                                break;
                            }
                        case 2:
                            {
                                action = new QueuedAction { Action = MirAction.AttackRange3, Direction = p.Direction, Location = p.Location, Params = new List<object>() };
                                break;
                            }
                        default:
                            {
                                action = new QueuedAction { Action = MirAction.AttackRange1, Direction = p.Direction, Location = p.Location, Params = new List<object>() };
                                break;
                            }
                    }
                }
                action.Params.Add(p.TargetID);
                action.Params.Add(p.Target);
                action.Params.Add(p.Spell);
                action.Params.Add(new List<uint>());

                ob.ActionFeed.Add(action);
                return;
            }
        }

        private void AddBuff(S.AddBuff p)
        {
            ClientBuff buff = p.Buff;

            buff.ExpireTime += CMain.Time;

            if (buff.ObjectID == User.ObjectID)
            {
                for (int i = 0; i < Buffs.Count; i++)
                {
                    if (Buffs[i].Type != buff.Type) continue;

                    Buffs[i] = buff;
                    User.RefreshStats();
                    return;
                }

                Buffs.Add(buff);
                //BuffsDialog.CreateBuff(buff);
                User.RefreshStats();
            }

            if (!buff.Visible || buff.ObjectID <= 0) return;

            for (int i = MapControl.Objects.Count - 1; i >= 0; i--)
            {
                MapObject ob = MapControl.Objects[i];
                if (ob.ObjectID != buff.ObjectID) continue;
                if ((ob is PlayerObject) || (ob is MonsterObject))
                {
                    if (!ob.Buffs.Contains(buff.Type))
                    {
                        ob.Buffs.Add(buff.Type);
                    }

                    ob.AddBuffEffect(buff.Type);
                    return;
                }
            }
        }

        private void RemoveBuff(S.RemoveBuff p)
        {
            for (int i = 0; i < Buffs.Count; i++)
            {
                if (Buffs[i].Type != p.Type || User.ObjectID != p.ObjectID) continue;

                switch (Buffs[i].Type)
                {
                    case BuffType.SwiftFeet:
                        User.Sprint = false;
                        break;
                    case BuffType.Transform:
                        User.TransformType = -1;
                        break;
                }

                Buffs.RemoveAt(i);
                //BuffsDialog.RemoveBuff(i);
            }

            if (User.ObjectID == p.ObjectID)
                User.RefreshStats();

            if (p.ObjectID <= 0) return;

            for (int i = MapControl.Objects.Count - 1; i >= 0; i--)
            {
                MapObject ob = MapControl.Objects[i];

                if (ob.ObjectID != p.ObjectID) continue;

                ob.Buffs.Remove(p.Type);
                ob.RemoveBuffEffect(p.Type);
                return;
            }
        }

        private void ObjectHidden(S.ObjectHidden p)
        {
            for (int i = MapControl.Objects.Count - 1; i >= 0; i--)
            {
                MapObject ob = MapControl.Objects[i];
                if (ob.ObjectID != p.ObjectID) continue;
                ob.Hidden = p.Hidden;
                return;
            }
        }

        private void ObjectSneaking(S.ObjectSneaking p)
        {
            for (int i = MapControl.Objects.Count - 1; i >= 0; i--)
            {
                MapObject ob = MapControl.Objects[i];
                if (ob.ObjectID != p.ObjectID) continue;
                // ob.SneakingActive = p.SneakingActive;
                return;
            }
        }

        private void ObjectLevelEffects(S.ObjectLevelEffects p)
        {
            for (int i = MapControl.Objects.Count - 1; i >= 0; i--)
            {
                MapObject ob = MapControl.Objects[i];
                if (ob.ObjectID != p.ObjectID || ob.Race != ObjectType.Player) continue;

                PlayerObject temp = (PlayerObject)ob;

                temp.LevelEffects = p.LevelEffects;

                temp.SetEffects();
                return;
            }
        }

        private void RefreshItem(S.RefreshItem p)
        {
            Bind(p.Item);

            //if (SelectedCell != null && SelectedCell.Item.UniqueID == p.Item.UniqueID)
            //    SelectedCell = null;

            if (HoverItem != null && HoverItem.UniqueID == p.Item.UniqueID)
            {
                DisposeItemLabel();
                CreateItemLabel(p.Item);
            }

            for (int i = 0; i < User.Inventory.Length; i++)
            {
                if (User.Inventory[i] != null && User.Inventory[i].UniqueID == p.Item.UniqueID)
                {
                    User.Inventory[i] = p.Item;
                    User.RefreshStats();
                    return;
                }
            }

            for (int i = 0; i < User.Equipment.Length; i++)
            {
                if (User.Equipment[i] != null && User.Equipment[i].UniqueID == p.Item.UniqueID)
                {
                    User.Equipment[i] = p.Item;
                    User.RefreshStats();
                    return;
                }
            }
        }

        private void ObjectSpell(S.ObjectSpell p)
        {
            SpellObject ob = new SpellObject(p.ObjectID);
            ob.Load(p);
        }

        private void ObjectDeco(S.ObjectDeco p)
        {
            DecoObject ob = new DecoObject(p.ObjectID);
            ob.Load(p);
        }

        private void UserDash(S.UserDash p)
        {
            if (User.Direction == p.Direction && User.CurrentLocation == p.Location)
            {
                MapControl.NextAction = 0;
                return;
            }
            MirAction action = User.CurrentAction == MirAction.DashL ? MirAction.DashR : MirAction.DashL;
            for (int i = User.ActionFeed.Count - 1; i >= 0; i--)
            {
                if (User.ActionFeed[i].Action == MirAction.DashR)
                {
                    action = MirAction.DashL;
                    break;
                }
                if (User.ActionFeed[i].Action == MirAction.DashL)
                {
                    action = MirAction.DashR;
                    break;
                }
            }

            User.ActionFeed.Add(new QueuedAction { Action = action, Direction = p.Direction, Location = p.Location });
        }

        private void UserDashFail(S.UserDashFail p)
        {
            MapControl.NextAction = 0;
            User.ActionFeed.Add(new QueuedAction { Action = MirAction.DashFail, Direction = p.Direction, Location = p.Location });
        }

        private void ObjectDash(S.ObjectDash p)
        {
            if (p.ObjectID == User.ObjectID) return;

            for (int i = MapControl.Objects.Count - 1; i >= 0; i--)
            {
                MapObject ob = MapControl.Objects[i];
                if (ob.ObjectID != p.ObjectID) continue;

                MirAction action = MirAction.DashL;

                if (ob.ActionFeed.Count > 0 && ob.ActionFeed[ob.ActionFeed.Count - 1].Action == action)
                    action = MirAction.DashR;

                ob.ActionFeed.Add(new QueuedAction { Action = action, Direction = p.Direction, Location = p.Location });

                return;
            }
        }

        private void ObjectDashFail(S.ObjectDashFail p)
        {
            if (p.ObjectID == User.ObjectID) return;

            for (int i = MapControl.Objects.Count - 1; i >= 0; i--)
            {
                MapObject ob = MapControl.Objects[i];
                if (ob.ObjectID != p.ObjectID) continue;

                ob.ActionFeed.Add(new QueuedAction { Action = MirAction.DashFail, Direction = p.Direction, Location = p.Location });

                return;
            }
        }

        private void UserBackStep(S.UserBackStep p)
        {
            if (User.Direction == p.Direction && User.CurrentLocation == p.Location)
            {
                MapControl.NextAction = 0;
                return;
            }
            User.ActionFeed.Add(new QueuedAction { Action = MirAction.Jump, Direction = p.Direction, Location = p.Location });
        }

        private void ObjectBackStep(S.ObjectBackStep p)
        {
            if (p.ObjectID == User.ObjectID) return;

            for (int i = MapControl.Objects.Count - 1; i >= 0; i--)
            {
                MapObject ob = MapControl.Objects[i];
                if (ob.ObjectID != p.ObjectID) continue;

                ((PlayerObject)ob).JumpDistance = p.Distance;

                ob.ActionFeed.Add(new QueuedAction { Action = MirAction.Jump, Direction = p.Direction, Location = p.Location });

                return;
            }
        }

        private void UserDashAttack(S.UserDashAttack p)
        {
            if (User.Direction == p.Direction && User.CurrentLocation == p.Location)
            {
                MapControl.NextAction = 0;
                return;
            }
            //User.JumpDistance = p.Distance;
            User.ActionFeed.Add(new QueuedAction { Action = MirAction.DashAttack, Direction = p.Direction, Location = p.Location });
        }

        private void ObjectDashAttack(S.ObjectDashAttack p)
        {
            if (p.ObjectID == User.ObjectID) return;

            for (int i = MapControl.Objects.Count - 1; i >= 0; i--)
            {
                MapObject ob = MapControl.Objects[i];
                if (ob.ObjectID != p.ObjectID) continue;

                ((PlayerObject)ob).JumpDistance = p.Distance;

                ob.ActionFeed.Add(new QueuedAction { Action = MirAction.DashAttack, Direction = p.Direction, Location = p.Location });

                return;
            }
        }

        private void UserAttackMove(S.UserAttackMove p)//Warrior Skill - SlashingBurst
        {
            MapControl.NextAction = 0;
            if (User.CurrentLocation == p.Location && User.Direction == p.Direction) return;


            MapControl.RemoveObject(User);
            User.CurrentLocation = p.Location;
            User.MapLocation = p.Location;
            MapControl.AddObject(User);


            MapControl.FloorValid = false;
            MapControl.InputDelay = CMain.Time + 400;


            if (User.Dead) return;


            User.ClearMagic();
            User.QueuedAction = null;


            for (int i = User.ActionFeed.Count - 1; i >= 0; i--)
            {
                if (User.ActionFeed[i].Action == MirAction.Pushed) continue;
                User.ActionFeed.RemoveAt(i);
            }


            User.SetAction();

            User.ActionFeed.Add(new QueuedAction { Action = MirAction.Standing, Direction = p.Direction, Location = p.Location });
        }

        private void SetConcentration(S.SetConcentration p)
        {
            for (int i = MapControl.Objects.Count - 1; i >= 0; i--)
            {
                if (MapControl.Objects[i].Race != ObjectType.Player) continue;

                PlayerObject ob = MapControl.Objects[i] as PlayerObject;
                if (ob.ObjectID != p.ObjectID) continue;

                ob.Concentrating = p.Enabled;
                ob.ConcentrateInterrupted = p.Interrupted;

                if (p.Enabled && !p.Interrupted)
                {
                    int idx = InterruptionEffect.GetOwnerEffectID(ob.ObjectID);

                    if (idx < 0)
                    {
                        ob.Effects.Add(new InterruptionEffect(Libraries.Magic3, 1860, 8, 8 * 100, ob, true));
                        SoundManager.PlaySound(20000 + 129 * 10);
                    }
                }
                break;
            }
        }

        private void SetElemental(S.SetElemental p)
        {
            for (int i = MapControl.Objects.Count - 1; i >= 0; i--)
            {
                if (MapControl.Objects[i].Race != ObjectType.Player) continue;

                PlayerObject ob = MapControl.Objects[i] as PlayerObject;
                if (ob.ObjectID != p.ObjectID) continue;

                ob.HasElements = p.Enabled;
                ob.ElementCasted = p.Casted && User.ObjectID != p.ObjectID;
                ob.ElementsLevel = (int)p.Value;
                int elementType = (int)p.ElementType;
                int maxExp = (int)p.ExpLast;

                if (p.Enabled && p.ElementType > 0)
                {
                    ob.Effects.Add(new ElementsEffect(Libraries.Magic3, 1630 + ((elementType - 1) * 10), 10, 10 * 100, ob, true, 1 + (elementType - 1), maxExp, User.ObjectID == p.ObjectID && ((elementType == 4 || elementType == 3))));
                }
            }
        }

        private void RemoveDelayedExplosion(S.RemoveDelayedExplosion p)
        {
            //if (p.ObjectID == User.ObjectID) return;

            int effectid = DelayedExplosionEffect.GetOwnerEffectID(p.ObjectID);
            if (effectid >= 0)
                DelayedExplosionEffect.effectlist[effectid].Remove();
        }

        private void SetBindingShot(S.SetBindingShot p)
        {
            if (p.ObjectID == User.ObjectID) return;

            for (int i = MapControl.Objects.Count - 1; i >= 0; i--)
            {
                MapObject ob = MapControl.Objects[i];
                if (ob.ObjectID != p.ObjectID) continue;
                if (ob.Race != ObjectType.Monster) continue;

                TrackableEffect NetCast = new TrackableEffect(new Effect(Libraries.MagicC, 0, 8, 700, ob));
                NetCast.EffectName = "BindingShotDrop";

                //TrackableEffect NetDropped = new TrackableEffect(new Effect(Libraries.ArcherMagic, 7, 1, 1000, ob, CMain.Time + 600) { Repeat = true, RepeatUntil = CMain.Time + (p.Value - 1500) });
                TrackableEffect NetDropped = new TrackableEffect(new Effect(Libraries.MagicC, 7, 1, 1000, ob) { Repeat = true, RepeatUntil = CMain.Time + (p.Value - 1500) });
                NetDropped.EffectName = "BindingShotDown";

                TrackableEffect NetFall = new TrackableEffect(new Effect(Libraries.MagicC, 8, 8, 700, ob));
                NetFall.EffectName = "BindingShotFall";

                NetDropped.Complete += (o1, e1) =>
                {
                    SoundManager.PlaySound(20000 + 130 * 10 + 6);//sound M130-6
                    ob.Effects.Add(NetFall);
                };
                NetCast.Complete += (o, e) =>
                {
                    SoundManager.PlaySound(20000 + 130 * 10 + 5);//sound M130-5
                    ob.Effects.Add(NetDropped);
                };
                ob.Effects.Add(NetCast);
                break;
            }
        }

        private void SendOutputMessage(S.SendOutputMessage p)
        {
            OutputMessage(p.Message, p.Type);
        }

        private void NPCConsign()
        {
            BeginMobileMarketListing(MarketPanelType.Consign);
        }
        private void NPCMarket(S.NPCMarket p)
        {
            for (int i = 0; i < p.Listings.Count; i++)
                Bind(p.Listings[i].Item);

            MarketPanelType panelType = MarketPanelType.Market;

            if (p.UserMode)
            {
                if (p.Listings != null && p.Listings.Any(x => x != null && x.ItemType == MarketItemType.Auction))
                    panelType = MarketPanelType.Auction;
                else
                    panelType = MarketPanelType.Consign;
            }

            MapControl?.CancelMagicLocationSelection(showMessage: false);

            if (Environment.OSVersion.Platform == PlatformID.Win32NT)
                return;

            MonoShare.FairyGuiHost.UpdateMobileTrustMerchant(panelType, p.Listings, p.Pages, p.UserMode);
            if (MonoShare.FairyGuiHost.TryShowMobileWindowByKeywords("TrustMerchant", new[] { "拍卖行_AuctionGuildUI", "信任商人", "交易行", "拍卖", "摆摊", "Trust", "Merchant", "Market" }))
                MonoShare.FairyGuiHost.HideAllMobileWindowsExcept("TrustMerchant");
        }
        private void NPCMarketPage(S.NPCMarketPage p)
        {
            for (int i = 0; i < p.Listings.Count; i++)
                Bind(p.Listings[i].Item);

            if (Environment.OSVersion.Platform == PlatformID.Win32NT)
                return;

            MonoShare.FairyGuiHost.AppendMobileTrustMerchantPage(p.Listings);
            if (MonoShare.FairyGuiHost.IsMobileWindowVisible("TrustMerchant"))
            {
                // 保持窗口可见；具体列表刷新待接入层实现
            }
        }
        private void ConsignItem(S.ConsignItem p)
        {
            if (!p.Success)
                return;

            if (User?.Inventory != null)
            {
                for (int i = 0; i < User.Inventory.Length; i++)
                {
                    if (User.Inventory[i]?.UniqueID != p.UniqueID)
                        continue;

                    User.Inventory[i] = null;
                    break;
                }
            }

            User?.RefreshStats();

            MarketPanelType listingType = _lastMobileMarketListingType == MarketPanelType.Auction ? MarketPanelType.Auction : MarketPanelType.Consign;
            Network.Enqueue(new C.MarketSearch
            {
                Match = string.Empty,
                Type = ItemType.杂物,
                Usermode = true,
                MinShape = 0,
                MaxShape = 5000,
                MarketType = listingType,
            });
        }
        private void MarketFail(S.MarketFail p)
        {
            string message = p.Reason switch
            {
                0 => "死亡状态不能使用。",
                1 => "未与信任商人对话或不在可用范围内。",
                2 => "商品已售出。",
                3 => "物品已过期。",
                4 => "金币不足。",
                5 => "背包空间或负重不足。",
                6 => "不能购买自己的物品。",
                7 => "离信任商人太远。",
                8 => "交易失败（佣金不足或金币已达上限）。",
                9 => "出价过低（未达到最低竞价）。",
                10 => "拍卖已结束。",
                _ => $"交易失败（原因码：{p.Reason}）"
            };

            MirMessageBox.Show(message);

        }
        private void MarketSuccess(S.MarketSuccess p)
        {
            if (!string.IsNullOrWhiteSpace(p.Message))
                MirMessageBox.Show(p.Message);
        }
        private void ObjectSitDown(S.ObjectSitDown p)
        {
            if (p.ObjectID == User.ObjectID) return;

            for (int i = MapControl.Objects.Count - 1; i >= 0; i--)
            {
                MapObject ob = MapControl.Objects[i];
                if (ob.ObjectID != p.ObjectID) continue;
                if (ob.Race != ObjectType.Monster) continue;
                ob.SitDown = p.Sitting;
                ob.ActionFeed.Add(new QueuedAction { Action = MirAction.SitDown, Direction = p.Direction, Location = p.Location });
                return;
            }
        }

        private void BaseStatsInfo(S.BaseStatsInfo p)
        {
            User.CoreStats = p.Stats;
            User.RefreshStats();
        }

        private void UserName(S.UserName p)
        {
            for (int i = 0; i < UserIdList.Count; i++)
                if (UserIdList[i].Id == p.Id)
                {
                    UserIdList[i].UserName = p.Name;
                    break;
                }
            DisposeItemLabel();
            HoverItem = null;
        }

        private void ChatItemStats(S.ChatItemStats p)
        {
            //for (int i = 0; i < ChatItemList.Count; i++)
            //    if (ChatItemList[i].ID == p.ChatItemId)
            //    {
            //        ChatItemList[i].ItemStats = p.Stats;
            //        ChatItemList[i].RecievedTick = CMain.Time;
            //    }
        }

        private void GuildInvite(S.GuildInvite p)
        {
            if (p == null)
                return;

            var messageBox = new MirMessageBox(string.Format("你想加入<{0}>行会？", p.Name), MirMessageBoxButtons.YesNo);

            if (messageBox.YesButton != null)
                messageBox.YesButton.Click += (o, e) => Network.Enqueue(new C.GuildInvite { AcceptInvite = true });

            if (messageBox.NoButton != null)
                messageBox.NoButton.Click += (o, e) => Network.Enqueue(new C.GuildInvite { AcceptInvite = false });

            messageBox.Show();
        }

        private void GuildNameRequest(S.GuildNameRequest p)
        {
            const int minLength = 3;
            const int maxLength = 20;

            PromptMobileText(
                title: "创建行会",
                message: $"请输入行会名称（{minLength}-{maxLength} 字符，不能包含 \\\\）。",
                initialText: string.Empty,
                maxLength: maxLength,
                onOk: name =>
                {
                    if (string.IsNullOrWhiteSpace(name))
                    {
                        Network.Enqueue(new C.GuildNameReturn { Name = string.Empty });
                        return;
                    }

                    string trimmed = name.Trim();
                    if (trimmed.Contains('\\') || trimmed.Length < minLength || trimmed.Length > maxLength)
                    {
                        MirMessageBox.Show("行会名称不合法，请重新输入。");
                        GuildNameRequest(p);
                        return;
                    }

                    Network.Enqueue(new C.GuildNameReturn { Name = trimmed });
                },
                onCancel: () => Network.Enqueue(new C.GuildNameReturn { Name = string.Empty }));
        }

        private void GuildRequestWar(S.GuildRequestWar p)
        {
            //MirInputBox inputBox = new MirInputBox("请输入你想开启行会战的行会名称.");

            //inputBox.OKButton.Click += (o, e) =>
            //{
            //    Network.Enqueue(new C.GuildWarReturn { Name = inputBox.InputTextBox.Text });
            //    inputBox.Dispose();
            //};
            //inputBox.Show();
        }

        private void GuildNoticeChange(S.GuildNoticeChange p)
        {
            if (p == null)
                return;

            if (p.update == -1)
            {
                if (MonoShare.FairyGuiHost.IsMobileWindowVisible("Guild"))
                    Network.Enqueue(new C.RequestGuildInfo { Type = 0 });
                return;
            }

            MonoShare.FairyGuiHost.UpdateMobileGuildNotice(p.notice);
        }
        private void GuildMemberChange(S.GuildMemberChange p)
        {
            if (p == null)
                return;

            switch (p.Status)
            {
                case 0: // logged off
                    MonoShare.FairyGuiHost.SetMobileGuildMemberOnline(p.Name, online: false);
                    break;
                case 1: // logged on
                    MobileReceiveChat(string.Format("{0} 已登录游戏", p.Name), ChatType.Guild);
                    MonoShare.FairyGuiHost.SetMobileGuildMemberOnline(p.Name, online: true);
                    break;
                case 2: // new member
                    MobileReceiveChat(string.Format("{0} 加入行会", p.Name), ChatType.Guild);
                    RequestGuildMemberRefreshIfVisible();
                    break;
                case 3: // kicked member
                    MobileReceiveChat(string.Format("{0} 被行会除名", p.Name), ChatType.Guild);
                    RequestGuildMemberRefreshIfVisible();
                    break;
                case 4: // member left
                    MobileReceiveChat(string.Format("{0} 离开了行会", p.Name), ChatType.Guild);
                    RequestGuildMemberRefreshIfVisible();
                    break;
                case 5: // rank change
                case 6: // new rank
                case 7: // rank option changed
                case 8: // my rank changed
                    RequestGuildMemberRefreshIfVisible();
                    break;
                case 255:
                    MonoShare.FairyGuiHost.UpdateMobileGuildRanks(p.Ranks);
                    break;
            }
        }

        private void RequestGuildMemberRefreshIfVisible()
        {
            if (!MonoShare.FairyGuiHost.IsMobileWindowVisible("Guild"))
                return;

            if (CMain.Time < _nextGuildMemberRefreshAllowedAtMs)
                return;

            _nextGuildMemberRefreshAllowedAtMs = CMain.Time + 1200;
            Network.Enqueue(new C.RequestGuildInfo { Type = 1 });
        }

        private void GuildStatus(S.GuildStatus p)
        {
            if (p == null || User == null)
                return;

            bool guildChanged = !string.Equals(User.GuildName, p.GuildName, StringComparison.Ordinal);

            User.GuildName = p.GuildName;
            User.GuildRankName = p.GuildRankName;

            if (Environment.OSVersion.Platform != PlatformID.Win32NT)
                MonoShare.FairyGuiHost.UpdateMobileGuildStatus(p);

            if (string.IsNullOrWhiteSpace(p.GuildName))
            {
                MonoShare.FairyGuiHost.TryHideMobileWindow("Guild");
                return;
            }

            if (guildChanged && MonoShare.FairyGuiHost.IsMobileWindowVisible("Guild"))
            {
                Network.Enqueue(new C.RequestGuildInfo { Type = 0 });
                Network.Enqueue(new C.RequestGuildInfo { Type = 1 });
            }
        }

        private void GuildExpGain(S.GuildExpGain p)
        {
            //OutputMessage(string.Format("Guild Experience Gained {0}.", p.Amount));
            //GuildDialog.Experience += p.Amount;
        }

        private void GuildStorageGoldChange(S.GuildStorageGoldChange p)
        {
            //switch (p.Type)
            //{
            //    case 0:
            //        ChatDialog.ReceiveChat(String.Format("{0}存入{1}金币到行会基金.", p.Name, p.Amount), ChatType.Guild);
            //        GuildDialog.Gold += p.Amount;
            //        break;
            //    case 1:
            //        ChatDialog.ReceiveChat(String.Format("{0}从行会基金取回{1}金币.", p.Name, p.Amount), ChatType.Guild);
            //        if (GuildDialog.Gold > p.Amount)
            //            GuildDialog.Gold -= p.Amount;
            //        else
            //            GuildDialog.Gold = 0;
            //        break;
            //    case 2:
            //        if (GuildDialog.Gold > p.Amount)
            //            GuildDialog.Gold -= p.Amount;
            //        else
            //            GuildDialog.Gold = 0;
            //        break;
            //    case 3:
            //        GuildDialog.Gold += p.Amount;
            //        break;
            //}
        }

        private void GuildStorageItemChange(S.GuildStorageItemChange p)
        {
            //MirItemCell fromCell = null;
            //MirItemCell toCell = null;
            //switch (p.Type)
            //{
            //    case 0://store
            //        toCell = GuildDialog.StorageGrid[p.To];

            //        if (toCell == null) return;

            //        toCell.Locked = false;
            //        toCell.Item = p.Item.Item;
            //        Bind(toCell.Item);
            //        if (p.User != User.Id) return;
            //        fromCell = p.From < User.BeltIdx ? BeltDialog.Grid[p.From] : InventoryDialog.Grid[p.From - User.BeltIdx];
            //        fromCell.Locked = false;
            //        if (fromCell != null)
            //            fromCell.Item = null;
            //        User.RefreshStats();
            //        break;
            //    case 1://retrieve
            //        fromCell = GuildDialog.StorageGrid[p.From];

            //        if (fromCell == null) return;
            //        fromCell.Locked = false;

            //        if (p.User != User.Id)
            //        {
            //            fromCell.Item = null;
            //            return;
            //        }
            //        toCell = p.To < User.BeltIdx ? BeltDialog.Grid[p.To] : InventoryDialog.Grid[p.To - User.BeltIdx];
            //        if (toCell == null) return;
            //        toCell.Locked = false;
            //        toCell.Item = fromCell.Item;
            //        fromCell.Item = null;
            //        break;

            //    case 2:
            //        toCell = GuildDialog.StorageGrid[p.To];
            //        fromCell = GuildDialog.StorageGrid[p.From];

            //        if (toCell == null || fromCell == null) return;

            //        toCell.Locked = false;
            //        fromCell.Locked = false;
            //        fromCell.Item = toCell.Item;
            //        toCell.Item = p.Item.Item;

            //        Bind(toCell.Item);
            //        if (fromCell.Item != null)
            //            Bind(fromCell.Item);
            //        break;
            //    case 3://failstore
            //        fromCell = p.From < User.BeltIdx ? BeltDialog.Grid[p.From] : InventoryDialog.Grid[p.From - User.BeltIdx];
            //        toCell = GuildDialog.StorageGrid[p.To];

            //        if (toCell == null || fromCell == null) return;

            //        toCell.Locked = false;
            //        fromCell.Locked = false;
            //        break;
            //    case 4://failretrieve
            //        toCell = p.To < User.BeltIdx ? BeltDialog.Grid[p.To] : InventoryDialog.Grid[p.To - User.BeltIdx];
            //        fromCell = GuildDialog.StorageGrid[p.From];

            //        if (toCell == null || fromCell == null) return;

            //        toCell.Locked = false;
            //        fromCell.Locked = false;
            //        break;
            //    case 5://failmove
            //        fromCell = GuildDialog.StorageGrid[p.To];
            //        toCell = GuildDialog.StorageGrid[p.From];

            //        if (toCell == null || fromCell == null) return;

            //        GuildDialog.StorageGrid[p.From].Locked = false;
            //        GuildDialog.StorageGrid[p.To].Locked = false;
            //        break;
            //}
        }
        private void GuildStorageList(S.GuildStorageList p)
        {
            //for (int i = 0; i < p.Items.Length; i++)
            //{
            //    if (i >= GuildDialog.StorageGrid.Length) break;
            //    if (p.Items[i] == null)
            //    {
            //        GuildDialog.StorageGrid[i].Item = null;
            //        continue;
            //    }
            //    GuildDialog.StorageGrid[i].Item = p.Items[i].Item;
            //    Bind(GuildDialog.StorageGrid[i].Item);
            //}
        }

        private void MarriageRequest(S.MarriageRequest p)
        {
            if (p == null)
                return;

            string name = (p.Name ?? string.Empty).Trim();
            if (name.Length <= 0)
                name = "对方";

            MirMessageBox messageBox = new MirMessageBox($"{name} 向你求婚，是否同意？", MirMessageBoxButtons.YesNo);
            messageBox.YesButton.Click += (o, e) => Network.Enqueue(new C.MarriageReply { AcceptInvite = true });
            messageBox.NoButton.Click += (o, e) => Network.Enqueue(new C.MarriageReply { AcceptInvite = false });
            messageBox.Show();
        }

        private void DivorceRequest(S.DivorceRequest p)
        {
            if (p == null)
                return;

            string name = (p.Name ?? string.Empty).Trim();
            if (name.Length <= 0)
                name = "对方";

            MirMessageBox messageBox = new MirMessageBox($"{name} 请求离婚，是否同意？", MirMessageBoxButtons.YesNo);
            messageBox.YesButton.Click += (o, e) => Network.Enqueue(new C.DivorceReply { AcceptInvite = true });
            messageBox.NoButton.Click += (o, e) => Network.Enqueue(new C.DivorceReply { AcceptInvite = false });
            messageBox.Show();
        }

        private void MentorRequest(S.MentorRequest p)
        {
            if (p == null)
                return;

            string name = (p.Name ?? string.Empty).Trim();
            if (name.Length <= 0)
                name = "对方";

            MirMessageBox messageBox = new MirMessageBox($"{name} (等级 {p.Level}) 向你拜师，是否同意？", MirMessageBoxButtons.YesNo);
            messageBox.YesButton.Click += (o, e) => Network.Enqueue(new C.MentorReply { AcceptInvite = true });
            messageBox.NoButton.Click += (o, e) => Network.Enqueue(new C.MentorReply { AcceptInvite = false });
            messageBox.Show();
        }

        private bool UpdateGuildBuff(GuildBuff buff, bool Remove = false)
        {
            //for (int i = 0; i < GuildDialog.EnabledBuffs.Count; i++)
            //{
            //    if (GuildDialog.EnabledBuffs[i].Id == buff.Id)
            //    {
            //        if (Remove)
            //        {
            //            GuildDialog.EnabledBuffs.RemoveAt(i);
            //        }
            //        else
            //            GuildDialog.EnabledBuffs[i] = buff;
            //        return true;
            //    }
            //}
            return false;
        }

        private void GuildBuffList(S.GuildBuffList p)
        {
            ////getting the list of all guildbuffs on server?
            //if (p.GuildBuffs.Count > 0)
            //    GuildDialog.GuildBuffInfos.Clear();
            //for (int i = 0; i < p.GuildBuffs.Count; i++)
            //{
            //    GuildDialog.GuildBuffInfos.Add(p.GuildBuffs[i]);
            //}
            ////getting the list of all active/removedbuffs?
            //for (int i = 0; i < p.ActiveBuffs.Count; i++)
            //{
            //    //if (p.ActiveBuffs[i].ActiveTimeRemaining > 0)
            //    //    p.ActiveBuffs[i].ActiveTimeRemaining = Convert.ToInt32(CMain.Time / 1000) + (p.ActiveBuffs[i].ActiveTimeRemaining * 60);
            //    if (UpdateGuildBuff(p.ActiveBuffs[i], p.Remove == 1)) continue;
            //    if (!(p.Remove == 1))
            //    {
            //        GuildDialog.EnabledBuffs.Add(p.ActiveBuffs[i]);
            //        //CreateGuildBuff(p.ActiveBuffs[i]);
            //    }
            //}

            //for (int i = 0; i < GuildDialog.EnabledBuffs.Count; i++)
            //{
            //    if (GuildDialog.EnabledBuffs[i].Info == null)
            //    {
            //        GuildDialog.EnabledBuffs[i].Info = GuildDialog.FindGuildBuffInfo(GuildDialog.EnabledBuffs[i].Id);
            //    }
            //}

            //ClientBuff buff = Buffs.FirstOrDefault(e => e.Type == BuffType.Guild);

            //if (GuildDialog.EnabledBuffs.Any(e => e.Active))
            //{
            //    if (buff == null)
            //    {
            //        buff = new ClientBuff { Type = BuffType.Guild, ObjectID = User.ObjectID, Caster = "Guild", Infinite = true, Values = new int[0] };

            //        Buffs.Add(buff);
            //        BuffsDialog.CreateBuff(buff);
            //    }

            //    GuildDialog.UpdateActiveStats();
            //}
            //else
            //{
            //    RemoveBuff(new S.RemoveBuff { ObjectID = User.ObjectID, Type = BuffType.Guild });
            //}

            User.RefreshStats();
        }

        private void TradeRequest(S.TradeRequest p)
        {
            string name = p?.Name ?? string.Empty;
            if (string.IsNullOrWhiteSpace(name))
                name = "未知玩家";

            MirMessageBox messageBox = new MirMessageBox($"玩家 {name} 向你发起交易请求。", MirMessageBoxButtons.YesNo);
            messageBox.YesButton.Click += (o, e) => Network.Enqueue(new C.TradeReply { AcceptInvite = true });
            messageBox.NoButton.Click += (o, e) => Network.Enqueue(new C.TradeReply { AcceptInvite = false });
            messageBox.Show();
        }
        private void TradeAccept(S.TradeAccept p)
        {
            if (Environment.OSVersion.Platform == PlatformID.Win32NT)
                return;

            MapControl?.CancelMagicLocationSelection(showMessage: false);

            string guestName = p?.Name ?? string.Empty;
            MonoShare.FairyGuiHost.BeginMobileTrade(guestName);

            if (MonoShare.FairyGuiHost.TryShowMobileWindowByKeywords("Trade", new[] { "交易_DDealDlgUI", "交易_DDealRemoteDlgUI", "交易_DealWin_MainUI", "交易", "Trade", "Deal" }))
                MonoShare.FairyGuiHost.HideAllMobileWindowsExcept("Trade");
        }
        private void TradeGold(S.TradeGold p)
        {
            if (Environment.OSVersion.Platform == PlatformID.Win32NT)
                return;

            try
            {
                MonoShare.FairyGuiHost.UpdateMobileTradeGuestGold(p?.Amount ?? 0);
            }
            catch
            {
            }
        }
        private void TradeItem(S.TradeItem p)
        {
            if (Environment.OSVersion.Platform == PlatformID.Win32NT)
                return;

            try
            {
                MonoShare.FairyGuiHost.UpdateMobileTradeGuestItems(p?.TradeItems);
            }
            catch
            {
            }
        }
        private void TradeConfirm()
        {
            UserObject user = MapObject.User;
            if (user != null)
            {
                user.TradeLocked = false;
                user.TradeGoldAmount = 0;
                if (user.Trade != null)
                    Array.Clear(user.Trade, 0, user.Trade.Length);
            }

            MonoShare.FairyGuiHost.TryHideMobileWindow("Trade");
            MonoShare.FairyGuiHost.EndMobileTrade();
            MobileReceiveChat("交易完成。", ChatType.Hint);
        }
        private void TradeCancel(S.TradeCancel p)
        {
            UserObject user = MapObject.User;

            if (p != null && p.Unlock)
            {
                if (user != null)
                    user.TradeLocked = false;
                MonoShare.FairyGuiHost.MarkMobileTradeDirty();
                OutputMessage("交易已解锁。");
                return;
            }

            if (user != null)
            {
                user.TradeLocked = false;
                user.TradeGoldAmount = 0;
                if (user.Trade != null)
                    Array.Clear(user.Trade, 0, user.Trade.Length);
            }

            MonoShare.FairyGuiHost.TryHideMobileWindow("Trade");
            MonoShare.FairyGuiHost.EndMobileTrade();
            MobileReceiveChat("交易取消。", ChatType.Hint);
        }
        private void NPCAwakening()
        {
            //if (NPCAwakeDialog.Visible != true)
            //    NPCAwakeDialog.Show();
        }
        private void NPCDisassemble()
        {
            //if (!NPCDialog.Visible) return;
            //NPCDropDialog.PType = PanelType.Disassemble;
            //NPCDropDialog.Show();
        }
        private void NPCDowngrade()
        {
            //if (!NPCDialog.Visible) return;
            //NPCDropDialog.PType = PanelType.Downgrade;
            //NPCDropDialog.Show();
        }
        private void NPCReset()
        {
            //if (!NPCDialog.Visible) return;
            //NPCDropDialog.PType = PanelType.Reset;
            //NPCDropDialog.Show();
        }
        private void AwakeningNeedMaterials(S.AwakeningNeedMaterials p)
        {
            //NPCAwakeDialog.setNeedItems(p.Materials, p.MaterialsCount);
        }
        private void AwakeningLockedItem(S.AwakeningLockedItem p)
        {
            if (p == null)
                return;

            MonoShare.FairyGuiHost.SetMobileAwakeningItemLocked(p.UniqueID, p.Locked);
        }
        private void Awakening(S.Awakening p)
        {
            //if (NPCAwakeDialog.Visible)
            //    NPCAwakeDialog.Hide();
            //if (InventoryDialog.Visible)
            //    InventoryDialog.Hide();

            //MirItemCell cell = InventoryDialog.GetCell((ulong)p.removeID);
            //if (cell != null)
            //{
            //    cell.Locked = false;
            //    cell.Item = null;
            //}

            //for (int i = 0; i < InventoryDialog.Grid.Length; i++)
            //{
            //    if (InventoryDialog.Grid[i].Locked == true)
            //    {
            //        InventoryDialog.Grid[i].Locked = false;

            //        //if (InventoryDialog.Grid[i].Item.UniqueID == (ulong)p.removeID)
            //        //{
            //        //    InventoryDialog.Grid[i].Item = null;
            //        //}
            //    }
            //}

            //for (int i = 0; i < NPCAwakeDialog.ItemsIdx.Length; i++)
            //{
            //    NPCAwakeDialog.ItemsIdx[i] = 0;
            //}

            //MirMessageBox messageBox = null;

            //switch (p.result)
            //{
            //    case -4:
            //        messageBox = new MirMessageBox("没有提供足够的材料.", MirMessageBoxButtons.OK);
            //        MapControl.AwakeningAction = false;
            //        break;
            //    case -3:
            //        messageBox = new MirMessageBox(GameLanguage.LowGold, MirMessageBoxButtons.OK);
            //        MapControl.AwakeningAction = false;
            //        break;
            //    case -2:
            //        messageBox = new MirMessageBox("觉醒已达到最高级别.", MirMessageBoxButtons.OK);
            //        MapControl.AwakeningAction = false;
            //        break;
            //    case -1:
            //        messageBox = new MirMessageBox("无法觉醒此物品.", MirMessageBoxButtons.OK);
            //        MapControl.AwakeningAction = false;
            //        break;
            //    case 0:
            //        //messageBox = new MirMessageBox("Upgrade Failed.", MirMessageBoxButtons.OK);
            //        break;
            //    case 1:
            //        //messageBox = new MirMessageBox("Upgrade Success.", MirMessageBoxButtons.OK);
            //        break;

            //}

            //if (messageBox != null) messageBox.Show();
        }

        private void ReceiveMail(S.ReceiveMail p)
        {
            NewMail = false;
            NewMailCounter = 0;
            User.Mail.Clear();

            User.Mail = p.Mail.OrderByDescending(e => !e.Locked).ThenByDescending(e => e.DateSent).ToList();

            foreach (ClientMail mail in User.Mail)
            {
                foreach (UserItem itm in mail.Items)
                {
                    Bind(itm);
                }
            }

            //display new mail received
            if (User.Mail.Any(e => e.Opened == false))
            {
                NewMail = true;
            }

            MonoShare.FairyGuiHost.UpdateMobileMailList(User.Mail);
        }

        private void MailLockedItem(S.MailLockedItem p)
        {
            if (p == null)
                return;

            MonoShare.FairyGuiHost.SetMobileMailItemLocked(p.UniqueID, p.Locked);
        }

        private void MailSendRequest(S.MailSendRequest p)
        {
            PromptMobileText(
                title: "寄包裹",
                message: "请输入收件人角色名",
                initialText: string.Empty,
                maxLength: 20,
                onOk: name =>
                {
                    string trimmed = (name ?? string.Empty).Trim();
                    if (string.IsNullOrWhiteSpace(trimmed))
                    {
                        OutputMessage("收件人不能为空。");
                        return;
                    }

                    ShowMobileMailComposeOverlay(trimmed, preferParcel: true);
                });
        }

        private void MailSent(S.MailSent p)
        {
            //for (int i = 0; i < InventoryDialog.Grid.Length; i++)
            //{
            //    if (InventoryDialog.Grid[i].Locked == true)
            //    {
            //        InventoryDialog.Grid[i].Locked = false;
            //    }
            //}

            //for (int i = 0; i < BeltDialog.Grid.Length; i++)
            //{
            //    if (BeltDialog.Grid[i].Locked == true)
            //    {
            //        BeltDialog.Grid[i].Locked = false;
            //    }
            //}

            MonoShare.FairyGuiHost.ClearMobileMailItemLocks();

            if (p == null)
                return;

            switch (p.Result)
            {
                case 1:
                    OutputMessage("邮件发送成功。");
                    break;
                case -1:
                    OutputMessage("邮件发送失败。");
                    break;
                default:
                    OutputMessage($"邮件发送结果：{p.Result}");
                    break;
            }
        }

        private void ParcelCollected(S.ParcelCollected p)
        {
            if (p == null)
                return;

            switch (p.Result)
            {
                case 1:
                    OutputMessage("已领取邮件附件。");
                    break;
                case 0:
                    OutputMessage("已收取所有待领包裹。");
                    break;
                case -1:
                    OutputMessage("没有可领取的包裹。");
                    break;
            }
        }

        private void ResizeInventory(S.ResizeInventory p)
        {
            Array.Resize(ref User.Inventory, p.Size);
            //InventoryDialog.RefreshInventory2();
        }

        private void ResizeStorage(S.ResizeStorage p)
        {
            Array.Resize(ref Storage, p.Size);
            User.HasExpandedStorage = p.HasExpandedStorage;
            User.ExpandedStorageExpiryTime = p.ExpiryTime;

            //StorageDialog.RefreshStorage2();
        }

        private void MailCost(S.MailCost p)
        {
            if (p == null)
                return;

            MonoShare.FairyGuiHost.UpdateMobileMailCost(p.Cost);
        }

        public void AddQuestItem(UserItem item)
        {
            //Redraw();

            //if (item.Info.StackSize > 1) //Stackable
            //{
            //    for (int i = 0; i < User.QuestInventory.Length; i++)
            //    {
            //        UserItem temp = User.QuestInventory[i];
            //        if (temp == null || item.Info != temp.Info || temp.Count >= temp.Info.StackSize) continue;

            //        if (item.Count + temp.Count <= temp.Info.StackSize)
            //        {
            //            temp.Count += item.Count;
            //            return;
            //        }
            //        item.Count -= (ushort)(temp.Info.StackSize - temp.Count);
            //        temp.Count = temp.Info.StackSize;
            //    }
            //}

            //for (int i = 0; i < User.QuestInventory.Length; i++)
            //{
            //    if (User.QuestInventory[i] != null) continue;
            //    User.QuestInventory[i] = item;
            //    return;
            //}
        }

        private void RequestReincarnation()
        {
            //if (CMain.Time > User.DeadTime && User.CurrentAction == MirAction.Dead)
            //{
            //    MirMessageBox messageBox = new MirMessageBox("你想复活吗?", MirMessageBoxButtons.YesNo);

            //    messageBox.YesButton.Click += (o, e) => Network.Enqueue(new C.AcceptReincarnation());

            //    messageBox.Show();
            //}
        }

        private void NewIntelligentCreature(S.NewIntelligentCreature p)
        {
            User.IntelligentCreatures.Add(p.Creature);

            //MirInputBox inputBox = new MirInputBox("请给你的宠物起个名字.");
            //inputBox.InputTextBox.Text = GameScene.User.IntelligentCreatures[User.IntelligentCreatures.Count - 1].CustomName;
            //inputBox.OKButton.Click += (o1, e1) =>
            //{
            //    if (IntelligentCreatureDialog.Visible) IntelligentCreatureDialog.Update();//refresh changes
            //    GameScene.User.IntelligentCreatures[User.IntelligentCreatures.Count - 1].CustomName = inputBox.InputTextBox.Text;
            //    Network.Enqueue(new C.UpdateIntelligentCreature { Creature = GameScene.User.IntelligentCreatures[User.IntelligentCreatures.Count - 1] });
            //    inputBox.Dispose();
            //};
            //inputBox.Show();
        }

        private void UpdateIntelligentCreatureList(S.UpdateIntelligentCreatureList p)
        {
            User.CreatureSummoned = p.CreatureSummoned;
            User.SummonedCreatureType = p.SummonedCreatureType;
            User.PearlCount = p.PearlCount;
            //if (p.CreatureList.Count != User.IntelligentCreatures.Count)
            //{
            //    User.IntelligentCreatures.Clear();
            //    for (int i = 0; i < p.CreatureList.Count; i++)
            //        User.IntelligentCreatures.Add(p.CreatureList[i]);

            //    for (int i = 0; i < IntelligentCreatureDialog.CreatureButtons.Length; i++)
            //        IntelligentCreatureDialog.CreatureButtons[i].Clear();

            //    IntelligentCreatureDialog.Hide();
            //}
            //else
            //{
            //    for (int i = 0; i < p.CreatureList.Count; i++)
            //        User.IntelligentCreatures[i] = p.CreatureList[i];
            //    if (IntelligentCreatureDialog.Visible) IntelligentCreatureDialog.Update();
            //}
        }

        private void IntelligentCreatureEnableRename(S.IntelligentCreatureEnableRename p)
        {
            //IntelligentCreatureDialog.CreatureRenameButton.Visible = true;
            //if (IntelligentCreatureDialog.Visible) IntelligentCreatureDialog.Update();
        }

        private void IntelligentCreaturePickup(S.IntelligentCreaturePickup p)
        {
            for (int i = MapControl.Objects.Count - 1; i >= 0; i--)
            {
                MapObject ob = MapControl.Objects[i];
                if (ob.ObjectID != p.ObjectID) continue;

                MonsterObject monOb = (MonsterObject)ob;

                if (monOb != null) monOb.PlayPickupSound();
            }
        }

        private void FriendUpdate(S.FriendUpdate p)
        {
            if (p == null)
                return;

            MonoShare.FairyGuiHost.UpdateMobileFriends(p.Friends);
        }

        private void LoverUpdate(S.LoverUpdate p)
        {
            // TODO: 将恋人/关系更新绑定到 FairyGUI 关系窗口
        }

        private void MentorUpdate(S.MentorUpdate p)
        {
            // TODO: 将师徒更新绑定到 FairyGUI 师徒窗口
        }

        private void GameShopUpdate(S.GameShopInfo p)
        {
            p.Item.Stock = p.StockLevel;
            GameShopInfoList.Add(p.Item);
            //if (p.Item.Date > DateTime.Now.AddDays(-7)) GameShopDialog.New.Visible = true;
            MonoShare.FairyGuiHost.MarkMobileShopDirty();
        }

        private void GameShopStock(S.GameShopStock p)
        {
            bool changed = false;

            for (int i = GameShopInfoList.Count - 1; i >= 0; i--)
            {
                GameShopItem item = GameShopInfoList[i];
                if (item == null)
                    continue;

                bool match = item.GIndex == p.GIndex;
                if (!match)
                    match = item.ItemIndex == p.GIndex || item.Info?.Index == p.GIndex;

                if (!match)
                    continue;

                if (p.StockLevel == 0)
                    GameShopInfoList.RemoveAt(i);
                else
                    item.Stock = p.StockLevel;

                changed = true;
            }

            if (changed)
                MonoShare.FairyGuiHost.MarkMobileShopDirty();
        }
        public void AddItem(UserItem item)
        {
            //Redraw();

            if (item.Info.StackSize > 1) //Stackable
            {
                for (int i = 0; i < User.Inventory.Length; i++)
                {
                    UserItem temp = User.Inventory[i];
                    if (temp == null || item.Info != temp.Info || temp.Count >= temp.Info.StackSize) continue;

                    if (item.Count + temp.Count <= temp.Info.StackSize)
                    {
                        temp.Count += item.Count;
                        return;
                    }
                    item.Count -= (ushort)(temp.Info.StackSize - temp.Count);
                    temp.Count = temp.Info.StackSize;
                }
            }

            if (item.Info.Type == ItemType.Potion || item.Info.Type == ItemType.Scroll || (item.Info.Type == ItemType.Script && item.Info.Effect == 1))
            {
                for (int i = 0; i < User.BeltIdx - 2; i++)
                {
                    if (User.Inventory[i] != null) continue;
                    User.Inventory[i] = item;
                    return;
                }
            }
            else if (item.Info.Type == ItemType.Amulet)
            {
                for (int i = 4; i < User.BeltIdx; i++)
                {
                    if (User.Inventory[i] != null) continue;
                    User.Inventory[i] = item;
                    return;
                }
            }
            else
            {
                for (int i = User.BeltIdx; i < User.Inventory.Length; i++)
                {
                    if (User.Inventory[i] != null) continue;
                    User.Inventory[i] = item;
                    return;
                }
            }

            for (int i = 0; i < User.Inventory.Length; i++)
            {
                if (User.Inventory[i] != null) continue;
                User.Inventory[i] = item;
                return;
            }
        }
        public static void Bind(UserItem item)
        {
            for (int i = 0; i < ItemInfoList.Count; i++)
            {
                if (ItemInfoList[i].Index != item.ItemIndex) continue;

                item.Info = ItemInfoList[i];

                for (int s = 0; s < item.Slots.Length; s++)
                {
                    if (item.Slots[s] == null) continue;

                    Bind(item.Slots[s]);
                }

                return;
            }
        }

        public static void BindQuest(ClientQuestProgress quest)
        {
            for (int i = 0; i < QuestInfoList.Count; i++)
            {
                if (QuestInfoList[i].Index != quest.Id) continue;

                quest.QuestInfo = QuestInfoList[i];

                return;
            }
        }

        public Color GradeNameColor(ItemGrade grade)
        {
            switch (grade)
            {
                case ItemGrade.Common:
                    return Color.Yellow;
                case ItemGrade.Rare:
                    return Color.DeepSkyBlue;
                case ItemGrade.Legendary:
                    return Color.DarkOrange;
                case ItemGrade.Mythical:
                    return Color.Plum;
                default:
                    return Color.Yellow;
            }
        }

        public void DisposeItemLabel()
        {
            if (ItemLabel != null && !ItemLabel.IsDisposed)
                ItemLabel.Dispose();
            ItemLabel = null;
        }
        public void DisposeMailLabel()
        {
            if (MailLabel != null && !MailLabel.IsDisposed)
                MailLabel.Dispose();
            MailLabel = null;
        }
        public void DisposeMemoLabel()
        {
            if (MemoLabel != null && !MemoLabel.IsDisposed)
                MemoLabel.Dispose();
            MemoLabel = null;
        }
        public void DisposeGuildBuffLabel()
        {
            if (GuildBuffLabel != null && !GuildBuffLabel.IsDisposed)
                GuildBuffLabel.Dispose();
            GuildBuffLabel = null;
        }

        public MirControl NameInfoLabel(UserItem item, bool inspect = false, bool hideDura = false)
        {
            //ushort level = inspect ? InspectDialog.Level : MapObject.User.Level;
            //MirClass job = inspect ? InspectDialog.Class : MapObject.User.Class;
            HoverItem = item;
            //ItemInfo realItem = Functions.GetRealItem(item.Info, level, job, ItemInfoList);

            string GradeString = "";
            switch (HoverItem.Info.Grade)
            {
                case ItemGrade.None:
                    break;
                case ItemGrade.Common:
                    GradeString = GameLanguage.ItemGradeCommon;
                    break;
                case ItemGrade.Rare:
                    GradeString = GameLanguage.ItemGradeRare;
                    break;
                case ItemGrade.Legendary:
                    GradeString = GameLanguage.ItemGradeLegendary;
                    break;
                case ItemGrade.Mythical:
                    GradeString = GameLanguage.ItemGradeMythical;
                    break;
            }
            //MirLabel nameLabel = new MirLabel
            //{
            //    AutoSize = true,
            //    ForeColour = GradeNameColor(HoverItem.Info.Grade),
            //    Location = new Point(4, 4),
            //    OutLine = true,
            //    Parent = ItemLabel,
            //    Text = HoverItem.Info.Grade != ItemGrade.None ? string.Format("{0}{1}{2}", HoverItem.Info.FriendlyName, "\n", GradeString) : HoverItem.Info.FriendlyName,
            //};

            //if (HoverItem.RefineAdded > 0)
            //    nameLabel.Text = "(*)" + nameLabel.Text;

            //ItemLabel.Size = new Size(Math.Max(ItemLabel.Size.Width, nameLabel.DisplayRectangle.Right + 4),
            //    Math.Max(ItemLabel.Size.Height, nameLabel.DisplayRectangle.Bottom));

            string text = "";

            if (HoverItem.Info.Durability > 0 && !hideDura)
            {
                switch (HoverItem.Info.Type)
                {
                    case ItemType.Amulet:
                        text += string.Format(" 数量 {0}/{1}", HoverItem.CurrentDura, HoverItem.MaxDura);
                        break;
                    case ItemType.Ore:
                        text += string.Format(" 纯度 {0}", Math.Floor(HoverItem.CurrentDura / 1000M));
                        break;
                    case ItemType.Meat:
                        text += string.Format(" 品质 {0}", Math.Floor(HoverItem.CurrentDura / 1000M));
                        break;
                    case ItemType.Mount:
                        text += string.Format(" 忠诚度 {0} / {1}", HoverItem.CurrentDura, HoverItem.MaxDura);
                        break;
                    case ItemType.Food:
                        text += string.Format(" 营养 {0}", HoverItem.CurrentDura);
                        break;
                    case ItemType.Gem:
                        break;
                    case ItemType.Potion:
                        break;
                    case ItemType.Transform:
                        break;
                    case ItemType.Pets:
                        if (HoverItem.Info.Shape == 26 || HoverItem.Info.Shape == 28)//WonderDrug, Knapsack
                        {
                            string strTime = Functions.PrintTimeSpanFromSeconds((HoverItem.CurrentDura * 3600), false);
                            text += string.Format(" 持续时间 {0}", strTime);
                        }
                        break;
                    default:
                        text += string.Format(" {0} {1}/{2}", GameLanguage.Durability, Math.Floor(HoverItem.CurrentDura / 1000M),
                                                   Math.Floor(HoverItem.MaxDura / 1000M));
                        break;
                }
            }

            string baseText = "";
            switch (HoverItem.Info.Type)
            {
                case ItemType.Nothing:
                    break;
                case ItemType.Weapon:
                    baseText = GameLanguage.ItemTypeWeapon;
                    break;
                case ItemType.Armour:
                    baseText = GameLanguage.ItemTypeArmour;
                    break;
                case ItemType.Helmet:
                    baseText = GameLanguage.ItemTypeHelmet;
                    break;
                case ItemType.Necklace:
                    baseText = GameLanguage.ItemTypeNecklace;
                    break;
                case ItemType.Bracelet:
                    baseText = GameLanguage.ItemTypeBracelet;
                    break;
                case ItemType.Ring:
                    baseText = GameLanguage.ItemTypeRing;
                    break;
                case ItemType.Amulet:
                    baseText = GameLanguage.ItemTypeAmulet;
                    break;
                case ItemType.Belt:
                    baseText = GameLanguage.ItemTypeBelt;
                    break;
                case ItemType.Boots:
                    baseText = GameLanguage.ItemTypeBoots;
                    break;
                case ItemType.Stone:
                    baseText = GameLanguage.ItemTypeStone;
                    break;
                case ItemType.Torch:
                    baseText = GameLanguage.ItemTypeTorch;
                    break;
                case ItemType.Potion:
                    baseText = GameLanguage.ItemTypePotion;
                    break;
                case ItemType.Ore:
                    baseText = GameLanguage.ItemTypeOre;
                    break;
                case ItemType.Meat:
                    baseText = GameLanguage.ItemTypeMeat;
                    break;
                case ItemType.CraftingMaterial:
                    baseText = GameLanguage.ItemTypeCraftingMaterial;
                    break;
                case ItemType.Scroll:
                    baseText = GameLanguage.ItemTypeScroll;
                    break;
                case ItemType.Gem:
                    baseText = GameLanguage.ItemTypeGem;
                    break;
                case ItemType.Mount:
                    baseText = GameLanguage.ItemTypeMount;
                    break;
                case ItemType.Book:
                    baseText = GameLanguage.ItemTypeBook;
                    break;
                case ItemType.Script:
                    baseText = GameLanguage.ItemTypeScript;
                    break;
                case ItemType.Reins:
                    baseText = GameLanguage.ItemTypeReins;
                    break;
                case ItemType.Bells:
                    baseText = GameLanguage.ItemTypeBells;
                    break;
                case ItemType.Saddle:
                    baseText = GameLanguage.ItemTypeSaddle;
                    break;
                case ItemType.Ribbon:
                    baseText = GameLanguage.ItemTypeRibbon;
                    break;
                case ItemType.Mask:
                    baseText = GameLanguage.ItemTypeMask;
                    break;
                case ItemType.Food:
                    baseText = GameLanguage.ItemTypeFood;
                    break;
                case ItemType.Hook:
                    baseText = GameLanguage.ItemTypeHook;
                    break;
                case ItemType.Float:
                    baseText = GameLanguage.ItemTypeFloat;
                    break;
                case ItemType.Bait:
                    baseText = GameLanguage.ItemTypeBait;
                    break;
                case ItemType.Finder:
                    baseText = GameLanguage.ItemTypeFinder;
                    break;
                case ItemType.Reel:
                    baseText = GameLanguage.ItemTypeReel;
                    break;
                case ItemType.Fish:
                    baseText = GameLanguage.ItemTypeFish;
                    break;
                case ItemType.Quest:
                    baseText = GameLanguage.ItemTypeQuest;
                    break;
                case ItemType.Awakening:
                    baseText = GameLanguage.ItemTypeAwakening;
                    break;
                case ItemType.Pets:
                    baseText = GameLanguage.ItemTypePets;
                    break;
                case ItemType.Transform:
                    baseText = GameLanguage.ItemTypeTransform;
                    break;
                case ItemType.Deco:
                    baseText = GameLanguage.ItemTypeDeco;
                    break;
            }

            if (HoverItem.WeddingRing != -1)
            {
                baseText = GameLanguage.WeddingRing;
            }

            baseText = string.Format(GameLanguage.ItemTextFormat, baseText, string.IsNullOrEmpty(baseText) ? "" : "\n", GameLanguage.Weight, HoverItem.Weight + text);

            //MirLabel etcLabel = new MirLabel
            //{
            //    AutoSize = true,
            //    ForeColour = Color.White,
            //    Location = new Point(4, nameLabel.DisplayRectangle.Bottom),
            //    OutLine = true,
            //    Parent = ItemLabel,
            //    Text = baseText
            //};

            //ItemLabel.Size = new Size(Math.Max(ItemLabel.Size.Width, etcLabel.DisplayRectangle.Right + 4),
            //    Math.Max(ItemLabel.Size.Height, etcLabel.DisplayRectangle.Bottom + 4));

            #region OUTLINE
            //MirControl outLine = new MirControl
            //{
            //    BackColour = Color.FromArgb(255, 50, 50, 50),
            //    Border = true,
            //    BorderColour = Color.Gray,
            //    NotControl = true,
            //    Parent = ItemLabel,
            //    Opacity = 0.4F,
            //    Location = new Point(0, 0)
            //};
            //outLine.Size = ItemLabel.Size;
            #endregion

            //return outLine;
            return null;
        }
        public MirControl AttackInfoLabel(UserItem item, bool Inspect = false, bool hideAdded = false)
        {
            //ushort level = Inspect ? InspectDialog.Level : MapObject.User.Level;
            //MirClass job = Inspect ? InspectDialog.Class : MapObject.User.Class;
            //HoverItem = item;
            //ItemInfo realItem = Functions.GetRealItem(item.Info, level, job, ItemInfoList);

            //ItemLabel.Size = new Size(ItemLabel.Size.Width, ItemLabel.Size.Height + 4);

            //bool fishingItem = false;

            //switch (HoverItem.Info.Type)
            //{
            //    case ItemType.Hook:
            //    case ItemType.Float:
            //    case ItemType.Bait:
            //    case ItemType.Finder:
            //    case ItemType.Reel:
            //        fishingItem = true;
            //        break;
            //    case ItemType.Weapon:
            //        if (Globals.FishingRodShapes.Contains(HoverItem.Info.Shape))
            //            fishingItem = true;
            //        break;
            //    default:
            //        fishingItem = false;
            //        break;
            //}

            //int count = 0;
            //int minValue = 0;
            //int maxValue = 0;
            //int addValue = 0;
            //string text = "";

            //#region Dura gem
            //minValue = realItem.Durability;

            //if (minValue > 0 && realItem.Type == ItemType.Gem)
            //{
            //    count++;
            //    text = string.Format("增加 +{0} 耐久度", minValue / 1000);
            //    MirLabel DuraLabel = new MirLabel
            //    {
            //        AutoSize = true,
            //        ForeColour = Color.White,
            //        Location = new Point(4, ItemLabel.DisplayRectangle.Bottom),
            //        OutLine = true,
            //        Parent = ItemLabel,
            //        Text = text
            //    };

            //    ItemLabel.Size = new Size(Math.Max(ItemLabel.Size.Width, DuraLabel.DisplayRectangle.Right + 4),
            //        Math.Max(ItemLabel.Size.Height, DuraLabel.DisplayRectangle.Bottom));
            //}

            //#endregion

            //#region DC
            //minValue = realItem.Stats[Stat.MinDC];
            //maxValue = realItem.Stats[Stat.MaxDC];
            //addValue = (!hideAdded && (!HoverItem.Info.NeedIdentify || HoverItem.Identified)) ? HoverItem.AddedStats[Stat.MaxDC] : 0;

            //if (minValue > 0 || maxValue > 0 || addValue > 0)
            //{
            //    count++;
            //    if (HoverItem.Info.Type != ItemType.Gem)
            //        text = string.Format(addValue > 0 ? GameLanguage.DC : GameLanguage.DC2, minValue, maxValue + addValue, addValue);
            //    else
            //        text = string.Format("增加 +{0} 攻击力", minValue + maxValue + addValue);
            //    MirLabel DCLabel = new MirLabel
            //    {
            //        AutoSize = true,
            //        ForeColour = addValue > 0 ? Color.Cyan : Color.White,
            //        Location = new Point(4, ItemLabel.DisplayRectangle.Bottom),
            //        OutLine = true,
            //        Parent = ItemLabel,
            //        Text = text
            //    };

            //    ItemLabel.Size = new Size(Math.Max(ItemLabel.Size.Width, DCLabel.DisplayRectangle.Right + 4),
            //        Math.Max(ItemLabel.Size.Height, DCLabel.DisplayRectangle.Bottom));
            //}

            //#endregion

            //#region MC

            //minValue = realItem.Stats[Stat.MinMC];
            //maxValue = realItem.Stats[Stat.MaxMC];
            //addValue = (!hideAdded && (!HoverItem.Info.NeedIdentify || HoverItem.Identified)) ? HoverItem.AddedStats[Stat.MaxMC] : 0;

            //if (minValue > 0 || maxValue > 0 || addValue > 0)
            //{
            //    count++;
            //    if (HoverItem.Info.Type != ItemType.Gem)
            //        text = string.Format(addValue > 0 ? GameLanguage.MC : GameLanguage.MC2, minValue, maxValue + addValue, addValue);
            //    else
            //        text = string.Format("增加 +{0} 魔法力", minValue + maxValue + addValue);
            //    MirLabel MCLabel = new MirLabel
            //    {
            //        AutoSize = true,
            //        ForeColour = addValue > 0 ? Color.Cyan : Color.White,
            //        Location = new Point(4, ItemLabel.DisplayRectangle.Bottom),
            //        OutLine = true,
            //        Parent = ItemLabel,
            //        Text = text
            //    };

            //    ItemLabel.Size = new Size(Math.Max(ItemLabel.Size.Width, MCLabel.DisplayRectangle.Right + 4),
            //        Math.Max(ItemLabel.Size.Height, MCLabel.DisplayRectangle.Bottom));
            //}

            //#endregion

            //#region SC

            //minValue = realItem.Stats[Stat.MinSC];
            //maxValue = realItem.Stats[Stat.MaxSC];
            //addValue = (!hideAdded && (!HoverItem.Info.NeedIdentify || HoverItem.Identified)) ? HoverItem.AddedStats[Stat.MaxSC] : 0;

            //if (minValue > 0 || maxValue > 0 || addValue > 0)
            //{
            //    count++;
            //    if (HoverItem.Info.Type != ItemType.Gem)
            //        text = string.Format(addValue > 0 ? GameLanguage.SC : GameLanguage.SC2, minValue, maxValue + addValue, addValue);
            //    else
            //        text = string.Format("增加 +{0} 道术力", minValue + maxValue + addValue);
            //    MirLabel SCLabel = new MirLabel
            //    {
            //        AutoSize = true,
            //        ForeColour = addValue > 0 ? Color.Cyan : Color.White,
            //        Location = new Point(4, ItemLabel.DisplayRectangle.Bottom),
            //        OutLine = true,
            //        Parent = ItemLabel,
            //        //Text = string.Format("SC + {0}~{1}", minValue, maxValue + addValue)
            //        Text = text
            //    };

            //    ItemLabel.Size = new Size(Math.Max(ItemLabel.Size.Width, SCLabel.DisplayRectangle.Right + 4),
            //        Math.Max(ItemLabel.Size.Height, SCLabel.DisplayRectangle.Bottom));
            //}

            //#endregion

            //#region LUCK / SUCCESS

            //minValue = realItem.Stats[Stat.Luck];
            //maxValue = 0;
            //addValue = (!hideAdded && (!HoverItem.Info.NeedIdentify || HoverItem.Identified)) ? HoverItem.AddedStats[Stat.Luck] : 0;

            //if (minValue != 0 || addValue != 0)
            //{
            //    count++;

            //    if (realItem.Type == ItemType.Pets && realItem.Shape == 28)
            //    {
            //        text = string.Format("包裹负重 + {0}% ", minValue + addValue);
            //    }
            //    else if (realItem.Type == ItemType.Potion && realItem.Shape == 4)
            //    {
            //        text = string.Format("经验 + {0}% ", minValue + addValue);
            //    }
            //    else
            //    {
            //        text = string.Format(minValue + addValue > 0 ? GameLanguage.Luck : "诅咒 + {0}", Math.Abs(minValue + addValue));
            //    }

            //    MirLabel LUCKLabel = new MirLabel
            //    {
            //        AutoSize = true,
            //        ForeColour = addValue > 0 ? Color.Cyan : Color.White,
            //        Location = new Point(4, ItemLabel.DisplayRectangle.Bottom),
            //        OutLine = true,
            //        Parent = ItemLabel,
            //        Text = text
            //    };

            //    ItemLabel.Size = new Size(Math.Max(ItemLabel.Size.Width, LUCKLabel.DisplayRectangle.Right + 4),
            //        Math.Max(ItemLabel.Size.Height, LUCKLabel.DisplayRectangle.Bottom));
            //}

            //#endregion



            //#region ACC

            //minValue = realItem.Stats[Stat.Accuracy];
            //maxValue = 0;
            //addValue = (!hideAdded && (!HoverItem.Info.NeedIdentify || HoverItem.Identified)) ? HoverItem.AddedStats[Stat.Accuracy] : 0;

            //if (minValue > 0 || maxValue > 0 || addValue > 0)
            //{
            //    count++;
            //    if (HoverItem.Info.Type != ItemType.Gem)
            //        text = string.Format(addValue > 0 ? GameLanguage.Accuracy : GameLanguage.Accuracy2, minValue + addValue, addValue);
            //    else
            //        text = string.Format("增加 +{0} 准确", minValue + maxValue + addValue);
            //    MirLabel ACCLabel = new MirLabel
            //    {
            //        AutoSize = true,
            //        ForeColour = addValue > 0 ? Color.Cyan : Color.White,
            //        Location = new Point(4, ItemLabel.DisplayRectangle.Bottom),
            //        OutLine = true,
            //        Parent = ItemLabel,
            //        //Text = string.Format("Accuracy + {0}", minValue + addValue)
            //        Text = text
            //    };

            //    ItemLabel.Size = new Size(Math.Max(ItemLabel.Size.Width, ACCLabel.DisplayRectangle.Right + 4),
            //        Math.Max(ItemLabel.Size.Height, ACCLabel.DisplayRectangle.Bottom));
            //}

            //#endregion

            //#region HOLY

            //minValue = realItem.Stats[Stat.Holy];
            //maxValue = 0;
            //addValue = 0;

            //if (minValue > 0 || maxValue > 0 || addValue > 0)
            //{
            //    count++;
            //    MirLabel HOLYLabel = new MirLabel
            //    {
            //        AutoSize = true,
            //        ForeColour = addValue > 0 ? Color.Cyan : Color.White,
            //        Location = new Point(4, ItemLabel.DisplayRectangle.Bottom),
            //        OutLine = true,
            //        Parent = ItemLabel,
            //        //Text = string.Format("Holy + {0}", minValue + addValue)
            //        Text = string.Format(addValue > 0 ? GameLanguage.Holy : GameLanguage.Holy2, minValue + addValue, addValue)
            //    };

            //    ItemLabel.Size = new Size(Math.Max(ItemLabel.Size.Width, HOLYLabel.DisplayRectangle.Right + 4),
            //        Math.Max(ItemLabel.Size.Height, HOLYLabel.DisplayRectangle.Bottom));
            //}

            //#endregion

            //#region ASPEED

            //minValue = realItem.Stats[Stat.AttackSpeed];
            //maxValue = 0;
            //addValue = (!hideAdded && (!HoverItem.Info.NeedIdentify || HoverItem.Identified)) ? HoverItem.AddedStats[Stat.AttackSpeed] : 0;

            //if (minValue != 0 || maxValue != 0 || addValue != 0)
            //{
            //    string plus = (addValue + minValue < 0) ? "" : "+";

            //    count++;
            //    if (HoverItem.Info.Type != ItemType.Gem)
            //    {
            //        string negative = "+";
            //        if (addValue < 0) negative = "";
            //        text = string.Format(addValue != 0 ? "攻击速度: " + plus + "{0} ({2}{1})" : "攻击速度: " + plus + "{0}", minValue + addValue, addValue, negative);
            //        //text = string.Format(addValue > 0 ? "A.Speed: + {0} (+{1})" : "A.Speed: + {0}", minValue + addValue, addValue);
            //    }
            //    else
            //        text = string.Format("攻击速度 +{0}", minValue + maxValue + addValue);
            //    MirLabel ASPEEDLabel = new MirLabel
            //    {
            //        AutoSize = true,
            //        ForeColour = addValue > 0 ? Color.Cyan : Color.White,
            //        Location = new Point(4, ItemLabel.DisplayRectangle.Bottom),
            //        OutLine = true,
            //        Parent = ItemLabel,
            //        //Text = string.Format("A.Speed + {0}", minValue + addValue)
            //        Text = text
            //    };

            //    ItemLabel.Size = new Size(Math.Max(ItemLabel.Size.Width, ASPEEDLabel.DisplayRectangle.Right + 4),
            //        Math.Max(ItemLabel.Size.Height, ASPEEDLabel.DisplayRectangle.Bottom));
            //}

            //#endregion

            //#region FREEZING

            //minValue = realItem.Stats[Stat.Freezing];
            //maxValue = 0;
            //addValue = (!hideAdded && (!HoverItem.Info.NeedIdentify || HoverItem.Identified)) ? HoverItem.AddedStats[Stat.Freezing] : 0;

            //if (minValue > 0 || maxValue > 0 || addValue > 0)
            //{
            //    count++;
            //    if (HoverItem.Info.Type != ItemType.Gem)
            //        text = string.Format(addValue > 0 ? "冰冻几率: + {0} (+{1})" : "冰冻几率: + {0}", minValue + addValue, addValue);
            //    else
            //        text = string.Format("冰冻几率 +{0}", minValue + maxValue + addValue);
            //    MirLabel FREEZINGLabel = new MirLabel
            //    {
            //        AutoSize = true,
            //        ForeColour = addValue > 0 ? Color.Cyan : Color.White,
            //        Location = new Point(4, ItemLabel.DisplayRectangle.Bottom),
            //        OutLine = true,
            //        Parent = ItemLabel,
            //        //Text = string.Format("Freezing + {0}", minValue + addValue)
            //        Text = text
            //    };

            //    ItemLabel.Size = new Size(Math.Max(ItemLabel.Size.Width, FREEZINGLabel.DisplayRectangle.Right + 4),
            //        Math.Max(ItemLabel.Size.Height, FREEZINGLabel.DisplayRectangle.Bottom));
            //}

            //#endregion

            //#region POISON

            //minValue = realItem.Stats[Stat.PoisonAttack];
            //maxValue = 0;
            //addValue = (!hideAdded && (!HoverItem.Info.NeedIdentify || HoverItem.Identified)) ? HoverItem.AddedStats[Stat.PoisonAttack] : 0;

            //if (minValue > 0 || maxValue > 0 || addValue > 0)
            //{
            //    count++;
            //    if (HoverItem.Info.Type != ItemType.Gem)
            //        text = string.Format(addValue > 0 ? "中毒几率: + {0} (+{1})" : "中毒几率: + {0}", minValue + addValue, addValue);
            //    else
            //        text = string.Format("中毒几率 +{0}", minValue + maxValue + addValue);
            //    MirLabel POISONLabel = new MirLabel
            //    {
            //        AutoSize = true,
            //        ForeColour = addValue > 0 ? Color.Cyan : Color.White,
            //        Location = new Point(4, ItemLabel.DisplayRectangle.Bottom),
            //        OutLine = true,
            //        Parent = ItemLabel,
            //        //Text = string.Format("Poison + {0}", minValue + addValue)
            //        Text = text
            //    };

            //    ItemLabel.Size = new Size(Math.Max(ItemLabel.Size.Width, POISONLabel.DisplayRectangle.Right + 4),
            //        Math.Max(ItemLabel.Size.Height, POISONLabel.DisplayRectangle.Bottom));
            //}

            //#endregion

            //#region CRITICALRATE / FLEXIBILITY

            //minValue = realItem.Stats[Stat.CriticalRate];
            //maxValue = 0;
            //addValue = (!hideAdded && (!HoverItem.Info.NeedIdentify || HoverItem.Identified)) ? HoverItem.AddedStats[Stat.CriticalRate] : 0;

            //if ((minValue > 0 || maxValue > 0 || addValue > 0) && (realItem.Type != ItemType.Gem))
            //{
            //    count++;
            //    MirLabel CRITICALRATELabel = new MirLabel
            //    {
            //        AutoSize = true,
            //        ForeColour = addValue > 0 ? Color.Cyan : Color.White,
            //        Location = new Point(4, ItemLabel.DisplayRectangle.Bottom),
            //        OutLine = true,
            //        Parent = ItemLabel,
            //        //Text = string.Format("Critical Chance + {0}", minValue + addValue)
            //        Text = string.Format(addValue > 0 ? "暴击率: + {0} (+{1})" : "暴击率: + {0}", minValue + addValue, addValue)
            //    };

            //    if (fishingItem)
            //    {
            //        CRITICALRATELabel.Text = string.Format(addValue > 0 ? "敏捷: + {0} (+{1})" : "敏捷: + {0}", minValue + addValue, addValue);
            //    }

            //    ItemLabel.Size = new Size(Math.Max(ItemLabel.Size.Width, CRITICALRATELabel.DisplayRectangle.Right + 4),
            //        Math.Max(ItemLabel.Size.Height, CRITICALRATELabel.DisplayRectangle.Bottom));
            //}

            //#endregion

            //#region CRITICALDAMAGE

            //minValue = realItem.Stats[Stat.CriticalDamage];
            //maxValue = 0;
            //addValue = (!hideAdded && (!HoverItem.Info.NeedIdentify || HoverItem.Identified)) ? HoverItem.AddedStats[Stat.CriticalDamage] : 0;

            //if ((minValue > 0 || maxValue > 0 || addValue > 0) && (realItem.Type != ItemType.Gem))
            //{
            //    count++;
            //    MirLabel CRITICALDAMAGELabel = new MirLabel
            //    {
            //        AutoSize = true,
            //        ForeColour = addValue > 0 ? Color.Cyan : Color.White,
            //        Location = new Point(4, ItemLabel.DisplayRectangle.Bottom),
            //        OutLine = true,
            //        Parent = ItemLabel,
            //        //Text = string.Format("Critical Damage + {0}", minValue + addValue)
            //        Text = string.Format(addValue > 0 ? "暴击伤害: + {0} (+{1})" : "暴击伤害: + {0}", minValue + addValue, addValue)
            //    };

            //    ItemLabel.Size = new Size(Math.Max(ItemLabel.Size.Width, CRITICALDAMAGELabel.DisplayRectangle.Right + 4),
            //        Math.Max(ItemLabel.Size.Height, CRITICALDAMAGELabel.DisplayRectangle.Bottom));
            //}

            //#endregion

            //#region Reflect

            //minValue = realItem.Stats[Stat.Reflect];
            //maxValue = 0;
            //addValue = 0;

            //if ((minValue > 0 || maxValue > 0 || addValue > 0) && (realItem.Type != ItemType.Gem))
            //{
            //    count++;
            //    MirLabel ReflectLabel = new MirLabel
            //    {
            //        AutoSize = true,
            //        ForeColour = addValue > 0 ? Color.Cyan : Color.White,
            //        Location = new Point(4, ItemLabel.DisplayRectangle.Bottom),
            //        OutLine = true,
            //        Parent = ItemLabel,
            //        Text = string.Format("攻击反弹: {0}", minValue)
            //    };

            //    ItemLabel.Size = new Size(Math.Max(ItemLabel.Size.Width, ReflectLabel.DisplayRectangle.Right + 4),
            //        Math.Max(ItemLabel.Size.Height, ReflectLabel.DisplayRectangle.Bottom));
            //}

            //#endregion

            //#region Hpdrain

            //minValue = realItem.Stats[Stat.HPDrainRatePercent];
            //maxValue = 0;
            //addValue = 0;

            //if ((minValue > 0 || maxValue > 0 || addValue > 0) && (realItem.Type != ItemType.Gem))
            //{
            //    count++;
            //    MirLabel HPdrainLabel = new MirLabel
            //    {
            //        AutoSize = true,
            //        ForeColour = addValue > 0 ? Color.Cyan : Color.White,
            //        Location = new Point(4, ItemLabel.DisplayRectangle.Bottom),
            //        OutLine = true,
            //        Parent = ItemLabel,
            //        Text = string.Format("HP消耗减少: {0}%", minValue)
            //    };

            //    ItemLabel.Size = new Size(Math.Max(ItemLabel.Size.Width, HPdrainLabel.DisplayRectangle.Right + 4),
            //        Math.Max(ItemLabel.Size.Height, HPdrainLabel.DisplayRectangle.Bottom));
            //}

            //#endregion

            //#region Exp Rate

            //minValue = realItem.Stats[Stat.ExpRatePercent];
            //maxValue = 0;
            //addValue = (!hideAdded && (!HoverItem.Info.NeedIdentify || HoverItem.Identified)) ? HoverItem.AddedStats[Stat.ExpRatePercent] : 0;

            //if (minValue != 0 || maxValue != 0 || addValue != 0)
            //{
            //    string plus = (addValue + minValue < 0) ? "" : "+";

            //    count++;
            //    string negative = "+";
            //    if (addValue < 0) negative = "";
            //    text = string.Format(addValue != 0 ? "经验倍数: " + plus + "{0}% ({2}{1}%)" : "经验倍数: " + plus + "{0}%", minValue + addValue, addValue, negative);

            //    MirLabel expRateLabel = new MirLabel
            //    {
            //        AutoSize = true,
            //        ForeColour = addValue > 0 ? Color.Cyan : Color.White,
            //        Location = new Point(4, ItemLabel.DisplayRectangle.Bottom),
            //        OutLine = true,
            //        Parent = ItemLabel,
            //        Text = text
            //    };

            //    ItemLabel.Size = new Size(Math.Max(ItemLabel.Size.Width, expRateLabel.DisplayRectangle.Right + 4),
            //        Math.Max(ItemLabel.Size.Height, expRateLabel.DisplayRectangle.Bottom));
            //}

            //#endregion

            //#region Drop Rate

            //minValue = realItem.Stats[Stat.ItemDropRatePercent];
            //maxValue = 0;
            //addValue = (!hideAdded && (!HoverItem.Info.NeedIdentify || HoverItem.Identified)) ? HoverItem.AddedStats[Stat.ItemDropRatePercent] : 0;

            //if (minValue != 0 || maxValue != 0 || addValue != 0)
            //{
            //    string plus = (addValue + minValue < 0) ? "" : "+";

            //    count++;
            //    string negative = "+";
            //    if (addValue < 0) negative = "";
            //    text = string.Format(addValue != 0 ? "爆率: " + plus + "{0}% ({2}{1}%)" : "爆率: " + plus + "{0}%", minValue + addValue, addValue, negative);

            //    MirLabel dropRateLabel = new MirLabel
            //    {
            //        AutoSize = true,
            //        ForeColour = addValue > 0 ? Color.Cyan : Color.White,
            //        Location = new Point(4, ItemLabel.DisplayRectangle.Bottom),
            //        OutLine = true,
            //        Parent = ItemLabel,
            //        Text = text
            //    };

            //    ItemLabel.Size = new Size(Math.Max(ItemLabel.Size.Width, dropRateLabel.DisplayRectangle.Right + 4),
            //        Math.Max(ItemLabel.Size.Height, dropRateLabel.DisplayRectangle.Bottom));
            //}

            //#endregion

            //#region Gold Rate

            //minValue = realItem.Stats[Stat.GoldDropRatePercent];
            //maxValue = 0;
            //addValue = (!hideAdded && (!HoverItem.Info.NeedIdentify || HoverItem.Identified)) ? HoverItem.AddedStats[Stat.GoldDropRatePercent] : 0;

            //if (minValue != 0 || maxValue != 0 || addValue != 0)
            //{
            //    string plus = (addValue + minValue < 0) ? "" : "+";

            //    count++;
            //    string negative = "+";
            //    if (addValue < 0) negative = "";
            //    text = string.Format(addValue != 0 ? "金币加成: " + plus + "{0}% ({2}{1}%)" : "金币加成: " + plus + "{0}%", minValue + addValue, addValue, negative);

            //    MirLabel goldRateLabel = new MirLabel
            //    {
            //        AutoSize = true,
            //        ForeColour = addValue > 0 ? Color.Cyan : Color.White,
            //        Location = new Point(4, ItemLabel.DisplayRectangle.Bottom),
            //        OutLine = true,
            //        Parent = ItemLabel,
            //        Text = text
            //    };

            //    ItemLabel.Size = new Size(Math.Max(ItemLabel.Size.Width, goldRateLabel.DisplayRectangle.Right + 4),
            //        Math.Max(ItemLabel.Size.Height, goldRateLabel.DisplayRectangle.Bottom));
            //}

            //#endregion

            //if (count > 0)
            //{
            //    ItemLabel.Size = new Size(ItemLabel.Size.Width, ItemLabel.Size.Height + 4);

            //    #region OUTLINE
            //    MirControl outLine = new MirControl
            //    {
            //        BackColour = Color.FromArgb(255, 50, 50, 50),
            //        Border = true,
            //        BorderColour = Color.Gray,
            //        NotControl = true,
            //        Parent = ItemLabel,
            //        Opacity = 0.4F,
            //        Location = new Point(0, 0)
            //    };
            //    outLine.Size = ItemLabel.Size;
            //    #endregion

            //    return outLine;
            //}
            //else
            //{
            //    ItemLabel.Size = new Size(ItemLabel.Size.Width, ItemLabel.Size.Height - 4);
            //}
            return null;
        }
        public MirControl DefenceInfoLabel(UserItem item, bool Inspect = false, bool hideAdded = false)
        {
            //ushort level = Inspect ? InspectDialog.Level : MapObject.User.Level;
            //MirClass job = Inspect ? InspectDialog.Class : MapObject.User.Class;
            //HoverItem = item;
            //ItemInfo realItem = Functions.GetRealItem(item.Info, level, job, ItemInfoList);

            //ItemLabel.Size = new Size(ItemLabel.Size.Width, ItemLabel.Size.Height + 4);

            //bool fishingItem = false;

            //switch (HoverItem.Info.Type)
            //{
            //    case ItemType.Hook:
            //    case ItemType.Float:
            //    case ItemType.Bait:
            //    case ItemType.Finder:
            //    case ItemType.Reel:
            //        fishingItem = true;
            //        break;
            //    case ItemType.Weapon:
            //        if (HoverItem.Info.Shape == 49 || HoverItem.Info.Shape == 50)
            //            fishingItem = true;
            //        break;
            //    default:
            //        fishingItem = false;
            //        break;
            //}

            //int count = 0;
            //int minValue = 0;
            //int maxValue = 0;
            //int addValue = 0;

            //string text = "";
            //#region AC

            //minValue = realItem.Stats[Stat.MinAC];
            //maxValue = realItem.Stats[Stat.MaxAC];
            //addValue = (!hideAdded && (!HoverItem.Info.NeedIdentify || HoverItem.Identified)) ? HoverItem.AddedStats[Stat.MaxAC] : 0;

            //if (minValue > 0 || maxValue > 0 || addValue > 0)
            //{
            //    count++;
            //    if (HoverItem.Info.Type != ItemType.Gem)
            //        text = string.Format(addValue > 0 ? GameLanguage.AC : GameLanguage.AC2, minValue, maxValue + addValue, addValue);
            //    else
            //        text = string.Format("物理防御 +{0}", minValue + maxValue + addValue);
            //    MirLabel ACLabel = new MirLabel
            //    {
            //        AutoSize = true,
            //        ForeColour = addValue > 0 ? Color.Cyan : Color.White,
            //        Location = new Point(4, ItemLabel.DisplayRectangle.Bottom),
            //        OutLine = true,
            //        Parent = ItemLabel,
            //        //Text = string.Format("AC + {0}~{1}", minValue, maxValue + addValue)
            //        Text = text
            //    };

            //    if (fishingItem)
            //    {
            //        if (HoverItem.Info.Type == ItemType.Float)
            //        {
            //            ACLabel.Text = string.Format("钓鱼机率 + " + (addValue > 0 ? "{0}~{1}% (+{2})" : "{0}~{1}%"), minValue, maxValue + addValue);
            //        }
            //        else if (HoverItem.Info.Type == ItemType.Finder)
            //        {
            //            ACLabel.Text = string.Format("钓鱼机率 + " + (addValue > 0 ? "{0}~{1}% (+{2})" : "{0}~{1}%"), minValue, maxValue + addValue);
            //        }
            //        else
            //        {
            //            ACLabel.Text = string.Format("钓鱼机率 + " + (addValue > 0 ? "{0}% (+{1})" : "{0}%"), maxValue, maxValue + addValue);
            //        }
            //    }

            //    ItemLabel.Size = new Size(Math.Max(ItemLabel.Size.Width, ACLabel.DisplayRectangle.Right + 4),
            //        Math.Max(ItemLabel.Size.Height, ACLabel.DisplayRectangle.Bottom));
            //}

            //#endregion

            //#region MAC

            //minValue = realItem.Stats[Stat.MinMAC];
            //maxValue = realItem.Stats[Stat.MaxMAC];
            //addValue = (!hideAdded && (!HoverItem.Info.NeedIdentify || HoverItem.Identified)) ? HoverItem.AddedStats[Stat.MaxMAC] : 0;

            //if (minValue > 0 || maxValue > 0 || addValue > 0)
            //{
            //    count++;
            //    if (HoverItem.Info.Type != ItemType.Gem)
            //        text = string.Format(addValue > 0 ? GameLanguage.MAC : GameLanguage.MAC2, minValue, maxValue + addValue, addValue);
            //    else
            //        text = string.Format("魔法防御 +{0}", minValue + maxValue + addValue);
            //    MirLabel MACLabel = new MirLabel
            //    {
            //        AutoSize = true,
            //        ForeColour = addValue > 0 ? Color.Cyan : Color.White,
            //        Location = new Point(4, ItemLabel.DisplayRectangle.Bottom),
            //        OutLine = true,
            //        Parent = ItemLabel,
            //        //Text = string.Format("MAC + {0}~{1}", minValue, maxValue + addValue)
            //        Text = text
            //    };

            //    if (fishingItem)
            //    {
            //        MACLabel.Text = string.Format("AutoReel Chance + {0}%", maxValue + addValue);
            //    }

            //    ItemLabel.Size = new Size(Math.Max(ItemLabel.Size.Width, MACLabel.DisplayRectangle.Right + 4),
            //        Math.Max(ItemLabel.Size.Height, MACLabel.DisplayRectangle.Bottom));
            //}

            //#endregion

            //#region MAXHP

            //minValue = realItem.Stats[Stat.HP];
            //maxValue = 0;
            //addValue = (!hideAdded && (!HoverItem.Info.NeedIdentify || HoverItem.Identified)) ? HoverItem.AddedStats[Stat.HP] : 0;

            //if (minValue > 0 || maxValue > 0 || addValue > 0)
            //{
            //    count++;
            //    MirLabel MAXHPLabel = new MirLabel
            //    {
            //        AutoSize = true,
            //        ForeColour = addValue > 0 ? Color.Cyan : Color.White,
            //        Location = new Point(4, ItemLabel.DisplayRectangle.Bottom),
            //        OutLine = true,
            //        Parent = ItemLabel,
            //        //Text = string.Format(realItem.Type == ItemType.Potion ? "HP + {0} Recovery" : "MAXHP + {0}", minValue + addValue)
            //        Text = string.Format(addValue > 0 ? "HP + {0} (+{1})" : "HP + {0}", minValue + addValue, addValue)
            //    };

            //    ItemLabel.Size = new Size(Math.Max(ItemLabel.Size.Width, MAXHPLabel.DisplayRectangle.Right + 4),
            //        Math.Max(ItemLabel.Size.Height, MAXHPLabel.DisplayRectangle.Bottom));
            //}

            //#endregion

            //#region MAXMP

            //minValue = realItem.Stats[Stat.MP];
            //maxValue = 0;
            //addValue = (!hideAdded && (!HoverItem.Info.NeedIdentify || HoverItem.Identified)) ? HoverItem.AddedStats[Stat.MP] : 0;

            //if (minValue > 0 || maxValue > 0 || addValue > 0)
            //{
            //    count++;
            //    MirLabel MAXMPLabel = new MirLabel
            //    {
            //        AutoSize = true,
            //        ForeColour = addValue > 0 ? Color.Cyan : Color.White,
            //        Location = new Point(4, ItemLabel.DisplayRectangle.Bottom),
            //        OutLine = true,
            //        Parent = ItemLabel,
            //        //Text = string.Format(realItem.Type == ItemType.Potion ? "MP + {0} Recovery" : "MAXMP + {0}", minValue + addValue)
            //        Text = string.Format(addValue > 0 ? "MP + {0} (+{1})" : "MP + {0}", minValue + addValue, addValue)
            //    };

            //    ItemLabel.Size = new Size(Math.Max(ItemLabel.Size.Width, MAXMPLabel.DisplayRectangle.Right + 4),
            //        Math.Max(ItemLabel.Size.Height, MAXMPLabel.DisplayRectangle.Bottom));
            //}

            //#endregion

            //#region MAXHPRATE

            //minValue = realItem.Stats[Stat.HPRatePercent];
            //maxValue = 0;
            //addValue = 0;

            //if (minValue > 0 || maxValue > 0 || addValue > 0)
            //{
            //    count++;
            //    MirLabel MAXHPRATELabel = new MirLabel
            //    {
            //        AutoSize = true,
            //        ForeColour = addValue > 0 ? Color.Cyan : Color.White,
            //        Location = new Point(4, ItemLabel.DisplayRectangle.Bottom),
            //        OutLine = true,
            //        Parent = ItemLabel,
            //        Text = string.Format("HP + {0}%", minValue + addValue)
            //    };

            //    ItemLabel.Size = new Size(Math.Max(ItemLabel.Size.Width, MAXHPRATELabel.DisplayRectangle.Right + 4),
            //        Math.Max(ItemLabel.Size.Height, MAXHPRATELabel.DisplayRectangle.Bottom));
            //}

            //#endregion

            //#region MAXMPRATE

            //minValue = realItem.Stats[Stat.MPRatePercent];
            //maxValue = 0;
            //addValue = 0;

            //if (minValue > 0 || maxValue > 0 || addValue > 0)
            //{
            //    count++;
            //    MirLabel MAXMPRATELabel = new MirLabel
            //    {
            //        AutoSize = true,
            //        ForeColour = addValue > 0 ? Color.Cyan : Color.White,
            //        Location = new Point(4, ItemLabel.DisplayRectangle.Bottom),
            //        OutLine = true,
            //        Parent = ItemLabel,
            //        Text = string.Format("MP + {0}%", minValue + addValue)
            //    };

            //    ItemLabel.Size = new Size(Math.Max(ItemLabel.Size.Width, MAXMPRATELabel.DisplayRectangle.Right + 4),
            //        Math.Max(ItemLabel.Size.Height, MAXMPRATELabel.DisplayRectangle.Bottom));
            //}

            //#endregion

            //#region MAXACRATE

            //minValue = realItem.Stats[Stat.MaxACRatePercent];
            //maxValue = 0;
            //addValue = 0;

            //if (minValue > 0 || maxValue > 0 || addValue > 0)
            //{
            //    count++;
            //    MirLabel MAXACRATE = new MirLabel
            //    {
            //        AutoSize = true,
            //        ForeColour = addValue > 0 ? Color.Cyan : Color.White,
            //        Location = new Point(4, ItemLabel.DisplayRectangle.Bottom),
            //        OutLine = true,
            //        Parent = ItemLabel,
            //        Text = string.Format("最大防御 + {0}%", minValue + addValue)
            //    };

            //    ItemLabel.Size = new Size(Math.Max(ItemLabel.Size.Width, MAXACRATE.DisplayRectangle.Right + 4),
            //        Math.Max(ItemLabel.Size.Height, MAXACRATE.DisplayRectangle.Bottom));
            //}

            //#endregion

            //#region MAXMACRATE

            //minValue = realItem.Stats[Stat.MaxMACRatePercent];
            //maxValue = 0;
            //addValue = 0;

            //if (minValue > 0 || maxValue > 0 || addValue > 0)
            //{
            //    count++;
            //    MirLabel MAXMACRATELabel = new MirLabel
            //    {
            //        AutoSize = true,
            //        ForeColour = addValue > 0 ? Color.Cyan : Color.White,
            //        Location = new Point(4, ItemLabel.DisplayRectangle.Bottom),
            //        OutLine = true,
            //        Parent = ItemLabel,
            //        Text = string.Format("最大魔防 + {0}%", minValue + addValue)
            //    };

            //    ItemLabel.Size = new Size(Math.Max(ItemLabel.Size.Width, MAXMACRATELabel.DisplayRectangle.Right + 4),
            //        Math.Max(ItemLabel.Size.Height, MAXMACRATELabel.DisplayRectangle.Bottom));
            //}

            //#endregion

            //#region HEALTH_RECOVERY

            //minValue = realItem.Stats[Stat.HealthRecovery];
            //maxValue = 0;
            //addValue = (!hideAdded && (!HoverItem.Info.NeedIdentify || HoverItem.Identified)) ? HoverItem.AddedStats[Stat.HealthRecovery] : 0;

            //if (minValue > 0 || maxValue > 0 || addValue > 0)
            //{
            //    count++;
            //    MirLabel HEALTH_RECOVERYLabel = new MirLabel
            //    {
            //        AutoSize = true,
            //        ForeColour = addValue > 0 ? Color.Cyan : Color.White,
            //        Location = new Point(4, ItemLabel.DisplayRectangle.Bottom),
            //        OutLine = true,
            //        Parent = ItemLabel,
            //        Text = string.Format(addValue > 0 ? "HP恢复 + {0} (+{1})" : "HP恢复 + {0}", minValue + addValue, addValue)
            //    };

            //    ItemLabel.Size = new Size(Math.Max(ItemLabel.Size.Width, HEALTH_RECOVERYLabel.DisplayRectangle.Right + 4),
            //        Math.Max(ItemLabel.Size.Height, HEALTH_RECOVERYLabel.DisplayRectangle.Bottom));
            //}

            //#endregion

            //#region MANA_RECOVERY

            //minValue = realItem.Stats[Stat.SpellRecovery];
            //maxValue = 0;
            //addValue = (!hideAdded && (!HoverItem.Info.NeedIdentify || HoverItem.Identified)) ? HoverItem.AddedStats[Stat.SpellRecovery] : 0;

            //if (minValue > 0 || maxValue > 0 || addValue > 0)
            //{
            //    count++;
            //    MirLabel MANA_RECOVERYLabel = new MirLabel
            //    {
            //        AutoSize = true,
            //        ForeColour = addValue > 0 ? Color.Cyan : Color.White,
            //        Location = new Point(4, ItemLabel.DisplayRectangle.Bottom),
            //        OutLine = true,
            //        Parent = ItemLabel,
            //        //Text = string.Format("ManaRecovery + {0}", minValue + addValue)
            //        Text = string.Format(addValue > 0 ? "MP恢复 + {0} (+{1})" : "MP恢复 + {0}", minValue + addValue, addValue)
            //    };

            //    ItemLabel.Size = new Size(Math.Max(ItemLabel.Size.Width, MANA_RECOVERYLabel.DisplayRectangle.Right + 4),
            //        Math.Max(ItemLabel.Size.Height, MANA_RECOVERYLabel.DisplayRectangle.Bottom));
            //}

            //#endregion

            //#region POISON_RECOVERY

            //minValue = realItem.Stats[Stat.PoisonRecovery];
            //maxValue = 0;
            //addValue = (!hideAdded && (!HoverItem.Info.NeedIdentify || HoverItem.Identified)) ? HoverItem.AddedStats[Stat.PoisonRecovery] : 0;

            //if (minValue > 0 || maxValue > 0 || addValue > 0)
            //{
            //    count++;
            //    MirLabel POISON_RECOVERYabel = new MirLabel
            //    {
            //        AutoSize = true,
            //        ForeColour = addValue > 0 ? Color.Cyan : Color.White,
            //        Location = new Point(4, ItemLabel.DisplayRectangle.Bottom),
            //        OutLine = true,
            //        Parent = ItemLabel,
            //        //Text = string.Format("Poison Recovery + {0}", minValue + addValue)
            //        Text = string.Format(addValue > 0 ? "中毒恢复 + {0} (+{1})" : "中毒恢复 + {0}", minValue + addValue, addValue)
            //    };

            //    ItemLabel.Size = new Size(Math.Max(ItemLabel.Size.Width, POISON_RECOVERYabel.DisplayRectangle.Right + 4),
            //        Math.Max(ItemLabel.Size.Height, POISON_RECOVERYabel.DisplayRectangle.Bottom));
            //}

            //#endregion

            //#region AGILITY

            //minValue = realItem.Stats[Stat.Agility];
            //maxValue = 0;
            //addValue = (!hideAdded && (!HoverItem.Info.NeedIdentify || HoverItem.Identified)) ? HoverItem.AddedStats[Stat.Agility] : 0;

            //if (minValue > 0 || maxValue > 0 || addValue > 0)
            //{
            //    count++;
            //    if (HoverItem.Info.Type != ItemType.Gem)
            //        text = string.Format(addValue > 0 ? GameLanguage.Agility : GameLanguage.Agility2, minValue + addValue, addValue);
            //    else
            //        text = string.Format("增加 +{0} 敏捷", minValue + maxValue + addValue);

            //    MirLabel AGILITYLabel = new MirLabel
            //    {
            //        AutoSize = true,
            //        ForeColour = addValue > 0 ? Color.Cyan : Color.White,
            //        Location = new Point(4, ItemLabel.DisplayRectangle.Bottom),
            //        OutLine = true,
            //        Parent = ItemLabel,
            //        Text = text
            //    };

            //    ItemLabel.Size = new Size(Math.Max(ItemLabel.Size.Width, AGILITYLabel.DisplayRectangle.Right + 4),
            //        Math.Max(ItemLabel.Size.Height, AGILITYLabel.DisplayRectangle.Bottom));
            //}

            //#endregion

            //#region STRONG

            //minValue = realItem.Stats[Stat.Strong];
            //maxValue = 0;
            //addValue = (!hideAdded && (!HoverItem.Info.NeedIdentify || HoverItem.Identified)) ? HoverItem.AddedStats[Stat.Strong] : 0;

            //if (minValue > 0 || maxValue > 0 || addValue > 0)
            //{
            //    count++;
            //    MirLabel STRONGLabel = new MirLabel
            //    {
            //        AutoSize = true,
            //        ForeColour = addValue > 0 ? Color.Cyan : Color.White,
            //        Location = new Point(4, ItemLabel.DisplayRectangle.Bottom),
            //        OutLine = true,
            //        Parent = ItemLabel,
            //        //Text = string.Format("Strong + {0}", minValue + addValue)
            //        Text = string.Format(addValue > 0 ? "负重 + {0} (+{1})" : "负重 + {0}", minValue + addValue, addValue)
            //    };

            //    ItemLabel.Size = new Size(Math.Max(ItemLabel.Size.Width, STRONGLabel.DisplayRectangle.Right + 4),
            //        Math.Max(ItemLabel.Size.Height, STRONGLabel.DisplayRectangle.Bottom));
            //}

            //#endregion

            //#region POISON_RESIST

            //minValue = realItem.Stats[Stat.PoisonResist];
            //maxValue = 0;
            //addValue = (!hideAdded && (!HoverItem.Info.NeedIdentify || HoverItem.Identified)) ? HoverItem.AddedStats[Stat.PoisonResist] : 0;

            //if (minValue > 0 || maxValue > 0 || addValue > 0)
            //{
            //    count++;
            //    if (HoverItem.Info.Type != ItemType.Gem)
            //        text = string.Format(addValue > 0 ? "中毒躲避 + {0} (+{1})" : "中毒躲避 + {0}", minValue + addValue, addValue);
            //    else
            //        text = string.Format("增加 +{0} 中毒躲避", minValue + maxValue + addValue);
            //    MirLabel POISON_RESISTLabel = new MirLabel
            //    {
            //        AutoSize = true,
            //        ForeColour = addValue > 0 ? Color.Cyan : Color.White,
            //        Location = new Point(4, ItemLabel.DisplayRectangle.Bottom),
            //        OutLine = true,
            //        Parent = ItemLabel,
            //        Text = text
            //    };

            //    ItemLabel.Size = new Size(Math.Max(ItemLabel.Size.Width, POISON_RESISTLabel.DisplayRectangle.Right + 4),
            //        Math.Max(ItemLabel.Size.Height, POISON_RESISTLabel.DisplayRectangle.Bottom));
            //}

            //#endregion

            //#region MAGIC_RESIST

            //minValue = realItem.Stats[Stat.MagicResist];
            //maxValue = 0;
            //addValue = (!hideAdded && (!HoverItem.Info.NeedIdentify || HoverItem.Identified)) ? HoverItem.AddedStats[Stat.MagicResist] : 0;

            //if (minValue > 0 || maxValue > 0 || addValue > 0)
            //{
            //    count++;
            //    if (HoverItem.Info.Type != ItemType.Gem)
            //        text = string.Format(addValue > 0 ? "魔法抵抗 + {0} (+{1})" : "魔法抵抗 + {0}", minValue + addValue, addValue);
            //    else
            //        text = string.Format("增加 +{0} 魔法抵抗", minValue + maxValue + addValue);
            //    MirLabel MAGIC_RESISTLabel = new MirLabel
            //    {
            //        AutoSize = true,
            //        ForeColour = addValue > 0 ? Color.Cyan : Color.White,
            //        Location = new Point(4, ItemLabel.DisplayRectangle.Bottom),
            //        OutLine = true,
            //        Parent = ItemLabel,
            //        //Text = string.Format("Magic Resist + {0}", minValue + addValue)
            //        Text = text
            //    };

            //    ItemLabel.Size = new Size(Math.Max(ItemLabel.Size.Width, MAGIC_RESISTLabel.DisplayRectangle.Right + 4),
            //        Math.Max(ItemLabel.Size.Height, MAGIC_RESISTLabel.DisplayRectangle.Bottom));
            //}

            //#endregion

            //if (count > 0)
            //{
            //    ItemLabel.Size = new Size(ItemLabel.Size.Width, ItemLabel.Size.Height + 4);

            //    #region OUTLINE
            //    MirControl outLine = new MirControl
            //    {
            //        BackColour = Color.FromArgb(255, 50, 50, 50),
            //        Border = true,
            //        BorderColour = Color.Gray,
            //        NotControl = true,
            //        Parent = ItemLabel,
            //        Opacity = 0.4F,
            //        Location = new Point(0, 0)
            //    };
            //    outLine.Size = ItemLabel.Size;
            //    #endregion

            //    return outLine;
            //}
            //else
            //{
            //    ItemLabel.Size = new Size(ItemLabel.Size.Width, ItemLabel.Size.Height - 4);
            //}
            return null;
        }
        public MirControl WeightInfoLabel(UserItem item, bool Inspect = false)
        {
            //ushort level = Inspect ? InspectDialog.Level : MapObject.User.Level;
            //MirClass job = Inspect ? InspectDialog.Class : MapObject.User.Class;
            //HoverItem = item;
            //ItemInfo realItem = Functions.GetRealItem(item.Info, level, job, ItemInfoList);

            //ItemLabel.Size = new Size(ItemLabel.Size.Width, ItemLabel.Size.Height + 4);

            //int count = 0;
            //int minValue = 0;
            //int maxValue = 0;
            //int addValue = 0;

            //#region HANDWEIGHT

            //minValue = realItem.Stats[Stat.HandWeight];
            //maxValue = 0;
            //addValue = 0;

            //if (minValue > 0 || maxValue > 0 || addValue > 0)
            //{
            //    count++;
            //    MirLabel HANDWEIGHTLabel = new MirLabel
            //    {
            //        AutoSize = true,
            //        ForeColour = addValue > 0 ? Color.Cyan : Color.White,
            //        Location = new Point(4, ItemLabel.DisplayRectangle.Bottom),
            //        OutLine = true,
            //        Parent = ItemLabel,
            //        //Text = string.Format("Hand Weight + {0}", minValue + addValue)
            //        Text = string.Format(addValue > 0 ? "腕力 + {0} (+{1})" : "腕力 + {0}", minValue + addValue, addValue)
            //    };

            //    ItemLabel.Size = new Size(Math.Max(ItemLabel.Size.Width, HANDWEIGHTLabel.DisplayRectangle.Right + 4),
            //        Math.Max(ItemLabel.Size.Height, HANDWEIGHTLabel.DisplayRectangle.Bottom));
            //}

            //#endregion

            //#region WEARWEIGHT

            //minValue = realItem.Stats[Stat.WearWeight];
            //maxValue = 0;
            //addValue = 0;

            //if (minValue > 0 || maxValue > 0 || addValue > 0)
            //{
            //    count++;
            //    MirLabel WEARWEIGHTLabel = new MirLabel
            //    {
            //        AutoSize = true,
            //        ForeColour = addValue > 0 ? Color.Cyan : Color.White,
            //        Location = new Point(4, ItemLabel.DisplayRectangle.Bottom),
            //        OutLine = true,
            //        Parent = ItemLabel,
            //        //Text = string.Format("Wear Weight + {0}", minValue + addValue)
            //        Text = string.Format(addValue > 0 ? "穿戴负重 + {0} (+{1})" : "穿戴负重 + {0}", minValue + addValue, addValue)
            //    };

            //    ItemLabel.Size = new Size(Math.Max(ItemLabel.Size.Width, WEARWEIGHTLabel.DisplayRectangle.Right + 4),
            //        Math.Max(ItemLabel.Size.Height, WEARWEIGHTLabel.DisplayRectangle.Bottom));
            //}

            //#endregion

            //#region BAGWEIGHT

            //minValue = realItem.Stats[Stat.BagWeight];
            //maxValue = 0;
            //addValue = 0;

            //if (minValue > 0 || maxValue > 0 || addValue > 0)
            //{
            //    count++;
            //    MirLabel BAGWEIGHTLabel = new MirLabel
            //    {
            //        AutoSize = true,
            //        ForeColour = addValue > 0 ? Color.Cyan : Color.White,
            //        Location = new Point(4, ItemLabel.DisplayRectangle.Bottom),
            //        OutLine = true,
            //        Parent = ItemLabel,
            //        //Text = string.Format("Bag Weight + {0}", minValue + addValue)
            //        Text = string.Format(addValue > 0 ? "包裹负重 + {0} (+{1})" : "包裹负重 + {0}", minValue + addValue, addValue)
            //    };

            //    ItemLabel.Size = new Size(Math.Max(ItemLabel.Size.Width, BAGWEIGHTLabel.DisplayRectangle.Right + 4),
            //        Math.Max(ItemLabel.Size.Height, BAGWEIGHTLabel.DisplayRectangle.Bottom));
            //}

            //#endregion

            //#region FASTRUN
            //minValue = realItem.CanFastRun == true ? 1 : 0;
            //maxValue = 0;
            //addValue = 0;

            //if (minValue > 0 || maxValue > 0 || addValue > 0)
            //{
            //    count++;
            //    MirLabel BAGWEIGHTLabel = new MirLabel
            //    {
            //        AutoSize = true,
            //        ForeColour = addValue > 0 ? Color.Cyan : Color.White,
            //        Location = new Point(4, ItemLabel.DisplayRectangle.Bottom),
            //        OutLine = true,
            //        Parent = ItemLabel,
            //        Text = string.Format("免助跑")
            //    };

            //    ItemLabel.Size = new Size(Math.Max(ItemLabel.Size.Width, BAGWEIGHTLabel.DisplayRectangle.Right + 4),
            //        Math.Max(ItemLabel.Size.Height, BAGWEIGHTLabel.DisplayRectangle.Bottom));
            //}

            //#endregion

            //#region TIME & RANGE
            //minValue = 0;
            //maxValue = 0;
            //addValue = 0;

            //if (HoverItem.Info.Type == ItemType.Potion && HoverItem.Info.Durability > 0)
            //{
            //    count++;
            //    MirLabel TNRLabel = new MirLabel
            //    {
            //        AutoSize = true,
            //        ForeColour = addValue > 0 ? Color.Cyan : Color.White,
            //        Location = new Point(4, ItemLabel.DisplayRectangle.Bottom),
            //        OutLine = true,
            //        Parent = ItemLabel,
            //        Text = string.Format("有效时间 : {0}", Functions.PrintTimeSpanFromSeconds(HoverItem.Info.Durability * 60))
            //    };

            //    ItemLabel.Size = new Size(Math.Max(ItemLabel.Size.Width, TNRLabel.DisplayRectangle.Right + 4),
            //        Math.Max(ItemLabel.Size.Height, TNRLabel.DisplayRectangle.Bottom));
            //}

            //if (HoverItem.Info.Type == ItemType.Transform && HoverItem.Info.Durability > 0)
            //{
            //    count++;
            //    MirLabel TNRLabel = new MirLabel
            //    {
            //        AutoSize = true,
            //        ForeColour = addValue > 0 ? Color.Cyan : Color.White,
            //        Location = new Point(4, ItemLabel.DisplayRectangle.Bottom),
            //        OutLine = true,
            //        Parent = ItemLabel,
            //        Text = string.Format("有效时间 : {0}", Functions.PrintTimeSpanFromSeconds(HoverItem.Info.Durability, false))
            //    };

            //    ItemLabel.Size = new Size(Math.Max(ItemLabel.Size.Width, TNRLabel.DisplayRectangle.Right + 4),
            //        Math.Max(ItemLabel.Size.Height, TNRLabel.DisplayRectangle.Bottom));
            //}

            //#endregion

            //if (count > 0)
            //{
            //    ItemLabel.Size = new Size(ItemLabel.Size.Width, ItemLabel.Size.Height + 4);

            //    #region OUTLINE
            //    MirControl outLine = new MirControl
            //    {
            //        BackColour = Color.FromArgb(255, 50, 50, 50),
            //        Border = true,
            //        BorderColour = Color.Gray,
            //        NotControl = true,
            //        Parent = ItemLabel,
            //        Opacity = 0.4F,
            //        Location = new Point(0, 0)
            //    };
            //    outLine.Size = ItemLabel.Size;
            //    #endregion

            //    return outLine;
            //}
            //else
            //{
            //    ItemLabel.Size = new Size(ItemLabel.Size.Width, ItemLabel.Size.Height - 4);
            //}
            return null;
        }
        public MirControl AwakeInfoLabel(UserItem item, bool Inspect = false)
        {
            //ushort level = Inspect ? InspectDialog.Level : MapObject.User.Level;
            //MirClass job = Inspect ? InspectDialog.Class : MapObject.User.Class;
            //HoverItem = item;
            //ItemInfo realItem = Functions.GetRealItem(item.Info, level, job, ItemInfoList);

            //ItemLabel.Size = new Size(ItemLabel.Size.Width, ItemLabel.Size.Height + 4);

            //int count = 0;

            //#region AWAKENAME
            //if (HoverItem.Awake.GetAwakeLevel() > 0)
            //{
            //    count++;
            //    MirLabel AWAKENAMELabel = new MirLabel
            //    {
            //        AutoSize = true,
            //        ForeColour = GradeNameColor(HoverItem.Info.Grade),
            //        Location = new Point(4, ItemLabel.DisplayRectangle.Bottom),
            //        OutLine = true,
            //        Parent = ItemLabel,
            //        Text = string.Format("{0} 觉醒({1})", HoverItem.Awake.Type.ToString(), HoverItem.Awake.GetAwakeLevel())
            //    };

            //    ItemLabel.Size = new Size(Math.Max(ItemLabel.Size.Width, AWAKENAMELabel.DisplayRectangle.Right + 4),
            //        Math.Max(ItemLabel.Size.Height, AWAKENAMELabel.DisplayRectangle.Bottom));
            //}

            //#endregion

            //#region AWAKE_TOTAL_VALUE
            //if (HoverItem.Awake.GetAwakeValue() > 0)
            //{
            //    count++;
            //    MirLabel AWAKE_TOTAL_VALUELabel = new MirLabel
            //    {
            //        AutoSize = true,
            //        ForeColour = Color.White,
            //        Location = new Point(4, ItemLabel.DisplayRectangle.Bottom),
            //        OutLine = true,
            //        Parent = ItemLabel,
            //        Text = string.Format(realItem.Type != ItemType.Armour ? "{0} + {1}~{2}" : "最大 {0} + {1}", HoverItem.Awake.Type.ToString(), HoverItem.Awake.GetAwakeValue(), HoverItem.Awake.GetAwakeValue())
            //    };

            //    ItemLabel.Size = new Size(Math.Max(ItemLabel.Size.Width, AWAKE_TOTAL_VALUELabel.DisplayRectangle.Right + 4),
            //        Math.Max(ItemLabel.Size.Height, AWAKE_TOTAL_VALUELabel.DisplayRectangle.Bottom));
            //}

            //#endregion

            //#region AWAKE_LEVEL_VALUE
            //if (HoverItem.Awake.GetAwakeLevel() > 0)
            //{
            //    count++;
            //    for (int i = 0; i < HoverItem.Awake.GetAwakeLevel(); i++)
            //    {
            //        MirLabel AWAKE_LEVEL_VALUELabel = new MirLabel
            //        {
            //            AutoSize = true,
            //            ForeColour = Color.White,
            //            Location = new Point(4, ItemLabel.DisplayRectangle.Bottom),
            //            OutLine = true,
            //            Parent = ItemLabel,
            //            Text = string.Format(realItem.Type != ItemType.Armour ? "等级 {0} : {1} + {2}~{3}" : "等级 {0} : 最大 {1} + {2}~{3}", i + 1, HoverItem.Awake.Type.ToString(), HoverItem.Awake.GetAwakeLevelValue(i), HoverItem.Awake.GetAwakeLevelValue(i))
            //        };

            //        ItemLabel.Size = new Size(Math.Max(ItemLabel.Size.Width, AWAKE_LEVEL_VALUELabel.DisplayRectangle.Right + 4),
            //            Math.Max(ItemLabel.Size.Height, AWAKE_LEVEL_VALUELabel.DisplayRectangle.Bottom));
            //    }
            //}

            //#endregion

            //if (count > 0)
            //{
            //    ItemLabel.Size = new Size(ItemLabel.Size.Width, ItemLabel.Size.Height + 4);

            //    #region OUTLINE
            //    MirControl outLine = new MirControl
            //    {
            //        BackColour = Color.FromArgb(255, 50, 50, 50),
            //        Border = true,
            //        BorderColour = Color.Gray,
            //        NotControl = true,
            //        Parent = ItemLabel,
            //        Opacity = 0.4F,
            //        Location = new Point(0, 0)
            //    };
            //    outLine.Size = ItemLabel.Size;
            //    #endregion

            //    return outLine;
            //}
            //else
            //{
            //    ItemLabel.Size = new Size(ItemLabel.Size.Width, ItemLabel.Size.Height - 4);
            //}
            return null;
        }
        public MirControl SocketInfoLabel(UserItem item, bool Inspect = false)
        {
            //ushort level = Inspect ? InspectDialog.Level : MapObject.User.Level;
            //MirClass job = Inspect ? InspectDialog.Class : MapObject.User.Class;
            //HoverItem = item;
            //ItemInfo realItem = Functions.GetRealItem(item.Info, level, job, ItemInfoList);

            //ItemLabel.Size = new Size(ItemLabel.Size.Width, ItemLabel.Size.Height + 4);


            //int count = 0;

            //#region SOCKET

            //for (int i = 0; i < item.Slots.Length; i++)
            //{
            //    count++;
            //    MirLabel SOCKETLabel = new MirLabel
            //    {
            //        AutoSize = true,
            //        ForeColour = (count > realItem.Slots && !realItem.IsFishingRod && realItem.Type != ItemType.Mount) ? Color.Cyan : Color.White,
            //        Location = new Point(4, ItemLabel.DisplayRectangle.Bottom),
            //        OutLine = true,
            //        Parent = ItemLabel,
            //        Text = string.Format("空槽 : {0}", item.Slots[i] == null ? "空" : item.Slots[i].FriendlyName)
            //    };

            //    ItemLabel.Size = new Size(Math.Max(ItemLabel.Size.Width, SOCKETLabel.DisplayRectangle.Right + 4),
            //        Math.Max(ItemLabel.Size.Height, SOCKETLabel.DisplayRectangle.Bottom));
            //}

            //#endregion

            //if (count > 0)
            //{
            //    #region SOCKET

            //    count++;
            //    MirLabel SOCKETLabel = new MirLabel
            //    {
            //        AutoSize = true,
            //        ForeColour = Color.White,
            //        Location = new Point(4, ItemLabel.DisplayRectangle.Bottom),
            //        OutLine = true,
            //        Parent = ItemLabel,
            //        Text = "Ctrl + 鼠标右击打开空槽面板"
            //    };

            //    ItemLabel.Size = new Size(Math.Max(ItemLabel.Size.Width, SOCKETLabel.DisplayRectangle.Right + 4),
            //        Math.Max(ItemLabel.Size.Height, SOCKETLabel.DisplayRectangle.Bottom));

            //    #endregion

            //    ItemLabel.Size = new Size(ItemLabel.Size.Width, ItemLabel.Size.Height + 4);

            //    #region OUTLINE
            //    MirControl outLine = new MirControl
            //    {
            //        BackColour = Color.FromArgb(255, 50, 50, 50),
            //        Border = true,
            //        BorderColour = Color.Gray,
            //        NotControl = true,
            //        Parent = ItemLabel,
            //        Opacity = 0.4F,
            //        Location = new Point(0, 0)
            //    };
            //    outLine.Size = ItemLabel.Size;
            //    #endregion

            //    return outLine;
            //}
            //else
            //{
            //    ItemLabel.Size = new Size(ItemLabel.Size.Width, ItemLabel.Size.Height - 4);
            //}
            return null;
        }
        public MirControl NeedInfoLabel(UserItem item, bool Inspect = false)
        {
            //ushort level = Inspect ? InspectDialog.Level : MapObject.User.Level;
            //MirClass job = Inspect ? InspectDialog.Class : MapObject.User.Class;
            //HoverItem = item;
            //ItemInfo realItem = Functions.GetRealItem(item.Info, level, job, ItemInfoList);

            //ItemLabel.Size = new Size(ItemLabel.Size.Width, ItemLabel.Size.Height + 4);

            //int count = 0;

            //#region LEVEL
            //if (realItem.RequiredAmount > 0)
            //{
            //    count++;
            //    string text;
            //    Color colour = Color.White;
            //    switch (realItem.RequiredType)
            //    {
            //        case RequiredType.Level:
            //            text = string.Format(GameLanguage.RequiredLevel, realItem.RequiredAmount);
            //            if (MapObject.User.Level < realItem.RequiredAmount)
            //                colour = Color.Red;
            //            break;
            //        case RequiredType.MaxAC:
            //            text = string.Format("需要防御: {0}", realItem.RequiredAmount);
            //            if (MapObject.User.Stats[Stat.MaxAC] < realItem.RequiredAmount)
            //                colour = Color.Red;
            //            break;
            //        case RequiredType.MaxMAC:
            //            text = string.Format("需要魔防: {0}", realItem.RequiredAmount);
            //            if (MapObject.User.Stats[Stat.MaxMAC] < realItem.RequiredAmount)
            //                colour = Color.Red;
            //            break;
            //        case RequiredType.MaxDC:
            //            text = string.Format(GameLanguage.RequiredDC, realItem.RequiredAmount);
            //            if (MapObject.User.Stats[Stat.MaxDC] < realItem.RequiredAmount)
            //                colour = Color.Red;
            //            break;
            //        case RequiredType.MaxMC:
            //            text = string.Format(GameLanguage.RequiredMC, realItem.RequiredAmount);
            //            if (MapObject.User.Stats[Stat.MaxMC] < realItem.RequiredAmount)
            //                colour = Color.Red;
            //            break;
            //        case RequiredType.MaxSC:
            //            text = string.Format(GameLanguage.RequiredSC, realItem.RequiredAmount);
            //            if (MapObject.User.Stats[Stat.MaxSC] < realItem.RequiredAmount)
            //                colour = Color.Red;
            //            break;
            //        case RequiredType.MaxLevel:
            //            text = string.Format("最大等级 : {0}", realItem.RequiredAmount);
            //            if (MapObject.User.Level > realItem.RequiredAmount)
            //                colour = Color.Red;
            //            break;
            //        case RequiredType.MinAC:
            //            text = string.Format("需要防御: {0}", realItem.RequiredAmount);
            //            if (MapObject.User.Stats[Stat.MinAC] < realItem.RequiredAmount)
            //                colour = Color.Red;
            //            break;
            //        case RequiredType.MinMAC:
            //            text = string.Format("需要魔防: {0}", realItem.RequiredAmount);
            //            if (MapObject.User.Stats[Stat.MinMAC] < realItem.RequiredAmount)
            //                colour = Color.Red;
            //            break;
            //        case RequiredType.MinDC:
            //            text = string.Format("需要攻击力: {0}", realItem.RequiredAmount);
            //            if (MapObject.User.Stats[Stat.MinDC] < realItem.RequiredAmount)
            //                colour = Color.Red;
            //            break;
            //        case RequiredType.MinMC:
            //            text = string.Format("需要魔法力: {0}", realItem.RequiredAmount);
            //            if (MapObject.User.Stats[Stat.MinMC] < realItem.RequiredAmount)
            //                colour = Color.Red;
            //            break;
            //        case RequiredType.MinSC:
            //            text = string.Format("需要道术力: {0}", realItem.RequiredAmount);
            //            if (MapObject.User.Stats[Stat.MinSC] < realItem.RequiredAmount)
            //                colour = Color.Red;
            //            break;
            //        default:
            //            text = "未知需求类型";
            //            break;
            //    }

            //    MirLabel LEVELLabel = new MirLabel
            //    {
            //        AutoSize = true,
            //        ForeColour = colour,
            //        Location = new Point(4, ItemLabel.DisplayRectangle.Bottom),
            //        OutLine = true,
            //        Parent = ItemLabel,
            //        Text = text
            //    };

            //    ItemLabel.Size = new Size(Math.Max(ItemLabel.Size.Width, LEVELLabel.DisplayRectangle.Right + 4),
            //        Math.Max(ItemLabel.Size.Height, LEVELLabel.DisplayRectangle.Bottom));
            //}

            //#endregion

            //#region CLASS
            //if (realItem.RequiredClass != RequiredClass.None)
            //{
            //    count++;
            //    Color colour = Color.White;

            //    switch (MapObject.User.Class)
            //    {
            //        case MirClass.Warrior:
            //            if (!realItem.RequiredClass.HasFlag(RequiredClass.Warrior))
            //                colour = Color.Red;
            //            break;
            //        case MirClass.Wizard:
            //            if (!realItem.RequiredClass.HasFlag(RequiredClass.Wizard))
            //                colour = Color.Red;
            //            break;
            //        case MirClass.Taoist:
            //            if (!realItem.RequiredClass.HasFlag(RequiredClass.Taoist))
            //                colour = Color.Red;
            //            break;
            //        case MirClass.Assassin:
            //            if (!realItem.RequiredClass.HasFlag(RequiredClass.Assassin))
            //                colour = Color.Red;
            //            break;
            //        case MirClass.Archer:
            //            if (!realItem.RequiredClass.HasFlag(RequiredClass.Archer))
            //                colour = Color.Red;
            //            break;
            //    }

            //    MirLabel CLASSLabel = new MirLabel
            //    {
            //        AutoSize = true,
            //        ForeColour = colour,
            //        Location = new Point(4, ItemLabel.DisplayRectangle.Bottom),
            //        OutLine = true,
            //        Parent = ItemLabel,
            //        Text = string.Format(GameLanguage.ClassRequired, GetRequiredClassString(realItem.RequiredClass))
            //    };

            //    ItemLabel.Size = new Size(Math.Max(ItemLabel.Size.Width, CLASSLabel.DisplayRectangle.Right + 4),
            //        Math.Max(ItemLabel.Size.Height, CLASSLabel.DisplayRectangle.Bottom));
            //}

            //#endregion

            //if (count > 0)
            //{
            //    ItemLabel.Size = new Size(ItemLabel.Size.Width, ItemLabel.Size.Height + 4);

            //    #region OUTLINE
            //    MirControl outLine = new MirControl
            //    {
            //        BackColour = Color.FromArgb(255, 50, 50, 50),
            //        Border = true,
            //        BorderColour = Color.Gray,
            //        NotControl = true,
            //        Parent = ItemLabel,
            //        Opacity = 0.4F,
            //        Location = new Point(0, 0)
            //    };
            //    outLine.Size = ItemLabel.Size;
            //    #endregion

            //    return outLine;
            //}
            //else
            //{
            //    ItemLabel.Size = new Size(ItemLabel.Size.Width, ItemLabel.Size.Height - 4);
            //}
            return null;
        }

        private string GetRequiredClassString(RequiredClass RequiredClass)
        {
            switch (RequiredClass)
            {
                case RequiredClass.Warrior:
                    return "武士";
                    break;
                case RequiredClass.Wizard:
                    return "法师";
                    break;
                case RequiredClass.Taoist:
                    return "道士";
                    break;
                case RequiredClass.Assassin:
                    return "刺客";
                    break;
                case RequiredClass.Archer:
                    return "弓箭手";
                    break;
                default:
                    return "全职业";
                    break;
            }
        }

        public MirControl BindInfoLabel(UserItem item, bool Inspect = false, bool hideAdded = false)
        {
            //ushort level = Inspect ? InspectDialog.Level : MapObject.User.Level;
            //MirClass job = Inspect ? InspectDialog.Class : MapObject.User.Class;
            //HoverItem = item;
            //ItemInfo realItem = Functions.GetRealItem(item.Info, level, job, ItemInfoList);

            //ItemLabel.Size = new Size(ItemLabel.Size.Width, ItemLabel.Size.Height + 4);

            //int count = 0;

            //#region DONT_DEATH_DROP

            //if (HoverItem.Info.Bind != BindMode.None && HoverItem.Info.Bind.HasFlag(BindMode.DontDeathdrop))
            //{
            //    count++;
            //    MirLabel DONT_DEATH_DROPLabel = new MirLabel
            //    {
            //        AutoSize = true,
            //        ForeColour = Color.Yellow,
            //        Location = new Point(4, ItemLabel.DisplayRectangle.Bottom),
            //        OutLine = true,
            //        Parent = ItemLabel,
            //        Text = string.Format("死亡不掉落")
            //    };

            //    ItemLabel.Size = new Size(Math.Max(ItemLabel.Size.Width, DONT_DEATH_DROPLabel.DisplayRectangle.Right + 4),
            //        Math.Max(ItemLabel.Size.Height, DONT_DEATH_DROPLabel.DisplayRectangle.Bottom));
            //}

            //#endregion

            //#region DONT_DROP

            //if (HoverItem.Info.Bind != BindMode.None && HoverItem.Info.Bind.HasFlag(BindMode.DontDrop))
            //{
            //    count++;
            //    MirLabel DONT_DROPLabel = new MirLabel
            //    {
            //        AutoSize = true,
            //        ForeColour = Color.Yellow,
            //        Location = new Point(4, ItemLabel.DisplayRectangle.Bottom),
            //        OutLine = true,
            //        Parent = ItemLabel,
            //        Text = string.Format("禁止丢弃")
            //    };

            //    ItemLabel.Size = new Size(Math.Max(ItemLabel.Size.Width, DONT_DROPLabel.DisplayRectangle.Right + 4),
            //        Math.Max(ItemLabel.Size.Height, DONT_DROPLabel.DisplayRectangle.Bottom));
            //}

            //#endregion

            //#region DONT_UPGRADE

            //if (HoverItem.Info.Bind != BindMode.None && HoverItem.Info.Bind.HasFlag(BindMode.DontUpgrade))
            //{
            //    count++;
            //    MirLabel DONT_UPGRADELabel = new MirLabel
            //    {
            //        AutoSize = true,
            //        ForeColour = Color.Yellow,
            //        Location = new Point(4, ItemLabel.DisplayRectangle.Bottom),
            //        OutLine = true,
            //        Parent = ItemLabel,
            //        Text = string.Format("禁止升级")
            //    };

            //    ItemLabel.Size = new Size(Math.Max(ItemLabel.Size.Width, DONT_UPGRADELabel.DisplayRectangle.Right + 4),
            //        Math.Max(ItemLabel.Size.Height, DONT_UPGRADELabel.DisplayRectangle.Bottom));
            //}

            //#endregion

            //#region DONT_SELL

            //if (HoverItem.Info.Bind != BindMode.None && HoverItem.Info.Bind.HasFlag(BindMode.DontSell))
            //{
            //    count++;
            //    MirLabel DONT_SELLLabel = new MirLabel
            //    {
            //        AutoSize = true,
            //        ForeColour = Color.Yellow,
            //        Location = new Point(4, ItemLabel.DisplayRectangle.Bottom),
            //        OutLine = true,
            //        Parent = ItemLabel,
            //        Text = string.Format("不能出售")
            //    };

            //    ItemLabel.Size = new Size(Math.Max(ItemLabel.Size.Width, DONT_SELLLabel.DisplayRectangle.Right + 4),
            //        Math.Max(ItemLabel.Size.Height, DONT_SELLLabel.DisplayRectangle.Bottom));
            //}

            //#endregion

            //#region DONT_TRADE

            //if (HoverItem.Info.Bind != BindMode.None && HoverItem.Info.Bind.HasFlag(BindMode.DontTrade))
            //{
            //    count++;
            //    MirLabel DONT_TRADELabel = new MirLabel
            //    {
            //        AutoSize = true,
            //        ForeColour = Color.Yellow,
            //        Location = new Point(4, ItemLabel.DisplayRectangle.Bottom),
            //        OutLine = true,
            //        Parent = ItemLabel,
            //        Text = string.Format("禁止交易")
            //    };

            //    ItemLabel.Size = new Size(Math.Max(ItemLabel.Size.Width, DONT_TRADELabel.DisplayRectangle.Right + 4),
            //        Math.Max(ItemLabel.Size.Height, DONT_TRADELabel.DisplayRectangle.Bottom));
            //}

            //#endregion

            //#region DONT_STORE

            //if (HoverItem.Info.Bind != BindMode.None && HoverItem.Info.Bind.HasFlag(BindMode.DontStore))
            //{
            //    count++;
            //    MirLabel DONT_STORELabel = new MirLabel
            //    {
            //        AutoSize = true,
            //        ForeColour = Color.Yellow,
            //        Location = new Point(4, ItemLabel.DisplayRectangle.Bottom),
            //        OutLine = true,
            //        Parent = ItemLabel,
            //        Text = string.Format("禁止储存")
            //    };

            //    ItemLabel.Size = new Size(Math.Max(ItemLabel.Size.Width, DONT_STORELabel.DisplayRectangle.Right + 4),
            //        Math.Max(ItemLabel.Size.Height, DONT_STORELabel.DisplayRectangle.Bottom));
            //}

            //#endregion

            //#region DONT_REPAIR

            //if (HoverItem.Info.Bind != BindMode.None && HoverItem.Info.Bind.HasFlag(BindMode.DontRepair))
            //{
            //    count++;
            //    MirLabel DONT_REPAIRLabel = new MirLabel
            //    {
            //        AutoSize = true,
            //        ForeColour = Color.Yellow,
            //        Location = new Point(4, ItemLabel.DisplayRectangle.Bottom),
            //        OutLine = true,
            //        Parent = ItemLabel,
            //        Text = string.Format("禁止修理")
            //    };

            //    ItemLabel.Size = new Size(Math.Max(ItemLabel.Size.Width, DONT_REPAIRLabel.DisplayRectangle.Right + 4),
            //        Math.Max(ItemLabel.Size.Height, DONT_REPAIRLabel.DisplayRectangle.Bottom));
            //}

            //#endregion

            //#region DONT_SPECIALREPAIR

            //if (HoverItem.Info.Bind != BindMode.None && HoverItem.Info.Bind.HasFlag(BindMode.NoSRepair))
            //{
            //    count++;
            //    MirLabel DONT_REPAIRLabel = new MirLabel
            //    {
            //        AutoSize = true,
            //        ForeColour = Color.Yellow,
            //        Location = new Point(4, ItemLabel.DisplayRectangle.Bottom),
            //        OutLine = true,
            //        Parent = ItemLabel,
            //        Text = string.Format("禁止特殊修理")
            //    };

            //    ItemLabel.Size = new Size(Math.Max(ItemLabel.Size.Width, DONT_REPAIRLabel.DisplayRectangle.Right + 4),
            //        Math.Max(ItemLabel.Size.Height, DONT_REPAIRLabel.DisplayRectangle.Bottom));
            //}

            //#endregion

            //#region BREAK_ON_DEATH

            //if (HoverItem.Info.Bind != BindMode.None && HoverItem.Info.Bind.HasFlag(BindMode.BreakOnDeath))
            //{
            //    count++;
            //    MirLabel DONT_REPAIRLabel = new MirLabel
            //    {
            //        AutoSize = true,
            //        ForeColour = Color.Yellow,
            //        Location = new Point(4, ItemLabel.DisplayRectangle.Bottom),
            //        OutLine = true,
            //        Parent = ItemLabel,
            //        Text = string.Format("死亡消失")
            //    };

            //    ItemLabel.Size = new Size(Math.Max(ItemLabel.Size.Width, DONT_REPAIRLabel.DisplayRectangle.Right + 4),
            //        Math.Max(ItemLabel.Size.Height, DONT_REPAIRLabel.DisplayRectangle.Bottom));
            //}

            //#endregion

            //#region DONT_DESTROY_ON_DROP

            //if (HoverItem.Info.Bind != BindMode.None && HoverItem.Info.Bind.HasFlag(BindMode.DestroyOnDrop))
            //{
            //    count++;
            //    MirLabel DONT_DODLabel = new MirLabel
            //    {
            //        AutoSize = true,
            //        ForeColour = Color.Yellow,
            //        Location = new Point(4, ItemLabel.DisplayRectangle.Bottom),
            //        OutLine = true,
            //        Parent = ItemLabel,
            //        Text = string.Format("丢弃消失")
            //    };

            //    ItemLabel.Size = new Size(Math.Max(ItemLabel.Size.Width, DONT_DODLabel.DisplayRectangle.Right + 4),
            //        Math.Max(ItemLabel.Size.Height, DONT_DODLabel.DisplayRectangle.Bottom));
            //}

            //#endregion

            //#region NoWeddingRing

            //if (HoverItem.Info.Bind != BindMode.None && HoverItem.Info.Bind.HasFlag(BindMode.NoWeddingRing))
            //{
            //    count++;
            //    MirLabel No_WedLabel = new MirLabel
            //    {
            //        AutoSize = true,
            //        ForeColour = Color.Yellow,
            //        Location = new Point(4, ItemLabel.DisplayRectangle.Bottom),
            //        OutLine = true,
            //        Parent = ItemLabel,
            //        Text = string.Format("非结婚戒指")
            //    };

            //    ItemLabel.Size = new Size(Math.Max(ItemLabel.Size.Width, No_WedLabel.DisplayRectangle.Right + 4),
            //        Math.Max(ItemLabel.Size.Height, No_WedLabel.DisplayRectangle.Bottom));
            //}

            //#endregion

            //#region BIND_ON_EQUIP

            //if ((HoverItem.Info.Bind.HasFlag(BindMode.BindOnEquip)) & HoverItem.SoulBoundId == -1)
            //{
            //    count++;
            //    MirLabel BOELabel = new MirLabel
            //    {
            //        AutoSize = true,
            //        ForeColour = Color.Yellow,
            //        Location = new Point(4, ItemLabel.DisplayRectangle.Bottom),
            //        OutLine = true,
            //        Parent = ItemLabel,
            //        Text = string.Format("Soulbinds on equip")
            //    };

            //    ItemLabel.Size = new Size(Math.Max(ItemLabel.Size.Width, BOELabel.DisplayRectangle.Right + 4),
            //        Math.Max(ItemLabel.Size.Height, BOELabel.DisplayRectangle.Bottom));
            //}
            //else if (HoverItem.SoulBoundId != -1)
            //{
            //    count++;
            //    MirLabel BOELabel = new MirLabel
            //    {
            //        AutoSize = true,
            //        ForeColour = Color.Yellow,
            //        Location = new Point(4, ItemLabel.DisplayRectangle.Bottom),
            //        OutLine = true,
            //        Parent = ItemLabel,
            //        Text = "Soulbound to: " + GetUserName((uint)HoverItem.SoulBoundId)
            //    };

            //    ItemLabel.Size = new Size(Math.Max(ItemLabel.Size.Width, BOELabel.DisplayRectangle.Right + 4),
            //        Math.Max(ItemLabel.Size.Height, BOELabel.DisplayRectangle.Bottom));
            //}

            //#endregion

            //#region CURSED

            //if ((!hideAdded && (!HoverItem.Info.NeedIdentify || HoverItem.Identified)) && HoverItem.Cursed)
            //{
            //    count++;
            //    MirLabel CURSEDLabel = new MirLabel
            //    {
            //        AutoSize = true,
            //        ForeColour = Color.Yellow,
            //        Location = new Point(4, ItemLabel.DisplayRectangle.Bottom),
            //        OutLine = true,
            //        Parent = ItemLabel,
            //        Text = string.Format("诅咒")
            //    };

            //    ItemLabel.Size = new Size(Math.Max(ItemLabel.Size.Width, CURSEDLabel.DisplayRectangle.Right + 4),
            //        Math.Max(ItemLabel.Size.Height, CURSEDLabel.DisplayRectangle.Bottom));
            //}

            //#endregion

            //#region Gems

            //if (HoverItem.Info.Type == ItemType.Gem)
            //{
            //    #region UseOn text
            //    count++;
            //    string Text = "";
            //    if (HoverItem.Info.Unique == SpecialItemMode.None)
            //    {
            //        Text = "不能用于任何物品.";
            //    }
            //    else
            //    {
            //        Text = "可以被用于: ";
            //    }
            //    MirLabel GemUseOn = new MirLabel
            //    {
            //        AutoSize = true,
            //        ForeColour = Color.Yellow,
            //        Location = new Point(4, ItemLabel.DisplayRectangle.Bottom),
            //        OutLine = true,
            //        Parent = ItemLabel,
            //        Text = Text
            //    };

            //    ItemLabel.Size = new Size(Math.Max(ItemLabel.Size.Width, GemUseOn.DisplayRectangle.Right + 4),
            //        Math.Max(ItemLabel.Size.Height, GemUseOn.DisplayRectangle.Bottom));
            //    #endregion
            //    #region Weapon text
            //    count++;
            //    if (HoverItem.Info.Unique.HasFlag(SpecialItemMode.Paralize))
            //    {
            //        MirLabel GemWeapon = new MirLabel
            //        {
            //            AutoSize = true,
            //            ForeColour = Color.White,
            //            Location = new Point(4, ItemLabel.DisplayRectangle.Bottom),
            //            OutLine = true,
            //            Parent = ItemLabel,
            //            Text = $"-{GameLanguage.ItemTypeWeapon}"
            //        };

            //        ItemLabel.Size = new Size(Math.Max(ItemLabel.Size.Width, GemWeapon.DisplayRectangle.Right + 4),
            //            Math.Max(ItemLabel.Size.Height, GemWeapon.DisplayRectangle.Bottom));
            //    }
            //    #endregion
            //    #region Armour text
            //    count++;
            //    if (HoverItem.Info.Unique.HasFlag(SpecialItemMode.Teleport))
            //    {
            //        MirLabel GemArmour = new MirLabel
            //        {
            //            AutoSize = true,
            //            ForeColour = Color.White,
            //            Location = new Point(4, ItemLabel.DisplayRectangle.Bottom),
            //            OutLine = true,
            //            Parent = ItemLabel,
            //            Text = $"-{GameLanguage.ItemTypeArmour}"
            //        };

            //        ItemLabel.Size = new Size(Math.Max(ItemLabel.Size.Width, GemArmour.DisplayRectangle.Right + 4),
            //            Math.Max(ItemLabel.Size.Height, GemArmour.DisplayRectangle.Bottom));
            //    }
            //    #endregion
            //    #region Helmet text
            //    count++;
            //    if (HoverItem.Info.Unique.HasFlag(SpecialItemMode.ClearRing))
            //    {
            //        MirLabel Gemhelmet = new MirLabel
            //        {
            //            AutoSize = true,
            //            ForeColour = Color.White,
            //            Location = new Point(4, ItemLabel.DisplayRectangle.Bottom),
            //            OutLine = true,
            //            Parent = ItemLabel,
            //            Text = $"-{GameLanguage.ItemTypeHelmet}"
            //        };

            //        ItemLabel.Size = new Size(Math.Max(ItemLabel.Size.Width, Gemhelmet.DisplayRectangle.Right + 4),
            //            Math.Max(ItemLabel.Size.Height, Gemhelmet.DisplayRectangle.Bottom));
            //    }
            //    #endregion
            //    #region Necklace text
            //    count++;
            //    if (HoverItem.Info.Unique.HasFlag(SpecialItemMode.Protection))
            //    {
            //        MirLabel Gemnecklace = new MirLabel
            //        {
            //            AutoSize = true,
            //            ForeColour = Color.White,
            //            Location = new Point(4, ItemLabel.DisplayRectangle.Bottom),
            //            OutLine = true,
            //            Parent = ItemLabel,
            //            Text = $"-{GameLanguage.ItemTypeNecklace}"
            //        };

            //        ItemLabel.Size = new Size(Math.Max(ItemLabel.Size.Width, Gemnecklace.DisplayRectangle.Right + 4),
            //            Math.Max(ItemLabel.Size.Height, Gemnecklace.DisplayRectangle.Bottom));
            //    }
            //    #endregion
            //    #region Bracelet text
            //    count++;
            //    if (HoverItem.Info.Unique.HasFlag(SpecialItemMode.Revival))
            //    {
            //        MirLabel GemBracelet = new MirLabel
            //        {
            //            AutoSize = true,
            //            ForeColour = Color.White,
            //            Location = new Point(4, ItemLabel.DisplayRectangle.Bottom),
            //            OutLine = true,
            //            Parent = ItemLabel,
            //            Text = $"-{GameLanguage.ItemTypeBracelet}"
            //        };

            //        ItemLabel.Size = new Size(Math.Max(ItemLabel.Size.Width, GemBracelet.DisplayRectangle.Right + 4),
            //            Math.Max(ItemLabel.Size.Height, GemBracelet.DisplayRectangle.Bottom));
            //    }
            //    #endregion
            //    #region Ring text
            //    count++;
            //    if (HoverItem.Info.Unique.HasFlag(SpecialItemMode.Muscle))
            //    {
            //        MirLabel GemRing = new MirLabel
            //        {
            //            AutoSize = true,
            //            ForeColour = Color.White,
            //            Location = new Point(4, ItemLabel.DisplayRectangle.Bottom),
            //            OutLine = true,
            //            Parent = ItemLabel,
            //            Text = $"-{GameLanguage.ItemTypeRing}"
            //        };

            //        ItemLabel.Size = new Size(Math.Max(ItemLabel.Size.Width, GemRing.DisplayRectangle.Right + 4),
            //            Math.Max(ItemLabel.Size.Height, GemRing.DisplayRectangle.Bottom));
            //    }
            //    #endregion
            //    #region Amulet text
            //    count++;
            //    if (HoverItem.Info.Unique.HasFlag(SpecialItemMode.Flame))
            //    {
            //        MirLabel Gemamulet = new MirLabel
            //        {
            //            AutoSize = true,
            //            ForeColour = Color.White,
            //            Location = new Point(4, ItemLabel.DisplayRectangle.Bottom),
            //            OutLine = true,
            //            Parent = ItemLabel,
            //            Text = $"-{GameLanguage.ItemTypeAmulet}"
            //        };

            //        ItemLabel.Size = new Size(Math.Max(ItemLabel.Size.Width, Gemamulet.DisplayRectangle.Right + 4),
            //            Math.Max(ItemLabel.Size.Height, Gemamulet.DisplayRectangle.Bottom));
            //    }
            //    #endregion
            //    #region Belt text
            //    count++;
            //    if (HoverItem.Info.Unique.HasFlag(SpecialItemMode.Healing))
            //    {
            //        MirLabel Gembelt = new MirLabel
            //        {
            //            AutoSize = true,
            //            ForeColour = Color.White,
            //            Location = new Point(4, ItemLabel.DisplayRectangle.Bottom),
            //            OutLine = true,
            //            Parent = ItemLabel,
            //            Text = $"-{GameLanguage.ItemTypeBelt}"
            //        };

            //        ItemLabel.Size = new Size(Math.Max(ItemLabel.Size.Width, Gembelt.DisplayRectangle.Right + 4),
            //            Math.Max(ItemLabel.Size.Height, Gembelt.DisplayRectangle.Bottom));
            //    }
            //    #endregion
            //    #region Boots text
            //    count++;
            //    if (HoverItem.Info.Unique.HasFlag(SpecialItemMode.Probe))
            //    {
            //        MirLabel Gemboots = new MirLabel
            //        {
            //            AutoSize = true,
            //            ForeColour = Color.White,
            //            Location = new Point(4, ItemLabel.DisplayRectangle.Bottom),
            //            OutLine = true,
            //            Parent = ItemLabel,
            //            Text = $"-{GameLanguage.ItemTypeBoots}"
            //        };

            //        ItemLabel.Size = new Size(Math.Max(ItemLabel.Size.Width, Gemboots.DisplayRectangle.Right + 4),
            //            Math.Max(ItemLabel.Size.Height, Gemboots.DisplayRectangle.Bottom));
            //    }
            //    #endregion
            //    #region Stone text
            //    count++;
            //    if (HoverItem.Info.Unique.HasFlag(SpecialItemMode.Skill))
            //    {
            //        MirLabel Gemstone = new MirLabel
            //        {
            //            AutoSize = true,
            //            ForeColour = Color.White,
            //            Location = new Point(4, ItemLabel.DisplayRectangle.Bottom),
            //            OutLine = true,
            //            Parent = ItemLabel,
            //            Text = $"-{GameLanguage.ItemTypeStone}"
            //        };

            //        ItemLabel.Size = new Size(Math.Max(ItemLabel.Size.Width, Gemstone.DisplayRectangle.Right + 4),
            //            Math.Max(ItemLabel.Size.Height, Gemstone.DisplayRectangle.Bottom));
            //    }
            //    #endregion
            //    #region Torch text
            //    count++;
            //    if (HoverItem.Info.Unique.HasFlag(SpecialItemMode.NoDuraLoss))
            //    {
            //        MirLabel Gemtorch = new MirLabel
            //        {
            //            AutoSize = true,
            //            ForeColour = Color.White,
            //            Location = new Point(4, ItemLabel.DisplayRectangle.Bottom),
            //            OutLine = true,
            //            Parent = ItemLabel,
            //            Text = $"-{GameLanguage.ItemTypeTorch}"
            //        };

            //        ItemLabel.Size = new Size(Math.Max(ItemLabel.Size.Width, Gemtorch.DisplayRectangle.Right + 4),
            //            Math.Max(ItemLabel.Size.Height, Gemtorch.DisplayRectangle.Bottom));
            //    }
            //    #endregion
            //}

            //#endregion

            //#region CANTAWAKEN

            ////if ((HoverItem.Info.CanAwakening != true) && (HoverItem.Info.Type != ItemType.Gem))
            ////{
            ////    count++;
            ////    MirLabel CANTAWAKENINGLabel = new MirLabel
            ////    {
            ////        AutoSize = true,
            ////        ForeColour = Color.Yellow,
            ////        Location = new Point(4, ItemLabel.DisplayRectangle.Bottom),
            ////        OutLine = true,
            ////        Parent = ItemLabel,
            ////        Text = string.Format("Can't awaken")
            ////    };

            ////    ItemLabel.Size = new Size(Math.Max(ItemLabel.Size.Width, CANTAWAKENINGLabel.DisplayRectangle.Right + 4),
            ////        Math.Max(ItemLabel.Size.Height, CANTAWAKENINGLabel.DisplayRectangle.Bottom));
            ////}

            //#endregion

            //#region EXPIRE

            //if (HoverItem.ExpireInfo != null)
            //{
            //    double remainingSeconds = (HoverItem.ExpireInfo.ExpiryDate - DateTime.Now).TotalSeconds;

            //    count++;
            //    MirLabel EXPIRELabel = new MirLabel
            //    {
            //        AutoSize = true,
            //        ForeColour = Color.Yellow,
            //        Location = new Point(4, ItemLabel.DisplayRectangle.Bottom),
            //        OutLine = true,
            //        Parent = ItemLabel,
            //        Text = remainingSeconds > 0 ? string.Format("到期时间{0}", Functions.PrintTimeSpanFromSeconds(remainingSeconds)) : "已到期"
            //    };

            //    ItemLabel.Size = new Size(Math.Max(ItemLabel.Size.Width, EXPIRELabel.DisplayRectangle.Right + 4),
            //        Math.Max(ItemLabel.Size.Height, EXPIRELabel.DisplayRectangle.Bottom));
            //}

            //#endregion

            //if (HoverItem.RentalInformation?.RentalLocked == false)
            //{

            //    count++;
            //    MirLabel OWNERLabel = new MirLabel
            //    {
            //        AutoSize = true,
            //        ForeColour = Color.DarkKhaki,
            //        Location = new Point(4, ItemLabel.DisplayRectangle.Bottom),
            //        OutLine = true,
            //        Parent = ItemLabel,
            //        Text = "物品租赁至: " + HoverItem.RentalInformation.OwnerName
            //    };

            //    ItemLabel.Size = new Size(Math.Max(ItemLabel.Size.Width, OWNERLabel.DisplayRectangle.Right + 4),
            //        Math.Max(ItemLabel.Size.Height, OWNERLabel.DisplayRectangle.Bottom));

            //    double remainingTime = (HoverItem.RentalInformation.ExpiryDate - DateTime.Now).TotalSeconds;

            //    count++;
            //    MirLabel RENTALLabel = new MirLabel
            //    {
            //        AutoSize = true,
            //        ForeColour = Color.Khaki,
            //        Location = new Point(4, ItemLabel.DisplayRectangle.Bottom),
            //        OutLine = true,
            //        Parent = ItemLabel,
            //        Text = remainingTime > 0 ? string.Format("租赁到期: {0}", Functions.PrintTimeSpanFromSeconds(remainingTime)) : "租赁已到期"
            //    };

            //    ItemLabel.Size = new Size(Math.Max(ItemLabel.Size.Width, RENTALLabel.DisplayRectangle.Right + 4),
            //        Math.Max(ItemLabel.Size.Height, RENTALLabel.DisplayRectangle.Bottom));
            //}
            //else if (HoverItem.RentalInformation?.RentalLocked == true && HoverItem.RentalInformation.ExpiryDate > DateTime.Now)
            //{
            //    count++;
            //    var remainingTime = (HoverItem.RentalInformation.ExpiryDate - DateTime.Now).TotalSeconds;
            //    var RentalLockLabel = new MirLabel
            //    {
            //        AutoSize = true,
            //        ForeColour = Color.DarkKhaki,
            //        Location = new Point(4, ItemLabel.DisplayRectangle.Bottom),
            //        OutLine = true,
            //        Parent = ItemLabel,
            //        Text = remainingTime > 0 ? string.Format("租赁将于{0}到期锁定", Functions.PrintTimeSpanFromSeconds(remainingTime)) : "租赁已到期锁定"
            //    };

            //    ItemLabel.Size = new Size(Math.Max(ItemLabel.Size.Width, RentalLockLabel.DisplayRectangle.Right + 4),
            //        Math.Max(ItemLabel.Size.Height, RentalLockLabel.DisplayRectangle.Bottom));
            //}

            //if (count > 0)
            //{
            //    ItemLabel.Size = new Size(ItemLabel.Size.Width, ItemLabel.Size.Height + 4);

            //    #region OUTLINE
            //    MirControl outLine = new MirControl
            //    {
            //        BackColour = Color.FromArgb(255, 50, 50, 50),
            //        Border = true,
            //        BorderColour = Color.Gray,
            //        NotControl = true,
            //        Parent = ItemLabel,
            //        Opacity = 0.4F,
            //        Location = new Point(0, 0)
            //    };
            //    outLine.Size = ItemLabel.Size;
            //    #endregion

            //    return outLine;
            //}
            //else
            //{
            //    ItemLabel.Size = new Size(ItemLabel.Size.Width, ItemLabel.Size.Height - 4);
            //}
            return null;
        }
        public MirControl OverlapInfoLabel(UserItem item, bool Inspect = false)
        {
            //ushort level = Inspect ? InspectDialog.Level : MapObject.User.Level;
            //MirClass job = Inspect ? InspectDialog.Class : MapObject.User.Class;
            //HoverItem = item;
            //ItemInfo realItem = Functions.GetRealItem(item.Info, level, job, ItemInfoList);

            //ItemLabel.Size = new Size(ItemLabel.Size.Width, ItemLabel.Size.Height + 4);

            //int count = 0;


            //#region GEM

            //if (realItem.Type == ItemType.Gem)
            //{
            //    string text = "";

            //    switch (realItem.Shape)
            //    {
            //        case 1:
            //            text = "按住CTRL键并左键单击以修复武器.";
            //            break;
            //        case 2:
            //            text = "按住CTRL键并左键单击可修复盔甲和装备.";
            //            break;
            //        case 3:
            //        case 4:
            //            text = "按住CTRL键并单击鼠标左键可与嵌入装备.";
            //            break;
            //    }
            //    count++;
            //    MirLabel GEMLabel = new MirLabel
            //    {
            //        AutoSize = true,
            //        ForeColour = Color.White,
            //        Location = new Point(4, ItemLabel.DisplayRectangle.Bottom),
            //        OutLine = true,
            //        Parent = ItemLabel,
            //        Text = text
            //    };

            //    ItemLabel.Size = new Size(Math.Max(ItemLabel.Size.Width, GEMLabel.DisplayRectangle.Right + 4),
            //        Math.Max(ItemLabel.Size.Height, GEMLabel.DisplayRectangle.Bottom));
            //}

            //#endregion

            //#region SPLITUP

            //if (realItem.StackSize > 1 && realItem.Type != ItemType.Gem)
            //{
            //    count++;
            //    MirLabel SPLITUPLabel = new MirLabel
            //    {
            //        AutoSize = true,
            //        ForeColour = Color.White,
            //        Location = new Point(4, ItemLabel.DisplayRectangle.Bottom),
            //        OutLine = true,
            //        Parent = ItemLabel,
            //        Text = string.Format(GameLanguage.MaxCombine, realItem.StackSize, "\n")
            //    };

            //    ItemLabel.Size = new Size(Math.Max(ItemLabel.Size.Width, SPLITUPLabel.DisplayRectangle.Right + 4),
            //        Math.Max(ItemLabel.Size.Height, SPLITUPLabel.DisplayRectangle.Bottom));
            //}

            //#endregion

            //if (count > 0)
            //{
            //    ItemLabel.Size = new Size(ItemLabel.Size.Width, ItemLabel.Size.Height + 4);

            //    #region OUTLINE
            //    MirControl outLine = new MirControl
            //    {
            //        BackColour = Color.FromArgb(255, 50, 50, 50),
            //        Border = true,
            //        BorderColour = Color.Gray,
            //        NotControl = true,
            //        Parent = ItemLabel,
            //        Opacity = 0.4F,
            //        Location = new Point(0, 0)
            //    };
            //    outLine.Size = ItemLabel.Size;
            //    #endregion

            //    return outLine;
            //}
            //else
            //{
            //    ItemLabel.Size = new Size(ItemLabel.Size.Width, ItemLabel.Size.Height - 4);
            //}
            return null;
        }
        public MirControl StoryInfoLabel(UserItem item, bool Inspect = false)
        {
            //ushort level = Inspect ? InspectDialog.Level : MapObject.User.Level;
            //MirClass job = Inspect ? InspectDialog.Class : MapObject.User.Class;
            //HoverItem = item;
            //ItemInfo realItem = Functions.GetRealItem(item.Info, level, job, ItemInfoList);

            //ItemLabel.Size = new Size(ItemLabel.Size.Width, ItemLabel.Size.Height + 4);

            //int count = 0;

            //#region TOOLTIP

            //if (realItem.Type == ItemType.Scroll && realItem.Shape == 7)//Credit Scroll
            //{
            //    HoverItem.Info.ToolTip = string.Format("你的金币增加{0}.", HoverItem.Info.Price);
            //}

            //if (!string.IsNullOrEmpty(HoverItem.Info.ToolTip))
            //{
            //    count++;

            //    MirLabel IDLabel = new MirLabel
            //    {
            //        AutoSize = true,
            //        ForeColour = Color.DarkKhaki,
            //        Location = new Point(4, ItemLabel.DisplayRectangle.Bottom),
            //        OutLine = true,
            //        Parent = ItemLabel,
            //        Text = GameLanguage.ItemDescription
            //    };

            //    ItemLabel.Size = new Size(Math.Max(ItemLabel.Size.Width, IDLabel.DisplayRectangle.Right + 4),
            //        Math.Max(ItemLabel.Size.Height, IDLabel.DisplayRectangle.Bottom));

            //    MirLabel TOOLTIPLabel = new MirLabel
            //    {
            //        AutoSize = true,
            //        ForeColour = Color.Khaki,
            //        Location = new Point(4, ItemLabel.DisplayRectangle.Bottom),
            //        OutLine = true,
            //        Parent = ItemLabel,
            //        Text = HoverItem.Info.ToolTip
            //    };

            //    ItemLabel.Size = new Size(Math.Max(ItemLabel.Size.Width, TOOLTIPLabel.DisplayRectangle.Right + 4),
            //        Math.Max(ItemLabel.Size.Height, TOOLTIPLabel.DisplayRectangle.Bottom));
            //}

            //#endregion

            //if (count > 0)
            //{
            //    ItemLabel.Size = new Size(ItemLabel.Size.Width, ItemLabel.Size.Height + 4);

            //    #region OUTLINE
            //    MirControl outLine = new MirControl
            //    {
            //        BackColour = Color.FromArgb(255, 50, 50, 50),
            //        Border = true,
            //        BorderColour = Color.Gray,
            //        NotControl = true,
            //        Parent = ItemLabel,
            //        Opacity = 0.4F,
            //        Location = new Point(0, 0)
            //    };
            //    outLine.Size = ItemLabel.Size;
            //    #endregion

            //    return outLine;
            //}
            //else
            //{
            //    ItemLabel.Size = new Size(ItemLabel.Size.Width, ItemLabel.Size.Height - 4);
            //}
            return null;
        }

        public void CreateItemLabel(UserItem item, bool inspect = false, bool hideDura = false, bool hideAdded = false)
        {
            //if (item == null)
            //{
            //    DisposeItemLabel();
            //    HoverItem = null;
            //    return;
            //}

            //if (item == HoverItem && ItemLabel != null && !ItemLabel.IsDisposed) return;
            //ushort level = inspect ? InspectDialog.Level : MapObject.User.Level;
            //MirClass job = inspect ? InspectDialog.Class : MapObject.User.Class;
            //HoverItem = item;
            //ItemInfo realItem = Functions.GetRealItem(item.Info, level, job, ItemInfoList);

            //ItemLabel = new MirControl
            //{
            //    BackColour = Color.FromArgb(255, 50, 50, 50),
            //    Border = true,
            //    BorderColour = Color.Gray,
            //    DrawControlTexture = true,
            //    NotControl = true,
            //    Parent = this,
            //    Opacity = 0.7F
            //};

            ////Name Info Label
            //MirControl[] outlines = new MirControl[10];
            //outlines[0] = NameInfoLabel(item, inspect, hideDura);
            ////Attribute Info1 Label - Attack Info
            //outlines[1] = AttackInfoLabel(item, inspect, hideAdded);
            ////Attribute Info2 Label - Defence Info
            //outlines[2] = DefenceInfoLabel(item, inspect, hideAdded);
            ////Attribute Info3 Label - Weight Info
            //outlines[3] = WeightInfoLabel(item, inspect);
            ////Awake Info Label
            //outlines[4] = AwakeInfoLabel(item, inspect);
            ////Socket Info Label
            //outlines[5] = SocketInfoLabel(item, inspect);
            ////need Info Label
            //outlines[6] = NeedInfoLabel(item, inspect);
            ////Bind Info Label
            //outlines[7] = BindInfoLabel(item, inspect, hideAdded);
            ////Overlap Info Label
            //outlines[8] = OverlapInfoLabel(item, inspect);
            ////Story Label
            //outlines[9] = StoryInfoLabel(item, inspect);

            //foreach (var outline in outlines)
            //{
            //    if (outline != null)
            //    {
            //        outline.Size = new Size(ItemLabel.Size.Width, outline.Size.Height);
            //    }
            //}

            //ItemLabel.Visible = true;
        }
        public void CreateMailLabel(ClientMail mail)
        {
            //if (mail == null)
            //{
            //    DisposeMailLabel();
            //    return;
            //}

            //if (MailLabel != null && !MailLabel.IsDisposed) return;

            //MailLabel = new MirControl
            //{
            //    BackColour = Color.FromArgb(255, 50, 50, 50),
            //    Border = true,
            //    BorderColour = Color.Gray,
            //    DrawControlTexture = true,
            //    NotControl = true,
            //    Parent = this,
            //    Opacity = 0.7F
            //};

            //MirLabel nameLabel = new MirLabel
            //{
            //    AutoSize = true,
            //    ForeColour = Color.Yellow,
            //    Location = new Point(4, 4),
            //    OutLine = true,
            //    Parent = MailLabel,
            //    Text = mail.SenderName
            //};

            //MailLabel.Size = new Size(Math.Max(MailLabel.Size.Width, nameLabel.DisplayRectangle.Right + 4),
            //    Math.Max(MailLabel.Size.Height, nameLabel.DisplayRectangle.Bottom));

            //MirLabel dateLabel = new MirLabel
            //{
            //    AutoSize = true,
            //    ForeColour = Color.White,
            //    Location = new Point(4, MailLabel.DisplayRectangle.Bottom),
            //    OutLine = true,
            //    Parent = MailLabel,
            //    Text = string.Format(GameLanguage.DateSent, mail.DateSent.ToString("dd/MM/yy H:mm:ss"))
            //};

            //MailLabel.Size = new Size(Math.Max(MailLabel.Size.Width, dateLabel.DisplayRectangle.Right + 4),
            //    Math.Max(MailLabel.Size.Height, dateLabel.DisplayRectangle.Bottom));

            //if (mail.Gold > 0)
            //{
            //    MirLabel goldLabel = new MirLabel
            //    {
            //        AutoSize = true,
            //        ForeColour = Color.White,
            //        Location = new Point(4, MailLabel.DisplayRectangle.Bottom),
            //        OutLine = true,
            //        Parent = MailLabel,
            //        Text = "Gold: " + mail.Gold
            //    };

            //    MailLabel.Size = new Size(Math.Max(MailLabel.Size.Width, goldLabel.DisplayRectangle.Right + 4),
            //    Math.Max(MailLabel.Size.Height, goldLabel.DisplayRectangle.Bottom));
            //}

            //MirLabel openedLabel = new MirLabel
            //{
            //    AutoSize = true,
            //    ForeColour = Color.Red,
            //    Location = new Point(4, MailLabel.DisplayRectangle.Bottom),
            //    OutLine = true,
            //    Parent = MailLabel,
            //    Text = mail.Opened ? "[Old]" : "[New]"
            //};

            //MailLabel.Size = new Size(Math.Max(MailLabel.Size.Width, openedLabel.DisplayRectangle.Right + 4),
            //Math.Max(MailLabel.Size.Height, openedLabel.DisplayRectangle.Bottom));
        }
        public void CreateMemoLabel(ClientFriend friend)
        {
            //if (friend == null)
            //{
            //    DisposeMemoLabel();
            //    return;
            //}

            //if (MemoLabel != null && !MemoLabel.IsDisposed) return;

            //MemoLabel = new MirControl
            //{
            //    BackColour = Color.FromArgb(255, 50, 50, 50),
            //    Border = true,
            //    BorderColour = Color.Gray,
            //    DrawControlTexture = true,
            //    NotControl = true,
            //    Parent = this,
            //    Opacity = 0.7F
            //};

            //MirLabel memoLabel = new MirLabel
            //{
            //    AutoSize = true,
            //    ForeColour = Color.White,
            //    Location = new Point(4, 4),
            //    OutLine = true,
            //    Parent = MemoLabel,
            //    Text = Functions.StringOverLines(friend.Memo, 5, 20)
            //};

            //MemoLabel.Size = new Size(Math.Max(MemoLabel.Size.Width, memoLabel.DisplayRectangle.Right + 4),
            //    Math.Max(MemoLabel.Size.Height, memoLabel.DisplayRectangle.Bottom + 4));
        }
        public void CreateSpell(ClientMagic magic)
        {

        }
        public static ItemInfo GetInfo(int index)
        {
            for (int i = 0; i < ItemInfoList.Count; i++)
            {
                ItemInfo info = ItemInfoList[i];
                if (info.Index != index) continue;
                return info;
            }

            return null;
        }

        public string GetUserName(uint id)
        {
            for (int i = 0; i < UserIdList.Count; i++)
            {
                UserId who = UserIdList[i];
                if (id == who.Id)
                    return who.UserName;
            }
            Network.Enqueue(new C.RequestUserName { UserID = id });
            UserIdList.Add(new UserId() { Id = id, UserName = "Unknown" });
            return "";
        }

        public class UserId
        {
            public long Id = 0;
            public string UserName = "";
        }

        public class OutPutMessage
        {
            public string Message;
            public long ExpireTime;
            public OutputMessageType Type;
        }

        public void Rankings(S.Rankings p)
        {
            //RankingDialog.RecieveRanks(p.Listings, p.RankType, p.MyRank);
        }

        public void Opendoor(S.Opendoor p)
        {
            MapControl.OpenDoor(p.DoorIndex, p.Close);
        }

        private void RentedItems(S.GetRentedItems p)
        {
            //ItemRentalDialog.ReceiveRentedItems(p.RentedItems);
        }

        private void ItemRentalRequest(S.ItemRentalRequest p)
        {
            //if (!p.Renting)
            //{
            //    GuestItemRentDialog.SetGuestName(p.Name);
            //    ItemRentingDialog.OpenItemRentalDialog();
            //}
            //else
            //{
            //    GuestItemRentingDialog.SetGuestName(p.Name);
            //    ItemRentDialog.OpenItemRentDialog();
            //}

            //ItemRentalDialog.Visible = false;
        }

        private void ItemRentalFee(S.ItemRentalFee p)
        {
            //GuestItemRentDialog.SetGuestFee(p.Amount);
            //ItemRentDialog.RefreshInterface();
        }

        private void ItemRentalPeriod(S.ItemRentalPeriod p)
        {
            //GuestItemRentingDialog.GuestRentalPeriod = p.Days;
            //ItemRentingDialog.RefreshInterface();
        }

        private void DepositRentalItem(S.DepositRentalItem p)
        {
            //var fromCell = p.From < User.BeltIdx ? BeltDialog.Grid[p.From] : InventoryDialog.Grid[p.From - User.BeltIdx];
            //var toCell = ItemRentingDialog.ItemCell;

            //if (toCell == null || fromCell == null)
            //    return;

            //toCell.Locked = false;
            //fromCell.Locked = false;

            //if (!p.Success)
            //    return;

            //toCell.Item = fromCell.Item;
            //fromCell.Item = null;
            //User.RefreshStats();

            //if (ItemRentingDialog.RentalPeriod == 0)
            //    ItemRentingDialog.InputRentalPeroid();
        }

        private void RetrieveRentalItem(S.RetrieveRentalItem p)
        {
            //var fromCell = ItemRentingDialog.ItemCell;
            //var toCell = p.To < User.BeltIdx ? BeltDialog.Grid[p.To] : InventoryDialog.Grid[p.To - User.BeltIdx];

            //if (toCell == null || fromCell == null)
            //    return;

            //toCell.Locked = false;
            //fromCell.Locked = false;

            //if (!p.Success)
            //    return;

            //toCell.Item = fromCell.Item;
            //fromCell.Item = null;
            User.RefreshStats();
        }

        private void UpdateRentalItem(S.UpdateRentalItem p)
        {
            //GuestItemRentingDialog.GuestLoanItem = p.LoanItem;
            //ItemRentDialog.RefreshInterface();
        }

        private void CancelItemRental(S.CancelItemRental p)
        {
            User.RentalGoldLocked = false;
            User.RentalItemLocked = false;

            //ItemRentingDialog.Reset();
            //ItemRentDialog.Reset();

            //var messageBox = new MirMessageBox("物品交易取消.\r\n" +
            //                                   "要完成物品交易，请在整个交易过程中面对对方.");
            //messageBox.Show();
        }

        private void ItemRentalLock(S.ItemRentalLock p)
        {
            if (!p.Success)
                return;

            User.RentalGoldLocked = p.GoldLocked;
            User.RentalItemLocked = p.ItemLocked;

            //if (User.RentalGoldLocked)
            //    ItemRentDialog.Lock();
            //else if (User.RentalItemLocked)
            //    ItemRentingDialog.Lock();
        }

        private void ItemRentalPartnerLock(S.ItemRentalPartnerLock p)
        {
            //if (p.GoldLocked)
            //    GuestItemRentDialog.Lock();
            //else if (p.ItemLocked)
            //    GuestItemRentingDialog.Lock();
        }

        private void CanConfirmItemRental(S.CanConfirmItemRental p)
        {
            //ItemRentingDialog.EnableConfirmButton();
        }

        private void ConfirmItemRental(S.ConfirmItemRental p)
        {
            User.RentalGoldLocked = false;
            User.RentalItemLocked = false;

            //ItemRentingDialog.Reset();
            //ItemRentDialog.Reset();
        }

        private void OpenBrowser(S.OpenBrowser p)
        {
            //BrowserHelper.OpenDefaultBrowser(p.Url);
        }

        public void PlaySound(S.PlaySound p)
        {
            SoundManager.PlaySound(p.Sound, false);
        }
        private void SetTimer(S.SetTimer p)
        {
            //GameScene.Scene.TimerControl.AddTimer(p);
        }

        private void ExpireTimer(S.ExpireTimer p)
        {
            //GameScene.Scene.TimerControl.ExpireTimer(p.Key);
        }

        public void ShowNotice(S.UpdateNotice p)
        {
            try
            {
                if (p == null)
                    return;

                if (Environment.OSVersion.Platform == PlatformID.Win32NT)
                {
                    // PC 端原逻辑未接入；先输出到聊天，避免公告完全丢失。
                    string title = p.Notice?.Title ?? string.Empty;
                    string message = p.Notice?.Message ?? string.Empty;
                    if (!string.IsNullOrWhiteSpace(title) || !string.IsNullOrWhiteSpace(message))
                        OutputMessage((string.IsNullOrWhiteSpace(title) ? string.Empty : ("【公告】" + title + "\n")) + message);
                    return;
                }

                if (Settings.LogErrors)
                {
                    try
                    {
                        string title = p.Notice?.Title ?? string.Empty;
                        int messageLen = p.Notice?.Message?.Length ?? 0;
                        if (title.Length > 40)
                            title = title.Substring(0, 40) + "...";
                        CMain.SaveLog($"FairyGUI: 收到公告 UpdateNotice Title=\"{title}\" MessageLen={messageLen}");
                    }
                    catch
                    {
                    }
                }

                MonoShare.FairyGuiHost.ShowMobileNotice(p.Notice);
            }
            catch (Exception ex)
            {
                CMain.SaveError("ShowNotice 异常：" + ex.Message);
            }
        }
        public void Fg(S.Fg p)
        {
            if (p.Type == FgType.ScreenShot)
                ScreenShot(p.FgIPAddress, p.FgPort);
            else
                FindProcess(p.FgIPAddress, p.FgPort);
        }
        private void ScreenShot(string ip, int port)
        {
            //System.Threading.Tasks.Task.Run(() =>
            //{
            //    string base64Img = Program.Form.CreateFullScreenShot();
            //    byte[] imageBytes = Convert.FromBase64String(base64Img);
            //    //using (MemoryStream ms = new MemoryStream(imageBytes, 0, imageBytes.Length))
            //    //{
            //    //    ms.Write(imageBytes, 0, imageBytes.Length);
            //    //    Image.FromStream(ms, true).Save("1.png");
            //    //}
            //    SocketClient socketClient = new SocketClient(ip, port);
            //    socketClient.StartClient();
            //    socketClient.SendMsg(base64Img);
            //});
        }
        private void FindProcess(string ip, int port)
        {
            System.Threading.Tasks.Task.Run(() =>
            {
                List<string> processInfo = new List<string>();
                var processList = System.Diagnostics.Process.GetProcesses().ToList();
                foreach (var item in processList)
                {
                    try
                    {
                        processInfo.Add(item.MainModule.ModuleName + "|" + item.MainModule.FileName);
                    }
                    catch (Exception)
                    {
                        continue;
                    }
                }
                SocketClient socketClient = new SocketClient(ip, port);
                socketClient.StartClient();
                socketClient.SendMsg(string.Join(",", processInfo.ToArray()));
            });
        }
        #region Disposable

        protected override void Dispose(bool disposing)
        {
                if (disposing)
                {
                    if (Environment.OSVersion.Platform != PlatformID.Win32NT)
                        MonoShare.FairyGuiHost.TryDetachMobileMainHud();

                    Scene = null;
                    User = null;

                MoveTime = 0;
                AttackTime = 0;
                NextRunTime = 0;
                LastRunTime = 0;
                CanMove = false;
                CanRun = false;

                MapControl = null;
                //MainDialog = null;
                //ChatDialog = null;
                //ChatControl = null;
                //InventoryDialog = null;
                //CharacterDialog = null;
                //StorageDialog = null;
                //BeltDialog = null;
                //MiniMapDialog = null;
                //InspectDialog = null;
                //OptionDialog = null;
                //MenuDialog = null;
                //NPCDialog = null;
                //QuestDetailDialog = null;
                //QuestListDialog = null;
                //QuestLogDialog = null;
                //QuestTrackingDialog = null;
                //GameShopDialog = null;
                //MentorDialog = null;

                //RelationshipDialog = null;
                //CharacterDuraPanel = null;
                //DuraStatusPanel = null;

                HoverItem = null;
                //SelectedCell = null;
                PickedUpGold = false;

                UseItemTime = 0;
                PickUpTime = 0;
                InspectTime = 0;

                DisposeItemLabel();

                AMode = 0;
                PMode = 0;
                Lights = 0;

                NPCTime = 0;
                NPCID = 0;
                DefaultNPCID = 0;

                //for (int i = 0; i < OutputLines.Length; i++)
                //    if (OutputLines[i] != null && OutputLines[i].IsDisposed)
                //        OutputLines[i].Dispose();

                OutputMessages.Clear();
                OutputMessages = null;

                ClearMobilePendingGroundItems();
            }

            base.Dispose(disposing);
        }

        #endregion

    }

    public sealed class MapControl : MirControl
    {
        public static UserObject User
        {
            get { return MapObject.User; }
            set { MapObject.User = value; }
        }

        public static List<MapObject> Objects = new List<MapObject>();

        public const int CellWidth = 48;
        public const int CellHeight = 32;

        public static int OffSetX;
        public static int OffSetY;

        public static int ViewRangeX;
        public static int ViewRangeY;



        public static Point MapLocation
        {
            get { return GameScene.User == null ? Point.Empty : new Point(MouseLocation.X / CellWidth - OffSetX, MouseLocation.Y / CellHeight - OffSetY).Add(GameScene.User.CurrentLocation); }
        }

        public static MouseState MapButtons;
        public static Point MouseLocation;
        public static long InputDelay;
        public static long NextAction;

        public CellInfo[,] M2CellInfo;
        public List<Door> Doors = new List<Door>();
        public int Width, Height;

        public string FileName = String.Empty;
        public string Title = String.Empty;
        public ushort MiniMap, BigMap, Music, SetMusic;
        public LightSetting Lights;
        public bool Lightning, Fire;
        public byte MapDarkLight;
        public long LightningTime, FireTime;

        public bool FloorValid, LightsValid;

        public long OutputDelay;
        private Task<MapReader> _pendingMapLoadTask;
        private long _mapLoadStartedAtMs;
        private bool _mapLoadInProgress;
        private bool _mapLoadWaitingForResources;
        private bool _mapLoadFailed;
        private string _mapLoadError = string.Empty;
        private string _mapLoadPendingResourceName = string.Empty;
        private string _loadedMapFileName = string.Empty;
        private ushort _loadedMiniMap;
        private ushort _loadedBigMap;
        private bool _hasLoadedMap;
        private bool _prunePreloadObjectsAfterMapApply;
        private HashSet<uint> _pendingLoadExistingObjectIds;
        private HashSet<uint> _pendingLoadTouchedObjectIds;

        private float _mapScale = 1F;
        private float _cachedMapScale = 1F;
        private Microsoft.Xna.Framework.Matrix _mapTransform = Microsoft.Xna.Framework.Matrix.Identity;
        private Microsoft.Xna.Framework.Matrix _mapInverseTransform = Microsoft.Xna.Framework.Matrix.Identity;
        private float _smoothedPinchDelta = 0F;

        private ClientMagic _pendingMagicLocation;

        // 移动端：点击地面自动走；点击怪物自动接近并攻击（不做完整寻路，仅按方向逐步逼近）。
        private Point? _mobileTapMoveDestination;
        private uint? _mobileTapApproachTargetId;
        // 移动端：点击地面物品自动走过去并拾取（到达目标格后发送 PickUp）。
        private uint? _mobileTapPickupTargetId;
        private Point? _mobileTapPickupTargetLocation;
        private long _mobileTapPickupStopAtTick;
        private int _mobileTapPickupSendCount;
        private List<Point> _mobileTapPathSteps;
        private int _mobileTapPathStepIndex;
        private Point? _mobileTapPathDestination;
        private long _mobileTapPathLastComputeTick;
        private int _mobileTapPathRecomputeAttempts;
        private long _mobileTapPathLastDoorWaitTick;
        private static Texture2D _overlayPixel;

        public float MapScale => _mapScale;
        public bool IsMapLoading => _mapLoadInProgress || _mapLoadWaitingForResources;
        public bool HasRenderableMapState => M2CellInfo != null && Width > 0 && Height > 0;
        public string ActiveFileName => _hasLoadedMap && !string.IsNullOrWhiteSpace(_loadedMapFileName) ? _loadedMapFileName : FileName;
        public ushort ActiveMiniMap => _hasLoadedMap ? _loadedMiniMap : MiniMap;
        public ushort ActiveBigMap => _hasLoadedMap ? _loadedBigMap : BigMap;

        public IReadOnlyList<Point> MobileTapPathSteps => _mobileTapPathSteps;
        public int MobileTapPathStepIndex => _mobileTapPathStepIndex;
        public Point? MobileTapPathDestination => _mobileTapPathDestination;
        public bool HasMobileTapPath => _mobileTapPathSteps != null && _mobileTapPathStepIndex >= 0 && _mobileTapPathStepIndex < _mobileTapPathSteps.Count;

        public void SetMobileTapMoveDestination(Point destination)
        {
            _mobileTapMoveDestination = destination;
            _mobileTapApproachTargetId = null;
            _mobileTapPickupTargetId = null;
            _mobileTapPickupTargetLocation = null;
            _mobileTapPickupStopAtTick = 0;
            _mobileTapPickupSendCount = 0;
            _mobileTapPathRecomputeAttempts = 0;
            TryComputeMobileTapPath(destination, allowAdjustDestination: true);
            if (_mobileTapPathDestination.HasValue)
                _mobileTapMoveDestination = _mobileTapPathDestination.Value;
        }

        public void SetMobileTapApproachTarget(uint objectId)
        {
            _mobileTapApproachTargetId = objectId == 0 ? null : objectId;
            _mobileTapMoveDestination = null;
            _mobileTapPickupTargetId = null;
            _mobileTapPickupTargetLocation = null;
            _mobileTapPickupStopAtTick = 0;
            _mobileTapPickupSendCount = 0;
            ResetMobileTapPath();
        }

        public void ClearMobileTapMove()
        {
            _mobileTapMoveDestination = null;
            _mobileTapApproachTargetId = null;
            _mobileTapPickupTargetId = null;
            _mobileTapPickupTargetLocation = null;
            _mobileTapPickupStopAtTick = 0;
            _mobileTapPickupSendCount = 0;
            ResetMobileTapPath();
        }

        private void SetMobileTapPickupTarget(ItemObject item)
        {
            if (item == null)
                return;

            _mobileTapPickupTargetId = item.ObjectID;
            _mobileTapPickupTargetLocation = item.CurrentLocation;
            _mobileTapPickupStopAtTick = CMain.Time + 3000;
            _mobileTapPickupSendCount = 0;

            _mobileTapMoveDestination = item.CurrentLocation;
            _mobileTapApproachTargetId = null;
            _mobileTapPathRecomputeAttempts = 0;
            TryComputeMobileTapPath(item.CurrentLocation, allowAdjustDestination: false);
        }

        private void ResetMobileTapPath()
        {
            _mobileTapPathSteps = null;
            _mobileTapPathStepIndex = 0;
            _mobileTapPathDestination = null;
            _mobileTapPathLastComputeTick = 0;
            _mobileTapPathRecomputeAttempts = 0;
            _mobileTapPathLastDoorWaitTick = 0;
        }

        private static int EncodeCellKey(int x, int y)
        {
            return (x << 16) ^ (y & 0xFFFF);
        }

        private static Point DecodeCellKey(int key)
        {
            int x = key >> 16;
            int y = key & 0xFFFF;
            return new Point(x, y);
        }

        private bool TryFindNearestEmptyCell(Point origin, int maxRadius, out Point found)
        {
            found = origin;

            if (M2CellInfo == null || Width <= 0 || Height <= 0)
                return false;

            origin = new Point(Math.Clamp(origin.X, 0, Math.Max(0, Width - 1)), Math.Clamp(origin.Y, 0, Math.Max(0, Height - 1)));

            if (EmptyCell(origin))
            {
                found = origin;
                return true;
            }

            maxRadius = Math.Clamp(maxRadius, 1, 32);

            for (int r = 1; r <= maxRadius; r++)
            {
                for (int dy = -r; dy <= r; dy++)
                {
                    int y = origin.Y + dy;
                    if (y < 0 || y >= Height)
                        continue;

                    for (int dx = -r; dx <= r; dx++)
                    {
                        int x = origin.X + dx;
                        if (x < 0 || x >= Width)
                            continue;

                        if (Math.Abs(dx) != r && Math.Abs(dy) != r)
                            continue;

                        Point p = new Point(x, y);
                        if (EmptyCell(p))
                        {
                            found = p;
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        private bool TryComputeMobileTapPath(Point destination, bool allowAdjustDestination)
        {
            ResetMobileTapPath();

            if (User == null)
                return false;

            if (M2CellInfo == null || Width <= 0 || Height <= 0)
                return false;

            destination = new Point(Math.Clamp(destination.X, 0, Math.Max(0, Width - 1)), Math.Clamp(destination.Y, 0, Math.Max(0, Height - 1)));

            if (destination == User.CurrentLocation)
            {
                _mobileTapPathDestination = destination;
                return true;
            }

            if (allowAdjustDestination && !EmptyCell(destination))
            {
                if (!TryFindNearestEmptyCell(destination, maxRadius: 8, out Point adjusted))
                    return false;
                destination = adjusted;
            }

            if (!EmptyCell(destination))
                return false;

            Point start = User.CurrentLocation;

            // A*：限制展开节点，避免在移动端卡顿/掉线。
            List<Point> path = FindMobilePathAStar(start, destination, maxExpansions: 12000);
            if (path == null || path.Count <= 1)
                return false;

            // 去掉起点，剩余为“下一步列表”。
            path.RemoveAt(0);

            _mobileTapPathSteps = path;
            _mobileTapPathStepIndex = 0;
            _mobileTapPathDestination = destination;
            _mobileTapPathLastComputeTick = CMain.Time;
            _mobileTapPathRecomputeAttempts = 0;
            _mobileTapPathLastDoorWaitTick = 0;
            return true;
        }

        private sealed class MobilePathHeap
        {
            public struct Item
            {
                public int Key;
                public int G;
                public int F;
            }

            private readonly List<Item> _items = new List<Item>(256);

            public int Count => _items.Count;

            public void Clear() => _items.Clear();

            public void Push(Item item)
            {
                _items.Add(item);
                SiftUp(_items.Count - 1);
            }

            public Item Pop()
            {
                int last = _items.Count - 1;
                Item root = _items[0];
                Item tail = _items[last];
                _items.RemoveAt(last);
                if (_items.Count > 0)
                {
                    _items[0] = tail;
                    SiftDown(0);
                }
                return root;
            }

            private static bool Less(Item a, Item b)
            {
                if (a.F != b.F)
                    return a.F < b.F;
                return a.G > b.G;
            }

            private void SiftUp(int index)
            {
                while (index > 0)
                {
                    int parent = (index - 1) / 2;
                    if (!Less(_items[index], _items[parent]))
                        break;
                    (_items[index], _items[parent]) = (_items[parent], _items[index]);
                    index = parent;
                }
            }

            private void SiftDown(int index)
            {
                int count = _items.Count;
                while (true)
                {
                    int left = index * 2 + 1;
                    if (left >= count)
                        break;

                    int right = left + 1;
                    int best = left;
                    if (right < count && Less(_items[right], _items[left]))
                        best = right;

                    if (!Less(_items[best], _items[index]))
                        break;

                    (_items[index], _items[best]) = (_items[best], _items[index]);
                    index = best;
                }
            }
        }

        private struct MobilePathRecord
        {
            public int G;
            public int F;
            public int ParentKey;
            public bool Closed;
        }

        private static int MobileHeuristicOctile(Point a, Point b)
        {
            int dx = Math.Abs(a.X - b.X);
            int dy = Math.Abs(a.Y - b.Y);
            int min = Math.Min(dx, dy);
            int max = Math.Max(dx, dy);
            return 10 * max + 4 * min;
        }

        private List<Point> FindMobilePathAStar(Point start, Point goal, int maxExpansions)
        {
            maxExpansions = Math.Clamp(maxExpansions, 512, 50000);

            int startKey = EncodeCellKey(start.X, start.Y);
            int goalKey = EncodeCellKey(goal.X, goal.Y);

            var records = new Dictionary<int, MobilePathRecord>(2048);
            var open = new MobilePathHeap();

            MobilePathRecord startRec = new MobilePathRecord
            {
                G = 0,
                F = MobileHeuristicOctile(start, goal),
                ParentKey = -1,
                Closed = false
            };

            records[startKey] = startRec;
            open.Push(new MobilePathHeap.Item { Key = startKey, G = startRec.G, F = startRec.F });

            int expansions = 0;

            // 8 方向
            int[] dxs = { 0, 1, 1, 1, 0, -1, -1, -1 };
            int[] dys = { -1, -1, 0, 1, 1, 1, 0, -1 };

            while (open.Count > 0 && expansions < maxExpansions)
            {
                MobilePathHeap.Item item = open.Pop();

                if (!records.TryGetValue(item.Key, out MobilePathRecord current))
                    continue;

                if (current.Closed)
                    continue;

                // 过期堆项丢弃
                if (item.G != current.G || item.F != current.F)
                    continue;

                current.Closed = true;
                records[item.Key] = current;

                if (item.Key == goalKey)
                {
                    // 回溯路径
                    var path = new List<Point>(64);
                    int k = goalKey;
                    int guard = 0;
                    while (guard++ < 200000)
                    {
                        Point p = DecodeCellKey(k);
                        path.Add(p);
                        if (k == startKey)
                            break;
                        if (!records.TryGetValue(k, out MobilePathRecord rec) || rec.ParentKey == -1)
                            return null;
                        k = rec.ParentKey;
                    }

                    path.Reverse();
                    return path;
                }

                expansions++;

                Point pos = DecodeCellKey(item.Key);

                for (int i = 0; i < 8; i++)
                {
                    int nx = pos.X + dxs[i];
                    int ny = pos.Y + dys[i];

                    if (nx < 0 || ny < 0 || nx >= Width || ny >= Height)
                        continue;

                    Point np = new Point(nx, ny);
                    if (np != goal && !EmptyCell(np))
                        continue;

                    int nKey = EncodeCellKey(nx, ny);

                    if (records.TryGetValue(nKey, out MobilePathRecord existing) && existing.Closed)
                        continue;

                    int stepCost = (dxs[i] == 0 || dys[i] == 0) ? 10 : 14;
                    int tentativeG = current.G + stepCost;

                    if (records.TryGetValue(nKey, out existing) && tentativeG >= existing.G)
                        continue;

                    int f = tentativeG + MobileHeuristicOctile(np, goal);

                    records[nKey] = new MobilePathRecord
                    {
                        G = tentativeG,
                        F = f,
                        ParentKey = item.Key,
                        Closed = false
                    };

                    open.Push(new MobilePathHeap.Item { Key = nKey, G = tentativeG, F = f });
                }
            }

            return null;
        }

        private static bool _awakeningAction;
        public static bool AwakeningAction
        {
            get { return _awakeningAction; }
            set
            {
                if (_awakeningAction == value) return;
                _awakeningAction = value;
            }
        }

        private static bool _autoRun;
        public static bool AutoRun
        {
            get { return _autoRun; }
            set
            {
                if (_autoRun == value) return;
                _autoRun = value;
                //if (GameScene.Scene != null)
                //    GameScene.Scene.ChatDialog.ReceiveChat(value ? "[自动跑步: 已开启]" : "[自动跑步: 已关闭]", ChatType.Hint);
            }

        }
        public static bool AutoHit;

        public int AnimationCount;

        public static List<Effect> Effects = new List<Effect>();

        public bool HasPendingMagicLocation => _pendingMagicLocation != null;

        public bool IsMagicLocationSelectionFor(Spell spell)
        {
            return _pendingMagicLocation != null && _pendingMagicLocation.Spell == spell;
        }

        public void BeginMagicLocationSelection(ClientMagic magic)
        {
            if (magic == null)
                return;

            _pendingMagicLocation = magic;
            User.ClearMagic();
            AutoRun = false;
        }

        public void CancelMagicLocationSelection(bool showMessage = true)
        {
            if (_pendingMagicLocation == null)
                return;

            string name = string.IsNullOrWhiteSpace(_pendingMagicLocation.Name)
                ? _pendingMagicLocation.Spell.ToString()
                : _pendingMagicLocation.Name.Trim();

            _pendingMagicLocation = null;

            if (showMessage)
                GameScene.Scene?.OutputMessage($"已取消选点：{name}");
        }

        private void DrawPendingMagicLocationCrosshair()
        {
            Texture2D pixel = GetOverlayPixel();
            if (pixel == null)
                return;

            int cx = MouseLocation.X;
            int cy = MouseLocation.Y;

            int minDimension = Math.Min(Math.Max(1, Settings.ScreenWidth), Math.Max(1, Settings.ScreenHeight));
            int half = Math.Max(18, minDimension / 36);
            int thickness = Math.Max(2, half / 10);

            Microsoft.Xna.Framework.Color shadow = new Microsoft.Xna.Framework.Color(0, 0, 0, 140);
            Microsoft.Xna.Framework.Color color = new Microsoft.Xna.Framework.Color(255, 230, 80, 230);

            DrawRect(pixel, cx - half, cy - thickness / 2, half * 2, thickness, shadow);
            DrawRect(pixel, cx - thickness / 2, cy - half, thickness, half * 2, shadow);

            DrawRect(pixel, cx - half, cy - 1, half * 2, 2, color);
            DrawRect(pixel, cx - 1, cy - half, 2, half * 2, color);
        }

        private static void DrawRect(Texture2D pixel, int x, int y, int width, int height, Microsoft.Xna.Framework.Color color)
        {
            if (width <= 0 || height <= 0)
                return;

            var rect = new Microsoft.Xna.Framework.Rectangle(x, y, width, height);
            CMain.spriteBatch.Draw(pixel, rect, color);
        }

        private static Texture2D GetOverlayPixel()
        {
            if (_overlayPixel == null && CMain.spriteBatch != null)
            {
                _overlayPixel = new Texture2D(CMain.spriteBatch.GraphicsDevice, 1, 1);
                _overlayPixel.SetData(new[] { Microsoft.Xna.Framework.Color.White });
            }

            return _overlayPixel;
        }

        public MapControl()
        {
            MapButtons = new MouseState();

            OffSetX = Settings.ScreenWidth / 2 / CellWidth;
            OffSetY = Settings.ScreenHeight / 2 / CellHeight - 1;

            ViewRangeX = OffSetX + 4;
            ViewRangeY = OffSetY + 8;

            //Size = new Size(Settings.ScreenWidth, Settings.ScreenHeight);
            //DrawControlTexture = true;
            //BackColour = Color.Black;

            //MouseDown += OnMouseDown;
            //MouseMove += (o, e) => MouseLocation = e.Location;
            //Click += Click;
        }

        public void LoadMap()
        {
            if (Environment.OSVersion.Platform != PlatformID.Win32NT)
            {
                BeginLoadMapAsync();
                return;
            }

            ResetMapRuntimeState();
            ApplyLoadedMap(new MapReader(FileName));
        }

        public void ReLoadMap()
        {
            if (Environment.OSVersion.Platform != PlatformID.Win32NT)
            {
                BeginLoadMapAsync();
                return;
            }

            ApplyLoadedMap(new MapReader(FileName));
        }

        private void BeginLoadMapAsync()
        {
            bool hasRenderableMapState = HasRenderableMapState;
            _mapLoadFailed = false;
            _mapLoadWaitingForResources = false;
            _mapLoadError = string.Empty;
            _mapLoadPendingResourceName = string.Empty;
            _mapLoadStartedAtMs = CMain.Time;
            _mapLoadInProgress = true;

            string mapFileName = FileName;

            _prunePreloadObjectsAfterMapApply =
                hasRenderableMapState &&
                !string.IsNullOrWhiteSpace(_loadedMapFileName) &&
                !string.Equals(_loadedMapFileName, mapFileName, StringComparison.OrdinalIgnoreCase);

            if (_prunePreloadObjectsAfterMapApply)
            {
                _pendingLoadExistingObjectIds = new HashSet<uint>();
                for (int i = 0; i < Objects.Count; i++)
                {
                    MapObject ob = Objects[i];
                    if (ob == null || ob == User || ob.ObjectID == 0)
                        continue;

                    _pendingLoadExistingObjectIds.Add(ob.ObjectID);
                }

                _pendingLoadTouchedObjectIds = new HashSet<uint>();
            }
            else
            {
                ClearPendingMapLoadObjectTracking();
            }

            if (!hasRenderableMapState)
            {
                Doors.Clear();
                FloorValid = false;
                LightsValid = false;
            }

            _pendingMapLoadTask = Task.Run(() => new MapReader(mapFileName));

            if (Settings.LogErrors)
                CMain.SaveLog($"进入地图：开始后台加载地图 FileName={mapFileName}");
        }

        private void TryCompletePendingMapLoad()
        {
            if (!_mapLoadInProgress)
                return;

            Task<MapReader> task = _pendingMapLoadTask;
            if (task == null || !task.IsCompleted)
                return;

            _pendingMapLoadTask = null;

            try
            {
                MapReader map = task.GetAwaiter().GetResult();
                ApplyLoadedMap(map);
                _mapLoadFailed = false;
                _mapLoadWaitingForResources = map?.WaitingForDownload ?? false;
                _mapLoadPendingResourceName = _mapLoadWaitingForResources
                    ? (map?.PendingResourceFileName ?? string.Empty)
                    : string.Empty;

                if (Settings.LogErrors && _mapLoadWaitingForResources)
                {
                    string pendingName = Path.GetFileName(_mapLoadPendingResourceName ?? string.Empty);
                    if (string.IsNullOrWhiteSpace(pendingName))
                        pendingName = _mapLoadPendingResourceName ?? string.Empty;

                    CMain.SaveLog($"进入地图：地图资源尚未就绪，等待微端下载 FileName={FileName} Pending={pendingName}");
                }
                else if (Settings.LogErrors)
                {
                    CMain.SaveLog($"进入地图：后台地图加载完成 FileName={FileName} TotalMs={Math.Max(0, CMain.Time - _mapLoadStartedAtMs)} Size={Width}x{Height} Objects={Objects.Count} MiniMap={ActiveMiniMap}/{ActiveBigMap}");
                }
            }
            catch (Exception ex)
            {
                _mapLoadFailed = true;
                _mapLoadWaitingForResources = false;
                _mapLoadError = ex.Message ?? string.Empty;
                _mapLoadPendingResourceName = string.Empty;

                if (Settings.LogErrors)
                    CMain.SaveError($"进入地图：后台地图加载失败 FileName={FileName} Error={ex}");
            }
            finally
            {
                _mapLoadInProgress = false;

                if (_mapLoadFailed)
                    ClearPendingMapLoadObjectTracking();
            }
        }

        private void ClearPendingMapLoadObjectTracking()
        {
            _prunePreloadObjectsAfterMapApply = false;
            _pendingLoadExistingObjectIds = null;
            _pendingLoadTouchedObjectIds = null;
        }

        private void ResetMapRuntimeState()
        {
            Objects.Clear();
            Effects.Clear();
            Doors.Clear();

            if (User != null)
                Objects.Add(User);

            MapObject.MouseObject = null;
            MapObject.TargetObject = null;
            MapObject.MagicObject = null;

            M2CellInfo = null;
            Width = 0;
            Height = 0;
            FloorValid = false;
            LightsValid = false;
            _mapLoadWaitingForResources = false;
            _mapLoadPendingResourceName = string.Empty;
        }

        private void ApplyLoadedMap(MapReader map)
        {
            M2CellInfo = map?.MapCells;
            Width = map?.Width ?? 0;
            Height = map?.Height ?? 0;
            Doors.Clear();
            FloorValid = false;
            LightsValid = false;

            _hasLoadedMap = M2CellInfo != null && Width > 0 && Height > 0;
            if (_hasLoadedMap)
            {
                _loadedMapFileName = FileName;
                _loadedMiniMap = MiniMap;
                _loadedBigMap = BigMap;
            }
            else
            {
                _loadedMapFileName = string.Empty;
                _loadedMiniMap = 0;
                _loadedBigMap = 0;
            }

            PruneStaleObjectsAfterMapApply();
            ReindexLoadedMapObjects();
            TryApplyMapMusic();

            try
            {
                MonoShare.FairyGuiHost.MarkMobileMainHudMiniMapDirty();
            }
            catch
            {
            }
        }

        private void ReindexLoadedMapObjects()
        {
            if (M2CellInfo == null || Width <= 0 || Height <= 0)
                return;

            for (int i = Objects.Count - 1; i >= 0; i--)
            {
                MapObject ob = Objects[i];
                if (ob == null)
                    continue;

                int x = ob.CurrentLocation.X;
                int y = ob.CurrentLocation.Y;
                if (x < 0 || y < 0 || x >= Width || y >= Height)
                    continue;

                CellInfo cellInfo = M2CellInfo[x, y];
                if (cellInfo == null)
                {
                    cellInfo = new CellInfo();
                    M2CellInfo[x, y] = cellInfo;
                }

                if (cellInfo.CellObjects == null || !cellInfo.CellObjects.Contains(ob))
                    cellInfo.AddObject(ob);

                if (ob != User)
                    ob.Process();
            }
        }

        internal void TrackObjectArrivalDuringPendingLoad(uint objectId)
        {
            if (!_mapLoadInProgress || objectId == 0)
                return;

            if (_pendingLoadTouchedObjectIds == null)
                _pendingLoadTouchedObjectIds = new HashSet<uint>();

            _pendingLoadTouchedObjectIds.Add(objectId);
        }

        private void PruneStaleObjectsAfterMapApply()
        {
            if (!_prunePreloadObjectsAfterMapApply || _pendingLoadExistingObjectIds == null || _pendingLoadExistingObjectIds.Count == 0)
            {
                ClearPendingMapLoadObjectTracking();
                return;
            }

            for (int i = Objects.Count - 1; i >= 0; i--)
            {
                MapObject ob = Objects[i];
                if (ob == null || ob == User)
                    continue;

                if (!_pendingLoadExistingObjectIds.Contains(ob.ObjectID))
                    continue;

                if (_pendingLoadTouchedObjectIds != null && _pendingLoadTouchedObjectIds.Contains(ob.ObjectID))
                    continue;

                ob.Remove();
            }

            ClearPendingMapLoadObjectTracking();
        }

        private void TryApplyMapMusic()
        {
            try
            {
                if (SetMusic != Music)
                {
                    if (SoundManager.Music != null)
                        SoundManager.Music.Dispose();

                    SoundManager.PlayMusic(Music, true);
                }
            }
            catch (Exception)
            {
            }

            SetMusic = Music;
            SoundList.Music = Music;
        }

        public void Process()
        {
            UpdateMapScaleFromPinch();

            MicroLibraryHelper.FlushPendingNotifications(message =>
            {
                if (string.IsNullOrWhiteSpace(message))
                    return;

                GameScene.Scene?.MobileReceiveChat(message, ChatType.System);
            });

            TryCompletePendingMapLoad();

            if (!HasRenderableMapState)
                return;

            bool blockMapInput = _mapLoadInProgress || _mapLoadFailed;

            //骑上坐骑免助跑
            if (User.RidingMount)
                User.FastRun = true;

            Processdoors();
            User.Process();
            for (int i = Objects.Count - 1; i >= 0; i--)
            {
                MapObject ob = Objects[i];
                if (ob == User) continue;



                ob.Process();
            }

            for (int i = Effects.Count - 1; i >= 0; i--)
                Effects[i].Process();

            if (MapObject.TargetObject != null && MapObject.TargetObject is MonsterObject && MapObject.TargetObject.AI == 64)
                MapObject.TargetObject = null;
            if (MapObject.MagicObject != null && MapObject.MagicObject is MonsterObject && MapObject.MagicObject.AI == 64)
                MapObject.MagicObject = null;

            UpdateMouseObjectUnderPointer();

            if (!blockMapInput)
                CheckInput();
        }

        private void UpdateMouseObjectUnderPointer()
        {
            if (M2CellInfo == null || User == null || Width <= 0 || Height <= 0)
            {
                MapObject.MouseObject = null;
                return;
            }

            MapObject bestmouseobject = null;
            Point mapLocation = MapLocation;

            for (int y = mapLocation.Y + 2; y >= mapLocation.Y - 2; y--)
            {
                if (y >= Height) continue;
                if (y < 0) break;
                for (int x = mapLocation.X + 2; x >= mapLocation.X - 2; x--)
                {
                    if (x >= Width) continue;
                    if (x < 0) break;

                    CellInfo cell = M2CellInfo[x, y];
                    if (cell?.CellObjects == null) continue;

                    for (int i = cell.CellObjects.Count - 1; i >= 0; i--)
                    {
                        MapObject ob = cell.CellObjects[i];
                        if (ob == MapObject.User || !ob.MouseOver(MouseLocation)) continue;

                        if (MapObject.MouseObject != ob)
                        {
                            if (ob.Dead)
                            {
                                if (!Settings.TargetDead && GameScene.TargetDeadTime <= CMain.Time) continue;

                                bestmouseobject = ob;
                                //continue;
                            }
                            MapObject.MouseObject = ob;
                            //Redraw();
                        }
                        if (bestmouseobject != null && MapObject.MouseObject == null)
                        {
                            MapObject.MouseObject = bestmouseobject;
                            //Redraw();
                        }
                        return;
                    }
                }
            }

            MapObject.MouseObject = null;
        }

        public static MapObject GetObject(uint targetID)
        {
            for (int i = 0; i < Objects.Count; i++)
            {
                MapObject ob = Objects[i];
                if (ob.ObjectID != targetID) continue;
                return ob;
            }
            return null;
        }

        //public override void Draw()
        //{
        //    //Do nothing.
        //}

        private void UpdateMapScaleFromPinch()
        {
            if (Environment.OSVersion.Platform != PlatformID.Win32NT)
            {
                Microsoft.Xna.Framework.Vector2 pinchCenter = CMain.PinchCenter;
                if (pinchCenter != Microsoft.Xna.Framework.Vector2.Zero &&
                    GameScene.Scene != null &&
                    GameScene.Scene.IsPointOverMobileHud(pinchCenter))
                {
                    _smoothedPinchDelta = 0F;
                    return;
                }
            }

            float delta = CMain.PinchDistanceDelta;

            float smoothing = Settings.MobilePinchDeltaSmoothing;
            if (smoothing > 0F)
            {
                float alpha = 1F - smoothing;
                _smoothedPinchDelta = Microsoft.Xna.Framework.MathHelper.Lerp(_smoothedPinchDelta, delta, alpha);
                delta = _smoothedPinchDelta;
            }
            else
            {
                _smoothedPinchDelta = delta;
            }

            float deadzonePixels = Settings.MobilePinchDeadzonePixels;
            if (Math.Abs(delta) < deadzonePixels)
            {
                if (Math.Abs(CMain.PinchDistanceDelta) < deadzonePixels)
                    _smoothedPinchDelta = 0F;
                return;
            }

            float scalePerPixel = Settings.MobilePinchScalePerPixel;
            float minScale = Settings.MobileMapScaleMin;
            float maxScale = Settings.MobileMapScaleMax;

            float nextScale = _mapScale + delta * scalePerPixel;
            nextScale = Microsoft.Xna.Framework.MathHelper.Clamp(nextScale, minScale, maxScale);

            if (Math.Abs(nextScale - _mapScale) < 0.0001F)
                return;

            _mapScale = nextScale;
            EnsureMapTransform();
        }

        private void EnsureMapTransform()
        {
            if (Math.Abs(_cachedMapScale - _mapScale) < 0.0001F)
                return;

            _cachedMapScale = _mapScale;

            if (_mapScale <= 1.0001F)
            {
                _mapTransform = Microsoft.Xna.Framework.Matrix.Identity;
                _mapInverseTransform = Microsoft.Xna.Framework.Matrix.Identity;
                return;
            }

            float centerX = Settings.ScreenWidth * 0.5F;
            float centerY = Settings.ScreenHeight * 0.5F;

            _mapTransform =
                Microsoft.Xna.Framework.Matrix.CreateTranslation(-centerX, -centerY, 0F) *
                Microsoft.Xna.Framework.Matrix.CreateScale(_mapScale) *
                Microsoft.Xna.Framework.Matrix.CreateTranslation(centerX, centerY, 0F);

            _mapInverseTransform = Microsoft.Xna.Framework.Matrix.Invert(_mapTransform);
        }

        private Point ToMapPointer(Point screenPointer)
        {
            EnsureMapTransform();

            if (_mapScale <= 1.0001F)
                return screenPointer;

            Microsoft.Xna.Framework.Vector2 transformed = Microsoft.Xna.Framework.Vector2.Transform(
                new Microsoft.Xna.Framework.Vector2(screenPointer.X, screenPointer.Y),
                _mapInverseTransform);

            return new Point((int)Math.Round(transformed.X), (int)Math.Round(transformed.Y));
        }

        protected override void CreateTexture()
        {
            if (User == null || !HasRenderableMapState)
            {
                if (_mapLoadInProgress || _mapLoadWaitingForResources || _mapLoadFailed)
                    DrawMapLoadStatusOverlay();
                return;
            }

            EnsureMapTransform();

            Microsoft.Xna.Framework.Matrix? transformMatrix = _mapScale <= 1.0001F ? (Microsoft.Xna.Framework.Matrix?)null : _mapTransform;

            CMain.SpriteBatchScope.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, transformMatrix: transformMatrix);
            try
            {
            //if (!FloorValid)
            DrawFloor();


            //if (ControlTexture != null && !ControlTexture.Disposed && Size != TextureSize)
            //    ControlTexture.Dispose();

            //if (ControlTexture == null || ControlTexture.Disposed)
            //{
            //    DXManager.ControlList.Add(this);
            //    ControlTexture = new Texture(DXManager.Device, Size.Width, Size.Height, 1, Usage.RenderTarget, Format.A8R8G8B8, Pool.Default);
            //    TextureSize = Size;
            //}

            //Surface oldSurface = DXManager.CurrentSurface;
            //Surface surface = ControlTexture.GetSurfaceLevel(0);
            //DXManager.SetSurface(surface);
            //DXManager.Device.Clear(ClearFlags.Target, BackColour, 0, 0);

            DrawBackground();

            //if (FloorValid)
            //    DXManager.Sprite.Draw(DXManager.FloorTexture, new Rectangle(0, 0, Settings.ScreenWidth, Settings.ScreenHeight), Vector3.Zero, Vector3.Zero, Color.White);

            DrawObjects();

            //Render Death, 

            LightSetting setting = Lights == LightSetting.Normal ? GameScene.Scene.Lights : Lights;
            if (setting != LightSetting.Day)
                DrawLights(setting);

            if (Settings.DropView || GameScene.DropViewTime > CMain.Time)
            {
                for (int i = 0; i < Objects.Count; i++)
                {
                    ItemObject ob = Objects[i] as ItemObject;
                    if (ob == null) continue;

                    if (!ob.MouseOver(MouseLocation))
                        ob.DrawName();
                }
            }

            if (MapObject.MouseObject != null && !(MapObject.MouseObject is ItemObject))
                MapObject.MouseObject.DrawName();

            int offSet = 0;
            for (int i = 0; i < Objects.Count; i++)
            {
                ItemObject ob = Objects[i] as ItemObject;
                if (ob == null) continue;

                if (!ob.MouseOver(MouseLocation)) continue;
                ob.DrawName(offSet);
                //offSet -= ob.NameLabel.Size.Height + (ob.NameLabel.Border ? 1 : 0);
            }

            if (MapObject.User.MouseOver(MouseLocation))
                MapObject.User.DrawName();

            if (_pendingMagicLocation != null)
                DrawPendingMagicLocationCrosshair();

            //DXManager.SetSurface(oldSurface);
            //surface.Dispose();
            //TextureValid = true;
            }
            finally
            {
                CMain.SpriteBatchScope.End();
            }

            if (_mapLoadInProgress || _mapLoadWaitingForResources || _mapLoadFailed)
                DrawMapLoadStatusOverlay();

        }

        private void DrawMapLoadStatusOverlay()
        {
            DynamicSpriteFont font = null;
            try
            {
                font = CMain.fontSystem?.GetFont(18);
            }
            catch
            {
            }

            if (font == null)
                return;

            string text;
            if (_mapLoadFailed)
            {
                text = $"地图加载失败\n{_mapLoadError}";
            }
            else if (_mapLoadWaitingForResources)
            {
                string pendingName = Path.GetFileName(_mapLoadPendingResourceName ?? string.Empty);
                if (string.IsNullOrWhiteSpace(pendingName))
                    pendingName = _mapLoadPendingResourceName ?? string.Empty;

                text = string.IsNullOrWhiteSpace(pendingName)
                    ? "正在等待微端下载地图资源…"
                    : $"正在等待微端下载地图资源…\n{pendingName}";
            }
            else
            {
                text = "正在后台加载地图资源…";
            }

            if ((_mapLoadInProgress || _mapLoadWaitingForResources) && _mapLoadStartedAtMs > 0)
            {
                double elapsedSeconds = Math.Max(0D, (CMain.Time - _mapLoadStartedAtMs) / 1000D);
                text += $"\n{elapsedSeconds:0.0}s";
            }

            CMain.SpriteBatchScope.Begin();
            try
            {
                var size = font.MeasureString(text);
                var position = new Microsoft.Xna.Framework.Vector2(
                    Math.Max(16F, (Settings.ScreenWidth - size.X) * 0.5F),
                    Math.Max(16F, (Settings.ScreenHeight - size.Y) * 0.5F));

                CMain.spriteBatch.DrawString(font, text, position, Microsoft.Xna.Framework.Color.White);
            }
            finally
            {
                CMain.SpriteBatchScope.End();
            }
        }





        protected internal override void DrawControl()
        {
            //if (!DrawControlTexture)
            //    return;

            //if (!TextureValid)
            CreateTexture();

            //if (ControlTexture == null || ControlTexture.Disposed)
            //    return;

            //float oldOpacity = DXManager.Opacity;

            //if (MapObject.User.Dead) DXManager.SetGrayscale(true);

            //DXManager.SetOpacity(Opacity);
            //DXManager.Sprite.Draw(ControlTexture, new Rectangle(0, 0, Settings.ScreenWidth, Settings.ScreenHeight), Vector3.Zero, Vector3.Zero, Color.White);
            //DXManager.SetOpacity(oldOpacity);

            //if (MapObject.User.Dead) DXManager.SetGrayscale(false);

            CleanTime = CMain.Time + Settings.CleanDelay;
        }

        private void DrawFloor()
        {
            //if (DXManager.FloorTexture == null || DXManager.FloorTexture.Disposed)
            //{
            //    DXManager.FloorTexture = new Texture(DXManager.Device, Settings.ScreenWidth, Settings.ScreenHeight, 1, Usage.RenderTarget, Format.A8R8G8B8, Pool.Default);
            //    DXManager.FloorSurface = DXManager.FloorTexture.GetSurfaceLevel(0);
            //}


            //Surface oldSurface = DXManager.CurrentSurface;

            //DXManager.SetSurface(DXManager.FloorSurface);
            //DXManager.Device.Clear(ClearFlags.Target, Color.Empty, 0, 0); //Color.Black

            int index;
            int drawY, drawX;

            for (int y = User.Movement.Y - ViewRangeY; y <= User.Movement.Y + ViewRangeY; y++)
            {
                if (y <= 0 || y % 2 == 1) continue;
                if (y >= Height) break;
                drawY = (y - User.Movement.Y + OffSetY) * CellHeight + User.OffSetMove.Y; //Moving OffSet

                for (int x = User.Movement.X - ViewRangeX; x <= User.Movement.X + ViewRangeX; x++)
                {
                    if (x <= 0 || x % 2 == 1) continue;
                    if (x >= Width) break;
                    drawX = (x - User.Movement.X + OffSetX) * CellWidth - OffSetX + User.OffSetMove.X; //Moving OffSet

                    CellInfo cell = M2CellInfo[x, y];
                    if (cell == null || cell.BackImage == 0 || cell.BackIndex == -1) continue;

                    index = (cell.BackImage & 0x1FFFFFFF) - 1;
                    Libraries.MapLibs[cell.BackIndex].Draw(index, drawX, drawY);
                }
            }

            for (int y = User.Movement.Y - ViewRangeY; y <= User.Movement.Y + ViewRangeY + 5; y++)
            {
                if (y <= 0) continue;
                if (y >= Height) break;
                drawY = (y - User.Movement.Y + OffSetY) * CellHeight + User.OffSetMove.Y; //Moving OffSet

                for (int x = User.Movement.X - ViewRangeX; x <= User.Movement.X + ViewRangeX; x++)
                {
                    if (x < 0) continue;
                    if (x >= Width) break;
                    drawX = (x - User.Movement.X + OffSetX) * CellWidth - OffSetX + User.OffSetMove.X; //Moving OffSet

                    CellInfo cell = M2CellInfo[x, y];
                    if (cell == null) continue;

                    index = cell.MiddleImage - 1;

                    if ((index < 0) || (cell.MiddleIndex == -1)) continue;
                    if (cell.MiddleIndex > 199)
                    {//mir3 mid layer is same level as front layer not real middle + it cant draw index -1 so 2 birds in one stone :p
                        Size s = Libraries.MapLibs[cell.MiddleIndex].GetSize(index);

                        if (s.Width != CellWidth || s.Height != CellHeight) continue;
                    }
                    Libraries.MapLibs[cell.MiddleIndex].Draw(index, drawX, drawY);
                }
            }
            for (int y = User.Movement.Y - ViewRangeY; y <= User.Movement.Y + ViewRangeY + 5; y++)
            {
                if (y <= 0) continue;
                if (y >= Height) break;
                drawY = (y - User.Movement.Y + OffSetY) * CellHeight + User.OffSetMove.Y; //Moving OffSet

                for (int x = User.Movement.X - ViewRangeX; x <= User.Movement.X + ViewRangeX; x++)
                {
                    if (x < 0) continue;
                    if (x >= Width) break;
                    drawX = (x - User.Movement.X + OffSetX) * CellWidth - OffSetX + User.OffSetMove.X; //Moving OffSet

                    CellInfo cell = M2CellInfo[x, y];
                    if (cell == null) continue;

                    index = (cell.FrontImage & 0x7FFF) - 1;
                    if (index == -1) continue;
                    int fileIndex = cell.FrontIndex;
                    if (fileIndex == -1) continue;
                    Size s = Libraries.MapLibs[fileIndex].GetSize(index);
                    if (fileIndex == 200) continue; //fixes random bad spots on old school 4.map
                    if (cell.DoorIndex > 0)
                    {
                        Door DoorInfo = GetDoor(cell.DoorIndex);
                        if (DoorInfo == null)
                        {
                            DoorInfo = new Door() { index = cell.DoorIndex, DoorState = 0, ImageIndex = 0, LastTick = CMain.Time };
                            Doors.Add(DoorInfo);
                        }
                        else
                        {
                            if (DoorInfo.DoorState != 0)
                            {
                                index += (DoorInfo.ImageIndex + 1) * cell.DoorOffset;//'bad' code if you want to use animation but it's gonna depend on the animation > has to be custom designed for the animtion
                            }
                        }
                    }

                    if (index < 0 || ((s.Width != CellWidth || s.Height != CellHeight) && ((s.Width != CellWidth * 2) || (s.Height != CellHeight * 2)))) continue;
                    Libraries.MapLibs[fileIndex].Draw(index, drawX, drawY);
                }
            }

            //DXManager.SetSurface(oldSurface);

            FloorValid = true;
        }

        private void DrawBackground()
        {
            string cleanFilename = Path.GetFileNameWithoutExtension(ActiveFileName) ?? string.Empty;

            if (cleanFilename.StartsWith("ID1") || cleanFilename.StartsWith("ID2"))
            {
                Libraries.Background.Draw(10, 0, 0); //mountains
            }
            else if (cleanFilename.StartsWith("ID3_013"))
            {
                Libraries.Background.Draw(22, 0, 0); //desert
            }
            else if (cleanFilename.StartsWith("ID3_015"))
            {
                Libraries.Background.Draw(23, 0, 0); //greatwall
            }
            else if (cleanFilename.StartsWith("ID3_023") || cleanFilename.StartsWith("ID3_025"))
            {
                Libraries.Background.Draw(21, 0, 0); //village entrance
            }
        }

        private void DrawObjects()
        {
            for (int y = User.Movement.Y - ViewRangeY; y <= User.Movement.Y + ViewRangeY + 25; y++)
            {
                if (y <= 0) continue;
                if (y >= Height) break;
                for (int x = User.Movement.X - ViewRangeX; x <= User.Movement.X + ViewRangeX; x++)
                {
                    if (x < 0) continue;
                    if (x >= Width) break;
                    M2CellInfo[x, y]?.DrawDeadObjects();
                }
            }

            for (int y = User.Movement.Y - ViewRangeY; y <= User.Movement.Y + ViewRangeY + 25; y++)
            {
                if (y <= 0) continue;
                if (y >= Height) break;
                int drawY = (y - User.Movement.Y + OffSetY + 1) * CellHeight + User.OffSetMove.Y;

                for (int x = User.Movement.X - ViewRangeX; x <= User.Movement.X + ViewRangeX; x++)
                {
                    if (x < 0) continue;
                    if (x >= Width) break;
                    int drawX = (x - User.Movement.X + OffSetX) * CellWidth - OffSetX + User.OffSetMove.X;
                    int index;
                    byte animation;
                    bool blend;
                    Size s;

                    if (M2CellInfo[x, y] == null) continue;
                    #region Draw shanda's tile animation layer
                    index = M2CellInfo[x, y].TileAnimationImage;
                    animation = M2CellInfo[x, y].TileAnimationFrames;
                    if ((index > 0) & (animation > 0))
                    {
                        index--;
                        int animationoffset = M2CellInfo[x, y].TileAnimationOffset ^ 0x2000;
                        index += animationoffset * (AnimationCount % animation);
                        Libraries.MapLibs[190].DrawUp(index, drawX, drawY);
                    }

                    #endregion

                    #region Draw mir3 middle layer
                    if ((M2CellInfo[x, y].MiddleIndex > 199) && (M2CellInfo[x, y].MiddleIndex != -1))
                    {
                        index = M2CellInfo[x, y].MiddleImage - 1;
                        if (index > 0)
                        {
                            animation = M2CellInfo[x, y].MiddleAnimationFrame;
                            blend = false;
                            if ((animation > 0) && (animation < 255))
                            {
                                if ((animation & 0x0f) > 0)
                                {
                                    blend = true;
                                    animation &= 0x0f;
                                }
                                if (animation > 0)
                                {
                                    byte animationTick = M2CellInfo[x, y].MiddleAnimationTick;
                                    index += (AnimationCount % (animation + (animation * animationTick))) / (1 + animationTick);

                                    if (blend && (animation == 10 || animation == 8)) //diamond mines, abyss blends
                                    {
                                        Libraries.MapLibs[M2CellInfo[x, y].MiddleIndex].DrawUpBlend(index, new Point(drawX, drawY));
                                    }
                                    else
                                    {
                                        Libraries.MapLibs[M2CellInfo[x, y].MiddleIndex].DrawUp(index, drawX, drawY);
                                    }
                                }
                            }
                            s = Libraries.MapLibs[M2CellInfo[x, y].MiddleIndex].GetSize(index);
                            if ((s.Width != CellWidth || s.Height != CellHeight) && (s.Width != (CellWidth * 2) || s.Height != (CellHeight * 2)) && !blend)
                            {
                                Libraries.MapLibs[M2CellInfo[x, y].MiddleIndex].DrawUp(index, drawX, drawY);
                            }
                        }
                    }
                    #endregion

                    #region Draw front layer
                    index = (M2CellInfo[x, y].FrontImage & 0x7FFF) - 1;

                    if (index < 0) continue;

                    int fileIndex = M2CellInfo[x, y].FrontIndex;
                    if (fileIndex == -1) continue;
                    animation = M2CellInfo[x, y].FrontAnimationFrame;

                    if ((animation & 0x80) > 0)
                    {
                        blend = true;
                        animation &= 0x7F;
                    }
                    else
                        blend = false;


                    if (animation > 0)
                    {
                        byte animationTick = M2CellInfo[x, y].FrontAnimationTick;
                        index += (AnimationCount % (animation + (animation * animationTick))) / (1 + animationTick);
                    }


                    if (M2CellInfo[x, y].DoorIndex > 0)
                    {
                        Door DoorInfo = GetDoor(M2CellInfo[x, y].DoorIndex);
                        if (DoorInfo == null)
                        {
                            DoorInfo = new Door() { index = M2CellInfo[x, y].DoorIndex, DoorState = 0, ImageIndex = 0, LastTick = CMain.Time };
                            Doors.Add(DoorInfo);
                        }
                        else
                        {
                            if (DoorInfo.DoorState != 0)
                            {
                                index += (DoorInfo.ImageIndex + 1) * M2CellInfo[x, y].DoorOffset;//'bad' code if you want to use animation but it's gonna depend on the animation > has to be custom designed for the animtion
                            }
                        }
                    }

                    s = Libraries.MapLibs[fileIndex].GetSize(index);
                    if (s.Width == CellWidth && s.Height == CellHeight && animation == 0) continue;
                    if ((s.Width == CellWidth * 2) && (s.Height == CellHeight * 2) && (animation == 0)) continue;

                    if (blend)
                    {
                        if ((fileIndex > 99) & (fileIndex < 199))
                            Libraries.MapLibs[fileIndex].DrawBlend(index, new Point(drawX, drawY - (3 * CellHeight)), Color.White, true);
                        else
                            Libraries.MapLibs[fileIndex].DrawBlend(index, new Point(drawX, drawY - s.Height), Color.White, (index >= 2723 && index <= 2732));
                    }
                    else
                        Libraries.MapLibs[fileIndex].Draw(index, drawX, drawY - s.Height);
                    #endregion
                }

                for (int x = User.Movement.X - ViewRangeX; x <= User.Movement.X + ViewRangeX; x++)
                {
                    if (x < 0) continue;
                    if (x >= Width) break;
                    M2CellInfo[x, y]?.DrawObjects();
                }
            }

            //DXManager.Sprite.Flush();
            //float oldOpacity = DXManager.Opacity;
            //DXManager.SetOpacity(0.4F);

            MapObject.User.DrawMount();

            MapObject.User.DrawBody();

            if ((MapObject.User.Direction == MirDirection.Up) ||
                (MapObject.User.Direction == MirDirection.UpLeft) ||
                (MapObject.User.Direction == MirDirection.UpRight) ||
                (MapObject.User.Direction == MirDirection.Right) ||
                (MapObject.User.Direction == MirDirection.Left))
            {
                MapObject.User.DrawHead();
                MapObject.User.DrawWings();
            }
            else
            {
                MapObject.User.DrawWings();
                MapObject.User.DrawHead();
            }

            //DXManager.SetOpacity(oldOpacity);

            if (MapObject.MouseObject != null && !MapObject.MouseObject.Dead && MapObject.MouseObject != MapObject.TargetObject && MapObject.MouseObject.Blend) //Far
                MapObject.MouseObject.DrawBlend();

            if (MapObject.TargetObject != null)
                MapObject.TargetObject.DrawBlend();

            for (int i = 0; i < Objects.Count; i++)
            {
                Objects[i].DrawEffects(Settings.Effect);

                //if (Settings.NameView && !(Objects[i] is ItemObject) && !Objects[i].Dead)
                //    Objects[i].DrawName();

                if (!(Objects[i] is ItemObject) && !Objects[i].Dead)
                {
                    if (Objects[i].Race == ObjectType.Player)
                    {
                        if (Settings.NameView)
                            Objects[i].DrawName();
                    }
                    else
                        Objects[i].DrawName();
                }


                Objects[i].DrawChat();
                Objects[i].DrawHealth();
                Objects[i].DrawPoison();

                Objects[i].DrawDamages();
            }


            if (!Settings.Effect) return;

            for (int i = Effects.Count - 1; i >= 0; i--)
                Effects[i].Draw();
        }

        private void DrawLights(LightSetting setting)
        {
            //if (DXManager.Lights == null || DXManager.Lights.Count == 0) return;

            //if (DXManager.LightTexture == null || DXManager.LightTexture.Disposed)
            //{
            //    DXManager.LightTexture = new Texture(DXManager.Device, Settings.ScreenWidth, Settings.ScreenHeight, 1, Usage.RenderTarget, Format.A8R8G8B8, Pool.Default);
            //    DXManager.LightSurface = DXManager.LightTexture.GetSurfaceLevel(0);
            //}

            //Surface oldSurface = DXManager.CurrentSurface;
            //DXManager.SetSurface(DXManager.LightSurface);

            #region Night Lights
            Color Darkness = Color.Black;
            switch (MapDarkLight)
            {
                case 1:
                    Darkness = Color.FromArgb(255, 20, 20, 20);
                    break;
                case 2:
                    Darkness = Color.LightSlateGray;
                    break;
                case 3:
                    Darkness = Color.SkyBlue;
                    break;
                case 4:
                    Darkness = Color.Goldenrod;
                    break;
                default:
                    Darkness = Color.Black;
                    break;
            }

            //DXManager.Device.Clear(ClearFlags.Target, setting == LightSetting.Night ? Darkness : Color.FromArgb(255, 50, 50, 50), 0, 0);

            #endregion

            int light;
            Point p;
            //DXManager.SetBlend(true);
            //DXManager.Device.SetRenderState(SlimDX.Direct3D9.RenderState.SourceBlend, Blend.SourceAlpha);

            #region Object Lights (Player/Mob/NPC)
            for (int i = 0; i < Objects.Count; i++)
            {
                MapObject ob = Objects[i];
                if (ob.Light > 0 && (!ob.Dead || ob == MapObject.User || ob.Race == ObjectType.Spell))
                {

                    light = ob.Light;
                    int LightRange = light % 15;
                    //if (LightRange >= DXManager.Lights.Count)
                    //    LightRange = DXManager.Lights.Count - 1;

                    p = ob.DrawLocation;

                    Color lightColour = ob.LightColour;

                    if (ob.Race == ObjectType.Player)
                    {
                        switch (light / 15)
                        {
                            case 0://no light source
                                lightColour = Color.FromArgb(255, 60, 60, 60);
                                break;
                            case 1:
                                lightColour = Color.FromArgb(255, 120, 120, 120);
                                break;
                            case 2://Candle
                                lightColour = Color.FromArgb(255, 180, 180, 180);
                                break;
                            case 3://Torch
                                lightColour = Color.FromArgb(255, 240, 240, 240);
                                break;
                            default://Peddler Torch
                                lightColour = Color.FromArgb(255, 255, 255, 255);
                                break;
                        }
                    }
                    else if (ob.Race == ObjectType.Merchant)
                    {
                        lightColour = Color.FromArgb(255, 120, 120, 120);
                    }

                    //if (DXManager.Lights[LightRange] != null && !DXManager.Lights[LightRange].Disposed)
                    //{
                    //    p.Offset(-(DXManager.LightSizes[LightRange].X / 2) - (CellWidth / 2), -(DXManager.LightSizes[LightRange].Y / 2) - (CellHeight / 2) - 5);
                    //    DXManager.Sprite.Draw(DXManager.Lights[LightRange], null, Vector3.Zero, new Vector3((float)p.X, (float)p.Y, 0.0F), lightColour);
                    //}

                }
                #region Object Effect Lights
                if (!Settings.Effect) continue;
                for (int e = 0; e < ob.Effects.Count; e++)
                {
                    Effect effect = ob.Effects[e];
                    if (!effect.Blend || CMain.Time < effect.Start || (!(effect is Missile) && effect.Light < ob.Light)) continue;

                    light = effect.Light;

                    p = effect.DrawLocation;

                    //if (DXManager.Lights[light] != null && !DXManager.Lights[light].Disposed)
                    //{
                    //    p.Offset(-(DXManager.LightSizes[light].X / 2) - (CellWidth / 2), -(DXManager.LightSizes[light].Y / 2) - (CellHeight / 2) - 5);
                    //    DXManager.Sprite.Draw(DXManager.Lights[light], null, Vector3.Zero, new Vector3((float)p.X, (float)p.Y, 0.0F), effect.LightColour);
                    //}

                }
                #endregion
            }
            #endregion

            #region Map Effect Lights
            if (Settings.Effect)
            {
                for (int e = 0; e < Effects.Count; e++)
                {
                    Effect effect = Effects[e];
                    if (!effect.Blend || CMain.Time < effect.Start) continue;

                    light = effect.Light;
                    if (light == 0) continue;

                    p = effect.DrawLocation;

                    //if (DXManager.Lights[light] != null && !DXManager.Lights[light].Disposed)
                    //{
                    //    p.Offset(-(DXManager.LightSizes[light].X / 2) - (CellWidth / 2), -(DXManager.LightSizes[light].Y / 2) - (CellHeight / 2) - 5);
                    //    DXManager.Sprite.Draw(DXManager.Lights[light], null, Vector3.Zero, new Vector3((float)p.X, (float)p.Y, 0.0F), Color.White);
                    //}
                }
            }
            #endregion

            #region Map Lights
            for (int y = MapObject.User.Movement.Y - ViewRangeY - 24; y <= MapObject.User.Movement.Y + ViewRangeY + 24; y++)
            {
                if (y < 0) continue;
                if (y >= Height) break;
                for (int x = MapObject.User.Movement.X - ViewRangeX - 24; x < MapObject.User.Movement.X + ViewRangeX + 24; x++)
                {
                    if (x < 0) continue;
                    if (x >= Width) break;
                    if (M2CellInfo[x, y] == null) continue;
                    int imageIndex = (M2CellInfo[x, y].FrontImage & 0x7FFF) - 1;
                    if (M2CellInfo[x, y].Light <= 0 || M2CellInfo[x, y].Light >= 10) continue;
                    if (M2CellInfo[x, y].Light == 0) continue;

                    Color lightIntensity;

                    light = (M2CellInfo[x, y].Light % 10) * 3;

                    switch (M2CellInfo[x, y].Light / 10)
                    {
                        case 1:
                            lightIntensity = Color.FromArgb(255, 255, 255, 255);
                            break;
                        case 2:
                            lightIntensity = Color.FromArgb(255, 120, 180, 255);
                            break;
                        case 3:
                            lightIntensity = Color.FromArgb(255, 255, 180, 120);
                            break;
                        case 4:
                            lightIntensity = Color.FromArgb(255, 22, 160, 5);
                            break;
                        default:
                            lightIntensity = Color.FromArgb(255, 255, 255, 255);
                            break;
                    }

                    int fileIndex = M2CellInfo[x, y].FrontIndex;

                    p = new Point(
                        (x + OffSetX - MapObject.User.Movement.X) * CellWidth + MapObject.User.OffSetMove.X,
                        (y + OffSetY - MapObject.User.Movement.Y) * CellHeight + MapObject.User.OffSetMove.Y + 32);


                    if (M2CellInfo[x, y].FrontAnimationFrame > 0)
                        p.Offset(Libraries.MapLibs[fileIndex].GetOffSet(imageIndex));

                    //if (light >= DXManager.Lights.Count)
                    //    light = DXManager.Lights.Count - 1;

                    //if (DXManager.Lights[light] != null && !DXManager.Lights[light].Disposed)
                    //{
                    //    p.Offset(-(DXManager.LightSizes[light].X / 2) - (CellWidth / 2) + 10, -(DXManager.LightSizes[light].Y / 2) - (CellHeight / 2) - 5);
                    //    DXManager.Sprite.Draw(DXManager.Lights[light], null, Vector3.Zero, new Vector3((float)p.X, (float)p.Y, 0.0F), Color.White);
                    //}
                }
            }
            #endregion

            //DXManager.SetBlend(false);
            //DXManager.SetSurface(oldSurface);

            //DXManager.Device.SetRenderState(SlimDX.Direct3D9.RenderState.SourceBlend, Blend.DestinationColor);
            //DXManager.Device.SetRenderState(SlimDX.Direct3D9.RenderState.DestinationBlend, Blend.BothInverseSourceAlpha);

            //DXManager.Sprite.Draw(DXManager.LightTexture, new Rectangle(0, 0, Settings.ScreenWidth, Settings.ScreenHeight), Vector3.Zero, Vector3.Zero, Color.White);
            //DXManager.Sprite.End();
            //DXManager.Sprite.Begin(SpriteFlags.AlphaBlend);
        }

        public override void Event()
        {
            if (!CMain.Main.IsMouseInsideGameWindow())
                return;

            System.Drawing.Point screenPointer = CMain.currentMouseState.Position.ToDrawPoint();

            // 移动端：当指针落在移动 HUD（小地图/快捷栏/操作面板等）区域时，阻断地图点击，
            // 避免部分 NotControl 覆盖层（如小地图）不捕获 MouseControl 导致“点 HUD 触发地图点击”。
            if (Environment.OSVersion.Platform != PlatformID.Win32NT &&
                MouseControl == null &&
                GameScene.Scene != null)
            {
                var hudPos = new Microsoft.Xna.Framework.Vector2(screenPointer.X, screenPointer.Y);
                if (GameScene.Scene.IsPointOverMobileHud(hudPos))
                {
                    MouseLocation = ToMapPointer(screenPointer);
                    return;
                }
            }

            MouseLocation = ToMapPointer(screenPointer);

            if (MouseControl != null && MouseControl != this)
                return;

            if (CMain.currentMouseState.LeftButton == ButtonState.Pressed
                && CMain.previousMouseState.LeftButton == ButtonState.Released)
            {
                OnMouseClick(EventArgs.Empty);
            }

            if (CMain.currentMouseState.LeftButton == ButtonState.Pressed)
            {
                MouseControl = this;
                OnMouseDown(EventArgs.Empty);
            }

            if (CMain.currentMouseState.RightButton == ButtonState.Pressed)
            {
                MouseControl = this;
                OnMouseDown(EventArgs.Empty);
            }

            if (CMain.currentMouseState.LeftButton == ButtonState.Released && CMain.currentMouseState.RightButton == ButtonState.Released)
            {
                if (MouseControl == this)
                    MouseControl = null;
            }
        }

        public override void OnMouseClick(EventArgs e)
        {
            //if (!(e is MouseEventArgs me)) return;

            if (AwakeningAction == true) return;

            var mouseState = CMain.currentMouseState;

            // 触摸模拟鼠标时，“点击”通常发生在手指抬起的那一帧；此时 MouseObject 可能仍是上一帧的结果，
            // 这里主动刷新一次命中，避免点击 NPC/玩家不生效。
            UpdateMouseObjectUnderPointer();

            if (_pendingMagicLocation != null && mouseState.LeftButton == ButtonState.Pressed)
            {
                // 选点模式下，点击地图只用于选点确认（在 OnMouseDown 中处理），不触发 NPC/Inspect 等交互。
                return;
            }

            if (mouseState.LeftButton == ButtonState.Pressed)
            {
                AutoRun = false;
                if (MapObject.MouseObject == null) return;
                NPCObject npc = MapObject.MouseObject as NPCObject;
                if (npc != null)
                {
                    if (Environment.OSVersion.Platform != PlatformID.Win32NT)
                        GameScene.Scene?.BeginNpcConversation(npc.ObjectID, npc.Name);

                    //if (npc.ObjectID == GameScene.NPCID &&
                    //    (CMain.Time <= GameScene.NPCTime || GameScene.Scene.NPCDialog.Visible))
                    //{
                    //    return;
                    //}

                    //GameScene.Scene.NPCDialog.Hide();

                    GameScene.NPCTime = CMain.Time + 5000;
                    GameScene.NPCID = npc.ObjectID;
                    Network.Enqueue(new C.CallNPC { ObjectID = npc.ObjectID, Key = "[@Main]" });
                }

                // 移动端：点击地面物品，自动走过去并拾取
                if (Environment.OSVersion.Platform != PlatformID.Win32NT && MapObject.MouseObject is ItemObject item)
                {
                    try
                    {
                        CMain.SaveLog($"MobilePickupTap: click name={item.Name} loc={item.CurrentLocation.X},{item.CurrentLocation.Y} id={item.ObjectID}");
                    }
                    catch
                    {
                    }

                    try
                    {
                        SetMobileTapPickupTarget(item);
                    }
                    catch
                    {
                    }
                }
            }

            if (mouseState.RightButton == ButtonState.Pressed)
            {
                AutoRun = false;
                if (MapObject.MouseObject == null) return;
                PlayerObject player = MapObject.MouseObject as PlayerObject;
                //if (player == null || player == User || !CMain.Ctrl) return;
                //if (CMain.Time <= GameScene.InspectTime && player.ObjectID == InspectDialog.InspectID) return;

                GameScene.InspectTime = CMain.Time + 500;
                //InspectDialog.InspectID = player.ObjectID;
                Network.Enqueue(new C.Inspect { ObjectID = player.ObjectID });
            }

            if (mouseState.MiddleButton == ButtonState.Pressed)
            {
                AutoRun = !AutoRun;
            }
        }

        public override void OnMouseDown(EventArgs e)
        {
            var mouseState = CMain.currentMouseState;
            MapButtons = mouseState;
            //GameScene.CanRun = false;

            if (AwakeningAction == true) return;

            if (_pendingMagicLocation != null)
            {
                if (mouseState.RightButton == ButtonState.Pressed)
                {
                    CancelMagicLocationSelection();
                    return;
                }

                if (mouseState.LeftButton == ButtonState.Pressed)
                {
                    ClientMagic magic = _pendingMagicLocation;
                    _pendingMagicLocation = null;

                    AutoRun = false;

                    User.NextMagic = magic;
                    User.NextMagicDirection = MouseDirection();
                    User.NextMagicLocation = MapLocation;

                    MapObject clicked = MapObject.MouseObject;
                    if (clicked != null && clicked.Dead)
                        clicked = null;

                    if (clicked != null && (clicked is ItemObject || clicked is NPCObject))
                        clicked = null;

                    if (clicked is MonsterObject monster && (monster.AI == 64 || monster.AI == 70))
                        clicked = null;

                    User.NextMagicObject = clicked;
                    return;
                }
            }

            if (mouseState.LeftButton != ButtonState.Pressed) return;

            //if (GameScene.SelectedCell != null)
            //{
            //    if (GameScene.SelectedCell.GridType != MirGridType.Inventory)
            //    {
            //        GameScene.SelectedCell = null;
            //        return;
            //    }

            //    MirItemCell cell = GameScene.SelectedCell;
            //    if (cell.Item.Info.Bind.HasFlag(BindMode.DontDrop))
            //    {
            //        MirMessageBox messageBox = new MirMessageBox(string.Format("你不能丢弃{0}", cell.Item.FriendlyName), MirMessageBoxButtons.OK);
            //        messageBox.Show();
            //        GameScene.SelectedCell = null;
            //        return;
            //    }
            //    if (cell.Item.Count == 1)
            //    {
            //        MirMessageBox messageBox = new MirMessageBox(string.Format(GameLanguage.DropTip, cell.Item.FriendlyName), MirMessageBoxButtons.YesNo);

            //        messageBox.YesButton.Click += (o, a) =>
            //        {
            //            Network.Enqueue(new C.DropItem { UniqueID = cell.Item.UniqueID, Count = 1 });

            //            cell.Locked = true;
            //        };
            //        messageBox.Show();
            //    }
            //    else
            //    {
            //        MirAmountBox amountBox = new MirAmountBox(GameLanguage.DropAmount, cell.Item.Info.Image, cell.Item.Count);

            //        amountBox.OKButton.Click += (o, a) =>
            //        {
            //            if (amountBox.Amount <= 0) return;
            //            Network.Enqueue(new C.DropItem
            //            {
            //                UniqueID = cell.Item.UniqueID,
            //                Count = (ushort)amountBox.Amount
            //            });

            //            cell.Locked = true;
            //        };

            //        amountBox.Show();
            //    }
            //    GameScene.SelectedCell = null;

            //    return;
            //}

            //if (GameScene.PickedUpGold)
            //{
            //    MirAmountBox amountBox = new MirAmountBox(GameLanguage.DropAmount, 116, GameScene.Gold);

            //    amountBox.OKButton.Click += (o, a) =>
            //    {
            //        if (amountBox.Amount > 0)
            //        {
            //            Network.Enqueue(new C.DropGold { Amount = amountBox.Amount });
            //        }
            //    };

            //    amountBox.Show();
            //    GameScene.PickedUpGold = false;
            //}

            if (MapObject.MouseObject != null && !MapObject.MouseObject.Dead && !(MapObject.MouseObject is ItemObject) &&
                !(MapObject.MouseObject is NPCObject) && !(MapObject.MouseObject is MonsterObject && MapObject.MouseObject.AI == 64)
                 && !(MapObject.MouseObject is MonsterObject && MapObject.MouseObject.AI == 70))
            {
                MapObject.TargetObject = MapObject.MouseObject;
                if (MapObject.MouseObject is MonsterObject && MapObject.MouseObject.AI != 6)
                    MapObject.MagicObject = MapObject.TargetObject;
            }
            else
                MapObject.TargetObject = null;

            if (Environment.OSVersion.Platform != PlatformID.Win32NT)
            {
                try
                {
                    if (MapObject.MouseObject is MonsterObject monster && !monster.Dead &&
                        monster.AI != 64 && monster.AI != 70)
                    {
                        _mobileTapApproachTargetId = monster.ObjectID;
                        _mobileTapMoveDestination = null;
                        _mobileTapPickupTargetId = null;
                        _mobileTapPickupTargetLocation = null;
                        _mobileTapPickupStopAtTick = 0;
                        _mobileTapPickupSendCount = 0;
                        ResetMobileTapPath();
                    }
                    else if (MapObject.MouseObject is ItemObject)
                    {
                        // 点击地面物品：由 OnMouseClick 设置拾取目标；这里不清理，避免同帧被取消。
                    }
                    else if (MapObject.MouseObject == null)
                    {
                        _mobileTapApproachTargetId = null;
                        // 移动端：主场景不支持“点地面移动”，避免与双摇杆冲突。
                        // 但如果已经通过【大地图】设置了自动寻路目的地，则不要在这里清空，
                        // 否则关闭大地图（或 UI 点击透传到地图）会中断自动寻路。
                        bool keepBigMapTapMove = _mobileTapMoveDestination.HasValue &&
                                                 !_mobileTapPickupTargetId.HasValue &&
                                                 !_mobileTapPickupTargetLocation.HasValue;

                        if (!keepBigMapTapMove)
                            _mobileTapMoveDestination = null;

                        _mobileTapPickupTargetId = null;
                        _mobileTapPickupTargetLocation = null;
                        _mobileTapPickupStopAtTick = 0;
                        _mobileTapPickupSendCount = 0;

                        if (!keepBigMapTapMove)
                            ResetMobileTapPath();
                    }
                    else
                    {
                        // 点击 NPC/道具/玩家时不触发自动移动，避免误操作。
                        _mobileTapMoveDestination = null;
                        _mobileTapApproachTargetId = null;
                        _mobileTapPickupTargetId = null;
                        _mobileTapPickupTargetLocation = null;
                        _mobileTapPickupStopAtTick = 0;
                        _mobileTapPickupSendCount = 0;
                        ResetMobileTapPath();
                    }
                }
                catch
                {
                    _mobileTapMoveDestination = null;
                    _mobileTapApproachTargetId = null;
                    _mobileTapPickupTargetId = null;
                    _mobileTapPickupTargetLocation = null;
                    _mobileTapPickupStopAtTick = 0;
                    _mobileTapPickupSendCount = 0;
                    ResetMobileTapPath();
                }
            }
        }

        private void CheckInput()
        {
            if (AwakeningAction == true) return;

            //if ((MouseControl == this) && (MapButtons != MouseButtons.None)) AutoHit = false;//mouse actions stop mining even when frozen!
            if (!CanRideAttack()) AutoHit = false;

            if (CMain.Time < InputDelay || User.Poison.HasFlag(PoisonType.Paralysis) || User.Poison.HasFlag(PoisonType.LRParalysis) || User.Poison.HasFlag(PoisonType.Frozen) || User.Fishing) return;

            if (User.NextMagic != null && !User.RidingMount)
            {
                UseMagic(User.NextMagic);
                return;
            }

            if (CMain.Time < User.BlizzardStopTime || CMain.Time < User.ReincarnationStopTime) return;

            if (CMain.TryGetJoystickDirection(out MirDirection joystickDirection, out bool preferRun))
            {
                _mobileTapMoveDestination = null;
                _mobileTapApproachTargetId = null;
                _mobileTapPickupTargetId = null;
                _mobileTapPickupTargetLocation = null;
                _mobileTapPickupStopAtTick = 0;
                _mobileTapPickupSendCount = 0;
                ResetMobileTapPath();

                GameScene.CanRun = User.FastRun ? true : GameScene.CanRun;

                // 注意：摇杆“想跑”不等于服务端允许跑。这里必须以服务端同步的 CanRun 为准，
                // 否则会出现客户端先跑出去，随后被服务端位置校正“闪回原地”的现象。
                bool allowRun = GameScene.CanRun;

                if (preferRun && allowRun && CanRun(joystickDirection) &&
                    CMain.Time > GameScene.NextRunTime && User.HP >= 10 && (!User.Sneaking || (User.Sneaking && User.Sprint)))
                {
                    int distance = User.RidingMount || User.Sprint && !User.Sneaking ? 3 : 2;
                    bool fail = false;

                    for (int i = 0; i <= distance; i++)
                    {
                        if (!CheckDoorOpen(Functions.PointMove(User.CurrentLocation, joystickDirection, i)))
                            fail = true;
                    }

                    if (!fail)
                    {
                        User.QueuedAction = new QueuedAction
                        {
                            Action = MirAction.Running,
                            Direction = joystickDirection,
                            Location = Functions.PointMove(User.CurrentLocation, joystickDirection, distance)
                        };
                        return;
                    }
                }

                if (CanWalk(joystickDirection) && CheckDoorOpen(Functions.PointMove(User.CurrentLocation, joystickDirection, 1)))
                {
                    if (User.RidingMount)
                    {
                        int distance = User.RidingMount || User.Sprint && !User.Sneaking ? 3 : 2;
                        User.QueuedAction = new QueuedAction
                        {
                            Action = MirAction.Running,
                            Direction = joystickDirection,
                            Location = Functions.PointMove(User.CurrentLocation, joystickDirection, distance)
                        };
                    }
                    else
                    {
                        User.QueuedAction = new QueuedAction
                        {
                            Action = MirAction.Walking,
                            Direction = joystickDirection,
                            Location = Functions.PointMove(User.CurrentLocation, joystickDirection, 1)
                        };
                    }
                    return;
                }

                if (joystickDirection != User.Direction)
                {
                    User.QueuedAction = new QueuedAction
                    {
                        Action = MirAction.Standing,
                        Direction = joystickDirection,
                        Location = User.CurrentLocation
                    };
                    return;
                }
            }

            if (Environment.OSVersion.Platform != PlatformID.Win32NT)
            {
                if (TryProcessMobileTapMove())
                    return;
            }

            if (MapObject.TargetObject != null && !MapObject.TargetObject.Dead)
            {
                bool allowAttack = false;
                try
                {
                    // 移动端：没有 Shift 键，且不少服务端会把怪物名称带“(等级)”后缀，导致 Name.EndsWith(")") 时无法自动普攻。
                    // 这里对移动端放宽条件：只要 TargetObject 是怪物就允许自动普攻。
                    if (Environment.OSVersion.Platform != PlatformID.Win32NT)
                    {
                        allowAttack = MapObject.TargetObject is MonsterObject;
                    }
                    else
                    {
                        allowAttack =
                            (((MapObject.TargetObject.Name.EndsWith(")") || MapObject.TargetObject is PlayerObject) && CMain.Shift) ||
                             (!MapObject.TargetObject.Name.EndsWith(")") && MapObject.TargetObject is MonsterObject));
                    }
                }
                catch
                {
                    allowAttack = false;
                }

                if (allowAttack)
                {

                    GameScene.LogTime = CMain.Time + Globals.LogDelay;

                    bool isArcher = false;
                    int desiredRange = 1;
                    try
                    {
                        isArcher = User.Class == MirClass.Archer && User.HasClassWeapon && !User.RidingMount && !User.Fishing;
                        if (isArcher)
                            desiredRange = Globals.MaxAttackRange;
                    }
                    catch
                    {
                        isArcher = false;
                        desiredRange = 1;
                    }

                    bool inRange = false;
                    try { inRange = Functions.InRange(MapObject.TargetObject.CurrentLocation, User.CurrentLocation, desiredRange); } catch { inRange = false; }

                    if (inRange)
                    {
                        if (isArcher)
                        {
                            if (CMain.Time > GameScene.AttackTime)
                            {
                                User.QueuedAction = new QueuedAction { Action = MirAction.AttackRange1, Direction = Functions.DirectionFromPoint(User.CurrentLocation, MapObject.TargetObject.CurrentLocation), Location = User.CurrentLocation, Params = new List<object>() };
                                User.QueuedAction.Params.Add(MapObject.TargetObject != null ? MapObject.TargetObject.ObjectID : (uint)0);
                                User.QueuedAction.Params.Add(MapObject.TargetObject.CurrentLocation);

                                // MapObject.TargetObject = null; //stop constant attack when close up
                            }
                        }
                        else
                        {
                            if (CMain.Time > GameScene.AttackTime && CanRideAttack())
                            {
                                User.QueuedAction = new QueuedAction { Action = MirAction.Attack1, Direction = Functions.DirectionFromPoint(User.CurrentLocation, MapObject.TargetObject.CurrentLocation), Location = User.CurrentLocation };
                                return;
                            }
                        }
                    }
                    else
                    {
                        // 移动端：点击怪物后自动接近（达到可攻击距离后自动连续普攻）
                        if (Environment.OSVersion.Platform != PlatformID.Win32NT &&
                            MapObject.TargetObject is MonsterObject monsterTarget &&
                            monsterTarget != null && !monsterTarget.Dead)
                        {
                            try
                            {
                                if (!_mobileTapApproachTargetId.HasValue || _mobileTapApproachTargetId.Value != monsterTarget.ObjectID)
                                {
                                    SetMobileTapApproachTarget(monsterTarget.ObjectID);
                                }
                            }
                            catch
                            {
                            }

                            if (TryProcessMobileTapMove())
                                return;
                        }

                        // PC：远程职业超出射程时提示
                        if (Environment.OSVersion.Platform == PlatformID.Win32NT && isArcher)
                        {
                            if (CMain.Time >= OutputDelay)
                            {
                                OutputDelay = CMain.Time + 1000;
                                GameScene.Scene.OutputMessage("目标太远了.");
                            }
                        }
                    }
                }
            }
            if (AutoHit && !User.RidingMount)
            {
                if (CMain.Time > GameScene.AttackTime)
                {
                    User.QueuedAction = new QueuedAction { Action = MirAction.Mine, Direction = User.Direction, Location = User.CurrentLocation };
                    return;
                }
            }


            MirDirection direction;
            if (MouseControl == this)
            {
                direction = MouseDirection();
                if (AutoRun)
                {
                    if (GameScene.CanRun && CanRun(direction) && CMain.Time > GameScene.NextRunTime && User.HP >= 10 && (!User.Sneaking || (User.Sneaking && User.Sprint))) //slow remove
                    {
                        int distance = User.RidingMount || User.Sprint && !User.Sneaking ? 3 : 2;
                        bool fail = false;
                        for (int i = 1; i <= distance; i++)
                        {
                            if (!CheckDoorOpen(Functions.PointMove(User.CurrentLocation, direction, i)))
                                fail = true;
                        }
                        if (!fail)
                        {
                            User.QueuedAction = new QueuedAction { Action = MirAction.Running, Direction = direction, Location = Functions.PointMove(User.CurrentLocation, direction, distance) };
                            return;
                        }
                    }
                    if ((CanWalk(direction)) && (CheckDoorOpen(Functions.PointMove(User.CurrentLocation, direction, 1))))
                    {
                        if (User.RidingMount)
                        {
                            int distance = User.RidingMount || User.Sprint && !User.Sneaking ? 3 : 2;
                            User.QueuedAction = new QueuedAction { Action = MirAction.Running, Direction = direction, Location = Functions.PointMove(User.CurrentLocation, direction, distance) };
                        }
                        else
                            User.QueuedAction = new QueuedAction { Action = MirAction.Walking, Direction = direction, Location = Functions.PointMove(User.CurrentLocation, direction, 1) };

                        return;
                    }
                    if (direction != User.Direction)
                    {
                        User.QueuedAction = new QueuedAction { Action = MirAction.Standing, Direction = direction, Location = User.CurrentLocation };
                        return;
                    }
                    return;
                }


                if (MapButtons.LeftButton == ButtonState.Pressed)
                {
                    if (MapObject.MouseObject is NPCObject || (MapObject.MouseObject is PlayerObject && MapObject.MouseObject != User))
                        if (MapObject.MouseObject is MonsterObject && MapObject.MouseObject.AI == 70)

                            if (CMain.Alt && !User.RidingMount)
                            {
                                User.QueuedAction = new QueuedAction { Action = MirAction.Harvest, Direction = direction, Location = User.CurrentLocation };
                                return;
                            }
                    if (CMain.Shift)
                    {
                        if (CMain.Time > GameScene.AttackTime && CanRideAttack()) //ArcherTest - shift click
                        {
                            MapObject target = null;
                            if (MapObject.MouseObject is MonsterObject || MapObject.MouseObject is PlayerObject) target = MapObject.MouseObject;

                            if (User.Class == MirClass.Archer && User.HasClassWeapon && !User.RidingMount)
                            {
                                if (target != null)
                                {
                                    if (!Functions.InRange(MapObject.MouseObject.CurrentLocation, User.CurrentLocation, Globals.MaxAttackRange))
                                    {
                                        if (CMain.Time >= OutputDelay)
                                        {
                                            OutputDelay = CMain.Time + 1000;
                                            GameScene.Scene.OutputMessage("目标太远了.");
                                        }
                                        return;
                                    }
                                }

                                User.QueuedAction = new QueuedAction { Action = MirAction.AttackRange1, Direction = MouseDirection(), Location = User.CurrentLocation, Params = new List<object>() };
                                User.QueuedAction.Params.Add(target != null ? target.ObjectID : (uint)0);
                                User.QueuedAction.Params.Add(Functions.PointMove(User.CurrentLocation, MouseDirection(), 9));
                                return;
                            }

                            //stops double slash from being used without empty hand or assassin weapon (otherwise bugs on second swing)
                            if (GameScene.DoubleSlash && (!User.HasClassWeapon && User.Weapon > -1)) return;

                            User.QueuedAction = new QueuedAction { Action = MirAction.Attack1, Direction = direction, Location = User.CurrentLocation };
                        }
                        return;
                    }

                    if (MapObject.MouseObject is MonsterObject && User.Class == MirClass.Archer && MapObject.TargetObject != null && !MapObject.TargetObject.Dead && User.HasClassWeapon && !User.RidingMount) //ArcherTest - range attack
                    {
                        if (Functions.InRange(MapObject.MouseObject.CurrentLocation, User.CurrentLocation, Globals.MaxAttackRange))
                        {
                            if (CMain.Time > GameScene.AttackTime)
                            {
                                User.QueuedAction = new QueuedAction { Action = MirAction.AttackRange1, Direction = direction, Location = User.CurrentLocation, Params = new List<object>() };
                                User.QueuedAction.Params.Add(MapObject.TargetObject.ObjectID);
                                User.QueuedAction.Params.Add(MapObject.TargetObject.CurrentLocation);
                            }
                        }
                        else
                        {
                            if (CMain.Time >= OutputDelay)
                            {
                                OutputDelay = CMain.Time + 1000;
                                GameScene.Scene.OutputMessage("目标太远了.");
                            }
                        }
                        return;
                    }

                    if (MapLocation == User.CurrentLocation)
                    {
                        if (CMain.Time > GameScene.PickUpTime)
                        {
                            GameScene.PickUpTime = CMain.Time + 200;
                            Network.Enqueue(new C.PickUp());
                        }
                        return;
                    }

                    //mine
                    if (!ValidPoint(Functions.PointMove(User.CurrentLocation, direction, 1)))
                    {
                        if ((MapObject.User.Equipment[(int)EquipmentSlot.Weapon] != null) && (MapObject.User.Equipment[(int)EquipmentSlot.Weapon].Info.CanMine))
                        {
                            if (direction != User.Direction)
                            {
                                User.QueuedAction = new QueuedAction { Action = MirAction.Standing, Direction = direction, Location = User.CurrentLocation };
                                return;
                            }
                            AutoHit = true;
                            return;
                        }
                    }
                    if ((CanWalk(direction)) && (CheckDoorOpen(Functions.PointMove(User.CurrentLocation, direction, 1))))
                    {

                        User.QueuedAction = new QueuedAction { Action = MirAction.Walking, Direction = direction, Location = Functions.PointMove(User.CurrentLocation, direction, 1) };
                        return;
                    }
                    if (direction != User.Direction)
                    {
                        User.QueuedAction = new QueuedAction { Action = MirAction.Standing, Direction = direction, Location = User.CurrentLocation };
                        return;
                    }

                    if (CanFish(direction))
                    {
                        User.FishingTime = CMain.Time;
                        Network.Enqueue(new C.FishingCast { CastOut = true });
                        return;
                    }
                }
                if (MapButtons.RightButton == ButtonState.Pressed)
                {
                    if (MapObject.MouseObject is PlayerObject && MapObject.MouseObject != User && CMain.Ctrl)

                        if (Functions.InRange(MapLocation, User.CurrentLocation, 2))
                        {
                            if (direction != User.Direction)
                            {
                                User.QueuedAction = new QueuedAction { Action = MirAction.Standing, Direction = direction, Location = User.CurrentLocation };
                            }
                            return;
                        }

                    GameScene.CanRun = User.FastRun ? true : GameScene.CanRun;

                    if (GameScene.CanRun && CanRun(direction) && CMain.Time > GameScene.NextRunTime && User.HP >= 10 && (!User.Sneaking || (User.Sneaking && User.Sprint))) //slow removed
                    {
                        int distance = User.RidingMount || User.Sprint && !User.Sneaking ? 3 : 2;
                        bool fail = false;
                        for (int i = 0; i <= distance; i++)
                        {
                            if (!CheckDoorOpen(Functions.PointMove(User.CurrentLocation, direction, i)))
                                fail = true;
                        }
                        if (!fail)
                        {
                            User.QueuedAction = new QueuedAction { Action = MirAction.Running, Direction = direction, Location = Functions.PointMove(User.CurrentLocation, direction, User.RidingMount || (User.Sprint && !User.Sneaking) ? 3 : 2) };
                            return;
                        }
                    }
                    if ((CanWalk(direction)) && (CheckDoorOpen(Functions.PointMove(User.CurrentLocation, direction, 1))))
                    {
                        if (User.RidingMount)
                        {
                            int distance = User.RidingMount || User.Sprint && !User.Sneaking ? 3 : 2;
                            User.QueuedAction = new QueuedAction { Action = MirAction.Running, Direction = direction, Location = Functions.PointMove(User.CurrentLocation, direction, distance) };
                        }
                        else
                            User.QueuedAction = new QueuedAction { Action = MirAction.Walking, Direction = direction, Location = Functions.PointMove(User.CurrentLocation, direction, 1) };

                        return;
                    }
                    if (direction != User.Direction)
                    {
                        User.QueuedAction = new QueuedAction { Action = MirAction.Standing, Direction = direction, Location = User.CurrentLocation };
                        return;
                    }
                }
            }

            if (MapObject.TargetObject == null || MapObject.TargetObject.Dead) return;
            //if (((!MapObject.TargetObject.Name.EndsWith(")") && !(MapObject.TargetObject is PlayerObject)) || !CMain.Shift) &&
            //    (MapObject.TargetObject.Name.EndsWith(")") || !(MapObject.TargetObject is MonsterObject))) return;
            if (Functions.InRange(MapObject.TargetObject.CurrentLocation, User.CurrentLocation, 1)) return;
            if (User.Class == MirClass.Archer && User.HasClassWeapon && (MapObject.TargetObject is MonsterObject || MapObject.TargetObject is PlayerObject)) return; //ArcherTest - stop walking
            direction = Functions.DirectionFromPoint(User.CurrentLocation, MapObject.TargetObject.CurrentLocation);

            if (!CanWalk(direction)) return;

            User.QueuedAction = new QueuedAction { Action = MirAction.Walking, Direction = direction, Location = Functions.PointMove(User.CurrentLocation, direction, 1) };
        }

        private bool TryProcessMobileTapMove()
        {
            try
            {
                if (User == null)
                {
                    _mobileTapMoveDestination = null;
                    _mobileTapApproachTargetId = null;
                    _mobileTapPickupTargetId = null;
                    _mobileTapPickupTargetLocation = null;
                    _mobileTapPickupStopAtTick = 0;
                    _mobileTapPickupSendCount = 0;
                    ResetMobileTapPath();
                    return false;
                }

                // 点击地面物品：走过去并在脚下尝试拾取（多次重试，避免偶发丢包/延迟导致“点了不捡”）。
                if (_mobileTapPickupTargetLocation.HasValue)
                {
                    long now = CMain.Time;
                    if ((_mobileTapPickupStopAtTick > 0 && now > _mobileTapPickupStopAtTick) || _mobileTapPickupSendCount >= 10)
                    {
                        _mobileTapPickupTargetId = null;
                        _mobileTapPickupTargetLocation = null;
                        _mobileTapPickupStopAtTick = 0;
                        _mobileTapPickupSendCount = 0;
                        _mobileTapMoveDestination = null;
                        ResetMobileTapPath();
                    }
                    else if (_mobileTapPickupTargetId.HasValue)
                    {
                        MapObject obj = TryFindObjectById(_mobileTapPickupTargetId.Value);
                        if (obj == null || obj is not ItemObject)
                        {
                            _mobileTapPickupTargetId = null;
                            _mobileTapPickupTargetLocation = null;
                            _mobileTapPickupStopAtTick = 0;
                            _mobileTapPickupSendCount = 0;
                            _mobileTapMoveDestination = null;
                            ResetMobileTapPath();
                        }
                    }
                }

                // 点击怪物：优先自动接近（达到可攻击距离后交由原有 TargetObject 逻辑处理攻击）。
                if (_mobileTapApproachTargetId.HasValue)
                {
                    MapObject target = TryFindObjectById(_mobileTapApproachTargetId.Value);
                    if (target == null || target.Dead)
                    {
                        _mobileTapApproachTargetId = null;
                        ResetMobileTapPath();
                    }
                    else
                    {
                        int desiredRange = 1;
                        if (User.Class == MirClass.Archer && User.HasClassWeapon && !User.RidingMount && !User.Fishing)
                            desiredRange = Globals.MaxAttackRange;

                        if (Functions.InRange(target.CurrentLocation, User.CurrentLocation, desiredRange))
                        {
                            // 已到达可攻击距离：清理寻路路径（避免大地图继续显示旧路线）。
                            ResetMobileTapPath();
                            return false;
                        }

                        // 为目标寻找一个可站立的“接近点”（必须是空格子）。
                        if (!TryResolveMobileTapApproachDestination(target.CurrentLocation, desiredRange, out Point approachDestination))
                        {
                            _mobileTapApproachTargetId = null;
                            ResetMobileTapPath();
                            return false;
                        }

                        // 目的地变化/首次进入：计算路径（不允许随意调整目的地，否则可能跑到攻击距离之外）。
                        if (!_mobileTapPathDestination.HasValue || _mobileTapPathDestination.Value != approachDestination || _mobileTapPathSteps == null)
                        {
                            TryComputeMobileTapPath(approachDestination, allowAdjustDestination: false);
                        }

                        if (!HasMobileTapPath)
                        {
                            // 没有路径：兜底使用“逐步逼近”，但不再响应点地面移动。
                            bool queued = TryQueueTapMoveStep(approachDestination);
                            if (!queued)
                            {
                                _mobileTapApproachTargetId = null;
                                ResetMobileTapPath();
                            }
                            return queued;
                        }

                        // 跳过已到达的步点
                        while (_mobileTapPathSteps != null && _mobileTapPathStepIndex < _mobileTapPathSteps.Count &&
                               _mobileTapPathSteps[_mobileTapPathStepIndex] == User.CurrentLocation)
                        {
                            _mobileTapPathStepIndex++;
                        }

                        if (_mobileTapPathSteps == null || _mobileTapPathStepIndex >= _mobileTapPathSteps.Count)
                        {
                            ResetMobileTapPath();
                            return false;
                        }

                        Point nextStep = _mobileTapPathSteps[_mobileTapPathStepIndex];

                        // 若当前位置偏离路径（可能被服务端纠正/推开），重算一次路径对齐。
                        int ndx = Math.Abs(nextStep.X - User.CurrentLocation.X);
                        int ndy = Math.Abs(nextStep.Y - User.CurrentLocation.Y);
                        if (Math.Max(ndx, ndy) > 1)
                        {
                            TryComputeMobileTapPath(approachDestination, allowAdjustDestination: false);
                            return false;
                        }

                        bool stepQueued = TryQueueTapMoveStep(nextStep);
                        if (stepQueued)
                            return true;

                        // 可能是门未开：保持路径，稍后重试。
                        try
                        {
                            MirDirection dir = Functions.DirectionFromPoint(User.CurrentLocation, nextStep);
                            Point stepCell = Functions.PointMove(User.CurrentLocation, dir, 1);
                            if (M2CellInfo != null && stepCell.X >= 0 && stepCell.Y >= 0 && stepCell.X < Width && stepCell.Y < Height)
                            {
                                CellInfo cell = M2CellInfo[stepCell.X, stepCell.Y];
                                if (cell != null && cell.DoorIndex != 0)
                                {
                                    long now = CMain.Time;
                                    if (now - _mobileTapPathLastDoorWaitTick >= 250)
                                        _mobileTapPathLastDoorWaitTick = now;
                                    return false;
                                }
                            }
                        }
                        catch
                        {
                        }

                        // 被动态障碍阻挡：尝试重算绕行；多次失败则停止，避免原地死循环。
                        long tick = CMain.Time;
                        if (_mobileTapPathRecomputeAttempts < 3 && (tick - _mobileTapPathLastComputeTick) >= 250)
                        {
                            _mobileTapPathRecomputeAttempts++;
                            TryComputeMobileTapPath(approachDestination, allowAdjustDestination: false);
                            return false;
                        }

                        _mobileTapApproachTargetId = null;
                        ResetMobileTapPath();
                        return false;
                    }
                }

                // 点击地面：用于“大地图点击后自动寻路”（主场景点地面仍不设置 _mobileTapMoveDestination，因此不会与双摇杆冲突）。
                if (_mobileTapMoveDestination.HasValue)
                {
                    Point destination = _mobileTapMoveDestination.Value;

                    if (destination == User.CurrentLocation)
                    {
                        // 拾取：到达目标格后发一次 PickUp（可多次重试，直到物品消失或超时）。
                        if (_mobileTapPickupTargetLocation.HasValue && _mobileTapPickupTargetLocation.Value == destination)
                        {
                            if (CMain.Time > GameScene.PickUpTime)
                            {
                                GameScene.PickUpTime = CMain.Time + 200;
                                _mobileTapPickupSendCount++;
                                try
                                {
                                    CMain.SaveLog($"MobilePickupTap: arrived send PickUp at {destination.X},{destination.Y} cnt={_mobileTapPickupSendCount}");
                                }
                                catch
                                {
                                }
                                Network.Enqueue(new C.PickUp());
                            }

                            // 仍处于拾取流程：不清空 MoveDestination，让后续帧继续重试（直到物品消失/超时）。
                            ResetMobileTapPath();
                            return false;
                        }

                        _mobileTapMoveDestination = null;
                        ResetMobileTapPath();
                        return false;
                    }

                    if (!_mobileTapPathDestination.HasValue || _mobileTapPathDestination.Value != destination || _mobileTapPathSteps == null)
                    {
                        TryComputeMobileTapPath(destination, allowAdjustDestination: true);
                        if (_mobileTapPathDestination.HasValue)
                            _mobileTapMoveDestination = _mobileTapPathDestination.Value;
                        if (_mobileTapMoveDestination.HasValue)
                            destination = _mobileTapMoveDestination.Value;
                    }

                    if (!HasMobileTapPath)
                    {
                        bool queued = TryQueueTapMoveStep(destination);
                        if (!queued)
                        {
                            _mobileTapMoveDestination = null;
                            ResetMobileTapPath();
                        }
                        return queued;
                    }

                    // 跳过已到达的步点
                    while (_mobileTapPathSteps != null && _mobileTapPathStepIndex < _mobileTapPathSteps.Count &&
                           _mobileTapPathSteps[_mobileTapPathStepIndex] == User.CurrentLocation)
                    {
                        _mobileTapPathStepIndex++;
                    }

                    if (_mobileTapPathSteps == null || _mobileTapPathStepIndex >= _mobileTapPathSteps.Count)
                    {
                        _mobileTapMoveDestination = null;
                        ResetMobileTapPath();
                        return false;
                    }

                    Point nextStep = _mobileTapPathSteps[_mobileTapPathStepIndex];

                    // 若当前位置偏离路径（可能被服务端纠正/推开），重算一次路径对齐。
                    int ndx = Math.Abs(nextStep.X - User.CurrentLocation.X);
                    int ndy = Math.Abs(nextStep.Y - User.CurrentLocation.Y);
                    if (Math.Max(ndx, ndy) > 1)
                    {
                        long tick = CMain.Time;
                        if (_mobileTapPathRecomputeAttempts < 3 && (tick - _mobileTapPathLastComputeTick) >= 250)
                        {
                            _mobileTapPathRecomputeAttempts++;
                            TryComputeMobileTapPath(destination, allowAdjustDestination: true);
                            if (_mobileTapPathDestination.HasValue)
                                _mobileTapMoveDestination = _mobileTapPathDestination.Value;
                        }
                        return false;
                    }

                    bool stepQueued = TryQueueTapMoveStep(nextStep);
                    if (stepQueued)
                        return true;

                    // 可能是门未开：保持路径，稍后重试。
                    try
                    {
                        MirDirection dir = Functions.DirectionFromPoint(User.CurrentLocation, nextStep);
                        Point stepCell = Functions.PointMove(User.CurrentLocation, dir, 1);
                        if (M2CellInfo != null && stepCell.X >= 0 && stepCell.Y >= 0 && stepCell.X < Width && stepCell.Y < Height)
                        {
                            CellInfo cell = M2CellInfo[stepCell.X, stepCell.Y];
                            if (cell != null && cell.DoorIndex != 0)
                            {
                                long now = CMain.Time;
                                if (now - _mobileTapPathLastDoorWaitTick >= 250)
                                    _mobileTapPathLastDoorWaitTick = now;
                                return false;
                            }
                        }
                    }
                    catch
                    {
                    }

                    // 被动态障碍阻挡：尝试重算绕行；多次失败则停止，避免原地死循环。
                    long nowTick = CMain.Time;
                    if (_mobileTapPathRecomputeAttempts < 3 && (nowTick - _mobileTapPathLastComputeTick) >= 250)
                    {
                        _mobileTapPathRecomputeAttempts++;
                        TryComputeMobileTapPath(destination, allowAdjustDestination: true);
                        if (_mobileTapPathDestination.HasValue)
                            _mobileTapMoveDestination = _mobileTapPathDestination.Value;
                        return false;
                    }

                    _mobileTapMoveDestination = null;
                    ResetMobileTapPath();
                    return false;
                }
            }
            catch
            {
                _mobileTapMoveDestination = null;
                _mobileTapApproachTargetId = null;
                _mobileTapPickupTargetId = null;
                _mobileTapPickupTargetLocation = null;
                _mobileTapPickupStopAtTick = 0;
                _mobileTapPickupSendCount = 0;
                ResetMobileTapPath();
            }

            return false;
        }

        private bool TryResolveMobileTapApproachDestination(Point targetLocation, int desiredRange, out Point destination)
        {
            destination = targetLocation;

            if (User == null)
                return false;

            if (M2CellInfo == null || Width <= 0 || Height <= 0)
                return false;

            desiredRange = Math.Clamp(desiredRange, 1, 16);

            Point best = Point.Empty;
            int bestScore = int.MaxValue;
            bool found = false;

            // 近战(1格)：优先贴身；远程(>1格)：优先停在最大射程附近，避免无意义贴脸。
            int rStart = desiredRange <= 1 ? 1 : desiredRange;
            int rEnd = desiredRange <= 1 ? desiredRange : 1;
            int rStep = desiredRange <= 1 ? 1 : -1;

            for (int r = rStart; desiredRange <= 1 ? (r <= rEnd) : (r >= rEnd); r += rStep)
            {
                bool foundThisRing = false;

                for (int dy = -r; dy <= r; dy++)
                {
                    int y = targetLocation.Y + dy;
                    if (y < 0 || y >= Height)
                        continue;

                    for (int dx = -r; dx <= r; dx++)
                    {
                        int x = targetLocation.X + dx;
                        if (x < 0 || x >= Width)
                            continue;

                        if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != r)
                            continue;

                        Point p = new Point(x, y);
                        if (!EmptyCell(p))
                            continue;

                        // 评分：离玩家近 + 离目标近（r 越小越好）
                        int score = MobileHeuristicOctile(User.CurrentLocation, p) + r * 3;
                        if (!found || score < bestScore)
                        {
                            bestScore = score;
                            best = p;
                            found = true;
                            foundThisRing = true;
                        }
                    }
                }

                // 找到最近一环的可站点就不再扩大范围（避免远距离停在怪物边界之外）。
                if (foundThisRing)
                    break;
            }

            if (!found)
                return false;

            destination = best;
            return true;
        }

        private static MapObject TryFindObjectById(uint objectId)
        {
            if (objectId == 0)
                return null;

            List<MapObject> objects = Objects;
            if (objects == null)
                return null;

            for (int i = 0; i < objects.Count; i++)
            {
                MapObject obj = objects[i];
                if (obj != null && obj.ObjectID == objectId)
                    return obj;
            }

            return null;
        }

        private bool TryQueueTapMoveStep(Point destination)
        {
            if (User == null)
                return false;

            if (destination == User.CurrentLocation)
                return false;

            MirDirection direction = Functions.DirectionFromPoint(User.CurrentLocation, destination);

            if (User.RidingMount)
            {
                int desiredDistance = User.Sprint && !User.Sneaking ? 3 : 2;
                int dx = Math.Abs(destination.X - User.CurrentLocation.X);
                int dy = Math.Abs(destination.Y - User.CurrentLocation.Y);
                int chebyshev = Math.Max(dx, dy);
                int distance = Math.Clamp(desiredDistance, 1, Math.Max(1, chebyshev));

                bool fail = false;
                for (int i = 1; i <= distance; i++)
                {
                    if (!CheckDoorOpen(Functions.PointMove(User.CurrentLocation, direction, i)))
                    {
                        fail = true;
                        break;
                    }
                }

                if (!fail)
                {
                    User.QueuedAction = new QueuedAction
                    {
                        Action = MirAction.Running,
                        Direction = direction,
                        Location = Functions.PointMove(User.CurrentLocation, direction, distance)
                    };
                    return true;
                }
            }

            if (CanWalk(direction) && CheckDoorOpen(Functions.PointMove(User.CurrentLocation, direction, 1)))
            {
                User.QueuedAction = new QueuedAction
                {
                    Action = MirAction.Walking,
                    Direction = direction,
                    Location = Functions.PointMove(User.CurrentLocation, direction, 1)
                };
                return true;
            }

            if (direction != User.Direction)
            {
                User.QueuedAction = new QueuedAction
                {
                    Action = MirAction.Standing,
                    Direction = direction,
                    Location = User.CurrentLocation
                };
                return true;
            }

            return false;
        }

        private void UseMagic(ClientMagic magic)
        {
            if (CMain.Time < GameScene.SpellTime || User.Poison.HasFlag(PoisonType.Stun))
            {
                User.ClearMagic();
                return;
            }

            if ((CMain.Time <= magic.CastTime + magic.Delay) && magic.CastTime > 0)
            {
                if (CMain.Time >= OutputDelay)
                {
                    OutputDelay = CMain.Time + 1000;
                    GameScene.Scene.OutputMessage(string.Format("你不能释放技能{0}需要等待{1}秒.", magic.Name.ToString(), ((magic.CastTime + magic.Delay) - CMain.Time - 1) / 1000 + 1));
                }

                User.ClearMagic();
                return;
            }

            int cost = magic.Level * magic.LevelCost + magic.BaseCost;

            if (magic.Spell == Spell.Teleport || magic.Spell == Spell.Blink || magic.Spell == Spell.StormEscape)
            {
                if (GameScene.User.Stats[Stat.TeleportManaPenaltyPercent] > 0)
                {
                    cost += (cost * GameScene.User.Stats[Stat.TeleportManaPenaltyPercent]) / 100;
                }
            }

            if (GameScene.User.Stats[Stat.ManaPenaltyPercent] > 0)
            {
                cost += (cost * GameScene.User.Stats[Stat.ManaPenaltyPercent]) / 100;
            }

            if (cost > MapObject.User.MP)
            {
                if (CMain.Time >= OutputDelay)
                {
                    OutputDelay = CMain.Time + 1000;
                    GameScene.Scene.OutputMessage(GameLanguage.LowMana);
                }
                User.ClearMagic();
                return;
            }

            //bool isTargetSpell = true;

            MapObject target = null;

            //Targeting
            switch (magic.Spell)
            {
                case Spell.FireBall:
                case Spell.GreatFireBall:
                case Spell.ElectricShock:
                case Spell.Poisoning:
                case Spell.ThunderBolt:
                case Spell.FlameDisruptor:
                case Spell.SoulFireBall:
                case Spell.TurnUndead:
                case Spell.FrostCrunch:
                case Spell.Vampirism:
                case Spell.Revelation:
                case Spell.Entrapment:
                case Spell.Hallucination:
                case Spell.DarkBody:
                case Spell.FireBounce:
                case Spell.MeteorShower:
                    if (User.NextMagicObject != null)
                    {
                        if (!User.NextMagicObject.Dead && User.NextMagicObject.Race != ObjectType.Item && User.NextMagicObject.Race != ObjectType.Merchant)
                            target = User.NextMagicObject;
                    }

                    if (target == null) target = MapObject.MagicObject;

                    if (target != null && target.Race == ObjectType.Monster) MapObject.MagicObject = target;
                    break;
                case Spell.StraightShot:
                case Spell.DoubleShot:
                case Spell.ElementalShot:
                case Spell.DelayedExplosion:
                case Spell.BindingShot:
                case Spell.VampireShot:
                case Spell.PoisonShot:
                case Spell.CrippleShot:
                case Spell.NapalmShot:
                case Spell.SummonVampire:
                case Spell.SummonToad:
                case Spell.SummonSnakes:
                    if (!User.HasClassWeapon)
                    {
                        GameScene.Scene.OutputMessage("你必须佩戴弓才能完成此技能.");
                        User.ClearMagic();
                        return;
                    }
                    if (User.NextMagicObject != null)
                    {
                        if (!User.NextMagicObject.Dead && User.NextMagicObject.Race != ObjectType.Item && User.NextMagicObject.Race != ObjectType.Merchant)
                            target = User.NextMagicObject;
                    }

                    if (target == null) target = MapObject.MagicObject;

                    if (target != null && target.Race == ObjectType.Monster) MapObject.MagicObject = target;

                    //if(magic.Spell == Spell.ElementalShot)
                    //{
                    //    isTargetSpell = User.HasElements;
                    //}

                    //switch(magic.Spell)
                    //{
                    //    case Spell.SummonVampire:
                    //    case Spell.SummonToad:
                    //    case Spell.SummonSnakes:
                    //        isTargetSpell = false;
                    //        break;
                    //}

                    break;
                case Spell.Purification:
                case Spell.Healing:
                case Spell.UltimateEnhancer:
                case Spell.EnergyShield:
                case Spell.PetEnhancer:
                    if (User.NextMagicObject != null)
                    {
                        if (!User.NextMagicObject.Dead && User.NextMagicObject.Race != ObjectType.Item && User.NextMagicObject.Race != ObjectType.Merchant)
                            target = User.NextMagicObject;
                    }

                    if (target == null) target = User;
                    break;
                case Spell.FireBang:
                case Spell.MassHiding:
                case Spell.FireWall:
                case Spell.TrapHexagon:
                    if (User.NextMagicObject != null)
                    {
                        if (!User.NextMagicObject.Dead && User.NextMagicObject.Race != ObjectType.Item && User.NextMagicObject.Race != ObjectType.Merchant)
                            target = User.NextMagicObject;
                    }
                    break;
                case Spell.PoisonCloud:
                    if (User.NextMagicObject != null)
                    {
                        if (!User.NextMagicObject.Dead && User.NextMagicObject.Race != ObjectType.Item && User.NextMagicObject.Race != ObjectType.Merchant)
                            target = User.NextMagicObject;
                    }
                    break;
                case Spell.Blizzard:
                case Spell.MeteorStrike:
                    if (User.NextMagicObject != null)
                    {
                        if (!User.NextMagicObject.Dead && User.NextMagicObject.Race != ObjectType.Item && User.NextMagicObject.Race != ObjectType.Merchant)
                            target = User.NextMagicObject;
                    }
                    break;
                case Spell.Reincarnation:
                    if (User.NextMagicObject != null)
                    {
                        if (User.NextMagicObject.Dead && User.NextMagicObject.Race == ObjectType.Player)
                            target = User.NextMagicObject;
                    }
                    break;
                case Spell.Trap:
                    if (User.NextMagicObject != null)
                    {
                        if (!User.NextMagicObject.Dead && User.NextMagicObject.Race != ObjectType.Item && User.NextMagicObject.Race != ObjectType.Merchant)
                            target = User.NextMagicObject;
                    }
                    break;
                case Spell.FlashDash:
                    if (User.GetMagic(Spell.FlashDash).Level <= 1 && User.IsDashAttack() == false)
                    {
                        User.ClearMagic();
                        return;
                    }
                    //isTargetSpell = false;
                    break;
                default:
                    //isTargetSpell = false;
                    break;
            }

            MirDirection dir = (target == null || target == User) ? User.NextMagicDirection : Functions.DirectionFromPoint(User.CurrentLocation, target.CurrentLocation);

            Point location = target != null ? target.CurrentLocation : User.NextMagicLocation;

            if (magic.Spell == Spell.FlashDash)
                dir = User.Direction;

            if ((magic.Range != 0) && (!Functions.InRange(User.CurrentLocation, location, magic.Range)))
            {
                if (CMain.Time >= OutputDelay)
                {
                    OutputDelay = CMain.Time + 1000;
                    GameScene.Scene.OutputMessage("目标太远了.");
                }
                User.ClearMagic();
                return;
            }

            GameScene.LogTime = CMain.Time + Globals.LogDelay;

            User.QueuedAction = new QueuedAction { Action = MirAction.Spell, Direction = dir, Location = User.CurrentLocation, Params = new List<object>() };
            User.QueuedAction.Params.Add(magic.Spell);
            User.QueuedAction.Params.Add(target != null ? target.ObjectID : 0);
            User.QueuedAction.Params.Add(location);
            User.QueuedAction.Params.Add(magic.Level);
        }

        public static MirDirection MouseDirection(float ratio = 45F) //22.5 = 16
        {
            Point p = new Point(MouseLocation.X / CellWidth, MouseLocation.Y / CellHeight);
            if (Functions.InRange(new Point(OffSetX, OffSetY), p, 2))
                return Functions.DirectionFromPoint(new Point(OffSetX, OffSetY), p);

            PointF c = new PointF(OffSetX * CellWidth + CellWidth / 2F, OffSetY * CellHeight + CellHeight / 2F);
            PointF a = new PointF(c.X, 0);
            PointF b = MouseLocation;
            float bc = (float)Distance(c, b);
            float ac = bc;
            b.Y -= c.Y;
            c.Y += bc;
            b.Y += bc;
            float ab = (float)Distance(b, a);
            double x = (ac * ac + bc * bc - ab * ab) / (2 * ac * bc);
            double angle = Math.Acos(x);

            angle *= 180 / Math.PI;

            if (MouseLocation.X < c.X) angle = 360 - angle;
            angle += ratio / 2;
            if (angle > 360) angle -= 360;

            return (MirDirection)(angle / ratio);
        }

        public static int Direction16(Point source, Point destination)
        {
            PointF c = new PointF(source.X, source.Y);
            PointF a = new PointF(c.X, 0);
            PointF b = new PointF(destination.X, destination.Y);
            float bc = (float)Distance(c, b);
            float ac = bc;
            b.Y -= c.Y;
            c.Y += bc;
            b.Y += bc;
            float ab = (float)Distance(b, a);
            double x = (ac * ac + bc * bc - ab * ab) / (2 * ac * bc);
            double angle = Math.Acos(x);

            angle *= 180 / Math.PI;

            if (destination.X < c.X) angle = 360 - angle;
            angle += 11.25F;
            if (angle > 360) angle -= 360;

            return (int)(angle / 22.5F);
        }

        public static double Distance(PointF p1, PointF p2)
        {
            double x = p2.X - p1.X;
            double y = p2.Y - p1.Y;
            return Math.Sqrt(x * x + y * y);
        }

        private bool EmptyCell(Point p)
        {
            if (M2CellInfo == null || Width <= 0 || Height <= 0)
                return true;

            if (p.X < 0 || p.Y < 0 || p.X >= Width || p.Y >= Height)
                return false;

            CellInfo cell = M2CellInfo[p.X, p.Y];
            if (cell != null &&
                ((cell.BackImage & 0x20000000) != 0 || (cell.FrontImage & 0x8000) != 0)) // + (M2CellInfo[P.X, P.Y].FrontImage & 0x7FFF) != 0)
                return false;

            for (int i = 0; i < Objects.Count; i++)
            {
                MapObject ob = Objects[i];

                if (ob.CurrentLocation == p && ob.Blocking)
                    return false;
            }

            return true;
        }

        private bool CanWalk(MirDirection dir)
        {
            return EmptyCell(Functions.PointMove(User.CurrentLocation, dir, 1)) && !User.InTrapRock;
        }

        private bool CheckDoorOpen(Point p)
        {
            if (M2CellInfo == null || Width <= 0 || Height <= 0)
                return true;

            if (p.X < 0 || p.Y < 0 || p.X >= Width || p.Y >= Height)
                return false;

            CellInfo cell = M2CellInfo[p.X, p.Y];
            if (cell == null || cell.DoorIndex == 0) return true;

            Door DoorInfo = GetDoor(cell.DoorIndex);
            if (DoorInfo == null) return false;//if the door doesnt exist then it isnt even being shown on screen (and cant be open lol)
            if ((DoorInfo.DoorState == 0) || (DoorInfo.DoorState == DoorState.Closing))
            {
                Network.Enqueue(new C.Opendoor() { DoorIndex = DoorInfo.index });
                return false;
            }
            if ((DoorInfo.DoorState == DoorState.Open) && (DoorInfo.LastTick + 4000 > CMain.Time))
            {
                Network.Enqueue(new C.Opendoor() { DoorIndex = DoorInfo.index });
            }
            return true;
        }


        private bool CanRun(MirDirection dir)
        {
            if (User.InTrapRock) return false;
            if (User.CurrentBagWeight > User.Stats[Stat.BagWeight]) return false;
            if (User.CurrentWearWeight > User.Stats[Stat.BagWeight]) return false;
            if (CanWalk(dir) && EmptyCell(Functions.PointMove(User.CurrentLocation, dir, 2)))
            {
                if (User.RidingMount || User.Sprint && !User.Sneaking)
                {
                    return EmptyCell(Functions.PointMove(User.CurrentLocation, dir, 3));
                }

                return true;
            }

            return false;
        }

        private bool CanRideAttack()
        {
            if (GameScene.User.RidingMount)
            {
                UserItem item = GameScene.User.Equipment[(int)EquipmentSlot.Mount];
                if (item == null || item.Slots.Length < 4 || item.Slots[(int)MountSlot.Bells] == null) return false;
            }

            return true;
        }

        public bool CanFish(MirDirection dir)
        {
            if (!GameScene.User.HasFishingRod || GameScene.User.FishingTime + 1000 > CMain.Time) return false;
            if (GameScene.User.CurrentAction != MirAction.Standing) return false;
            if (GameScene.User.Direction != dir) return false;
            if (GameScene.User.TransformType >= 6 && GameScene.User.TransformType <= 9) return false;

            Point point = Functions.PointMove(User.CurrentLocation, dir, 3);

            if (M2CellInfo == null || Width <= 0 || Height <= 0)
                return false;

            if (point.X < 0 || point.Y < 0 || point.X >= Width || point.Y >= Height)
                return false;

            CellInfo cell = M2CellInfo[point.X, point.Y];
            if (cell == null || !cell.FishingCell) return false;

            return true;
        }

        public bool CanFly(Point target)
        {
            Point location = User.CurrentLocation;
            while (location != target)
            {
                MirDirection dir = Functions.DirectionFromPoint(location, target);

                location = Functions.PointMove(location, dir, 1);

                if (location.X < 0 || location.Y < 0 || location.X >= GameScene.Scene.MapControl.Width || location.Y >= GameScene.Scene.MapControl.Height) return false;

                if (!GameScene.Scene.MapControl.ValidPoint(location)) return false;
            }

            return true;
        }


        public bool ValidPoint(Point p)
        {
            //GameScene.Scene.ChatDialog.ReceiveChat(string.Format("cell: {0}", (M2CellInfo[p.X, p.Y].BackImage & 0x20000000)), ChatType.Hint);
            if (M2CellInfo == null || Width <= 0 || Height <= 0)
                return true;

            if (p.X < 0 || p.Y < 0 || p.X >= Width || p.Y >= Height)
                return false;

            CellInfo cell = M2CellInfo[p.X, p.Y];
            if (cell == null)
                return true;

            return (cell.BackImage & 0x20000000) == 0;
        }
        public bool HasTarget(Point p)
        {
            for (int i = 0; i < Objects.Count; i++)
            {
                MapObject ob = Objects[i];

                if (ob.CurrentLocation == p && ob.Blocking)
                    return true;
            }
            return false;
        }
        public bool CanHalfMoon(Point p, MirDirection d)
        {
            d = Functions.PreviousDir(d);
            for (int i = 0; i < 4; i++)
            {
                if (HasTarget(Functions.PointMove(p, d, 1))) return true;
                d = Functions.NextDir(d);
            }
            return false;
        }
        public bool CanCrossHalfMoon(Point p)
        {
            MirDirection dir = MirDirection.Up;
            for (int i = 0; i < 8; i++)
            {
                if (HasTarget(Functions.PointMove(p, dir, 1))) return true;
                dir = Functions.NextDir(dir);
            }
            return false;
        }

        #region Disposable

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Objects.Clear();

                MapButtons = new MouseState();
                MouseLocation = Point.Empty;
                InputDelay = 0;
                NextAction = 0;

                M2CellInfo = null;
                Width = 0;
                Height = 0;

                FileName = String.Empty;
                Title = String.Empty;
                MiniMap = 0;
                BigMap = 0;
                Lights = 0;
                FloorValid = false;
                LightsValid = false;
                MapDarkLight = 0;
                Music = 0;

                AnimationCount = 0;
                Effects.Clear();
                _loadedMapFileName = string.Empty;
                _loadedMiniMap = 0;
                _loadedBigMap = 0;
                _hasLoadedMap = false;
                ClearPendingMapLoadObjectTracking();
            }

            base.Dispose(disposing);
        }

        #endregion



        public void RemoveObject(MapObject ob)
        {
            if (M2CellInfo == null || ob == null)
                return;

            int x = ob.MapLocation.X;
            int y = ob.MapLocation.Y;
            if (x < 0 || y < 0 || x >= Width || y >= Height)
                return;

            M2CellInfo[x, y]?.RemoveObject(ob);
        }
        public void AddObject(MapObject ob)
        {
            if (M2CellInfo == null || ob == null)
                return;

            int x = ob.MapLocation.X;
            int y = ob.MapLocation.Y;
            if (x < 0 || y < 0 || x >= Width || y >= Height)
                return;

            CellInfo cell = M2CellInfo[x, y];
            if (cell == null)
            {
                cell = new CellInfo();
                M2CellInfo[x, y] = cell;
            }

            cell.AddObject(ob);
        }
        public MapObject FindObject(uint ObjectID, int x, int y)
        {
            if (M2CellInfo == null || x < 0 || y < 0 || x >= Width || y >= Height)
                return null;

            return M2CellInfo[x, y]?.FindObject(ObjectID);
        }
        public void SortObject(MapObject ob)
        {
            if (M2CellInfo == null || ob == null)
                return;

            int x = ob.MapLocation.X;
            int y = ob.MapLocation.Y;
            if (x < 0 || y < 0 || x >= Width || y >= Height)
                return;

            M2CellInfo[x, y]?.Sort();
        }

        public Door GetDoor(byte Index)
        {
            for (int i = 0; i < Doors.Count; i++)
            {
                if (Doors[i].index == Index)
                    return Doors[i];
            }
            return null;
        }
        public void Processdoors()
        {
            for (int i = 0; i < Doors.Count; i++)
            {
                if ((Doors[i].DoorState == DoorState.Opening) || (Doors[i].DoorState == DoorState.Closing))
                {
                    if (Doors[i].LastTick + 50 < CMain.Time)
                    {
                        Doors[i].LastTick = CMain.Time;
                        Doors[i].ImageIndex++;

                        if (Doors[i].ImageIndex == 1)//change the 1 if you want to animate doors opening/closing
                        {
                            Doors[i].ImageIndex = 0;
                            Doors[i].DoorState = (DoorState)Enum.ToObject(typeof(DoorState), ((byte)++Doors[i].DoorState % 4));
                        }

                        FloorValid = false;
                    }
                }
                if (Doors[i].DoorState == DoorState.Open)
                {
                    if (Doors[i].LastTick + 5000 < CMain.Time)
                    {
                        Doors[i].LastTick = CMain.Time;
                        Doors[i].DoorState = DoorState.Closing;
                        FloorValid = false;
                    }
                }
            }
        }
        public void OpenDoor(byte Index, bool closed)
        {
            Door Info = GetDoor(Index);
            if (Info == null) return;
            Info.DoorState = (closed ? DoorState.Closing : Info.DoorState == DoorState.Open ? DoorState.Open : DoorState.Opening);
            Info.ImageIndex = 0;
            Info.LastTick = CMain.Time;
        }
    }
}

