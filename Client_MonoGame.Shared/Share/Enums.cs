using System;
using System.ComponentModel;

public enum MouseCursor : byte
{
    None,
    Default,
    Attack,
    AttackRed,
    NPCTalk,
    TextPrompt,
    Trash,
    Upgrade
}

public enum PanelType : byte
{
    Buy,
    BuySub,
    Craft,

    Sell,
    Repair,
    SpecialRepair,
    Consign,
    Refine,
    CheckRefine,
    Disassemble,
    Downgrade,
    Reset,
    CollectRefine,
    ReplaceWedRing,
}

public enum MarketItemType : byte
{
    Consign,
    Auction,
    GameShop
}

public enum MarketPanelType : byte
{
    Market,
    Consign,
    Auction,
    GameShop
}

public enum BlendMode : sbyte
{
    NONE = -1,
    NORMAL = 0,
    LIGHT = 1,
    LIGHTINV = 2,
    INVNORMAL = 3,
    INVLIGHT = 4,
    INVLIGHTINV = 5,
    INVCOLOR = 6,
    INVBACKGROUND = 7
}

public enum DamageType : byte
{
    Hit = 0,
    Miss = 1,
    Critical = 2
}

[Flags]
public enum GMOptions : byte
{
    None = 0,
    GameMaster = 0x0001,
    Observer = 0x0002,
    Superman = 0x0004
}

public enum AwakeType : byte
{
    None = 0,
    物理攻击 = 1,
    DC = 物理攻击,
    魔法攻击 = 2,
    MC = 魔法攻击,
    道术攻击 = 3,
    SC = 道术攻击,
    物理防御 = 4,
    AC = 物理防御,
    魔法防御 = 5,
    MAC = 魔法防御,
    生命法力值 = 6,
    HPMP = 生命法力值,
}

[Flags]
public enum LevelEffects : byte
{
    None = 0,
    Mist = 0x0001,
    RedDragon = 0x0002,
    BlueDragon = 0x0004
}

public enum OutputMessageType : byte
{
    Normal,
    Quest,
    Guild
}

public enum ItemGrade : byte
{
    None = 0,
    Common = 1,
    Rare = 2,
    Legendary = 3,
    Mythical = 4,
}



public enum RefinedValue : byte
{
    None = 0,
    DC = 1,
    MC = 2,
    SC = 3,
}

public enum QuestType : byte
{
    General = 0,
    Daily = 1,
    Repeatable = 2,
    Story = 3
}

public enum QuestIcon : byte
{
    None = 0,
    QuestionWhite = 1,
    ExclamationYellow = 2,
    QuestionYellow = 3,
    ExclamationBlue = 5,
    QuestionBlue = 6,
    ExclamationGreen = 52,
    QuestionGreen = 53
}

public enum QuestState : byte
{
    Add,
    Update,
    Remove
}

public enum DefaultNPCType : byte
{
    Login,
    LevelUp,
    UseItem,
    MapCoord,
    MapEnter,
    Die,
    Trigger,
    CustomCommand,
    OnAcceptQuest,
    OnFinishQuest,
    Daily,
    Client
}

public enum IntelligentCreatureType : byte
{
    None = 99,
    BabyPig = 0,
    Chick = 1,
    Kitten = 2,
    BabySkeleton = 3,
    Baekdon = 4,
    Wimaen = 5,
    BlackKitten = 6,
    BabyDragon = 7,
    OlympicFlame = 8,
    BabySnowMan = 9,
    Frog = 10,
    BabyMonkey = 11,
    AngryBird = 12,
    Foxey = 13,
    MedicalRat = 14,
}

//2 blank mob files
public enum Monster : ushort
{
    Guard = 0,
    TaoistGuard = 1,
    Guard2 = 2,
    Hen = 3,
    Deer = 4,
    Scarecrow = 5,
    HookingCat = 6,
    RakingCat = 7,
    Yob = 8,
    Oma = 9,
    CannibalPlant = 10,
    ForestYeti = 11,
    SpittingSpider = 12,
    ChestnutTree = 13,
    EbonyTree = 14,
    LargeMushroom = 15,
    CherryTree = 16,
    OmaFighter = 17,
    OmaWarrior = 18,
    CaveBat = 19,
    CaveMaggot = 20,
    Scorpion = 21,
    Skeleton = 22,
    BoneFighter = 23,
    AxeSkeleton = 24,
    BoneWarrior = 25,
    BoneElite = 26,
    Dung = 27,
    Dark = 28,
    WoomaSoldier = 29,
    WoomaFighter = 30,
    WoomaWarrior = 31,
    FlamingWooma = 32,
    WoomaGuardian = 33,
    WoomaTaurus = 34,
    WhimperingBee = 35,
    GiantWorm = 36,
    Centipede = 37,
    BlackMaggot = 38,
    Tongs = 39,
    EvilTongs = 40,
    EvilCentipede = 41,
    BugBat = 42,
    BugBatMaggot = 43,
    WedgeMoth = 44,
    RedBoar = 45,
    BlackBoar = 46,
    SnakeScorpion = 47,
    WhiteBoar = 48,
    EvilSnake = 49,
    BombSpider = 50,
    RootSpider = 51,
    SpiderBat = 52,
    VenomSpider = 53,
    GangSpider = 54,
    GreatSpider = 55,
    LureSpider = 56,
    BigApe = 57,
    EvilApe = 58,
    GrayEvilApe = 59,
    RedEvilApe = 60,
    CrystalSpider = 61,
    RedMoonEvil = 62,
    BigRat = 63,
    ZumaArcher = 64,
    ZumaStatue = 65,
    ZumaGuardian = 66,
    RedThunderZuma = 67,
    ZumaTaurus = 68,
    DigOutZombie = 69,
    ClZombie = 70,
    NdZombie = 71,
    CrawlerZombie = 72,
    ShamanZombie = 73,
    Ghoul = 74,
    KingScorpion = 75,
    KingHog = 76,
    DarkDevil = 77,
    BoneFamiliar = 78,
    Shinsu = 79,
    Shinsu1 = 80,
    SpiderFrog = 81,
    HoroBlaster = 82,
    BlueHoroBlaster = 83,
    KekTal = 84,
    VioletKekTal = 85,
    Khazard = 86,
    RoninGhoul = 87,
    ToxicGhoul = 88,
    BoneCaptain = 89,
    BoneSpearman = 90,
    BoneBlademan = 91,
    BoneArcher = 92,
    BoneLord = 93,
    Minotaur = 94,
    IceMinotaur = 95,
    ElectricMinotaur = 96,
    WindMinotaur = 97,
    FireMinotaur = 98,
    RightGuard = 99,
    LeftGuard = 100,
    MinotaurKing = 101,
    FrostTiger = 102,
    Sheep = 103,
    Wolf = 104,
    ShellNipper = 105,
    Keratoid = 106,
    GiantKeratoid = 107,
    SkyStinger = 108,
    SandWorm = 109,
    VisceralWorm = 110,
    RedSnake = 111,
    TigerSnake = 112,
    Yimoogi = 113,
    GiantWhiteSnake = 114,
    BlueSnake = 115,
    YellowSnake = 116,
    HolyDeva = 117,
    AxeOma = 118,
    SwordOma = 119,
    CrossbowOma = 120,
    WingedOma = 121,
    FlailOma = 122,
    OmaGuard = 123,
    YinDevilNode = 124,
    YangDevilNode = 125,
    OmaKing = 126,
    BlackFoxman = 127,
    RedFoxman = 128,
    WhiteFoxman = 129,
    TrapRock = 130,
    GuardianRock = 131,
    ThunderElement = 132,
    CloudElement = 133,
    GreatFoxSpirit = 134,
    HedgeKekTal = 135,
    BigHedgeKekTal = 136,
    RedFrogSpider = 137,
    BrownFrogSpider = 138,
    ArcherGuard = 139,
    KatanaGuard = 140,
    ArcherGuard2 = 141,
    Pig = 142,
    Bull = 143,
    Bush = 144,
    ChristmasTree = 145,
    HighAssassin = 146,
    DarkDustPile = 147,
    DarkBrownWolf = 148,
    Football = 149,
    GingerBreadman = 150,
    HalloweenScythe = 151,
    GhastlyLeecher = 152,
    CyanoGhast = 153,
    MutatedManworm = 154,
    CrazyManworm = 155,
    MudPile = 156,
    TailedLion = 157,
    Behemoth = 158,//done BOSS
    DarkDevourer = 159,//done
    PoisonHugger = 160,//done
    Hugger = 161,//done
    MutatedHugger = 162,//done
    DreamDevourer = 163,//done
    Treasurebox = 164,//done
    SnowPile = 165,//done
    Snowman = 166,//done
    SnowTree = 167,//done
    GiantEgg = 168,//done
    RedTurtle = 169,//done
    GreenTurtle = 170,//done
    BlueTurtle = 171,//done
    Catapult1 = 172, //not added frames
    Catapult2 = 173, //not added frames
    OldSpittingSpider = 174,
    SiegeRepairman = 175, //not added frames
    BlueSanta = 176,//done
    BattleStandard = 177,//done
    Blank2 = 178,
    RedYimoogi = 179,//done
    LionRiderMale = 180, //frames not added
    LionRiderFemale = 181, //frames not added
    Tornado = 182,//done
    FlameTiger = 183,//done
    WingedTigerLord = 184,//done BOSS
    TowerTurtle = 185,//done
    FinialTurtle = 186,//done
    TurtleKing = 187,//done BOSS
    DarkTurtle = 188,//done
    LightTurtle = 189,//done  
    DarkSwordOma = 190,//done
    DarkAxeOma = 191,//done
    DarkCrossbowOma = 192,//done
    DarkWingedOma = 193,//done
    BoneWhoo = 194,//done
    DarkSpider = 195,//done
    ViscusWorm = 196,//done
    ViscusCrawler = 197,//done
    CrawlerLave = 198,//done
    DarkYob = 199,//done

    FlamingMutant = 200,//FINISH
    StoningStatue = 201,//FINISH BOSS
    FlyingStatue = 202,//FINISH
    ValeBat = 203,//done
    Weaver = 204,//done
    VenomWeaver = 205,//done
    CrackingWeaver = 206,//done
    ArmingWeaver = 207,//done
    CrystalWeaver = 208,//done
    FrozenZumaStatue = 209,//done
    FrozenZumaGuardian = 210,//done
    FrozenRedZuma = 211,//done
    GreaterWeaver = 212,//done
    SpiderWarrior = 213,//done
    SpiderBarbarian = 214,//done
    HellSlasher = 215,//done
    HellPirate = 216,//done
    HellCannibal = 217,//done
    HellKeeper = 218, //done BOSS
    HellBolt = 219, //done
    WitchDoctor = 220,//done
    ManectricHammer = 221,//done
    ManectricClub = 222,//done
    ManectricClaw = 223,//done
    ManectricStaff = 224,//done
    NamelessGhost = 225,//done
    DarkGhost = 226,//done
    ChaosGhost = 227,//done
    ManectricBlest = 228,//done
    ManectricKing = 229,//done
    Blank3 = 230,
    IcePillar = 231,//done
    FrostYeti = 232,//done
    ManectricSlave = 233,//done
    TrollHammer = 234,//done
    TrollBomber = 235,//done
    TrollStoner = 236,//done
    TrollKing = 237,//done BOSS
    FlameSpear = 238,//done
    FlameMage = 239,//done
    FlameScythe = 240,//done
    FlameAssassin = 241,//done
    FlameQueen = 242, //finish BOSS
    HellKnight1 = 243,//done
    HellKnight2 = 244,//done
    HellKnight3 = 245,//done
    HellKnight4 = 246,//done
    HellLord = 247,//done BOSS
    WaterGuard = 248,//done
    IceGuard = 249, // Done (DG)
    ElementGuard = 250, // Done (DG)
    DemonGuard = 251, // Done (DG)
    KingGuard = 252, //TODO: AI Incomplete - Needs revisiting
    Snake10 = 253,//done
    Snake11 = 254,//done
    Snake12 = 255,//done
    Snake13 = 256,//done
    Snake14 = 257,//done
    Snake15 = 258,//done
    Snake16 = 259,//done
    Snake17 = 260,//done

    DeathCrawler = 261, // Done (DG)
    BurningZombie = 262, // Done (DG)
    MudZombie = 263, // Done (DG)
    FrozenZombie = 264, // Done (DG)
    UndeadWolf = 265, // No AI, basic attack Mob (DG)
    Demonwolf = 266, // Done (DG)
    WhiteMammoth = 267, // Done (DG)
    DarkBeast = 268, // Done (DG)
    LightBeast = 269, // Done (DG) - USE AI 112 (DARKBEAST)
    BloodBaboon = 270, // Done (DG)
    HardenRhino = 271, // TODO: AI (Shoulder Dash Attack)
    AncientBringer = 272, // Done (DG)
    FightingCat = 273, // No AI, basic attack Mob (DG)
    FireCat = 274, // Done (DG) - Use BlackFoxMan AI (44)
    CatWidow = 275, // Done (DG)
    StainHammerCat = 276, // Done (DG)
    BlackHammerCat = 277, // Done (DG)
    StrayCat = 278, // Done (DG)
    CatShaman = 279, // Done (DG)
    Jar1 = 280,
    Jar2 = 281,
    SeedingsGeneral = 282, // Done (DG)
    RestlessJar = 283,
    GeneralJinmYo = 284, // TODO: AI Incomplete - Thunderbolt and orb at end of lib file, not sure what this does? See notes in AI file (DG).
    GeneralMeowMeow = GeneralJinmYo,
    Bunny = 285,
    Tucson = 286, //No AI or spell animations (DG)
    TucsonFighter = 287, // Use AI 44 - No spell animation (DG)
    TucsonMage = 288, // Done (DG)
    TucsonWarrior = 289, // Done (DG)
    Armadillo = 290, // Done (DG)
    ArmadilloElder = 291, // Done (DG)
    TucsonEgg = 292, // Done (DG) - 2 AIs added (TucsonEgg1 will spawn TucsonGeneral upon death).
    PlaguedTucson = 293, //No AI - Basic Attack Mob (DG)
    SandSnail = 294, // Done (DG)
    CannibalTentacles = 295, // Done (DG)
    TucsonGeneral = 296,
    GasToad = 297, // Done (DG)
    Mantis = 298, // Done (DG)
    SwampWarrior = 299, // Done (DG)

    AssassinBird = 300, // Done (DG)
    RhinoWarrior = 301, // AI Incomplete - Needs water and rock attacks coding (DG).
    RhinoPriest = 302, // Done (DG)
    SwampSlime = 303, // Done (DG) - USE AI 113 (BloodBaboon) for Attack2. 
    RockGuard = 304,  // No spell animations (DG) - USE AI 113 (BloodBaboon) for Attack2.
    MudWarrior = 305, // Done (DG) - Frames amended (Attack2 changed to AttackRange1)
    SmallPot = 306,
    TreeQueen = 307,
    ShellFighter = 308,
    DarkBaboon = 309, // Done (DG) - USE AI 113 (BloodBaboon) for Attack2. 
    TwinHeadBeast = 310, // Done (DG) - USE AI 112 (DARKBEAST)
    OmaCannibal = 311, // Done (DG)
    OmaBlest = 312, // Done (DG)
    OmaSlasher = 313, // Done (DG)
    OmaAssassin = 314, // Done (DG)
    OmaMage = 315, // Done (DG)
    OmaWitchDoctor = 316,
    LightningBead = 317, // Minion of DarkOmaKing
    HealingBead = 318, // Minion of DarkOmaKing
    PowerUpBead = 319, // Minion of DarkOmaKing
    DarkOmaKing = 320, //TODO - BOSS AI
    CaveMage = 321,
    Mandrill = 322, // INCOMPLETE - TODO: TELEPORT NEEDS CODING
    PlagueCrab = 323, // Done (DG) - Note: There are seven frames missing from the DrawEffect in the lib (causes die effect to look off).
    CreeperPlant = 324, //Done (DG)
    FloatingWraith = 325, //Done (DG) - Use AI 8 (AxeSkeleton)
    ArmedPlant = 326, //Done (DG)
    AvengerPlant = 327, //Done (DG)
    Nadz = 328, // Done (DG)
    AvengingSpirit = 329, //Done (DG)
    AvengingWarrior = 330, //Done (DG)
    AxePlant = 331, //Done (DG)
    WoodBox = 332,
    ClawBeast = 333,
    KillerPlant = 334, //TODO - BOSS AI
    SackWarrior = 335, // Done (DG)
    WereTiger = 336, // Done (DG) - USE AI 113 (BloodBaboon) for Attack2. 
    KingHydrax = 337, //TODO - BOSS AI
    Hydrax = 338, // Done (DG) - No AI
    HornedMage = 339,
    Basiloid = 340,
    HornedArcher = 341,
    ColdArcher = 342,
    HornedWarrior = 343,
    FloatingRock = 344,
    ScalyBeast = 345,
    HornedSorceror = 346,
    BoulderSpirit = 347,
    HornedCommander = 348,
    MoonStone = 349,
    SunStone = 350,
    LightningStone = 351,
    Turtlegrass = 352, // Done (DG)
    ManTree = 353, //Done (DG)
    Bear = 354, //Done (DG)
    Leopard = 355, // Basic mob (No AI or spell animations) (DG)
    ChieftainArcher = 356,
    ChieftainSword = 357, //TODO - BOSS AI
    StoningSpider = 358, //Archer Spell mob (not yet coded)
    VampireSpider = 359, //Archer Spell mob
    SpittingToad = 360, //Archer Spell mob
    SnakeTotem = 361, //Archer Spell mob
    CharmedSnake = 362, //Archer Spell mob
    FrozenSoldier = 363, // Basic mob (No AI or spell animations) (DG)
    FrozenFighter = 364, // Done (DG)
    FrozenArcher = 365, //Done (DG) - Use AI 8 (AxeSkeleton)
    FrozenKnight = 366, // Done (DG)
    FrozenGolem = 367, // Done (DG) - Basic Attack1 mob
    IcePhantom = 368, //Done (DG) - //TODO - AI needs revisiting (blue explosion and snakes)
    SnowWolf = 369, // Done (DG)
    SnowWolfKing = 370, //TODO - BOSS AI
    WaterDragon = 371, //TODO - BOSS AI
    BlackTortoise = 372, //Done (DG) //TODO - figure out what the blue flashes are for (Critical hits??)
    Manticore = 373, //TODO - BOSS AI
    DragonWarrior = 374, //Done (DG)
    DragonArcher = 375, //TODO - Wind Arrow spell and Tornado (minion?)    
    Kirin = 376,
    Guard3 = 377,
    ArcherGuard3 = 378,
    Bunny2 = 379,
    FrozenMiner = 380,
    FrozenAxeman = 381,
    FrozenMagician = 382,
    SnowYeti = 383,
    IceCrystalSoldier = 384,
    DarkWraith = 385,
    DarkSpirit = 386,
    CrystalBeast = 387,
    RedOrb = 388,
    BlueOrb = 389,
    YellowOrb = 390,
    GreenOrb = 391,
    WhiteOrb = 392,
    FatalLotus = 393,
    AntCommander = 394,
    CargoBoxwithlogo = 395,
    Doe = 396,
    Reindeer = 397, //frames not added
    AngryReindeer = 398,
    CargoBox = 399,

    Ram1 = 400,
    Ram2 = 401,
    Kite = 402,
    PurpleFaeFlower = 403,
    Furball = 404,
    GlacierSnail = 405,
    FurbolgWarrior = 406,
    FurbolgArcher = 407,
    FurbolgCommander = 408,
    RedFaeFlower = 409,
    FurbolgGuard = 410,
    GlacierBeast = 411,
    GlacierWarrior = 412,
    ShardGuardian = 413,
    WarriorScroll = 414,
    TaoistScroll = 415,
    WizardScroll = 416,
    AssassinScroll = 417,
    HoodedSummoner = 418, //Summons Scrolls
    HoodedIceMage = 419,
    HoodedPriest = 420,
    ShardMaiden = 421,
    KingKong = 422,
    WarBear = 423,
    ReaperPriest = 424,
    ReaperWizard = 425,
    ReaperAssassin = 426,
    LivingVines = 427,
    BlueMonk = 428,
    MutantBeserker = 429,
    MutantGuardian = 430,
    MutantHighPriest = 431,
    MysteriousMage = 432,
    FeatheredWolf = 433,
    MysteriousAssassin = 434,
    MysteriousMonk = 435,
    ManEatingPlant = 436,
    HammerDwarf = 437,
    ArcherDwarf = 438,
    NobleWarrior = 439,
    NobleArcher = 440,
    NoblePriest = 441,
    NobleAssassin = 442,
    Swain = 443,
    RedMutantPlant = 444,
    BlueMutantPlant = 445,
    UndeadHammerDwarf = 446,
    UndeadDwarfArcher = 447,
    AncientStoneGolem = 448,
    Serpentirian = 449,

    Butcher = 450,
    Riklebites = 451,
    FeralTundraFurbolg = 452,
    FeralFlameFurbolg = 453,
    ArcaneTotem = 454,
    SpectralWraith = 455,
    BabyMagmaDragon = 456,
    BloodLord = 457,
    SerpentLord = 458,
    MirEmperor = 459,
    MutantManEatingPlant = 460,
    MutantWarg = 461,
    GrassElemental = 462,
    RockElemental = 463,

    EvilMir = 900,
    EvilMirBody = 901,
    DragonStatue = 902,
    HellBomb1 = 903,
    HellBomb2 = 904,
    HellBomb3 = 905,

    SabukGate = 950,
    PalaceWallLeft = 951,
    PalaceWall1 = 952,
    PalaceWall2 = 953,
    GiGateSouth = 954,
    GiGateEast = 955,
    GiGateWest = 956,
    SSabukWall1 = 957,
    SSabukWall2 = 958,
    SSabukWall3 = 959,
    NammandGate1 = 960, //Not Coded
    NammandGate2 = 961, //Not Coded
    SabukWallSection = 962, //Not Coded
    NammandWallSection = 963, //Not Coded
    FrozenDoor = 964, //Not Coded

    BabyPig = 10000,//Permanent
    Chick = 10001,//Special
    Kitten = 10002,//Permanent
    BabySkeleton = 10003,//Special
    Baekdon = 10004,//Special
    Wimaen = 10005,//Event
    BlackKitten = 10006,//unknown
    BabyDragon = 10007,//unknown
    OlympicFlame = 10008,//unknown
    BabySnowMan = 10009,//unknown
    Frog = 10010,//unknown
    BabyMonkey = 10011,//unknown
    AngryBird = 10012,
    Foxey = 10013,
    MedicalRat = 10014,
}

public enum MirAction : byte
{
    站立动作,
    Standing = 站立动作,
    行走动作,
    Walking = 行走动作,
    跑步动作,
    Running = 跑步动作,
    推开动作,
    Pushed = 推开动作,
    左冲动作,
    DashL = 左冲动作,
    右冲动作,
    DashR = 右冲动作,
    冲击失败,
    DashFail = 冲击失败,
    站立姿势,
    Stance = 站立姿势,
    站立姿势2,
    Stance2 = 站立姿势2,
    近距攻击1,
    Attack1 = 近距攻击1,
    近距攻击2,
    Attack2 = 近距攻击2,
    近距攻击3,
    Attack3 = 近距攻击3,
    近距攻击4,
    Attack4 = 近距攻击4,
    近距攻击5,
    Attack5 = 近距攻击5,
    远程攻击1,
    AttackRange1 = 远程攻击1,
    远程攻击2,
    AttackRange2 = 远程攻击2,
    远程攻击3,
    AttackRange3 = 远程攻击3,
    特殊攻击,
    Special = 特殊攻击,
    被击动作,
    Struck = 被击动作,
    挖矿展示,
    Harvest = 挖矿展示,
    施法动作,
    Spell = 施法动作,
    死亡动作,
    Die = 死亡动作,
    死后尸体,
    Dead = 死后尸体,
    挖后尸骸,
    Skeleton = 挖后尸骸,
    召唤初现,
    Show = 召唤初现,
    切换LIB,
    Hide = 切换LIB,
    石化状态,
    Stoned = 石化状态,
    石化苏醒,
    Appear = 石化苏醒,
    复活动作,
    Revive = 复活动作,
    SitDown,
    Mine,
    Sneek,
    DashAttack,
    Lunge,

    WalkingBow,
    RunningBow,
    Jump,

    MountStanding,
    MountWalking,
    MountRunning,
    MountStruck,
    MountAttack,

    FishingCast,
    FishingWait,
    FishingReel
}

public enum CellAttribute : byte
{
    Walk = 0,
    HighWall = 1,
    LowWall = 2,
}

public enum LightSetting : byte
{
    Normal = 0,
    Dawn = 1,
    Day = 2,
    Evening = 3,
    Night = 4
}

public enum MirGender : byte
{
    Male = 0,
    男性 = Male,
    Female = 1,
    女性 = Female
}

public enum MirClass : byte
{
    [Description("战士")]
    Warrior = 0,
    [Description("法师")]
    Wizard = 1,
    [Description("道士")]
    Taoist = 2,
    [Description("刺客")]
    Assassin = 3,
    [Description("弓箭手")]
    Archer = 4
}

public enum MirDirection : byte
{
    Up = 0,
    UpRight = 1,
    Right = 2,
    DownRight = 3,
    Down = 4,
    DownLeft = 5,
    Left = 6,
    UpLeft = 7
}

public enum ObjectType : byte
{
    None = 0,
    Player = 1,
    Item = 2,
    Merchant = 3,
    Spell = 4,
    Monster = 5,
    Deco = 6,
    Creature = 7
}

public enum ChatType : byte
{
    /// <summary>
    /// [正常]
    /// </summary>
    [Description("[正常]")]
    Normal = 0,
    /// <summary>
    /// [附近]
    /// </summary>
    [Description("[附近]")]
    Shout = 1,
    /// <summary>
    /// [系统]
    /// </summary>
    [Description("[系统]")]
    System = 2,
    /// <summary>
    /// [提示]
    /// </summary>
    [Description("[提示]")]
    Hint = 3,
    /// <summary>
    /// [公告]
    /// </summary>
    [Description("[公告]")]
    Announcement = 4,
    /// <summary>
    /// [组队]
    /// </summary>
    [Description("[组队]")]
    Group = 5,
    /// <summary>
    /// [私聊]
    /// </summary>
    [Description("[私聊]")]
    WhisperIn = 6,
    /// <summary>
    /// [私聊]
    /// </summary>
    [Description("[私聊]")]
    WhisperOut = 7,
    /// <summary>
    /// [行会]
    /// </summary>
    [Description("[行会]")]
    Guild = 8,
    /// <summary>
    /// [提示]
    /// </summary>
    [Description("[提示]")]
    Trainer = 9,
    /// <summary>
    /// [升级]
    /// </summary>
    [Description("[升级]")]
    LevelUp = 10,
    /// <summary>
    /// [系统]
    /// </summary>
    [Description("[系统]")]
    System2 = 11,
    /// <summary>
    /// [夫妻]
    /// </summary>
    [Description("[夫妻]")]
    Relationship = 12,
    /// <summary>
    /// [师徒]
    /// </summary>
    [Description("[师徒]")]
    Mentor = 13,
    /// <summary>
    /// [附近]
    /// </summary>
    [Description("[附近]")]
    Shout2 = 14,
    /// <summary>
    /// [附近]
    /// </summary>
    [Description("[附近]")]
    Shout3 = 15,
    /// <summary>
    /// [公告]
    /// </summary>
    [Description("[公告]")]
    LineMessage = 16,
}

public enum ItemType : byte
{
    杂物 = 0,
    Nothing = 杂物,
    武器 = 1,
    Weapon = 武器,
    盔甲 = 2,
    Armour = 盔甲,
    头盔 = 4,
    Helmet = 头盔,
    项链 = 5,
    Necklace = 项链,
    手镯 = 6,
    Bracelet = 手镯,
    戒指 = 7,
    Ring = 戒指,
    护身符 = 8,
    Amulet = 护身符,
    腰带 = 9,
    Belt = 腰带,
    靴子 = 10,
    Boots = 靴子,
    守护石 = 11,
    Stone = 守护石,
    照明物 = 12,
    Torch = 照明物,
    药水 = 13,
    Potion = 药水,
    矿石 = 14,
    Ore = 矿石,
    肉 = 15,
    Meat = 肉,
    工艺材料 = 16,
    CraftingMaterial = 工艺材料,
    卷轴 = 17,
    Scroll = 卷轴,
    宝玉神珠 = 18,
    Gem = 宝玉神珠,
    坐骑 = 19,
    Mount = 坐骑,
    技能书 = 20,
    Book = 技能书,
    特殊消耗品 = 21,
    Script = 特殊消耗品,
    缰绳 = 22,
    Reins = 缰绳,
    铃铛 = 23,
    Bells = 铃铛,
    马鞍 = 24,
    Saddle = 马鞍,
    蝴蝶结 = 25,
    Ribbon = 蝴蝶结,
    面甲 = 26,
    Mask = 面甲,
    坐骑食物 = 27,
    Food = 坐骑食物,
    鱼钩 = 28,
    Hook = 鱼钩,
    鱼漂 = 29,
    Float = 鱼漂,
    鱼饵 = 30,
    Bait = 鱼饵,
    探鱼器 = 31,
    Finder = 探鱼器,
    摇轮 = 32,
    Reel = 摇轮,
    鱼 = 33,
    Fish = 鱼,
    任务物品 = 34,
    Quest = 任务物品,
    觉醒物品 = 35,
    Awakening = 觉醒物品,
    灵物 = 36,
    Pets = 灵物,
    外形物品 = 37,
    Transform = 外形物品,
    装饰 = 38,
    Deco = 装饰,
    镶嵌宝石 = 39,
    Socket = 镶嵌宝石,
    怪物蛋 = 40,
    MonsterEgg = 怪物蛋,
    攻城弹药 = 41,
    SiegeAmmo = 攻城弹药,
    封印 = 42,
    Seal = 封印,
    攻击型绝技 = 43,
    AttackFragment = 攻击型绝技,
    防御型绝技 = 44,
    DefenceFragment = 防御型绝技,
    技能型绝技 = 45,
    SkillFragment = 技能型绝技,
    绝技材料 = 46,
    FragmentMaterial = 绝技材料
}

public enum MirGridType : byte
{
    None = 0,
    Inventory = 1,
    Equipment = 2,
    Trade = 3,
    Storage = 4,
    BuyBack = 5,
    DropPanel = 6,
    Inspect = 7,
    TrustMerchant = 8,
    GuildStorage = 9,
    GuestTrade = 10,
    Mount = 11,
    Fishing = 12,
    QuestInventory = 13,
    AwakenItem = 14,
    Mail = 15,
    Refine = 16,
    Renting = 17,
    GuestRenting = 18,
    Craft = 19,
    Socket = 20
}

public enum EquipmentSlot : byte
{
    Weapon = 0,
    Armour = 1,
    Helmet = 2,
    Torch = 3,
    Necklace = 4,
    BraceletL = 5,
    BraceletR = 6,
    RingL = 7,
    RingR = 8,
    Amulet = 9,
    Belt = 10,
    Boots = 11,
    Stone = 12,
    Mount = 13
}

public enum MountSlot : byte
{
    Reins = 0,
    Bells = 1,
    Saddle = 2,
    Ribbon = 3,
    Mask = 4
}

public enum FishingSlot : byte
{
    Hook = 0,
    Float = 1,
    Bait = 2,
    Finder = 3,
    Reel = 4
}

public enum AttackMode : byte
{
    Peace = 0,
    Group = 1,
    Guild = 2,
    EnemyGuild = 3,
    RedBrown = 4,
    All = 5
}

public enum PetMode : byte
{
    Both = 0,
    MoveOnly = 1,
    AttackOnly = 2,
    None = 3,
}

[Flags]
public enum PoisonType : ushort
{
    None = 0,
    Green = 1,
    Red = 2,
    Slow = 4,
    Frozen = 8,
    Stun = 16,
    Paralysis = 32,
    DelayedExplosion = 64,
    Bleeding = 128,
    LRParalysis = 256
}

[Flags]

public enum BindMode : short
{
    None = 0,
    DontDeathdrop = 1,//0x0001
    DontDrop = 2,//0x0002
    DontSell = 4,//0x0004
    DontStore = 8,//0x0008
    DontTrade = 16,//0x0010
    DontRepair = 32,//0x0020
    DontUpgrade = 64,//0x0040
    DestroyOnDrop = 128,//0x0080
    BreakOnDeath = 256,//0x0100
    BindOnEquip = 512,//0x0200
    NoSRepair = 1024,//0x0400
    NoWeddingRing = 2048,//0x0800
    UnableToRent = 4096,
    UnableToDisassemble = 8192,
    NoMail = 16384
}

[Flags]
public enum SpecialItemMode : short
{
    None = 0,
    Paralize = 0x0001,
    Teleport = 0x0002,
    ClearRing = 0x0004,
    Protection = 0x0008,
    Revival = 0x0010,
    Muscle = 0x0020,
    Flame = 0x0040,
    Healing = 0x0080,
    Probe = 0x0100,
    Skill = 0x0200,
    NoDuraLoss = 0x0400,
    Blink = 0x800,
}

[Flags]
public enum RequiredClass : byte
{
    战士 = 1,
    Warrior = 战士,
    法师 = 2,
    Wizard = 法师,
    道士 = 4,
    Taoist = 道士,
    刺客 = 8,
    Assassin = 刺客,
    弓箭 = 16,
    Archer = 弓箭,
    战士刺客 = 战士 | 刺客,
    WarAssassin = 战士刺客,
    战法道 = 战士 | 法师 | 道士,
    WarWizTao = 战法道,
    全职业 = 战法道 | 刺客 | 弓箭,
    None = 全职业
}

[Flags]
public enum RequiredGender : byte
{
    男性 = 1,
    Male = 男性,
    女性 = 2,
    Female = 女性,
    性别不限 = 男性 | 女性,
    None = 性别不限
}

public enum RequiredType : byte
{
    Level = 0,
    MaxAC = 1,
    MaxMAC = 2,
    MaxDC = 3,
    MaxMC = 4,
    MaxSC = 5,
    MaxLevel = 6,
    MinAC = 7,
    MinMAC = 8,
    MinDC = 9,
    MinMC = 10,
    MinSC = 11,
}

public enum ItemSet : byte
{
    非套装 = 0,
    None = 非套装,
    祈祷套装 = 1,
    Spirit = 祈祷套装,
    记忆套装 = 2,
    Recall = 记忆套装,
    赤兰套装 = 3,
    RedOrchid = 赤兰套装,
    密火套装 = 4,
    RedFlower = 密火套装,
    破碎套装 = 5,
    Smash = 破碎套装,
    幻魔石套 = 6,
    HwanDevil = 幻魔石套,
    灵玉套装 = 7,
    Purity = 灵玉套装,
    五玄套装 = 8,
    FiveString = 五玄套装,
    世轮套装 = 9,
    Mundane = 世轮套装,
    绿翠套装 = 10,
    NokChi = 绿翠套装,
    道护套装 = 11,
    TaoProtect = 道护套装,
    天龙套装 = 12,
    Mir = 天龙套装,
    白骨套装 = 13,
    Bone = 白骨套装,
    虫血套装 = 14,
    Bug = 虫血套装,
    白金套装 = 15,
    WhiteGold = 白金套装,
    强白金套 = 16,
    WhiteGoldH = 强白金套,
    红玉套装 = 17,
    RedJade = 红玉套装,
    强红玉套 = 18,
    RedJadeH = 强红玉套,
    软玉套装 = 19,
    Nephrite = 软玉套装,
    强软玉套 = 20,
    NephriteH = 强软玉套,
    贵人战套 = 21,
    Whisker1 = 贵人战套,
    贵人法套 = 22,
    Whisker2 = 贵人法套,
    贵人道套 = 23,
    Whisker3 = 贵人道套,
    贵人刺套 = 24,
    Whisker4 = 贵人刺套,
    贵人弓套 = 25,
    Whisker5 = 贵人弓套,
    龙血套装 = 26,
    Hyeolryong = 龙血套装,
    监视套装 = 27,
    Monitor = 监视套装,
    暴压套装 = 28,
    Oppressive = 暴压套装,
    贝玉套装 = 29,
    Paeok = 贝玉套装,
    黑术套装 = 30,
    Sulgwan = 黑术套装,
    青玉套装 = 31,
    BlueFrost = 青玉套装,
    鏃未套装 = 38,
    DarkGhost = 鏃未套装,
    强青玉套 = 39,
    BlueFrostH = 强青玉套,
    圣龙套装 = 40,
    神龙套装 = 41
}

public enum Spell : byte
{
    None = 0,

    //Warrior
    [Description("基本剑术")]
    Fencing = 1,
    [Description("攻杀剑术")]
    Slaying = 2,
    [Description("刺杀剑术")]
    Thrusting = 3,
    [Description("半月弯刀")]
    HalfMoon = 4,
    [Description("野蛮冲撞")]
    ShoulderDash = 5,
    [Description("双龙斩")]
    TwinDrakeBlade = 6,
    [Description("捕绳剑")]
    Entrapment = 7,
    [Description("烈火剑法")]
    FlamingSword = 8,
    [Description("狮子吼")]
    LionRoar = 9,
    [Description("圆月弯刀")]
    CrossHalfMoon = 10,
    [Description("攻破斩")]
    BladeAvalanche = 11,
    [Description("护身气幕")]
    ProtectionField = 12,
    [Description("剑气爆")]
    Rage = 13,
    [Description("反击")]
    CounterAttack = 14,
    [Description("日闪")]
    SlashingBurst = 15,
    [Description("血龙剑法")]
    Fury = 16,
    [Description("ImmortalSkin")]
    ImmortalSkin = 17,

    //Wizard
    [Description("火球术")]
    FireBall = 31,
    [Description("抗拒火环")]
    Repulsion = 32,
    [Description("诱惑之光")]
    ElectricShock = 33,
    [Description("大火球")]
    GreatFireBall = 34,
    [Description("地狱火")]
    HellFire = 35,
    [Description("雷电术")]
    ThunderBolt = 36,
    [Description("瞬息移动")]
    Teleport = 37,
    [Description("爆裂火焰")]
    FireBang = 38,
    [Description("火墙")]
    FireWall = 39,
    [Description("疾光电影")]
    Lightning = 40,
    [Description("寒冰掌")]
    FrostCrunch = 41,
    [Description("地狱雷光")]
    ThunderStorm = 42,
    [Description("魔法盾")]
    MagicShield = 43,
    [Description("圣言术")]
    TurnUndead = 44,
    [Description("嗜血术")]
    Vampirism = 45,
    [Description("冰咆哮")]
    IceStorm = 46,
    [Description("火龙术")]
    FlameDisruptor = 47,
    [Description("分身术")]
    Mirroring = 48,
    [Description("火龙气焰")]
    FlameField = 49,
    [Description("天霜冰环")]
    Blizzard = 50,
    [Description("深延术")]
    MagicBooster = 51,
    [Description("流星火雨")]
    MeteorStrike = 52,
    [Description("冰焰术")]
    IceThrust = 53,
    [Description("FastMove")]
    FastMove = 54,
    [Description("StormEscape")]
    StormEscape = 55,


    //Taoist
    [Description("治愈术")]
    Healing = 61,
    [Description("精神力战法")]
    SpiritSword = 62,
    [Description("施毒术")]
    Poisoning = 63,
    [Description("灵魂火符")]
    SoulFireBall = 64,
    [Description("召唤骷髅")]
    SummonSkeleton = 65,
    [Description("隐身术")]
    Hiding = 67,
    [Description("集体隐身术")]
    MassHiding = 68,
    [Description("幽灵盾")]
    SoulShield = 69,
    [Description("心灵启示")]
    Revelation = 70,
    [Description("神圣战甲术")]
    BlessedArmour = 71,
    [Description("气功波")]
    EnergyRepulsor = 72,
    [Description("困魔咒")]
    TrapHexagon = 73,
    [Description("净化术")]
    Purification = 74,
    [Description("群体治疗术")]
    MassHealing = 75,
    [Description("迷魂术")]
    Hallucination = 76,
    [Description("无极真气")]
    UltimateEnhancer = 77,
    [Description("召唤神兽")]
    SummonShinsu = 78,
    [Description("复活术")]
    Reincarnation = 79,
    [Description("召唤月灵")]
    SummonHolyDeva = 80,
    [Description("诅咒术")]
    Curse = 81,
    [Description("瘟疫")]
    Plague = 82,
    [Description("毒云")]
    PoisonCloud = 83,
    [Description("阴阳盾")]
    EnergyShield = 84,
    [Description("血龙水")]
    PetEnhancer = 85,
    [Description("HealingCircle")]
    HealingCircle = 86,

    //Assassin
    [Description("绝命剑法")]
    FatalSword = 91,
    [Description("双刀术")]
    DoubleSlash = 92,
    [Description("体迅风")]
    Haste = 93,
    [Description("拔刀术")]
    FlashDash = 94,
    [Description("风身术")]
    LightBody = 95,
    [Description("迁移剑")]
    HeavenlySword = 96,
    [Description("烈风击")]
    FireBurst = 97,
    [Description("捕缚术")]
    Trap = 98,
    [Description("猛毒剑气")]
    PoisonSword = 99,
    [Description("月影术")]
    MoonLight = 100,
    [Description("吸气")]
    MPEater = 101,
    [Description("轻身步")]
    SwiftFeet = 102,
    [Description("烈火身")]
    DarkBody = 103,
    [Description("血风击")]
    Hemorrhage = 104,
    [Description("猫舌兰")]
    CrescentSlash = 105,
    [Description("MoonMist")]
    MoonMist = 106,

    //Archer
    [Description("必中闪")]
    Focus = 121,
    [Description("天日闪")]
    StraightShot = 122,
    [Description("无我闪")]
    DoubleShot = 123,
    [Description("爆阱")]
    ExplosiveTrap = 124,
    [Description("爆闪")]
    DelayedExplosion = 125,
    [Description("气功术")]
    Meditation = 126,
    [Description("万斤闪")]
    BackStep = 127,
    [Description("气流术")]
    ElementalShot = 128,
    [Description("金刚术")]
    Concentration = 129,
    [Description("风弹步")]
    Stonetrap = 130,
    [Description("自然囚笼")]
    ElementalBarrier = 131,
    [Description("吸血地精")]
    SummonVampire = 132,
    [Description("吸血地闪")]
    VampireShot = 133,
    [Description("痹魔阱")]
    SummonToad = 134,
    [Description("毒魔闪")]
    PoisonShot = 135,
    [Description("邪爆闪")]
    CrippleShot = 136,
    [Description("蛇柱阱")]
    SummonSnakes = 137,
    [Description("血龙闪")]
    NapalmShot = 138,
    [Description("OneWithNature")]
    OneWithNature = 139,
    [Description("BindingShot")]
    BindingShot = 140,
    [Description("精神状态")]
    MentalState = 141,

    //Custom
    [Description("Blink")]
    Blink = 151,
    [Description("Portal")]
    Portal = 152,
    [Description("BattleCry")]
    BattleCry = 153,
    [Description("FireBounce")]
    FireBounce = 154,
    [Description("MeteorShower")]
    MeteorShower = 155,

    //Map Events
    [Description("DigOutZombie")]
    DigOutZombie = 200,
    [Description("Rubble")]
    Rubble = 201,
    [Description("MapLightning")]
    MapLightning = 202,
    [Description("MapLava")]
    MapLava = 203,
    [Description("MapQuake1")]
    MapQuake1 = 204,
    [Description("MapQuake2")]
    MapQuake2 = 205
}

public enum SpellEffect : byte
{
    None,
    FatalSword,
    Teleport,
    Healing,
    RedMoonEvil,
    TwinDrakeBlade,
    MagicShieldUp,
    MagicShieldDown,
    GreatFoxSpirit,
    Entrapment,
    Reflect,
    Critical,
    Mine,
    ElementalBarrierUp,
    ElementalBarrierDown,
    DelayedExplosion,
    MPEater,
    Hemorrhage,
    Bleeding,
    AwakeningSuccess,
    AwakeningFail,
    AwakeningMiss,
    AwakeningHit,
    StormEscape,
    TurtleKing,
    Behemoth,
    Stunned,
    IcePillar,
    KingGuard,
}


public enum BuffType : byte
{
    None = 0,

    //magics
    [Description("移动速度减少")]
    TemporalFlux,
    [Description("隐身")]
    Hiding,
    [Description("体迅风")]
    Haste,
    [Description("轻身步")]
    SwiftFeet,
    [Description("血龙剑法")]
    Fury,
    [Description("幽灵盾")]
    SoulShield,
    [Description("神圣战甲术")]
    BlessedArmour,
    [Description("风身术")]
    LightBody,
    [Description("无极真气")]
    UltimateEnhancer,
    [Description("护体气幕")]
    ProtectionField,
    [Description("剑气爆")]
    Rage,
    [Description("诅咒")]
    Curse,
    [Description("月隐术")]
    MoonLight,
    [Description("烈火身")]
    DarkBody,
    [Description("气流术")]
    Concentration,
    [Description("吸血地闪")]
    VampireShot,
    [Description("毒魔闪")]
    PoisonShot,
    [Description("天务")]
    CounterAttack,
    [Description("")]
    MentalState,
    [Description("先天真气")]
    EnergyShield,
    [Description("深延术")]
    MagicBooster,
    [Description("血龙水")]
    PetEnhancer,
    [Description("金刚不坏")]
    ImmortalSkin,
    [Description("魔法盾")]
    MagicShield,
    [Description("")]
    ElementalBarrier,
    天上秘术 = 27,
    万效符 = 28,
    万效符秘籍 = 29,

    //monster
    HornedArcherBuff = 50,
    ColdArcherBuff = 51,
    HornedColdArcherBuff = 52,
    GeneralMeowMeowShield = 53,
    惩戒真言 = 54,
    御体之力 = 55,
    HornedWarriorShield = 56,
    Mon409BShieldBuff = 57,
    失明状态 = 58,
    ChieftainSwordBuff = 59,
    寒冰护甲 = 60,
    ReaperPriestBuff = 61,
    至尊威严 = 62,
    伤口加深 = 63,
    死亡印记 = 64,
    RiklebitesShield = 65,
    麻痹状态 = 66,
    绝对封锁 = 67,
    Mon564NSealing = 68,
    烈火焚烧 = 69,
    防御诅咒 = 70,
    Mon579BShield = 71,
    Mon580BShield = 72,
    万效符爆杀 = 73,

    //special
    游戏管理 = 100,
    General,
    获取经验提升,
    物品掉落提升,
    金币辉煌,
    背包负重提升,
    变形效果,
    心心相映,
    衣钵相传,
    火传穷薪,
    公会特效,
    Prison,
    精力充沛,
    技巧项链,
    隐身戒指,
    新人特效,
    技能经验提升,
    英雄灵气,
    暗影侵袭,
    攻击型绝技,
    防御型绝技,
    技能型绝技,
    共用型绝技,
    GameMaster = 游戏管理,
    Exp = 获取经验提升,
    Drop = 物品掉落提升,
    Gold = 金币辉煌,
    BagWeight = 背包负重提升,
    Transform = 变形效果,
    RelationshipEXP = 心心相映,
    Mentee = 衣钵相传,
    Mentor = 火传穷薪,
    Guild = 公会特效,
    Rested = 精力充沛,
    Skill = 技巧项链,
    ClearRing = 隐身戒指,

    //stats
    [Description("攻击")]
    Impact = 200,
    [Description("魔法")]
    Magic,
    [Description("道术")]
    Taoist,
    [Description("攻速")]
    Storm,
    [Description("生命值")]
    HealthAid,
    [Description("魔法值")]
    ManaAid,
    [Description("防御")]
    Defence,
    [Description("魔御")]
    MagicDefence,
    [Description("奇异圣水")]
    WonderDrug,
    [Description("负重")]
    Knapsack,
    龍之祝福 = 210,
    华丽雨光 = 215,
    龙之特效 = 216,
    龙的特效 = 217,
}

public enum BuffStackType : byte
{
    Reset,
    Duration,
    Stat
}

public enum DefenceType : byte
{
    ACAgility,
    AC,
    MACAgility,
    MAC,
    Agility,
    Repulsion,
    None
}

public enum ServerPacketIds : short
{
    Connected = 0,
    ClientVersion = 1,
    Disconnect = 2,
    KeepAlive = 3,
    NewAccount = 4,
    ChangePassword = 5,
    ChangePasswordBanned = 6,
    Login = 7,
    LoginBanned = 8,
    LoginSuccess = 9,
    NewCharacter = 10,
    NewCharacterSuccess = 11,
    DeleteCharacter = 12,
    DeleteCharacterSuccess = 13,
    StartGame = 14,
    StartGameBanned = 15,
    StartGameDelay = 16,
    MapInformation = 17,
    NewMapInfo = 18,
    WorldMapSetup = 19,
    SearchMapResult = 20,
    UserInformation = 21,
    UserSlotsRefresh = 22,
    UserLocation = 23,
    ObjectPlayer = 24,
    ObjectHero = 25,
    ObjectRemove = 26,
    ObjectTurn = 27,
    ObjectWalk = 28,
    ObjectRun = 29,
    Chat = 30,
    ObjectChat = 31,
    NewItemInfo = 32,
    NewHeroInfo = 33,
    NewChatItem = 34,
    MoveItem = 35,
    EquipItem = 36,
    MergeItem = 37,
    RemoveItem = 38,
    RemoveSlotItem = 39,
    TakeBackItem = 40,
    StoreItem = 41,
    SplitItem = 42,
    SplitItem1 = 43,
    DepositRefineItem = 44,
    RetrieveRefineItem = 45,
    RefineCancel = 46,
    RefineItem = 47,
    DepositTradeItem = 48,
    RetrieveTradeItem = 49,
    UseItem = 50,
    DropItem = 51,
    TakeBackHeroItem = 52,
    TransferHeroItem = 53,
    PlayerUpdate = 54,
    PlayerInspect = 55,
    LogOutSuccess = 56,
    LogOutFailed = 57,
    ReturnToLogin = 58,
    TimeOfDay = 59,
    ChangeAMode = 60,
    ChangePMode = 61,
    ObjectItem = 62,
    ObjectGold = 63,
    GainedItem = 64,
    GainedGold = 65,
    LoseGold = 66,
    GainedCredit = 67,
    LoseCredit = 68,
    ObjectMonster = 69,
    ObjectAttack = 70,
    Struck = 71,
    ObjectStruck = 72,
    DamageIndicator = 73,
    DuraChanged = 74,
    HealthChanged = 75,
    HeroHealthChanged = 76,
    DeleteItem = 77,
    Death = 78,
    ObjectDied = 79,
    ColourChanged = 80,
    ObjectColourChanged = 81,
    ObjectGuildNameChanged = 82,
    GainExperience = 83,
    GainHeroExperience = 84,
    LevelChanged = 85,
    HeroLevelChanged = 86,
    ObjectLeveled = 87,
    ObjectHarvest = 88,
    ObjectHarvested = 89,
    ObjectNpc = 90,
    NPCResponse = 91,
    ObjectHide = 92,
    ObjectShow = 93,
    Poisoned = 94,
    ObjectPoisoned = 95,
    MapChanged = 96,
    ObjectTeleportOut = 97,
    ObjectTeleportIn = 98,
    TeleportIn = 99,
    NPCGoods = 100,
    NPCSell = 101,
    NPCRepair = 102,
    NPCSRepair = 103,
    NPCRefine = 104,
    NPCCheckRefine = 105,
    NPCCollectRefine = 106,
    NPCReplaceWedRing = 107,
    NPCStorage = 108,
    SellItem = 109,
    CraftItem = 110,
    RepairItem = 111,
    ItemRepaired = 112,
    ItemSlotSizeChanged = 113,
    ItemSealChanged = 114,
    NewMagic = 115,
    RemoveMagic = 116,
    MagicLeveled = 117,
    Magic = 118,
    MagicDelay = 119,
    MagicCast = 120,
    ObjectMagic = 121,
    ObjectEffect = 122,
    ObjectProjectile = 123,
    RangeAttack = 124,
    Pushed = 125,
    ObjectPushed = 126,
    ObjectName = 127,
    UserStorage = 128,
    SwitchGroup = 129,
    DeleteGroup = 130,
    DeleteMember = 131,
    GroupInvite = 132,
    AddMember = 133,
    Revived = 134,
    ObjectRevived = 135,
    SpellToggle = 136,
    ObjectHealth = 137,
    ObjectMana = 138,
    MapEffect = 139,
    AllowObserve = 140,
    ObjectRangeAttack = 141,
    AddBuff = 142,
    RemoveBuff = 143,
    PauseBuff = 144,
    ObjectHidden = 145,
    RefreshItem = 146,
    ObjectSpell = 147,
    UserDash = 148,
    ObjectDash = 149,
    UserDashFail = 150,
    ObjectDashFail = 151,
    NPCConsign = 152,
    NPCMarket = 153,
    NPCMarketPage = 154,
    ConsignItem = 155,
    MarketFail = 156,
    MarketSuccess = 157,
    ObjectSitDown = 158,
    InTrapRock = 159,
    BaseStatsInfo = 160,
    HeroBaseStatsInfo = 161,
    UserName = 162,
    ChatItemStats = 163,
    GuildNoticeChange = 164,
    GuildMemberChange = 165,
    GuildStatus = 166,
    GuildInvite = 167,
    GuildExpGain = 168,
    GuildNameRequest = 169,
    GuildStorageGoldChange = 170,
    GuildStorageItemChange = 171,
    GuildStorageList = 172,
    GuildRequestWar = 173,
    HeroCreateRequest = 174,
    NewHero = 175,
    HeroInformation = 176,
    UpdateHeroSpawnState = 177,
    UnlockHeroAutoPot = 178,
    SetAutoPotValue = 179,
    SetAutoPotItem = 180,
    SetHeroBehaviour = 181,
    ManageHeroes = 182,
    ChangeHero = 183,
    DefaultNPC = 184,
    NPCUpdate = 185,
    NPCImageUpdate = 186,
    MarriageRequest = 187,
    DivorceRequest = 188,
    MentorRequest = 189,
    TradeRequest = 190,
    TradeAccept = 191,
    TradeGold = 192,
    TradeItem = 193,
    TradeConfirm = 194,
    TradeCancel = 195,
    MountUpdate = 196,
    EquipSlotItem = 197,
    FishingUpdate = 198,
    ChangeQuest = 199,
    CompleteQuest = 200,
    ShareQuest = 201,
    NewQuestInfo = 202,
    GainedQuestItem = 203,
    DeleteQuestItem = 204,
    CancelReincarnation = 205,
    RequestReincarnation = 206,
    UserBackStep = 207,
    ObjectBackStep = 208,
    UserDashAttack = 209,
    ObjectDashAttack = 210,
    UserAttackMove = 211,
    CombineItem = 212,
    ItemUpgraded = 213,
    SetConcentration = 214,
    SetElemental = 215,
    RemoveDelayedExplosion = 216,
    ObjectDeco = 217,
    ObjectSneaking = 218,
    ObjectLevelEffects = 219,
    SetBindingShot = 220,
    SendOutputMessage = 221,
    NPCAwakening = 222,
    NPCDisassemble = 223,
    NPCDowngrade = 224,
    NPCReset = 225,
    AwakeningNeedMaterials = 226,
    AwakeningLockedItem = 227,
    Awakening = 228,
    ReceiveMail = 229,
    MailLockedItem = 230,
    MailSendRequest = 231,
    MailSent = 232,
    ParcelCollected = 233,
    MailCost = 234,
    ResizeInventory = 235,
    ResizeStorage = 236,
    NewIntelligentCreature = 237,
    UpdateIntelligentCreatureList = 238,
    IntelligentCreatureEnableRename = 239,
    IntelligentCreaturePickup = 240,
    NPCPearlGoods = 241,
    TransformUpdate = 242,
    FriendUpdate = 243,
    LoverUpdate = 244,
    MentorUpdate = 245,
    GuildBuffList = 246,
    NPCRequestInput = 247,
    GameShopInfo = 248,
    GameShopStock = 249,
    Rankings = 250,
    Opendoor = 251,
    GetRentedItems = 252,
    ItemRentalRequest = 253,
    ItemRentalFee = 254,
    ItemRentalPeriod = 255,
    DepositRentalItem = 256,
    RetrieveRentalItem = 257,
    UpdateRentalItem = 258,
    CancelItemRental = 259,
    ItemRentalLock = 260,
    ItemRentalPartnerLock = 261,
    CanConfirmItemRental = 262,
    ConfirmItemRental = 263,
    NewRecipeInfo = 264,
    OpenBrowser = 265,
    PlaySound = 266,
    SetTimer = 267,
    ExpireTimer = 268,
    UpdateNotice = 269,
    Roll = 270,
    SetCompass = 271,
    GroupMembersMap = 272,
    SendMemberLocation = 273,
    Fg = 274
}

public enum ClientPacketIds : short
{
    ClientVersion = 0,
    Disconnect = 1,
    KeepAlive = 2,
    NewAccount = 3,
    ChangePassword = 4,
    Login = 5,
    NewCharacter = 6,
    DeleteCharacter = 7,
    StartGame = 8,
    LogOut = 9,
    Turn = 10,
    Walk = 11,
    Run = 12,
    Chat = 13,
    MoveItem = 14,
    StoreItem = 15,
    TakeBackItem = 16,
    MergeItem = 17,
    EquipItem = 18,
    RemoveItem = 19,
    RemoveSlotItem = 20,
    SplitItem = 21,
    UseItem = 22,
    DropItem = 23,
    DepositRefineItem = 24,
    RetrieveRefineItem = 25,
    RefineCancel = 26,
    RefineItem = 27,
    CheckRefine = 28,
    ReplaceWedRing = 29,
    DepositTradeItem = 30,
    RetrieveTradeItem = 31,
    TakeBackHeroItem = 32,
    TransferHeroItem = 33,
    DropGold = 34,
    PickUp = 35,
    RequestMapInfo = 36,
    TeleportToNPC = 37,
    SearchMap = 38,
    Inspect = 39,
    Observe = 40,
    ChangeAMode = 41,
    ChangePMode = 42,
    ChangeTrade = 43,
    Attack = 44,
    RangeAttack = 45,
    Harvest = 46,
    CallNPC = 47,
    BuyItem = 48,
    SellItem = 49,
    CraftItem = 50,
    RepairItem = 51,
    BuyItemBack = 52,
    SRepairItem = 53,
    MagicKey = 54,
    Magic = 55,
    SwitchGroup = 56,
    AddMember = 57,
    DellMember = 58,
    GroupInvite = 59,
    NewHero = 60,
    SetAutoPotValue = 61,
    SetAutoPotItem = 62,
    SetHeroBehaviour = 63,
    ChangeHero = 64,
    TownRevive = 65,
    SpellToggle = 66,
    ConsignItem = 67,
    MarketSearch = 68,
    MarketRefresh = 69,
    MarketPage = 70,
    MarketBuy = 71,
    MarketGetBack = 72,
    MarketSellNow = 73,
    RequestUserName = 74,
    RequestChatItem = 75,
    EditGuildMember = 76,
    EditGuildNotice = 77,
    GuildInvite = 78,
    GuildNameReturn = 79,
    RequestGuildInfo = 80,
    GuildStorageGoldChange = 81,
    GuildStorageItemChange = 82,
    GuildWarReturn = 83,
    MarriageRequest = 84,
    MarriageReply = 85,
    ChangeMarriage = 86,
    DivorceRequest = 87,
    DivorceReply = 88,
    AddMentor = 89,
    MentorReply = 90,
    AllowMentor = 91,
    CancelMentor = 92,
    TradeRequest = 93,
    TradeReply = 94,
    TradeGold = 95,
    TradeConfirm = 96,
    TradeCancel = 97,
    EquipSlotItem = 98,
    FishingCast = 99,
    FishingChangeAutocast = 100,
    AcceptQuest = 101,
    FinishQuest = 102,
    AbandonQuest = 103,
    ShareQuest = 104,
    AcceptReincarnation = 105,
    CancelReincarnation = 106,
    CombineItem = 107,
    AwakeningNeedMaterials = 108,
    AwakeningLockedItem = 109,
    Awakening = 110,
    DisassembleItem = 111,
    DowngradeAwakening = 112,
    ResetAddedItem = 113,
    SendMail = 114,
    ReadMail = 115,
    CollectParcel = 116,
    DeleteMail = 117,
    LockMail = 118,
    MailLockedItem = 119,
    MailCost = 120,
    UpdateIntelligentCreature = 121,
    IntelligentCreaturePickup = 122,
    RequestIntelligentCreatureUpdates = 123,
    AddFriend = 124,
    RemoveFriend = 125,
    RefreshFriends = 126,
    AddMemo = 127,
    GuildBuffUpdate = 128,
    NPCConfirmInput = 129,
    GameshopBuy = 130,
    ReportIssue = 131,
    GetRanking = 132,
    Opendoor = 133,
    GetRentedItems = 134,
    ItemRentalRequest = 135,
    ItemRentalFee = 136,
    ItemRentalPeriod = 137,
    DepositRentalItem = 138,
    RetrieveRentalItem = 139,
    CancelItemRental = 140,
    ItemRentalLockFee = 141,
    ItemRentalLockItem = 142,
    ConfirmItemRental = 143,
    Fg = 144
}

public enum ConquestType : byte
{
    Request = 0,
    Auto = 1,
    Forced = 2,
}

public enum ConquestGame : byte
{
    CapturePalace = 0,
    KingOfHill = 1,
    Random = 2,
    Classic = 3,
    ControlPoints = 4
}

[Flags]
public enum GuildRankOptions : byte
{
    CanChangeRank = 1,
    CanRecruit = 2,
    CanKick = 4,
    CanStoreItem = 8,
    CanRetrieveItem = 16,
    CanAlterAlliance = 32,
    CanChangeNotice = 64,
    CanActivateBuff = 128
}

public enum DoorState : byte
{
    Closed = 0,
    Opening = 1,
    Open = 2,
    Closing = 3
}

public enum IntelligentCreaturePickupMode : byte
{
    Automatic = 0,
    SemiAutomatic = 1,
}


public enum FgType : byte
{
    ScreenShot = 0,
    Process = 1,
}

public enum WeatherSetting : ushort
{
    无效果 = 0,
    雾天 = 1,
    红色余烬 = 2,
    白色余烬 = 4,
    黄色余烬 = 8,
    落雪 = 16,
    飘雪 = 32,
    雨天 = 64,
    黄色花瓣 = 128,
    红色花瓣 = 256,
    粉色花瓣 = 512,
    沙尘 = 1024,
    沙雾 = 2048,
}

public enum HeroSpawnState : byte
{
    None = 0,
    Unsummoned = 1,
    Summoned = 2,
    Dead = 3
}

public enum HeroBehaviour : byte
{
    攻击 = 0,
    反击 = 1,
    跟随 = 2,
    自定 = 3,
    守护 = 4,
    跑回 = 5,
    瞬回 = 6
}

public enum SpellToggleState : sbyte
{
    None = -1,
    False = 0,
    True = 1
}

public enum MarketCollectionMode : byte
{
    Any = 0,
    Sold = 1,
    Expired = 2
}