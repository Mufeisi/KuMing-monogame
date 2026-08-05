using ClientPackets;
using Server.Library.Utils;
using Server.MirDatabase;
using Server.MirNetwork;
using Server.MirObjects;
using Server.MirObjects.Monsters;
using Server.Persistence;
using Server.Persistence.Sql;
using Server.Scripting;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Numerics;
using System.Text.RegularExpressions;
using S = ServerPackets;

namespace Server.MirEnvir
{
    public class MobThread
    {
        public int Id = 0;
        public long LastRunTime = 0;
        public long StartTime = 0;
        public long EndTime = 0;
        public LinkedList<MapObject> ObjectsList = new LinkedList<MapObject>();
        public LinkedListNode<MapObject> _current = null;
        public bool Stop = false;
    }

    public class RandomProvider
    {
        private static int seed = Environment.TickCount;
        private static readonly ThreadLocal<Random> RandomWrapper = new ThreadLocal<Random>(() => new Random(Interlocked.Increment(ref seed)));

        public static Random GetThreadRandom() =>
            RandomWrapper.Value;

        public int Next() =>
            RandomWrapper.Value.Next();
        public int Next(int maxValue) =>
            RandomWrapper.Value.Next(maxValue);
        public int Next(int minValue, int maxValue) =>
            RandomWrapper.Value.Next(minValue, maxValue);
    }

    public class Envir
    {
        public static Envir Main { get; } = new Envir();

        public static Envir Edit { get; } = new Envir();

        protected static MessageQueue MessageQueue => MessageQueue.Instance;

        public ScriptManager CSharpScripts { get; } = new ScriptManager();

        private IServerPersistence? _persistence;

        private IServerPersistence Persistence => _persistence ??= ServerPersistenceFactory.CreateFromSettings();

        public IQuestProvider QuestProvider { get; private set; }
        private HashSet<string> _lastAppliedCSharpQuestKeys = new HashSet<string>(StringComparer.Ordinal);

        public IDropTableProvider DropTableProvider { get; private set; }

        public IRecipeProvider RecipeProvider { get; private set; }

        public IValueProvider ValueProvider { get; private set; }
        private readonly JsonValueStore _valueStore = new JsonValueStore();

        public INameListProvider NameListProvider { get; private set; }
        private readonly JsonNameListStore _nameListStore = new JsonNameListStore();

        public IRouteProvider RouteProvider { get; private set; }

        public IRootConfigProvider RootConfigProvider { get; private set; }

        public ITextFileProvider TextFileProvider { get; private set; }

        private long _lastAppliedCSharpActiveMapCoordsVersion = -1;

        private static readonly ConcurrentDictionary<string, object> NameListFileLocks =
            new ConcurrentDictionary<string, object>(StringComparer.OrdinalIgnoreCase);

        private static readonly Dictionary<string, string> LegacyItemNameAliases =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["WoodenBow"] = "木弓",
                ["EbonyBow"] = "革弓",
                ["ShortBow"] = "铁弓",
                ["BoneBow"] = "弩弓",
                ["CompoundBow"] = "杉木铁弓",
                ["PheonixBead"] = "凤凰明珠",
                ["Focus"] = "必中闪",
                ["StraightShot"] = "天日闪",
                ["MentalState"] = "精神状态",
                ["DoubleShot"] = "无我闪",
                ["Meditation"] = "气功术",
                ["血龙闪秘笈"] = "血龙闪-秘籍",
                ["LightLeatherArmour(男)"] = "轻型盔甲(男)",
                ["LightLeatherArmour(女)"] = "轻型盔甲(女)",
                ["黄铜头盔"] = "青铜头盔",
                ["紫水晶项链"] = "紫晶项链",
                ["飞天通用币"] = "通用币",
                ["服务测试卷轴"] = "服务器测试书卷",
                ["BoxofHolyWater(1)"] = "BoxofHolyWater1",
                ["BoxofHolyWater(3)"] = "BoxofHolyWater1",
                ["飞天令牌(1)"] = "比奇令牌(1)",
                ["飞天令牌(2)"] = "比奇令牌(2)",
                ["飞天令牌(3)"] = "比奇令牌(3)",
                ["钢斧"] = "钢铁斧",
                ["黑术腰"] = "黑术腰带",
            };

        public static object AccountLock = new object();
        public static object LoadLock = new object();

        public const int MinVersion = 60;
        public const int Version = 110;
        public const int CustomVersion = 0;
        public static readonly string DatabasePath = Path.Combine(".", "Server.MirDB");
        public static readonly string AccountPath = Path.Combine(".", "Server.MirADB");
        public static readonly string BackUpPath = Path.Combine(".", "Back Up");
        public static readonly string AccountsBackUpPath = Path.Combine(".", "Back Up", "Accounts");
        public static readonly string ArchivePath = Path.Combine(".", "Archive");
        public bool ResetGS = false;
        public bool GuildRefreshNeeded;

        private static readonly Regex AccountIDReg, PasswordReg, EMailReg, CharacterReg;

        public static int LoadVersion;
        public static int LoadCustomVersion;

        private readonly DateTime _startTime = DateTime.Now;
        public readonly Stopwatch Stopwatch = Stopwatch.StartNew();

        public long Time { get; private set; }
        public RespawnTimer RespawnTick = new RespawnTimer();

        private int _autoSaveRequested;

        private static List<string> DisabledCharNames = new List<string>();
        private static List<string> LineMessages = new List<string>();

        public static ConcurrentDictionary<string, DateTime> IPBlocks = new ConcurrentDictionary<string, DateTime>();

        public static ConcurrentDictionary<string, MirConnectionLog> ConnectionLogs = new ConcurrentDictionary<string, MirConnectionLog>();

        public DateTime Now =>
            _startTime.AddMilliseconds(Time);

        public bool IsNetworkBound
        {
            get
            {
                try
                {
                    return _listener?.Server?.IsBound == true;
                }
                catch
                {
                    return false;
                }
            }
        }

        public void RequestAutoSave()
        {
            Interlocked.Exchange(ref _autoSaveRequested, 1);
        }

        public bool Running { get; private set; }

        private abstract class MainThreadWorkItem
        {
            public Exception Exception;
            public ManualResetEventSlim Done { get; } = new ManualResetEventSlim(initialState: false);
            public abstract void Execute();
        }

        private sealed class MainThreadWorkItem<T> : MainThreadWorkItem
        {
            private readonly Func<T> _func;
            public T Result;

            public MainThreadWorkItem(Func<T> func)
            {
                _func = func ?? throw new ArgumentNullException(nameof(func));
            }

            public override void Execute()
            {
                try
                {
                    Result = _func();
                }
                catch (Exception ex)
                {
                    Exception = ex;
                }
                finally
                {
                    Done.Set();
                }
            }
        }

        private int _mainThreadId;
        private readonly ConcurrentQueue<MainThreadWorkItem> _mainThreadQueue = new ConcurrentQueue<MainThreadWorkItem>();

        public bool IsMainThread
        {
            get
            {
                var id = Volatile.Read(ref _mainThreadId);
                return id != 0 && Thread.CurrentThread.ManagedThreadId == id;
            }
        }

        public T InvokeOnMainThread<T>(Func<T> func, int timeoutMs = 5000)
        {
            if (func == null) throw new ArgumentNullException(nameof(func));

            if (IsMainThread) return func();

            // 主线程尚未建立（或服务已停止）时，避免死锁：直接返回默认值或执行。
            if (Volatile.Read(ref _mainThreadId) == 0) return func();
            if (!Running) return default;

            var item = new MainThreadWorkItem<T>(func);
            _mainThreadQueue.Enqueue(item);

            if (!item.Done.Wait(timeoutMs))
            {
                MessageQueue.Enqueue($"[Scripts] 投递到主线程执行超时（{timeoutMs}ms）。");
                return default;
            }

            if (item.Exception != null) throw item.Exception;

            return item.Result;
        }

        private void ProcessMainThreadQueue()
        {
            if (!IsMainThread) return;

            const int maxPerLoop = 128;

            for (var i = 0; i < maxPerLoop; i++)
            {
                if (!_mainThreadQueue.TryDequeue(out var item)) return;
                item.Execute();
            }
        }


        private static uint _objectID;
        public uint ObjectID => ++_objectID;

        public static int _playerCount;
        public int PlayerCount => Players.Count;

        public int[] OnlineRankingCount = new int[6];
        public int HeroCount => Heroes.Count;

        public RandomProvider Random = new RandomProvider();

        private Thread _thread;
        private TcpListener _listener;
        private bool StatusPortEnabled = true;
        public List<MirStatusConnection> StatusConnections = new List<MirStatusConnection>();
        private TcpListener _StatusPort;
        private int _sessionID;
        public List<MirConnection> Connections = new List<MirConnection>();

        //Server DB
        public int MapIndex, ItemIndex, MonsterIndex, NPCIndex, QuestIndex, GameshopIndex, ConquestIndex, RespawnIndex, ScriptIndex;
        public List<MapInfo> MapInfoList = new List<MapInfo>();
        public List<ItemInfo> ItemInfoList = new List<ItemInfo>();
        public List<MonsterInfo> MonsterInfoList = new List<MonsterInfo>();
        public List<MagicInfo> MagicInfoList = new List<MagicInfo>();
        public List<NPCInfo> NPCInfoList = new List<NPCInfo>();
        public DragonInfo DragonInfo = new DragonInfo();
        public List<QuestInfo> QuestInfoList = new List<QuestInfo>();
        public List<GameShopItem> GameShopList = new List<GameShopItem>();
        private readonly Dictionary<int, ulong> _recipeUniqueIdByItemIndex = new Dictionary<int, ulong>();
        private RecipeInfo[] _recipeInfoList = Array.Empty<RecipeInfo>();
        public IReadOnlyList<RecipeInfo> RecipeInfoList => Volatile.Read(ref _recipeInfoList);
        public List<BuffInfo> BuffInfoList = new List<BuffInfo>();
        public List<ConquestInfo> ConquestInfoList = new List<ConquestInfo>();

        //User DB
        public int NextAccountID, NextCharacterID, NextGuildID, NextHeroID;
        public ulong NextUserItemID, NextAuctionID, NextMailID, NextRecipeID;
        public List<AccountInfo> AccountList = new List<AccountInfo>();
        public List<CharacterInfo> CharacterList = new List<CharacterInfo>();
        public List<GuildInfo> GuildList = new List<GuildInfo>();
        public LinkedList<AuctionInfo> Auctions = new LinkedList<AuctionInfo>();
        public List<ConquestGuildInfo> ConquestList = new List<ConquestGuildInfo>();
        public Dictionary<int, int> GameshopLog = new Dictionary<int, int>();
        public List<HeroInfo> HeroList = new List<HeroInfo>();

        public int GuildCount; //This shouldn't be needed?? -> remove in the future

        //Live Info
        public bool Saving = false;
        public List<Map> MapList = new List<Map>();
        public List<SafeZoneInfo> StartPoints = new List<SafeZoneInfo>();
        public List<ItemInfo> StartItems = new List<ItemInfo>();

        public List<PlayerObject> Players = new List<PlayerObject>();
        public List<SpellObject> Spells = new List<SpellObject>();
        public List<NPCObject> NPCs = new List<NPCObject>();
        public List<GuildObject> Guilds = new List<GuildObject>();
        public List<ConquestObject> Conquests = new List<ConquestObject>();
        public List<HeroObject> Heroes = new List<HeroObject>();

        public LightSetting Lights;
        public LinkedList<MapObject> Objects = new LinkedList<MapObject>();
        public Dictionary<int, NPCScript> Scripts = new Dictionary<int, NPCScript>();
        public Dictionary<string, Timer> Timers = new Dictionary<string, Timer>();

        //multithread vars
        readonly object _locker = new object();
        public MobThread[] MobThreads = new MobThread[Settings.ThreadLimit];
        private readonly Thread[] MobThreading = new Thread[Settings.ThreadLimit];
        public int SpawnMultiplier = 1;//set this to 2 if you want double spawns (warning this can easily lag your server far beyond what you imagine)

        public List<string> CustomCommands = new List<string>();

        public Dragon DragonSystem;
        public NPCScript DefaultNPC, MonsterNPC, RobotNPC;

        public List<DropInfo> FishingDrops = new List<DropInfo>();
        public List<DropInfo> AwakeningDrops = new List<DropInfo>();

        public List<DropInfo> StrongboxDrops = new List<DropInfo>();
        public List<DropInfo> BlackstoneDrops = new List<DropInfo>();

        public List<GuildAtWar> GuildsAtWar = new List<GuildAtWar>();
        public List<MapRespawn> SavedSpawns = new List<MapRespawn>();

        public List<RankCharacterInfo> RankTop = new List<RankCharacterInfo>();
        public List<RankCharacterInfo>[] RankClass = new List<RankCharacterInfo>[5];

        static HttpServer http;

        static Envir()
        {
            AccountIDReg = new Regex(@"^[A-Za-z0-9]{" + Globals.MinAccountIDLength + "," + Globals.MaxAccountIDLength + "}$");
            PasswordReg = new Regex(@"^[A-Za-z0-9]{" + Globals.MinPasswordLength + "," + Globals.MaxPasswordLength + "}$");
            EMailReg = new Regex(@"\w+([-+.]\w+)*@\w+([-.]\w+)*\.\w+([-.]\w+)*");
            CharacterReg = new Regex(@"^[A-Za-z0-9\u4E00-\u9FA5]{" + Globals.MinCharacterNameLength + "," + Globals.MaxCharacterNameLength + "}$");
        }

        public static int LastCount = 0, LastRealCount = 0;
        public static long LastRunTime = 0;
        public int MonsterCount;

        private long warTime, guildTime, conquestTime, rentalItemsTime, auctionTime, spawnTime, robotTime, timerTime;
        private int dailyTime = DateTime.Now.Day;

        private bool MagicExists(Spell spell)
        {
            for (var i = 0; i < MagicInfoList.Count; i++)
            {
                if (MagicInfoList[i].Spell == spell) return true;
            }
            return false;
        }

        private void UpdateMagicInfo()
        {
            for (var i = 0; i < MagicInfoList.Count; i++)
            {
                switch (MagicInfoList[i].Spell)
                {
                    //warrior
                    case Spell.Thrusting:
                        MagicInfoList[i].MultiplierBase = 0.25f;
                        MagicInfoList[i].MultiplierBonus = 0.25f;
                        break;
                    case Spell.HalfMoon:
                        MagicInfoList[i].MultiplierBase = 0.3f;
                        MagicInfoList[i].MultiplierBonus = 0.1f;
                        break;
                    case Spell.ShoulderDash:
                        MagicInfoList[i].MPowerBase = 4;
                        break;
                    case Spell.TwinDrakeBlade:
                        MagicInfoList[i].MultiplierBase = 0.8f;
                        MagicInfoList[i].MultiplierBonus = 0.1f;
                        break;
                    case Spell.FlamingSword:
                        MagicInfoList[i].MultiplierBase = 1.4f;
                        MagicInfoList[i].MultiplierBonus = 0.4f;
                        break;
                    case Spell.CrossHalfMoon:
                        MagicInfoList[i].MultiplierBase = 0.4f;
                        MagicInfoList[i].MultiplierBonus = 0.1f;
                        break;
                    case Spell.BladeAvalanche:
                        MagicInfoList[i].MultiplierBase = 1f;
                        MagicInfoList[i].MultiplierBonus = 0.4f;
                        break;
                    case Spell.SlashingBurst:
                        MagicInfoList[i].MultiplierBase = 3.25f;
                        MagicInfoList[i].MultiplierBonus = 0.25f;
                        break;
                    case Spell.DimensionalSword:
                        MagicInfoList[i].MultiplierBase = 1f;
                        MagicInfoList[i].MultiplierBonus = 0.25f;
                        break;
                    case Spell.DimensionalSwordRare:
                        MagicInfoList[i].MultiplierBase = 1f;
                        MagicInfoList[i].MultiplierBonus = 0.25f;
                        break;
                    //wiz
                    case Spell.Repulsion:
                        MagicInfoList[i].MPowerBase = 4;
                        break;
                    //tao
                    case Spell.Poisoning:
                        MagicInfoList[i].MPowerBase = 0;
                        break;
                    case Spell.Curse:
                        MagicInfoList[i].MPowerBase = 20;
                        break;
                    case Spell.Plague:
                        MagicInfoList[i].MPowerBase = 0;
                        MagicInfoList[i].PowerBase = 0;
                        break;
                    //sin
                    case Spell.FatalSword:
                        MagicInfoList[i].MPowerBase = 20;
                        break;
                    case Spell.DoubleSlash:
                        MagicInfoList[i].MultiplierBase = 0.8f;
                        MagicInfoList[i].MultiplierBonus = 0.1f;
                        break;
                    case Spell.FireBurst:
                        MagicInfoList[i].MPowerBase = 4;
                        break;
                    case Spell.MoonLight:
                        MagicInfoList[i].MPowerBase = 20;
                        break;
                    case Spell.DarkBody:
                        MagicInfoList[i].MPowerBase = 20;
                        break;
                    case Spell.Hemorrhage:
                        MagicInfoList[i].MultiplierBase = 0.2f;
                        MagicInfoList[i].MultiplierBonus = 0.05f;
                        break;
                    case Spell.CrescentSlash:
                        MagicInfoList[i].MultiplierBase = 1f;
                        MagicInfoList[i].MultiplierBonus = 0.4f;
                        break;
                }
            }
        }

        private void FillMagicInfoList()
        {
            //Warrior
            if (!MagicExists(Spell.Fencing)) MagicInfoList.Add(new MagicInfo {Name = "基本剑术", Spell = Spell.Fencing, Icon = 2, Level1 = 7, Level2 = 9, Level3 = 12, Need1 = 270, Need2 = 600, Need3 = 1300, Range = 0 });
            if (!MagicExists(Spell.Slaying)) MagicInfoList.Add(new MagicInfo { Name = "攻杀剑术", Spell = Spell.Slaying, Icon = 6, Level1 = 15, Level2 = 17, Level3 = 20, Need1 = 500, Need2 = 1100, Need3 = 1800, Range = 0 });
            if (!MagicExists(Spell.Thrusting)) MagicInfoList.Add(new MagicInfo { Name = "刺杀剑术", Spell = Spell.Thrusting, Icon = 11, Level1 = 22, Level2 = 24, Level3 = 27, Need1 = 2000, Need2 = 3500, Need3 = 6000, Range = 0, MultiplierBase = 0.25f, MultiplierBonus = 0.25f });
            if (!MagicExists(Spell.HalfMoon)) MagicInfoList.Add(new MagicInfo { Name = "半月弯刀", Spell = Spell.HalfMoon, Icon = 24, Level1 = 26, Level2 = 28, Level3 = 31, Need1 = 5000, Need2 = 8000, Need3 = 14000, BaseCost = 3, Range = 0, MultiplierBase =0.3f, MultiplierBonus = 0.1f });
            if (!MagicExists(Spell.ShoulderDash)) MagicInfoList.Add(new MagicInfo { Name = "野蛮冲撞", Spell = Spell.ShoulderDash, Icon = 26, Level1 = 30, Level2 = 32, Level3 = 34, Need1 = 3000, Need2 = 4000, Need3 = 6000, BaseCost = 4, LevelCost = 4, DelayBase = 2500, Range = 0 , MPowerBase = 4});
            if (!MagicExists(Spell.TwinDrakeBlade)) MagicInfoList.Add(new MagicInfo { Name = "双龙斩", Spell = Spell.TwinDrakeBlade, Icon = 37, Level1 = 32, Level2 = 34, Level3 = 37, Need1 = 4000, Need2 = 6000, Need3 = 10000, BaseCost = 10, Range = 0 , MultiplierBase = 0.8f, MultiplierBonus = 0.1f});
            if (!MagicExists(Spell.Entrapment)) MagicInfoList.Add(new MagicInfo { Name = "捕绳剑", Spell = Spell.Entrapment, Icon = 46, Level1 = 32, Level2 = 35, Level3 = 37, Need1 = 2000, Need2 = 3500, Need3 = 5500, BaseCost = 15, LevelCost = 3, Range = 9 });
            if (!MagicExists(Spell.FlamingSword)) MagicInfoList.Add(new MagicInfo { Name = "烈火剑法", Spell = Spell.FlamingSword, Icon = 25, Level1 = 35, Level2 = 37, Level3 = 40, Need1 = 2000, Need2 = 4000, Need3 = 6000, BaseCost = 7, Range = 0, MultiplierBase = 1.4f, MultiplierBonus = 0.4f});
            if (!MagicExists(Spell.LionRoar)) MagicInfoList.Add(new MagicInfo { Name = "狮子吼", Spell = Spell.LionRoar, Icon = 42, Level1 = 36, Level2 = 39, Level3 = 41, Need1 = 5000, Need2 = 8000, Need3 = 12000, BaseCost = 14, LevelCost = 4, Range = 0 });
            if (!MagicExists(Spell.CrossHalfMoon)) MagicInfoList.Add(new MagicInfo { Name = "狂风斩", Spell = Spell.CrossHalfMoon, Icon = 33, Level1 = 38, Level2 = 40, Level3 = 42, Need1 = 7000, Need2 = 11000, Need3 = 16000, BaseCost = 6, Range = 0, MultiplierBase = 0.4f, MultiplierBonus = 0.1f });
            if (!MagicExists(Spell.BladeAvalanche)) MagicInfoList.Add(new MagicInfo { Name = "空破闪", Spell = Spell.BladeAvalanche, Icon = 43, Level1 = 38, Level2 = 41, Level3 = 43, Need1 = 5000, Need2 = 8000, Need3 = 12000, BaseCost = 14, LevelCost = 4, Range = 0, MultiplierBonus = 0.3f});
            if (!MagicExists(Spell.ProtectionField)) MagicInfoList.Add(new MagicInfo { Name = "护身气幕", Spell = Spell.ProtectionField, Icon = 50, Level1 = 39, Level2 = 42, Level3 = 45, Need1 = 6000, Need2 = 12000, Need3 = 18000, BaseCost = 23, LevelCost = 6, Range = 0 });
            if (!MagicExists(Spell.Rage)) MagicInfoList.Add(new MagicInfo { Name = "剑气爆", Spell = Spell.Rage, Icon = 49, Level1 = 44, Level2 = 47, Level3 = 50, Need1 = 8000, Need2 = 14000, Need3 = 20000, BaseCost = 20, LevelCost = 5, Range = 0 });
            if (!MagicExists(Spell.CounterAttack)) MagicInfoList.Add(new MagicInfo { Name = "天务", Spell = Spell.CounterAttack, Icon = 72, Level1 = 47, Level2 = 51, Level3 = 55, Need1 = 7000, Need2 = 11000, Need3 = 15000, BaseCost = 12, LevelCost = 4, DelayBase = 24000, Range = 0 , MultiplierBonus = 0.4f});
            if (!MagicExists(Spell.SlashingBurst)) MagicInfoList.Add(new MagicInfo { Name = "日闪", Spell = Spell.SlashingBurst, Icon = 55, Level1 = 50, Level2 = 53, Level3 = 56, Need1 = 10000, Need2 = 16000, Need3 = 24000, BaseCost = 25, LevelCost = 4, MPowerBase = 1, PowerBase = 3, DelayBase = 14000, DelayReduction = 4000, Range = 0 , MultiplierBase = 3.25f, MultiplierBonus = 0.25f});
            if (!MagicExists(Spell.Fury)) MagicInfoList.Add(new MagicInfo { Name = "血龙剑法", Spell = Spell.Fury, Icon = 76, Level1 = 45, Level2 = 48, Level3 = 51, Need1 = 8000, Need2 = 14000, Need3 = 20000, BaseCost = 10, LevelCost = 4, DelayBase = 600000, DelayReduction = 120000, Range = 0 });
            if (!MagicExists(Spell.ImmortalSkin)) MagicInfoList.Add(new MagicInfo { Name = "金刚不坏", Spell = Spell.ImmortalSkin, Icon = 80, Level1 = 60, Level2 = 61, Level3 = 62, Need1 = 1560, Need2 = 2200, Need3 = 3000, BaseCost = 10, LevelCost = 4, DelayBase = 600000, DelayReduction = 120000, Range = 0 });
            if (!MagicExists(Spell.EntrapmentRare)) MagicInfoList.Add(new MagicInfo { Name = "捕绳剑-秘籍", Spell = Spell.EntrapmentRare, Icon = 107, Level1 = 55, Level2 = 60, Level3 = 65, Need1 = 10000, Need2 = 13000, Need3 = 16000, BaseCost = 15, LevelCost = 3, Range = 9 });
            if (!MagicExists(Spell.ImmortalSkinRare)) MagicInfoList.Add(new MagicInfo { Name = "金刚不坏-秘籍", Spell = Spell.ImmortalSkinRare, Icon = 84, Level1 = 62, Level2 = 64, Level3 = 66, Need1 = 1000, Need2 = 1560, Need3 = 2200, BaseCost = 10, LevelCost = 4, DelayBase = 600000, DelayReduction = 120000, Range = 0 });
            if (!MagicExists(Spell.LionRoarRare)) MagicInfoList.Add(new MagicInfo { Name = "狮子吼-秘籍", Spell = Spell.LionRoarRare, Icon = 112, Level1 = 95, Level2 = 97, Level3 = 102, Need1 = 8900, Need2 = 15000, Need3 = 21600, BaseCost = 14, LevelCost = 4, Range = 0 });
            if (!MagicExists(Spell.DimensionalSword)) MagicInfoList.Add(new MagicInfo { Name = "时空剑", Spell = Spell.DimensionalSword, Icon = 117, Level1 = 90, Level2 = 92, Level3 = 94, Need1 = 3800, Need2 = 6300, Need3 = 9300, BaseCost = 32, LevelCost = 4, MPowerBase = 1, PowerBase = 3, DelayBase = 14000, DelayReduction = 4000, Range = 2 , MultiplierBase = 1f, MultiplierBonus = 0.25f});
            if (!MagicExists(Spell.DimensionalSwordRare)) MagicInfoList.Add(new MagicInfo { Name = "时空剑-秘籍", Spell = Spell.DimensionalSwordRare, Icon = 122, Level1 = 100, Level2 = 105, Level3 = 110, Need1 = 8800, Need2 = 13000, Need3 = 21600, BaseCost = 32, LevelCost = 4, MPowerBase = 1, PowerBase = 3, DelayBase = 14000, DelayReduction = 4000, Range = 2 , MultiplierBase = 1f, MultiplierBonus = 0.25f});

            //Wizard
            if (!MagicExists(Spell.FireBall)) MagicInfoList.Add(new MagicInfo { Name = "火球术", Spell = Spell.FireBall, Icon = 0, Level1 = 7, Level2 = 9, Level3 = 11, Need1 = 200, Need2 = 350, Need3 = 700, BaseCost = 3, LevelCost = 2, MPowerBase = 8, PowerBase = 2, Range = 9 });
            if (!MagicExists(Spell.Repulsion)) MagicInfoList.Add(new MagicInfo { Name = "抗拒火环", Spell = Spell.Repulsion, Icon = 7, Level1 = 12, Level2 = 15, Level3 = 19, Need1 = 500, Need2 = 1300, Need3 = 2200, BaseCost = 2, LevelCost = 2, Range = 0, MPowerBase = 4});
            if (!MagicExists(Spell.ElectricShock)) MagicInfoList.Add(new MagicInfo { Name = "诱惑之光", Spell = Spell.ElectricShock, Icon = 19, Level1 = 13, Level2 = 18, Level3 = 24, Need1 = 530, Need2 = 1100, Need3 = 2200, BaseCost = 3, LevelCost = 1, Range = 9 });
            if (!MagicExists(Spell.GreatFireBall)) MagicInfoList.Add(new MagicInfo { Name = "大火球", Spell = Spell.GreatFireBall, Icon = 4, Level1 = 15, Level2 = 18, Level3 = 21, Need1 = 2000, Need2 = 2700, Need3 = 3500, BaseCost = 5, LevelCost = 1, MPowerBase = 6, PowerBase = 10, Range = 9 });
            if (!MagicExists(Spell.HellFire)) MagicInfoList.Add(new MagicInfo { Name = "地狱火", Spell = Spell.HellFire, Icon = 8, Level1 = 16, Level2 = 20, Level3 = 24, Need1 = 700, Need2 = 2700, Need3 = 3500, BaseCost = 10, LevelCost = 3, MPowerBase = 14, PowerBase = 6, Range = 0 });
            if (!MagicExists(Spell.ThunderBolt)) MagicInfoList.Add(new MagicInfo { Name = "雷电术", Spell = Spell.ThunderBolt, Icon = 10, Level1 = 17, Level2 = 20, Level3 = 23, Need1 = 500, Need2 = 2000, Need3 = 3500, BaseCost = 9, LevelCost = 2, MPowerBase = 8, MPowerBonus = 20, PowerBase = 9, Range = 9 });
            if (!MagicExists(Spell.Teleport)) MagicInfoList.Add(new MagicInfo { Name = "瞬息移动", Spell = Spell.Teleport, Icon = 20, Level1 = 19, Level2 = 22, Level3 = 25, Need1 = 350, Need2 = 1000, Need3 = 2000, BaseCost = 10, LevelCost = 3, Range = 0 });
            if (!MagicExists(Spell.FireBang)) MagicInfoList.Add(new MagicInfo { Name = "爆裂火焰", Spell = Spell.FireBang, Icon = 22, Level1 = 22, Level2 = 25, Level3 = 28, Need1 = 3000, Need2 = 5000, Need3 = 10000, BaseCost = 14, LevelCost = 4, MPowerBase = 8, PowerBase = 8, Range = 9 });
            if (!MagicExists(Spell.FireWall)) MagicInfoList.Add(new MagicInfo { Name = "火墙", Spell = Spell.FireWall, Icon = 21, Level1 = 24, Level2 = 28, Level3 = 33, Need1 = 4000, Need2 = 10000, Need3 = 20000, BaseCost = 30, LevelCost = 5, MPowerBase = 3, PowerBase = 3, Range = 9 });
            if (!MagicExists(Spell.Lightning)) MagicInfoList.Add(new MagicInfo { Name = "疾光电影", Spell = Spell.Lightning, Icon = 9, Level1 = 26, Level2 = 29, Level3 = 32, Need1 = 3000, Need2 = 6000, Need3 = 12000, BaseCost = 38, LevelCost = 7, MPowerBase = 12, PowerBase = 12, Range = 0 });
            if (!MagicExists(Spell.FrostCrunch)) MagicInfoList.Add(new MagicInfo { Name = "寒冰掌", Spell = Spell.FrostCrunch, Icon = 38, Level1 = 28, Level2 = 30, Level3 = 33, Need1 = 3000, Need2 = 5000, Need3 = 8000, BaseCost = 15, LevelCost = 3, MPowerBase = 12, PowerBase = 12, Range = 9 });
            if (!MagicExists(Spell.ThunderStorm)) MagicInfoList.Add(new MagicInfo { Name = "地狱雷光", Spell = Spell.ThunderStorm, Icon = 23, Level1 = 30, Level2 = 32, Level3 = 34, Need1 = 4000, Need2 = 8000, Need3 = 12000, BaseCost = 29, LevelCost = 9, MPowerBase = 10, MPowerBonus = 20, PowerBase = 10, PowerBonus = 20, Range = 0 });
            if (!MagicExists(Spell.MagicShield)) MagicInfoList.Add(new MagicInfo { Name = "魔法盾", Spell = Spell.MagicShield, Icon = 30, Level1 = 31, Level2 = 34, Level3 = 38, Need1 = 3000, Need2 = 7000, Need3 = 10000, BaseCost = 35, LevelCost = 5, Range = 0 });
            if (!MagicExists(Spell.TurnUndead)) MagicInfoList.Add(new MagicInfo { Name = "圣言术", Spell = Spell.TurnUndead, Icon = 31, Level1 = 32, Level2 = 35, Level3 = 39, Need1 = 3000, Need2 = 7000, Need3 = 10000, BaseCost = 52, LevelCost = 13, Range = 9 });
            if (!MagicExists(Spell.Vampirism)) MagicInfoList.Add(new MagicInfo { Name = "嗜血术", Spell = Spell.Vampirism, Icon = 47, Level1 = 33, Level2 = 36, Level3 = 40, Need1 = 3000, Need2 = 5000, Need3 = 8000, BaseCost = 26, LevelCost = 13, MPowerBase = 12, PowerBase = 12, Range = 9 });
            if (!MagicExists(Spell.IceStorm)) MagicInfoList.Add(new MagicInfo { Name = "冰咆哮", Spell = Spell.IceStorm, Icon = 32, Level1 = 35, Level2 = 37, Level3 = 40, Need1 = 4000, Need2 = 8000, Need3 = 12000, BaseCost = 33, LevelCost = 3, MPowerBase = 12, PowerBase = 14, Range = 9 });
            if (!MagicExists(Spell.FlameDisruptor)) MagicInfoList.Add(new MagicInfo { Name = "灭天火", Spell = Spell.FlameDisruptor, Icon = 34, Level1 = 38, Level2 = 40, Level3 = 42, Need1 = 5000, Need2 = 9000, Need3 = 14000, BaseCost = 28, LevelCost = 3, MPowerBase = 15, MPowerBonus = 20, PowerBase = 9, Range = 9 });
            if (!MagicExists(Spell.Mirroring)) MagicInfoList.Add(new MagicInfo { Name = "分身术", Spell = Spell.Mirroring, Icon = 41, Level1 = 41, Level2 = 43, Level3 = 45, Need1 = 6000, Need2 = 11000, Need3 = 16000, BaseCost = 21, Range = 0 });
            if (!MagicExists(Spell.FlameField)) MagicInfoList.Add(new MagicInfo { Name = "火龙气焰", Spell = Spell.FlameField, Icon = 44, Level1 = 42, Level2 = 43, Level3 = 45, Need1 = 6000, Need2 = 11000, Need3 = 16000, BaseCost = 45, LevelCost = 8, MPowerBase = 100, PowerBase = 25, Range = 9 });
            if (!MagicExists(Spell.Blizzard)) MagicInfoList.Add(new MagicInfo { Name = "天霜冰环", Spell = Spell.Blizzard, Icon = 51, Level1 = 44, Level2 = 47, Level3 = 50, Need1 = 8000, Need2 = 16000, Need3 = 24000, BaseCost = 65, LevelCost = 10, MPowerBase = 30, MPowerBonus = 10, PowerBase = 20, PowerBonus = 5, Range = 9 });
            if (!MagicExists(Spell.MagicBooster)) MagicInfoList.Add(new MagicInfo { Name = "深延术", Spell = Spell.MagicBooster, Icon = 73, Level1 = 47, Level2 = 49, Level3 = 52, Need1 = 12000, Need2 = 18000, Need3 = 24000, BaseCost = 150, LevelCost = 15, Range = 0 });
            if (!MagicExists(Spell.MeteorStrike)) MagicInfoList.Add(new MagicInfo { Name = "天上落焰", Spell = Spell.MeteorStrike, Icon = 52, Level1 = 49, Level2 = 52, Level3 = 55, Need1 = 15000, Need2 = 20000, Need3 = 25000, BaseCost = 115, LevelCost = 17, MPowerBase = 40, MPowerBonus = 10, PowerBase = 20, PowerBonus = 15, Range = 9 });
            if (!MagicExists(Spell.IceThrust)) MagicInfoList.Add(new MagicInfo { Name = "冰焰术", Spell = Spell.IceThrust, Icon = 56, Level1 = 53, Level2 = 56, Level3 = 59, Need1 = 17000, Need2 = 22000, Need3 = 27000, BaseCost = 100, LevelCost = 20, MPowerBase = 100, PowerBase = 50, Range = 0 });
            //if (!MagicExists(Spell.FastMove)) MagicInfoList.Add(new MagicInfo { Name = "FastMove", Spell = Spell.FastMove, Icon = ?, Level1 = ?, Level2 = ?, Level3 = ?, Need1 = ?, Need2 = ?, Need3 = ?, BaseCost = ?, LevelCost = ?, DelayBase = ?, DelayReduction = ? });
            if (!MagicExists(Spell.StormEscape)) MagicInfoList.Add(new MagicInfo { Name = "雷仙风", Spell = Spell.StormEscape, Icon = 81, Level1 = 60, Level2 = 61, Level3 = 62, Need1 = 2200, Need2 = 3300, Need3 = 4400, BaseCost = 65, LevelCost = 8, MPowerBase = 12, PowerBase = 4, DelayBase = 300000, DelayReduction = 40000, Range = 9 , MultiplierBase = 1f, MultiplierBonus = 0f});
            if (!MagicExists(Spell.HeavenlySecrets)) MagicInfoList.Add(new MagicInfo { Name = "天上秘术", Spell = Spell.HeavenlySecrets, Icon = 77, Level1 = 50, Level2 = 63, Level3 = 56, Need1 = 1000, Need2 = 2000, Need3 = 3500, BaseCost = 28, LevelCost = 2, MPowerBase = 1, PowerBase = 1, DelayBase = 600000, DelayReduction = 100000, Range = 0 , MultiplierBase = 1f, MultiplierBonus = 0f});
            if (!MagicExists(Spell.GreatFireBallRare)) MagicInfoList.Add(new MagicInfo { Name = "大火球-秘籍", Spell = Spell.GreatFireBallRare, Icon = 108, Level1 = 55, Level2 = 60, Level3 = 65, Need1 = 17000, Need2 = 22000, Need3 = 27000, BaseCost = 5, LevelCost = 1, MPowerBase = 15, PowerBase = 18, Range = 9 });
            if (!MagicExists(Spell.StormEscapeRare)) MagicInfoList.Add(new MagicInfo { Name = "雷仙风-秘籍", Spell = Spell.StormEscapeRare, Icon = 85, Level1 = 62, Level2 = 64, Level3 = 66, Need1 = 2200, Need2 = 3300, Need3 = 4400, BaseCost = 65, LevelCost = 8, MPowerBase = 30, PowerBase = 10, DelayBase = 300000, DelayReduction = 40000, Range = 9 , MultiplierBase = 3.25f, MultiplierBonus = 0.25f});
            
            
            //Taoist
            if (!MagicExists(Spell.Healing)) MagicInfoList.Add(new MagicInfo { Name = "治愈术", Spell = Spell.Healing, Icon = 1, Level1 = 7, Level2 = 11, Level3 = 14, Need1 = 150, Need2 = 350, Need3 = 700, BaseCost = 3, LevelCost = 2, MPowerBase = 14, Range = 9 });
            if (!MagicExists(Spell.SpiritSword)) MagicInfoList.Add(new MagicInfo { Name = "精神力战法", Spell = Spell.SpiritSword, Icon = 3, Level1 = 9, Level2 = 12, Level3 = 15, Need1 = 350, Need2 = 1300, Need3 = 2700, Range = 0 });
            if (!MagicExists(Spell.Poisoning)) MagicInfoList.Add(new MagicInfo { Name = "施毒术", Spell = Spell.Poisoning, Icon = 5, Level1 = 14, Level2 = 17, Level3 = 20, Need1 = 700, Need2 = 1300, Need3 = 2700, BaseCost = 2, LevelCost = 1, Range = 9 });
            if (!MagicExists(Spell.SoulFireBall)) MagicInfoList.Add(new MagicInfo { Name = "灵魂火符", Spell = Spell.SoulFireBall, Icon = 12, Level1 = 18, Level2 = 21, Level3 = 24, Need1 = 1300, Need2 = 2700, Need3 = 4000, BaseCost = 3, LevelCost = 1, MPowerBase = 8, PowerBase = 3, Range = 9 });
            if (!MagicExists(Spell.SummonSkeleton)) MagicInfoList.Add(new MagicInfo { Name = "召唤骷髅", Spell = Spell.SummonSkeleton, Icon = 16, Level1 = 19, Level2 = 22, Level3 = 26, Need1 = 1000, Need2 = 2000, Need3 = 3500, BaseCost = 12, LevelCost = 4, Range = 0 });
            if (!MagicExists(Spell.Hiding)) MagicInfoList.Add(new MagicInfo { Name = "隐身术", Spell = Spell.Hiding, Icon = 17, Level1 = 20, Level2 = 23, Level3 = 26, Need1 = 1300, Need2 = 2700, Need3 = 5300, BaseCost = 1, LevelCost = 1, Range = 0 });
            if (!MagicExists(Spell.MassHiding)) MagicInfoList.Add(new MagicInfo { Name = "集体隐身术", Spell = Spell.MassHiding, Icon = 18, Level1 = 21, Level2 = 25, Level3 = 29, Need1 = 1300, Need2 = 2700, Need3 = 5300, BaseCost = 2, LevelCost = 2, Range = 9 });
            if (!MagicExists(Spell.SoulShield)) MagicInfoList.Add(new MagicInfo { Name = "幽灵盾", Spell = Spell.SoulShield, Icon = 13, Level1 = 22, Level2 = 24, Level3 = 26, Need1 = 2000, Need2 = 3500, Need3 = 7000, BaseCost = 2, LevelCost = 2, Range = 9 });
            if (!MagicExists(Spell.Revelation)) MagicInfoList.Add(new MagicInfo { Name = "心灵启示", Spell = Spell.Revelation, Icon = 27, Level1 = 23, Level2 = 25, Level3 = 28, Need1 = 1500, Need2 = 2500, Need3 = 4000, BaseCost = 4, LevelCost = 4, Range = 9 });
            if (!MagicExists(Spell.BlessedArmour)) MagicInfoList.Add(new MagicInfo { Name = "神圣战甲术", Spell = Spell.BlessedArmour, Icon = 14, Level1 = 25, Level2 = 27, Level3 = 29, Need1 = 4000, Need2 = 6000, Need3 = 10000, BaseCost = 2, LevelCost = 2, Range = 9 });
            if (!MagicExists(Spell.EnergyRepulsor)) MagicInfoList.Add(new MagicInfo { Name = "气功波", Spell = Spell.EnergyRepulsor, Icon = 36, Level1 = 27, Level2 = 29, Level3 = 31, Need1 = 1800, Need2 = 2400, Need3 = 3200, BaseCost = 2, LevelCost = 2, Range = 0, MPowerBase = 4 });
            if (!MagicExists(Spell.TrapHexagon)) MagicInfoList.Add(new MagicInfo { Name = "困魔咒", Spell = Spell.TrapHexagon, Icon = 15, Level1 = 28, Level2 = 30, Level3 = 32, Need1 = 2500, Need2 = 5000, Need3 = 10000, BaseCost = 7, LevelCost = 3, Range = 9 });
            if (!MagicExists(Spell.Purification)) MagicInfoList.Add(new MagicInfo { Name = "净化术", Spell = Spell.Purification, Icon = 39, Level1 = 30, Level2 = 32, Level3 = 35, Need1 = 3000, Need2 = 5000, Need3 = 8000, BaseCost = 14, LevelCost = 2, Range = 9 });
            if (!MagicExists(Spell.MassHealing)) MagicInfoList.Add(new MagicInfo { Name = "群体治疗术", Spell = Spell.MassHealing, Icon = 28, Level1 = 31, Level2 = 33, Level3 = 36, Need1 = 2000, Need2 = 4000, Need3 = 8000, BaseCost = 28, LevelCost = 3, MPowerBase = 10, PowerBase = 4, Range = 9 });
            if (!MagicExists(Spell.Hallucination)) MagicInfoList.Add(new MagicInfo { Name = "迷魂术", Spell = Spell.Hallucination, Icon = 48, Level1 = 31, Level2 = 34, Level3 = 36, Need1 = 4000, Need2 = 6000, Need3 = 9000, BaseCost = 22, LevelCost = 10, Range = 9 });
            if (!MagicExists(Spell.UltimateEnhancer)) MagicInfoList.Add(new MagicInfo { Name = "无极真气", Spell = Spell.UltimateEnhancer, Icon = 35, Level1 = 33, Level2 = 35, Level3 = 38, Need1 = 5000, Need2 = 7000, Need3 = 10000, BaseCost = 28, LevelCost = 4, Range = 9 });
            if (!MagicExists(Spell.SummonShinsu)) MagicInfoList.Add(new MagicInfo { Name = "召唤神兽", Spell = Spell.SummonShinsu, Icon = 29, Level1 = 35, Level2 = 37, Level3 = 40, Need1 = 2000, Need2 = 4000, Need3 = 6000, BaseCost = 28, LevelCost = 4, Range = 0 });
            if (!MagicExists(Spell.Reincarnation)) MagicInfoList.Add(new MagicInfo { Name = "苏生术", Spell = Spell.Reincarnation, Icon = 53, Level1 = 37, Level2 = 39, Level3 = 41, Need1 = 2000, Need2 = 6000, Need3 = 10000, BaseCost = 125, LevelCost = 17, Range = 9 });
            if (!MagicExists(Spell.SummonHolyDeva)) MagicInfoList.Add(new MagicInfo { Name = "精魂召唤术", Spell = Spell.SummonHolyDeva, Icon = 40, Level1 = 38, Level2 = 41, Level3 = 43, Need1 = 4000, Need2 = 6000, Need3 = 9000, BaseCost = 28, LevelCost = 4, Range = 0 });
            if (!MagicExists(Spell.Curse)) MagicInfoList.Add(new MagicInfo { Name = "诅咒术", Spell = Spell.Curse, Icon = 45, Level1 = 40, Level2 = 42, Level3 = 44, Need1 = 4000, Need2 = 6000, Need3 = 9000, BaseCost = 17, LevelCost = 3, Range = 9, MPowerBase = 20 });
            if (!MagicExists(Spell.Plague)) MagicInfoList.Add(new MagicInfo { Name = "烦脑", Spell = Spell.Plague, Icon = 74, Level1 = 42, Level2 = 44, Level3 = 47, Need1 = 5000, Need2 = 9000, Need3 = 13000, BaseCost = 20, LevelCost = 5, Range = 9 });
            if (!MagicExists(Spell.PoisonCloud)) MagicInfoList.Add(new MagicInfo { Name = "毒雾", Spell = Spell.PoisonCloud, Icon = 54, Level1 = 43, Level2 = 45, Level3 = 48, Need1 = 4000, Need2 = 8000, Need3 = 12000, BaseCost = 30, LevelCost = 5, MPowerBase = 40, PowerBase = 20, DelayBase = 18000, DelayReduction = 2000, Range = 9 });
            if (!MagicExists(Spell.EnergyShield)) MagicInfoList.Add(new MagicInfo { Name = "先天气功", Spell = Spell.EnergyShield, Icon = 57, Level1 = 48, Level2 = 51, Level3 = 54, Need1 = 5000, Need2 = 9000, Need3 = 13000, BaseCost = 50, LevelCost = 20, Range = 9 });
            if (!MagicExists(Spell.PetEnhancer)) MagicInfoList.Add(new MagicInfo { Name = "血龙兽", Spell = Spell.PetEnhancer, Icon = 78, Level1 = 45, Level2 = 48, Level3 = 51, Need1 = 4000, Need2 = 8000, Need3 = 12000, BaseCost = 30, LevelCost = 40, Range = 0 });
            if (!MagicExists(Spell.HealingCircle)) MagicInfoList.Add(new MagicInfo { Name = "阴阳五行阵", Spell = Spell.HealingCircle, Icon = 82, Level1 = 39, Level2 = 41, Level3 = 43, Need1 = 7000, Need2 = 12000, Need3 = 15000, BaseCost = 10, LevelCost = 100 });
			if (!MagicExists(Spell.HealingRare)) MagicInfoList.Add(new MagicInfo { Name = "治愈术-秘籍", Spell = Spell.HealingRare, Icon = 109, Level1 = 55, Level2 = 60, Level3 = 65, Need1 = 17000, Need2 = 22000, Need3 = 27000, BaseCost = 3, LevelCost = 2, MPowerBase = 14, Range = 9 });
            if (!MagicExists(Spell.HealingcircleRare)) MagicInfoList.Add(new MagicInfo { Name = "阴阳五行阵-秘籍", Spell = Spell.HealingcircleRare, Icon = 86, Level1 = 60, Level2 = 61, Level3 = 61, Need1 = 4400, Need2 = 7400, Need3 = 11700, BaseCost = 28, LevelCost = 3, DelayBase = 1800, DelayReduction = 0 });
            if (!MagicExists(Spell.PetEnhancerRare)) MagicInfoList.Add(new MagicInfo { Name = "血龙兽-秘籍", Spell = Spell.PetEnhancerRare, Icon = 115, Level1 = 95, Level2 = 97, Level3 = 102, Need1 = 23600, Need2 = 38900, Need3 = 57900, BaseCost = 12, LevelCost = 4, Range = 0 });
            if (!MagicExists(Spell.MultipleEffects)) MagicInfoList.Add(new MagicInfo { Name = "万效符", Spell = Spell.MultipleEffects, Icon = 120, Level1 = 90, Level2 = 92, Level3 = 94, Need1 = 18500, Need2 = 29900, Need3 = 43200, BaseCost = 2, LevelCost = 2, Range = 9 });
            if (!MagicExists(Spell.MultipleEffectsRare)) MagicInfoList.Add(new MagicInfo { Name = "万效符-秘籍", Spell = Spell.MultipleEffectsRare, Icon = 125, Level1 = 100, Level2 = 105, Level3 = 110, Need1 = 23600, Need2 = 38900, Need3 = 57600, BaseCost = 2, LevelCost = 2, Range = 9 });
            
            //Assassin
            if (!MagicExists(Spell.FatalSword)) MagicInfoList.Add(new MagicInfo { Name = "绝命剑法", Spell = Spell.FatalSword, Icon = 58, Level1 = 7, Level2 = 9, Level3 = 12, Need1 = 500, Need2 = 1000, Need3 = 2300, Range = 0 });
            if (!MagicExists(Spell.DoubleSlash)) MagicInfoList.Add(new MagicInfo { Name = "风剑术", Spell = Spell.DoubleSlash, Icon = 59, Level1 = 15, Level2 = 17, Level3 = 19, Need1 = 700, Need2 = 1500, Need3 = 2200, BaseCost = 2, LevelCost = 1 });
            if (!MagicExists(Spell.Haste)) MagicInfoList.Add(new MagicInfo { Name = "体迅风", Spell = Spell.Haste, Icon = 60, Level1 = 20, Level2 = 22, Level3 = 25, Need1 = 2000, Need2 = 3000, Need3 = 6000, BaseCost = 3, LevelCost = 2, Range = 0 });
            if (!MagicExists(Spell.FlashDash)) MagicInfoList.Add(new MagicInfo { Name = "拔刀术", Spell = Spell.FlashDash, Icon = 61, Level1 = 25, Level2 = 27, Level3 = 30, Need1 = 4000, Need2 = 7000, Need3 = 9000, BaseCost = 12, LevelCost = 2, DelayBase = 200, Range = 0 });
            if (!MagicExists(Spell.LightBody)) MagicInfoList.Add(new MagicInfo { Name = "风身术", Spell = Spell.LightBody, Icon = 68, Level1 = 27, Level2 = 29, Level3 = 32, Need1 = 5000, Need2 = 7000, Need3 = 10000, BaseCost = 11, LevelCost = 2, Range = 0 });
            if (!MagicExists(Spell.HeavenlySword)) MagicInfoList.Add(new MagicInfo { Name = "迁移剑", Spell = Spell.HeavenlySword, Icon = 62, Level1 = 30, Level2 = 32, Level3 = 35, Need1 = 4000, Need2 = 8000, Need3 = 10000, BaseCost = 13, LevelCost = 2, MPowerBase = 8, Range = 0 });
            if (!MagicExists(Spell.FireBurst)) MagicInfoList.Add(new MagicInfo { Name = "烈风击", Spell = Spell.FireBurst, Icon = 63, Level1 = 33, Level2 = 35, Level3 = 38, Need1 = 4000, Need2 = 6000, Need3 = 8000, BaseCost = 10, LevelCost = 1, Range = 0 });
            if (!MagicExists(Spell.Trap)) MagicInfoList.Add(new MagicInfo { Name = "捕缚术", Spell = Spell.Trap, Icon = 64, Level1 = 33, Level2 = 35, Level3 = 38, Need1 = 2000, Need2 = 4000, Need3 = 6000, BaseCost = 14, LevelCost = 2, DelayBase = 60000, DelayReduction = 15000, Range = 9 });
            if (!MagicExists(Spell.PoisonSword)) MagicInfoList.Add(new MagicInfo { Name = "猛毒剑气", Spell = Spell.PoisonSword, Icon = 69, Level1 = 34, Level2 = 36, Level3 = 39, Need1 = 5000, Need2 = 8000, Need3 = 11000, BaseCost = 14, LevelCost = 3, Range = 0 });
            if (!MagicExists(Spell.MoonLight)) MagicInfoList.Add(new MagicInfo { Name = "月影术", Spell = Spell.MoonLight, Icon = 65, Level1 = 36, Level2 = 39, Level3 = 42, Need1 = 3000, Need2 = 5000, Need3 = 8000, BaseCost = 36, LevelCost = 3, Range = 0 });
            if (!MagicExists(Spell.MPEater)) MagicInfoList.Add(new MagicInfo { Name = "吸气", Spell = Spell.MPEater, Icon = 66, Level1 = 38, Level2 = 41, Level3 = 44, Need1 = 5000, Need2 = 8000, Need3 = 11000, Range = 0 });
            if (!MagicExists(Spell.SwiftFeet)) MagicInfoList.Add(new MagicInfo { Name = "轻身步", Spell = Spell.SwiftFeet, Icon = 67, Level1 = 40, Level2 = 43, Level3 = 46, Need1 = 4000, Need2 = 6000, Need3 = 9000, BaseCost = 17, LevelCost = 5, DelayBase = 210000, DelayReduction = 40000, Range = 0 });
            if (!MagicExists(Spell.DarkBody)) MagicInfoList.Add(new MagicInfo { Name = "烈火身", Spell = Spell.DarkBody, Icon = 70, Level1 = 46, Level2 = 49, Level3 = 52, Need1 = 6000, Need2 = 10000, Need3 = 14000, BaseCost = 40, LevelCost = 7, Range = 0 });
            if (!MagicExists(Spell.Hemorrhage)) MagicInfoList.Add(new MagicInfo { Name = "血风击", Spell = Spell.Hemorrhage, Icon = 75, Level1 = 47, Level2 = 51, Level3 = 55, Need1 = 9000, Need2 = 15000, Need3 = 21000, Range = 0 });
            if (!MagicExists(Spell.CrescentSlash)) MagicInfoList.Add(new MagicInfo { Name = "月华乱舞", Spell = Spell.CrescentSlash, Icon = 71, Level1 = 50, Level2 = 53, Level3 = 56, Need1 = 12000, Need2 = 16000, Need3 = 24000, BaseCost = 19, LevelCost = 3, Range = 0 });
            if (!MagicExists(Spell.MoonMist)) MagicInfoList.Add(new MagicInfo { Name = "月影雾", Spell = Spell.MoonMist, Icon = 83, Level1 = 60, Level2 = 61, Level3 = 62, Need1 = 3650, Need2 = 5950, Need3 = 8800, BaseCost = 30, LevelCost = 5, DelayBase = 180000, DelayReduction = 30000 });
            if (!MagicExists(Spell.CatTongue)) MagicInfoList.Add(new MagicInfo { Name = "猫舌兰", Spell = Spell.CatTongue, Icon = 79, Level1 = 48, Level2 = 51, Level3 = 53, Need1 = 2000, Need2 = 4000, Need3 = 7000, BaseCost = 30, LevelCost = 5, DelayBase = 360000, DelayReduction = 60000 });

            //Archer
            if (!MagicExists(Spell.Focus)) MagicInfoList.Add(new MagicInfo { Name = "必中闪", Spell = Spell.Focus, Icon = 88, Level1 = 7, Level2 = 13, Level3 = 17, Need1 = 270, Need2 = 600, Need3 = 1300, Range = 0 });
            if (!MagicExists(Spell.StraightShot)) MagicInfoList.Add(new MagicInfo { Name = "天日闪", Spell = Spell.StraightShot, Icon = 89, Level1 = 9, Level2 = 12, Level3 = 16, Need1 = 350, Need2 = 750, Need3 = 1400, BaseCost = 3, LevelCost = 2, MPowerBase = 8, PowerBase = 3, Range = 9 });
            if (!MagicExists(Spell.DoubleShot)) MagicInfoList.Add(new MagicInfo { Name = "无我闪", Spell = Spell.DoubleShot, Icon = 90, Level1 = 14, Level2 = 18, Level3 = 21, Need1 = 700, Need2 = 1500, Need3 = 2100, BaseCost = 3, LevelCost = 2, MPowerBase = 6, PowerBase = 2, Range = 9 });
            if (!MagicExists(Spell.ExplosiveTrap)) MagicInfoList.Add(new MagicInfo { Name = "爆阱", Spell = Spell.ExplosiveTrap, Icon = 91, Level1 = 22, Level2 = 25, Level3 = 30, Need1 = 2000, Need2 = 3500, Need3 = 5000, BaseCost = 10, LevelCost = 3, MPowerBase = 15, PowerBase = 15, Range = 0 });
            if (!MagicExists(Spell.DelayedExplosion)) MagicInfoList.Add(new MagicInfo { Name = "爆闪", Spell = Spell.DelayedExplosion, Icon = 92, Level1 = 31, Level2 = 34, Level3 = 39, Need1 = 3000, Need2 = 7000, Need3 = 10000, BaseCost = 8, LevelCost = 2, MPowerBase = 30, PowerBase = 15, Range = 9 });
            if (!MagicExists(Spell.Meditation)) MagicInfoList.Add(new MagicInfo { Name = "气功术", Spell = Spell.Meditation, Icon = 93, Level1 = 19, Level2 = 24, Level3 = 29, Need1 = 1800, Need2 = 2600, Need3 = 5600, BaseCost = 8, LevelCost = 2, Range = 0 });
            if (!MagicExists(Spell.ElementalShot)) MagicInfoList.Add(new MagicInfo { Name = "万金闪", Spell = Spell.ElementalShot, Icon = 94, Level1 = 20, Level2 = 25, Level3 = 31, Need1 = 1800, Need2 = 2700, Need3 = 6000, BaseCost = 8, LevelCost = 2, MPowerBase = 6, PowerBase = 3, Range = 9 });
            if (!MagicExists(Spell.Concentration)) MagicInfoList.Add(new MagicInfo { Name = "气流术", Spell = Spell.Concentration, Icon = 96, Level1 = 23, Level2 = 27, Level3 = 32, Need1 = 2100, Need2 = 3800, Need3 = 6500, BaseCost = 8, LevelCost = 2, Range = 0 });
            if (!MagicExists(Spell.ElementalBarrier)) MagicInfoList.Add(new MagicInfo { Name = "金刚术", Spell = Spell.ElementalBarrier, Icon = 98, Level1 = 33, Level2 = 38, Level3 = 44, Need1 = 3000, Need2 = 7000, Need3 = 10000, BaseCost = 10, LevelCost = 2, MPowerBase = 15, PowerBase = 5, Range = 0 });
            if (!MagicExists(Spell.BackStep)) MagicInfoList.Add(new MagicInfo { Name = "风弹步", Spell = Spell.BackStep, Icon = 95, Level1 = 30, Level2 = 34, Level3 = 38, Need1 = 2400, Need2 = 3000, Need3 = 6000, BaseCost = 12, LevelCost = 2, DelayBase = 2500, Range = 0 });
            if (!MagicExists(Spell.BindingShot)) MagicInfoList.Add(new MagicInfo { Name = "困兽笼", Spell = Spell.BindingShot, Icon = 46, Level1 = 35, Level2 = 39, Level3 = 42, Need1 = 400, Need2 = 7000, Need3 = 9500, BaseCost = 7, LevelCost = 3, Range = 9 });
            if (!MagicExists(Spell.Stonetrap)) MagicInfoList.Add(new MagicInfo { Name = "地柱钉", Spell = Spell.Stonetrap, Icon = 97, Level1 = 40, Level2 = 43, Level3 = 46, Need1 = 4900, Need2 = 9800, Need3 = 14100, BaseCost = 7, LevelCost = 3, Range = 9 });
            if (!MagicExists(Spell.SummonVampire)) MagicInfoList.Add(new MagicInfo { Name = "吸血地精", Spell = Spell.SummonVampire, Icon = 99, Level1 = 28, Level2 = 33, Level3 = 41, Need1 = 2000, Need2 = 2700, Need3 = 7500, BaseCost = 10, LevelCost = 5, Range = 9 });
            if (!MagicExists(Spell.VampireShot)) MagicInfoList.Add(new MagicInfo { Name = "吸血地闪", Spell = Spell.VampireShot, Icon = 100, Level1 = 26, Level2 = 32, Level3 = 36, Need1 = 3000, Need2 = 6000, Need3 = 12000, BaseCost = 12, LevelCost = 3, MPowerBase = 10, PowerBase = 7, Range = 9 });
            if (!MagicExists(Spell.SummonToad)) MagicInfoList.Add(new MagicInfo { Name = "痹魔阱", Spell = Spell.SummonToad, Icon = 101, Level1 = 37, Level2 = 43, Level3 = 47, Need1 = 5800, Need2 = 10000, Need3 = 13000, BaseCost = 10, LevelCost = 5, Range = 9 });
            if (!MagicExists(Spell.PoisonShot)) MagicInfoList.Add(new MagicInfo { Name = "毒魔闪", Spell = Spell.PoisonShot, Icon = 102, Level1 = 40, Level2 = 45, Level3 = 49, Need1 = 6000, Need2 = 14000, Need3 = 16000, BaseCost = 10, LevelCost = 4, MPowerBase = 10, PowerBase = 10, Range = 9 });
            if (!MagicExists(Spell.CrippleShot)) MagicInfoList.Add(new MagicInfo { Name = "邪爆闪", Spell = Spell.CrippleShot, Icon = 103, Level1 = 43, Level2 = 47, Level3 = 50, Need1 = 12000, Need2 = 15000, Need3 = 18000, BaseCost = 15, LevelCost = 3, MPowerBase = 10, MPowerBonus = 20, PowerBase = 10, Range = 9 });
            if (!MagicExists(Spell.SummonSnakes)) MagicInfoList.Add(new MagicInfo { Name = "蛇柱阱", Spell = Spell.SummonSnakes, Icon = 104, Level1 = 46, Level2 = 51, Level3 = 54, Need1 = 14000, Need2 = 17000, Need3 = 20000, BaseCost = 10, LevelCost = 5, Range = 9 });
            if (!MagicExists(Spell.NapalmShot)) MagicInfoList.Add(new MagicInfo { Name = "血龙闪", Spell = Spell.NapalmShot, Icon = 105, Level1 = 48, Level2 = 52, Level3 = 55, Need1 = 15000, Need2 = 18000, Need3 = 21000, BaseCost = 40, LevelCost = 10, MPowerBase = 25, MPowerBonus = 25, PowerBase = 25, Range = 9 });
            if (!MagicExists(Spell.OneWithNature)) MagicInfoList.Add(new MagicInfo { Name = "血龙闪-秘籍", Spell = Spell.OneWithNature, Icon = 106, Level1 = 50, Level2 = 53, Level3 = 56, Need1 = 17000, Need2 = 19000, Need3 = 24000, BaseCost = 80, LevelCost = 15, MPowerBase = 75, MPowerBonus = 35, PowerBase = 30, PowerBonus = 20, Range = 9 });
            if (!MagicExists(Spell.MentalState)) MagicInfoList.Add(new MagicInfo { Name = "精神状态", Spell = Spell.MentalState, Icon = 81, Level1 = 11, Level2 = 15, Level3 = 22, Need1 = 500, Need2 = 900, Need3 = 1800, BaseCost = 1, LevelCost = 1, Range = 0 });

            //Custom
            if (!MagicExists(Spell.Blink)) MagicInfoList.Add(new MagicInfo { Name = "时光涌动", Spell = Spell.Blink, Icon = 20, Level1 = 19, Level2 = 22, Level3 = 25, Need1 = 350, Need2 = 1000, Need3 = 2000, BaseCost = 10, LevelCost = 3, Range = 9 });
            if (!MagicExists(Spell.Portal)) MagicInfoList.Add(new MagicInfo { Name = "传送门", Spell = Spell.Portal, Icon = 1, Level1 = 7, Level2 = 11, Level3 = 14, Need1 = 150, Need2 = 350, Need3 = 700, BaseCost = 3, LevelCost = 2, Range = 9 });
            if (!MagicExists(Spell.BattleCry)) MagicInfoList.Add(new MagicInfo {  Name = "呐喊", Spell = Spell.BattleCry, Icon = 42, Level1 = 48, Level2 = 51, Level3 = 55, Need1 = 8000, Need2 = 11000, Need3 = 15000, BaseCost = 22, LevelCost = 10, Range = 0 });
            if (!MagicExists(Spell.FireBounce)) MagicInfoList.Add(new MagicInfo { Name = "火焰跳动", Spell = Spell.FireBounce, Icon = 4, Level1 = 15, Level2 = 18, Level3 = 21, Need1 = 2000, Need2 = 2700, Need3 = 3500, BaseCost = 5, LevelCost = 1, MPowerBase = 6, PowerBase = 10, Range = 9 });
            if (!MagicExists(Spell.MeteorShower)) MagicInfoList.Add(new MagicInfo { Name = "火流星", Spell = Spell.MeteorShower, Icon = 4, Level1 = 15, Level2 = 18, Level3 = 21, Need1 = 2000, Need2 = 2700, Need3 = 3500, BaseCost = 5, LevelCost = 1, MPowerBase = 6, PowerBase = 10, Range = 9 });
        }

        private string CanStartEnvir()
        {
            if (StartPoints.Count == 0) return "尚未设置角色出生点，无法启动服务器";

            if (Settings.EnforceDBChecks)
            {
                if (GetMonsterInfo(Settings.SkeletonName, true) == null) return "缺少必要怪物无法启动服务器 " + Settings.SkeletonName;
                if (GetMonsterInfo(Settings.ShinsuName, true) == null) return "缺少必要怪物无法启动服务器 " + Settings.ShinsuName;
                if (GetMonsterInfo(Settings.BugBatName, true) == null) return "缺少必要怪物无法启动服务器 " + Settings.BugBatName;
                if (GetMonsterInfo(Settings.Zuma1, true) == null) return "缺少必要怪物无法启动服务器 " + Settings.Zuma1;
                if (GetMonsterInfo(Settings.Zuma2, true) == null) return "缺少必要怪物无法启动服务器 " + Settings.Zuma2;
                if (GetMonsterInfo(Settings.Zuma3, true) == null) return "缺少必要怪物无法启动服务器 " + Settings.Zuma3;
                if (GetMonsterInfo(Settings.Zuma4, true) == null) return "缺少必要怪物无法启动服务器 " + Settings.Zuma4;
                if (GetMonsterInfo(Settings.Zuma5, true) == null) return "缺少必要怪物无法启动服务器 " + Settings.Zuma5;
                if (GetMonsterInfo(Settings.Zuma6, true) == null) return "缺少必要怪物无法启动服务器 " + Settings.Zuma6;
                if (GetMonsterInfo(Settings.Zuma7, true) == null) return "缺少必要怪物无法启动服务器 " + Settings.Zuma7;
                if (GetMonsterInfo(Settings.Turtle1, true) == null) return "缺少必要怪物无法启动服务器 " + Settings.Turtle1;
                if (GetMonsterInfo(Settings.Turtle2, true) == null) return "缺少必要怪物无法启动服务器 " + Settings.Turtle2;
                if (GetMonsterInfo(Settings.Turtle3, true) == null) return "缺少必要怪物无法启动服务器 " + Settings.Turtle3;
                if (GetMonsterInfo(Settings.Turtle4, true) == null) return "缺少必要怪物无法启动服务器 " + Settings.Turtle4;
                if (GetMonsterInfo(Settings.Turtle5, true) == null) return "缺少必要怪物无法启动服务器 " + Settings.Turtle5;
                if (GetMonsterInfo(Settings.BoneMonster1, true) == null) return "缺少必要怪物无法启动服务器 " + Settings.BoneMonster1;
                if (GetMonsterInfo(Settings.BoneMonster2, true) == null) return "缺少必要怪物无法启动服务器 " + Settings.BoneMonster2;
                if (GetMonsterInfo(Settings.BoneMonster3, true) == null) return "缺少必要怪物无法启动服务器 " + Settings.BoneMonster3;
                if (GetMonsterInfo(Settings.BoneMonster4, true) == null) return "缺少必要怪物无法启动服务器 " + Settings.BoneMonster4;
                if (GetMonsterInfo(Settings.BehemothMonster1, true) == null) return "缺少必要怪物无法启动服务器 " + Settings.BehemothMonster1;
                if (GetMonsterInfo(Settings.BehemothMonster2, true) == null) return "缺少必要怪物无法启动服务器 " + Settings.BehemothMonster2;
                if (GetMonsterInfo(Settings.BehemothMonster3, true) == null) return "缺少必要怪物无法启动服务器 " + Settings.BehemothMonster3;
                if (GetMonsterInfo(Settings.HellKnight1, true) == null) return "缺少必要怪物无法启动服务器 " + Settings.HellKnight1;
                if (GetMonsterInfo(Settings.HellKnight2, true) == null) return "缺少必要怪物无法启动服务器 " + Settings.HellKnight2;
                if (GetMonsterInfo(Settings.HellKnight3, true) == null) return "缺少必要怪物无法启动服务器 " + Settings.HellKnight3;
                if (GetMonsterInfo(Settings.HellKnight4, true) == null) return "缺少必要怪物无法启动服务器 " + Settings.HellKnight4;
                if (GetMonsterInfo(Settings.HellBomb1, true) == null) return "缺少必要怪物无法启动服务器 " + Settings.HellBomb1;
                if (GetMonsterInfo(Settings.HellBomb2, true) == null) return "缺少必要怪物无法启动服务器 " + Settings.HellBomb2;
                if (GetMonsterInfo(Settings.HellBomb3, true) == null) return "缺少必要怪物无法启动服务器 " + Settings.HellBomb3;
                if (GetMonsterInfo(Settings.WhiteSnake, true) == null) return "缺少必要怪物无法启动服务器 " + Settings.WhiteSnake;
                if (GetMonsterInfo(Settings.AngelName, true) == null) return "缺少必要怪物无法启动服务器 " + Settings.AngelName;
                if (GetMonsterInfo(Settings.BombSpiderName, true) == null) return "缺少必要怪物无法启动服务器 " + Settings.BombSpiderName;
                if (GetMonsterInfo(Settings.CloneName, true) == null) return "缺少必要怪物无法启动服务器 " + Settings.CloneName;
                if (GetMonsterInfo(Settings.AssassinCloneName, true) == null) return "缺少必要怪物无法启动服务器 " + Settings.AssassinCloneName;
                if (GetMonsterInfo(Settings.VampireName, true) == null) return "缺少必要怪物无法启动服务器 " + Settings.VampireName;
                if (GetMonsterInfo(Settings.ToadName, true) == null) return "缺少必要怪物无法启动服务器 " + Settings.ToadName;
                if (GetMonsterInfo(Settings.SnakeTotemName, true) == null) return "缺少必要怪物无法启动服务器 " + Settings.SnakeTotemName;
                if (GetMonsterInfo(Settings.StoneName, true) == null) return "缺少必要怪物无法启动服务器 " + Settings.StoneName;
                if (GetMonsterInfo(Settings.FishingMonster, true) == null) return "缺少必要怪物无法启动服务器 " + Settings.FishingMonster;
                if (GetMonsterInfo(Settings.GeneralMeowMeowMob1, true) == null) return "缺少必要怪物无法启动服务器 " + Settings.GeneralMeowMeowMob1;
                if (GetMonsterInfo(Settings.GeneralMeowMeowMob2, true) == null) return "缺少必要怪物无法启动服务器 " + Settings.GeneralMeowMeowMob2;
                if (GetMonsterInfo(Settings.GeneralMeowMeowMob3, true) == null) return "缺少必要怪物无法启动服务器 " + Settings.GeneralMeowMeowMob3;
                if (GetMonsterInfo(Settings.GeneralMeowMeowMob4, true) == null) return "缺少必要怪物无法启动服务器 " + Settings.GeneralMeowMeowMob4;
                if (GetMonsterInfo(Settings.KingHydraxMob, true) == null) return "缺少必要怪物无法启动服务器 " + Settings.KingHydraxMob;
                if (GetMonsterInfo(Settings.Mon409BMob1, true) == null) return "缺少必要怪物无法启动服务器 " + Settings.Mon409BMob1;
                if (GetMonsterInfo(Settings.Mon409BMob2, true) == null) return "缺少必要怪物无法启动服务器 " + Settings.Mon409BMob2;
                if (GetMonsterInfo(Settings.Mon409BBombMob, true) == null) return "缺少必要怪物无法启动服务器 " + Settings.Mon409BBombMob;
                if (GetMonsterInfo(Settings.SnowWolfKingMob, true) == null) return "缺少必要怪物无法启动服务器 " + Settings.SnowWolfKingMob;
                if (GetMonsterInfo(Settings.CallScrollMob, true) == null) return "缺少必要怪物无法启动服务器 " + Settings.CallScrollMob;
                if (GetMonsterInfo(Settings.ShardMaidenMob1, true) == null) return "缺少必要怪物无法启动服务器 " + Settings.ShardMaidenMob1;
                if (GetMonsterInfo(Settings.ShardMaidenMob2, true) == null) return "缺少必要怪物无法启动服务器 " + Settings.ShardMaidenMob2;
                if (GetMonsterInfo(Settings.ShardMaidenMob3, true) == null) return "缺少必要怪物无法启动服务器 " + Settings.ShardMaidenMob3;
                if (GetMonsterInfo(Settings.ShardMaidenMob4, true) == null) return "缺少必要怪物无法启动服务器 " + Settings.ShardMaidenMob4;
                if (GetMonsterInfo(Settings.Mon570BMob, true) == null) return "缺少必要怪物无法启动服务器 " + Settings.Mon570BMob;
                if (GetMonsterInfo(Settings.Mon573BMob, true) == null) return "缺少必要怪物无法启动服务器 " + Settings.Mon573BMob;
                if (GetMonsterInfo(Settings.Mon580BMob, true) == null) return "缺少必要怪物无法启动服务器 " + Settings.Mon580BMob;
                if (GetMonsterInfo(Settings.Mon603BMob, true) == null) return "缺少必要怪物无法启动服务器 " + Settings.Mon603BMob;

                if (GetItemInfo(Settings.RefineOreName) == null) return "缺少精炼所需矿石无法启动服务器" + Settings.RefineOreName;
            }

            WorldMapIcon wmi = ValidateWorldMap();
            if (wmi != null)
                return $"无效的世界地图目录: {wmi.MapIndex} ({wmi.Title})";


            //add intelligent creature checks?

            return "true";
        }

        private void TryAutoSave(SqlSaveDomain domain, Action action)
        {
            if (action == null) return;

            if (SqlSaveResilience.ShouldSkipAutoSave(domain, out _))
                return;

            try
            {
                action();
            }
            catch (Exception ex)
            {
                SqlSaveResilience.ReportFailure(Persistence.Provider, domain, ex, operation: "AutoSave");
            }
        }

        private void WorkLoop()
        {
            try
            {
                Volatile.Write(ref _mainThreadId, Thread.CurrentThread.ManagedThreadId);
                Time = Stopwatch.ElapsedMilliseconds;

                var conTime = Time;
                var saveTime = Time + Settings.SaveDelay * Settings.Minute;
                var userTime = Time + Settings.Minute * 5;
                var lineMessageTime = Time + Settings.Minute * Settings.LineMessageTimer;
                var processTime = Time + 1000;
                var startTime = Time;

                var processCount = 0;
                var processRealCount = 0;

                LinkedListNode<MapObject> current = null;

                if (Settings.Multithreaded)
                {
                    for (var j = 0; j < MobThreads.Length; j++)
                    {
                        MobThreads[j] = new MobThread();
                        MobThreads[j].Id = j;
                    }
                }

                StartEnvir();
                var canstartserver = CanStartEnvir();
                if (canstartserver != "true")
                {
                    MessageQueue.Enqueue(canstartserver);
                    StopEnvir();
                    _thread = null;
                    Volatile.Write(ref _mainThreadId, 0);
                    Stop();
                    return;
                }

                if (Settings.Multithreaded)
                {
                    for (var j = 0; j < MobThreads.Length; j++)
                    {
                        var Info = MobThreads[j];
                        if (j <= 0) continue;
                        MobThreading[j] = new Thread(() => ThreadLoop(Info)) { IsBackground = true };
                        MobThreading[j].Start();
                    }
                }

                StartNetwork();
                if (Settings.StartHTTPService)
                {
                    http = new HttpServer();
                    http.Start();
                }
                try
                {
                    while (Running)
                    {
                        Time = Stopwatch.ElapsedMilliseconds;
                        ProcessMainThreadQueue();

                        if (Time >= processTime)
                        {
                            LastCount = processCount;
                            LastRealCount = processRealCount;
                            processCount = 0;
                            processRealCount = 0;
                            processTime = Time + 1000;
                        }

                        if (conTime != Time)
                        {
                            conTime = Time;

                            AdjustLights();

                            lock (Connections)
                            {
                                for (var i = Connections.Count - 1; i >= 0; i--)
                                {
                                    Connections[i].Process();
                                }
                            }

                            lock (StatusConnections)
                            {
                                for (var i = StatusConnections.Count - 1; i >= 0; i--)
                                {
                                    StatusConnections[i].Process();
                                }
                            }
                        }


                        if (current == null)
                            current = Objects.First;

                        if (current == Objects.First)
                        {
                            LastRunTime = Time - startTime;
                            startTime = Time;
                        }

                        if (Settings.Multithreaded)
                        {
                            for (var j = 1; j < MobThreads.Length; j++)
                            {
                                var Info = MobThreads[j];

                                if (!Info.Stop) continue;
                                Info.EndTime = Time + 10;
                                Info.Stop = false;
                            }
                            lock (_locker)
                            {
                                Monitor.PulseAll(_locker); //changing a blocking condition. (this makes the threads wake up!)
                            }
                            //run the first loop in the main thread so the main thread automaticaly 'halts' until the other threads are finished
                            ThreadLoop(MobThreads[0]);
                        }

                        var TheEnd = false;
                        var Start = Stopwatch.ElapsedMilliseconds;
                        while (!TheEnd && Stopwatch.ElapsedMilliseconds - Start < 20)
                        {
                            if (current == null)
                            {
                                TheEnd = true;
                                break;
                            }

                            var next = current.Next;
                            if (!Settings.Multithreaded || current.Value.Race != ObjectType.Monster || current.Value.Master != null)
                            {
                                if (Time > current.Value.OperateTime)
                                {
                                    current.Value.Process();
                                    current.Value.SetOperateTime();
                                }
                                processCount++;
                            }
                            current = next;
                        }

                        for (var i = 0; i < MapList.Count; i++)
                            MapList[i].Process();

                        DragonSystem?.Process();

                        Process();

                        var forceAutoSave = Interlocked.Exchange(ref _autoSaveRequested, 0) == 1;

                        if (Time >= saveTime || forceAutoSave)
                        {
                            saveTime = Time + Settings.SaveDelay * Settings.Minute;
                            TryAutoSave(SqlSaveDomain.Accounts, BeginSaveAccounts);
                            TryAutoSave(SqlSaveDomain.Guilds, () => SaveGuilds());
                            TryAutoSave(SqlSaveDomain.Goods, () => SaveGoods());
                            TryAutoSave(SqlSaveDomain.Conquests, () => SaveConquests());
                        }

                        if (Time >= userTime)
                        {
                            userTime = Time + Settings.Minute * 5;
                            Broadcast(new S.Chat
                            {
                                Message = string.Format(GameLanguage.OnlinePlayers, Players.Count),
                                Type = ChatType.Hint
                            });
                        }

                        if (LineMessages.Count > 0 && Time >= lineMessageTime)
                        {
                            lineMessageTime = Time + Settings.Minute * Settings.LineMessageTimer;
                            Broadcast(new S.Chat
                            {
                                Message = LineMessages[Random.Next(LineMessages.Count)],
                                Type = ChatType.LineMessage
                            });
                        }

                        //   if (Players.Count == 0) Thread.Sleep(1);
                        //   GC.Collect();
                    }
                }
                catch (Exception ex)
                {
                    lock (Connections)
                    {
                        for (var i = Connections.Count - 1; i >= 0; i--)
                            Connections[i].SendDisconnect(3);
                    }

                    // Get stack trace for the exception with source file information
                    var st = new StackTrace(ex, true);
                    // Get the top stack frame
                    var frame = st.GetFrame(0);
                    // Get the line number from the stack frame
                    var line = frame.GetFileLineNumber();

                    MessageQueue.Enqueue($"[内循环错误 线程 {line}]" + ex);
                }

                StopNetwork();
                StopEnvir();
                SaveAccounts();
                SaveGuilds(true);
                SaveConquests(true);
            }
            catch (Exception ex)
            {
                try
                {
                    http?.Stop();
                }
                catch
                {
                }

                http = null;

                try
                {
                    StopNetwork();
                }
                catch
                {
                    ReleaseListener(ref _listener);
                    ReleaseListener(ref _StatusPort);
                }

                try
                {
                    StopEnvir();
                }
                catch
                {
                }

                // Get stack trace for the exception with source file information
                var st = new StackTrace(ex, true);
                // Get the top stack frame
                var frame = st.GetFrame(0);
                // Get the line number from the stack frame
                var line = frame.GetFileLineNumber();

                MessageQueue.Enqueue($"[外循环错误 线程 {line}]" + ex);
            }

            Volatile.Write(ref _mainThreadId, 0);
            _thread = null;
        }

        private void ThreadLoop(MobThread Info)
        {
            Info.Stop = false;

            try
            {

                var stopping = false;
                if (Info._current == null)
                    Info._current = Info.ObjectsList.First;
                stopping = Info._current == null;

                while (Running)
                {
                    ProcessMainThreadQueue();
                    if (Info._current == null)
                        Info._current = Info.ObjectsList.First;
                    else
                    {
                        var next = Info._current.Next;

                        //if we reach the end of our list > go back to the top (since we are running threaded, we dont want the system to sit there for xxms doing nothing)
                        if (Info._current == Info.ObjectsList.Last)
                        {
                            next = Info.ObjectsList.First;
                            Info.LastRunTime = (Info.LastRunTime + (Time - Info.StartTime)) / 2;
                            //Info.LastRunTime = (Time - Info.StartTime) /*> 0 ? (Time - Info.StartTime) : Info.LastRunTime */;
                            Info.StartTime = Time;
                        }
                        if (Time > Info._current.Value.OperateTime)
                        {
                            if (Info._current.Value.Master == null) //since we are running multithreaded, dont allow pets to be processed (unless you constantly move pets into their map appropriate thead)
                            {
                                Info._current.Value.Process();
                                Info._current.Value.SetOperateTime();
                            }
                        }
                        Info._current = next;
                    }

                    //if it's the main thread > make it loop till the subthreads are done, else make it stop after 'endtime'
                    if (Info.Id == 0)
                    {
                        stopping = true;
                        for (var x = 1; x < MobThreads.Length; x++)
                        {
                            if (MobThreads[x].Stop == false)
                            {
                                stopping = false;
                            }
                        }
                        if (!stopping) continue;
                        Info.Stop = stopping;
                        return;
                    }

                    if (Stopwatch.ElapsedMilliseconds <= Info.EndTime || !Running) continue;
                    Info.Stop = true;
                    lock (_locker)
                    {
                        while (Info.Stop) Monitor.Wait(_locker);
                    }
                }
            }
            catch (Exception ex)
            {
                if (ex is ThreadInterruptedException) return;

                MessageQueue.Enqueue($"[数据循环处理出现错误]" + ex);
            }
        }

        private void AdjustLights()
        {
            var oldLights = Lights;

            var hours = Now.Hour * 2 % 24;
            if (hours == 6 || hours == 7)
                Lights = LightSetting.Dawn;
            else if (hours >= 8 && hours <= 15)
                Lights = LightSetting.Day;
            else if (hours == 16 || hours == 17)
                Lights = LightSetting.Evening;
            else
                Lights = LightSetting.Night;

            if (oldLights == Lights) return;

            Broadcast(new S.TimeOfDay { Lights = Lights });
        }

        public void Process()
        {
            if (Now.Day != dailyTime)
            {
                dailyTime = Now.Day;
                ProcessNewDay();
            }

            if (Time >= warTime)
            {
                warTime = Time + Settings.Minute;
                for (var i = GuildsAtWar.Count - 1; i >= 0; i--)
                {
                    GuildsAtWar[i].TimeRemaining -= Settings.Minute;

                    if (GuildsAtWar[i].TimeRemaining >= 0) continue;
                    GuildsAtWar[i].EndWar();
                    GuildsAtWar.RemoveAt(i);
                }
            }

            if (Time >= guildTime)
            {
                guildTime = Time + Settings.Minute;
                for (var i = 0; i < Guilds.Count; i++)
                {
                    Guilds[i].Process();
                }
            }

            if (Time >= conquestTime)
            {
                conquestTime = Time + Settings.Second * 10;
                for (var i = 0; i < Conquests.Count; i++)
                {
                    Conquests[i].Process();
                }
            }

            if (Time >= rentalItemsTime)
            {
                rentalItemsTime = Time + Settings.Minute * 5;
                ProcessRentedItems();
            }

            if (Time >= auctionTime)
            {
                auctionTime = Time + Settings.Minute * 10;
                ProcessAuction();
            }

            if (Time >= spawnTime)
            {
                spawnTime = Time + Settings.Second * 10;
                Main.RespawnTick.Process();
            }

            if (Time >= robotTime)
            {
                robotTime = Time + Settings.Minute;
                Robot.Process(RobotNPC);
            }

            if (Time >= timerTime)
            {
                timerTime = Time + Settings.Second;

                string[] keys = Timers.Keys.ToArray();

                foreach (var key in keys)
                {
                    if (!Timers.TryGetValue(key, out var timer)) continue;

                    if (timer.RelativeTime <= Time)
                    {
                        Timers.Remove(key);
                        HandleTimerExpired(key, timer);
                    }
                }
            }
        }

        private void HandleTimerExpired(string fullTimerKey, Timer timer)
        {
            if (string.IsNullOrEmpty(fullTimerKey)) return;
            if (timer == null) return;

            // 全局计时器：来自 NPCSegment 的 global SETTIMER（Key = "_-<key>"）
            if (fullTimerKey.StartsWith("_-", StringComparison.Ordinal))
                return;

            // 玩家计时器：PlayerObject.SetTimer（Key = "<playerName>-<key>"）
            var index = fullTimerKey.IndexOf('-');
            if (index <= 0) return;

            var playerName = fullTimerKey.Substring(0, index);
            var timerKey = index < fullTimerKey.Length - 1 ? fullTimerKey.Substring(index + 1) : string.Empty;

            var player = GetPlayer(playerName);
            if (player == null) return;

            CSharpScripts.TryHandlePlayerTimerExpired(player, timerKey, timer.Type);
        }

        private void ProcessAuction()
        {
            LinkedListNode<AuctionInfo> current = Auctions.First;

            while (current != null)
            {
                AuctionInfo info = current.Value;

                if (!info.Expired && !info.Sold && Now >= info.ConsignmentDate.AddDays(Globals.ConsignmentLength))
                {
                    if (info.ItemType == MarketItemType.Auction && info.CurrentBid > info.Price)
                    {
                        string message = string.Format("你以 {1:#,##0} 金币赢得了 {0}", info.Item.FriendlyName, info.CurrentBid);

                        info.Sold = true;
                        MailCharacter(info.CurrentBuyerInfo, item: info.Item, customMessage: message);

                        MessageAccount(info.CurrentBuyerInfo.AccountInfo, string.Format("你以 {1:#,##0} 金币购买了 {0}", info.Item.FriendlyName, info.CurrentBid), ChatType.Hint);
                        MessageAccount(info.SellerInfo.AccountInfo, string.Format("你以 {1:#,##0} 金币卖出了 {0}", info.Item.FriendlyName, info.CurrentBid), ChatType.Hint);
                    }
                    else
                    {
                        info.Expired = true;
                    }
                }

                current = current.Next;
            }
        }

        public void Broadcast(Packet p)
        {
            for (var i = 0; i < Players.Count; i++) Players[i].Enqueue(p);
        }

        public void RequiresBaseStatUpdate()
        {
            for (var i = 0; i < Players.Count; i++) Players[i].HasUpdatedBaseStats = false;
        }

        public void SaveDB()
        {
            Persistence.SaveWorld(this);
        }

        internal void Legacy_SaveDB()
        {
            using var stream = File.Create(DatabasePath);
            Legacy_SaveDBToStream(stream);
        }

        internal byte[] Legacy_SaveDBToBytes()
        {
            using var mStream = new MemoryStream();
            Legacy_SaveDBToStream(mStream);
            return mStream.ToArray();
        }

        internal void Legacy_SaveDBToStream(Stream stream)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));

            using var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true);

            writer.Write(Version);
            writer.Write(CustomVersion);
            writer.Write(MapIndex);
            writer.Write(ItemIndex);
            writer.Write(MonsterIndex);
            writer.Write(NPCIndex);
            writer.Write(QuestIndex);
            writer.Write(GameshopIndex);
            writer.Write(ConquestIndex);
            writer.Write(RespawnIndex);

            writer.Write(MapInfoList.Count);
            for (var i = 0; i < MapInfoList.Count; i++)
                MapInfoList[i].Save(writer);

            writer.Write(ItemInfoList.Count);
            for (var i = 0; i < ItemInfoList.Count; i++)
                ItemInfoList[i].Save(writer);

            writer.Write(MonsterInfoList.Count);
            for (var i = 0; i < MonsterInfoList.Count; i++)
                MonsterInfoList[i].Save(writer);

            writer.Write(NPCInfoList.Count);
            for (var i = 0; i < NPCInfoList.Count; i++)
                NPCInfoList[i].Save(writer);

            writer.Write(QuestInfoList.Count);
            for (var i = 0; i < QuestInfoList.Count; i++)
                QuestInfoList[i].Save(writer);

            DragonInfo.Save(writer);
            writer.Write(MagicInfoList.Count);
            for (var i = 0; i < MagicInfoList.Count; i++)
                MagicInfoList[i].Save(writer);

            writer.Write(GameShopList.Count);
            for (var i = 0; i < GameShopList.Count; i++)
                GameShopList[i].Save(writer);

            writer.Write(ConquestInfoList.Count);
            for (var i = 0; i < ConquestInfoList.Count; i++)
                ConquestInfoList[i].Save(writer);

            RespawnTick.Save(writer);
            writer.Flush();
        }


        public CharacterInfo GetArchivedCharacter(string name)
        {
            return Persistence.GetArchivedCharacter(this, name);
        }

        internal CharacterInfo Legacy_GetArchivedCharacter(string name)
        {
            DirectoryInfo dir = new DirectoryInfo(ArchivePath);
            FileInfo[] files = dir.GetFiles($"{name}*.MirCA");

            if (files.Length != 1)
            {
                return null;
            }

            var fileInfo = files[0];

            CharacterInfo info = null;

            using (var stream = fileInfo.OpenRead())
            {
                using var reader = new BinaryReader(stream);

                var version = reader.ReadInt32();
                var customVersion = reader.ReadInt32();

                info = new CharacterInfo(reader, version, customVersion);
            }

            return info;
        }

        public void SaveArchivedCharacter(CharacterInfo info)
        {
            Persistence.SaveArchivedCharacter(this, info);
        }

        internal void Legacy_SaveArchivedCharacter(CharacterInfo info)
        {
            if (!Directory.Exists(ArchivePath)) Directory.CreateDirectory(ArchivePath);

            using var stream = File.Create(Path.Combine(ArchivePath, @$"{info.Name}{Now:_MMddyyyy_HHmmss}.MirCA"));
            using var writer = new BinaryWriter(stream);

            writer.Write(Version);
            writer.Write(CustomVersion);

            info.Save(writer);
        }

        public void SaveAccounts()
        {
            Persistence.SaveAccounts(this);
        }

        internal void Legacy_SaveAccounts()
        {
            while (Saving)
                Thread.Sleep(1);

            try
            {
                using (var stream = File.Create(AccountPath + "n"))
                    SaveAccounts(stream);
                if (File.Exists(AccountPath))
                    File.Move(AccountPath, AccountPath + "o");
                File.Move(AccountPath + "n", AccountPath);
                if (File.Exists(AccountPath + "o"))
                    File.Delete(AccountPath + "o");

                SqlSaveResilience.ReportSuccess(DatabaseProviderKind.Legacy, SqlSaveDomain.Accounts);
            }
            catch (Exception ex)
            {
                MessageQueue.Enqueue(ex);
                SqlSaveResilience.ReportFailure(DatabaseProviderKind.Legacy, SqlSaveDomain.Accounts, ex, operation: "SaveAccounts");
            }
        }

        private void SaveAccounts(Stream stream)
        {
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(Version);
                writer.Write(CustomVersion);
                writer.Write(NextAccountID);
                writer.Write(NextCharacterID);
                writer.Write(NextUserItemID);
                writer.Write(NextHeroID);

                writer.Write(GuildList.Count);
                writer.Write(NextGuildID);
                writer.Write(HeroList.Count);
                for (var i = 0; i < HeroList.Count; i++)
                    HeroList[i].Save(writer);
                writer.Write(AccountList.Count);
                for (var i = 0; i < AccountList.Count; i++)
                    AccountList[i].Save(writer);

                writer.Write(NextAuctionID);
                writer.Write(Auctions.Count);
                foreach (var auction in Auctions)
                    auction.Save(writer);

                writer.Write(NextMailID);

                writer.Write(GameshopLog.Count);
                foreach (var item in GameshopLog)
                {
                    writer.Write(item.Key);
                    writer.Write(item.Value);
                }

                writer.Write(SavedSpawns.Count);
                foreach (var Spawn in SavedSpawns)
                {
                    var Save = new RespawnSave { RespawnIndex = Spawn.Info.RespawnIndex, NextSpawnTick = Spawn.NextSpawnTick, Spawned = Spawn.Count >= Spawn.Info.Count * SpawnMultiplier };
                    Save.Save(writer);
                }
            }
        }

        private void SaveGuilds(bool forced = false)
        {
            Persistence.SaveGuilds(this, forced);
        }

        internal void Legacy_SaveGuilds(bool forced = false)
        {
            if (!Directory.Exists(Settings.GuildPath)) Directory.CreateDirectory(Settings.GuildPath);

            if (GuildRefreshNeeded == true) //deletes guild files and resaves with new indexing if a guild is deleted.
            {
                foreach (var guildfile in Directory.GetFiles(Settings.GuildPath, "*.mgd"))
                {
                    File.Delete(guildfile);
                }

                GuildRefreshNeeded = false;
                forced = true; //triggers a full resave of all guilds
            }

            for (var i = 0; i < GuildList.Count; i++)
            {
                if (GuildList[i].NeedSave || forced)
                {
                    GuildList[i].NeedSave = false;
                    var mStream = new MemoryStream();
                    var writer = new BinaryWriter(mStream);
                    GuildList[i].Save(writer);
                    var data = mStream.ToArray();

                    FileStream fStream = null;
                    try
                    {
                        fStream = new FileStream(Path.Combine(Settings.GuildPath, i + ".mgdn"), FileMode.Create);
                        fStream.BeginWrite(data, 0, data.Length, EndSaveGuildsAsync, fStream);
                        fStream = null; // 交由回调释放
                    }
                    finally
                    {
                        try { fStream?.Dispose(); } catch { /* ignore */ }
                    }
                }
            }
        }
        private void EndSaveGuildsAsync(IAsyncResult result)
        {
            var fStream = result.AsyncState as FileStream;
            var success = false;
            try
            {
                if (fStream == null) return;
                var oldfilename = fStream.Name.Substring(0, fStream.Name.Length - 1);
                var newfilename = fStream.Name;
                fStream.EndWrite(result);
                if (File.Exists(oldfilename))
                    File.Move(oldfilename, oldfilename + "o");
                File.Move(newfilename, oldfilename);
                if (File.Exists(oldfilename + "o"))
                    File.Delete(oldfilename + "o");
                success = true;
            }
            catch (Exception ex)
            {
                SqlSaveResilience.ReportFailure(DatabaseProviderKind.Legacy, SqlSaveDomain.Guilds, ex, operation: "EndSaveGuildsAsync");

                try
                {
                    if (fStream != null)
                    {
                        var oldfilename = fStream.Name.Substring(0, fStream.Name.Length - 1);
                        var indexText = Path.GetFileNameWithoutExtension(oldfilename);
                        if (int.TryParse(indexText, out var listIndex))
                        {
                            InvokeOnMainThread(() =>
                            {
                                if (listIndex >= 0 && listIndex < GuildList.Count)
                                    GuildList[listIndex].NeedSave = true;
                                return 0;
                            }, timeoutMs: 500);
                        }
                    }
                }
                catch
                {
                    // ignore
                }
            }
            finally
            {
                try { fStream?.Dispose(); } catch { /* ignore */ }

                if (success)
                    SqlSaveResilience.ReportSuccess(DatabaseProviderKind.Legacy, SqlSaveDomain.Guilds);
            }
        }

        private void SaveGoods(bool forced = false)
        {
            Persistence.SaveGoods(this, forced);
        }

        internal void Legacy_SaveGoods(bool forced = false)
        {
            if (!Directory.Exists(Settings.GoodsPath)) Directory.CreateDirectory(Settings.GoodsPath);

            for (var i = 0; i < MapList.Count; i++)
            {
                var map = MapList[i];

                if (map.NPCs.Count == 0) continue;

                for (var j = 0; j < map.NPCs.Count; j++)
                {
                    var npc = map.NPCs[j];

                    if (forced)
                    {
                        npc.ProcessGoods(forced);
                    }

                    if (!npc.NeedSave) continue;

                    var path = Path.Combine(Settings.GoodsPath, npc.Info.Index + ".msdn");

                    var mStream = new MemoryStream();
                    var writer = new BinaryWriter(mStream);
                    var Temp = 9999;
                    writer.Write(Temp);
                    writer.Write(Version);
                    writer.Write(CustomVersion);
                    writer.Write(npc.UsedGoods.Count);

                    for (var k = 0; k < npc.UsedGoods.Count; k++)
                    {
                        npc.UsedGoods[k].Save(writer);
                    }

                    var data = mStream.ToArray();

                    FileStream fStream = null;
                    try
                    {
                        fStream = new FileStream(path, FileMode.Create);
                        fStream.BeginWrite(data, 0, data.Length, EndSaveGoodsAsync, fStream);
                        fStream = null; // 交由回调释放
                    }
                    finally
                    {
                        try { fStream?.Dispose(); } catch { /* ignore */ }
                    }
                }
            }
        }
        private void EndSaveGoodsAsync(IAsyncResult result)
        {
            FileStream fStream = null;
            var success = false;
            try
            {
                fStream = result.AsyncState as FileStream;
                if (fStream == null) return;
                var oldfilename = fStream.Name.Substring(0, fStream.Name.Length - 1);
                var newfilename = fStream.Name;
                fStream.EndWrite(result);
                if (File.Exists(oldfilename))
                    File.Move(oldfilename, oldfilename + "o");
                File.Move(newfilename, oldfilename);
                if (File.Exists(oldfilename + "o"))
                    File.Delete(oldfilename + "o");
                success = true;
            }
            catch (Exception ex)
            {
                SqlSaveResilience.ReportFailure(DatabaseProviderKind.Legacy, SqlSaveDomain.Goods, ex, operation: "EndSaveGoodsAsync");

                try
                {
                    if (fStream != null)
                    {
                        var oldfilename = fStream.Name.Substring(0, fStream.Name.Length - 1);
                        var npcIndexText = Path.GetFileNameWithoutExtension(oldfilename);
                        if (int.TryParse(npcIndexText, out var npcIndex))
                        {
                            InvokeOnMainThread(() =>
                            {
                                for (var i = 0; i < MapList.Count; i++)
                                {
                                    var map = MapList[i];
                                    if (map?.NPCs == null || map.NPCs.Count == 0) continue;

                                    for (var j = 0; j < map.NPCs.Count; j++)
                                    {
                                        var npc = map.NPCs[j];
                                        if (npc?.Info == null) continue;
                                        if (npc.Info.Index != npcIndex) continue;

                                        npc.NeedSave = true;
                                        return 0;
                                    }
                                }

                                return 0;
                            }, timeoutMs: 500);
                        }
                    }
                }
                catch
                {
                    // ignore
                }
            }
            finally
            {
                try { fStream?.Dispose(); } catch { /* ignore */ }

                if (success)
                    SqlSaveResilience.ReportSuccess(DatabaseProviderKind.Legacy, SqlSaveDomain.Goods);
            }
        }

        private void SaveConquests(bool forced = false)
        {
            Persistence.SaveConquests(this, forced);
        }

        internal void Legacy_SaveConquests(bool forced = false)
        {
            if (!Directory.Exists(Settings.ConquestsPath)) Directory.CreateDirectory(Settings.ConquestsPath);
            for (var i = 0; i < ConquestList.Count; i++)
            {
                if (!ConquestList[i].NeedSave && !forced) continue;
                ConquestList[i].NeedSave = false;
                var mStream = new MemoryStream();
                var writer = new BinaryWriter(mStream);
                ConquestList[i].Save(writer);
                var data = mStream.ToArray();

                FileStream fStream = null;
                try
                {
                    fStream = new FileStream(Path.Combine(Settings.ConquestsPath, ConquestList[i].Info.Index + ".mcdn"), FileMode.Create);
                    fStream.BeginWrite(data, 0, data.Length, EndSaveConquestsAsync, fStream);
                    fStream = null; // 交由回调释放
                }
                finally
                {
                    try { fStream?.Dispose(); } catch { /* ignore */ }
                }
            }
        }
        private void EndSaveConquestsAsync(IAsyncResult result)
        {
            var fStream = result.AsyncState as FileStream;
            var success = false;
            try
            {
                if (fStream == null) return;
                var oldfilename = fStream.Name.Substring(0, fStream.Name.Length - 1);
                var newfilename = fStream.Name;
                fStream.EndWrite(result);
                if (File.Exists(oldfilename))
                    File.Move(oldfilename, oldfilename + "o");
                File.Move(newfilename, oldfilename);
                if (File.Exists(oldfilename + "o"))
                    File.Delete(oldfilename + "o");
                success = true;
            }
            catch (Exception ex)
            {
                SqlSaveResilience.ReportFailure(DatabaseProviderKind.Legacy, SqlSaveDomain.Conquests, ex, operation: "EndSaveConquestsAsync");

                try
                {
                    if (fStream != null)
                    {
                        var oldfilename = fStream.Name.Substring(0, fStream.Name.Length - 1);
                        var conquestIndexText = Path.GetFileNameWithoutExtension(oldfilename);
                        if (int.TryParse(conquestIndexText, out var conquestIndex))
                        {
                            InvokeOnMainThread(() =>
                            {
                                for (var i = 0; i < ConquestList.Count; i++)
                                {
                                    var conquest = ConquestList[i];
                                    if (conquest?.Info == null) continue;
                                    if (conquest.Info.Index != conquestIndex) continue;
                                    conquest.NeedSave = true;
                                    return 0;
                                }

                                return 0;
                            }, timeoutMs: 500);
                        }
                    }
                }
                catch
                {
                    // ignore
                }
            }
            finally
            {
                try { fStream?.Dispose(); } catch { /* ignore */ }

                if (success)
                    SqlSaveResilience.ReportSuccess(DatabaseProviderKind.Legacy, SqlSaveDomain.Conquests);
            }
        }

        public void BeginSaveAccounts()
        {
            Persistence.BeginSaveAccounts(this);
        }

        internal void Legacy_BeginSaveAccounts()
        {
            if (Saving) return;

            Saving = true;

            string backupPath = null;

            try
            {
                using (var mStream = new MemoryStream())
                {
                    if (File.Exists(AccountPath))
                    {
                        if (!Directory.Exists(AccountsBackUpPath)) Directory.CreateDirectory(AccountsBackUpPath);
                        var fileName =
                            $"Accounts {Now.Year:0000}-{Now.Month:00}-{Now.Day:00} {Now.Hour:00}-{Now.Minute:00}-{Now.Second:00}.bak";
                        backupPath = Path.Combine(AccountsBackUpPath, fileName);
                        if (File.Exists(backupPath)) File.Delete(backupPath);
                        File.Move(AccountPath, backupPath);
                    }

                    SaveAccounts(mStream);

                    var data = mStream.ToArray();

                    FileStream fStream = null;
                    try
                    {
                        fStream = new FileStream(AccountPath + "n", FileMode.Create);
                        fStream.BeginWrite(data, 0, data.Length, EndSaveAccounts, fStream);
                        fStream = null; // 交由回调释放
                    }
                    finally
                    {
                        try { fStream?.Dispose(); } catch { /* ignore */ }
                    }
                }
            }
            catch (Exception ex)
            {
                try
                {
                    if (!string.IsNullOrWhiteSpace(backupPath) && File.Exists(backupPath) && !File.Exists(AccountPath))
                        File.Move(backupPath, AccountPath);
                }
                catch
                {
                    // ignore
                }

                try
                {
                    if (File.Exists(AccountPath + "n"))
                        File.Delete(AccountPath + "n");
                }
                catch
                {
                    // ignore
                }

                Saving = false;
                SqlSaveResilience.ReportFailure(DatabaseProviderKind.Legacy, SqlSaveDomain.Accounts, ex, operation: "BeginSaveAccounts");
            }

        }
        private void EndSaveAccounts(IAsyncResult result)
        {
            var fStream = result.AsyncState as FileStream;
            var success = false;
            try
            {
                if (fStream != null)
                {
                    var oldfilename = fStream.Name.Substring(0, fStream.Name.Length - 1);
                    var newfilename = fStream.Name;
                    fStream.EndWrite(result);
                    if (File.Exists(oldfilename))
                        File.Move(oldfilename, oldfilename + "o");
                    File.Move(newfilename, oldfilename);
                    if (File.Exists(oldfilename + "o"))
                        File.Delete(oldfilename + "o");
                    success = true;
                }
            }
            catch (Exception ex)
            {
                SqlSaveResilience.ReportFailure(DatabaseProviderKind.Legacy, SqlSaveDomain.Accounts, ex, operation: "EndSaveAccounts");
            }
            finally
            {
                try { fStream?.Dispose(); } catch { /* ignore */ }

                if (success)
                    SqlSaveResilience.ReportSuccess(DatabaseProviderKind.Legacy, SqlSaveDomain.Accounts);

                Saving = false;
            }
        }

        public bool LoadDB()
        {
            return Persistence.LoadWorld(this);
        }

        internal bool Legacy_LoadDB()
        {
            if (!File.Exists(DatabasePath))
            {
                SaveDB();
            }

            using var stream = File.OpenRead(DatabasePath);
            return Legacy_LoadDBFromStream(stream);
        }

        internal bool Legacy_LoadDBFromStream(Stream stream)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));

            lock (LoadLock)
            {
                if (stream.CanSeek)
                    stream.Seek(0, SeekOrigin.Begin);

                using var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, leaveOpen: true);

                LoadVersion = reader.ReadInt32();
                LoadCustomVersion = reader.ReadInt32();

                if (LoadVersion < MinVersion)
                {
                    MessageQueue.Enqueue($"无法运行 {LoadVersion} 版本的数据库. 支持的最老版本为：{MinVersion}.");
                    return false;
                }
                else if (LoadVersion > Version)
                {
                    MessageQueue.Enqueue($"无法运行 {LoadVersion} 版本的数据库. 支持的最新版本为： {Version} ");
                    return false;

                }

                MapIndex = reader.ReadInt32();
                ItemIndex = reader.ReadInt32();
                MonsterIndex = reader.ReadInt32();

                NPCIndex = reader.ReadInt32();
                QuestIndex = reader.ReadInt32();

                if (LoadVersion >= 63)
                {
                    GameshopIndex = reader.ReadInt32();
                }

                if (LoadVersion >= 66)
                {
                    ConquestIndex = reader.ReadInt32();
                }

                if (LoadVersion >= 68)
                    RespawnIndex = reader.ReadInt32();


                var count = reader.ReadInt32();
                MapInfoList.Clear();
                for (var i = 0; i < count; i++)
                    MapInfoList.Add(new MapInfo(reader));

                count = reader.ReadInt32();
                ItemInfoList.Clear();
                for (var i = 0; i < count; i++)
                {
                    ItemInfoList.Add(new ItemInfo(reader, LoadVersion, LoadCustomVersion));
                    if (ItemInfoList[i] != null && ItemInfoList[i].RandomStatsId < Settings.RandomItemStatsList.Count)
                    {
                        ItemInfoList[i].RandomStats = Settings.RandomItemStatsList[ItemInfoList[i].RandomStatsId];
                    }
                }
                count = reader.ReadInt32();
                MonsterInfoList.Clear();
                for (var i = 0; i < count; i++)
                    MonsterInfoList.Add(new MonsterInfo(reader));

                count = reader.ReadInt32();
                NPCInfoList.Clear();
                for (var i = 0; i < count; i++)
                    NPCInfoList.Add(new NPCInfo(reader));

                count = reader.ReadInt32();
                QuestInfoList.Clear();
                for (var i = 0; i < count; i++)
                    QuestInfoList.Add(new QuestInfo(reader));

                DragonInfo = new DragonInfo(reader);
                count = reader.ReadInt32();
                for (var i = 0; i < count; i++)
                {
                    var m = new MagicInfo(reader, LoadVersion, LoadCustomVersion);
                    if (!MagicExists(m.Spell))
                        MagicInfoList.Add(m);
                }

                FillMagicInfoList();
                if (LoadVersion <= 70)
                    UpdateMagicInfo();

                if (LoadVersion >= 63)
                {
                    count = reader.ReadInt32();
                    GameShopList.Clear();
                    for (var i = 0; i < count; i++)
                    {
                        var item = new GameShopItem(reader, LoadVersion, LoadCustomVersion);
                        if (Main.BindGameShop(item))
                        {
                            GameShopList.Add(item);
                        }
                    }
                }

                if (LoadVersion >= 66)
                {
                    ConquestInfoList.Clear();
                    count = reader.ReadInt32();
                    for (var i = 0; i < count; i++)
                    {
                        ConquestInfoList.Add(new ConquestInfo(reader));
                    }
                }

                if (LoadVersion > 67)
                    RespawnTick = new RespawnTimer(reader);

                Settings.LinkGuildCreationItems(ItemInfoList);
            }

            return true;
        }

        public void LoadAccounts()
        {
            Persistence.LoadAccounts(this);
        }

        internal void Legacy_LoadAccounts()
        {
            if (!File.Exists(AccountPath))
            {
                Legacy_SaveAccounts();
            }

            using var stream = File.OpenRead(AccountPath);
            Legacy_LoadAccountsFromStream(stream);
        }

        internal void Legacy_LoadAccountsFromStream(Stream stream)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));

            //reset ranking
            for (var i = 0; i < RankClass.Count(); i++)
            {
                if (RankClass[i] != null)
                {
                    RankClass[i].Clear();
                }
                else
                {
                    RankClass[i] = new List<RankCharacterInfo>();
                }
            }

            RankTop.Clear();

            lock (LoadLock)
            {
                if (stream.CanSeek)
                    stream.Seek(0, SeekOrigin.Begin);

                using var reader = new BinaryReader(stream);

                LoadVersion = reader.ReadInt32();
                LoadCustomVersion = reader.ReadInt32();
                NextAccountID = reader.ReadInt32();
                NextCharacterID = reader.ReadInt32();
                NextUserItemID = reader.ReadUInt64();
                if (LoadVersion > 98)
                    NextHeroID = reader.ReadInt32();

                GuildCount = reader.ReadInt32();
                NextGuildID = reader.ReadInt32();

                int count;
                if (LoadVersion > 102)
                {
                    count = reader.ReadInt32();

                    HeroList.Clear();

                    for (var i = 0; i < count; i++)
                        HeroList.Add(new HeroInfo(reader, LoadVersion, LoadCustomVersion));
                }

                count = reader.ReadInt32();

                AccountList.Clear();
                CharacterList.Clear();

                int TrueAccount = 0;
                for (var i = 0; i < count; i++)
                {
                    AccountInfo NextAccount = new AccountInfo(reader);
                    if (i > 0 && NextAccount.Characters.Count == 0)
                        continue;
                    AccountList.Add(NextAccount);
                    CharacterList.AddRange(AccountList[TrueAccount].Characters);
                    if (LoadVersion > 98 && LoadVersion < 103)
                        AccountList[TrueAccount].Characters.ForEach(character => HeroList.AddRange(character.Heroes));
                    TrueAccount++;
                }

                foreach (var auction in Auctions)
                {
                    auction.SellerInfo.AccountInfo.Auctions.Remove(auction);
                }

                Auctions.Clear();

                NextAuctionID = reader.ReadUInt64();

                count = reader.ReadInt32();
                for (var i = 0; i < count; i++)
                {
                    var auction = new AuctionInfo(reader, LoadVersion, LoadCustomVersion);

                    if (!BindItem(auction.Item) || !BindCharacter(auction)) continue;

                    Auctions.AddLast(auction);
                    auction.SellerInfo.AccountInfo.Auctions.AddLast(auction);
                }

                NextMailID = reader.ReadUInt64();

                if (LoadVersion <= 80)
                {
                    count = reader.ReadInt32();
                    for (var i = 0; i < count; i++)
                    {
                        var mail = new MailInfo(reader, LoadVersion, LoadCustomVersion);

                        mail.RecipientInfo = GetCharacterInfo(mail.RecipientIndex);

                        if (mail.RecipientInfo != null)
                        {
                            mail.RecipientInfo.Mail.Add(mail); //add to players inbox
                        }
                    }
                }

                if (LoadVersion >= 63)
                {
                    var logCount = reader.ReadInt32();
                    for (var i = 0; i < logCount; i++)
                    {
                        GameshopLog.Add(reader.ReadInt32(), reader.ReadInt32());
                    }

                    if (ResetGS) ClearGameshopLog();
                }

                if (LoadVersion >= 68)
                {
                    var saveCount = reader.ReadInt32();
                    for (var i = 0; i < saveCount; i++)
                    {
                        var saved = new RespawnSave(reader);
                        foreach (var respawn in SavedSpawns)
                        {
                            if (respawn.Info.RespawnIndex != saved.RespawnIndex) continue;

                            respawn.NextSpawnTick = saved.NextSpawnTick;

                            if (!saved.Spawned || respawn.Info.Count * SpawnMultiplier <= respawn.Count)
                            {
                                continue;
                            }

                            var mobcount = respawn.Info.Count * SpawnMultiplier - respawn.Count;
                            for (var j = 0; j < mobcount; j++)
                            {
                                respawn.Spawn();
                            }
                        }
                    }
                }
            }
        }

        internal byte[] Legacy_SaveAccountsToBytes()
        {
            using var mStream = new MemoryStream();
            SaveAccounts(mStream);
            return mStream.ToArray();
        }

        public void LoadGuilds()
        {
            Persistence.LoadGuilds(this);
        }

        internal void Legacy_LoadGuilds()
        {
            lock (LoadLock)
            {
                var count = 0;

                GuildList.Clear();

                for (var i = 0; i < GuildCount; i++)
                {
                    GuildInfo guildInfo;
                    if (!File.Exists(Path.Combine(Settings.GuildPath, i + ".mgd"))) continue;

                    using (var stream = File.OpenRead(Path.Combine(Settings.GuildPath, i + ".mgd")))
                    {
                        using var reader = new BinaryReader(stream);
                        guildInfo = new GuildInfo(reader);
                    }

                    GuildList.Add(guildInfo);

                    new GuildObject(guildInfo);

                    count++;
                }

                if (count != GuildCount) GuildCount = count;
            }
        }

        public void LoadConquests()
        {
            Persistence.LoadConquests(this);
        }

        internal void Legacy_LoadConquests()
        {
            lock (LoadLock)
            {
                Conquests.Clear();
                ConquestList.Clear();

                for (var i = 0; i < ConquestInfoList.Count; i++)
                {
                    ConquestObject newConquest;
                    ConquestGuildInfo conquestGuildInfo;
                    var tempMap = GetMap(ConquestInfoList[i].MapIndex);

                    if (tempMap == null) continue;

                    if (File.Exists(Path.Combine(Settings.ConquestsPath, ConquestInfoList[i].Index + ".mcd")))
                    {
                        using (var stream = File.OpenRead(Path.Combine(Settings.ConquestsPath, ConquestInfoList[i].Index + ".mcd")))
                        {
                            using var reader = new BinaryReader(stream);
                            conquestGuildInfo = new ConquestGuildInfo(reader) { Info = ConquestInfoList[i] };
                        }

                        newConquest = new ConquestObject(conquestGuildInfo)
                        {
                            ConquestMap = tempMap
                        };

                        for (var k = 0; k < Guilds.Count; k++)
                        {
                            if (conquestGuildInfo.Owner != Guilds[k].Guildindex) continue;
                            newConquest.Guild = Guilds[k];
                            Guilds[k].Conquest = newConquest;
                        }
                    }
                    else
                    {
                        conquestGuildInfo = new ConquestGuildInfo { Info = ConquestInfoList[i], NeedSave = true };
                        newConquest = new ConquestObject(conquestGuildInfo)
                        {
                            ConquestMap = tempMap
                        };
                    }

                    ConquestList.Add(conquestGuildInfo);
                    Conquests.Add(newConquest);
                    tempMap.Conquest.Add(newConquest);

                    newConquest.Bind();
                }
            }
        }

        public void LoadDisabledChars()
        {
            DisabledCharNames.Clear();

            var lines = GetRootConfigLinesOrEmpty(
                key: "disabledchars",
                traceName: "DisabledChars.txt",
                csharpGetter: provider => provider.DisabledChars != null ? provider.DisabledChars.Values : null);

            for (var i = 0; i < lines.Count; i++)
            {
                var line = lines[i];
                if (string.IsNullOrWhiteSpace(line)) continue;
                if (line.StartsWith(";")) continue;
                DisabledCharNames.Add(line.ToUpper());
            }
        }

        public void LoadLineMessages()
        {
            LineMessages.Clear();

            var lines = GetRootConfigLinesOrEmpty(
                key: "linemessage",
                traceName: "LineMessage.txt",
                csharpGetter: provider => provider.LineMessages != null ? provider.LineMessages.Lines : null);

            for (var i = 0; i < lines.Count; i++)
            {
                var line = lines[i];
                if (string.IsNullOrWhiteSpace(line)) continue;
                if (line.StartsWith(";")) continue;
                LineMessages.Add(line);
            }
        }

        private bool BindCharacter(AuctionInfo auction)
        {
            bool bound = false;

            for (int i = 0; i < CharacterList.Count; i++)
            {
                if (CharacterList[i].Index == auction.SellerIndex)
                {
                    auction.SellerInfo = CharacterList[i];
                    bound = true;
                }

                else if (CharacterList[i].Index == auction.CurrentBuyerIndex)
                {
                    auction.CurrentBuyerInfo = CharacterList[i];
                    bound = true;
                }
            }

            return bound;
        }

        internal void ApplyCSharpQuestDefinitions(ScriptRegistry registry)
        {
            var scriptsRuntimeActive = Settings.CSharpScriptsEnabled && CSharpScripts.Enabled;

            var questRegistry = registry?.Quests;
            var provider = questRegistry != null && questRegistry.Count > 0 ? new CSharpQuestProvider(questRegistry.Definitions) : null;

            QuestProvider = provider;

            var applied = 0;
            var errorCount = 0;
            var removed = 0;

            var newAppliedKeys = new HashSet<string>(StringComparer.Ordinal);
            var questByKey = new Dictionary<string, QuestInfo>(QuestInfoList.Count, StringComparer.Ordinal);

            for (var i = 0; i < QuestInfoList.Count; i++)
            {
                var quest = QuestInfoList[i];
                if (quest == null) continue;

                var key = $"Quests/{quest.FileName}";

                if (LogicKey.TryNormalize(key, out var normalizedKey))
                    questByKey[normalizedKey] = quest;

                var allowCSharp = scriptsRuntimeActive && ScriptDispatchPolicy.ShouldTryCSharp(key);

                if (!allowCSharp)
                    continue;

                var definition = provider?.GetByKey(key);
                if (definition == null) continue;

                if (!quest.TryApplyDefinition(definition, out var error))
                {
                    errorCount++;
                    MessageQueue.Enqueue($"[Scripts] Quests 应用失败：{key} {error}");
                    continue;
                }

                newAppliedKeys.Add(definition.Key);
                applied++;
            }

            if (_lastAppliedCSharpQuestKeys.Count > 0)
            {
                foreach (var oldKey in _lastAppliedCSharpQuestKeys)
                {
                    if (newAppliedKeys.Contains(oldKey))
                        continue;

                    removed++;

                    if (questByKey.TryGetValue(oldKey, out var quest))
                        quest.LoadInfo(clear: true);
                }
            }

            _lastAppliedCSharpQuestKeys = newAppliedKeys;

            if (applied > 0 || removed > 0 || errorCount > 0)
            {
                var total = provider?.GetAll().Count ?? 0;
                MessageQueue.Enqueue($"[Scripts] Quests: 已应用 {applied}/{total} 个 C# 任务定义（移除={removed}，error={errorCount}）");
            }
        }

        internal void ApplyCSharpDropTables(ScriptRegistry registry)
        {
            var scriptsRuntimeActive = Settings.CSharpScriptsEnabled && CSharpScripts.Enabled;

            if (!scriptsRuntimeActive)
            {
                DropTableProvider = null;
                return;
            }

            var dropRegistry = registry?.Drops;
            DropTableProvider = dropRegistry != null && dropRegistry.Count > 0 ? new CSharpDropTableProvider(dropRegistry.Definitions) : null;

            // 启动阶段可能早于 DB/Item 加载：不在此时主动 ReloadDrops；后续 StartEnvir 会加载一次 Drops。
            if (!Running || MonsterInfoList.Count == 0 || ItemInfoList.Count == 0)
                return;

            try
            {
                ReloadDrops();
                MessageQueue.Enqueue($"[Scripts] Drops: 已刷新（v{CSharpScripts.Version}，tables={dropRegistry?.Count ?? 0}）");
            }
            catch (Exception ex)
            {
                MessageQueue.Enqueue("[Scripts] Drops 刷新失败：" + ex);
            }
        }

        internal void ApplyCSharpRecipeDefinitions(ScriptRegistry registry)
        {
            var scriptsRuntimeActive = Settings.CSharpScriptsEnabled && CSharpScripts.Enabled;

            var recipeRegistry = registry?.Recipes;
            var provider = recipeRegistry != null && recipeRegistry.Count > 0 ? new CSharpRecipeProvider(recipeRegistry.Definitions) : null;

            RecipeProvider = scriptsRuntimeActive ? provider : null;

            // 启动阶段可能早于 DB/Item 加载：不在此时主动 ReloadRecipes；后续 StartEnvir 会加载一次 Recipes。
            if (!Running || ItemInfoList.Count == 0)
                return;

            try
            {
                ReloadRecipes(out var total, out var csharp, out var txt, out var skipped, out var errors);
                MessageQueue.Enqueue($"[Scripts] Recipe: 已刷新（v{CSharpScripts.Version}，defs={recipeRegistry?.Count ?? 0}，recipes={total}，C#={csharp}，txt={txt}，skip={skipped}，error={errors}）");
            }
            catch (Exception ex)
            {
                MessageQueue.Enqueue("[Scripts] Recipe 刷新失败：" + ex);
            }
        }

        internal void ApplyCSharpValueDefinitions(ScriptRegistry registry)
        {
            var scriptsRuntimeActive = Settings.CSharpScriptsEnabled && CSharpScripts.Enabled;

            if (!scriptsRuntimeActive)
            {
                ValueProvider = null;
                return;
            }

            var valueRegistry = registry?.Values;
            ValueProvider = valueRegistry != null && valueRegistry.Count > 0 ? new CSharpValueProvider(valueRegistry.Definitions) : null;

            if (ValueProvider != null)
            {
                MessageQueue.Enqueue($"[Scripts] 已加载 {ValueProvider.GetAll().Count} 个 C# 数值表");
            }
        }

        internal void ApplyCSharpNameListDefinitions(ScriptRegistry registry)
        {
            var scriptsRuntimeActive = Settings.CSharpScriptsEnabled && CSharpScripts.Enabled;

            if (!scriptsRuntimeActive)
            {
                NameListProvider = null;
                return;
            }

            var nameListRegistry = registry?.NameLists;
            NameListProvider = nameListRegistry != null && nameListRegistry.Count > 0 ? new CSharpNameListProvider(nameListRegistry.Definitions) : null;

            if (NameListProvider != null)
            {
                MessageQueue.Enqueue($"[Scripts] 已加载 {NameListProvider.GetAll().Count} 个 C# 名单");
            }
        }

        private void ReloadRoutes(out int respawnCount, out int errorCount)
        {
            respawnCount = 0;
            errorCount = 0;

            for (var i = 0; i < MapList.Count; i++)
            {
                var map = MapList[i];
                if (map?.Respawns == null || map.Respawns.Count == 0) continue;

                for (var j = 0; j < map.Respawns.Count; j++)
                {
                    var respawn = map.Respawns[j];
                    if (respawn == null) continue;

                    respawnCount++;

                    try
                    {
                        respawn.LoadRoutes();
                    }
                    catch (Exception ex)
                    {
                        errorCount++;
                        MessageQueue.Enqueue("[Scripts] Routes 刷新失败：" + ex);
                    }
                }
            }
        }

        internal void ApplyCSharpRouteDefinitions(ScriptRegistry registry)
        {
            var scriptsRuntimeActive = Settings.CSharpScriptsEnabled && CSharpScripts.Enabled;

            var routeRegistry = registry?.Routes;
            var provider = routeRegistry != null && routeRegistry.Count > 0 ? new CSharpRouteProvider(routeRegistry.Definitions) : null;

            RouteProvider = scriptsRuntimeActive ? provider : null;

            // 启动阶段可能早于 Map/Respawn 加载：不在此时主动刷新；后续 Map 加载会读取一次 Routes。
            if (!Running || MapList.Count == 0)
                return;

            try
            {
                ReloadRoutes(out var respawns, out var errors);
                MessageQueue.Enqueue($"[Scripts] Routes: 已刷新（v{CSharpScripts.Version}，defs={routeRegistry?.Count ?? 0}，respawns={respawns}，error={errors}）");
            }
            catch (Exception ex)
            {
                MessageQueue.Enqueue("[Scripts] Routes 刷新失败：" + ex);
            }
        }

        internal List<RouteInfo> LoadRouteDefinitionOrEmpty(string routePathRaw, int respawnIndex, int monsterIndex)
        {
            var newRoute = new List<RouteInfo>();

            if (string.IsNullOrWhiteSpace(routePathRaw))
                return newRoute;

            var routePath = routePathRaw.Trim();
            if (routePath.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
                routePath = routePath.Substring(0, routePath.Length - 4);

            var scriptsRuntimeActive = Settings.CSharpScriptsEnabled && CSharpScripts.Enabled;

            var key = $"Routes/{routePath}";
            var allowCSharp = scriptsRuntimeActive && Server.Scripting.ScriptDispatchPolicy.ShouldTryCSharp(key);

            if (allowCSharp)
            {
                var definition = RouteProvider?.GetByKey(key);
                if (definition != null)
                {
                    if (definition.Route != null && definition.Route.Count > 0)
                    {
                        newRoute.AddRange(definition.Route.Where(r => r != null));
                    }

                    return newRoute;
                }

                if (scriptsRuntimeActive)
                {
                    if (Settings.TxtScriptsLogDispatch)
                    {
                        var csharpState = CSharpScripts.Enabled ? $"v{CSharpScripts.Version}, handlers={CSharpScripts.LastRegisteredHandlerCount}" : $"不可用: {CSharpScripts.LastError}";
                        MessageQueue.Enqueue($"[Scripts][Load] Routes respawn={respawnIndex} monster={monsterIndex} -> 未提供 C# 定义（key={key}，allowCSharp={allowCSharp}，C#={csharpState}）");
                    }

                    return newRoute;
                }
            }
            else if (scriptsRuntimeActive)
            {
                if (Settings.TxtScriptsLogDispatch)
                {
                    var csharpState = CSharpScripts.Enabled ? $"v{CSharpScripts.Version}, handlers={CSharpScripts.LastRegisteredHandlerCount}" : $"不可用: {CSharpScripts.LastError}";
                    MessageQueue.Enqueue($"[Scripts][Load] Routes respawn={respawnIndex} monster={monsterIndex} -> 未提供 C# 定义（key={key}，allowCSharp={allowCSharp}，C#={csharpState}）");
                }

                return newRoute;
            }

            return newRoute;
        }

        internal void LoadDropTable(List<DropInfo> list, string name, string path, byte type = 0)
        {
            if (list == null) return;

            var scriptsRuntimeActive = Settings.CSharpScriptsEnabled && CSharpScripts.Enabled;

            if (scriptsRuntimeActive && DropInfo.TryResolveDropTableKey(path, out var key))
            {
                var allowCSharp = ScriptDispatchPolicy.ShouldTryCSharp(key);

                if (allowCSharp)
                {
                    var table = DropTableProvider?.Get(key);

                    if (table != null)
                    {
                        DropInfo.AddFromCSharpTable(list, table, type);
                        DropInfo.SortDrops(list);
                        return;
                    }
                }

                if (Settings.TxtScriptsLogDispatch)
                {
                    var csharpState = CSharpScripts.Enabled ? $"v{CSharpScripts.Version}, handlers={CSharpScripts.LastRegisteredHandlerCount}" : $"不可用: {CSharpScripts.LastError}";
                    MessageQueue.Enqueue($"[Scripts][Load] Drops {name} -> 未提供 C# 定义（key={key}，allowCSharp={allowCSharp}，C#={csharpState}）");
                }

                return;
            }

            if (Settings.TxtScriptsLogDispatch)
            {
                MessageQueue.Enqueue($"[Scripts][Load] Drops {name} -> 无法解析逻辑 Key（path={path}）");
            }
        }

        internal void ApplyCSharpRootConfigDefinitions(ScriptRegistry registry)
        {
            var scriptsRuntimeActive = Settings.CSharpScriptsEnabled && CSharpScripts.Enabled;

            var rootRegistry = registry?.RootConfigs;
            RootConfigProvider = scriptsRuntimeActive && rootRegistry != null && rootRegistry.Count > 0
                ? new CSharpRootConfigProvider(rootRegistry)
                : null;

            if (RootConfigProvider != null)
            {
                MessageQueue.Enqueue($"[Scripts] 已加载 {RootConfigProvider.Count} 个 C# RootConfig");
            }

            try
            {
                LoadDisabledChars();
                LoadLineMessages();
                ReloadNotice();
            }
            catch (Exception ex)
            {
                MessageQueue.Enqueue("[Scripts] RootConfig 刷新失败：" + ex);
            }
        }

        internal void ApplyCSharpTextFileDefinitions(ScriptRegistry registry)
        {
            var scriptsRuntimeActive = Settings.CSharpScriptsEnabled && CSharpScripts.Enabled;

            if (!scriptsRuntimeActive)
            {
                TextFileProvider = null;
                return;
            }

            var textRegistry = registry?.TextFiles;
            TextFileProvider = textRegistry != null && textRegistry.Count > 0 ? new CSharpTextFileProvider(textRegistry.Definitions) : null;

            if (TextFileProvider != null)
            {
                MessageQueue.Enqueue($"[Scripts] 已加载 {TextFileProvider.GetAll().Count} 个 C# 文本");
            }

            ClearSetBuffsCache();
        }

        internal void EnsureCSharpActiveMapCoordsApplied()
        {
            var scriptsRuntimeActive = Settings.CSharpScriptsEnabled && CSharpScripts.Enabled;

            if (!scriptsRuntimeActive)
            {
                _lastAppliedCSharpActiveMapCoordsVersion = -1;
                return;
            }

            var version = CSharpScripts.Version;
            if (version <= 0 || version == _lastAppliedCSharpActiveMapCoordsVersion)
                return;

            _lastAppliedCSharpActiveMapCoordsVersion = version;

            ApplyCSharpActiveMapCoords(CSharpScripts.CurrentRegistry);
        }

        internal void ApplyCSharpActiveMapCoords(ScriptRegistry registry)
        {
            var scriptsRuntimeActive = Settings.CSharpScriptsEnabled && CSharpScripts.Enabled;
            if (!scriptsRuntimeActive) return;

            var coordRegistry = registry?.ActiveMapCoords;
            if (coordRegistry == null || coordRegistry.CoordCount <= 0) return;

            if (MapList == null || MapList.Count == 0) return;

            var added = 0;
            var touchedMaps = 0;

            foreach (var entry in coordRegistry.ByMap)
            {
                var mapFileName = entry.Key;
                if (string.IsNullOrWhiteSpace(mapFileName)) continue;

                Map map = null;
                for (var i = 0; i < MapList.Count; i++)
                {
                    var candidate = MapList[i];
                    if (candidate == null) continue;

                    if (string.Equals(candidate.Info.FileName, mapFileName, StringComparison.OrdinalIgnoreCase))
                    {
                        map = candidate;
                        break;
                    }
                }
                if (map == null) continue;

                touchedMaps++;

                foreach (var point in entry.Value)
                {
                    if (map.Info.ActiveCoords.Contains(point)) continue;
                    map.Info.ActiveCoords.Add(point);
                    added++;
                }
            }

            if (added > 0)
            {
                MessageQueue.Enqueue($"[Scripts] MapCoords: 已加载 {added} 个坐标触发点（maps={touchedMaps}，v{CSharpScripts.Version}）");
            }
        }

        private IReadOnlyList<string> GetRootConfigLinesOrEmpty(string key, string traceName, Func<IRootConfigProvider, IReadOnlyList<string>> csharpGetter)
        {
            var scriptsRuntimeActive = Settings.CSharpScriptsEnabled && CSharpScripts.Enabled;
            var allowCSharp = scriptsRuntimeActive && RootConfigProvider != null;

            if (allowCSharp)
            {
                var lines = csharpGetter != null ? csharpGetter(RootConfigProvider) : null;
                if (lines != null)
                    return lines;

                if (!Server.Scripting.TxtFallbackPolicy.ShouldFallbackToTxt(key))
                {
                    if (Settings.TxtScriptsLogDispatch)
                    {
                        var csharpState = CSharpScripts.Enabled ? $"v{CSharpScripts.Version}, handlers={CSharpScripts.LastRegisteredHandlerCount}" : $"不可用: {CSharpScripts.LastError}";
                        MessageQueue.Enqueue($"[Scripts][Load] RootConfig {traceName} -> 阻止回落TXT（key={key}，C#={csharpState}）");
                    }

                    return Array.Empty<string>();
                }
            }

            if (scriptsRuntimeActive)
            {
                if (Settings.TxtScriptsLogDispatch)
                {
                    var csharpState = CSharpScripts.Enabled ? $"v{CSharpScripts.Version}, handlers={CSharpScripts.LastRegisteredHandlerCount}" : $"不可用: {CSharpScripts.LastError}";
                    MessageQueue.Enqueue($"[Scripts][Load] RootConfig {traceName} -> 未提供 C# 定义（key={key}，C#={csharpState}）");
                }
            }

            return Array.Empty<string>();
        }

        public void ReloadNotice()
        {
            try
            {
                Settings.Notice = LoadNoticeFromRootConfigOrEmpty();
            }
            catch (Exception ex)
            {
                MessageQueue.Enqueue("[Scripts] Notice 刷新失败：" + ex);
            }
        }

        internal void ReloadDragonDrops(DragonInfo info)
        {
            if (info == null) return;

            info.ClearDrops();

            var dropsRoot = Settings.DropPath ?? string.Empty;
            if (string.IsNullOrWhiteSpace(dropsRoot))
            {
                info.SortDrops();
                return;
            }

            var legacyTxtPath = Path.Combine(dropsRoot, "破天魔龙.txt");
            var jsonPath = Path.ChangeExtension(legacyTxtPath, ".json");

            var loadedFromJson = info.TryLoadDropsFromJson(jsonPath, out var jsonError);
            if (loadedFromJson)
            {
                info.SortDrops();
                return;
            }

            if (!string.IsNullOrWhiteSpace(jsonError) && File.Exists(jsonPath))
            {
                MessageQueue.Enqueue($"[Scripts] DragonDrops JSON 读取失败：{jsonPath} {jsonError}");
            }

            if (Settings.TxtScriptsLogDispatch && !File.Exists(jsonPath))
            {
                MessageQueue.Enqueue($"[Scripts][Load] DragonDrops -> 未提供 JSON 定义（path={jsonPath}）");
            }

            info.SortDrops();
        }

        private Notice LoadNoticeFromRootConfigOrEmpty()
        {
            var notice = new Notice();

            const string key = "notice";
            var scriptsRuntimeActive = Settings.CSharpScriptsEnabled && CSharpScripts.Enabled;
            var allowCSharp = scriptsRuntimeActive && RootConfigProvider != null;

            if (allowCSharp)
            {
                var definition = RootConfigProvider.Notice;
                if (definition != null)
                {
                    if (!string.IsNullOrWhiteSpace(definition.Title))
                        notice.Title = definition.Title;

                    FillNoticeFromLines(notice, definition.Lines);

                    if (definition.Lines.Count > 0 || !string.IsNullOrWhiteSpace(definition.Title))
                    {
                        notice.LastUpdate = Now;
                    }

                    return notice;
                }

                if (scriptsRuntimeActive && !Server.Scripting.TxtFallbackPolicy.ShouldFallbackToTxt(key))
                {
                    if (Settings.TxtScriptsLogDispatch)
                    {
                        var csharpState = CSharpScripts.Enabled ? $"v{CSharpScripts.Version}, handlers={CSharpScripts.LastRegisteredHandlerCount}" : $"不可用: {CSharpScripts.LastError}";
                        MessageQueue.Enqueue($"[Scripts][Load] Notice -> 阻止回落TXT（key={key}，C#={csharpState}）");
                    }

                    return notice;
                }
            }
            else if (scriptsRuntimeActive)
            {
                if (Settings.TxtScriptsLogDispatch)
                {
                    var csharpState = CSharpScripts.Enabled ? $"v{CSharpScripts.Version}, handlers={CSharpScripts.LastRegisteredHandlerCount}" : $"不可用: {CSharpScripts.LastError}";
                    MessageQueue.Enqueue($"[Scripts][Load] Notice -> 未提供 C# 定义（key={key}，C#={csharpState}）");
                }
            }
            return notice;
        }

        private static void FillNoticeFromLines(Notice notice, IReadOnlyList<string> lines)
        {
            if (notice == null) return;
            if (lines == null || lines.Count == 0) return;

            for (var i = 0; i < lines.Count; i++)
            {
                var line = lines[i] ?? string.Empty;

                if (string.Compare(line, "TITLE", false) > 0 && line.Contains("="))
                {
                    notice.Title = line.Split('=')[1];
                    continue;
                }

                notice.Message += line + "\r\n";
            }
        }

        private static object GetNameListFileLock(string fullFilePath)
        {
            return NameListFileLocks.GetOrAdd(fullFilePath, _ => new object());
        }

        private sealed class SetBuffEntry
        {
            public Stats BaseStats { get; }
            public HashSet<Stat> ExtraAllowedStats { get; }

            public SetBuffEntry(Stats baseStats, HashSet<Stat> extraAllowedStats)
            {
                BaseStats = baseStats ?? new Stats();
                ExtraAllowedStats = extraAllowedStats ?? new HashSet<Stat>();
            }
        }

        private enum SetBuffsCacheSourceKind : byte
        {
            None = 0,
            CSharp = 1,
        }

        private static readonly object SetBuffsCacheGate = new object();
        private static Dictionary<string, SetBuffEntry> SetBuffsCache = null;
        private static SetBuffsCacheSourceKind SetBuffsCacheSource = SetBuffsCacheSourceKind.None;
        private static DateTime SetBuffsCacheLastWriteTimeUtc = DateTime.MinValue;

        private static void ClearSetBuffsCache()
        {
            lock (SetBuffsCacheGate)
            {
                SetBuffsCache = null;
                SetBuffsCacheSource = SetBuffsCacheSourceKind.None;
                SetBuffsCacheLastWriteTimeUtc = DateTime.MinValue;
            }
        }

        private void EnsureSetBuffsCache()
        {
            const string key = "setbuffs";

            var scriptsRuntimeActive = Settings.CSharpScriptsEnabled && CSharpScripts.Enabled;
            var allowCSharp = scriptsRuntimeActive && ScriptDispatchPolicy.ShouldTryCSharp(key);

            lock (SetBuffsCacheGate)
            {
                var csharpDefinition = allowCSharp ? RootConfigProvider?.SetBuffs : null;
                if (csharpDefinition != null)
                {
                    if (SetBuffsCache != null && SetBuffsCacheSource == SetBuffsCacheSourceKind.CSharp)
                        return;

                    SetBuffsCache = BuildSetBuffsCacheFromLines(csharpDefinition.Lines);
                    SetBuffsCacheSource = SetBuffsCacheSourceKind.CSharp;
                    SetBuffsCacheLastWriteTimeUtc = DateTime.MaxValue;
                    return;
                }

                if (scriptsRuntimeActive && !Server.Scripting.TxtFallbackPolicy.ShouldFallbackToTxt(key))
                {
                    if (SetBuffsCache != null && SetBuffsCacheSource == SetBuffsCacheSourceKind.None)
                        return;

                    if (Settings.TxtScriptsLogDispatch)
                    {
                        var csharpState = CSharpScripts.Enabled ? $"v{CSharpScripts.Version}, handlers={CSharpScripts.LastRegisteredHandlerCount}" : $"不可用: {CSharpScripts.LastError}";
                        MessageQueue.Enqueue($"[Scripts][Load] SetBuffs -> 阻止回落TXT（key={key}，allowCSharp={allowCSharp}，C#={csharpState}）");
                    }

                    SetBuffsCache = new Dictionary<string, SetBuffEntry>(StringComparer.OrdinalIgnoreCase);
                    SetBuffsCacheSource = SetBuffsCacheSourceKind.None;
                    SetBuffsCacheLastWriteTimeUtc = DateTime.MaxValue;
                    return;
                }
                if (Settings.TxtScriptsLogDispatch && scriptsRuntimeActive)
                {
                    var csharpState = CSharpScripts.Enabled ? $"v{CSharpScripts.Version}, handlers={CSharpScripts.LastRegisteredHandlerCount}" : $"不可用: {CSharpScripts.LastError}";
                    MessageQueue.Enqueue($"[Scripts][Load] SetBuffs -> 未提供 C# 定义（key={key}，allowCSharp={allowCSharp}，C#={csharpState}）");
                }

                SetBuffsCache = new Dictionary<string, SetBuffEntry>(StringComparer.OrdinalIgnoreCase);
                SetBuffsCacheSource = SetBuffsCacheSourceKind.None;
                SetBuffsCacheLastWriteTimeUtc = DateTime.MaxValue;
            }
        }

        private static Dictionary<string, SetBuffEntry> BuildSetBuffsCacheFromLines(IReadOnlyList<string> lines)
        {
            var dict = new Dictionary<string, SetBuffEntry>(StringComparer.OrdinalIgnoreCase);

            if (lines == null || lines.Count == 0)
                return dict;

            for (var i = 0; i < lines.Count; i++)
            {
                var line = lines[i];
                if (string.IsNullOrWhiteSpace(line)) continue;
                if (line.StartsWith(";")) continue;

                var firstSep = line.IndexOf(';');
                if (firstSep <= 0) continue;

                var buffKey = line.Substring(0, firstSep).Trim();
                if (buffKey.Length == 0) continue;
                if (dict.ContainsKey(buffKey)) continue; // 保持 legacy：同 Key 只取第一行

                var stats = new Stats();
                var extraAllowedStats = new HashSet<Stat>();

                var statsPart = line.Substring(firstSep + 1).Trim(';');
                if (!string.IsNullOrWhiteSpace(statsPart))
                {
                    var entries = statsPart.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
                    for (var j = 0; j < entries.Length; j++)
                    {
                        var entry = entries[j];
                        var parts = entry.Split('=');
                        if (parts.Length != 2) continue;

                        var statName = parts[0].Trim();
                        var statValueText = parts[1].Trim();

                        if (string.IsNullOrWhiteSpace(statName)) continue;
                        if (string.IsNullOrWhiteSpace(statValueText)) continue;
                        if (!int.TryParse(statValueText, out var statValue)) continue;
                        if (!Enum.TryParse(statName, out Stat statEnum) || !Enum.IsDefined(typeof(Stat), statEnum)) continue;

                        if (statValue == 0)
                        {
                            stats[statEnum] = 0; // 删除已有值
                            extraAllowedStats.Add(statEnum);
                        }
                        else
                        {
                            stats[statEnum] = statValue;
                            extraAllowedStats.Remove(statEnum);
                        }
                    }
                }

                dict.Add(buffKey, new SetBuffEntry(stats, extraAllowedStats));
            }

            return dict;
        }

        public Stats LoadBuffStatsFromSetBuffs(string buffKey)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(buffKey)) return new Stats();

                EnsureSetBuffsCache();

                var key = buffKey.Trim();
                var cache = SetBuffsCache;

                if (cache != null && cache.TryGetValue(key, out var entry) && entry?.BaseStats != null)
                {
                    return new Stats(entry.BaseStats);
                }

                return new Stats();
            }
            catch (Exception ex)
            {
                MessageQueue.Enqueue("[Scripts] SetBuffs 读取失败：" + ex);
                return new Stats();
            }
        }

        public bool TryBuildBuffStatsFromSetBuffs(string buffKey, IReadOnlyList<string> extraStatPairs, out Stats buffStats)
        {
            buffStats = new Stats();

            try
            {
                if (string.IsNullOrWhiteSpace(buffKey))
                    return true;

                EnsureSetBuffsCache();

                var key = buffKey.Trim();
                var cache = SetBuffsCache;
                HashSet<Stat> extraAllowedStats = null;

                if (cache != null && cache.TryGetValue(key, out var entry) && entry != null)
                {
                    buffStats = entry.BaseStats != null ? new Stats(entry.BaseStats) : new Stats();
                    extraAllowedStats = entry.ExtraAllowedStats;
                }

                if (extraStatPairs == null || extraStatPairs.Count == 0)
                    return true;

                for (var i = 0; i < extraStatPairs.Count; i++)
                {
                    var kv = extraStatPairs[i];
                    if (string.IsNullOrWhiteSpace(kv)) continue;

                    var parts = kv.Split('=');
                    if (parts.Length != 2) continue;

                    var statName = parts[0].Trim();
                    var statValueText = parts[1].Trim();

                    if (string.IsNullOrWhiteSpace(statValueText)) continue;
                    if (!int.TryParse(statValueText, out var statValue)) continue;

                    if (!Enum.TryParse(statName, out Stat statEnum) || !Enum.IsDefined(typeof(Stat), statEnum))
                        continue;

                    if (extraAllowedStats == null || !extraAllowedStats.Contains(statEnum))
                        return false;

                    buffStats[statEnum] = statValue;
                }

                return true;
            }
            catch (Exception ex)
            {
                MessageQueue.Enqueue("[Scripts] SetBuffs 解析失败：" + ex);
                return false;
            }
        }

        private static bool TryGetRelativeNameListFileName(string filePath, out string relativeFileName)
        {
            relativeFileName = string.Empty;

            try
            {
                var nameListsRoot = Settings.NameListPath;
                if (string.IsNullOrWhiteSpace(nameListsRoot))
                    return false;

                var rootFull = Path.GetFullPath(nameListsRoot);
                if (!rootFull.EndsWith(Path.DirectorySeparatorChar))
                    rootFull += Path.DirectorySeparatorChar;

                var pathFull = Path.GetFullPath(filePath);
                if (!pathFull.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase))
                    return false;

                relativeFileName = pathFull.Substring(rootFull.Length);
                return !string.IsNullOrWhiteSpace(relativeFileName);
            }
            catch
            {
                return false;
            }
        }

        private static bool TryGetNameListFullPath(string listPath, out string fullPath)
        {
            fullPath = string.Empty;

            try
            {
                if (string.IsNullOrWhiteSpace(listPath)) return false;

                var root = Settings.NameListPath ?? string.Empty;
                if (string.IsNullOrWhiteSpace(root)) return false;

                var rootFull = Path.GetFullPath(root);
                if (!rootFull.EndsWith(Path.DirectorySeparatorChar))
                    rootFull += Path.DirectorySeparatorChar;

                var fileFull = Path.GetFullPath(Path.Combine(root, listPath));
                if (!fileFull.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase))
                    return false;

                fullPath = fileFull;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static string[] ReadNameListLines(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                return Array.Empty<string>();

            return File.Exists(filePath) ? File.ReadAllLines(filePath) : Array.Empty<string>();
        }

        internal bool IsCSharpNameListDefined(string listPath)
        {
            if (string.IsNullOrWhiteSpace(listPath)) return false;

            var scriptsRuntimeActive = Settings.CSharpScriptsEnabled && CSharpScripts.Enabled;
            if (!scriptsRuntimeActive) return false;

            var rawKey = $"NameLists/{listPath}";
            if (!ScriptDispatchPolicy.ShouldTryCSharp(rawKey)) return false;

            return NameListProvider?.GetByKey(rawKey) != null;
        }

        public bool NameListContains(string listPath, string value)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(listPath)) return false;
                value ??= string.Empty;

                var rawKey = $"NameLists/{listPath}";
                if (!LogicKey.TryNormalize(rawKey, out var normalizedKey))
                    return false;

                if (!TryGetNameListFullPath(listPath, out var fullPath))
                    return false;

                if (!Settings.CSharpScriptsEnabled)
                {
                    var dir = Path.GetDirectoryName(fullPath);
                    if (!string.IsNullOrWhiteSpace(dir))
                        Directory.CreateDirectory(dir);

                    lock (GetNameListFileLock(fullPath))
                    {
                        if (File.Exists(fullPath))
                        {
                            var lines = ReadNameListLines(fullPath);
                            return lines.Contains(value);
                        }
                    }

                    return false;
                }

                var scriptsRuntimeActive = CSharpScripts.Enabled;
                const bool allowLegacyImport = false;

                IReadOnlyCollection<string> seed = null;
                var allowCSharp = scriptsRuntimeActive && ScriptDispatchPolicy.ShouldTryCSharp(rawKey);
                if (allowCSharp)
                    seed = NameListProvider?.GetByKey(rawKey)?.Values;

                var jsonPath = Path.ChangeExtension(fullPath, ".json");
                var seedVersion = scriptsRuntimeActive ? CSharpScripts.Version : 0;

                return _nameListStore.Contains(normalizedKey, jsonPath, fullPath, allowLegacyImport, seed, seedVersion, value);
            }
            catch (Exception ex)
            {
                MessageQueue.Enqueue("[Scripts] NameLists Contains 失败：" + ex);
                return false;
            }
        }

        public bool NameListContainsFromFilePath(string filePath, string value)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(filePath)) return false;

                if (TryGetRelativeNameListFileName(filePath, out var relative))
                    return NameListContains(relative, value);

                // 非 NameLists 根目录下的文件：保持 legacy 行为。
                value ??= string.Empty;
                if (!File.Exists(filePath)) return false;
                var lines = ReadNameListLines(filePath);
                return lines.Contains(value);
            }
            catch (Exception ex)
            {
                MessageQueue.Enqueue("[Scripts] NameLists ContainsFromFilePath 失败：" + ex);
                return false;
            }
        }

        public bool AddNameToNameList(string listPath, string value)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(listPath)) return false;
                value ??= string.Empty;

                var rawKey = $"NameLists/{listPath}";
                if (!LogicKey.TryNormalize(rawKey, out var normalizedKey))
                    return false;

                if (!TryGetNameListFullPath(listPath, out var fullPath))
                    return false;

                if (!Settings.CSharpScriptsEnabled)
                {
                    var dir = Path.GetDirectoryName(fullPath);
                    if (!string.IsNullOrWhiteSpace(dir))
                        Directory.CreateDirectory(dir);

                    lock (GetNameListFileLock(fullPath))
                    {
                        if (!File.Exists(fullPath))
                        {
                            File.Create(fullPath).Close();
                        }

                        var lines = ReadNameListLines(fullPath);
                        if (lines.Contains(value)) return true;

                        using (var line = File.AppendText(fullPath))
                        {
                            line.WriteLine(value);
                        }

                        return true;
                    }
                }

                var scriptsRuntimeActive = CSharpScripts.Enabled;
                const bool allowLegacyImport = false;

                IReadOnlyCollection<string> seed = null;
                var allowCSharp = scriptsRuntimeActive && ScriptDispatchPolicy.ShouldTryCSharp(rawKey);
                if (allowCSharp)
                    seed = NameListProvider?.GetByKey(rawKey)?.Values;

                var jsonPath = Path.ChangeExtension(fullPath, ".json");
                var seedVersion = scriptsRuntimeActive ? CSharpScripts.Version : 0;

                return _nameListStore.Add(normalizedKey, jsonPath, fullPath, allowLegacyImport, seed, seedVersion, value);
            }
            catch (Exception ex)
            {
                MessageQueue.Enqueue("[Scripts] NameLists Add 失败：" + ex);
                return false;
            }
        }

        public bool AddNameToNameListFromFilePath(string filePath, string value)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(filePath)) return false;

                if (TryGetRelativeNameListFileName(filePath, out var relative))
                    return AddNameToNameList(relative, value);

                // 非 NameLists 根目录下的文件：保持 legacy 行为。
                value ??= string.Empty;

                var dir = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrWhiteSpace(dir))
                    Directory.CreateDirectory(dir);

                lock (GetNameListFileLock(Path.GetFullPath(filePath)))
                {
                    if (!File.Exists(filePath))
                        File.Create(filePath).Close();

                    var lines = ReadNameListLines(filePath);
                    if (lines.Contains(value)) return true;

                    using (var line = File.AppendText(filePath))
                    {
                        line.WriteLine(value);
                    }

                    return true;
                }
            }
            catch (Exception ex)
            {
                MessageQueue.Enqueue("[Scripts] NameLists AddFromFilePath 失败：" + ex);
                return false;
            }
        }

        public bool RemoveNameFromNameList(string listPath, string value)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(listPath)) return false;
                value ??= string.Empty;

                var rawKey = $"NameLists/{listPath}";
                if (!LogicKey.TryNormalize(rawKey, out var normalizedKey))
                    return false;

                if (!TryGetNameListFullPath(listPath, out var fullPath))
                    return false;

                if (!Settings.CSharpScriptsEnabled)
                {
                    var dir = Path.GetDirectoryName(fullPath);
                    if (!string.IsNullOrWhiteSpace(dir))
                        Directory.CreateDirectory(dir);

                    lock (GetNameListFileLock(fullPath))
                    {
                        if (!File.Exists(fullPath))
                            return false;

                        var lines = ReadNameListLines(fullPath);
                        if (!lines.Contains(value)) return true;

                        File.WriteAllLines(fullPath, lines.Where(l => l != value).ToArray());
                        return true;
                    }
                }

                var scriptsRuntimeActive = CSharpScripts.Enabled;
                const bool allowLegacyImport = false;

                IReadOnlyCollection<string> seed = null;
                var allowCSharp = scriptsRuntimeActive && ScriptDispatchPolicy.ShouldTryCSharp(rawKey);
                if (allowCSharp)
                    seed = NameListProvider?.GetByKey(rawKey)?.Values;

                var jsonPath = Path.ChangeExtension(fullPath, ".json");
                var seedVersion = scriptsRuntimeActive ? CSharpScripts.Version : 0;

                return _nameListStore.Remove(normalizedKey, jsonPath, fullPath, allowLegacyImport, seed, seedVersion, value);
            }
            catch (Exception ex)
            {
                MessageQueue.Enqueue("[Scripts] NameLists Remove 失败：" + ex);
                return false;
            }
        }

        public bool RemoveNameFromNameListFromFilePath(string filePath, string value)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(filePath)) return false;

                if (TryGetRelativeNameListFileName(filePath, out var relative))
                    return RemoveNameFromNameList(relative, value);

                // 非 NameLists 根目录下的文件：保持 legacy 行为。
                value ??= string.Empty;
                if (!File.Exists(filePath)) return false;

                lock (GetNameListFileLock(Path.GetFullPath(filePath)))
                {
                    var lines = ReadNameListLines(filePath);
                    if (!lines.Contains(value)) return true;

                    File.WriteAllLines(filePath, lines.Where(l => l != value).ToArray());
                    return true;
                }
            }
            catch (Exception ex)
            {
                MessageQueue.Enqueue("[Scripts] NameLists RemoveFromFilePath 失败：" + ex);
                return false;
            }
        }

        public bool ClearNameList(string listPath)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(listPath)) return false;

                var rawKey = $"NameLists/{listPath}";
                if (!LogicKey.TryNormalize(rawKey, out var normalizedKey))
                    return false;

                if (!TryGetNameListFullPath(listPath, out var fullPath))
                    return false;

                if (!Settings.CSharpScriptsEnabled)
                {
                    var dir = Path.GetDirectoryName(fullPath);
                    if (!string.IsNullOrWhiteSpace(dir))
                        Directory.CreateDirectory(dir);

                    lock (GetNameListFileLock(fullPath))
                    {
                        if (!File.Exists(fullPath))
                            return false;

                        File.WriteAllLines(fullPath, Array.Empty<string>());
                        return true;
                    }
                }

                var scriptsRuntimeActive = CSharpScripts.Enabled;
                const bool allowLegacyImport = false;

                IReadOnlyCollection<string> seed = null;
                var allowCSharp = scriptsRuntimeActive && ScriptDispatchPolicy.ShouldTryCSharp(rawKey);
                if (allowCSharp)
                    seed = NameListProvider?.GetByKey(rawKey)?.Values;

                var jsonPath = Path.ChangeExtension(fullPath, ".json");
                var seedVersion = scriptsRuntimeActive ? CSharpScripts.Version : 0;

                return _nameListStore.Clear(normalizedKey, jsonPath, fullPath, allowLegacyImport, seed, seedVersion);
            }
            catch (Exception ex)
            {
                MessageQueue.Enqueue("[Scripts] NameLists Clear 失败：" + ex);
                return false;
            }
        }

        public bool ClearNameListFromFilePath(string filePath)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(filePath)) return false;

                if (TryGetRelativeNameListFileName(filePath, out var relative))
                    return ClearNameList(relative);

                // 非 NameLists 根目录下的文件：保持 legacy 行为。
                if (!File.Exists(filePath)) return false;

                lock (GetNameListFileLock(Path.GetFullPath(filePath)))
                {
                    File.WriteAllLines(filePath, Array.Empty<string>());
                    return true;
                }
            }
            catch (Exception ex)
            {
                MessageQueue.Enqueue("[Scripts] NameLists ClearFromFilePath 失败：" + ex);
                return false;
            }
        }

        public void Start()
        {
            if (Running || _thread != null) return;

            if (Settings.CSharpScriptsEnabled)
            {
                try
                {
                    CSharpScripts.LogDiagnostics = Settings.CSharpScriptsLogDiagnostics;

                    var hotReloadEnabled = Settings.CSharpScriptsHotReloadEnabled && !Settings.CSharpScriptsPushModeEnabled;
                    CSharpScripts.Start(
                        scriptsRootPath: Settings.CSharpScriptsPath,
                        hotReloadEnabled: hotReloadEnabled,
                        debounceMs: Settings.CSharpScriptsDebounceMs);

                    if (Settings.CSharpScriptsPushModeEnabled)
                    {
                        MessageQueue.Enqueue("[Scripts] 推送模式已启用：已禁用 ScriptWatcher 与 @ReloadScripts，请使用管理端“脚本调试...”推送并热更。");
                    }
                }
                catch (Exception ex)
                {
                    MessageQueue.Enqueue("[Scripts] 启动失败：" + ex);
                }
            }

            Running = true;

            _thread = new Thread(WorkLoop) { IsBackground = true };
            _thread.Start();

        }
        public void Stop()
        {
            Running = false;

            try
            {
                CSharpScripts.Stop();
            }
            catch (Exception ex)
            {
                MessageQueue.Enqueue("[Scripts] 停止失败：" + ex);
            }

            lock (_locker)
            {
                // changing a blocking condition. (this makes the threads wake up!)
                Monitor.PulseAll(_locker);
            }

            //simply intterupt all the mob threads if they are running (will give an invisible error on them but fastest way of getting rid of them on shutdowns)
            for (var i = 1; i < MobThreading.Length; i++)
            {
                if (MobThreads[i] != null)
                {
                    MobThreads[i].EndTime = Time + 9999;
                }
                if (MobThreading[i] != null &&
                    MobThreading[i].ThreadState != System.Threading.ThreadState.Stopped && MobThreading[i].ThreadState != System.Threading.ThreadState.Unstarted)
                {
                    //lyo：Interrupt方法在aot中不适用
                    this.Running = false;

                    //MobThreading[i].Interrupt();
                }
            }

            http?.Stop();

            while (_thread != null)
                Thread.Sleep(1);
        }

        public void Reboot()
        {
            new Thread(() =>
            {
                MessageQueue.Enqueue("服务器正在重启...");
                Stop();
                Start();
            }).Start();
        }

        public void UpdateIPBlock(string ipAddress, TimeSpan value)
        {
            IPBlocks[ipAddress] = Now.Add(value);
        }

        private bool TryGetRelativeValueFileName(string filePath, out string relativeFileName)
        {
            relativeFileName = null;

            if (string.IsNullOrWhiteSpace(filePath))
                return false;

            try
            {
                var root = Settings.ValuePath ?? string.Empty;
                if (string.IsNullOrWhiteSpace(root))
                    return false;

                var rootFull = Path.GetFullPath(root);
                var pathFull = Path.GetFullPath(filePath);

                if (!rootFull.EndsWith(Path.DirectorySeparatorChar))
                    rootFull += Path.DirectorySeparatorChar;

                if (!pathFull.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase))
                    return false;

                relativeFileName = pathFull.Substring(rootFull.Length);
                return !string.IsNullOrWhiteSpace(relativeFileName);
            }
            catch
            {
                return false;
            }
        }

        private static bool IsTxtUnderEnvirPath(string filePath)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(filePath))
                    return false;

                var fullPath = Path.GetFullPath(filePath);
                if (!fullPath.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
                    return false;

                var envirRoot = Path.GetFullPath(Settings.EnvirPath ?? string.Empty);
                if (string.IsNullOrWhiteSpace(envirRoot))
                    return false;

                if (!envirRoot.EndsWith(Path.DirectorySeparatorChar))
                    envirRoot += Path.DirectorySeparatorChar;

                return fullPath.StartsWith(envirRoot, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private bool ShouldBlockLegacyValueFilePath(string filePath, string traceName)
        {
            if (!Settings.CSharpScriptsEnabled || !CSharpScripts.Enabled || Settings.CSharpScriptsFallbackToTxt)
                return false;

            if (!IsTxtUnderEnvirPath(filePath))
                return false;

            if (Settings.TxtScriptsLogDispatch)
            {
                var csharpState = CSharpScripts.Enabled ? $"v{CSharpScripts.Version}, handlers={CSharpScripts.LastRegisteredHandlerCount}" : $"不可用: {CSharpScripts.LastError}";
                MessageQueue.Enqueue($"[Scripts][Load] Values {traceName} -> 阻止绕过回落TXT（file={filePath}，C#={csharpState}）");
            }

            return true;
        }

        private static string ReadIniValueFromTextFile(string filePath, string section, string key, string defaultValue)
        {
            if (string.IsNullOrWhiteSpace(filePath) || string.IsNullOrWhiteSpace(key))
                return defaultValue ?? string.Empty;

            if (!File.Exists(filePath))
                return defaultValue ?? string.Empty;

            var lines = File.ReadAllLines(filePath);
            var targetSection = section ?? string.Empty;
            var currentSection = string.Empty;

            for (var i = 0; i < lines.Length; i++)
            {
                var raw = lines[i];
                if (string.IsNullOrWhiteSpace(raw))
                    continue;

                var trimmed = raw.Trim();
                if (trimmed.Length == 0 || trimmed.StartsWith(";", StringComparison.Ordinal))
                    continue;

                if (trimmed.StartsWith("[", StringComparison.Ordinal) &&
                    trimmed.EndsWith("]", StringComparison.Ordinal) &&
                    trimmed.Length >= 2)
                {
                    currentSection = trimmed.Substring(1, trimmed.Length - 2);
                    continue;
                }

                if (!string.Equals(currentSection, targetSection, StringComparison.Ordinal))
                    continue;

                var eqIndex = trimmed.IndexOf('=');
                if (eqIndex <= 0)
                    continue;

                var currentKey = trimmed.Substring(0, eqIndex);
                if (!string.Equals(currentKey, key, StringComparison.Ordinal))
                    continue;

                return eqIndex + 1 < trimmed.Length ? trimmed.Substring(eqIndex + 1) : string.Empty;
            }

            return defaultValue ?? string.Empty;
        }

        public string LoadValue(string fileName, string section, string key, string defaultValue = "", bool writeWhenNull = true)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(fileName)) return defaultValue ?? string.Empty;
                if (string.IsNullOrWhiteSpace(key)) return defaultValue ?? string.Empty;

                var rawTableKey = $"Values/{fileName}";
                if (!LogicKey.TryNormalize(rawTableKey, out var normalizedTableKey))
                    return defaultValue ?? string.Empty;

                var valuesRoot = Settings.ValuePath ?? string.Empty;
                if (string.IsNullOrWhiteSpace(valuesRoot))
                    return defaultValue ?? string.Empty;

                var legacyTxtPath = Path.GetFullPath(Path.Combine(valuesRoot, fileName));

                // 二次校验：防止 fileName 通过奇怪的分隔符/相对段逃逸出 Values 根目录。
                var rootFull = Path.GetFullPath(valuesRoot);
                if (!rootFull.EndsWith(Path.DirectorySeparatorChar))
                    rootFull += Path.DirectorySeparatorChar;

                if (!legacyTxtPath.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase))
                    return defaultValue ?? string.Empty;

                var jsonPath = Path.ChangeExtension(legacyTxtPath, ".json");

                var scriptsRuntimeActive = Settings.CSharpScriptsEnabled && CSharpScripts.Enabled;
                const bool allowLegacyImport = false;

                if (_valueStore.TryGet(normalizedTableKey, jsonPath, legacyTxtPath, allowLegacyImport, section, key, out var storedValue))
                    return storedValue;

                var allowCSharpDefaults = scriptsRuntimeActive && ScriptDispatchPolicy.ShouldTryCSharp(rawTableKey);
                if (allowCSharpDefaults && ValueProvider != null &&
                    ValueProvider.TryGet(rawTableKey, section, key, out var csharpValue) &&
                    !string.IsNullOrEmpty(csharpValue))
                {
                    if (writeWhenNull)
                    {
                        _valueStore.Set(normalizedTableKey, jsonPath, legacyTxtPath, allowLegacyImport, section, key, csharpValue);
                    }

                    return csharpValue;
                }

                var dv = defaultValue ?? string.Empty;

                if (writeWhenNull)
                {
                    _valueStore.Set(normalizedTableKey, jsonPath, legacyTxtPath, allowLegacyImport, section, key, dv);
                }

                return dv;
            }
            catch (Exception ex)
            {
                MessageQueue.Enqueue("[Scripts] Values LoadValue 失败：" + ex);
                return defaultValue ?? string.Empty;
            }
        }

        public string LoadValueFromFilePath(string filePath, string section, string key, string defaultValue = "", bool writeWhenNull = true)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(filePath)) return defaultValue ?? string.Empty;

                if (TryGetRelativeValueFileName(filePath, out var relative))
                    return LoadValue(relative, section, key, defaultValue, writeWhenNull);

                if (ShouldBlockLegacyValueFilePath(filePath, "LegacyIniFilePath"))
                    return defaultValue ?? string.Empty;

                if (IsTxtUnderEnvirPath(filePath))
                    return ReadIniValueFromTextFile(filePath, section, key, defaultValue);

                // 非 Values 根目录下的文件：保持 legacy 行为。
                var reader = new InIReader(filePath);
                return reader.ReadString(section ?? string.Empty, key, defaultValue ?? string.Empty, writeWhenNull);
            }
            catch (Exception ex)
            {
                MessageQueue.Enqueue("[Scripts] Values LoadValueFromFilePath 失败：" + ex);
                return defaultValue ?? string.Empty;
            }
        }

        public bool SaveValue(string fileName, string section, string key, string value)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(fileName)) return false;
                if (string.IsNullOrWhiteSpace(key)) return false;

                var rawTableKey = $"Values/{fileName}";
                if (!LogicKey.TryNormalize(rawTableKey, out var normalizedTableKey))
                    return false;

                var valuesRoot = Settings.ValuePath ?? string.Empty;
                if (string.IsNullOrWhiteSpace(valuesRoot))
                    return false;

                var legacyTxtPath = Path.GetFullPath(Path.Combine(valuesRoot, fileName));

                var rootFull = Path.GetFullPath(valuesRoot);
                if (!rootFull.EndsWith(Path.DirectorySeparatorChar))
                    rootFull += Path.DirectorySeparatorChar;

                if (!legacyTxtPath.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase))
                    return false;

                var jsonPath = Path.ChangeExtension(legacyTxtPath, ".json");

                var scriptsRuntimeActive = Settings.CSharpScriptsEnabled && CSharpScripts.Enabled;
                const bool allowLegacyImport = false;

                _valueStore.Set(normalizedTableKey, jsonPath, legacyTxtPath, allowLegacyImport, section, key, value ?? string.Empty);
                return true;
            }
            catch (Exception ex)
            {
                MessageQueue.Enqueue("[Scripts] Values SaveValue 失败：" + ex);
                return false;
            }
        }

        public bool SaveValueFromFilePath(string filePath, string section, string key, string value)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(filePath)) return false;

                if (TryGetRelativeValueFileName(filePath, out var relative))
                    return SaveValue(relative, section, key, value);

                if (ShouldBlockLegacyValueFilePath(filePath, "LegacyIniFilePath"))
                    return false;

                // 非 Values 根目录下的文件：保持 legacy 行为。
                var reader = new InIReader(filePath);
                reader.Write(section ?? string.Empty, key, value ?? string.Empty);
                return true;
            }
            catch (Exception ex)
            {
                MessageQueue.Enqueue("[Scripts] Values SaveValueFromFilePath 失败：" + ex);
                return false;
            }
        }

        private ulong GetOrCreateRecipeUniqueId(int itemIndex)
        {
            if (itemIndex <= 0) return 0;

            if (_recipeUniqueIdByItemIndex.TryGetValue(itemIndex, out var uniqueId) && uniqueId > 0)
                return uniqueId;

            uniqueId = ++NextRecipeID;
            _recipeUniqueIdByItemIndex[itemIndex] = uniqueId;
            return uniqueId;
        }

        private bool TryBuildRecipeFromDefinition(RecipeDefinition definition, string recipeName, out RecipeInfo recipe, out string error)
        {
            recipe = null;
            error = string.Empty;

            if (definition == null)
            {
                error = "RecipeDefinition 不能为空。";
                return false;
            }

            if (string.IsNullOrWhiteSpace(definition.Key))
            {
                error = "RecipeDefinition.Key 不能为空。";
                return false;
            }

            if (!LogicKey.TryNormalize($"Recipe/{recipeName}", out var expectedKey))
            {
                error = $"配方名无效：{recipeName}";
                return false;
            }

            if (!string.Equals(definition.Key, expectedKey, StringComparison.Ordinal))
            {
                error = $"配方定义 Key 与配方名不匹配：expected={expectedKey} actual={definition.Key}";
                return false;
            }

            if (definition.Amount == 0)
            {
                error = "Amount 必须 > 0。";
                return false;
            }

            var itemInfo = GetItemInfo(recipeName);
            if (itemInfo == null)
            {
                error = $"缺少物品：{recipeName}";
                return false;
            }

            var uniqueId = GetOrCreateRecipeUniqueId(itemInfo.Index);
            if (uniqueId == 0)
            {
                error = $"无法分配配方 UniqueID：itemIndex={itemInfo.Index}";
                return false;
            }

            recipe = new RecipeInfo();

            var goods = CreateShopItem(itemInfo, uniqueId);
            if (goods == null)
            {
                error = $"创建产出物品失败：{recipeName}";
                recipe = null;
                return false;
            }

            goods.Count = definition.Amount;

            recipe.Item = goods;
            recipe.Chance = definition.Chance > 100 ? (byte)100 : definition.Chance;
            recipe.Gold = definition.Gold;

            recipe.RequiredLevel = definition.RequiredLevel;
            recipe.RequiredGender = definition.RequiredGender;
            recipe.RequiredClass.AddRange(definition.RequiredClass);
            recipe.RequiredFlag.AddRange(definition.RequiredFlag);
            recipe.RequiredQuest.AddRange(definition.RequiredQuest);

            for (var i = 0; i < definition.Tools.Count; i++)
            {
                var toolName = definition.Tools[i]?.Trim();
                if (string.IsNullOrWhiteSpace(toolName)) continue;

                var toolInfo = GetItemInfo(toolName);
                if (toolInfo == null) continue;

                var toolItem = CreateShopItem(toolInfo, 0);
                if (toolItem != null)
                {
                    recipe.Tools.Add(toolItem);
                }
            }

            for (var i = 0; i < definition.Ingredients.Count; i++)
            {
                var ingredient = definition.Ingredients[i];
                var ingredientName = ingredient.ItemName?.Trim();
                if (string.IsNullOrWhiteSpace(ingredientName)) continue;
                if (ingredient.Count == 0) continue;

                var ingredientInfo = GetItemInfo(ingredientName);
                if (ingredientInfo == null) continue;

                var ingredientItem = CreateShopItem(ingredientInfo, 0);
                if (ingredientItem == null) continue;

                var ingredientCount = ingredient.Count;
                if (ingredientCount > ingredientInfo.StackSize)
                    ingredientCount = ingredientInfo.StackSize;

                ingredientItem.Count = ingredientCount;

                if (ingredient.RequiredDura > 0 && ingredient.RequiredDura < ingredientItem.MaxDura)
                {
                    ingredientItem.CurrentDura = ingredient.RequiredDura;
                }

                recipe.Ingredients.Add(ingredientItem);
            }

            return true;
        }

        private void RefreshNpcCraftGoodsAfterRecipeReload(IReadOnlyList<RecipeInfo> recipes)
        {
            if (Scripts == null || Scripts.Count == 0)
                return;

            var recipeByItemIndex = new Dictionary<int, RecipeInfo>(recipes?.Count ?? 0);

            if (recipes != null)
            {
                for (var i = 0; i < recipes.Count; i++)
                {
                    var recipe = recipes[i];
                    var item = recipe?.Item;
                    if (item == null) continue;
                    recipeByItemIndex[item.ItemIndex] = recipe;
                }
            }

            foreach (var script in Scripts.Values)
            {
                if (script?.CraftGoods == null || script.CraftGoods.Count == 0)
                    continue;

                for (var i = script.CraftGoods.Count - 1; i >= 0; i--)
                {
                    var oldRecipe = script.CraftGoods[i];
                    var oldItem = oldRecipe?.Item;
                    if (oldItem == null)
                    {
                        script.CraftGoods.RemoveAt(i);
                        continue;
                    }

                    if (recipeByItemIndex.TryGetValue(oldItem.ItemIndex, out var newRecipe))
                    {
                        script.CraftGoods[i] = newRecipe;
                    }
                    else
                    {
                        script.CraftGoods.RemoveAt(i);
                    }
                }
            }
        }

        private void ReloadRecipes(out int total, out int csharpCount, out int txtCount, out int skipped, out int errorCount)
        {
            total = 0;
            csharpCount = 0;
            txtCount = 0;
            skipped = 0;
            errorCount = 0;

            var scriptsRuntimeActive = Settings.CSharpScriptsEnabled && CSharpScripts.Enabled;
            var provider = RecipeProvider;

            var list = new List<RecipeInfo>();

            var definitions = provider?.GetAll();

            if (definitions != null)
            {
                foreach (var definition in definitions)
                {
                    if (definition == null) continue;

                    var normalizedKey = definition.Key;
                    if (string.IsNullOrWhiteSpace(normalizedKey)) continue;

                    if (!normalizedKey.StartsWith("recipe/", StringComparison.Ordinal))
                        continue;

                    var allowCSharp = scriptsRuntimeActive && ScriptDispatchPolicy.ShouldTryCSharp(normalizedKey);
                    if (!allowCSharp)
                    {
                        skipped++;
                        continue;
                    }

                    var recipeName = normalizedKey.Substring("recipe/".Length);

                    if (string.IsNullOrWhiteSpace(recipeName) || recipeName.Contains('/'))
                    {
                        errorCount++;
                        MessageQueue.Enqueue($"[Scripts] Recipe Key 无效：{normalizedKey}");
                        continue;
                    }

                    if (!TryBuildRecipeFromDefinition(definition, recipeName, out var builtRecipe, out var buildError))
                    {
                        // 产出物品不存在：直接跳过（否则会刷屏；且旧逻辑也无法正常工作）。
                        if (buildError.StartsWith("缺少物品：", StringComparison.Ordinal))
                        {
                            skipped++;
                            continue;
                        }

                        errorCount++;
                        if (Settings.TxtScriptsLogDispatch)
                        {
                            MessageQueue.Enqueue($"[Scripts] Recipe 构建失败：{normalizedKey} {buildError}");
                        }
                        continue;
                    }

                    list.Add(builtRecipe);
                    csharpCount++;
                }
            }

            var snapshot = list.ToArray();
            Volatile.Write(ref _recipeInfoList, snapshot);

            RefreshNpcCraftGoodsAfterRecipeReload(snapshot);

            total = snapshot.Length;
        }

        private void StartEnvir()
        {
            Players.Clear();
            StartPoints.Clear();
            StartItems.Clear();
            MapList.Clear();
            GameshopLog.Clear();
            CustomCommands.Clear();
            Heroes.Clear();
            MonsterCount = 0;

            LoadDB();

            BuffInfoList.Clear();
            foreach (var buff in BuffInfo.Load())
            {
                BuffInfoList.Add(buff);
            }

            MessageQueue.Enqueue($"游戏特效 {BuffInfoList.Count}种 加载完成");

            ReloadRecipes(out _, out _, out _, out _, out _);

            MessageQueue.Enqueue($"合成配方 {RecipeInfoList.Count}个 加载完成");

            for (var i = 0; i < MapInfoList.Count; i++)
            {
                MapInfoList[i].CreateMap();
            }
            MessageQueue.Enqueue($"地图文件 {MapInfoList.Count}张 加载完成");

            for (var i = 0; i < ItemInfoList.Count; i++)
            {
                if (ItemInfoList[i].StartItem)
                {
                    StartItems.Add(ItemInfoList[i]);
                }
            }

            ReloadDrops();

            LoadDisabledChars();
            LoadLineMessages();

            if (DragonInfo.Enabled)
            {
                DragonSystem = new Dragon(DragonInfo);
                if (DragonSystem != null)
                {
                    if (DragonSystem.Load()) DragonSystem.Info.LoadDrops();
                }

                MessageQueue.Enqueue("破天魔龙加载完成");
            }

            DefaultNPC = NPCScript.GetOrAdd((uint)Random.Next(1000000, 1999999), Settings.DefaultNPCFilename, NPCScriptType.AutoPlayer);
            MonsterNPC = NPCScript.GetOrAdd((uint)Random.Next(2000000, 2999999), Settings.MonsterNPCFilename, NPCScriptType.AutoMonster);
            RobotNPC = NPCScript.GetOrAdd((uint)Random.Next(3000000, 3999999), Settings.RobotNPCFilename, NPCScriptType.Robot);

            MessageQueue.Enqueue("正在部署游戏环境......");
        }
        private void StartNetwork()
        {
            Connections.Clear();
            StatusConnections.Clear();

            ReleaseListener(ref _listener);
            ReleaseListener(ref _StatusPort);

            LoadAccounts();

            LoadGuilds();

            LoadConquests();

            IPAddress listenAddress = IPAddress.Parse(Settings.IPAddress);
            _listener = CreateStartedListener(listenAddress, Settings.Port, "游戏端口");
            _listener.BeginAcceptTcpClient(Connection, null);

            if (StatusPortEnabled)
            {
                _StatusPort = CreateStartedListener(listenAddress, 3000, "状态端口");
                _StatusPort.BeginAcceptTcpClient(StatusConnection, null);
            }

            MessageQueue.Enqueue("网络设置加载完成");
        }

        private void StopEnvir()
        {
            SaveGoods(true);

            MapList.Clear();
            StartPoints.Clear();
            StartItems.Clear();
            Objects.Clear();
            Players.Clear();
            Heroes.Clear();

            CleanUp();

            GC.Collect();

            MessageQueue.Enqueue("游戏服务已停止");
        }
        private void StopNetwork()
        {
            ReleaseListener(ref _listener);

            lock (Connections)
            {
                for (var i = Connections.Count - 1; i >= 0; i--)
                    Connections[i].SendDisconnect(0);
            }

            ReleaseListener(ref _StatusPort);

            if (StatusPortEnabled)
            {
                for (var i = StatusConnections.Count - 1; i >= 0; i--)
                    StatusConnections[i].SendDisconnect();
            }

            var expire = Time + 5000;

            while (Connections.Count != 0 && Stopwatch.ElapsedMilliseconds < expire)
            {
                Time = Stopwatch.ElapsedMilliseconds;

                for (var i = Connections.Count - 1; i >= 0; i--)
                    Connections[i].Process();

                Thread.Sleep(1);
            }


            Connections.Clear();

            expire = Time + 10000;
            while (StatusConnections.Count != 0 && Stopwatch.ElapsedMilliseconds < expire)
            {
                Time = Stopwatch.ElapsedMilliseconds;

                for (var i = StatusConnections.Count - 1; i >= 0; i--)
                    StatusConnections[i].Process();

                Thread.Sleep(1);
            }


            StatusConnections.Clear();
            MessageQueue.Enqueue("网络已停止");
        }

        private static TcpListener CreateStartedListener(IPAddress address, int port, string label)
        {
            const int maxAttempts = 5;
            Exception lastError = null;

            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                TcpListener listener = null;
                try
                {
                    listener = new TcpListener(address, port);

                    try
                    {
                        listener.Server.ExclusiveAddressUse = false;
                        listener.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                    }
                    catch
                    {
                    }

                    listener.Start();
                    return listener;
                }
                catch (SocketException ex) when (ex.SocketErrorCode == SocketError.AddressAlreadyInUse && attempt < maxAttempts)
                {
                    lastError = ex;
                    try
                    {
                        listener?.Stop();
                    }
                    catch
                    {
                    }

                    Thread.Sleep(250 * attempt);
                }
                catch (Exception ex)
                {
                    try
                    {
                        listener?.Stop();
                    }
                    catch
                    {
                    }

                    throw new InvalidOperationException($"{label}启动失败：{address}:{port}", ex);
                }
            }

            throw new InvalidOperationException($"{label}启动失败：{address}:{port}", lastError);
        }

        private static void ReleaseListener(ref TcpListener listener)
        {
            TcpListener current = listener;
            listener = null;

            if (current == null)
                return;

            try
            {
                current.Stop();
            }
            catch
            {
            }
        }

        private void CleanUp()
        {
            for (var i = 0; i < CharacterList.Count; i++)
            {
                var info = CharacterList[i];

                if (info.Deleted)
                {
                    #region Mentor Cleanup
                    if (info.Mentor > 0)
                    {
                        var mentor = GetCharacterInfo(info.Mentor);

                        if (mentor != null)
                        {
                            mentor.Mentor = 0;
                            mentor.MentorExp = 0;
                            mentor.IsMentor = false;
                        }

                        info.Mentor = 0;
                        info.MentorExp = 0;
                        info.IsMentor = false;
                    }
                    #endregion

                    #region Marriage Cleanup
                    if (info.Married > 0)
                    {
                        var Lover = GetCharacterInfo(info.Married);

                        info.Married = 0;
                        info.MarriedDate = Now;

                        Lover.Married = 0;
                        Lover.MarriedDate = Now;
                        if (Lover.Equipment[(int)EquipmentSlot.左戒指] != null)
                            Lover.Equipment[(int)EquipmentSlot.左戒指].WeddingRing = -1;
                    }
                    #endregion
                }

                if (info.Mail.Count > Settings.MailCapacity)
                {
                    for (var j = info.Mail.Count - 1 - (int)Settings.MailCapacity; j >= 0; j--)
                    {
                        if (info.Mail[j].DateOpened > Now && info.Mail[j].Collected && info.Mail[j].Items.Count == 0 && info.Mail[j].Gold == 0)
                        {
                            info.Mail.Remove(info.Mail[j]);
                        }
                    }
                }
            }
        }

        private void Connection(IAsyncResult result)
        {
            try
            {
                if (!Running || !_listener.Server.IsBound) return;
            }
            catch (Exception e)
            {
                MessageQueue.Enqueue(e.ToString());
            }

            try
            {
                var tempTcpClient = _listener.EndAcceptTcpClient(result);

                bool connected = false;
                var ipAddress = tempTcpClient.Client.RemoteEndPoint.ToString().Split(':')[0];

                if (!IPBlocks.TryGetValue(ipAddress, out DateTime banDate) || banDate < Now)
                {
                    int count = 0;

                    for (int i = 0; i < Connections.Count; i++)
                    {
                        var connection = Connections[i];

                        if (!connection.Connected || connection.IPAddress != ipAddress)
                            continue;

                        count++;
                    }

                    if (count >= Settings.MaxIP)
                    {
                        UpdateIPBlock(ipAddress, TimeSpan.FromSeconds(Settings.IPBlockSeconds));

                        MessageQueue.Enqueue(ipAddress + " 已断开连接，连接次数过于频繁");
                    }
                    else
                    {
                        var tempConnection = new MirConnection(++_sessionID, tempTcpClient);
                        if (tempConnection.Connected)
                        {
                            connected = true;
                            lock (Connections)
                                Connections.Add(tempConnection);
                        }
                    }
                }

                if (!connected)
                    tempTcpClient.Close();
            }
            catch (Exception ex)
            {
                MessageQueue.Enqueue(ex);
            }
            finally
            {
                while (Connections.Count >= Settings.MaxUser)
                    Thread.Sleep(1);

                if (Running && _listener.Server.IsBound)
                    _listener.BeginAcceptTcpClient(Connection, null);
            }
        }

        private void StatusConnection(IAsyncResult result)
        {
            if (!Running || !_StatusPort.Server.IsBound) return;

            try
            {
                var tempTcpClient = _StatusPort.EndAcceptTcpClient(result);
                lock (StatusConnections)
                    StatusConnections.Add(new MirStatusConnection(tempTcpClient));
            }
            catch (Exception ex)
            {
                MessageQueue.Enqueue(ex);
            }
            finally
            {
                while (StatusConnections.Count >= 5) //dont allow to many status port connections it's just an abuse thing
                    Thread.Sleep(1);

                if (Running && _StatusPort.Server.IsBound)
                    _StatusPort.BeginAcceptTcpClient(StatusConnection, null);
            }
        }

        public void NewAccount(ClientPackets.NewAccount p, MirConnection c)
        {
            if (!Settings.AllowNewAccount)
            {
                c.Enqueue(new ServerPackets.NewAccount { Result = 0 });
                return;
            }

            
            if (ConnectionLogs.TryGetValue(c.IPAddress, out MirConnectionLog currentlog))
            {
                if (currentlog.AccountsMade.Count > 2)
                {
                    IPBlocks[c.IPAddress] = Now.AddHours(24);
                    c.Enqueue(new ServerPackets.NewAccount { Result = 0 });
                    return;
                }
                currentlog.AccountsMade.Add(Time);
                for (int i = 0; i < currentlog.AccountsMade.Count; i++)
                {
                    if ((currentlog.AccountsMade[i] + 60 * 60 * 1000) < Time)
                    {
                        currentlog.AccountsMade.RemoveAt(i);
                        break;
                    }
                }
            }
            else
            {
                ConnectionLogs[c.IPAddress] = new MirConnectionLog() { IPAddress = c.IPAddress};
            }


            if (!AccountIDReg.IsMatch(p.AccountID))
            {
                c.Enqueue(new ServerPackets.NewAccount { Result = 1 });
                return;
            }

            if (!PasswordReg.IsMatch(p.Password))
            {
                c.Enqueue(new ServerPackets.NewAccount { Result = 2 });
                return;
            }
            if (!string.IsNullOrWhiteSpace(p.EMailAddress) && !EMailReg.IsMatch(p.EMailAddress) ||
                p.EMailAddress.Length > 50)
            {
                c.Enqueue(new ServerPackets.NewAccount { Result = 3 });
                return;
            }

            if (!string.IsNullOrWhiteSpace(p.UserName) && p.UserName.Length > 20)
            {
                c.Enqueue(new ServerPackets.NewAccount { Result = 4 });
                return;
            }

            if (!string.IsNullOrWhiteSpace(p.SecretQuestion) && p.SecretQuestion.Length > 30)
            {
                c.Enqueue(new ServerPackets.NewAccount { Result = 5 });
                return;
            }

            if (!string.IsNullOrWhiteSpace(p.SecretAnswer) && p.SecretAnswer.Length > 30)
            {
                c.Enqueue(new ServerPackets.NewAccount { Result = 6 });
                return;
            }

            lock (AccountLock)
            {
                if (AccountExists(p.AccountID))
                {
                    c.Enqueue(new ServerPackets.NewAccount { Result = 7 });
                    return;
                }

                AccountList.Add(new AccountInfo(p) { Index = ++NextAccountID, CreationIP = c.IPAddress });


                c.Enqueue(new ServerPackets.NewAccount { Result = 8 });
            }
        }

        public int HTTPNewAccount(ClientPackets.NewAccount p, string ip)
        {
            if (!Settings.AllowNewAccount)
            {
                return 0;
            }

            if (!AccountIDReg.IsMatch(p.AccountID))
            {
                return 1;
            }

            if (!PasswordReg.IsMatch(p.Password))
            {
                return 2;
            }
            if (!string.IsNullOrWhiteSpace(p.EMailAddress) && !EMailReg.IsMatch(p.EMailAddress) ||
                p.EMailAddress.Length > 50)
            {
                return 3;
            }

            if (!string.IsNullOrWhiteSpace(p.UserName) && p.UserName.Length > 20)
            {
                return 4;
            }

            if (!string.IsNullOrWhiteSpace(p.SecretQuestion) && p.SecretQuestion.Length > 30)
            {
                return 5;
            }

            if (!string.IsNullOrWhiteSpace(p.SecretAnswer) && p.SecretAnswer.Length > 30)
            {
                return 6;
            }

            lock (AccountLock)
            {
                if (AccountExists(p.AccountID))
                {
                    return 7;
                }

                AccountList.Add(new AccountInfo(p) { Index = ++NextAccountID, CreationIP = ip });
                return 8;
            }
        }

        public void ChangePassword(ClientPackets.ChangePassword p, MirConnection c)
        {
            if (!Settings.AllowChangePassword)
            {
                c.Enqueue(new ServerPackets.ChangePassword { Result = 0 });
                return;
            }

            if (!AccountIDReg.IsMatch(p.AccountID))
            {
                c.Enqueue(new ServerPackets.ChangePassword { Result = 1 });
                return;
            }

            if (!PasswordReg.IsMatch(p.CurrentPassword))
            {
                c.Enqueue(new ServerPackets.ChangePassword { Result = 2 });
                return;
            }

            if (!PasswordReg.IsMatch(p.NewPassword))
            {
                c.Enqueue(new ServerPackets.ChangePassword { Result = 3 });
                return;
            }

            var account = GetAccount(p.AccountID);

            if (account == null)
            {
                c.Enqueue(new ServerPackets.ChangePassword { Result = 4 });
                return;
            }

            if (account.Banned)
            {
                if (account.ExpiryDate > Now)
                {
                    c.Enqueue(new ServerPackets.ChangePasswordBanned { Reason = account.BanReason, ExpiryDate = account.ExpiryDate });
                    return;
                }
                account.Banned = false;
            }
            account.BanReason = string.Empty;
            account.ExpiryDate = DateTime.MinValue;

            p.CurrentPassword = Utils.Crypto.HashPassword(p.CurrentPassword, account.Salt);
            if (string.CompareOrdinal(account.Password, p.CurrentPassword) != 0)
            {
                c.Enqueue(new ServerPackets.ChangePassword { Result = 5 });
                return;
            }

            account.Password = p.NewPassword;
            account.RequirePasswordChange = false;
            c.Enqueue(new ServerPackets.ChangePassword { Result = 6 });
        }
        public void Login(ClientPackets.Login p, MirConnection c)
        {
            if (!Settings.AllowLogin)
            {
                c.Enqueue(new ServerPackets.Login { Result = 0 });
                return;
            }

            if (!AccountIDReg.IsMatch(p.AccountID))
            {
                c.Enqueue(new ServerPackets.Login { Result = 1 });
                return;
            }

            if (!PasswordReg.IsMatch(p.Password))
            {
                c.Enqueue(new ServerPackets.Login { Result = 2 });
                return;
            }
            var account = GetAccount(p.AccountID);

            if (account == null)
            {
                c.Enqueue(new ServerPackets.Login { Result = 3 });
                return;
            }

            if (account.Banned)
            {
                if (account.ExpiryDate > Now)
                {
                    c.Enqueue(new ServerPackets.LoginBanned
                    {
                        Reason = account.BanReason,
                        ExpiryDate = account.ExpiryDate
                    });
                    return;
                }
                account.Banned = false;
            }
            account.BanReason = string.Empty;
            account.ExpiryDate = DateTime.MinValue;

            p.Password = Utils.Crypto.HashPassword(p.Password, account.Salt);

            if (string.CompareOrdinal(account.Password, p.Password) != 0)
            {
                if (account.WrongPasswordCount++ >= 5)
                {
                    account.Banned = true;
                    account.BanReason = "错误登录次数太多";
                    account.ExpiryDate = Now.AddMinutes(2);

                    c.Enqueue(new ServerPackets.LoginBanned
                    {
                        Reason = account.BanReason,
                        ExpiryDate = account.ExpiryDate
                    });
                    return;
                }

                c.Enqueue(new ServerPackets.Login { Result = 4 });
                return;
            }
            account.WrongPasswordCount = 0;

            if (account.RequirePasswordChange)
            {
                c.Enqueue(new ServerPackets.Login { Result = 5 });
                return;
            }

            lock (AccountLock)
            {
                account.Connection?.SendDisconnect(1);

                account.Connection = c;
            }

            c.Account = account;
            c.Stage = GameStage.Select;

            account.LastDate = Now;
            account.LastIP = c.IPAddress;

            MessageQueue.Enqueue(account.Connection.SessionID + ", " + account.Connection.IPAddress + "已登录服务器");
            c.Enqueue(new ServerPackets.LoginSuccess { Characters = account.GetSelectInfo() });
        }

        public int HTTPLogin(string AccountID, string Password)
        {
            if (!Settings.AllowLogin)
            {
                return 0;
            }

            if (!AccountIDReg.IsMatch(AccountID))
            {
                return 1;
            }

            if (!PasswordReg.IsMatch(Password))
            {
                return 2;
            }

            var account = GetAccount(AccountID);

            if (account == null)
            {
                return 3;
            }

            if (account.Banned)
            {
                if (account.ExpiryDate > Now)
                {
                    return 4;
                }
                account.Banned = false;
            }
            account.BanReason = string.Empty;
            account.ExpiryDate = DateTime.MinValue;
            if (string.CompareOrdinal(account.Password, Password) != 0)
            {
                if (account.WrongPasswordCount++ >= 5)
                {
                    account.Banned = true;
                    account.BanReason = "登录错误次数太多";
                    account.ExpiryDate = Now.AddMinutes(2);
                    return 5;
                }
                return 6;
            }
            account.WrongPasswordCount = 0;
            return 7;
        }

        public void NewCharacter(ClientPackets.NewCharacter p, MirConnection c, bool IsGm)
        {
            if (!Settings.AllowNewCharacter)
            {
                c.Enqueue(new ServerPackets.NewCharacter { Result = 0 });
                return;
            }

            if (ConnectionLogs.TryGetValue(c.IPAddress, out MirConnectionLog currentlog))
            {
                if (currentlog.CharactersMade.Count > 4)
                {
                    IPBlocks[c.IPAddress] = Now.AddHours(24);
                    c.Enqueue(new ServerPackets.NewCharacter { Result = 0 });
                    return;
                }
                currentlog.CharactersMade.Add(Time);
                for (int i = 0; i < currentlog.CharactersMade.Count; i++)
                {
                    if ((currentlog.CharactersMade[i] + 60 * 60 * 1000) < Time)
                    {
                        currentlog.CharactersMade.RemoveAt(i);
                        break;
                    }
                }
            }
            else
            {
                ConnectionLogs[c.IPAddress] = new MirConnectionLog() { IPAddress = c.IPAddress };
            }


            if (!CharacterReg.IsMatch(p.Name))
            {
                c.Enqueue(new ServerPackets.NewCharacter { Result = 1 });
                return;
            }

            if (!IsGm && DisabledCharNames.Contains(p.Name.ToUpper()))
            {
                c.Enqueue(new ServerPackets.NewCharacter { Result = 1 });
                return;
            }

            if (p.Gender != MirGender.男性 && p.Gender != MirGender.女性)
            {
                c.Enqueue(new ServerPackets.NewCharacter { Result = 2 });
                return;
            }

            if (p.Class != MirClass.战士 && p.Class != MirClass.法师 && p.Class != MirClass.道士 &&
                p.Class != MirClass.刺客 && p.Class != MirClass.弓箭)
            {
                c.Enqueue(new ServerPackets.NewCharacter { Result = 3 });
                return;
            }

            if(p.Class == MirClass.刺客 && !Settings.AllowCreateAssassin ||
                p.Class == MirClass.弓箭 && !Settings.AllowCreateArcher)
            {
                c.Enqueue(new ServerPackets.NewCharacter { Result = 3 });
                return;
            }

            var count = 0;

            for (var i = 0; i < c.Account.Characters.Count; i++)
            {
                if (c.Account.Characters[i].Deleted) continue;

                if (++count >= Globals.MaxCharacterCount)
                {
                    c.Enqueue(new ServerPackets.NewCharacter { Result = 4 });
                    return;
                }
            }

            lock (AccountLock)
            {
                if (CharacterExists(p.Name))
                {
                    c.Enqueue(new ServerPackets.NewCharacter { Result = 5 });
                    return;
                }

                var info = new CharacterInfo(p, c) { Index = ++NextCharacterID, AccountInfo = c.Account };

                c.Account.Characters.Add(info);
                CharacterList.Add(info);

                c.Enqueue(new ServerPackets.NewCharacterSuccess { CharInfo = info.ToSelectInfo() });
            }
        }

        public bool CanCreateHero(ClientPackets.NewHero p, MirConnection c, bool IsGm)
        {
            if (!Settings.AllowNewHero)
            {
                c.Enqueue(new S.NewHero { Result = 0 });
                return false;
            }

            if (!CharacterReg.IsMatch(p.Name))
            {
                c.Enqueue(new S.NewHero { Result = 1 });
                return false;
            }

            if (!IsGm && DisabledCharNames.Contains(p.Name.ToUpper()))
            {
                c.Enqueue(new S.NewHero { Result = 1 });
                return false;
            }

            if (p.Gender != MirGender.男性 && p.Gender != MirGender.女性)
            {
                c.Enqueue(new S.NewHero { Result = 2 });
                return false;
            }

            if (p.Class != MirClass.战士 && p.Class != MirClass.法师 && p.Class != MirClass.道士 && p.Class != MirClass.刺客 && p.Class != MirClass.弓箭)
            {
                c.Enqueue(new S.NewHero { Result = 3 });
                return false;
            }

            if (p.Class == MirClass.战士 && !Settings.Hero_CanCreateClass[0] || p.Class == MirClass.法师 && !Settings.Hero_CanCreateClass[1] || p.Class == MirClass.道士 && !Settings.Hero_CanCreateClass[2] || p.Class == MirClass.刺客 && !Settings.Hero_CanCreateClass[3] || p.Class == MirClass.弓箭 && !Settings.Hero_CanCreateClass[4])
            {
                c.Enqueue(new S.NewHero { Result = 3 });
                return false;
            }

            lock (AccountLock)
            {
                if (CharacterExists(p.Name))
                {
                    c.Enqueue(new S.NewHero { Result = 5 });
                    return false;
                }
            }

            return true;
        }

        public bool AccountExists(string accountID)
        {
            for (var i = 0; i < AccountList.Count; i++)
                if (string.Compare(AccountList[i].AccountID, accountID, StringComparison.OrdinalIgnoreCase) == 0)
                    return true;

            return false;
        }

        public bool CharacterExists(string name)
        {
            for (var i = 0; i < CharacterList.Count; i++)
                if (string.Compare(CharacterList[i].Name, name, StringComparison.OrdinalIgnoreCase) == 0)
                    return true;

            return false;
        }

        public List<CharacterInfo> MatchPlayer(string PlayerID, bool match = false)
        {
            if (string.IsNullOrEmpty(PlayerID)) return new List<CharacterInfo>(CharacterList);

            List<CharacterInfo> list = new List<CharacterInfo>();

            for (int i = 0; i < CharacterList.Count; i++)
            {
                if (match)
                {
                    if (CharacterList[i].Name.Equals(PlayerID, StringComparison.OrdinalIgnoreCase))
                        list.Add(CharacterList[i]);
                }
                else
                {
                    if (CharacterList[i].Name.IndexOf(PlayerID, StringComparison.OrdinalIgnoreCase) >= 0)
                        list.Add(CharacterList[i]);
                }
            }

            return list;
        }
        public List<CharacterInfo> MatchPlayerbyItem(string itemIdentifier, bool match = false)
        {
            List<CharacterInfo> list = new List<CharacterInfo>();

            bool isNumeric = ulong.TryParse(itemIdentifier, out ulong itemId);

            for (int i = 0; i < CharacterList.Count; i++)
            {
                if (match)
                {
                    foreach (var item in CharacterList[i].Inventory)
                        if (item != null && ((isNumeric && item.UniqueID == itemId) || (!isNumeric && item.FriendlyName.Equals(itemIdentifier, StringComparison.OrdinalIgnoreCase))) && !list.Contains(CharacterList[i]))
                            list.Add(CharacterList[i]);

                    foreach (var item in CharacterList[i].AccountInfo.Storage)
                        if (item != null && ((isNumeric && item.UniqueID == itemId) || (!isNumeric && item.FriendlyName.Equals(itemIdentifier, StringComparison.OrdinalIgnoreCase))) && !list.Contains(CharacterList[i]))
                            list.Add(CharacterList[i]);

                    foreach (var item in CharacterList[i].QuestInventory)
                        if (item != null && ((isNumeric && item.UniqueID == itemId) || (!isNumeric && item.FriendlyName.Equals(itemIdentifier, StringComparison.OrdinalIgnoreCase))) && !list.Contains(CharacterList[i]))
                            list.Add(CharacterList[i]);

                    foreach (var item in CharacterList[i].Equipment)
                        if (item != null && ((isNumeric && item.UniqueID == itemId) || (!isNumeric && item.FriendlyName.Equals(itemIdentifier, StringComparison.OrdinalIgnoreCase))) && !list.Contains(CharacterList[i]))
                            list.Add(CharacterList[i]);

                    foreach (var mail in CharacterList[i].Mail)
                        foreach (var item in mail.Items)
                            if (item != null && ((isNumeric && item.UniqueID == itemId) || (!isNumeric && item.FriendlyName.Equals(itemIdentifier, StringComparison.OrdinalIgnoreCase))) && !list.Contains(CharacterList[i]))
                                list.Add(CharacterList[i]);
                }
                else
                {
                    foreach (var item in CharacterList[i].Inventory)
                        if (item != null && ((isNumeric && item.UniqueID == itemId) || (!isNumeric && item.FriendlyName.IndexOf(itemIdentifier, StringComparison.OrdinalIgnoreCase) >= 0)) && !list.Contains(CharacterList[i]))
                            list.Add(CharacterList[i]);

                    foreach (var item in CharacterList[i].QuestInventory)
                        if (item != null && ((isNumeric && item.UniqueID == itemId) || (!isNumeric && item.FriendlyName.IndexOf(itemIdentifier, StringComparison.OrdinalIgnoreCase) >= 0)) && !list.Contains(CharacterList[i]))
                            list.Add(CharacterList[i]);

                    foreach (var item in CharacterList[i].Equipment)
                        if (item != null && ((isNumeric && item.UniqueID == itemId) || (!isNumeric && item.FriendlyName.IndexOf(itemIdentifier, StringComparison.OrdinalIgnoreCase) >= 0)) && !list.Contains(CharacterList[i]))
                            list.Add(CharacterList[i]);

                    foreach (var item in CharacterList[i].AccountInfo.Storage)
                        if (item != null && ((isNumeric && item.UniqueID == itemId) || (!isNumeric && item.FriendlyName.IndexOf(itemIdentifier, StringComparison.OrdinalIgnoreCase) >= 0)) && !list.Contains(CharacterList[i]))
                            list.Add(CharacterList[i]);
                }
            }

            return list;
        }

        public AccountInfo GetAccount(string accountID)
        {
            for (var i = 0; i < AccountList.Count; i++)
                if (string.Compare(AccountList[i].AccountID, accountID, StringComparison.OrdinalIgnoreCase) == 0)
                    return AccountList[i];

            return null;
        }

        public AccountInfo GetAccountByCharacter(string name)
        {
            for (var i = 0; i < AccountList.Count; i++)
            {
                for (int j = 0; j < AccountList[i].Characters.Count; j++)
                {
                    if (string.Compare(AccountList[i].Characters[j].Name, name, StringComparison.OrdinalIgnoreCase) == 0)
                        return AccountList[i];
                }
            }

            return null;
        }

        public List<AccountInfo> MatchAccounts(string accountID, bool match = false)
        {
            if (string.IsNullOrEmpty(accountID)) return new List<AccountInfo>(AccountList);

            var list = new List<AccountInfo>();

            for (var i = 0; i < AccountList.Count; i++)
            {
                if (match)
                {
                    if (AccountList[i].AccountID.Equals(accountID, StringComparison.OrdinalIgnoreCase))
                        list.Add(AccountList[i]);
                }
                else
                {
                    if (AccountList[i].AccountID.IndexOf(accountID, StringComparison.OrdinalIgnoreCase) >= 0)
                        list.Add(AccountList[i]);
                }
            }

            return list;
        }

        public List<AccountInfo> MatchAccountsByPlayer(string playerName, bool match = false)
        {
            if (string.IsNullOrEmpty(playerName)) return new List<AccountInfo>(AccountList);

            var list = new List<AccountInfo>();

            for (var i = 0; i < AccountList.Count; i++)
            {
                for (var j = 0; j < AccountList[i].Characters.Count; j++)
                {
                    if (match)
                    {
                        if (AccountList[i].Characters[j].Name.Equals(playerName, StringComparison.OrdinalIgnoreCase))
                            list.Add(AccountList[i]);
                    }
                    else
                    {
                        if (AccountList[i].Characters[j].Name.IndexOf(playerName, StringComparison.OrdinalIgnoreCase) >= 0)
                            list.Add(AccountList[i]);
                    }
                }
            }

            return list;
        }

        public List<AccountInfo> MatchAccountsByIP(string ipAddress, bool matchLastIP = false, bool match = false)
        {
            if (string.IsNullOrEmpty(ipAddress)) return new List<AccountInfo>(AccountList);

            var list = new List<AccountInfo>();

            for (var i = 0; i < AccountList.Count; i++)
            {
                string ipToMatch = matchLastIP ? AccountList[i].LastIP : AccountList[i].CreationIP;

                if (match)
                {
                    if (ipToMatch.Equals(ipAddress, StringComparison.OrdinalIgnoreCase))
                        list.Add(AccountList[i]);
                }
                else
                {
                    if (ipToMatch.IndexOf(ipAddress, StringComparison.OrdinalIgnoreCase) >= 0)
                        list.Add(AccountList[i]);
                }
            }

            return list;
        }


        public void CreateAccountInfo()
        {
            AccountList.Add(new AccountInfo {Index = ++NextAccountID});
        }
        public void CreateMapInfo()
        {
            MapInfoList.Add(new MapInfo {Index = ++MapIndex});
        }
        public void CreateItemInfo(ItemType type = ItemType.杂物)
        {
            ItemInfoList.Add(new ItemInfo { Index = ++ItemIndex, Type = type, RandomStatsId = 255});
        }
        public void CreateMonsterInfo()
        {
            MonsterInfoList.Add(new MonsterInfo {Index = ++MonsterIndex});
        }
        public void CreateNPCInfo()
        {
            NPCInfoList.Add(new NPCInfo { Index = ++NPCIndex });
        }
        public void CreateQuestInfo()
        {
            QuestInfoList.Add(new QuestInfo { Index = ++QuestIndex });
        }

        public void AddToGameShop(ItemInfo Info)
        {
            GameShopList.Add(new GameShopItem { GIndex = ++GameshopIndex, GoldPrice = (uint)(1000 * Settings.CredxGold), CreditPrice = 1000, ItemIndex = Info.Index, Info = Info, Date = Now, Class = "All", Category = Info.Type.ToString() });
        }

        public void Remove(MapInfo info)
        {
            MapInfoList.Remove(info);
            //Desync all objects\
        }
        public void Remove(ItemInfo info)
        {
            ItemInfoList.Remove(info);
        }
        public void Remove(MonsterInfo info)
        {
            MonsterInfoList.Remove(info);
            //Desync all objects\
        }
        public void Remove(NPCInfo info)
        {
            NPCInfoList.Remove(info);
            //Desync all objects\
        }
        public void Remove(QuestInfo info)
        {
            QuestInfoList.Remove(info);
            //Desync all objects\
        }

        public void Remove(GameShopItem info)
        {
            GameShopList.Remove(info);

            if (GameShopList.Count == 0)
            {
                GameshopIndex = 0;
            }
                
            //Desync all objects\
        }

        public UserItem CreateFreshItem(ItemInfo info)
        {
            var item = new UserItem(info)
                {
                    UniqueID = ++NextUserItemID,
                    CurrentDura = info.Durability,
                    MaxDura = info.Durability
                };

            UpdateItemExpiry(item);

            return item;
        }
        public UserItem CreateDropItem(int index)
        {
            return CreateDropItem(GetItemInfo(index));
        }
        public UserItem CreateDropItem(ItemInfo info)
        {
            if (info == null) return null;

            var item = new UserItem(info)
                {
                    UniqueID = ++NextUserItemID,
                    MaxDura = info.Durability,
                    CurrentDura = (ushort) Math.Min(info.Durability, Random.Next(info.Durability) + 1000)
                };

            UpgradeItem(item);

            UpdateItemExpiry(item);

            if (!info.NeedIdentify) item.Identified = true;
            return item;
        }

        public UserItem CreateShopItem(ItemInfo info, ulong id)
        {
            if (info == null) return null;

            var item = new UserItem(info)
            {
                UniqueID = id,
                CurrentDura = info.Durability,
                MaxDura = info.Durability,
                IsShopItem = true,
            };

            return item;
        }

        public void UpdateItemExpiry(UserItem item)
        {
            var expiryInfo = new ExpireInfo();

            var r = new Regex(@"\[(.*?)\]");
            var expiryMatch = r.Match(item.Info.Name);

            if (expiryMatch.Success)
            {
                var parameter = expiryMatch.Groups[1].Captures[0].Value;

                var numAlpha = new Regex("(?<Numeric>[0-9]*)(?<Alpha>[a-zA-Z]*)");
                var match = numAlpha.Match(parameter);

                var alpha = match.Groups["Alpha"].Value;
                var num = 0;

                int.TryParse(match.Groups["Numeric"].Value, out num);

                switch (alpha)
                {
                    case "m":
                        expiryInfo.ExpiryDate = Now.AddMinutes(num);
                        break;
                    case "h":
                        expiryInfo.ExpiryDate = Now.AddHours(num);
                        break;
                    case "d":
                        expiryInfo.ExpiryDate = Now.AddDays(num);
                        break;
                    case "M":
                        expiryInfo.ExpiryDate = Now.AddMonths(num);
                        break;
                    case "y":
                        expiryInfo.ExpiryDate = Now.AddYears(num);
                        break;
                    default:
                        expiryInfo.ExpiryDate = DateTime.MaxValue;
                        break;
                }

                item.ExpireInfo = expiryInfo;
            }
        }

        public void UpgradeItem(UserItem item)
        {
            if (item.Info.RandomStats == null) return;
            var stat = item.Info.RandomStats;
            if (stat.MaxDuraChance > 0 && Random.Next(stat.MaxDuraChance) == 0)
            {
                var dura = RandomomRange(stat.MaxDuraMaxStat, stat.MaxDuraStatChance);
                item.MaxDura = (ushort)Math.Min(ushort.MaxValue, item.MaxDura + dura * 1000);
                item.CurrentDura = (ushort)Math.Min(ushort.MaxValue, item.CurrentDura + dura * 1000);
            }

            if (stat.MaxAcChance > 0 && Random.Next(stat.MaxAcChance) == 0) item.AddedStats[Stat.MaxAC] = (byte)(RandomomRange(stat.MaxAcMaxStat-1, stat.MaxAcStatChance)+1);
            if (stat.MaxMacChance > 0 && Random.Next(stat.MaxMacChance) == 0) item.AddedStats[Stat.MaxMAC] = (byte)(RandomomRange(stat.MaxMacMaxStat-1, stat.MaxMacStatChance)+1);
            if (stat.MaxDcChance > 0 && Random.Next(stat.MaxDcChance) == 0) item.AddedStats[Stat.MaxDC] = (byte)(RandomomRange(stat.MaxDcMaxStat-1, stat.MaxDcStatChance)+1);
            if (stat.MaxMcChance > 0 && Random.Next(stat.MaxMcChance) == 0) item.AddedStats[Stat.MaxMC] = (byte)(RandomomRange(stat.MaxMcMaxStat-1, stat.MaxMcStatChance)+1);
            if (stat.MaxScChance > 0 && Random.Next(stat.MaxScChance) == 0) item.AddedStats[Stat.MaxSC] = (byte)(RandomomRange(stat.MaxScMaxStat-1, stat.MaxScStatChance)+1);
            if (stat.AccuracyChance > 0 && Random.Next(stat.AccuracyChance) == 0) item.AddedStats[Stat.准确] = (byte)(RandomomRange(stat.AccuracyMaxStat-1, stat.AccuracyStatChance)+1);
            if (stat.AgilityChance > 0 && Random.Next(stat.AgilityChance) == 0) item.AddedStats[Stat.敏捷] = (byte)(RandomomRange(stat.AgilityMaxStat-1, stat.AgilityStatChance)+1);
            if (stat.HpChance > 0 && Random.Next(stat.HpChance) == 0) item.AddedStats[Stat.HP] = (byte)(RandomomRange(stat.HpMaxStat-1, stat.HpStatChance)+1);
            if (stat.MpChance > 0 && Random.Next(stat.MpChance) == 0) item.AddedStats[Stat.MP] = (byte)(RandomomRange(stat.MpMaxStat-1, stat.MpStatChance)+1);
            if (stat.StrongChance > 0 && Random.Next(stat.StrongChance) == 0) item.AddedStats[Stat.强度] = (byte)(RandomomRange(stat.StrongMaxStat-1, stat.StrongStatChance)+1);
            if (stat.MagicResistChance > 0 && Random.Next(stat.MagicResistChance) == 0) item.AddedStats[Stat.魔法躲避] = (byte)(RandomomRange(stat.MagicResistMaxStat-1, stat.MagicResistStatChance)+1);
            if (stat.PoisonResistChance > 0 && Random.Next(stat.PoisonResistChance) == 0) item.AddedStats[Stat.毒物躲避] = (byte)(RandomomRange(stat.PoisonResistMaxStat-1, stat.PoisonResistStatChance)+1);
            if (stat.HpRecovChance > 0 && Random.Next(stat.HpRecovChance) == 0) item.AddedStats[Stat.生命恢复] = (byte)(RandomomRange(stat.HpRecovMaxStat-1, stat.HpRecovStatChance)+1);
            if (stat.MpRecovChance > 0 && Random.Next(stat.MpRecovChance) == 0) item.AddedStats[Stat.法力恢复] = (byte)(RandomomRange(stat.MpRecovMaxStat-1, stat.MpRecovStatChance)+1);
            if (stat.PoisonRecovChance > 0 && Random.Next(stat.PoisonRecovChance) == 0) item.AddedStats[Stat.中毒恢复] = (byte)(RandomomRange(stat.PoisonRecovMaxStat-1, stat.PoisonRecovStatChance)+1);
            if (stat.CriticalRateChance > 0 && Random.Next(stat.CriticalRateChance) == 0) item.AddedStats[Stat.暴击倍率] = (byte)(RandomomRange(stat.CriticalRateMaxStat-1, stat.CriticalRateStatChance)+1);
            if (stat.CriticalDamageChance > 0 && Random.Next(stat.CriticalDamageChance) == 0) item.AddedStats[Stat.暴击伤害] = (byte)(RandomomRange(stat.CriticalDamageMaxStat-1, stat.CriticalDamageStatChance)+1);
            if (stat.FreezeChance > 0 && Random.Next(stat.FreezeChance) == 0) item.AddedStats[Stat.冰冻伤害] = (byte)(RandomomRange(stat.FreezeMaxStat-1, stat.FreezeStatChance)+1);
            if (stat.PoisonAttackChance > 0 && Random.Next(stat.PoisonAttackChance) == 0) item.AddedStats[Stat.毒素伤害] = (byte)(RandomomRange(stat.PoisonAttackMaxStat-1, stat.PoisonAttackStatChance)+1);
            if (stat.AttackSpeedChance > 0 && Random.Next(stat.AttackSpeedChance) == 0) item.AddedStats[Stat.攻击速度] = (sbyte)(RandomomRange(stat.AttackSpeedMaxStat-1, stat.AttackSpeedStatChance)+1);
            if (stat.LuckChance > 0 && Random.Next(stat.LuckChance) == 0) item.AddedStats[Stat.幸运] = (sbyte)(RandomomRange(stat.LuckMaxStat-1, stat.LuckStatChance)+1);
            if (stat.CurseChance > 0 && Random.Next(100) <= stat.CurseChance) item.Cursed = true;

            if (stat.SlotChance > 0 && Random.Next(stat.SlotChance) == 0)
            {
                var slot = (byte)(RandomomRange(stat.SlotMaxStat - 1, stat.SlotStatChance) + 1);

                if (slot > item.Info.Slots)
                {
                    item.SetSlotSize(slot);
                }
            }
        }

        public int RandomomRange(int count, int rate)
        {
            var x = 0;
            for (var i = 0; i < count; i++) if (Random.Next(rate) == 0) x++;
            return x;
        }
        public bool BindItem(UserItem item)
        {
            for (var i = 0; i < ItemInfoList.Count; i++)
            {
                var info = ItemInfoList[i];
                if (info.Index != item.ItemIndex) continue;
                item.Info = info;

                return BindSlotItems(item);
            }
            return false;
        }

        public bool BindGameShop(GameShopItem item, bool editEnvir = true)
        {
            for (var i = 0; i < Edit.ItemInfoList.Count; i++)
            {
                var info = Edit.ItemInfoList[i];
                if (info.Index != item.ItemIndex) continue;
                item.Info = info;

                return true;
            }
            return false;
        }

        public bool BindSlotItems(UserItem item)
        {           
            for (var i = 0; i < item.Slots.Length; i++)
            {
                if (item.Slots[i] == null) continue;

                if (!BindItem(item.Slots[i])) return false;
            }

            return true;
        }

        public bool BindQuest(QuestProgressInfo quest)
        {
            for (var i = 0; i < QuestInfoList.Count; i++)
            {
                var info = QuestInfoList[i];
                if (info.Index != quest.Index) continue;
                quest.Info = info;
                return true;
            }
            return false;
        }

        public Map GetMap(int index)
        {
            return MapList.FirstOrDefault(t => t.Info.Index == index);
        }

        public Map GetMap(string name, bool strict = true)
        {
            return MapList.FirstOrDefault(t => strict ? string.Equals(t.Info.Title, name, StringComparison.CurrentCultureIgnoreCase) : t.Info.Title.StartsWith(name, StringComparison.CurrentCultureIgnoreCase));
        }

        public Map GetWorldMap(string name)
        {
            return MapList.FirstOrDefault(t => t.Info.Title.StartsWith(name, StringComparison.CurrentCultureIgnoreCase) && t.Info.BigMap > 0);
        }

        public MapInfo GetMapInfo(int index)
        {
            return MapInfoList.FirstOrDefault(t => t.Index == index);
        }

        public Map GetMapByNameAndInstance(string name, int instanceValue = 0)
        {
            if (instanceValue < 0) instanceValue = 0;
            if (instanceValue > 0) instanceValue--;

            var instanceMapList = MapList.Where(t => string.Equals(t.Info.FileName, name, StringComparison.CurrentCultureIgnoreCase)).ToList();
            return instanceValue < instanceMapList.Count() ? instanceMapList[instanceValue] : null;
        }

        public MonsterInfo GetMonsterInfo(int index)
        {
            for (var i = 0; i < MonsterInfoList.Count; i++)
                if (MonsterInfoList[i].Index == index) return MonsterInfoList[i];

            return null;
        }

        public MonsterInfo GetMonsterInfo(int ai, int effect = -1)
        {
            for (var i = 0; i < MonsterInfoList.Count; i++)
                if (MonsterInfoList[i].AI == ai && (MonsterInfoList[i].Effect == effect || effect < 0)) return MonsterInfoList[i];

            return null;
        }

        public NPCObject GetNPC(string name)
        {
            return MapList.SelectMany(t1 => t1.NPCs.Where(t => t.Info.Name == name)).FirstOrDefault();
        }

        public NPCObject GetWorldMapNPC(string name)
        {
            return MapList.SelectMany(t1 => t1.NPCs.Where(t => t.Info.GameName.StartsWith(name, StringComparison.CurrentCultureIgnoreCase) && t.Info.ShowOnBigMap)).FirstOrDefault();
        }

        public MonsterInfo GetMonsterInfo(int id, bool strict = false)
        {
            String monsterName = MonsterInfoList.FirstOrDefault(x => x.Index == id)?.Name;

            if (monsterName == null)
            {
                return null;
            }
            else
            {
                return (GetMonsterInfo(monsterName, strict));
            }
        }

        public MonsterInfo GetMonsterInfo(string name, bool Strict = false)
        {
            for (var i = 0; i < MonsterInfoList.Count; i++)
            {
                var info = MonsterInfoList[i];
                if (Strict)
                {
                    if (info.Name != name) continue;
                    return info;
                }
                else
                {
                    if (string.Compare(info.Name, name, StringComparison.OrdinalIgnoreCase) != 0 && string.Compare(info.Name.Replace(" ", ""), name.Replace(" ", ""), StringComparison.OrdinalIgnoreCase) != 0) continue;
                    return info;
                }
            }
            return null;
        }
        public PlayerObject GetPlayer(string name)
        {
            for (var i = 0; i < Players.Count; i++)
                if (string.Compare(Players[i].Name, name, StringComparison.OrdinalIgnoreCase) == 0)
                    return Players[i];

            return null;
        }
        public PlayerObject GetPlayer(uint PlayerId)
        {
            for (var i = 0; i < Players.Count; i++)
                if (Players[i].Info.Index == PlayerId)
                    return Players[i];

            return null;
        }
        public CharacterInfo GetCharacterInfo(string name)
        {
            for (var i = 0; i < CharacterList.Count; i++)
                if (string.Compare(CharacterList[i].Name, name, StringComparison.OrdinalIgnoreCase) == 0)
                    return CharacterList[i];

            return null;
        }

        public CharacterInfo GetCharacterInfo(int index)
        {
            for (var i = 0; i < CharacterList.Count; i++)
                if (CharacterList[i].Index == index)
                    return CharacterList[i];

            return null;
        }
        public HeroInfo GetHeroInfo(int index)
        {
            return HeroList.FirstOrDefault(x => x.Index == index);
        }

        public ItemInfo GetItemInfo(int index)
        {
            for (var i = 0; i < ItemInfoList.Count; i++)
            {
                var info = ItemInfoList[i];
                if (info.Index != index) continue;
                return info;
            }
            return null;
        }

        public ItemInfo GetItemInfo(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return null;

            var exactKey = NormalizeItemLookupKey(name, trimNumericSuffix: false);
            var relaxedKey = NormalizeItemLookupKey(name, trimNumericSuffix: true);

            for (var i = 0; i < ItemInfoList.Count; i++)
            {
                var info = ItemInfoList[i];
                if (NormalizeItemLookupKey(info.Name, trimNumericSuffix: false) != exactKey) continue;
                return info;
            }

            if (LegacyItemNameAliases.TryGetValue(exactKey, out var aliasName) && !string.IsNullOrWhiteSpace(aliasName))
            {
                var aliasInfo = GetItemInfo(aliasName);
                if (aliasInfo != null)
                    return aliasInfo;
            }

            for (var i = 0; i < ItemInfoList.Count; i++)
            {
                var info = ItemInfoList[i];
                if (NormalizeItemLookupKey(info.Name, trimNumericSuffix: true) == relaxedKey) return info;
                if (NormalizeItemLookupKey(info.FriendlyName, trimNumericSuffix: true) == relaxedKey) return info;
            }

            return null;
        }

        public RecipeInfo GetRecipeInfoByOutputItemName(string itemName)
        {
            if (string.IsNullOrWhiteSpace(itemName))
                return null;

            var itemInfo = GetItemInfo(itemName);
            if (itemInfo != null)
            {
                for (var i = 0; i < RecipeInfoList.Count; i++)
                {
                    var recipe = RecipeInfoList[i];
                    if (recipe != null && recipe.MatchItem(itemInfo.Index))
                        return recipe;
                }
            }

            var exactKey = NormalizeItemLookupKey(itemName, trimNumericSuffix: false);
            var relaxedKey = NormalizeItemLookupKey(itemName, trimNumericSuffix: true);

            for (var i = 0; i < RecipeInfoList.Count; i++)
            {
                var recipe = RecipeInfoList[i];
                var outputItem = recipe?.Item?.Info;
                if (outputItem == null)
                    continue;

                if (NormalizeItemLookupKey(outputItem.Name, trimNumericSuffix: false) == exactKey)
                    return recipe;

                if (NormalizeItemLookupKey(outputItem.Name, trimNumericSuffix: true) == relaxedKey)
                    return recipe;

                if (NormalizeItemLookupKey(outputItem.FriendlyName, trimNumericSuffix: true) == relaxedKey)
                    return recipe;
            }

            return null;
        }

        private static string NormalizeItemLookupKey(string value, bool trimNumericSuffix)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            var normalized = value.Trim()
                .Replace(" ", string.Empty)
                .Replace("　", string.Empty)
                .Replace("（", "(")
                .Replace("）", ")");

            if (trimNumericSuffix)
            {
                normalized = Regex.Replace(normalized, @"\[[^\]]*\]$", string.Empty);
                normalized = Regex.Replace(normalized, @"\d+$", string.Empty);
            }

            return normalized;
        }

        public QuestInfo GetQuestInfo(int index)
        {
            return QuestInfoList.FirstOrDefault(info => info.Index == index);
        }

        public ItemInfo GetBook(short Skill)
        {
            for (var i = 0; i < ItemInfoList.Count; i++)
            {
                var info = ItemInfoList[i];
                if (info.Type != ItemType.技能书 || info.Shape != Skill) continue;
                return info;
            }
            return null;
        }

        public BuffInfo GetBuffInfo(BuffType type)
        {
            for (int i = 0; i < BuffInfoList.Count; i++)
            {
                var info = BuffInfoList[i];
                if (info.Type != type) continue;

                return info;
            }

            throw new NotImplementedException($"{type} 尚未实施");
        }

        public void MessageAccount(AccountInfo account, string message, ChatType type)
        {
            if (account?.Characters == null) return;

            for (var i = 0; i < account.Characters.Count; i++)
            {
                if (account.Characters[i].Player == null) continue;
                account.Characters[i].Player.ReceiveChat(message, type);
                return;
            }
        }


        public void MailCharacter(CharacterInfo info, UserItem item = null, uint gold = 0, int reason = 0, string customMessage = null)
        {
            string sender = "比奇城管理员";

            string message = "由于以下原因，您已收到邮件:\r\n\r\n";

            switch (reason)
            {
                case 1:
                    message += "交易后无法将物品退回到背包中";
                    break;
                case 99:
                    message += "密码不正确不能打开仓库";
                    break;
                default:
                    message += customMessage ?? "未知原因";
                    break;
            }

            MailInfo mail = new MailInfo(info.Index)
            {
                Sender = sender,
                Message = message,
                Gold = gold
            };

            if (item != null)
            {
                mail.Items.Add(item);
            }

            mail.Send();
        }

        public GuildObject GetGuild(string name)
        {
            for (var i = 0; i < Guilds.Count; i++)
            {
                if (string.Compare(Guilds[i].Name.Replace(" ", ""), name, StringComparison.OrdinalIgnoreCase) != 0) continue;

                return Guilds[i];
            }

            return null;
        }
        public GuildObject GetGuild(int index)
        {
            for (var i = 0; i < Guilds.Count; i++)
            {
                if (Guilds[i].Guildindex == index)
                {
                    return Guilds[i];
                }
            }

            return null;
        }

        public void ProcessNewDay()
        {
            foreach (var c in CharacterList)
            {
                ClearDailyQuests(c);

                c.NewDay = true;

                c.Player?.CallDefaultNPC(DefaultNPCType.Daily);
            }
        }

        private void ProcessRentedItems()
        {
            foreach (var characterInfo in CharacterList)
            {
                if (characterInfo.RentedItems.Count <= 0)
                {
                    continue;
                }

                foreach (var rentedItemInfo in characterInfo.RentedItems)
                {
                    if (rentedItemInfo.ItemReturnDate >= Now)
                        continue;

                    var rentingPlayer = GetCharacterInfo(rentedItemInfo.RentingPlayerName);

                    for (var i = 0; i < rentingPlayer.Inventory.Length; i++)
                    {
                        if (rentedItemInfo.ItemId != rentingPlayer?.Inventory[i]?.UniqueID)
                        {
                            continue;
                        }

                        var item = rentingPlayer.Inventory[i];

                        if (item?.RentalInformation == null)
                        {
                            continue;
                        }

                        if (Now <= item.RentalInformation.ExpiryDate)
                        {
                            continue;
                        }

                        ReturnRentalItem(item, item.RentalInformation.OwnerName, rentingPlayer, false);
                        rentingPlayer.Inventory[i] = null;
                        rentingPlayer.HasRentedItem = false;

                        if (rentingPlayer.Player == null)
                        {
                            continue;
                        }

                        rentingPlayer.Player.ReceiveChat($"仓库中 {item.Info.FriendlyName} 已过期", ChatType.Hint);
                        rentingPlayer.Player.Enqueue(new S.DeleteItem { UniqueID = item.UniqueID, Count = item.Count });
                        rentingPlayer.Player.RefreshStats();
                    }

                    for (var i = 0; i < rentingPlayer.Equipment.Length; i++)
                    {
                        var item = rentingPlayer.Equipment[i];

                        if (item?.RentalInformation == null)
                        {
                            continue;
                        }

                        if (Now <= item.RentalInformation.ExpiryDate)
                        {
                            continue;
                        }

                        ReturnRentalItem(item, item.RentalInformation.OwnerName, rentingPlayer, false);
                        rentingPlayer.Equipment[i] = null;
                        rentingPlayer.HasRentedItem = false;

                        if (rentingPlayer.Player == null)
                        {
                            continue;
                        }

                        rentingPlayer.Player.ReceiveChat($"仓库中 {item.Info.FriendlyName} 已过期。", ChatType.Hint);
                        rentingPlayer.Player.Enqueue(new S.DeleteItem { UniqueID = item.UniqueID, Count = item.Count });
                        rentingPlayer.Player.RefreshStats();
                    }
                }
            }

            foreach (var characterInfo in CharacterList)
            {
                if (characterInfo.RentedItemsToRemove.Count <= 0)
                {
                    continue;
                }

                foreach (var rentalInformationToRemove in characterInfo.RentedItemsToRemove)
                {
                    characterInfo.RentedItems.Remove(rentalInformationToRemove);
                }

                characterInfo.RentedItemsToRemove.Clear();
            }
        }

        public bool ReturnRentalItem(UserItem rentedItem, string ownerName, CharacterInfo rentingCharacterInfo, bool removeNow = true)
        {
            if (rentedItem.RentalInformation == null)
            {
                return false;
            }

            var owner = GetCharacterInfo(ownerName);
            var returnItems = new List<UserItem>();

            foreach (var rentalInformation in owner.RentedItems)
            {
                if (rentalInformation.ItemId == rentedItem.UniqueID)
                {
                    owner.RentedItemsToRemove.Add(rentalInformation);
                }
            }
            
            rentedItem.RentalInformation.BindingFlags = BindMode.None;
            rentedItem.RentalInformation.RentalLocked = true;
            rentedItem.RentalInformation.ExpiryDate = rentedItem.RentalInformation.ExpiryDate.AddDays(1);

            returnItems.Add(rentedItem);

            var mail = new MailInfo(owner.Index, true)
            {
                Sender = rentingCharacterInfo.Name,
                Message = rentedItem.Info.FriendlyName,
                Items = returnItems
            };

            mail.Send();

            if (removeNow)
            {
                foreach (var rentalInformationToRemove in owner.RentedItemsToRemove)
                {
                    owner.RentedItems.Remove(rentalInformationToRemove);
                }

                owner.RentedItemsToRemove.Clear();
            }

            return true;
        }

        private void ClearDailyQuests(CharacterInfo info)
        {
            foreach (var quest in QuestInfoList)
            {
                if (quest.Type != QuestType.每日) continue;

                for (var i = 0; i < info.CompletedQuests.Count; i++)
                {
                    if (info.CompletedQuests[i] != quest.Index) continue;

                    info.CompletedQuests.RemoveAt(i);
                } 
            }

            info.Player?.GetCompletedQuests();
        }

        public GuildBuffInfo FindGuildBuffInfo(int Id)
        {
            for (var i = 0; i < Settings.Guild_BuffList.Count; i++)
            {
                if (Settings.Guild_BuffList[i].Id == Id)
                {
                    return Settings.Guild_BuffList[i];
                }
            }

            return null;
        }

        public void ClearGameshopLog()
        {
            Main.GameshopLog.Clear();

            for (var i = 0; i < AccountList.Count; i++)
            {
                for (var f = 0; f < AccountList[i].Characters.Count; f++)
                {
                    AccountList[i].Characters[f].GSpurchases.Clear();
                }
            }

            ResetGS = false;
            MessageQueue.Enqueue("游戏商城购买日志已清除");
        }

        public void Inspect(MirConnection con, uint id)
        {
            if (ObjectID == id) return;

            PlayerObject player = Players.SingleOrDefault(x => x.ObjectID == id || x.Pets.Count(y => y.ObjectID == id && y is HumanWizard) > 0);

            if (player == null) return;
            Inspect(con, player.Info.Index);
        }

        public void Inspect(MirConnection con, int id)
        {
            if (ObjectID == id) return;

            CharacterInfo player = GetCharacterInfo(id);
            if (player == null) return;

            CharacterInfo Lover = null;
            string loverName = "";

            if (player.Married != 0) Lover = GetCharacterInfo(player.Married);

            if (Lover != null)
            {
                loverName = Lover.Name;
            }

            for (int i = 0; i < player.Equipment.Length; i++)
            {
                UserItem u = player.Equipment[i];
                if (u == null) continue;

                con.CheckItem(u);
            }

            string guildname = "";
            string guildrank = "";
            GuildObject guild = null;
            GuildRank guildRank = null;
            if (player.GuildIndex != -1)
            {
                guild = GetGuild(player.GuildIndex);
                if (guild != null)
                {
                    guildRank = guild.FindRank(player.Name);
                    if (guildRank == null)
                    {
                        guild = null;
                    }
                    else
                    {
                        guildname = guild.Name;
                        guildrank = guildRank.Name;
                    }
                }
            }

            con.Enqueue(new S.PlayerInspect
            {
                Name = player.Name,
                Equipment = player.Equipment,
                GuildName = guildname,
                GuildRank = guildrank,
                Hair = player.Hair,
                Gender = player.Gender,
                Class = player.Class,
                Level = player.Level,
                LoverName = loverName,
                AllowObserve = player.AllowObserve && Settings.AllowObserve
            });
        }

        public void InspectHero(MirConnection con, int id)
        {
            if (ObjectID == id)
            {
                return;
            }

            HeroObject heroObject = Heroes.SingleOrDefault(h => h.ObjectID == id);

            if (heroObject == null)
            {
                return;
            }

            HeroInfo heroInfo = GetHeroInfo(heroObject.Info.Index);

            if (heroInfo == null)
            {
                return;
            }

            for (int i = 0; i < heroInfo.Equipment.Length; i++)
            {
                UserItem u = heroInfo.Equipment[i];

                if (u == null)
                {
                    continue;
                }

                con.CheckItem(u);
            }

            var ownerName = heroObject.Owner.Name;

            con.Enqueue(new S.PlayerInspect
            {
                Name = $"{ownerName}的英雄",
                Equipment = heroInfo.Equipment,
                GuildName = String.Empty,
                GuildRank = String.Empty,
                Hair = heroInfo.Hair,
                Gender = heroInfo.Gender,
                Class = heroInfo.Class,
                Level = heroInfo.Level,
                LoverName = String.Empty,
                AllowObserve = false,
                IsHero = true
            });

        }

        public void Observe(MirConnection con, string Name)
        {
            var player = GetPlayer(Name);

            if (player == null) return;
            if (!player.AllowObserve || !Settings.AllowObserve) return;

            player.AddObserver(con);
        }

        public void GetRanking(MirConnection con, byte RankType, int RankIndex, bool OnlineOnly)
        {
            if (RankType > 6) return;
            List<RankCharacterInfo> listings = RankType == 0 ? RankTop : RankClass[RankType - 1];

            if (RankIndex >= listings.Count || RankIndex < 0) return;

            S.Rankings p = new S.Rankings
            {
                RankType = RankType,
                Count = OnlineOnly ? OnlineRankingCount[RankType] : listings.Count
            };

            if (con.Player != null)
            {
                if (RankType == 0)
                    p.MyRank = con.Player.Info.Rank[0];
                else
                    p.MyRank = (byte)con.Player.Class == (RankType - 1) ? con.Player.Info.Rank[1] : 0;
            }

            int c = 0;
            for (int i = RankIndex; i < listings.Count; i++)
            {
                if (OnlineOnly && GetPlayer(listings[i].Name) == null) continue;

                if (!CheckListing(con, listings[i]))
                    p.ListingDetails.Add(listings[i]);
                p.Listings.Add(listings[i].PlayerId);
                c++;

                if (c > 19 || c >= p.Count) break;
            }

            con.Enqueue(p);
        }

        private bool CheckListing(MirConnection con, RankCharacterInfo listing)
        {
            if (!con.SentRankings.ContainsKey(listing.PlayerId))
            {
                con.SentRankings.Add(listing.PlayerId, listing.LastUpdated);
                return false;
            }

            DateTime lastUpdated = con.SentRankings[listing.PlayerId];
            if (lastUpdated != listing.LastUpdated)
            {
                con.SentRankings[listing.PlayerId] = lastUpdated;
                return false;
            }

            return true;
        }

        public int InsertRank(List<RankCharacterInfo> Ranking, RankCharacterInfo NewRank)
        {
            if (Ranking.Count == 0)
            {
                Ranking.Add(NewRank);
                return Ranking.Count;
            }

            for (var i = 0; i < Ranking.Count; i++)
            {
                //if level is lower
                if (Ranking[i].level < NewRank.level)
                {
                    Ranking.Insert(i, NewRank);
                    return i + 1;
                }

                //if exp is lower but level = same
                if (Ranking[i].level == NewRank.level && Ranking[i].Experience < NewRank.Experience)
                {
                    Ranking.Insert(i, NewRank);
                    return i + 1;
                }
            }

            Ranking.Add(NewRank);
            return Ranking.Count;
        }

        public bool TryAddRank(List<RankCharacterInfo> Ranking, CharacterInfo info, byte type)
        {
            var NewRank = new RankCharacterInfo() { Name = info.Name, Class = info.Class, Experience = info.Experience, level = info.Level, PlayerId = info.Index, info = info, LastUpdated = Now };
            var NewRankIndex = InsertRank(Ranking, NewRank);
            if (NewRankIndex == 0) return false;
            for (var i = NewRankIndex; i < Ranking.Count; i++ )
            {
                SetNewRank(Ranking[i], i + 1, type);
            }
            info.Rank[type] = NewRankIndex;
            return true;
        }

        public int FindRank(List<RankCharacterInfo> Ranking, CharacterInfo info, byte type)
        {
            var startindex = info.Rank[type];
            if (startindex > 0) //if there's a previously known rank then the user can only have gone down in the ranking (or stayed the same)
            {
                for (var i = startindex-1; i < Ranking.Count; i++)
                {
                    if (Ranking[i].Name == info.Name)
                        return i;
                }
                info.Rank[type] = 0;//set the rank to 0 to tell future searches it's not there anymore
            }
            return -1;//index can be 0
        }

        public bool UpdateRank(List<RankCharacterInfo> Ranking, CharacterInfo info, byte type)
        {
            var CurrentRank = FindRank(Ranking, info, type);
            if (CurrentRank == -1) return false;//not in ranking list atm
            
            var NewRank = CurrentRank;
            //next find our updated rank
            for (var i = CurrentRank-1; i >= 0; i-- )
            {
                if (Ranking[i].level > info.Level || Ranking[i].level == info.Level && Ranking[i].Experience > info.Experience) break;
                    NewRank =i;
            }

            Ranking[CurrentRank].level = info.Level;
            Ranking[CurrentRank].Experience = info.Experience;
            Ranking[CurrentRank].LastUpdated = Now;

            if (NewRank < CurrentRank)
            {//if we gained any ranks
                Ranking.Insert(NewRank, Ranking[CurrentRank]);
                Ranking.RemoveAt(CurrentRank + 1);
                for (var i = NewRank + 1; i < Math.Min(Ranking.Count, CurrentRank +1); i++)
                {
                    SetNewRank(Ranking[i], i + 1, type);
                }
            }
            info.Rank[type] = NewRank+1;
            
            return true;
        }

        public void SetNewRank(RankCharacterInfo Rank, int Index, byte type)
        {
            Rank.LastUpdated = Now;
            if (!(Rank.info is CharacterInfo Player)) return;
            Player.Rank[type] = Index;
        }

        public void RemoveRank(CharacterInfo info)
        {
            List<RankCharacterInfo> Ranking;
            var Rankindex = -1;
            //first check overall top           
            Ranking = RankTop;
            Rankindex = FindRank(Ranking, info, 0);
            if (Rankindex >= 0)
            {
                Ranking.RemoveAt(Rankindex);
                for (var i = Rankindex; i < Ranking.Count(); i++)
                {
                    SetNewRank(Ranking[i], i, 0);
                }
            }

            //next class based top
            Ranking = RankTop;
            Rankindex = FindRank(Ranking, info, 1);
            if (Rankindex >= 0)
            {
                Ranking.RemoveAt(Rankindex);
                for (var i = Rankindex; i < Ranking.Count(); i++)
                {
                    SetNewRank(Ranking[i], i, 1);
                }
            }

        }

        public void CheckRankUpdate(CharacterInfo info)
        {
            List<RankCharacterInfo> Ranking;

            //first check overall top           

            Ranking = RankTop;
            if (!UpdateRank(Ranking, info, 0))
            {
                TryAddRank(Ranking, info, 0);
            }

            //now check class top

            Ranking = RankClass[(byte)info.Class];
            if (!UpdateRank(Ranking, info, 1))
            {
                TryAddRank(Ranking, info, 1);
            }
        }


        public void ReloadNPCs()
        {
            var beforeScripts = Scripts.Count;
            var beforeSections = 0;
            var beforePages = 0;
            var beforeGoods = 0;
            var beforeCraftGoods = 0;

            foreach (var s in Scripts.Values)
            {
                if (s == null) continue;
                beforeSections += s.NPCSections?.Count ?? 0;
                beforePages += s.NPCPages?.Count ?? 0;
                beforeGoods += s.Goods?.Count ?? 0;
                beforeCraftGoods += s.CraftGoods?.Count ?? 0;
            }

            var sw = System.Diagnostics.Stopwatch.StartNew();

            SaveGoods(true);

            Robot.Clear();

            var keys = Scripts.Keys;

            foreach (var key in keys)
            {
                Scripts[key].Load();
            }

            sw.Stop();

            var afterScripts = Scripts.Count;
            var afterSections = 0;
            var afterPages = 0;
            var afterGoods = 0;
            var afterCraftGoods = 0;

            foreach (var s in Scripts.Values)
            {
                if (s == null) continue;
                afterSections += s.NPCSections?.Count ?? 0;
                afterPages += s.NPCPages?.Count ?? 0;
                afterGoods += s.Goods?.Count ?? 0;
                afterCraftGoods += s.CraftGoods?.Count ?? 0;
            }

            if (Settings.TxtScriptsLogLoads)
            {
                MessageQueue.Enqueue($"[TxtScripts][Reload] NPC脚本 ReloadNPCs：scripts={beforeScripts}->{afterScripts}，sections={beforeSections}->{afterSections}，pages={beforePages}->{afterPages}，goods={beforeGoods}->{afterGoods}，craft={beforeCraftGoods}->{afterCraftGoods}，耗时={sw.ElapsedMilliseconds}ms");
            }

            MessageQueue.Enqueue("NPC脚本已重新加载");
        }

        public void ReloadDrops()
        {
            var beforeMonsterDrops = 0;
            for (var i = 0; i < MonsterInfoList.Count; i++)
            {
                beforeMonsterDrops += MonsterInfoList[i].Drops?.Count ?? 0;
            }

            var beforeFishingDrops = FishingDrops.Count;
            var beforeAwakeningDrops = AwakeningDrops.Count;
            var beforeStrongboxDrops = StrongboxDrops.Count;
            var beforeBlackstoneDrops = BlackstoneDrops.Count;

            var sw = System.Diagnostics.Stopwatch.StartNew();

            for (var i = 0; i < MonsterInfoList.Count; i++)
            {
                string path = Path.Combine(Settings.DropPath, MonsterInfoList[i].Name + ".txt");

                if (!string.IsNullOrEmpty(MonsterInfoList[i].DropPath))
                {
                    path = Path.Combine(Settings.DropPath, MonsterInfoList[i].DropPath + ".txt");
                }

                MonsterInfoList[i].Drops.Clear();

                DropInfo.Load(MonsterInfoList[i].Drops, MonsterInfoList[i].Name, path, 0);
            }

            FishingDrops.Clear();
            for (int i = 0; i < 19; i++)
            {
                var path = Path.Combine(Settings.DropPath, Settings.FishingDropFilename + ".txt");
                path = path.Replace("00", i.ToString("D2"));

                DropInfo.Load(FishingDrops, $"钓鱼功能 {i}", path, (byte)i);
            }

            AwakeningDrops.Clear();
            DropInfo.Load(AwakeningDrops, "觉醒功能", Path.Combine(Settings.DropPath, Settings.AwakeningDropFilename + ".txt"));

            StrongboxDrops.Clear();
            DropInfo.Load(StrongboxDrops, "宝箱功能", Path.Combine(Settings.DropPath, Settings.StrongboxDropFilename + ".txt"));

            BlackstoneDrops.Clear();
            DropInfo.Load(BlackstoneDrops, "灵物石功能", Path.Combine(Settings.DropPath, Settings.BlackstoneDropFilename + ".txt"));

            sw.Stop();

            var afterMonsterDrops = 0;
            for (var i = 0; i < MonsterInfoList.Count; i++)
            {
                afterMonsterDrops += MonsterInfoList[i].Drops?.Count ?? 0;
            }

            var afterFishingDrops = FishingDrops.Count;
            var afterAwakeningDrops = AwakeningDrops.Count;
            var afterStrongboxDrops = StrongboxDrops.Count;
            var afterBlackstoneDrops = BlackstoneDrops.Count;

            if (Settings.TxtScriptsLogLoads)
            {
                MessageQueue.Enqueue($"[TxtScripts][Reload] 掉落 ReloadDrops：monsters={MonsterInfoList.Count}，monsterDrops={beforeMonsterDrops}->{afterMonsterDrops}，fishing={beforeFishingDrops}->{afterFishingDrops}，awakening={beforeAwakeningDrops}->{afterAwakeningDrops}，strongbox={beforeStrongboxDrops}->{afterStrongboxDrops}，blackstone={beforeBlackstoneDrops}->{afterBlackstoneDrops}，耗时={sw.ElapsedMilliseconds}ms");
            }

            MessageQueue.Enqueue("怪物掉落加载完成");
        }

        public void ReloadLineMessages()
        {
            LoadLineMessages();
            MessageQueue.Enqueue("LineMessages 已重新加载");
        }

        private WorldMapIcon ValidateWorldMap()
        {
            foreach (WorldMapIcon wmi in Settings.WorldMapSetup.Icons)
            {
                MapInfo info = GetMapInfo(wmi.MapIndex);

                if (info == null)
                    return wmi;
            }
            return null;
        }

        public void DeleteGuild(GuildObject guild)
        {
            Guilds.Remove(guild);
            GuildList.Remove(guild.Info);

            GuildRefreshNeeded = true;
            MessageQueue.Enqueue(guild.Info.Name + " 注销行会完成服务器数据已删除");
        }
    }
}
