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
    Critical = 2,
    HpRegen = 3,
    Poisoning = 4
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
public enum LevelEffects : ushort
{
    None = 0,
    Mist = 1,
    RedDragon = 2,
    BlueDragon = 4,
    Rebirth1 = 8,
    Rebirth2 = 16,
    Rebirth3 = 32,
    NewBlue = 64,
    YellowDragon = 128,
    Phoenix = 256
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
    普通 = 1,
    Common = 普通,
    宝物 = 2,
    Rare = 宝物,
    圣物 = 3,
    Legendary = 圣物,
    神物 = 4,
    Mythical = 神物,
    英雄 = 5,
    Heroic = 英雄,
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
    一般 = 0,
    General = 一般,
    每日 = 1,
    Daily = 每日,
    重复 = 2,
    Repeatable = 重复,
    主线 = 3,
    Story = 主线
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

public enum QuestAction : byte
{
    TimeExpired
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
    小猪 = 0,
    BabyPig = 小猪,
    小鸡 = 1,
    Chick = 小鸡,
    小猫 = 2,
    Kitten = 小猫,
    精灵骷髅 = 3,
    BabySkeleton = 精灵骷髅,
    白猪 = 4,
    Baekdon = 白猪,
    纸片人 = 5,
    Wimaen = 纸片人,
    黑猫 = 6,
    BlackKitten = 黑猫,
    龙蛋 = 7,
    BabyDragon = 龙蛋,
    火娃 = 8,
    OlympicFlame = 火娃,
    雪人 = 9,
    BabySnowMan = 雪人,
    青蛙 = 10,
    Frog = 青蛙,
    红猴 = 11,
    BabyMonkey = 红猴,
    愤怒的小鸟 = 12,
    AngryBird = 愤怒的小鸟,
    阿福 = 13,
    Foxey = 阿福,
    治疗拉拉 = 14,
    MedicalRat = 治疗拉拉,
    猫咪超人 = 15,
    龙宝宝 = 16,
}

//2 blank mob files
public enum Monster : ushort
{
    Guard = 0,  //Mon1.wil
    ForestYeti = 1,
    TaoistGuard = 2,
    Football = 3,
    Guard2 = 4,
    CannibalPlant = 10,  //Mon2.wil
    HalloweenScythe = 12,
    GiantEgg = 13,
    Wolf = 14,
    DarkBrownWolf = 15,
    Bull = 17,
    Skeleton = 20,  //Mon3.wil
    AxeSkeleton = 21,
    BoneFighter = 22,
    BoneWarrior = 23,
    CaveMaggot = 24,
    HookingCat = 25,
    RakingCat = 26,
    Scarecrow = 27,
    Dark = 28,
    Dung = 29,
    WoomaSoldier = 30,  //Mon4.wil
    FlamingWooma = 31,
    WoomaFighter = 32,
    WoomaWarrior = 33,
    WoomaTaurus = 34,
    RedSnake = 35,
    BoneFamiliar = 36,
    TigerSnake = 37,
    WedgeMoth = 38,
    ShamanZombie = 40,  //Mon5.wil
    BugBatMaggot = 41,
    BugBat = 42,
    Sheep = 43,
    SkyStinger = 44,
    ShellNipper = 45,
    BigRat = 46,
    ZumaArcher = 47,
    VisceralWorm = 48,
    SandWorm = 49,
    DigOutZombie = 50,  //Mon6.wil
    ClZombie = 51,
    NdZombie = 52,
    CrawlerZombie = 53,
    DarkDevourer = 54,
    PoisonHugger = 55,
    Hugger = 56,
    Behemoth = 57,
    TailedLion = 58,
    HighAssassin = 59,
    ZumaStatue = 61,  //Mon7.wil
    ZumaGuardian = 62,
    ZumaTaurus = 63,
    MudPile = 64,
    DarkDustPile = 65,
    SnowPile = 66,
    MutatedHugger = 67,
    GingerBreadman = 68,
    Bush = 69,
    GreyWolf = 70,  //Mon8.wil
    ArcherGuard = 71,
    KatanaGuard = 72,
    Centipede = 73,
    BlackMaggot = 74,
    CaveBat = 80,  //Mon9.wil
    WhimperingBee = 81,
    GiantWorm = 82,
    Scorpion = 83,
    Keratoid = 90,  //Mon10.wil
    GiantKeratoid = 91,
    RedEvilApe = 92,
    GrayEvilApe = 93,
    Oma = 100,  //Mon11.wil
    OmaFighter = 101,
    OmaWarrior = 102,
    SpiderFrog = 103,
    HoroBlaster = 104,
    BlueHoroBlaster = 105,
    KekTal = 106,
    VioletKekTal = 107,
    RedBoar = 110,  //Mon12.wil
    BlackBoar = 111,
    WhiteBoar = 112,
    SpiderBat = 113,
    GangSpider = 114,
    BigApe = 115,
    EvilApe = 116,
    LureSpider = 117,
    GreatSpider = 118,
    VenomSpider = 119,
    Tongs = 120,  //Mon13.wil
    EvilTongs = 121,
    SnakeScorpion = 130,  //Mon14.wil
    RedMoonEvil = 131,
    RootSpider = 132,
    BombSpider = 133,
    EvilCentipede1 = 134,
    GhastlyLeecher = 135,
    CyanoGhast = 136,
    MutatedManworm = 137,
    CrazyManworm = 138,
    DreamDevourer = 139,
    EvilCentipede = 140,  //Mon15.wil
    ChestnutTree = 141,
    ChristmasTree = 142,
    EbonyTree = 143,
    LargeMushroom = 144,
    Treasurebox = 145,
    SnowTree = 146,
    Snowman = 147,
    CherryTree = 148,
    BoneElite = 150,  //Mon16.wil
    WoomaGuardian = 151,
    Ghoul = 152,
    Hen = 160,  //Mon17.wil
    Deer = 161,
    Yob = 162,
    SpittingSpider = 163,
    EvilSnake = 164,
    Shinsu = 170,  //Mon18.wil
    Shinsu1 = 171,
    HolyDeva = 172,
    KingScorpion = 180,  //Mon19.wil
    KingHog = 181,
    DarkDevil = 182,
    RedTurtle = 183,
    GreenTurtle = 184,
    BlueTurtle = 185,
    RoninGhoul = 190,  //Mon20.wil
    ToxicGhoul = 191,
    BoneCaptain = 192,
    BoneSpearman = 193,
    BoneBlademan = 194,
    BoneArcher = 195,
    BoneLord = 196,
    BlueSanta = 197,
    BattleStandard = 198,
    ArcherGuard2 = 199,
    Minotaur = 200,  //Mon21.wil
    IceMinotaur = 201,
    ElectricMinotaur = 202,
    WindMinotaur = 203,
    FireMinotaur = 204,
    RightGuard = 205,
    LeftGuard = 206,
    MinotaurKing = 207,
    RedYimoogi = 208,
    WingedOma = 210,  //Mon22.wil
    FlailOma = 211,
    OmaGuard = 212,
    SwordOma = 213,
    AxeOma = 214,
    CrossbowOma = 215,
    YangDevilNode = 216,
    YinDevilNode = 217,
    OmaKing = 218,
    Khazard = 220,  //Mon23.wil
    RedThunderZuma = 221,
    FrostTiger = 222,
    CrystalSpider = 223,
    Yimoogi = 224,
    GiantWhiteSnake = 225,
    YellowSnake = 226,
    BlueSnake = 227,
    FlameTiger = 228,
    WingedTigerLord = 229,
    BlackFoxman = 230,  //Mon24.wil
    RedFoxman = 231,
    WhiteFoxman = 232,
    TrapRock = 233,
    GuardianRock = 234,
    ThunderElement = 235,
    CloudElement = 236,
    GreatFoxSpirit = 237,
    HedgeKekTal = 238,
    BigHedgeKekTal = 239,
    RedFrogSpider = 240,  //Mon25.wil
    BrownFrogSpider = 241,
    TowerTurtle = 242,
    FinialTurtle = 243,
    TurtleKing = 244,
    DarkTurtle = 245,
    LightTurtle = 246,
    DarkSwordOma = 247,
    DarkAxeOma = 248,
    DarkCrossbowOma = 249,
    DarkWingedOma = 250,  //Mon26.wil
    BoneWhoo = 251,
    DarkSpider = 252,
    ViscusWorm = 253,
    ViscusCrawler = 254,
    CrawlerLave = 255,
    DarkYob = 256,
    FlamingMutant = 257,
    StoningStatue = 258,
    FlyingStatue = 259,
    ValeBat = 260,  //Mon27.wil
    Weaver = 261,
    VenomWeaver = 262,
    CrackingWeaver = 263,
    ArmingWeaver = 264,
    CrystalWeaver = 265,
    FrozenZumaStatue = 266,
    FrozenZumaGuardian = 267,
    FrozenRedZuma = 268,
    GreaterWeaver = 269,
    SpiderWarrior = 270,  //Mon28.wil
    Mon271N = 271,
    HellSlasher = 272,
    HellPirate = 273,
    HellCannibal = 274,
    HellKeeper = 275,
    HellBolt = 276,
    WitchDoctor = 277,
    ManectricHammer = 278,
    ManectricClub = 279,
    ManectricClaw = 280,  //Mon29.wil
    ManectricStaff = 281,
    NamelessGhost = 282,
    DarkGhost = 283,
    ChaosGhost = 284,
    ManectricBlest = 285,
    ManectricKing = 286,
    Blank3 = 287,
    IcePillar = 288,
    FrostYeti = 289,
    ManectricSlave = 290,  //Mon30.wil
    TrollHammer = 291,
    TrollBomber = 292,
    TrollStoner = 293,
    TrollKing = 294,
    FlameSpear = 295,
    FlameMage = 296,
    FlameScythe = 297,
    FlameAssassin = 298,
    FlameQueen = 299,
    HellKnight1 = 300,  //Mon31.wil
    HellKnight2 = 301,
    HellKnight3 = 302,
    HellKnight4 = 303,
    HellLord = 304,
    WaterGuard = 305,
    IceGuard = 306,
    ElementGuard = 307,
    DemonGuard = 308,
    KingGuard = 309,
    Snake10 = 310,  //Mon32.wil
    Snake11 = 311,
    Snake12 = 312,
    Snake13 = 313,
    Snake14 = 314,
    Snake15 = 315,
    Snake16 = 316,
    Snake17 = 317,
    DeathCrawler = 318,
    BurningZombie = 319,
    MudZombie = 320,  //Mon33.wil
    FrozenZombie = 321,
    UndeadWolf = 322,
    DemonWolf = 323,
    Demonwolf = DemonWolf,
    WhiteMammoth = 324,
    DarkBeast = 325,
    LightBeast = 326,
    BloodBaboon = 327,
    HardenRhino = 328,
    AncientBringer = 329,
    FightingCat = 330,  //Mon34.wil
    FireCat = 331,
    CatWidow = 332,
    StainHammerCat = 333,
    BlackHammerCat = 334,
    StrayCat = 335,
    CatShaman = 336,
    Jar1 = 337,
    Jar2 = 338,
    SeedingsGeneral = 339,
    RestlessJar = 340,  //Mon35.wil
    GeneralMeowMeow = 341,
    Bunny = 342,
    Tucson = 343,
    TucsonFighter = 344,
    TucsonMage = 345,
    TucsonWarrior = 346,
    Armadillo = 347,
    ArmadilloElder = 348,
    TucsonEgg = 350,  //Mon36.wil
    PlaguedTucson = 351,
    SandSnail = 352,
    CannibalTentacles = 353,
    TucsonGeneral = 354,
    GasToad = 355,
    Mantis = 356,
    SwampWarrior = 357,
    AssassinBird = 358,
    RhinoWarrior = 359,
    RhinoPriest = 360,  //Mon37.wil
    ElephantMan = 361,
    StoneGolem = 362,
    EarthGolem = 363,
    TreeGuardian = 364,
    TreeQueen = 365,
    PeacockSpider = 366,
    DarkBaboon = 367,
    TwinHeadBeast = 368,
    OmaCannibal = 369,
    OmaBlest = 370,  //Mon38.wil
    OmaSlasher = 371,
    OmaAssassin = 372,
    OmaMage = 373,
    OmaWitchDoctor = 374,
    LightningBead = 375,
    HealingBead = 376,
    PowerUpBead = 377,
    DarkOmaKing = 378,
    Mon380P = 380,  //Mon39.wil
    Mandrill = 381,
    PlagueCrab = 382,
    CreeperPlant = 383,
    FloatingWraith = 384,
    ArmedPlant = 385,
    AvengerPlant = 386,
    Nadz = 387,
    AvengingSpirit = 388,
    AvengingWarrior = 390,  //Mon40.wil
    AxePlant = 391,
    WoodBox = 392,
    ClawBeast = 393,
    WaterSoul = 394,
    DarkCaptain = 395,
    SackWarrior = 396,
    WereTiger = 397,
    KingHydrax = 398,
    Hydrax = 399,
    HornedMage = 400,  //Mon41.wil
    HornedArcher = 401,
    ColdArcher = 402,
    HornedWarrior = 403,
    FloatingRock = 404,
    ScalyBeast = 405,
    HornedSorceror = 406,
    BlueSoul = 407,
    BoulderSpirit = 408,
    Mon409B = 409,
    Turtlegrass = 410, //Mon42.wil
    ManTree = 411,
    Bear = 412,
    Leopard = 413,
    ChieftainSword = 414,
    MoonStone = 415,
    SunStone = 416,
    ChieftainArcher = 417,
    LightningStone = 418,
    StoningSpider = 419,
    VampireSpider = 420,  //Mon43.wil
    SpittingToad = 421,
    SnakeTotem = 422,
    CharmedSnake = 423,
    FrozenSoldier = 424,
    FrozenFighter = 425,
    FrozenArcher = 426,
    FrozenKnight = 427,
    FrozenGolem = 428,
    IcePhantom = 429,
    SnowWolf = 430,  //Mon44.wil
    SnowWolfKing = 431,
    WaterDragon = 432,
    BlackTortoise = 433,
    Manticore = 434,
    DragonWarrior = 435,
    DragonArcher = 436,
    FlameWhirlwind = 437,
    Kirin = 438,
    Guard3 = 439,
    ArcherGuard3 = 440,  //Mon45.wil
    Bunny2 = 441,
    FrozenMiner = 442,
    Mon443N = 443,
    FrozenMagician = 444,
    SnowYeti = 445,
    IceCrystalSoldier = 446,
    DarkWraith = 447,
    DarkSpirit = 448,
    CrystalBeast = 449,
    RedOrb = 450,  //Mon46.wil
    BlueOrb = 451,
    YellowOrb = 452,
    GreenOrb = 453,
    WhiteOrb = 454,
    FatalLotus = 455,
    AntCommander = 456,
    CargoBoxwithlogo = 457,
    Doe = 458,
    Reindeer = 459,
    AngryReindeer = 460,  //Mon47.wil
    CargoBox = 461,
    Ram1 = 462,
    Ram2 = 463,
    Kite = 465,
    PurpleFaeFlower = 466,
    Furball = 467,
    GlacierSnail = 468,
    FurbolgWarrior = 469,
    FurbolgArcher = 470,  //Mon48.wil
    FurbolgCommander = 471,
    Mon472N = 472,
    FurbolgGuard = 473,
    GlacierBeast = 474,
    GlacierWarrior = 475,
    ShardGuardian = 476,
    CallScroll = 477,
    PoisonScroll = 478,
    FireballScroll = 479,
    LightningScroll = 480,  //Mon49.wil
    HoodedSummoner = 481,
    HoodedIceMage = 482,
    HoodedPriest = 483,
    ShardMaiden = 484,
    KingKong = 485,
    WarBear = 486,
    ReaperPriest = 487,
    ReaperWizard = 488,
    ReaperAssassin = 489,
    LivingVines = 490,  //Mon50.wil
    BlueMonk = 491,
    MutantBeserker = 492,
    MutantGuardian = 493,
    MutantHighPriest = 494,
    MysteriousMage = 495,
    FeatheredWolf = 496,
    MysteriousAssassin = 497,
    MysteriousMonk = 498,
    ManEatingPlant = 499,
    HammerDwarf = 500,  //Mon51.wil
    ArcherDwarf = 501,
    NobleWarrior = 502,
    NobleArcher = 503,
    NoblePriest = 504,
    NobleAssassin = 505,
    //ExplosiveSkull = 506,
    //SeaFire = 507,
    Swain = 508,
    Swain1 = 509,
    RedMutantPlant = 510,  //Mon52.wil
    BlueMutantPlant = 511,
    UndeadHammerDwarf = 512,
    UndeadDwarfArcher = 513,
    AncientStoneGolem = 514,
    Serpentirian = 515,
    Butcher = 516,
    Mon517P = 517,
    Riklebites = 518,
    Mon519D = 519,
    Mon520P = 520,  //Mon53.wil
    FeralTundraFurbolg = 521,
    FeralFlameFurbolg = 522,
    EnhanceFlameFurbolg = 523,
    BlueArcaneTotem = 525,
    GreenArcaneTotem = 526,
    RedArcaneTotem = 527,
    YellowArcaneTotem = 528,
    WarpGate = 529,
    SpectralWraith = 530,  //Mon54.wil
    BabyMagmaDragon = 531,
    BloodLord = 532,
    SerpentLord = 533,
    MirEmperor = 534,
    MutantManEatingPlant = 535,
    MutantWarg = 536,
    GrassElemental = 537,
    RockElemental = 538,
    Mon540N = 540,  //Mon55.wil
    Mon541N = 541,
    Mon542N = 542,
    Mon543N = 543,
    Mon544S = 544,
    Mon545N = 545,
    Mon546T = 546,
    Mon547N = 547,
    Mon548N = 548,
    Mon549N = 549,
    Mon550N = 550,  //Mon56.wil
    Mon551N = 551,
    Mon552N = 552,
    Mon553N = 553,
    Mon554N = 554,
    Mon555N = 555,
    Mon556B = 556,
    Mon557B = 557,
    Mon558B = 558,
    Mon559B = 559,
    Mon560N = 560,  //Mon57.wil
    Mon561N = 561,
    Mon562N = 562,
    Mon563N = 563,
    Mon564N = 564,
    Mon565T = 565,
    Mon570B = 570,  //Mon58.wil
    Mon571B = 571,
    Mon572B = 572,
    Mon573B = 573,
    Mon574T = 574,
    Mon575S = 575,
    Mon576T = 576,
    Mon577N = 577,
    Mon578N = 578,
    Mon579B = 579,
    Mon580B = 580,  //Mon59.wil
    Mon581D = 581,
    Mon582D = 582,
    Mon583N = 583,
    Mon584D = 584,
    Mon585D = 585,
    Mon586N = 586,
    Mon587N = 587,
    Mon588N = 588,
    Mon589N = 589,
    Mon590N = 590,  //Mon60.wil
    Mon591N = 591,
    Mon592N = 592,
    Mon593N = 593,
    Mon594N = 594,
    Mon595N = 595,
    Mon596N = 596,
    Mon597N = 597,
    Mon598N = 598,
    Mon599N = 599,
    Mon600N = 600,  //Mon61.wil
    Mon601N = 601,
    Mon602N = 602,
    Mon603B = 603,
    Mon604N = 604,
    Mon605N = 605,
    Mon606N = 606,
    Mon607N = 607,
    Mon608N = 608,
    Mon609N = 609,
    Mon610B = 610,
    //B=Boss D=Door N=Normal P=Peculiar S=Stoned T=Tree

    //Special
    EvilMir = 900,
    EvilMirBody = 901,
    DragonStatue = 902,
    HellBomb1 = 903,
    HellBomb2 = 904,
    HellBomb3 = 905,

    //Siege
    Catapult = 940,
    ChariotBallista = 941,
    Ballista = 942,
    Trebuchet = 943,
    CanonTrebuchet = 944,

    //Gates
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
    NammandGate1 = 960,
    NammandGate2 = 961,
    SabukWallSection = 962,
    NammandWallSection = 963,
    FrozenDoor = 964,
    GonRyunDoor = 965,
    UnderPassDoor1 = 966,
    UnderPassDoor2 = 967,
    InDunFences = 968,

    //Flags 1000 ~ 1100

    //Creatures
    小猪 = 10000,//Permanent
    BabyPig = 小猪,
    小鸡 = 10001,//Special
    Chick = 小鸡,
    小猫 = 10002,//Permanent
    Kitten = 小猫,
    精灵骷髅 = 10003,//Special
    BabySkeleton = 精灵骷髅,
    白猪 = 10004,//Special
    Baekdon = 白猪,
    纸片人 = 10005,//Event
    Wimaen = 纸片人,
    黑猫 = 10006,//unknown
    BlackKitten = 黑猫,
    龙蛋 = 10007,//unknown
    BabyDragon = 龙蛋,
    火娃 = 10008,//unknown
    OlympicFlame = 火娃,
    雪人 = 10009,//unknown
    BabySnowMan = 雪人,
    青蛙 = 10010,//unknown
    Frog = 青蛙,
    红猴 = 10011,//unknown
    BabyMonkey = 红猴,
    愤怒的小鸟 = 10012,
    AngryBird = 愤怒的小鸟,
    阿福 = 10013,
    Foxey = 阿福,
    治疗拉拉 = 10014,
    MedicalRat = 治疗拉拉,
    猫咪超人 = 10015,
    龙宝宝 = 10016,
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
    石化苏醒,
    Appear = 石化苏醒,
    切换LIB,
    Hide = 切换LIB,
    石化状态,
    Stoned = 石化状态,
    召唤初现,
    Show = 召唤初现,
    复活动作,
    Revive = 复活动作,
    坐下动作,
    SitDown = 坐下动作,
    挖矿动作,
    Mine = 挖矿动作,
    刺客潜行,
    Sneek = 刺客潜行,
    刺客冲击,
    DashAttack = 刺客冲击,
    刺客步刺,
    Lunge = 刺客步刺,

    弓箭行走,
    WalkingBow = 弓箭行走,
    弓箭奔跑,
    RunningBow = 弓箭奔跑,
    弓箭跳跃,
    Jump = 弓箭跳跃,

    坐骑站立,
    MountStanding = 坐骑站立,
    坐骑行走,
    MountWalking = 坐骑行走,
    坐骑奔跑,
    MountRunning = 坐骑奔跑,
    坐骑被击,
    MountStruck = 坐骑被击,
    坐骑攻击,
    MountAttack = 坐骑攻击,

    钓鱼抛竿,
    FishingCast = 钓鱼抛竿,
    钓鱼等待,
    FishingWait = 钓鱼等待,
    钓鱼收线,
    FishingReel = 钓鱼收线
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
    战士 = Warrior,
    [Description("法师")]
    Wizard = 1,
    法师 = Wizard,
    [Description("道士")]
    Taoist = 2,
    道士 = Taoist,
    [Description("刺客")]
    Assassin = 3,
    刺客 = Assassin,
    [Description("弓箭手")]
    Archer = 4
    ,
    弓箭 = Archer
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
    Creature = 7,
    Hero = 8
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
    攻城弹药 = 41, //TODO
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
    Socket = 20,
    HeroEquipment = 21,
    HeroInventory = 22,
    HeroHPItem = 23,
    HeroMPItem = 24
}

public enum EquipmentSlot : byte
{
    Weapon = 0,
    武器 = Weapon,
    Armour = 1,
    盔甲 = Armour,
    Helmet = 2,
    头盔 = Helmet,
    Torch = 3,
    照明物 = Torch,
    Necklace = 4,
    项链 = Necklace,
    BraceletL = 5,
    左手镯 = BraceletL,
    BraceletR = 6,
    右手镯 = BraceletR,
    RingL = 7,
    左戒指 = RingL,
    RingR = 8,
    右戒指 = RingR,
    Amulet = 9,
    护身符 = Amulet,
    Belt = 10,
    腰带 = Belt,
    Boots = 11,
    靴子 = Boots,
    Stone = 12,
    守护石 = Stone,
    Mount = 13,
    坐骑 = Mount
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
    FocusMasterTarget = 4,
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
    LRParalysis = 256,
    Blindness = 512,
    Dazed = 1024
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
    NoMail = 16384,
    NoHero = -32768
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

public enum Spell : ushort
{
    None = 0,

    //Warrior
    Fencing = 1,
    基本剑术 = Fencing,
    Slaying = 2,
    攻杀剑术 = Slaying,
    Thrusting = 3,
    刺杀剑术 = Thrusting,
    HalfMoon = 4,
    半月弯刀 = HalfMoon,
    ShoulderDash = 5,
    野蛮冲撞 = ShoulderDash,
    TwinDrakeBlade = 6,
    双龙斩 = TwinDrakeBlade,
    Entrapment = 7,
    捕绳剑 = Entrapment,
    FlamingSword = 8,
    烈火剑法 = FlamingSword,
    LionRoar = 9,
    狮子吼 = LionRoar,
    CrossHalfMoon = 10,
    圆月弯刀 = CrossHalfMoon,
    BladeAvalanche = 11,
    攻破斩 = BladeAvalanche,
    ProtectionField = 12,
    护身气幕 = ProtectionField,
    Rage = 13,
    剑气爆 = Rage,
    CounterAttack = 14,
    反击 = CounterAttack,
    SlashingBurst = 15,
    日闪 = SlashingBurst,
    Fury = 16,
    血龙剑法 = Fury,
    ImmortalSkin = 17,
    金刚不坏 = ImmortalSkin,
    EntrapmentRare = 18,
    ImmortalSkinRare = 19,
    LionRoarRare = 20,
    DimensionalSword = 21,
    DimensionalSwordRare = 22,

    //Wizard
    FireBall = 51,
    火球术 = FireBall,
    Repulsion = 52,
    抗拒火环 = Repulsion,
    ElectricShock = 53,
    诱惑之光 = ElectricShock,
    GreatFireBall = 54,
    大火球 = GreatFireBall,
    HellFire = 55,
    地狱火 = HellFire,
    ThunderBolt = 56,
    雷电术 = ThunderBolt,
    Teleport = 57,
    瞬息移动 = Teleport,
    FireBang = 58,
    爆裂火焰 = FireBang,
    FireWall = 59,
    火墙 = FireWall,
    Lightning = 60,
    疾光电影 = Lightning,
    FrostCrunch = 61,
    寒冰掌 = FrostCrunch,
    ThunderStorm = 62,
    地狱雷光 = ThunderStorm,
    MagicShield = 63,
    魔法盾 = MagicShield,
    TurnUndead = 64,
    圣言术 = TurnUndead,
    Vampirism = 65,
    嗜血术 = Vampirism,
    IceStorm = 66,
    冰咆哮 = IceStorm,
    FlameDisruptor = 67,
    火龙术 = FlameDisruptor,
    Mirroring = 68,
    分身术 = Mirroring,
    FlameField = 69,
    火龙气焰 = FlameField,
    Blizzard = 70,
    天霜冰环 = Blizzard,
    MagicBooster = 71,
    深延术 = MagicBooster,
    MeteorStrike = 72,
    流星火雨 = MeteorStrike,
    IceThrust = 73,
    冰焰术 = IceThrust,
    FastMove = 74,
    StormEscape = 75,
    HeavenlySecrets = 76,
    GreatFireBallRare = 77,
    StormEscapeRare = 78,

    //Taoist
    Healing = 101,
    治愈术 = Healing,
    SpiritSword = 102,
    精神力战法 = SpiritSword,
    Poisoning = 103,
    施毒术 = Poisoning,
    SoulFireBall = 104,
    灵魂火符 = SoulFireBall,
    SummonSkeleton = 105,
    召唤骷髅 = SummonSkeleton,
    Hiding = 107,
    隐身术 = Hiding,
    MassHiding = 108,
    集体隐身术 = MassHiding,
    SoulShield = 109,
    幽灵盾 = SoulShield,
    Revelation = 110,
    心灵启示 = Revelation,
    BlessedArmour = 111,
    神圣战甲术 = BlessedArmour,
    EnergyRepulsor = 112,
    气功波 = EnergyRepulsor,
    TrapHexagon = 113,
    困魔咒 = TrapHexagon,
    Purification = 114,
    净化术 = Purification,
    MassHealing = 115,
    群体治疗术 = MassHealing,
    Hallucination = 116,
    迷魂术 = Hallucination,
    UltimateEnhancer = 117,
    无极真气 = UltimateEnhancer,
    SummonShinsu = 118,
    召唤神兽 = SummonShinsu,
    Reincarnation = 119,
    复活术 = Reincarnation,
    SummonHolyDeva = 120,
    召唤月灵 = SummonHolyDeva,
    Curse = 121,
    诅咒术 = Curse,
    Plague = 122,
    瘟疫 = Plague,
    PoisonCloud = 123,
    毒云 = PoisonCloud,
    EnergyShield = 124,
    阴阳盾 = EnergyShield,
    PetEnhancer = 125,
    血龙水 = PetEnhancer,
    HealingCircle = 126,
    阴阳五行阵 = HealingCircle,
    HealingRare = 127,
    HealingcircleRare = 128,
    PetEnhancerRare = 129,
    MultipleEffects = 130,
    MultipleEffectsRare = 131,

    //Assassin
    FatalSword = 151,
    绝命剑法 = FatalSword,
    DoubleSlash = 152,
    双刀术 = DoubleSlash,
    Haste = 153,
    体迅风 = Haste,
    FlashDash = 154,
    拔刀术 = FlashDash,
    LightBody = 155,
    风身术 = LightBody,
    HeavenlySword = 156,
    迁移剑 = HeavenlySword,
    FireBurst = 157,
    烈风击 = FireBurst,
    Trap = 158,
    捕缚术 = Trap,
    PoisonSword = 159,
    猛毒剑气 = PoisonSword,
    MoonLight = 160,
    月影术 = MoonLight,
    MPEater = 161,
    吸气 = MPEater,
    SwiftFeet = 162,
    轻身步 = SwiftFeet,
    DarkBody = 163,
    烈火身 = DarkBody,
    Hemorrhage = 164,
    血风击 = Hemorrhage,
    CrescentSlash = 165,
    猫舌兰 = CrescentSlash,
    MoonMist = 166,
    月雾 = MoonMist,
    CatTongue = 167,

    //Archer
    Focus = 201,
    必中闪 = Focus,
    StraightShot = 202,
    天日闪 = StraightShot,
    DoubleShot = 203,
    无我闪 = DoubleShot,
    ExplosiveTrap = 204,
    爆阱 = ExplosiveTrap,
    DelayedExplosion = 205,
    爆闪 = DelayedExplosion,
    Meditation = 206,
    气功术 = Meditation,
    BackStep = 207,
    万斤闪 = BackStep,
    ElementalShot = 208,
    气流术 = ElementalShot,
    Concentration = 209,
    金刚术 = Concentration,
    Stonetrap = 210,
    风弹步 = Stonetrap,
    ElementalBarrier = 211,
    自然囚笼 = ElementalBarrier,
    SummonVampire = 212,
    吸血地精 = SummonVampire,
    VampireShot = 213,
    吸血地闪 = VampireShot,
    SummonToad = 214,
    痹魔阱 = SummonToad,
    PoisonShot = 215,
    毒魔闪 = PoisonShot,
    CrippleShot = 216,
    邪爆闪 = CrippleShot,
    SummonSnakes = 217,
    蛇柱阱 = SummonSnakes,
    NapalmShot = 218,
    血龙闪 = NapalmShot,
    OneWithNature = 219,
    天人合一 = OneWithNature,
    BindingShot = 220,
    束缚箭 = BindingShot,
    MentalState = 221,
    精神状态 = MentalState,

    //Custom
    Blink = 301,
    闪现 = Blink,
    Portal = 302,
    传送门 = Portal,
    BattleCry = 303,
    战吼 = BattleCry,
    FireBounce = 304,
    火焰弹射 = FireBounce,
    MeteorShower = 305,
    流星雨 = MeteorShower,

    //Map Events
    DigOutZombie = 400,
    爬出僵尸 = DigOutZombie,
    Rubble = 401,
    碎石 = Rubble,
    MapLightning = 402,
    地图雷击 = MapLightning,
    MapLava = 403,
    地图熔岩 = MapLava,
    MapQuake1 = 404,
    地图震动1 = MapQuake1,
    MapQuake2 = 405,
    地图震动2 = MapQuake2,
    DigOutArmadillo = 406,
    FlyingStatueIceTornado = 407, //259
    GeneralMeowMeowThunder = 408, //341
    TucsonGeneralRock = 409, //354
    StoneGolemQuake = 410, //362
    EarthGolemPile = 411, //363
    TreeQueenRoot = 412, //365
    TreeQueenMassRoots = 413, //365
    TreeQueenGroundRoots = 414, //365
    DarkOmaKingNuke = 415, //378
    HornedSorcererDustTornado = 416, //406
    Mon409BRockFall = 417,
    Mon409BRockSpike = 418,
    Mon409BShield = 419,
    YangDragonFlame = 420, //414
    YangDragonIcyBurst = 421, //414
    ShardGuardianIceBomb = 422, //476
    GroundFissure = 423, //498
    SkeletonBomb = 424, //508
    FlameExplosion = 425, //509
    ButcherFlyAxe = 426, //516
    RiklebitesBlast = 427, //518
    RiklebitesRollCall = 428, //518
    SwordFormation = 429, //550
    Mon564NWhirlwind = 430,
    Mon570BRupture = 431,
    Mon570BLightningCloud = 432,
    Mon571BFireBomb = 433,
    Mon572BFlame = 434,
    Mon572BDarkVortex = 435,
    Mon573BBigCobweb = 436,
    Mon580BPoisonousMist = 437,
    Mon580BDenseFog = 438,
    Mon580BRoot = 439,
    Mon603BWhirlPool = 440,
    Mon609NBomb = 441
}

public enum SpellEffect : byte
{
    None,
    FatalSword,
    Teleport,
    Healing,
    HealingRare,
    RedMoonEvil,
    TwinDrakeBlade,
    MagicShieldUp,
    MagicShieldDown,
    GreatFoxSpirit,
    Entrapment,
    EntrapmentRare,
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
    StormEscapeRare,
    TurtleKing,
    Behemoth,
    Stunned,
    IcePillar,
    KingGuard,
    KingGuard2,
    DeathCrawlerBreath,
    FlamingMutantWeb,
    FurbolgWarriorCritical,
    Tester,
    MoonMist,
    HealingcircleRare,
    HealingcircleRare1,
    BloodthirstySpike,
    GroundBurstIce,
    MirEmperor,
    Mon562NLightning,
    Mon563NPoisonCloud,
    Mon564NFlame,
    Mon572BLightning,
    Mon573BCobweb,
    Mon580BLightning,
    Mon580BSpikeTrap,
}


public enum BuffType : byte
{
    None = 0,

    //Magics
    时间之殇,
    隐身术,
    体迅风,
    轻身步,
    血龙剑法,
    幽灵盾,
    神圣战甲术,
    风身术,
    无极真气,
    护身气幕,
    剑气爆,
    诅咒术,
    月影术,
    烈火身,
    气流术,
    吸血地闪,
    毒魔闪,
    天务,
    精神状态,
    先天气功,
    深延术,
    血龙兽,
    金刚不坏,
    金刚不坏秘籍,
    魔法盾,
    金刚术,
    天上秘术,
    万效符,
    万效符秘籍,
    TemporalFlux = 时间之殇,
    Hiding = 隐身术,
    Haste = 体迅风,
    SwiftFeet = 轻身步,
    Fury = 血龙剑法,
    SoulShield = 幽灵盾,
    BlessedArmour = 神圣战甲术,
    LightBody = 风身术,
    UltimateEnhancer = 无极真气,
    ProtectionField = 护身气幕,
    Rage = 剑气爆,
    Curse = 诅咒术,
    MoonLight = 月影术,
    DarkBody = 烈火身,
    Concentration = 气流术,
    VampireShot = 吸血地闪,
    PoisonShot = 毒魔闪,
    CounterAttack = 天务,
    MentalState = 精神状态,
    EnergyShield = 先天气功,
    MagicBooster = 深延术,
    PetEnhancer = 血龙兽,
    ImmortalSkin = 金刚不坏,
    MagicShield = 魔法盾,
    ElementalBarrier = 金刚术,

    //Monster
    HornedArcherBuff = 50,
    ColdArcherBuff,
    HornedColdArcherBuff,
    GeneralMeowMeowShield,
    惩戒真言,
    御体之力,
    HornedWarriorShield,
    Mon409BShieldBuff,
    失明状态,
    ChieftainSwordBuff,
    寒冰护甲,
    ReaperPriestBuff,
    至尊威严,
    伤口加深,
    死亡印记,
    RiklebitesShield,
    麻痹状态,
    绝对封锁,
    Mon564NSealing,
    烈火焚烧,
    防御诅咒,
    Mon579BShield,
    Mon580BShield,
    万效符爆杀,

    //Special
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

    //Stats
    攻击力提升 = 200,
    魔法力提升,
    道术力提升,
    攻击速度提升,
    生命值提升,
    法力值提升,
    防御提升,
    魔法防御提升,
    奇异药水,
    包容万斤,
    龍之祝福,
    准确命中提升,
    敏捷躲避提升,
    安息之气,
    远古气息,
    华丽雨光,
    龙之特效,
    龙的特效,
    组队加成,
    强化队伍,
    天灵水,
    玉清水,
    甜筒HP,
    甜筒MP,
    内尔族的灵药,
    摩鲁的赤色药剂,
    摩鲁的青色药剂,
    摩鲁的黄色药剂,
    古代宗师祝福,
    黄金宗师祝福,
    Impact = 攻击力提升,
    Magic = 魔法力提升,
    Taoist = 道术力提升,
    Storm = 攻击速度提升,
    HealthAid = 生命值提升,
    ManaAid = 法力值提升,
    Defence = 防御提升,
    MagicDefence = 魔法防御提升,
    WonderDrug = 奇异药水,
    Knapsack = 包容万斤,
}

[Flags]
public enum BuffProperty : byte
{
    None = 0,
    RemoveOnDeath = 1,
    RemoveOnExit = 2,
    Debuff = 4,
    PauseInSafeZone = 8
}

public enum BuffStackType : byte
{
    None = 0,
    ResetDuration = 1,
    Reset = ResetDuration,
    StackDuration = 2,
    Duration = StackDuration,
    StackStat = 3,
    Stat = StackStat,
    StackStatAndDuration = 4,
    Infinite = 5,
    ResetStat = 6,
    ResetStatAndDuration = 7,
    AttrStackStat = 8,
    AttrStackStatAndDuration = 9
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
    申请启动 = 0,
    Request = 申请启动,
    Auto = 1,
    强制启动 = 2,
    Forced = 强制启动,
}

public enum ConquestGame : byte
{
    占领皇宫 = 0,
    CapturePalace = 占领皇宫,
    争夺国王 = 1,
    KingOfHill = 争夺国王,
    随机模式 = 2,
    Random = 随机模式,
    经典模式 = 3,
    Classic = 经典模式,
    征服模式 = 4,
    ControlPoints = 征服模式
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

public enum FgType : byte
{
    ScreenShot = 0,
    Process = 1,
}
