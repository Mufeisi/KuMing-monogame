using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Server.MirDatabase;
using Server.MirEnvir;
using Server.MirObjects;

namespace Server.Persistence.Sql
{
    /// <summary>
    /// SQL 持久化入口（SQLite/MySQL）。
    /// 当前阶段：仅用于把调用链切换到“持久化层”，具体各域表结构与加载/保存闭环将按升级计划逐步落地。
    /// </summary>
    public sealed partial class SqlServerPersistence : IGamePersistence, IIdentityStore, ICharacterStore, IWorldStore
    {
        private const string ServerMetaKeyAccountsRelationsEpochUtcMs = "accounts_relations_epoch_utc_ms";

        private const string NextIdNextAccountId = "next_account_id";
        private const string NextIdNextCharacterId = "next_character_id";
        private const string NextIdNextUserItemId = "next_user_item_id";
        private const string NextIdNextHeroId = "next_hero_id";
        private const string NextIdNextGuildId = "next_guild_id";
        private const string NextIdNextAuctionId = "next_auction_id";
        private const string NextIdNextMailId = "next_mail_id";

        private static readonly string[] AccountsNextIdKeys =
        [
            NextIdNextAccountId,
            NextIdNextCharacterId,
            NextIdNextUserItemId,
            NextIdNextHeroId,
            NextIdNextGuildId,
            NextIdNextAuctionId,
            NextIdNextMailId,
        ];

        private sealed class NextIdRow
        {
            public string Name { get; set; }

            public long NextValue { get; set; }
        }

        private sealed class ServerMetaValueRow
        {
            public string MetaValue { get; set; }
        }

        private sealed class CharacterBackupRow
        {
            public long CharacterId { get; set; }
            public string CanonicalJson { get; set; }
            public string Sha256 { get; set; }
        }

        private sealed class ConquestRuntimeRow
        {
            public long ConquestId { get; set; }
            public long OwnerGuildId { get; set; }
            public long AttackerGuildId { get; set; }
            public long Treasury { get; set; }
            public int TaxRate { get; set; }
        }

        private sealed class ConquestFacilityRow
        {
            public long ConquestId { get; set; }
            public string FacilityKind { get; set; }
            public int FacilityIndex { get; set; }
            public long CurrentHp { get; set; }
            public long MaxHp { get; set; }
        }

        private sealed class AccountRow
        {
            public long AccountId { get; set; }
            public string AccountName { get; set; }
            public string PasswordHash { get; set; }
            public byte[] PasswordSalt { get; set; }
            public int RequirePasswordChange { get; set; }
            public string UserName { get; set; }
            public long BirthUtcMs { get; set; }
            public string SecretQuestion { get; set; }
            public string SecretAnswer { get; set; }
            public string EmailAddress { get; set; }
            public string CreationIp { get; set; }
            public long CreationUtcMs { get; set; }
            public int Banned { get; set; }
            public string BanReason { get; set; }
            public long ExpiryUtcMs { get; set; }
            public string LastIp { get; set; }
            public long LastUtcMs { get; set; }
            public int AdminAccount { get; set; }
            public int WrongPasswordCount { get; set; }
            public long Gold { get; set; }
            public long Credit { get; set; }
        }

        private sealed class CharacterRow
        {
            public long CharacterId { get; set; }
            public long AccountId { get; set; }
            public int CharacterKind { get; set; }
            public string CharacterName { get; set; }
            public int Level { get; set; }
            public int Class { get; set; }
            public int Gender { get; set; }
            public int Hair { get; set; }
            public long GuildId { get; set; }
            public string CreationIp { get; set; }
            public long CreationUtcMs { get; set; }
            public int Banned { get; set; }
            public string BanReason { get; set; }
            public long ExpiryUtcMs { get; set; }
            public int ChatBanned { get; set; }
            public long ChatBanExpiryUtcMs { get; set; }
            public string LastIp { get; set; }
            public long LastLogoutUtcMs { get; set; }
            public long LastLoginUtcMs { get; set; }
            public int Deleted { get; set; }
            public long DeleteUtcMs { get; set; }
            public long MarriedCharacterId { get; set; }
            public long MarriedUtcMs { get; set; }
            public long MentorCharacterId { get; set; }
            public long MentorUtcMs { get; set; }
            public int IsMentor { get; set; }
            public long MentorExp { get; set; }
            public int CurrentMapId { get; set; }
            public int CurrentX { get; set; }
            public int CurrentY { get; set; }
            public int Direction { get; set; }
            public int BindMapId { get; set; }
            public int BindX { get; set; }
            public int BindY { get; set; }
            public int Hp { get; set; }
            public int Mp { get; set; }
            public long Experience { get; set; }
            public int AttackMode { get; set; }
            public int PetMode { get; set; }
            public int AllowGroup { get; set; }
            public int AllowTrade { get; set; }
            public int AllowObserve { get; set; }
            public int PkPoints { get; set; }
            public int NewDay { get; set; }
            public int Thrusting { get; set; }
            public int HalfMoon { get; set; }
            public int CrossHalfMoon { get; set; }
            public int DoubleSlash { get; set; }
            public int MentalState { get; set; }
            public int PearlCount { get; set; }
            public long CollectTimeRemainingMs { get; set; }
            public int MaximumHeroCount { get; set; }
            public int CurrentHeroIndex { get; set; }
            public int HeroSpawned { get; set; }
            public int HeroBehaviour { get; set; }
        }

        private sealed class CharacterHeroSlotRow
        {
            public long CharacterId { get; set; }
            public int SlotIndex { get; set; }
            public long HeroCharacterId { get; set; }
        }

        private sealed class CharacterBuffRow
        {
            public long CharacterId { get; set; }
            public int ListIndex { get; set; }
            public int BuffType { get; set; }
            public long ObjectId { get; set; }
            public long ExpireTime { get; set; }
            public long LastTime { get; set; }
            public long NextTime { get; set; }
            public int FlagForRemoval { get; set; }
            public int Paused { get; set; }
        }

        private sealed class CharacterBuffStatRow
        {
            public long CharacterId { get; set; }
            public int ListIndex { get; set; }
            public int StatId { get; set; }
            public long StatValue { get; set; }
        }

        private sealed class CharacterBuffValueRow
        {
            public long CharacterId { get; set; }
            public int ListIndex { get; set; }
            public int ValueIndex { get; set; }
            public string ValueType { get; set; }
            public long? IntegerValue { get; set; }
            public double? RealValue { get; set; }
            public string TextValue { get; set; }
        }

        private sealed class CharacterBuffDataRow
        {
            public long CharacterId { get; set; }
            public int ListIndex { get; set; }
            public string DataKey { get; set; }
            public string DataType { get; set; }
            public long? IntegerValue { get; set; }
            public double? RealValue { get; set; }
            public string TextValue { get; set; }
        }

        private sealed class ItemRow
        {
            public long ItemId { get; set; }
            public int ItemIndex { get; set; }
            public int CurrentDura { get; set; }
            public int MaxDura { get; set; }
            public int StackCount { get; set; }
            public int GemCount { get; set; }
            public int SoulBoundId { get; set; }
            public int Identified { get; set; }
            public int Cursed { get; set; }
            public int SlotCount { get; set; }
            public int AwakeType { get; set; }
            public int RefinedValue { get; set; }
            public int RefineAdded { get; set; }
            public int RefineSuccessChance { get; set; }
            public int WeddingRing { get; set; }
            public long ExpireUtcMs { get; set; }
            public string RentalOwnerName { get; set; }
            public int RentalBindingFlags { get; set; }
            public long RentalExpiryUtcMs { get; set; }
            public int RentalLocked { get; set; }
            public int IsShopItem { get; set; }
            public long SealedExpiryUtcMs { get; set; }
            public long SealedNextSealUtcMs { get; set; }
            public int GmMade { get; set; }
        }

        private sealed class ItemAddedStatRow
        {
            public long ItemId { get; set; }
            public int StatId { get; set; }
            public int StatValue { get; set; }
        }

        private sealed class ItemAwakeLevelRow
        {
            public long ItemId { get; set; }
            public int LevelIndex { get; set; }
            public int LevelValue { get; set; }
        }

        private sealed class ItemSlotLinkRow
        {
            public long ParentItemId { get; set; }
            public int SlotIndex { get; set; }
            public long ChildItemId { get; set; }
        }

        private sealed class ItemLocationRow
        {
            public long ItemId { get; set; }
            public string LocationKind { get; set; }
            public long OwnerId { get; set; }
            public int ContainerKind { get; set; }
            public int SlotIndex { get; set; }
            public long? ParentItemId { get; set; }
        }

        private enum CharacterContainerKind
        {
            Inventory = 1,
            Equipment = 2,
            QuestInventory = 3,
            CurrentRefine = 4,
        }

        private enum CharacterEntityKind
        {
            Player = 0,
            Hero = 1,
        }

        private sealed class AccountStorageRow
        {
            public long AccountId { get; set; }
            public int SlotCount { get; set; }
            public int HasExpandedStorage { get; set; }
            public long ExpandedStorageExpiryUtcMs { get; set; }
        }

        private sealed class AccountStorageSlotRow
        {
            public long AccountId { get; set; }
            public int SlotIndex { get; set; }
            public long ItemId { get; set; }
        }

        private sealed class CharacterContainerRow
        {
            public long CharacterId { get; set; }
            public int ContainerKind { get; set; }
            public int SlotCount { get; set; }
        }

        private sealed class CharacterContainerSlotRow
        {
            public long CharacterId { get; set; }
            public int ContainerKind { get; set; }
            public int SlotIndex { get; set; }
            public long ItemId { get; set; }
        }

        private sealed class AuctionRow
        {
            public long AuctionId { get; set; }
            public long ItemId { get; set; }
            public long ConsignmentUtcMs { get; set; }
            public long Price { get; set; }
            public long CurrentBid { get; set; }
            public long SellerCharacterId { get; set; }
            public long CurrentBuyerCharacterId { get; set; }
            public int Expired { get; set; }
            public int Sold { get; set; }
            public int ItemType { get; set; }
        }

        private sealed class MailRow
        {
            public long MailId { get; set; }
            public string SenderName { get; set; }
            public long RecipientCharacterId { get; set; }
            public string Message { get; set; }
            public long Gold { get; set; }
            public long DateSentUtcMs { get; set; }
            public long DateOpenedUtcMs { get; set; }
            public int Locked { get; set; }
            public int Collected { get; set; }
            public int CanReply { get; set; }
        }

        private sealed class MailItemRow
        {
            public long MailId { get; set; }
            public int SlotIndex { get; set; }
            public long ItemId { get; set; }
        }

        private sealed class GameshopLogRow
        {
            public int ItemIndex { get; set; }
            public int Count { get; set; }
        }

        private sealed class RespawnSaveRow
        {
            public int RespawnIndex { get; set; }
            public long NextSpawnTick { get; set; }
            public int Spawned { get; set; }
        }

        private sealed class CharacterMagicRow
        {
            public long CharacterId { get; set; }
            public int Spell { get; set; }
            public int MagicLevel { get; set; }
            public int MagicKey { get; set; }
            public int Experience { get; set; }
            public int IsTempSpell { get; set; }
            public long CastTime { get; set; }
        }

        private sealed class CharacterCompletedQuestRow
        {
            public long CharacterId { get; set; }
            public long QuestId { get; set; }
        }

        private sealed class CharacterFlagRow
        {
            public long CharacterId { get; set; }
            public int FlagIndex { get; set; }
            public int FlagValue { get; set; }
        }

        private sealed class CharacterGameshopPurchaseRow
        {
            public long CharacterId { get; set; }
            public int ItemIndex { get; set; }
            public int PurchaseCount { get; set; }
        }

        private sealed class CurrentQuestRow
        {
            public long CharacterId { get; set; }
            public int SlotIndex { get; set; }
            public long QuestId { get; set; }
            public long StartUtcMs { get; set; }
            public long EndUtcMs { get; set; }
        }

        private sealed class CurrentQuestKillTaskRow
        {
            public long CharacterId { get; set; }
            public long QuestId { get; set; }
            public int MonsterId { get; set; }
            public int TaskCount { get; set; }
        }

        private sealed class CurrentQuestItemTaskRow
        {
            public long CharacterId { get; set; }
            public long QuestId { get; set; }
            public int ItemId { get; set; }
            public int TaskCount { get; set; }
        }

        private sealed class CurrentQuestFlagTaskRow
        {
            public long CharacterId { get; set; }
            public long QuestId { get; set; }
            public int FlagNumber { get; set; }
            public int FlagState { get; set; }
        }

        private sealed class CharacterPetRow
        {
            public long CharacterId { get; set; }
            public int ListIndex { get; set; }
            public int MonsterId { get; set; }
            public int Hp { get; set; }
            public long Experience { get; set; }
            public int PetLevel { get; set; }
            public int MaxPetLevel { get; set; }
        }

        private sealed class CharacterFriendRow
        {
            public long CharacterId { get; set; }
            public int ListIndex { get; set; }
            public long FriendCharacterId { get; set; }
            public int Blocked { get; set; }
            public string Memo { get; set; }
        }

        private sealed class CharacterRentedItemRow
        {
            public long CharacterId { get; set; }
            public int ListIndex { get; set; }
            public long ItemId { get; set; }
            public string ItemName { get; set; }
            public string RentingPlayerName { get; set; }
            public long ItemReturnUtcMs { get; set; }
        }

        private sealed class CharacterIntelligentCreatureRow
        {
            public long CharacterId { get; set; }
            public int SlotIndex { get; set; }
            public int PetType { get; set; }
            public string CustomName { get; set; }
            public int Fullness { get; set; }
            public long ExpireUtcMs { get; set; }
            public long BlackstoneTime { get; set; }
            public int PickupMode { get; set; }
            public int FilterPickupAll { get; set; }
            public int FilterPickupGold { get; set; }
            public int FilterPickupWeapons { get; set; }
            public int FilterPickupArmours { get; set; }
            public int FilterPickupHelmets { get; set; }
            public int FilterPickupBoots { get; set; }
            public int FilterPickupBelts { get; set; }
            public int FilterPickupAccessories { get; set; }
            public int FilterPickupOthers { get; set; }
            public int FilterPickupGrade { get; set; }
            public long MaintainFoodTime { get; set; }
        }

        private sealed class HeroDetailRow
        {
            public long CharacterId { get; set; }
            public int AutoPot { get; set; }
            public int Grade { get; set; }
            public int HpItemIndex { get; set; }
            public int MpItemIndex { get; set; }
            public int AutoHpPercent { get; set; }
            public int AutoMpPercent { get; set; }
            public int SealCount { get; set; }
        }

        private sealed class AccountsSnapshot
        {
            public long SaveEpochUtcMs { get; }

            public IReadOnlyDictionary<string, long> NextIds { get; }

            public IReadOnlyList<AccountRow> Accounts { get; }

            public IReadOnlyList<CharacterRow> Characters { get; }

            public IReadOnlyList<ItemRow> Items { get; }

            public IReadOnlyList<ItemAddedStatRow> ItemAddedStats { get; }

            public IReadOnlyList<ItemAwakeLevelRow> ItemAwakeLevels { get; }

            public IReadOnlyList<ItemSlotLinkRow> ItemSlotLinks { get; }
            public IReadOnlyList<ItemLocationRow> ItemLocations { get; }

            public IReadOnlyList<AccountStorageRow> AccountStorage { get; }

            public IReadOnlyList<AccountStorageSlotRow> AccountStorageSlots { get; }

            public IReadOnlyList<CharacterContainerRow> CharacterContainers { get; }

            public IReadOnlyList<CharacterContainerSlotRow> CharacterContainerSlots { get; }

            public IReadOnlyList<AuctionRow> Auctions { get; }

            public IReadOnlyList<MailRow> Mails { get; }

            public IReadOnlyList<MailItemRow> MailItems { get; }

            public IReadOnlyList<GameshopLogRow> GameshopLog { get; }

            public IReadOnlyList<RespawnSaveRow> RespawnSaves { get; }

            public IReadOnlyList<CharacterMagicRow> CharacterMagics { get; }

            public IReadOnlyList<CharacterCompletedQuestRow> CharacterCompletedQuests { get; }

            public IReadOnlyList<CharacterFlagRow> CharacterFlags { get; }

            public IReadOnlyList<CharacterGameshopPurchaseRow> CharacterGameshopPurchases { get; }

            public IReadOnlyList<CurrentQuestRow> CurrentQuests { get; }

            public IReadOnlyList<CurrentQuestKillTaskRow> CurrentQuestKillTasks { get; }

            public IReadOnlyList<CurrentQuestItemTaskRow> CurrentQuestItemTasks { get; }

            public IReadOnlyList<CurrentQuestFlagTaskRow> CurrentQuestFlagTasks { get; }

            public IReadOnlyList<CharacterPetRow> CharacterPets { get; }

            public IReadOnlyList<CharacterFriendRow> CharacterFriends { get; }

            public IReadOnlyList<CharacterRentedItemRow> CharacterRentedItems { get; }

            public IReadOnlyList<CharacterIntelligentCreatureRow> CharacterIntelligentCreatures { get; }

            public IReadOnlyList<HeroDetailRow> HeroDetails { get; }

            public IReadOnlyList<CharacterHeroSlotRow> CharacterHeroSlots { get; }

            public IReadOnlyList<CharacterBuffRow> CharacterBuffs { get; }
            public IReadOnlyList<CharacterBuffStatRow> CharacterBuffStats { get; }
            public IReadOnlyList<CharacterBuffValueRow> CharacterBuffValues { get; }
            public IReadOnlyList<CharacterBuffDataRow> CharacterBuffData { get; }
            public IReadOnlyList<ConquestRuntimeRow> ConquestRuntime { get; }
            public IReadOnlyList<ConquestFacilityRow> ConquestFacilities { get; }

            public AccountsSnapshot(
                long saveEpochUtcMs,
                IReadOnlyDictionary<string, long> nextIds,
                IReadOnlyList<AccountRow> accounts,
                IReadOnlyList<CharacterRow> characters,
                IReadOnlyList<ItemRow> items,
                IReadOnlyList<ItemAddedStatRow> itemAddedStats,
                IReadOnlyList<ItemAwakeLevelRow> itemAwakeLevels,
                IReadOnlyList<ItemSlotLinkRow> itemSlotLinks,
                IReadOnlyList<ItemLocationRow> itemLocations,
                IReadOnlyList<AccountStorageRow> accountStorage,
                IReadOnlyList<AccountStorageSlotRow> accountStorageSlots,
                IReadOnlyList<CharacterContainerRow> characterContainers,
                IReadOnlyList<CharacterContainerSlotRow> characterContainerSlots,
                IReadOnlyList<AuctionRow> auctions,
                IReadOnlyList<MailRow> mails,
                IReadOnlyList<MailItemRow> mailItems,
                IReadOnlyList<GameshopLogRow> gameshopLog,
                IReadOnlyList<RespawnSaveRow> respawnSaves,
                IReadOnlyList<CharacterMagicRow> characterMagics,
                IReadOnlyList<CharacterCompletedQuestRow> characterCompletedQuests,
                IReadOnlyList<CharacterFlagRow> characterFlags,
                IReadOnlyList<CharacterGameshopPurchaseRow> characterGameshopPurchases,
                IReadOnlyList<CurrentQuestRow> currentQuests,
                IReadOnlyList<CurrentQuestKillTaskRow> currentQuestKillTasks,
                IReadOnlyList<CurrentQuestItemTaskRow> currentQuestItemTasks,
                IReadOnlyList<CurrentQuestFlagTaskRow> currentQuestFlagTasks,
                IReadOnlyList<CharacterPetRow> characterPets,
                IReadOnlyList<CharacterFriendRow> characterFriends,
                IReadOnlyList<CharacterRentedItemRow> characterRentedItems,
                IReadOnlyList<CharacterIntelligentCreatureRow> characterIntelligentCreatures,
                IReadOnlyList<HeroDetailRow> heroDetails,
                IReadOnlyList<CharacterHeroSlotRow> characterHeroSlots,
                IReadOnlyList<CharacterBuffRow> characterBuffs,
                IReadOnlyList<CharacterBuffStatRow> characterBuffStats,
                IReadOnlyList<CharacterBuffValueRow> characterBuffValues,
                IReadOnlyList<CharacterBuffDataRow> characterBuffData,
                IReadOnlyList<ConquestRuntimeRow> conquestRuntime,
                IReadOnlyList<ConquestFacilityRow> conquestFacilities)
            {
                SaveEpochUtcMs = saveEpochUtcMs <= 0 ? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() : saveEpochUtcMs;
                NextIds = nextIds ?? new Dictionary<string, long>(StringComparer.Ordinal);
                Accounts = accounts ?? Array.Empty<AccountRow>();
                Characters = characters ?? Array.Empty<CharacterRow>();
                Items = items ?? Array.Empty<ItemRow>();
                ItemAddedStats = itemAddedStats ?? Array.Empty<ItemAddedStatRow>();
                ItemAwakeLevels = itemAwakeLevels ?? Array.Empty<ItemAwakeLevelRow>();
                ItemSlotLinks = itemSlotLinks ?? Array.Empty<ItemSlotLinkRow>();
                ItemLocations = itemLocations ?? Array.Empty<ItemLocationRow>();
                AccountStorage = accountStorage ?? Array.Empty<AccountStorageRow>();
                AccountStorageSlots = accountStorageSlots ?? Array.Empty<AccountStorageSlotRow>();
                CharacterContainers = characterContainers ?? Array.Empty<CharacterContainerRow>();
                CharacterContainerSlots = characterContainerSlots ?? Array.Empty<CharacterContainerSlotRow>();
                Auctions = auctions ?? Array.Empty<AuctionRow>();
                Mails = mails ?? Array.Empty<MailRow>();
                MailItems = mailItems ?? Array.Empty<MailItemRow>();
                GameshopLog = gameshopLog ?? Array.Empty<GameshopLogRow>();
                RespawnSaves = respawnSaves ?? Array.Empty<RespawnSaveRow>();
                CharacterMagics = characterMagics ?? Array.Empty<CharacterMagicRow>();
                CharacterCompletedQuests = characterCompletedQuests ?? Array.Empty<CharacterCompletedQuestRow>();
                CharacterFlags = characterFlags ?? Array.Empty<CharacterFlagRow>();
                CharacterGameshopPurchases = characterGameshopPurchases ?? Array.Empty<CharacterGameshopPurchaseRow>();
                CurrentQuests = currentQuests ?? Array.Empty<CurrentQuestRow>();
                CurrentQuestKillTasks = currentQuestKillTasks ?? Array.Empty<CurrentQuestKillTaskRow>();
                CurrentQuestItemTasks = currentQuestItemTasks ?? Array.Empty<CurrentQuestItemTaskRow>();
                CurrentQuestFlagTasks = currentQuestFlagTasks ?? Array.Empty<CurrentQuestFlagTaskRow>();
                CharacterPets = characterPets ?? Array.Empty<CharacterPetRow>();
                CharacterFriends = characterFriends ?? Array.Empty<CharacterFriendRow>();
                CharacterRentedItems = characterRentedItems ?? Array.Empty<CharacterRentedItemRow>();
                CharacterIntelligentCreatures = characterIntelligentCreatures ?? Array.Empty<CharacterIntelligentCreatureRow>();
                HeroDetails = heroDetails ?? Array.Empty<HeroDetailRow>();
                CharacterHeroSlots = characterHeroSlots ?? Array.Empty<CharacterHeroSlotRow>();
                CharacterBuffs = characterBuffs ?? Array.Empty<CharacterBuffRow>();
                CharacterBuffStats = characterBuffStats ?? Array.Empty<CharacterBuffStatRow>();
                CharacterBuffValues = characterBuffValues ?? Array.Empty<CharacterBuffValueRow>();
                CharacterBuffData = characterBuffData ?? Array.Empty<CharacterBuffDataRow>();
                ConquestRuntime = conquestRuntime ?? Array.Empty<ConquestRuntimeRow>();
                ConquestFacilities = conquestFacilities ?? Array.Empty<ConquestFacilityRow>();
            }
        }

        private readonly DatabaseProviderKind _provider;
        private readonly IServerStatePort _statePort;
        private readonly SqlDatabaseOptions _identityOptions;
        private readonly SqlDatabaseOptions _databaseOptions;
        private readonly SqlDatabaseOptions _worldOptions;
        private readonly object _initGate = new object();
        private bool _initialized;
        private long _generation;

        public DatabaseProviderKind Provider => _provider;
        public PersistenceModuleState State { get; private set; } = PersistenceModuleState.Created;

        public SqlServerPersistence(DatabaseProviderKind provider, IServerStatePort statePort)
        {
            _provider = provider;
            _statePort = statePort ?? throw new ArgumentNullException(nameof(statePort));

            var layout = provider == DatabaseProviderKind.Sqlite
                ? SqliteDatabaseLayout.Resolve(Settings.SqliteDirectory)
                : null;

            _identityOptions = CreateOptions(
                DatabaseAuthority.Identity,
                layout?.IdentityPath,
                Settings.MySqlIdentityConnectionString);
            _databaseOptions = CreateOptions(
                DatabaseAuthority.Character,
                layout?.CharacterPath,
                Settings.MySqlCharacterConnectionString,
                layout?.IdentityPath);
            _worldOptions = CreateOptions(
                DatabaseAuthority.World,
                layout?.WorldPath,
                Settings.MySqlWorldConnectionString);
        }

        private static SqlDatabaseOptions CreateOptions(
            DatabaseAuthority authority,
            string sqlitePath,
            string mySqlConnectionString,
            string sqliteIdentityPath = null)
        {
            return new SqlDatabaseOptions
            {
                Authority = authority,
                SqlitePath = sqlitePath ?? string.Empty,
                SqliteIdentityPath = sqliteIdentityPath ?? string.Empty,
                MySqlConnectionString = mySqlConnectionString,
                MySqlPooling = Settings.MySqlPooling,
                MySqlMinPoolSize = Settings.MySqlMinPoolSize,
                MySqlMaxPoolSize = Settings.MySqlMaxPoolSize,
                MySqlConnectionTimeoutSeconds = Settings.MySqlConnectionTimeoutSeconds,
                MySqlKeepAliveSeconds = Settings.MySqlKeepAliveSeconds,
                MySqlConnectionIdleTimeoutSeconds = Settings.MySqlConnectionIdleTimeoutSeconds,
                MySqlConnectionLifeTimeSeconds = Settings.MySqlConnectionLifeTimeSeconds,
                CommandTimeoutSeconds = 30,
            };
        }

        private void EnsureInitialized()
        {
            if (_initialized) return;

            lock (_initGate)
            {
                if (_initialized) return;

                if (!Settings.AutoApplySchemaOnStartup)
                    throw new InvalidOperationException("三库布局要求启动时验证 Schema；AutoApplySchemaOnStartup 不能关闭。");

                var version = typeof(SqlServerPersistence).Assembly.GetName().Version?.ToString() ?? string.Empty;
                var commit = string.Empty;

                foreach (var entry in new[]
                {
                    (Authority: DatabaseAuthority.Identity, Options: _identityOptions),
                    (Authority: DatabaseAuthority.Character, Options: _databaseOptions),
                    (Authority: DatabaseAuthority.World, Options: _worldOptions),
                })
                {
                    using var session = SqlSession.Open(_provider, entry.Options, maxRetries: 3, baseRetryDelayMs: 200);
                    var migrator = new SchemaMigrator(
                        AuthoritySchemaMigrator.Create(entry.Authority),
                        commandTimeoutSeconds: entry.Options.CommandTimeoutSeconds);
                    migrator.ApplyPendingMigrations(session.Connection, session.Dialect, version, commit);
                    EnsureCompletedManifest(session, entry.Authority);
                }

                _initialized = true;
            }
        }

        private static void EnsureCompletedManifest(SqlSession session, DatabaseAuthority authority)
        {
            var rows = session.Query<int>(
                "SELECT completed FROM database_manifest WHERE authority=@authority",
                new { authority = authority.ToString().ToLowerInvariant() });

            if (rows.Count == 0)
            {
                session.RunInTransaction(s => AuthoritySchemaMigrator.MarkComplete(s, authority, "bootstrap", 0, string.Empty));
                return;
            }

            if (rows.Count != 1 || rows[0] != 1)
                throw new InvalidOperationException($"{authority} database_manifest 未完成，拒绝启动。");
        }

        private static string SchemaNotReadyMessage(DatabaseProviderKind provider, Exception ex)
        {
            return
                $"SQL 持久化表结构未就绪（Provider={provider}）。" +
                $"请在 `Setup.ini` 的 `[Database]` 段启用 `AutoApplySchemaOnStartup=True`（开发/测试推荐），" +
                $"或先运行后续将补齐的 `Tools/DbMigrator` 来建表/迁移。原始错误：{ex.GetType().Name}: {ex.Message}";
        }

        private static void UpsertServerMeta(SqlSession session, string key, string value, long updatedUtcMs)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));
            if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("key 不能为空。", nameof(key));

            if (updatedUtcMs <= 0)
                updatedUtcMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            var sql = session.Dialect.BuildUpsert(
                tableName: "server_meta",
                insertColumns: ["meta_key", "meta_value", "updated_utc_ms"],
                keyColumns: ["meta_key"],
                updateColumns: ["meta_value", "updated_utc_ms"]);

            session.Execute(
                sql,
                new
                {
                    meta_key = key.Trim(),
                    meta_value = value ?? string.Empty,
                    updated_utc_ms = updatedUtcMs,
                });
        }

        private static long TryLoadServerMetaInt64(SqlSession session, string key)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));
            if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("key 不能为空。", nameof(key));

            var rows = session.Query<ServerMetaValueRow>(
                "SELECT meta_value AS MetaValue FROM server_meta WHERE meta_key=@Key",
                new { Key = key.Trim() });

            if (rows.Count == 0 || rows[0] == null)
                return 0;

            var text = (rows[0].MetaValue ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(text))
                return 0;

            return long.TryParse(text, out var value) ? value : 0;
        }

        private static long ToDbInt64(ulong value, string name)
        {
            if (value > long.MaxValue)
                throw new NotSupportedException($"NextIds 超出 BIGINT 范围：{name}={value}（max={long.MaxValue}）。");

            return (long)value;
        }

        private static int ToNonNegativeInt32(long value, string name)
        {
            if (value < 0 || value > int.MaxValue)
                throw new NotSupportedException($"NextIds 值无效：{name}={value}（允许范围 0..{int.MaxValue}）。");

            return (int)value;
        }

        private static ulong ToNonNegativeUInt64(long value, string name)
        {
            if (value < 0)
                throw new NotSupportedException($"NextIds 值无效：{name}={value}（不允许为负）。");

            return (ulong)value;
        }

        private static IReadOnlyDictionary<string, long> CaptureAccountsNextIds(Envir envir)
        {
            if (envir == null) throw new ArgumentNullException(nameof(envir));

            return new Dictionary<string, long>(StringComparer.Ordinal)
            {
                [NextIdNextAccountId] = envir.NextAccountID,
                [NextIdNextCharacterId] = envir.NextCharacterID,
                [NextIdNextUserItemId] = ToDbInt64(envir.NextUserItemID, NextIdNextUserItemId),
                [NextIdNextHeroId] = envir.NextHeroID,
                [NextIdNextGuildId] = envir.NextGuildID,
                [NextIdNextAuctionId] = ToDbInt64(envir.NextAuctionID, NextIdNextAuctionId),
                [NextIdNextMailId] = ToDbInt64(envir.NextMailID, NextIdNextMailId),
            };
        }

        private static void ApplyAccountsNextIds(Envir envir, IReadOnlyDictionary<string, long> nextIds)
        {
            if (envir == null) throw new ArgumentNullException(nameof(envir));
            if (nextIds == null || nextIds.Count == 0) return;

            if (nextIds.TryGetValue(NextIdNextAccountId, out var nextAccountId))
                envir.NextAccountID = Math.Max(envir.NextAccountID, ToNonNegativeInt32(nextAccountId, NextIdNextAccountId));

            if (nextIds.TryGetValue(NextIdNextCharacterId, out var nextCharacterId))
                envir.NextCharacterID = Math.Max(envir.NextCharacterID, ToNonNegativeInt32(nextCharacterId, NextIdNextCharacterId));

            if (nextIds.TryGetValue(NextIdNextHeroId, out var nextHeroId))
                envir.NextHeroID = Math.Max(envir.NextHeroID, ToNonNegativeInt32(nextHeroId, NextIdNextHeroId));

            if (nextIds.TryGetValue(NextIdNextGuildId, out var nextGuildId))
                envir.NextGuildID = Math.Max(envir.NextGuildID, ToNonNegativeInt32(nextGuildId, NextIdNextGuildId));

            if (nextIds.TryGetValue(NextIdNextUserItemId, out var nextUserItemId))
                envir.NextUserItemID = Math.Max(envir.NextUserItemID, ToNonNegativeUInt64(nextUserItemId, NextIdNextUserItemId));

            if (nextIds.TryGetValue(NextIdNextAuctionId, out var nextAuctionId))
                envir.NextAuctionID = Math.Max(envir.NextAuctionID, ToNonNegativeUInt64(nextAuctionId, NextIdNextAuctionId));

            if (nextIds.TryGetValue(NextIdNextMailId, out var nextMailId))
                envir.NextMailID = Math.Max(envir.NextMailID, ToNonNegativeUInt64(nextMailId, NextIdNextMailId));
        }

        private static IReadOnlyDictionary<string, long> LoadNextIds(SqlSession session, IReadOnlyList<string> keys)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));
            if (keys == null || keys.Count == 0) return new Dictionary<string, long>(StringComparer.Ordinal);

            var rows = session.Query<NextIdRow>(
                "SELECT name AS Name, next_value AS NextValue FROM next_ids WHERE name IN @Names",
                new { Names = keys });

            if (rows.Count == 0) return new Dictionary<string, long>(StringComparer.Ordinal);

            var result = new Dictionary<string, long>(StringComparer.Ordinal);
            for (var i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                if (row == null) continue;
                if (string.IsNullOrWhiteSpace(row.Name)) continue;

                result[row.Name.Trim()] = row.NextValue;
            }

            return result;
        }

        private static void UpsertNextIds(SqlSession session, IReadOnlyDictionary<string, long> values)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));
            if (values == null || values.Count == 0) return;

            var sql = session.Dialect.BuildUpsert(
                tableName: "next_ids",
                insertColumns: ["name", "next_value", "updated_utc_ms"],
                keyColumns: ["name"],
                updateColumns: ["next_value", "updated_utc_ms"]);

            var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            foreach (var pair in values)
            {
                if (string.IsNullOrWhiteSpace(pair.Key)) continue;

                session.Execute(
                    sql,
                    new
                    {
                        name = pair.Key.Trim(),
                        next_value = pair.Value,
                        updated_utc_ms = nowMs,
                    });
            }
        }

        private static long ToUtcMs(DateTime value)
        {
            if (value == DateTime.MinValue) return 0;
            return new DateTimeOffset(value).ToUnixTimeMilliseconds();
        }

        private static DateTime FromUtcMsToLocal(long utcMs)
        {
            if (utcMs <= 0) return DateTime.MinValue;

            var local = DateTimeOffset.FromUnixTimeMilliseconds(utcMs).ToLocalTime().DateTime;
            return DateTime.SpecifyKind(local, DateTimeKind.Local);
        }

        private static IReadOnlyList<AccountRow> CaptureAccounts(Envir envir)
        {
            if (envir == null) throw new ArgumentNullException(nameof(envir));

            lock (Envir.AccountLock)
            {
                var result = new List<AccountRow>(envir.AccountList.Count);
                for (var i = 0; i < envir.AccountList.Count; i++)
                {
                    var account = envir.AccountList[i];
                    if (account == null) continue;

                    result.Add(new AccountRow
                    {
                        AccountId = account.Index,
                        AccountName = account.AccountID ?? string.Empty,
                        PasswordHash = account.Password ?? string.Empty,
                        PasswordSalt = account.Salt ?? Array.Empty<byte>(),
                        RequirePasswordChange = account.RequirePasswordChange ? 1 : 0,
                        UserName = account.UserName ?? string.Empty,
                        BirthUtcMs = ToUtcMs(account.BirthDate),
                        SecretQuestion = account.SecretQuestion ?? string.Empty,
                        SecretAnswer = account.SecretAnswer ?? string.Empty,
                        EmailAddress = account.EMailAddress ?? string.Empty,
                        CreationIp = account.CreationIP ?? string.Empty,
                        CreationUtcMs = ToUtcMs(account.CreationDate),
                        Banned = account.Banned ? 1 : 0,
                        BanReason = account.BanReason ?? string.Empty,
                        ExpiryUtcMs = ToUtcMs(account.ExpiryDate),
                        LastIp = account.LastIP ?? string.Empty,
                        LastUtcMs = ToUtcMs(account.LastDate),
                        AdminAccount = account.AdminAccount ? 1 : 0,
                        WrongPasswordCount = account.WrongPasswordCount,
                        Gold = account.Gold,
                        Credit = account.Credit,
                    });
                }

                return result;
            }
        }

        private static void UpsertAccounts(SqlSession session, IReadOnlyList<AccountRow> accounts)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));
            if (accounts == null || accounts.Count == 0) return;

            var sql = session.Dialect.BuildUpsert(
                tableName: "accounts",
                insertColumns:
                [
                    "account_id",
                    "account_name",
                    "password_hash",
                    "password_salt",
                    "require_password_change",
                    "user_name",
                    "birth_utc_ms",
                    "secret_question",
                    "secret_answer",
                    "email_address",
                    "creation_ip",
                    "creation_utc_ms",
                    "banned",
                    "ban_reason",
                    "expiry_utc_ms",
                    "last_ip",
                    "last_utc_ms",
                    "wrong_password_count",
                    "admin_account",
                    "updated_utc_ms",
                ],
                keyColumns: ["account_id"],
                updateColumns:
                [
                    "account_name",
                    "password_hash",
                    "password_salt",
                    "require_password_change",
                    "user_name",
                    "birth_utc_ms",
                    "secret_question",
                    "secret_answer",
                    "email_address",
                    "creation_ip",
                    "creation_utc_ms",
                    "banned",
                    "ban_reason",
                    "expiry_utc_ms",
                    "last_ip",
                    "last_utc_ms",
                    "wrong_password_count",
                    "admin_account",
                    "updated_utc_ms",
                ]);

            var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var batchSize = Settings.SaveBatchSize <= 0 ? 2000 : Settings.SaveBatchSize;

            for (var offset = 0; offset < accounts.Count; offset += batchSize)
            {
                var take = Math.Min(batchSize, accounts.Count - offset);
                var batch = new List<object>(take);

                for (var i = 0; i < take; i++)
                {
                    var account = accounts[offset + i];
                    if (account == null) continue;

                    batch.Add(new
                    {
                        account_id = account.AccountId,
                        account_name = account.AccountName ?? string.Empty,
                        password_hash = account.PasswordHash ?? string.Empty,
                        password_salt = account.PasswordSalt ?? Array.Empty<byte>(),
                        require_password_change = account.RequirePasswordChange,
                        user_name = account.UserName ?? string.Empty,
                        birth_utc_ms = account.BirthUtcMs,
                        secret_question = account.SecretQuestion ?? string.Empty,
                        secret_answer = account.SecretAnswer ?? string.Empty,
                        email_address = account.EmailAddress ?? string.Empty,
                        creation_ip = account.CreationIp ?? string.Empty,
                        creation_utc_ms = account.CreationUtcMs,
                        banned = account.Banned,
                        ban_reason = account.BanReason ?? string.Empty,
                        expiry_utc_ms = account.ExpiryUtcMs,
                        last_ip = account.LastIp ?? string.Empty,
                        last_utc_ms = account.LastUtcMs,
                        wrong_password_count = account.WrongPasswordCount,
                        admin_account = account.AdminAccount,
                        updated_utc_ms = nowMs,
                    });
                }

                if (batch.Count > 0)
                    session.Execute(sql, batch);
            }

            session.Execute("DELETE FROM accounts WHERE updated_utc_ms <> @nowMs", new { nowMs });
        }

        private static void UpsertAccountWallets(SqlSession session, IReadOnlyList<AccountRow> accounts, long saveEpochUtcMs)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));
            accounts ??= Array.Empty<AccountRow>();
            var nowMs = saveEpochUtcMs > 0 ? saveEpochUtcMs : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var sql = session.Dialect.BuildUpsert(
                "account_wallets",
                ["account_id", "gold", "credit", "updated_utc_ms"],
                ["account_id"],
                ["gold", "credit", "updated_utc_ms"]);

            if (accounts.Count > 0)
            {
                session.Execute(sql, accounts.Select(account => new
                {
                    account_id = account.AccountId,
                    gold = account.Gold,
                    credit = account.Credit,
                    updated_utc_ms = nowMs,
                }).ToArray());
            }

            session.Execute("DELETE FROM account_wallets WHERE updated_utc_ms <> @nowMs", new { nowMs });
        }

        private static IReadOnlyList<ItemLocationRow> CaptureItemLocations(
            IReadOnlyList<ItemRow> items,
            IReadOnlyList<AccountStorageSlotRow> storage,
            IReadOnlyList<CharacterContainerSlotRow> containers,
            IReadOnlyList<MailItemRow> mailItems,
            IReadOnlyList<AuctionRow> auctions,
            IReadOnlyList<ItemSlotLinkRow> slotLinks,
            IReadOnlyList<GuildStorageSlotRow> guildStorage,
            IReadOnlyList<NpcBuybackRow> npcBuybacks,
            IReadOnlyList<NpcUsedGoodRow> npcUsedGoods)
        {
            var locations = new Dictionary<long, ItemLocationRow>();
            void Add(ItemLocationRow row)
            {
                if (!locations.TryAdd(row.ItemId, row))
                    throw new InvalidOperationException($"物品实例 {row.ItemId} 同时出现在 {locations[row.ItemId].LocationKind} 与 {row.LocationKind}。");
            }

            foreach (var row in storage ?? Array.Empty<AccountStorageSlotRow>())
                Add(new ItemLocationRow { ItemId = row.ItemId, LocationKind = "account_storage", OwnerId = row.AccountId, SlotIndex = row.SlotIndex });
            foreach (var row in containers ?? Array.Empty<CharacterContainerSlotRow>())
                Add(new ItemLocationRow { ItemId = row.ItemId, LocationKind = "character", OwnerId = row.CharacterId, ContainerKind = row.ContainerKind, SlotIndex = row.SlotIndex });
            foreach (var row in mailItems ?? Array.Empty<MailItemRow>())
                Add(new ItemLocationRow { ItemId = row.ItemId, LocationKind = "mail", OwnerId = row.MailId, SlotIndex = row.SlotIndex });
            foreach (var row in auctions ?? Array.Empty<AuctionRow>())
                Add(new ItemLocationRow { ItemId = row.ItemId, LocationKind = "auction", OwnerId = row.AuctionId });
            foreach (var row in slotLinks ?? Array.Empty<ItemSlotLinkRow>())
                Add(new ItemLocationRow { ItemId = row.ChildItemId, LocationKind = "socket", OwnerId = row.ParentItemId, SlotIndex = row.SlotIndex, ParentItemId = row.ParentItemId });
            foreach (var row in guildStorage ?? Array.Empty<GuildStorageSlotRow>())
                Add(new ItemLocationRow { ItemId = row.ItemId, LocationKind = "guild_storage", OwnerId = row.GuildId, SlotIndex = row.SlotIndex });
            foreach (var row in npcBuybacks ?? Array.Empty<NpcBuybackRow>())
                Add(new ItemLocationRow { ItemId = row.ItemId, LocationKind = "npc_buyback", OwnerId = row.CharacterId, ContainerKind = (int)row.NpcId });
            foreach (var row in npcUsedGoods ?? Array.Empty<NpcUsedGoodRow>())
                Add(new ItemLocationRow { ItemId = row.ItemId, LocationKind = "npc_used_goods", OwnerId = row.NpcId });
            foreach (var row in items ?? Array.Empty<ItemRow>())
            {
                if (!locations.ContainsKey(row.ItemId))
                    locations.Add(row.ItemId, new ItemLocationRow { ItemId = row.ItemId, LocationKind = "quarantine", OwnerId = 0, SlotIndex = -1 });
            }

            return locations.Values.OrderBy(row => row.ItemId).ToArray();
        }

        private static void UpsertItemLocations(SqlSession session, IReadOnlyList<ItemLocationRow> rows, long saveEpochUtcMs)
        {
            rows ??= Array.Empty<ItemLocationRow>();
            var nowMs = saveEpochUtcMs > 0 ? saveEpochUtcMs : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var sql = session.Dialect.BuildUpsert(
                "item_locations",
                ["item_id", "location_kind", "owner_id", "container_kind", "slot_index", "parent_item_id", "updated_utc_ms"],
                ["item_id"],
                ["location_kind", "owner_id", "container_kind", "slot_index", "parent_item_id", "updated_utc_ms"]);
            if (rows.Count > 0)
                session.Execute(sql, rows.Select(row => new
                {
                    item_id = row.ItemId,
                    location_kind = row.LocationKind,
                    owner_id = row.OwnerId,
                    container_kind = row.ContainerKind,
                    slot_index = row.SlotIndex,
                    parent_item_id = row.ParentItemId,
                    updated_utc_ms = nowMs,
                }).ToArray());
        }

        private static IReadOnlyList<AccountRow> LoadAccountRows(SqlSession session)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));

            return session.Query<AccountRow>(
                "SELECT " +
                "account_id AS AccountId, " +
                "account_name AS AccountName, " +
                "password_hash AS PasswordHash, " +
                "password_salt AS PasswordSalt, " +
                "require_password_change AS RequirePasswordChange, " +
                "user_name AS UserName, " +
                "birth_utc_ms AS BirthUtcMs, " +
                "secret_question AS SecretQuestion, " +
                "secret_answer AS SecretAnswer, " +
                "email_address AS EmailAddress, " +
                "creation_ip AS CreationIp, " +
                "creation_utc_ms AS CreationUtcMs, " +
                "banned AS Banned, " +
                "ban_reason AS BanReason, " +
                "expiry_utc_ms AS ExpiryUtcMs, " +
                "last_ip AS LastIp, " +
                "last_utc_ms AS LastUtcMs, " +
                "wrong_password_count AS WrongPasswordCount, " +
                "admin_account AS AdminAccount, " +
                "gold AS Gold, " +
                "credit AS Credit " +
                "FROM accounts " +
                "ORDER BY account_id");
        }

        private static void ApplyAccounts(Envir envir, IReadOnlyList<AccountRow> rows)
        {
            if (envir == null) throw new ArgumentNullException(nameof(envir));
            if (rows == null || rows.Count == 0) return;

            var byId = new Dictionary<long, AccountRow>(rows.Count);
            for (var i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                if (row == null) continue;
                byId[row.AccountId] = row;
            }

            lock (Envir.AccountLock)
            {
                for (var i = 0; i < envir.AccountList.Count; i++)
                {
                    var account = envir.AccountList[i];
                    if (account == null) continue;

                    if (!byId.TryGetValue(account.Index, out var row) || row == null)
                        continue;

                    account.AccountID = row.AccountName ?? account.AccountID ?? string.Empty;
                    account.SetPasswordHashAndSalt(row.PasswordHash ?? string.Empty, row.PasswordSalt ?? Array.Empty<byte>());
                    account.RequirePasswordChange = row.RequirePasswordChange != 0;
                    account.UserName = row.UserName ?? string.Empty;
                    account.BirthDate = FromUtcMsToLocal(row.BirthUtcMs);
                    account.SecretQuestion = row.SecretQuestion ?? string.Empty;
                    account.SecretAnswer = row.SecretAnswer ?? string.Empty;
                    account.EMailAddress = row.EmailAddress ?? string.Empty;
                    account.CreationIP = row.CreationIp ?? string.Empty;
                    account.CreationDate = FromUtcMsToLocal(row.CreationUtcMs);
                    account.Banned = row.Banned != 0;
                    account.BanReason = row.BanReason ?? string.Empty;
                    account.ExpiryDate = FromUtcMsToLocal(row.ExpiryUtcMs);
                    account.LastIP = row.LastIp ?? string.Empty;
                    account.LastDate = FromUtcMsToLocal(row.LastUtcMs);
                    account.AdminAccount = row.AdminAccount != 0;
                    account.WrongPasswordCount = row.WrongPasswordCount;
                    account.Gold = (uint)Math.Clamp(row.Gold, 0, uint.MaxValue);
                    account.Credit = (uint)Math.Clamp(row.Credit, 0, uint.MaxValue);
                }
            }
        }

        private static void ResetAccountLoadState(Envir envir)
        {
            if (envir == null) throw new ArgumentNullException(nameof(envir));

            for (var index = 0; index < envir.RankClass.Count(); index++)
            {
                if (envir.RankClass[index] != null)
                    envir.RankClass[index].Clear();
                else
                    envir.RankClass[index] = new List<RankCharacterInfo>();
            }

            envir.RankTop.Clear();
            envir.AccountList.Clear();
            envir.CharacterList.Clear();
            envir.HeroList.Clear();
        }

        private static bool TryBuildAccountsGraphFromRelations(SqlSession session, Envir envir)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));
            if (envir == null) throw new ArgumentNullException(nameof(envir));

            var accountRows = LoadAccountRows(session);
            var characterRows = LoadCharacterRows(session);

            if (accountRows.Count == 0 && characterRows.Count == 0)
                return false;

            lock (Envir.LoadLock)
            {
                ResetAccountLoadState(envir);

                var accountsById = new Dictionary<long, AccountInfo>();
                for (var index = 0; index < accountRows.Count; index++)
                {
                    var row = accountRows[index];
                    if (row == null) continue;

                    var account = new AccountInfo
                    {
                        Index = (int)Math.Clamp(row.AccountId, int.MinValue, int.MaxValue),
                    };

                    accountsById[row.AccountId] = account;
                    envir.AccountList.Add(account);
                }

                for (var index = 0; index < characterRows.Count; index++)
                {
                    var row = characterRows[index];
                    if (row == null) continue;

                    if (row.CharacterKind == (int)CharacterEntityKind.Hero)
                    {
                        var hero = new HeroInfo
                        {
                            Index = (int)Math.Clamp(row.CharacterId, int.MinValue, int.MaxValue),
                            Inventory = new UserItem[10],
                            Equipment = new UserItem[14],
                            Magics = new List<UserMagic>(),
                        };

                        envir.HeroList.Add(hero);
                        continue;
                    }

                    var character = new CharacterInfo
                    {
                        Index = (int)Math.Clamp(row.CharacterId, int.MinValue, int.MaxValue),
                        Heroes = new HeroInfo[Math.Max(1, row.MaximumHeroCount)],
                        Magics = new List<UserMagic>(),
                    };

                    if (accountsById.TryGetValue(row.AccountId, out var account))
                    {
                        character.AccountInfo = account;
                        account.Characters.Add(character);
                    }

                    envir.CharacterList.Add(character);
                }
            }

            return true;
        }

        private static IReadOnlyList<CharacterRow> CaptureCharacters(Envir envir)
        {
            if (envir == null) throw new ArgumentNullException(nameof(envir));

            lock (Envir.AccountLock)
            {
                var result = new List<CharacterRow>(envir.CharacterList.Count + (envir.HeroList?.Count ?? 0));
                var visited = new HashSet<int>();

                void CaptureCharacterRow(CharacterInfo character, CharacterEntityKind kind)
                {
                    if (character == null) return;
                    if (!visited.Add(character.Index)) return;

                    result.Add(new CharacterRow
                    {
                        CharacterId = character.Index,
                        AccountId = kind == CharacterEntityKind.Player ? (character.AccountInfo?.Index ?? 0) : 0,
                        CharacterKind = (int)kind,
                        CharacterName = character.Name ?? string.Empty,
                        Level = character.Level,
                        Class = (int)character.Class,
                        Gender = (int)character.Gender,
                        Hair = character.Hair,
                        GuildId = character.GuildIndex,
                        CreationIp = character.CreationIP ?? string.Empty,
                        CreationUtcMs = ToUtcMs(character.CreationDate),
                        Banned = character.Banned ? 1 : 0,
                        BanReason = character.BanReason ?? string.Empty,
                        ExpiryUtcMs = ToUtcMs(character.ExpiryDate),
                        ChatBanned = character.ChatBanned ? 1 : 0,
                        ChatBanExpiryUtcMs = ToUtcMs(character.ChatBanExpiryDate),
                        LastIp = character.LastIP ?? string.Empty,
                        LastLogoutUtcMs = ToUtcMs(character.LastLogoutDate),
                        LastLoginUtcMs = ToUtcMs(character.LastLoginDate),
                        Deleted = character.Deleted ? 1 : 0,
                        DeleteUtcMs = ToUtcMs(character.DeleteDate),
                        MarriedCharacterId = character.Married,
                        MarriedUtcMs = ToUtcMs(character.MarriedDate),
                        MentorCharacterId = character.Mentor,
                        MentorUtcMs = ToUtcMs(character.MentorDate),
                        IsMentor = character.IsMentor ? 1 : 0,
                        MentorExp = character.MentorExp,
                        CurrentMapId = character.CurrentMapIndex,
                        CurrentX = character.CurrentLocation.X,
                        CurrentY = character.CurrentLocation.Y,
                        Direction = (int)character.Direction,
                        BindMapId = character.BindMapIndex,
                        BindX = character.BindLocation.X,
                        BindY = character.BindLocation.Y,
                        Hp = character.HP,
                        Mp = character.MP,
                        Experience = character.Experience,
                        AttackMode = (int)character.AMode,
                        PetMode = (int)character.PMode,
                        AllowGroup = character.AllowGroup ? 1 : 0,
                        AllowTrade = character.AllowTrade ? 1 : 0,
                        AllowObserve = character.AllowObserve ? 1 : 0,
                        PkPoints = character.PKPoints,
                        NewDay = character.NewDay ? 1 : 0,
                        Thrusting = character.Thrusting ? 1 : 0,
                        HalfMoon = character.HalfMoon ? 1 : 0,
                        CrossHalfMoon = character.CrossHalfMoon ? 1 : 0,
                        DoubleSlash = character.DoubleSlash ? 1 : 0,
                        MentalState = character.MentalState,
                        PearlCount = character.PearlCount,
                        CollectTimeRemainingMs = Math.Max(0L, character.CollectTime - envir.Time),
                        MaximumHeroCount = character.MaximumHeroCount,
                        CurrentHeroIndex = character.CurrentHeroIndex,
                        HeroSpawned = character.HeroSpawned ? 1 : 0,
                        HeroBehaviour = (int)character.HeroBehaviour,
                    });
                }

                for (var i = 0; i < envir.CharacterList.Count; i++)
                    CaptureCharacterRow(envir.CharacterList[i], CharacterEntityKind.Player);

                if (envir.HeroList != null)
                {
                    for (var i = 0; i < envir.HeroList.Count; i++)
                        CaptureCharacterRow(envir.HeroList[i], CharacterEntityKind.Hero);
                }

                return result;
            }
        }

        private static void VisitAllPersistentCharacters(Envir envir, Action<CharacterInfo> visitor)
        {
            if (envir == null) throw new ArgumentNullException(nameof(envir));
            if (visitor == null) throw new ArgumentNullException(nameof(visitor));

            var visited = new HashSet<int>();

            if (envir.AccountList != null)
            {
                for (var accountIndex = 0; accountIndex < envir.AccountList.Count; accountIndex++)
                {
                    var account = envir.AccountList[accountIndex];
                    if (account?.Characters == null) continue;

                    for (var characterIndex = 0; characterIndex < account.Characters.Count; characterIndex++)
                    {
                        var character = account.Characters[characterIndex];
                        if (character == null) continue;
                        if (!visited.Add(character.Index)) continue;

                        visitor(character);
                    }
                }
            }

            if (envir.HeroList != null)
            {
                for (var heroIndex = 0; heroIndex < envir.HeroList.Count; heroIndex++)
                {
                    var hero = envir.HeroList[heroIndex];
                    if (hero == null) continue;
                    if (!visited.Add(hero.Index)) continue;

                    visitor(hero);
                }
            }
        }

        private static void UpsertCharacters(SqlSession session, IReadOnlyList<CharacterRow> characters, long saveEpochUtcMs = 0)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));
            characters ??= Array.Empty<CharacterRow>();

            var sql = session.Dialect.BuildUpsert(
                tableName: "characters",
                insertColumns:
                [
                    "character_id",
                    "account_id",
                    "character_kind",
                    "character_name",
                    "level",
                    "class",
                    "gender",
                    "hair",
                    "guild_id",
                    "creation_ip",
                    "creation_utc_ms",
                    "banned",
                    "ban_reason",
                    "expiry_utc_ms",
                    "chat_banned",
                    "chat_ban_expiry_utc_ms",
                    "last_ip",
                    "last_logout_utc_ms",
                    "last_login_utc_ms",
                    "deleted",
                    "delete_utc_ms",
                    "married_character_id",
                    "married_utc_ms",
                    "mentor_character_id",
                    "mentor_utc_ms",
                    "is_mentor",
                    "mentor_exp",
                    "current_map_id",
                    "current_x",
                    "current_y",
                    "direction",
                    "bind_map_id",
                    "bind_x",
                    "bind_y",
                    "hp",
                    "mp",
                    "experience",
                    "attack_mode",
                    "pet_mode",
                    "allow_group",
                    "allow_trade",
                    "allow_observe",
                    "pk_points",
                    "new_day",
                    "thrusting",
                    "half_moon",
                    "cross_half_moon",
                    "double_slash",
                    "mental_state",
                    "pearl_count",
                    "collect_time_remaining_ms",
                    "maximum_hero_count",
                    "current_hero_index",
                    "hero_spawned",
                    "hero_behaviour",
                    "lifecycle_state",
                    "updated_utc_ms",
                ],
                keyColumns: ["character_id"],
                updateColumns:
                [
                    "account_id",
                    "character_kind",
                    "character_name",
                    "level",
                    "class",
                    "gender",
                    "hair",
                    "guild_id",
                    "creation_ip",
                    "creation_utc_ms",
                    "banned",
                    "ban_reason",
                    "expiry_utc_ms",
                    "chat_banned",
                    "chat_ban_expiry_utc_ms",
                    "last_ip",
                    "last_logout_utc_ms",
                    "last_login_utc_ms",
                    "deleted",
                    "delete_utc_ms",
                    "married_character_id",
                    "married_utc_ms",
                    "mentor_character_id",
                    "mentor_utc_ms",
                    "is_mentor",
                    "mentor_exp",
                    "current_map_id",
                    "current_x",
                    "current_y",
                    "direction",
                    "bind_map_id",
                    "bind_x",
                    "bind_y",
                    "hp",
                    "mp",
                    "experience",
                    "attack_mode",
                    "pet_mode",
                    "allow_group",
                    "allow_trade",
                    "allow_observe",
                    "pk_points",
                    "new_day",
                    "thrusting",
                    "half_moon",
                    "cross_half_moon",
                    "double_slash",
                    "mental_state",
                    "pearl_count",
                    "collect_time_remaining_ms",
                    "maximum_hero_count",
                    "current_hero_index",
                    "hero_spawned",
                    "hero_behaviour",
                    "lifecycle_state",
                    "updated_utc_ms",
                ]);

            var nowMs = saveEpochUtcMs > 0 ? saveEpochUtcMs : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var batchSize = Settings.SaveBatchSize <= 0 ? 2000 : Settings.SaveBatchSize;

            for (var offset = 0; offset < characters.Count; offset += batchSize)
            {
                var take = Math.Min(batchSize, characters.Count - offset);
                var batch = new List<object>(take);

                for (var i = 0; i < take; i++)
                {
                    var character = characters[offset + i];
                    if (character == null) continue;

                    batch.Add(new
                    {
                        character_id = character.CharacterId,
                        account_id = character.AccountId,
                        character_kind = character.CharacterKind,
                        character_name = character.CharacterName ?? string.Empty,
                        level = character.Level,
                        @class = character.Class,
                        gender = character.Gender,
                        hair = character.Hair,
                        guild_id = character.GuildId,
                        creation_ip = character.CreationIp ?? string.Empty,
                        creation_utc_ms = character.CreationUtcMs,
                        banned = character.Banned,
                        ban_reason = character.BanReason ?? string.Empty,
                        expiry_utc_ms = character.ExpiryUtcMs,
                        chat_banned = character.ChatBanned,
                        chat_ban_expiry_utc_ms = character.ChatBanExpiryUtcMs,
                        last_ip = character.LastIp ?? string.Empty,
                        last_logout_utc_ms = character.LastLogoutUtcMs,
                        last_login_utc_ms = character.LastLoginUtcMs,
                        deleted = character.Deleted,
                        delete_utc_ms = character.DeleteUtcMs,
                        married_character_id = character.MarriedCharacterId,
                        married_utc_ms = character.MarriedUtcMs,
                        mentor_character_id = character.MentorCharacterId,
                        mentor_utc_ms = character.MentorUtcMs,
                        is_mentor = character.IsMentor,
                        mentor_exp = character.MentorExp,
                        current_map_id = character.CurrentMapId,
                        current_x = character.CurrentX,
                        current_y = character.CurrentY,
                        direction = character.Direction,
                        bind_map_id = character.BindMapId,
                        bind_x = character.BindX,
                        bind_y = character.BindY,
                        hp = character.Hp,
                        mp = character.Mp,
                        experience = character.Experience,
                        attack_mode = character.AttackMode,
                        pet_mode = character.PetMode,
                        allow_group = character.AllowGroup,
                        allow_trade = character.AllowTrade,
                        allow_observe = character.AllowObserve,
                        pk_points = character.PkPoints,
                        new_day = character.NewDay,
                        thrusting = character.Thrusting,
                        half_moon = character.HalfMoon,
                        cross_half_moon = character.CrossHalfMoon,
                        double_slash = character.DoubleSlash,
                        mental_state = character.MentalState,
                        pearl_count = character.PearlCount,
                        collect_time_remaining_ms = character.CollectTimeRemainingMs,
                        maximum_hero_count = character.MaximumHeroCount,
                        current_hero_index = character.CurrentHeroIndex,
                        hero_spawned = character.HeroSpawned,
                        hero_behaviour = character.HeroBehaviour,
                        lifecycle_state = "active",
                        updated_utc_ms = nowMs,
                    });
                }

                if (batch.Count > 0)
                    session.Execute(sql, batch);
            }

        }

        private static IReadOnlyList<CharacterRow> LoadCharacterRows(SqlSession session)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));

            return session.Query<CharacterRow>(
                "SELECT " +
                "character_id AS CharacterId, " +
                "account_id AS AccountId, " +
                "character_kind AS CharacterKind, " +
                "character_name AS CharacterName, " +
                "level AS Level, " +
                "class AS Class, " +
                "gender AS Gender, " +
                "hair AS Hair, " +
                "guild_id AS GuildId, " +
                "creation_ip AS CreationIp, " +
                "creation_utc_ms AS CreationUtcMs, " +
                "banned AS Banned, " +
                "ban_reason AS BanReason, " +
                "expiry_utc_ms AS ExpiryUtcMs, " +
                "chat_banned AS ChatBanned, " +
                "chat_ban_expiry_utc_ms AS ChatBanExpiryUtcMs, " +
                "last_ip AS LastIp, " +
                "last_logout_utc_ms AS LastLogoutUtcMs, " +
                "last_login_utc_ms AS LastLoginUtcMs, " +
                "deleted AS Deleted, " +
                "delete_utc_ms AS DeleteUtcMs, " +
                "married_character_id AS MarriedCharacterId, " +
                "married_utc_ms AS MarriedUtcMs, " +
                "mentor_character_id AS MentorCharacterId, " +
                "mentor_utc_ms AS MentorUtcMs, " +
                "is_mentor AS IsMentor, " +
                "mentor_exp AS MentorExp, " +
                "current_map_id AS CurrentMapId, " +
                "current_x AS CurrentX, " +
                "current_y AS CurrentY, " +
                "direction AS Direction, " +
                "bind_map_id AS BindMapId, " +
                "bind_x AS BindX, " +
                "bind_y AS BindY, " +
                "hp AS Hp, " +
                "mp AS Mp, " +
                "experience AS Experience, " +
                "attack_mode AS AttackMode, " +
                "pet_mode AS PetMode, " +
                "allow_group AS AllowGroup, " +
                "allow_trade AS AllowTrade, " +
                "allow_observe AS AllowObserve, " +
                "pk_points AS PkPoints, " +
                "new_day AS NewDay, " +
                "thrusting AS Thrusting, " +
                "half_moon AS HalfMoon, " +
                "cross_half_moon AS CrossHalfMoon, " +
                "double_slash AS DoubleSlash, " +
                "mental_state AS MentalState, " +
                "pearl_count AS PearlCount, " +
                "collect_time_remaining_ms AS CollectTimeRemainingMs, " +
                "maximum_hero_count AS MaximumHeroCount, " +
                "current_hero_index AS CurrentHeroIndex, " +
                "hero_spawned AS HeroSpawned, " +
                "hero_behaviour AS HeroBehaviour " +
                "FROM characters WHERE lifecycle_state='active' " +
                "ORDER BY character_kind, account_id, character_id");
        }

        private static void ApplyCharacters(Envir envir, IReadOnlyList<CharacterRow> rows)
        {
            if (envir == null) throw new ArgumentNullException(nameof(envir));
            if (rows == null || rows.Count == 0) return;

            var byId = new Dictionary<long, CharacterRow>(rows.Count);
            for (var i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                if (row == null) continue;
                byId[row.CharacterId] = row;
            }

            lock (Envir.AccountLock)
            {
                var charactersById = BuildCharacterIndex(envir);

                foreach (var pair in charactersById)
                {
                    var character = pair.Value;
                    if (character == null) continue;

                    if (!byId.TryGetValue(character.Index, out var row) || row == null)
                        continue;

                    character.Name = row.CharacterName ?? character.Name ?? string.Empty;
                    character.Level = (ushort)Math.Clamp(row.Level, 0, ushort.MaxValue);
                    character.Class = (MirClass)row.Class;
                    character.Gender = (MirGender)row.Gender;
                    character.Hair = (byte)Math.Clamp(row.Hair, 0, byte.MaxValue);
                    character.GuildIndex = (int)Math.Clamp(row.GuildId, int.MinValue, int.MaxValue);
                    character.CreationIP = row.CreationIp ?? string.Empty;
                    character.CreationDate = FromUtcMsToLocal(row.CreationUtcMs);
                    character.Banned = row.Banned != 0;
                    character.BanReason = row.BanReason ?? string.Empty;
                    character.ExpiryDate = FromUtcMsToLocal(row.ExpiryUtcMs);
                    character.ChatBanned = row.ChatBanned != 0;
                    character.ChatBanExpiryDate = FromUtcMsToLocal(row.ChatBanExpiryUtcMs);
                    character.LastIP = row.LastIp ?? string.Empty;
                    character.LastLogoutDate = FromUtcMsToLocal(row.LastLogoutUtcMs);
                    character.LastLoginDate = FromUtcMsToLocal(row.LastLoginUtcMs);
                    character.Deleted = row.Deleted != 0;
                    character.DeleteDate = FromUtcMsToLocal(row.DeleteUtcMs);
                    character.Married = (int)Math.Clamp(row.MarriedCharacterId, int.MinValue, int.MaxValue);
                    character.MarriedDate = FromUtcMsToLocal(row.MarriedUtcMs);
                    character.Mentor = (int)Math.Clamp(row.MentorCharacterId, int.MinValue, int.MaxValue);
                    character.MentorDate = FromUtcMsToLocal(row.MentorUtcMs);
                    character.IsMentor = row.IsMentor != 0;
                    character.MentorExp = row.MentorExp;
                    character.CurrentMapIndex = row.CurrentMapId;
                    character.CurrentLocation = new Point(row.CurrentX, row.CurrentY);
                    character.Direction = (MirDirection)row.Direction;
                    character.BindMapIndex = row.BindMapId;
                    character.BindLocation = new Point(row.BindX, row.BindY);
                    character.HP = row.Hp;
                    character.MP = row.Mp;
                    character.Experience = row.Experience;
                    character.AMode = (AttackMode)row.AttackMode;
                    character.PMode = (PetMode)row.PetMode;
                    character.AllowGroup = row.AllowGroup != 0;
                    character.AllowTrade = row.AllowTrade != 0;
                    character.AllowObserve = row.AllowObserve != 0;
                    character.PKPoints = row.PkPoints;
                    character.NewDay = row.NewDay != 0;
                    character.Thrusting = row.Thrusting != 0;
                    character.HalfMoon = row.HalfMoon != 0;
                    character.CrossHalfMoon = row.CrossHalfMoon != 0;
                    character.DoubleSlash = row.DoubleSlash != 0;
                    character.MentalState = (byte)Math.Clamp(row.MentalState, 0, byte.MaxValue);
                    character.PearlCount = row.PearlCount;
                    character.CollectTime = row.CollectTimeRemainingMs > 0 ? envir.Time + row.CollectTimeRemainingMs : 0;
                    character.MaximumHeroCount = Math.Max(1, row.MaximumHeroCount);
                    character.CurrentHeroIndex = row.CurrentHeroIndex;
                    character.HeroSpawned = row.HeroSpawned != 0;
                    character.HeroBehaviour = (HeroBehaviour)row.HeroBehaviour;

                    if (character.Heroes == null || character.Heroes.Length != character.MaximumHeroCount)
                        character.Heroes = new HeroInfo[character.MaximumHeroCount];
                }
            }
        }

        private static void CaptureItems(
            Envir envir,
            out IReadOnlyList<ItemRow> items,
            out IReadOnlyList<ItemAddedStatRow> itemAddedStats,
            out IReadOnlyList<ItemAwakeLevelRow> itemAwakeLevels,
            out IReadOnlyList<ItemSlotLinkRow> itemSlotLinks)
        {
            if (envir == null) throw new ArgumentNullException(nameof(envir));

            var itemRows = new List<ItemRow>();
            var statRows = new List<ItemAddedStatRow>();
            var awakeRows = new List<ItemAwakeLevelRow>();
            var slotRows = new List<ItemSlotLinkRow>();

            var visited = new HashSet<ulong>();

            void VisitItem(UserItem item)
            {
                if (item == null) return;
                if (item.UniqueID == 0) return;
                if (!visited.Add(item.UniqueID)) return;

                var itemId = ToDbInt64(item.UniqueID, "item_id");

                var row = new ItemRow
                {
                    ItemId = itemId,
                    ItemIndex = item.ItemIndex,
                    CurrentDura = item.CurrentDura,
                    MaxDura = item.MaxDura,
                    StackCount = item.Count,
                    GemCount = item.GemCount,
                    SoulBoundId = item.SoulBoundId,
                    Identified = item.Identified ? 1 : 0,
                    Cursed = item.Cursed ? 1 : 0,
                    SlotCount = item.Slots?.Length ?? 0,
                    AwakeType = item.Awake != null ? (int)item.Awake.Type : 0,
                    RefinedValue = (int)item.RefinedValue,
                    RefineAdded = item.RefineAdded,
                    RefineSuccessChance = item.RefineSuccessChance,
                    WeddingRing = item.WeddingRing,
                    ExpireUtcMs = item.ExpireInfo != null ? ToUtcMs(item.ExpireInfo.ExpiryDate) : 0,
                    RentalOwnerName = item.RentalInformation?.OwnerName ?? string.Empty,
                    RentalBindingFlags = item.RentalInformation != null ? (int)item.RentalInformation.BindingFlags : 0,
                    RentalExpiryUtcMs = item.RentalInformation != null ? ToUtcMs(item.RentalInformation.ExpiryDate) : 0,
                    RentalLocked = item.RentalInformation?.RentalLocked == true ? 1 : 0,
                    IsShopItem = item.IsShopItem ? 1 : 0,
                    SealedExpiryUtcMs = item.SealedInfo != null ? ToUtcMs(item.SealedInfo.ExpiryDate) : 0,
                    SealedNextSealUtcMs = item.SealedInfo != null ? ToUtcMs(item.SealedInfo.NextSealDate) : 0,
                    GmMade = item.GMMade ? 1 : 0,
                };

                itemRows.Add(row);

                if (item.AddedStats?.Values != null && item.AddedStats.Values.Count > 0)
                {
                    foreach (var pair in item.AddedStats.Values)
                    {
                        statRows.Add(new ItemAddedStatRow
                        {
                            ItemId = itemId,
                            StatId = (int)pair.Key,
                            StatValue = pair.Value,
                        });
                    }
                }

                if (item.Awake != null)
                {
                    var awakeCount = item.Awake.GetAwakeLevel();
                    for (var i = 0; i < awakeCount; i++)
                    {
                        awakeRows.Add(new ItemAwakeLevelRow
                        {
                            ItemId = itemId,
                            LevelIndex = i,
                            LevelValue = item.Awake.GetAwakeLevelValue(i),
                        });
                    }
                }

                var slots = item.Slots ?? Array.Empty<UserItem>();
                for (var slotIndex = 0; slotIndex < slots.Length; slotIndex++)
                {
                    var child = slots[slotIndex];
                    if (child == null) continue;
                    if (child.UniqueID == 0) continue;

                    var childItemId = ToDbInt64(child.UniqueID, "child_item_id");
                    slotRows.Add(new ItemSlotLinkRow
                    {
                        ParentItemId = itemId,
                        SlotIndex = slotIndex,
                        ChildItemId = childItemId,
                    });

                    VisitItem(child);
                }
            }

            void VisitCharacter(CharacterInfo character)
            {
                if (character == null) return;

                var inventory = character.Inventory ?? Array.Empty<UserItem>();
                for (var i = 0; i < inventory.Length; i++)
                    VisitItem(inventory[i]);

                var equipment = character.Equipment ?? Array.Empty<UserItem>();
                for (var i = 0; i < equipment.Length; i++)
                    VisitItem(equipment[i]);

                var questInventory = character.QuestInventory ?? Array.Empty<UserItem>();
                for (var i = 0; i < questInventory.Length; i++)
                    VisitItem(questInventory[i]);

                if (character.CurrentRefine != null)
                    VisitItem(character.CurrentRefine);

                if (character.Mail != null)
                {
                    for (var i = 0; i < character.Mail.Count; i++)
                    {
                        var mail = character.Mail[i];
                        if (mail?.Items == null) continue;

                        for (var j = 0; j < mail.Items.Count; j++)
                            VisitItem(mail.Items[j]);
                    }
                }

                if (character.Heroes != null)
                {
                    for (var i = 0; i < character.Heroes.Length; i++)
                    {
                        var hero = character.Heroes[i];
                        if (hero == null) continue;
                        VisitCharacter(hero);
                    }
                }
            }

            lock (Envir.AccountLock)
            {
                for (var i = 0; i < envir.AccountList.Count; i++)
                {
                    var account = envir.AccountList[i];
                    if (account == null) continue;

                    var storage = account.Storage ?? Array.Empty<UserItem>();
                    for (var j = 0; j < storage.Length; j++)
                        VisitItem(storage[j]);

                    if (account.Characters != null)
                    {
                        for (var j = 0; j < account.Characters.Count; j++)
                            VisitCharacter(account.Characters[j]);
                    }
                }

                for (var i = 0; i < envir.HeroList.Count; i++)
                    VisitCharacter(envir.HeroList[i]);

                foreach (var auction in envir.Auctions)
                    VisitItem(auction?.Item);

                foreach (var guild in envir.GuildList)
                {
                    if (guild?.StoredItems == null) continue;
                    foreach (var entry in guild.StoredItems)
                        VisitItem(entry?.Item);
                }

                foreach (var npc in envir.NPCs)
                {
                    if (npc == null) continue;
                    foreach (var item in npc.UsedGoods)
                        VisitItem(item);
                    foreach (var list in npc.BuyBack.Values)
                        foreach (var item in list)
                            VisitItem(item);
                }
            }

            items = itemRows;
            itemAddedStats = statRows;
            itemAwakeLevels = awakeRows;
            itemSlotLinks = slotRows;
        }

        private static void ReplaceItems(
            SqlSession session,
            IReadOnlyList<ItemRow> items,
            IReadOnlyList<ItemAddedStatRow> itemAddedStats,
            IReadOnlyList<ItemAwakeLevelRow> itemAwakeLevels,
            IReadOnlyList<ItemSlotLinkRow> itemSlotLinks,
            long saveEpochUtcMs = 0)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));

            var nowMs = saveEpochUtcMs > 0 ? saveEpochUtcMs : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            var batchSize = Settings.SaveBatchSize <= 0 ? 2000 : Settings.SaveBatchSize;

            if (items != null && items.Count > 0)
            {
                var sql = session.Dialect.BuildUpsert(
                    tableName: "item_instances",
                    insertColumns:
                    [
                        "item_id",
                        "item_index",
                        "current_dura",
                        "max_dura",
                        "stack_count",
                        "gem_count",
                        "soul_bound_id",
                        "identified",
                        "cursed",
                        "slot_count",
                        "awake_type",
                        "refined_value",
                        "refine_added",
                        "refine_success_chance",
                        "wedding_ring",
                        "expire_utc_ms",
                        "rental_owner_name",
                        "rental_binding_flags",
                        "rental_expiry_utc_ms",
                        "rental_locked",
                        "is_shop_item",
                        "sealed_expiry_utc_ms",
                        "sealed_next_seal_utc_ms",
                        "gm_made",
                        "updated_utc_ms",
                    ],
                    keyColumns: ["item_id"],
                    updateColumns:
                    [
                        "item_index",
                        "current_dura",
                        "max_dura",
                        "stack_count",
                        "gem_count",
                        "soul_bound_id",
                        "identified",
                        "cursed",
                        "slot_count",
                        "awake_type",
                        "refined_value",
                        "refine_added",
                        "refine_success_chance",
                        "wedding_ring",
                        "expire_utc_ms",
                        "rental_owner_name",
                        "rental_binding_flags",
                        "rental_expiry_utc_ms",
                        "rental_locked",
                        "is_shop_item",
                        "sealed_expiry_utc_ms",
                        "sealed_next_seal_utc_ms",
                        "gm_made",
                        "updated_utc_ms",
                    ]);

                for (var offset = 0; offset < items.Count; offset += batchSize)
                {
                    var take = Math.Min(batchSize, items.Count - offset);
                    var batch = new List<object>(take);

                    for (var i = 0; i < take; i++)
                    {
                        var item = items[offset + i];
                        if (item == null) continue;

                        batch.Add(new
                        {
                            item_id = item.ItemId,
                            item_index = item.ItemIndex,
                            current_dura = item.CurrentDura,
                            max_dura = item.MaxDura,
                            stack_count = item.StackCount,
                            gem_count = item.GemCount,
                            soul_bound_id = item.SoulBoundId,
                            identified = item.Identified,
                            cursed = item.Cursed,
                            slot_count = item.SlotCount,
                            awake_type = item.AwakeType,
                            refined_value = item.RefinedValue,
                            refine_added = item.RefineAdded,
                            refine_success_chance = item.RefineSuccessChance,
                            wedding_ring = item.WeddingRing,
                            expire_utc_ms = item.ExpireUtcMs,
                            rental_owner_name = item.RentalOwnerName ?? string.Empty,
                            rental_binding_flags = item.RentalBindingFlags,
                            rental_expiry_utc_ms = item.RentalExpiryUtcMs,
                            rental_locked = item.RentalLocked,
                            is_shop_item = item.IsShopItem,
                            sealed_expiry_utc_ms = item.SealedExpiryUtcMs,
                            sealed_next_seal_utc_ms = item.SealedNextSealUtcMs,
                            gm_made = item.GmMade,
                            updated_utc_ms = nowMs,
                        });
                    }

                    if (batch.Count > 0)
                        session.Execute(sql, batch);
                }
            }

            if (itemAddedStats != null && itemAddedStats.Count > 0)
            {
                var sql = session.Dialect.BuildUpsert(
                    tableName: "item_added_stats",
                    insertColumns: ["item_id", "stat_id", "stat_value", "updated_utc_ms"],
                    keyColumns: ["item_id", "stat_id"],
                    updateColumns: ["stat_value", "updated_utc_ms"]);

                for (var offset = 0; offset < itemAddedStats.Count; offset += batchSize)
                {
                    var take = Math.Min(batchSize, itemAddedStats.Count - offset);
                    var batch = new List<object>(take);

                    for (var i = 0; i < take; i++)
                    {
                        var stat = itemAddedStats[offset + i];
                        if (stat == null) continue;
                        batch.Add(new
                        {
                            item_id = stat.ItemId,
                            stat_id = stat.StatId,
                            stat_value = stat.StatValue,
                            updated_utc_ms = nowMs,
                        });
                    }

                    if (batch.Count > 0)
                        session.Execute(sql, batch);
                }
            }

            if (itemAwakeLevels != null && itemAwakeLevels.Count > 0)
            {
                var sql = session.Dialect.BuildUpsert(
                    tableName: "item_awake_levels",
                    insertColumns: ["item_id", "level_index", "level_value", "updated_utc_ms"],
                    keyColumns: ["item_id", "level_index"],
                    updateColumns: ["level_value", "updated_utc_ms"]);

                for (var offset = 0; offset < itemAwakeLevels.Count; offset += batchSize)
                {
                    var take = Math.Min(batchSize, itemAwakeLevels.Count - offset);
                    var batch = new List<object>(take);

                    for (var i = 0; i < take; i++)
                    {
                        var level = itemAwakeLevels[offset + i];
                        if (level == null) continue;
                        batch.Add(new
                        {
                            item_id = level.ItemId,
                            level_index = level.LevelIndex,
                            level_value = level.LevelValue,
                            updated_utc_ms = nowMs,
                        });
                    }

                    if (batch.Count > 0)
                        session.Execute(sql, batch);
                }
            }

            if (itemSlotLinks != null && itemSlotLinks.Count > 0)
            {
                var sql = session.Dialect.BuildUpsert(
                    tableName: "item_slot_links",
                    insertColumns: ["parent_item_id", "slot_index", "child_item_id", "updated_utc_ms"],
                    keyColumns: ["parent_item_id", "slot_index"],
                    updateColumns: ["child_item_id", "updated_utc_ms"]);

                for (var offset = 0; offset < itemSlotLinks.Count; offset += batchSize)
                {
                    var take = Math.Min(batchSize, itemSlotLinks.Count - offset);
                    var batch = new List<object>(take);

                    for (var i = 0; i < take; i++)
                    {
                        var link = itemSlotLinks[offset + i];
                        if (link == null) continue;
                        batch.Add(new
                        {
                            parent_item_id = link.ParentItemId,
                            slot_index = link.SlotIndex,
                            child_item_id = link.ChildItemId,
                            updated_utc_ms = nowMs,
                        });
                    }

                    if (batch.Count > 0)
                        session.Execute(sql, batch);
                }
            }

            // 清理本轮未触达的旧数据（等价于“全量替换”，但避免先删再插的窗口期）。
            session.Execute("DELETE FROM item_slot_links WHERE updated_utc_ms <> @nowMs", new { nowMs });
            session.Execute("DELETE FROM item_awake_levels WHERE updated_utc_ms <> @nowMs", new { nowMs });
            session.Execute("DELETE FROM item_added_stats WHERE updated_utc_ms <> @nowMs", new { nowMs });
        }

        private static IReadOnlyList<ItemRow> LoadItemRows(SqlSession session)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));

            return session.Query<ItemRow>(
                "SELECT " +
                "item_id AS ItemId, " +
                "item_index AS ItemIndex, " +
                "current_dura AS CurrentDura, " +
                "max_dura AS MaxDura, " +
                "stack_count AS StackCount, " +
                "gem_count AS GemCount, " +
                "soul_bound_id AS SoulBoundId, " +
                "identified AS Identified, " +
                "cursed AS Cursed, " +
                "slot_count AS SlotCount, " +
                "awake_type AS AwakeType, " +
                "refined_value AS RefinedValue, " +
                "refine_added AS RefineAdded, " +
                "refine_success_chance AS RefineSuccessChance, " +
                "wedding_ring AS WeddingRing, " +
                "expire_utc_ms AS ExpireUtcMs, " +
                "rental_owner_name AS RentalOwnerName, " +
                "rental_binding_flags AS RentalBindingFlags, " +
                "rental_expiry_utc_ms AS RentalExpiryUtcMs, " +
                "rental_locked AS RentalLocked, " +
                "is_shop_item AS IsShopItem, " +
                "sealed_expiry_utc_ms AS SealedExpiryUtcMs, " +
                "sealed_next_seal_utc_ms AS SealedNextSealUtcMs, " +
                "gm_made AS GmMade " +
                "FROM item_instances");
        }

        private static IReadOnlyList<ItemAddedStatRow> LoadItemAddedStatRows(SqlSession session)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));

            return session.Query<ItemAddedStatRow>(
                "SELECT item_id AS ItemId, stat_id AS StatId, stat_value AS StatValue FROM item_added_stats");
        }

        private static IReadOnlyList<ItemAwakeLevelRow> LoadItemAwakeLevelRows(SqlSession session)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));

            return session.Query<ItemAwakeLevelRow>(
                "SELECT item_id AS ItemId, level_index AS LevelIndex, level_value AS LevelValue FROM item_awake_levels");
        }

        private static IReadOnlyList<ItemSlotLinkRow> LoadItemSlotLinkRows(SqlSession session)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));

            return session.Query<ItemSlotLinkRow>(
                "SELECT parent_item_id AS ParentItemId, slot_index AS SlotIndex, child_item_id AS ChildItemId FROM item_slot_links");
        }

        private static Dictionary<long, UserItem> CollectInMemoryItems(Envir envir)
        {
            if (envir == null) throw new ArgumentNullException(nameof(envir));

            var result = new Dictionary<long, UserItem>();
            var visited = new HashSet<ulong>();

            void VisitItem(UserItem item)
            {
                if (item == null) return;
                if (item.UniqueID == 0) return;
                if (!visited.Add(item.UniqueID)) return;

                var itemId = ToDbInt64(item.UniqueID, "item_id");
                result[itemId] = item;

                var slots = item.Slots ?? Array.Empty<UserItem>();
                for (var i = 0; i < slots.Length; i++)
                    VisitItem(slots[i]);
            }

            void VisitCharacter(CharacterInfo character)
            {
                if (character == null) return;

                var inventory = character.Inventory ?? Array.Empty<UserItem>();
                for (var i = 0; i < inventory.Length; i++)
                    VisitItem(inventory[i]);

                var equipment = character.Equipment ?? Array.Empty<UserItem>();
                for (var i = 0; i < equipment.Length; i++)
                    VisitItem(equipment[i]);

                var questInventory = character.QuestInventory ?? Array.Empty<UserItem>();
                for (var i = 0; i < questInventory.Length; i++)
                    VisitItem(questInventory[i]);

                if (character.CurrentRefine != null)
                    VisitItem(character.CurrentRefine);

                if (character.Mail != null)
                {
                    for (var i = 0; i < character.Mail.Count; i++)
                    {
                        var mail = character.Mail[i];
                        if (mail?.Items == null) continue;

                        for (var j = 0; j < mail.Items.Count; j++)
                            VisitItem(mail.Items[j]);
                    }
                }

                if (character.Heroes != null)
                {
                    for (var i = 0; i < character.Heroes.Length; i++)
                    {
                        var hero = character.Heroes[i];
                        if (hero == null) continue;
                        VisitCharacter(hero);
                    }
                }
            }

            lock (Envir.AccountLock)
            {
                for (var i = 0; i < envir.AccountList.Count; i++)
                {
                    var account = envir.AccountList[i];
                    if (account == null) continue;

                    var storage = account.Storage ?? Array.Empty<UserItem>();
                    for (var j = 0; j < storage.Length; j++)
                        VisitItem(storage[j]);

                    if (account.Characters != null)
                    {
                        for (var j = 0; j < account.Characters.Count; j++)
                            VisitCharacter(account.Characters[j]);
                    }
                }

                for (var i = 0; i < envir.HeroList.Count; i++)
                    VisitCharacter(envir.HeroList[i]);

                foreach (var auction in envir.Auctions)
                    VisitItem(auction?.Item);

                foreach (var guild in envir.GuildList)
                {
                    if (guild?.StoredItems == null) continue;
                    foreach (var entry in guild.StoredItems)
                        VisitItem(entry?.Item);
                }

                foreach (var npc in envir.NPCs)
                {
                    if (npc == null) continue;
                    foreach (var item in npc.UsedGoods)
                        VisitItem(item);
                    foreach (var list in npc.BuyBack.Values)
                        foreach (var item in list)
                            VisitItem(item);
                }
            }

            return result;
        }

        private static Awake BuildAwake(int awakeType, IReadOnlyList<ItemAwakeLevelRow> levels)
        {
            return new Awake(
                (AwakeType)awakeType,
                (levels ?? Array.Empty<ItemAwakeLevelRow>())
                    .Select(level => (byte)Math.Clamp(level?.LevelValue ?? 0, 0, byte.MaxValue)));
        }

        private static Dictionary<long, UserItem> ApplyItems(
            Envir envir,
            IReadOnlyList<ItemRow> itemRows,
            IReadOnlyList<ItemAddedStatRow> itemAddedStats,
            IReadOnlyList<ItemAwakeLevelRow> itemAwakeLevels,
            IReadOnlyList<ItemSlotLinkRow> itemSlotLinks)
        {
            if (envir == null) throw new ArgumentNullException(nameof(envir));
            if (itemRows == null || itemRows.Count == 0) return new Dictionary<long, UserItem>();

            var itemInfoByIndex = new Dictionary<int, ItemInfo>(envir.ItemInfoList.Count);
            for (var i = 0; i < envir.ItemInfoList.Count; i++)
            {
                var info = envir.ItemInfoList[i];
                if (info == null) continue;
                itemInfoByIndex[info.Index] = info;
            }

            var inMemory = CollectInMemoryItems(envir);

            var rowsById = new Dictionary<long, ItemRow>(itemRows.Count);
            for (var i = 0; i < itemRows.Count; i++)
            {
                var row = itemRows[i];
                if (row == null) continue;
                rowsById[row.ItemId] = row;
            }

            var statsByItem = new Dictionary<long, List<ItemAddedStatRow>>();
            if (itemAddedStats != null)
            {
                for (var i = 0; i < itemAddedStats.Count; i++)
                {
                    var stat = itemAddedStats[i];
                    if (stat == null) continue;

                    if (!statsByItem.TryGetValue(stat.ItemId, out var list))
                    {
                        list = new List<ItemAddedStatRow>();
                        statsByItem[stat.ItemId] = list;
                    }
                    list.Add(stat);
                }
            }

            var awakeByItem = new Dictionary<long, List<ItemAwakeLevelRow>>();
            if (itemAwakeLevels != null)
            {
                for (var i = 0; i < itemAwakeLevels.Count; i++)
                {
                    var level = itemAwakeLevels[i];
                    if (level == null) continue;

                    if (!awakeByItem.TryGetValue(level.ItemId, out var list))
                    {
                        list = new List<ItemAwakeLevelRow>();
                        awakeByItem[level.ItemId] = list;
                    }
                    list.Add(level);
                }
            }

            foreach (var pair in awakeByItem)
                pair.Value.Sort((a, b) => (a?.LevelIndex ?? 0).CompareTo(b?.LevelIndex ?? 0));

            var bindList = new List<UserItem>();

            for (var i = 0; i < itemRows.Count; i++)
            {
                var row = itemRows[i];
                if (row == null) continue;
                if (row.ItemId <= 0) continue;

                if (!inMemory.TryGetValue(row.ItemId, out var item) || item == null)
                {
                    if (!itemInfoByIndex.TryGetValue(row.ItemIndex, out var info) || info == null)
                        continue;

                    item = new UserItem(info)
                    {
                        UniqueID = (ulong)row.ItemId,
                    };
                    inMemory[row.ItemId] = item;
                }

                if (item.ItemIndex != row.ItemIndex)
                {
                    item.ItemIndex = row.ItemIndex;
                    item.Info = null;
                }

                item.CurrentDura = (ushort)Math.Clamp(row.CurrentDura, 0, ushort.MaxValue);
                item.MaxDura = (ushort)Math.Clamp(row.MaxDura, 0, ushort.MaxValue);
                item.Count = (ushort)Math.Clamp(row.StackCount, 0, ushort.MaxValue);
                item.GemCount = (ushort)Math.Clamp(row.GemCount, 0, ushort.MaxValue);
                item.SoulBoundId = row.SoulBoundId;
                item.Identified = row.Identified != 0;
                item.Cursed = row.Cursed != 0;
                item.RefinedValue = (RefinedValue)row.RefinedValue;
                item.RefineAdded = (byte)Math.Clamp(row.RefineAdded, 0, byte.MaxValue);
                item.RefineSuccessChance = row.RefineSuccessChance;
                item.WeddingRing = row.WeddingRing;

                item.ExpireInfo = row.ExpireUtcMs > 0
                    ? new ExpireInfo { ExpiryDate = FromUtcMsToLocal(row.ExpireUtcMs) }
                    : null;

                item.RentalInformation = row.RentalExpiryUtcMs > 0 || row.RentalLocked != 0 || row.RentalBindingFlags != 0 || !string.IsNullOrWhiteSpace(row.RentalOwnerName)
                    ? new RentalInformation
                    {
                        OwnerName = row.RentalOwnerName ?? string.Empty,
                        BindingFlags = (BindMode)row.RentalBindingFlags,
                        ExpiryDate = FromUtcMsToLocal(row.RentalExpiryUtcMs),
                        RentalLocked = row.RentalLocked != 0,
                    }
                    : null;

                item.IsShopItem = row.IsShopItem != 0;

                item.SealedInfo = row.SealedExpiryUtcMs > 0 || row.SealedNextSealUtcMs > 0
                    ? new SealedInfo
                    {
                        ExpiryDate = FromUtcMsToLocal(row.SealedExpiryUtcMs),
                        NextSealDate = FromUtcMsToLocal(row.SealedNextSealUtcMs),
                    }
                    : null;

                item.GMMade = row.GmMade != 0;

                var stats = new Stats();
                if (statsByItem.TryGetValue(row.ItemId, out var statList))
                {
                    for (var j = 0; j < statList.Count; j++)
                    {
                        var stat = statList[j];
                        if (stat == null) continue;
                        stats[(Stat)stat.StatId] = stat.StatValue;
                    }
                }
                item.AddedStats = stats;

                awakeByItem.TryGetValue(row.ItemId, out var awakeLevels);
                item.Awake = BuildAwake(row.AwakeType, (IReadOnlyList<ItemAwakeLevelRow>)awakeLevels ?? Array.Empty<ItemAwakeLevelRow>());

                item.SetSlotSize(Math.Max(0, row.SlotCount));
                if (item.Slots != null && item.Slots.Length > 0)
                    Array.Clear(item.Slots, 0, item.Slots.Length);

                if (item.Info == null || item.Info.Index != item.ItemIndex)
                    bindList.Add(item);
            }

            if (itemSlotLinks != null && itemSlotLinks.Count > 0)
            {
                for (var i = 0; i < itemSlotLinks.Count; i++)
                {
                    var link = itemSlotLinks[i];
                    if (link == null) continue;

                    if (!inMemory.TryGetValue(link.ParentItemId, out var parent) || parent == null) continue;
                    if (!inMemory.TryGetValue(link.ChildItemId, out var child) || child == null) continue;

                    var slots = parent.Slots ?? Array.Empty<UserItem>();
                    if (link.SlotIndex < 0 || link.SlotIndex >= slots.Length) continue;

                    slots[link.SlotIndex] = child;
                }
            }

            for (var i = 0; i < bindList.Count; i++)
            {
                var item = bindList[i];
                if (item == null) continue;
                envir.BindItem(item);
            }

            return inMemory;
        }

        private static void CaptureContainers(
            Envir envir,
            out IReadOnlyList<AccountStorageRow> accountStorage,
            out IReadOnlyList<AccountStorageSlotRow> accountStorageSlots,
            out IReadOnlyList<CharacterContainerRow> characterContainers,
            out IReadOnlyList<CharacterContainerSlotRow> characterContainerSlots)
        {
            if (envir == null) throw new ArgumentNullException(nameof(envir));

            var storageRows = new List<AccountStorageRow>();
            var storageSlotRows = new List<AccountStorageSlotRow>();
            var containerRows = new List<CharacterContainerRow>();
            var containerSlotRows = new List<CharacterContainerSlotRow>();

            var visitedCharacterIds = new HashSet<int>();

            void CaptureCharacter(CharacterInfo character)
            {
                if (character == null) return;
                if (!visitedCharacterIds.Add(character.Index)) return;

                var characterId = (long)character.Index;

                var inventory = character.Inventory ?? Array.Empty<UserItem>();
                containerRows.Add(new CharacterContainerRow
                {
                    CharacterId = characterId,
                    ContainerKind = (int)CharacterContainerKind.Inventory,
                    SlotCount = inventory.Length,
                });

                for (var i = 0; i < inventory.Length; i++)
                {
                    var item = inventory[i];
                    if (item == null) continue;
                    if (item.UniqueID == 0) continue;

                    containerSlotRows.Add(new CharacterContainerSlotRow
                    {
                        CharacterId = characterId,
                        ContainerKind = (int)CharacterContainerKind.Inventory,
                        SlotIndex = i,
                        ItemId = ToDbInt64(item.UniqueID, "item_id"),
                    });
                }

                var equipment = character.Equipment ?? Array.Empty<UserItem>();
                containerRows.Add(new CharacterContainerRow
                {
                    CharacterId = characterId,
                    ContainerKind = (int)CharacterContainerKind.Equipment,
                    SlotCount = equipment.Length,
                });

                for (var i = 0; i < equipment.Length; i++)
                {
                    var item = equipment[i];
                    if (item == null) continue;
                    if (item.UniqueID == 0) continue;

                    containerSlotRows.Add(new CharacterContainerSlotRow
                    {
                        CharacterId = characterId,
                        ContainerKind = (int)CharacterContainerKind.Equipment,
                        SlotIndex = i,
                        ItemId = ToDbInt64(item.UniqueID, "item_id"),
                    });
                }

                var questInventory = character.QuestInventory ?? Array.Empty<UserItem>();
                containerRows.Add(new CharacterContainerRow
                {
                    CharacterId = characterId,
                    ContainerKind = (int)CharacterContainerKind.QuestInventory,
                    SlotCount = questInventory.Length,
                });

                for (var i = 0; i < questInventory.Length; i++)
                {
                    var item = questInventory[i];
                    if (item == null) continue;
                    if (item.UniqueID == 0) continue;

                    containerSlotRows.Add(new CharacterContainerSlotRow
                    {
                        CharacterId = characterId,
                        ContainerKind = (int)CharacterContainerKind.QuestInventory,
                        SlotIndex = i,
                        ItemId = ToDbInt64(item.UniqueID, "item_id"),
                    });
                }

                containerRows.Add(new CharacterContainerRow
                {
                    CharacterId = characterId,
                    ContainerKind = (int)CharacterContainerKind.CurrentRefine,
                    SlotCount = 1,
                });

                if (character.CurrentRefine != null && character.CurrentRefine.UniqueID != 0)
                {
                    containerSlotRows.Add(new CharacterContainerSlotRow
                    {
                        CharacterId = characterId,
                        ContainerKind = (int)CharacterContainerKind.CurrentRefine,
                        SlotIndex = 0,
                        ItemId = ToDbInt64(character.CurrentRefine.UniqueID, "item_id"),
                    });
                }

                if (character.Heroes != null)
                {
                    for (var i = 0; i < character.Heroes.Length; i++)
                    {
                        var hero = character.Heroes[i];
                        if (hero == null) continue;
                        CaptureCharacter(hero);
                    }
                }
            }

            lock (Envir.AccountLock)
            {
                for (var i = 0; i < envir.AccountList.Count; i++)
                {
                    var account = envir.AccountList[i];
                    if (account == null) continue;

                    var accountId = (long)account.Index;
                    var storage = account.Storage ?? Array.Empty<UserItem>();

                    storageRows.Add(new AccountStorageRow
                    {
                        AccountId = accountId,
                        SlotCount = storage.Length,
                        HasExpandedStorage = account.HasExpandedStorage ? 1 : 0,
                        ExpandedStorageExpiryUtcMs = ToUtcMs(account.ExpandedStorageExpiryDate),
                    });

                    for (var j = 0; j < storage.Length; j++)
                    {
                        var item = storage[j];
                        if (item == null) continue;
                        if (item.UniqueID == 0) continue;

                        storageSlotRows.Add(new AccountStorageSlotRow
                        {
                            AccountId = accountId,
                            SlotIndex = j,
                            ItemId = ToDbInt64(item.UniqueID, "item_id"),
                        });
                    }

                    if (account.Characters != null)
                    {
                        for (var j = 0; j < account.Characters.Count; j++)
                            CaptureCharacter(account.Characters[j]);
                    }
                }

                for (var i = 0; i < envir.HeroList.Count; i++)
                    CaptureCharacter(envir.HeroList[i]);
            }

            accountStorage = storageRows;
            accountStorageSlots = storageSlotRows;
            characterContainers = containerRows;
            characterContainerSlots = containerSlotRows;
        }

        private static void ReplaceContainers(
            SqlSession session,
            IReadOnlyList<AccountStorageRow> accountStorage,
            IReadOnlyList<AccountStorageSlotRow> accountStorageSlots,
            IReadOnlyList<CharacterContainerRow> characterContainers,
            IReadOnlyList<CharacterContainerSlotRow> characterContainerSlots,
            long saveEpochUtcMs = 0)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));

            var nowMs = saveEpochUtcMs > 0 ? saveEpochUtcMs : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            var batchSize = Settings.SaveBatchSize <= 0 ? 2000 : Settings.SaveBatchSize;

            if (accountStorage != null && accountStorage.Count > 0)
            {
                var sql = session.Dialect.BuildUpsert(
                    tableName: "account_storage",
                    insertColumns: ["account_id", "slot_count", "has_expanded_storage", "expanded_storage_expiry_utc_ms", "updated_utc_ms"],
                    keyColumns: ["account_id"],
                    updateColumns: ["slot_count", "has_expanded_storage", "expanded_storage_expiry_utc_ms", "updated_utc_ms"]);

                for (var offset = 0; offset < accountStorage.Count; offset += batchSize)
                {
                    var take = Math.Min(batchSize, accountStorage.Count - offset);
                    var batch = new List<object>(take);

                    for (var i = 0; i < take; i++)
                    {
                        var row = accountStorage[offset + i];
                        if (row == null) continue;

                        batch.Add(new
                        {
                            account_id = row.AccountId,
                            slot_count = row.SlotCount,
                            has_expanded_storage = row.HasExpandedStorage,
                            expanded_storage_expiry_utc_ms = row.ExpandedStorageExpiryUtcMs,
                            updated_utc_ms = nowMs,
                        });
                    }

                    if (batch.Count > 0)
                        session.Execute(sql, batch);
                }
            }

            if (accountStorageSlots != null && accountStorageSlots.Count > 0)
            {
                var sql = session.Dialect.BuildUpsert(
                    tableName: "account_storage_slots",
                    insertColumns: ["account_id", "slot_index", "item_id", "updated_utc_ms"],
                    keyColumns: ["account_id", "slot_index"],
                    updateColumns: ["item_id", "updated_utc_ms"]);

                for (var offset = 0; offset < accountStorageSlots.Count; offset += batchSize)
                {
                    var take = Math.Min(batchSize, accountStorageSlots.Count - offset);
                    var batch = new List<object>(take);

                    for (var i = 0; i < take; i++)
                    {
                        var row = accountStorageSlots[offset + i];
                        if (row == null) continue;

                        batch.Add(new
                        {
                            account_id = row.AccountId,
                            slot_index = row.SlotIndex,
                            item_id = row.ItemId,
                            updated_utc_ms = nowMs,
                        });
                    }

                    if (batch.Count > 0)
                        session.Execute(sql, batch);
                }
            }

            if (characterContainers != null && characterContainers.Count > 0)
            {
                var sql = session.Dialect.BuildUpsert(
                    tableName: "character_containers",
                    insertColumns: ["character_id", "container_kind", "slot_count", "updated_utc_ms"],
                    keyColumns: ["character_id", "container_kind"],
                    updateColumns: ["slot_count", "updated_utc_ms"]);

                for (var offset = 0; offset < characterContainers.Count; offset += batchSize)
                {
                    var take = Math.Min(batchSize, characterContainers.Count - offset);
                    var batch = new List<object>(take);

                    for (var i = 0; i < take; i++)
                    {
                        var row = characterContainers[offset + i];
                        if (row == null) continue;

                        batch.Add(new
                        {
                            character_id = row.CharacterId,
                            container_kind = row.ContainerKind,
                            slot_count = row.SlotCount,
                            updated_utc_ms = nowMs,
                        });
                    }

                    if (batch.Count > 0)
                        session.Execute(sql, batch);
                }
            }

            if (characterContainerSlots != null && characterContainerSlots.Count > 0)
            {
                var sql = session.Dialect.BuildUpsert(
                    tableName: "character_container_slots",
                    insertColumns: ["character_id", "container_kind", "slot_index", "item_id", "updated_utc_ms"],
                    keyColumns: ["character_id", "container_kind", "slot_index"],
                    updateColumns: ["item_id", "updated_utc_ms"]);

                for (var offset = 0; offset < characterContainerSlots.Count; offset += batchSize)
                {
                    var take = Math.Min(batchSize, characterContainerSlots.Count - offset);
                    var batch = new List<object>(take);

                    for (var i = 0; i < take; i++)
                    {
                        var row = characterContainerSlots[offset + i];
                        if (row == null) continue;

                        batch.Add(new
                        {
                            character_id = row.CharacterId,
                            container_kind = row.ContainerKind,
                            slot_index = row.SlotIndex,
                            item_id = row.ItemId,
                            updated_utc_ms = nowMs,
                        });
                    }

                    if (batch.Count > 0)
                        session.Execute(sql, batch);
                }
            }

            // 清理本轮未触达的旧数据（等价于“全量替换”，但避免先删再插的窗口期）。
            session.Execute("DELETE FROM account_storage_slots WHERE updated_utc_ms <> @nowMs", new { nowMs });
            session.Execute("DELETE FROM account_storage WHERE updated_utc_ms <> @nowMs", new { nowMs });
            session.Execute("DELETE FROM character_container_slots WHERE updated_utc_ms <> @nowMs", new { nowMs });
            session.Execute("DELETE FROM character_containers WHERE updated_utc_ms <> @nowMs", new { nowMs });
        }

        private static IReadOnlyList<AccountStorageRow> LoadAccountStorageRows(SqlSession session)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));

            return session.Query<AccountStorageRow>(
                "SELECT " +
                "account_id AS AccountId, " +
                "slot_count AS SlotCount, " +
                "has_expanded_storage AS HasExpandedStorage, " +
                "expanded_storage_expiry_utc_ms AS ExpandedStorageExpiryUtcMs " +
                "FROM account_storage");
        }

        private static IReadOnlyList<AccountStorageSlotRow> LoadAccountStorageSlotRows(SqlSession session)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));

            return session.Query<AccountStorageSlotRow>(
                "SELECT account_id AS AccountId, slot_index AS SlotIndex, item_id AS ItemId FROM account_storage_slots");
        }

        private static IReadOnlyList<CharacterContainerRow> LoadCharacterContainerRows(SqlSession session)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));

            return session.Query<CharacterContainerRow>(
                "SELECT character_id AS CharacterId, container_kind AS ContainerKind, slot_count AS SlotCount FROM character_containers");
        }

        private static IReadOnlyList<CharacterContainerSlotRow> LoadCharacterContainerSlotRows(SqlSession session)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));

            return session.Query<CharacterContainerSlotRow>(
                "SELECT character_id AS CharacterId, container_kind AS ContainerKind, slot_index AS SlotIndex, item_id AS ItemId FROM character_container_slots");
        }

        private static void ApplyContainers(
            Envir envir,
            IReadOnlyDictionary<long, UserItem> itemsById,
            IReadOnlyList<AccountStorageRow> accountStorage,
            IReadOnlyList<AccountStorageSlotRow> accountStorageSlots,
            IReadOnlyList<CharacterContainerRow> characterContainers,
            IReadOnlyList<CharacterContainerSlotRow> characterContainerSlots)
        {
            if (envir == null) throw new ArgumentNullException(nameof(envir));

            itemsById ??= new Dictionary<long, UserItem>();

            var accountById = new Dictionary<long, AccountInfo>();
            var characterById = new Dictionary<long, CharacterInfo>();

            lock (Envir.AccountLock)
            {
                for (var i = 0; i < envir.AccountList.Count; i++)
                {
                    var account = envir.AccountList[i];
                    if (account == null) continue;
                    accountById[account.Index] = account;
                }

                for (var i = 0; i < envir.CharacterList.Count; i++)
                {
                    var character = envir.CharacterList[i];
                    if (character == null) continue;
                    characterById[character.Index] = character;
                }

                for (var i = 0; i < envir.HeroList.Count; i++)
                {
                    var hero = envir.HeroList[i];
                    if (hero == null) continue;
                    characterById[hero.Index] = hero;
                }

                if (accountStorage != null)
                {
                    for (var i = 0; i < accountStorage.Count; i++)
                    {
                        var row = accountStorage[i];
                        if (row == null) continue;

                        if (!accountById.TryGetValue(row.AccountId, out var account) || account == null)
                            continue;

                        account.HasExpandedStorage = row.HasExpandedStorage != 0;
                        account.ExpandedStorageExpiryDate = FromUtcMsToLocal(row.ExpandedStorageExpiryUtcMs);

                        var slotCount = Math.Max(0, row.SlotCount);
                        account.Storage = new UserItem[slotCount];
                    }
                }

                if (accountStorageSlots != null)
                {
                    for (var i = 0; i < accountStorageSlots.Count; i++)
                    {
                        var row = accountStorageSlots[i];
                        if (row == null) continue;

                        if (!accountById.TryGetValue(row.AccountId, out var account) || account?.Storage == null)
                            continue;

                        if (row.SlotIndex < 0 || row.SlotIndex >= account.Storage.Length)
                            continue;

                        if (!itemsById.TryGetValue(row.ItemId, out var item) || item == null)
                            continue;

                        account.Storage[row.SlotIndex] = item;
                    }
                }

                if (characterContainers != null)
                {
                    for (var i = 0; i < characterContainers.Count; i++)
                    {
                        var row = characterContainers[i];
                        if (row == null) continue;

                        if (!characterById.TryGetValue(row.CharacterId, out var character) || character == null)
                            continue;

                        var slotCount = Math.Max(0, row.SlotCount);

                        switch ((CharacterContainerKind)row.ContainerKind)
                        {
                            case CharacterContainerKind.Inventory:
                                character.Inventory = new UserItem[slotCount];
                                break;
                            case CharacterContainerKind.Equipment:
                                character.Equipment = new UserItem[slotCount];
                                break;
                            case CharacterContainerKind.QuestInventory:
                                character.QuestInventory = new UserItem[slotCount];
                                break;
                            case CharacterContainerKind.CurrentRefine:
                                character.CurrentRefine = null;
                                break;
                        }
                    }
                }

                if (characterContainerSlots != null)
                {
                    for (var i = 0; i < characterContainerSlots.Count; i++)
                    {
                        var row = characterContainerSlots[i];
                        if (row == null) continue;

                        if (!characterById.TryGetValue(row.CharacterId, out var character) || character == null)
                            continue;

                        if (!itemsById.TryGetValue(row.ItemId, out var item) || item == null)
                            continue;

                        switch ((CharacterContainerKind)row.ContainerKind)
                        {
                            case CharacterContainerKind.Inventory:
                                if (character.Inventory == null) break;
                                if (row.SlotIndex < 0 || row.SlotIndex >= character.Inventory.Length) break;
                                character.Inventory[row.SlotIndex] = item;
                                break;
                            case CharacterContainerKind.Equipment:
                                if (character.Equipment == null) break;
                                if (row.SlotIndex < 0 || row.SlotIndex >= character.Equipment.Length) break;
                                character.Equipment[row.SlotIndex] = item;
                                break;
                            case CharacterContainerKind.QuestInventory:
                                if (character.QuestInventory == null) break;
                                if (row.SlotIndex < 0 || row.SlotIndex >= character.QuestInventory.Length) break;
                                character.QuestInventory[row.SlotIndex] = item;
                                break;
                            case CharacterContainerKind.CurrentRefine:
                                if (row.SlotIndex != 0) break;
                                character.CurrentRefine = item;
                                break;
                        }
                    }
                }
            }
        }

        private static IReadOnlyList<AuctionRow> CaptureAuctions(Envir envir)
        {
            if (envir == null) throw new ArgumentNullException(nameof(envir));

            lock (Envir.AccountLock)
            {
                if (envir.Auctions == null || envir.Auctions.Count == 0)
                    return Array.Empty<AuctionRow>();

                var result = new List<AuctionRow>(envir.Auctions.Count);

                foreach (var auction in envir.Auctions)
                {
                    if (auction == null) continue;
                    if (auction.AuctionID == 0) continue;
                    if (auction.Item == null || auction.Item.UniqueID == 0) continue;

                    result.Add(new AuctionRow
                    {
                        AuctionId = ToDbInt64(auction.AuctionID, "auction_id"),
                        ItemId = ToDbInt64(auction.Item.UniqueID, "item_id"),
                        ConsignmentUtcMs = ToUtcMs(auction.ConsignmentDate),
                        Price = auction.Price,
                        CurrentBid = auction.CurrentBid,
                        SellerCharacterId = auction.SellerIndex,
                        CurrentBuyerCharacterId = auction.CurrentBuyerIndex,
                        Expired = auction.Expired ? 1 : 0,
                        Sold = auction.Sold ? 1 : 0,
                        ItemType = (int)auction.ItemType,
                    });
                }

                return result;
            }
        }

        private static void CaptureMails(
            Envir envir,
            out IReadOnlyList<MailRow> mails,
            out IReadOnlyList<MailItemRow> mailItems)
        {
            if (envir == null) throw new ArgumentNullException(nameof(envir));

            lock (Envir.AccountLock)
            {
                var mailRows = new List<MailRow>();
                var mailItemRows = new List<MailItemRow>();

                var visitedCharacters = new HashSet<int>();

                void VisitCharacter(CharacterInfo character)
                {
                    if (character == null) return;
                    if (!visitedCharacters.Add(character.Index)) return;

                    if (character.Mail == null || character.Mail.Count == 0)
                        return;

                    for (var i = 0; i < character.Mail.Count; i++)
                    {
                        var mail = character.Mail[i];
                        if (mail == null) continue;
                        if (mail.MailID == 0) continue;

                        var recipientId = mail.RecipientIndex > 0 ? mail.RecipientIndex : character.Index;

                        mailRows.Add(new MailRow
                        {
                            MailId = ToDbInt64(mail.MailID, "mail_id"),
                            SenderName = mail.Sender ?? string.Empty,
                            RecipientCharacterId = recipientId,
                            Message = mail.Message ?? string.Empty,
                            Gold = mail.Gold,
                            DateSentUtcMs = ToUtcMs(mail.DateSent),
                            DateOpenedUtcMs = ToUtcMs(mail.DateOpened),
                            Locked = mail.Locked ? 1 : 0,
                            Collected = mail.Collected ? 1 : 0,
                            CanReply = mail.CanReply ? 1 : 0,
                        });

                        var items = mail.Items ?? new List<UserItem>();
                        for (var slotIndex = 0; slotIndex < items.Count; slotIndex++)
                        {
                            var item = items[slotIndex];
                            if (item == null) continue;
                            if (item.UniqueID == 0) continue;

                            mailItemRows.Add(new MailItemRow
                            {
                                MailId = ToDbInt64(mail.MailID, "mail_id"),
                                SlotIndex = slotIndex,
                                ItemId = ToDbInt64(item.UniqueID, "item_id"),
                            });
                        }
                    }
                }

                if (envir.CharacterList != null)
                {
                    for (var i = 0; i < envir.CharacterList.Count; i++)
                        VisitCharacter(envir.CharacterList[i]);
                }

                if (envir.HeroList != null)
                {
                    for (var i = 0; i < envir.HeroList.Count; i++)
                        VisitCharacter(envir.HeroList[i]);
                }

                mails = mailRows;
                mailItems = mailItemRows;
            }
        }

        private static IReadOnlyList<GameshopLogRow> CaptureGameshopLog(Envir envir)
        {
            if (envir == null) throw new ArgumentNullException(nameof(envir));

            lock (Envir.AccountLock)
            {
                if (envir.GameshopLog == null || envir.GameshopLog.Count == 0)
                    return Array.Empty<GameshopLogRow>();

                var result = new List<GameshopLogRow>(envir.GameshopLog.Count);

                foreach (var pair in envir.GameshopLog)
                {
                    result.Add(new GameshopLogRow
                    {
                        ItemIndex = pair.Key,
                        Count = pair.Value,
                    });
                }

                result.Sort((a, b) => a.ItemIndex.CompareTo(b.ItemIndex));
                return result;
            }
        }

        private static IReadOnlyList<RespawnSaveRow> CaptureRespawnSaves(Envir envir)
        {
            if (envir == null) throw new ArgumentNullException(nameof(envir));

            lock (Envir.AccountLock)
            {
                if (envir.SavedSpawns == null || envir.SavedSpawns.Count == 0)
                    return Array.Empty<RespawnSaveRow>();

                var result = new List<RespawnSaveRow>(envir.SavedSpawns.Count);

                for (var i = 0; i < envir.SavedSpawns.Count; i++)
                {
                    var spawn = envir.SavedSpawns[i];
                    if (spawn?.Info == null) continue;

                    var desiredCount = spawn.Info.Count * envir.SpawnMultiplier;

                    result.Add(new RespawnSaveRow
                    {
                        RespawnIndex = spawn.Info.RespawnIndex,
                        NextSpawnTick = ToDbInt64(spawn.NextSpawnTick, "next_spawn_tick"),
                        Spawned = spawn.Count >= desiredCount ? 1 : 0,
                    });
                }

                result.Sort((a, b) => a.RespawnIndex.CompareTo(b.RespawnIndex));
                return result;
            }
        }

        private static IReadOnlyList<CharacterMagicRow> CaptureCharacterMagics(Envir envir)
        {
            if (envir == null) throw new ArgumentNullException(nameof(envir));

            lock (Envir.AccountLock)
            {
                var result = new List<CharacterMagicRow>();

                VisitAllPersistentCharacters(envir, character =>
                {
                    var magics = character?.Magics;
                    if (magics == null || magics.Count == 0) return;

                    for (var magicIndex = 0; magicIndex < magics.Count; magicIndex++)
                    {
                        var magic = magics[magicIndex];
                        if (magic == null) continue;

                        result.Add(new CharacterMagicRow
                        {
                            CharacterId = character.Index,
                            Spell = (int)magic.Spell,
                            MagicLevel = magic.Level,
                            MagicKey = magic.Key,
                            Experience = magic.Experience,
                            IsTempSpell = magic.IsTempSpell ? 1 : 0,
                            CastTime = magic.CastTime,
                        });
                    }
                });

                result.Sort((left, right) =>
                {
                    var compare = left.CharacterId.CompareTo(right.CharacterId);
                    return compare != 0 ? compare : left.Spell.CompareTo(right.Spell);
                });

                return result;
            }
        }

        private static IReadOnlyList<CharacterCompletedQuestRow> CaptureCharacterCompletedQuests(Envir envir)
        {
            if (envir == null) throw new ArgumentNullException(nameof(envir));

            lock (Envir.AccountLock)
            {
                var result = new List<CharacterCompletedQuestRow>();

                VisitAllPersistentCharacters(envir, character =>
                {
                    var quests = character?.CompletedQuests;
                    if (quests == null || quests.Count == 0) return;

                    for (var questIndex = 0; questIndex < quests.Count; questIndex++)
                    {
                        var questId = quests[questIndex];
                        if (questId <= 0) continue;

                        result.Add(new CharacterCompletedQuestRow
                        {
                            CharacterId = character.Index,
                            QuestId = questId,
                        });
                    }
                });

                result.Sort((left, right) =>
                {
                    var compare = left.CharacterId.CompareTo(right.CharacterId);
                    return compare != 0 ? compare : left.QuestId.CompareTo(right.QuestId);
                });

                return result;
            }
        }

        private static IReadOnlyList<CharacterFlagRow> CaptureCharacterFlags(Envir envir)
        {
            if (envir == null) throw new ArgumentNullException(nameof(envir));

            lock (Envir.AccountLock)
            {
                var result = new List<CharacterFlagRow>();

                VisitAllPersistentCharacters(envir, character =>
                {
                    var flags = character?.Flags;
                    if (flags == null || flags.Length == 0) return;

                    for (var flagIndex = 0; flagIndex < flags.Length; flagIndex++)
                    {
                        if (!flags[flagIndex]) continue;

                        result.Add(new CharacterFlagRow
                        {
                            CharacterId = character.Index,
                            FlagIndex = flagIndex,
                            FlagValue = 1,
                        });
                    }
                });

                result.Sort((left, right) =>
                {
                    var compare = left.CharacterId.CompareTo(right.CharacterId);
                    return compare != 0 ? compare : left.FlagIndex.CompareTo(right.FlagIndex);
                });

                return result;
            }
        }

        private static IReadOnlyList<CharacterGameshopPurchaseRow> CaptureCharacterGameshopPurchases(Envir envir)
        {
            if (envir == null) throw new ArgumentNullException(nameof(envir));

            lock (Envir.AccountLock)
            {
                var result = new List<CharacterGameshopPurchaseRow>();

                VisitAllPersistentCharacters(envir, character =>
                {
                    var purchases = character?.GSpurchases;
                    if (purchases == null || purchases.Count == 0) return;

                    foreach (var pair in purchases)
                    {
                        result.Add(new CharacterGameshopPurchaseRow
                        {
                            CharacterId = character.Index,
                            ItemIndex = pair.Key,
                            PurchaseCount = pair.Value,
                        });
                    }
                });

                result.Sort((left, right) =>
                {
                    var compare = left.CharacterId.CompareTo(right.CharacterId);
                    return compare != 0 ? compare : left.ItemIndex.CompareTo(right.ItemIndex);
                });

                return result;
            }
        }

        private static void CaptureCurrentQuests(
            Envir envir,
            out IReadOnlyList<CurrentQuestRow> currentQuests,
            out IReadOnlyList<CurrentQuestKillTaskRow> currentQuestKillTasks,
            out IReadOnlyList<CurrentQuestItemTaskRow> currentQuestItemTasks,
            out IReadOnlyList<CurrentQuestFlagTaskRow> currentQuestFlagTasks)
        {
            if (envir == null) throw new ArgumentNullException(nameof(envir));

            lock (Envir.AccountLock)
            {
                var questRows = new List<CurrentQuestRow>();
                var killTaskRows = new List<CurrentQuestKillTaskRow>();
                var itemTaskRows = new List<CurrentQuestItemTaskRow>();
                var flagTaskRows = new List<CurrentQuestFlagTaskRow>();

                VisitAllPersistentCharacters(envir, character =>
                {
                    var quests = character?.CurrentQuests;
                    if (quests == null || quests.Count == 0) return;

                    var seenQuestIds = new HashSet<int>();
                    var duplicateQuestIds = new HashSet<int>();
                    var persistedSlotIndex = 0;

                    for (var slotIndex = 0; slotIndex < quests.Count; slotIndex++)
                    {
                        var quest = quests[slotIndex];
                        if (quest == null) continue;
                        if (quest.Index <= 0) continue;

                        if (!seenQuestIds.Add(quest.Index))
                        {
                            duplicateQuestIds.Add(quest.Index);
                            continue;
                        }

                        questRows.Add(new CurrentQuestRow
                        {
                            CharacterId = character.Index,
                            SlotIndex = persistedSlotIndex++,
                            QuestId = quest.Index,
                            StartUtcMs = ToUtcMs(quest.StartDateTime),
                            EndUtcMs = ToUtcMs(quest.EndDateTime),
                        });

                        if (quest.KillTaskCount != null)
                        {
                            for (var taskIndex = 0; taskIndex < quest.KillTaskCount.Count; taskIndex++)
                            {
                                var task = quest.KillTaskCount[taskIndex];
                                if (task == null) continue;

                                killTaskRows.Add(new CurrentQuestKillTaskRow
                                {
                                    CharacterId = character.Index,
                                    QuestId = quest.Index,
                                    MonsterId = task.MonsterID,
                                    TaskCount = task.Count,
                                });
                            }
                        }

                        if (quest.ItemTaskCount != null)
                        {
                            for (var taskIndex = 0; taskIndex < quest.ItemTaskCount.Count; taskIndex++)
                            {
                                var task = quest.ItemTaskCount[taskIndex];
                                if (task == null) continue;

                                itemTaskRows.Add(new CurrentQuestItemTaskRow
                                {
                                    CharacterId = character.Index,
                                    QuestId = quest.Index,
                                    ItemId = task.ItemID,
                                    TaskCount = task.Count,
                                });
                            }
                        }

                        if (quest.FlagTaskSet != null)
                        {
                            for (var taskIndex = 0; taskIndex < quest.FlagTaskSet.Count; taskIndex++)
                            {
                                var task = quest.FlagTaskSet[taskIndex];
                                if (task == null) continue;

                                flagTaskRows.Add(new CurrentQuestFlagTaskRow
                                {
                                    CharacterId = character.Index,
                                    QuestId = quest.Index,
                                    FlagNumber = task.Number,
                                    FlagState = task.State ? 1 : 0,
                                });
                            }
                        }
                    }

                    if (duplicateQuestIds.Count > 0)
                    {
                        var characterName = character.Name ?? character.Index.ToString();
                        MessageQueue.Instance.EnqueueDebugging($"[SQL] CurrentQuests 去重：Character={characterName}({character.Index}) DuplicateQuestIds={string.Join(",", duplicateQuestIds.OrderBy(x => x))}");
                    }
                });

                questRows.Sort((left, right) =>
                {
                    var compare = left.CharacterId.CompareTo(right.CharacterId);
                    return compare != 0 ? compare : left.SlotIndex.CompareTo(right.SlotIndex);
                });

                killTaskRows.Sort((left, right) =>
                {
                    var compare = left.CharacterId.CompareTo(right.CharacterId);
                    if (compare != 0) return compare;
                    compare = left.QuestId.CompareTo(right.QuestId);
                    return compare != 0 ? compare : left.MonsterId.CompareTo(right.MonsterId);
                });

                itemTaskRows.Sort((left, right) =>
                {
                    var compare = left.CharacterId.CompareTo(right.CharacterId);
                    if (compare != 0) return compare;
                    compare = left.QuestId.CompareTo(right.QuestId);
                    return compare != 0 ? compare : left.ItemId.CompareTo(right.ItemId);
                });

                flagTaskRows.Sort((left, right) =>
                {
                    var compare = left.CharacterId.CompareTo(right.CharacterId);
                    if (compare != 0) return compare;
                    compare = left.QuestId.CompareTo(right.QuestId);
                    return compare != 0 ? compare : left.FlagNumber.CompareTo(right.FlagNumber);
                });

                currentQuests = questRows;
                currentQuestKillTasks = killTaskRows;
                currentQuestItemTasks = itemTaskRows;
                currentQuestFlagTasks = flagTaskRows;
            }
        }

        private static IReadOnlyList<CharacterPetRow> CaptureCharacterPets(Envir envir)
        {
            if (envir == null) throw new ArgumentNullException(nameof(envir));

            lock (Envir.AccountLock)
            {
                var result = new List<CharacterPetRow>();

                VisitAllPersistentCharacters(envir, character =>
                {
                    if (character?.Pets == null || character.Pets.Count == 0) return;

                    for (var listIndex = 0; listIndex < character.Pets.Count; listIndex++)
                    {
                        var pet = character.Pets[listIndex];
                        if (pet == null) continue;

                        result.Add(new CharacterPetRow
                        {
                            CharacterId = character.Index,
                            ListIndex = listIndex,
                            MonsterId = pet.MonsterIndex,
                            Hp = pet.HP,
                            Experience = pet.Experience,
                            PetLevel = pet.Level,
                            MaxPetLevel = pet.MaxPetLevel,
                        });
                    }
                });

                result.Sort((left, right) =>
                {
                    var compare = left.CharacterId.CompareTo(right.CharacterId);
                    return compare != 0 ? compare : left.ListIndex.CompareTo(right.ListIndex);
                });

                return result;
            }
        }

        private static IReadOnlyList<CharacterFriendRow> CaptureCharacterFriends(Envir envir)
        {
            if (envir == null) throw new ArgumentNullException(nameof(envir));

            lock (Envir.AccountLock)
            {
                var result = new List<CharacterFriendRow>();

                VisitAllPersistentCharacters(envir, character =>
                {
                    if (character?.Friends == null || character.Friends.Count == 0) return;

                    for (var listIndex = 0; listIndex < character.Friends.Count; listIndex++)
                    {
                        var friend = character.Friends[listIndex];
                        if (friend == null) continue;
                        if (friend.Index <= 0) continue;

                        result.Add(new CharacterFriendRow
                        {
                            CharacterId = character.Index,
                            ListIndex = listIndex,
                            FriendCharacterId = friend.Index,
                            Blocked = friend.Blocked ? 1 : 0,
                            Memo = friend.Memo ?? string.Empty,
                        });
                    }
                });

                result.Sort((left, right) =>
                {
                    var compare = left.CharacterId.CompareTo(right.CharacterId);
                    return compare != 0 ? compare : left.ListIndex.CompareTo(right.ListIndex);
                });

                return result;
            }
        }

        private static IReadOnlyList<CharacterRentedItemRow> CaptureCharacterRentedItems(Envir envir)
        {
            if (envir == null) throw new ArgumentNullException(nameof(envir));

            lock (Envir.AccountLock)
            {
                var result = new List<CharacterRentedItemRow>();

                VisitAllPersistentCharacters(envir, character =>
                {
                    if (character?.RentedItems == null || character.RentedItems.Count == 0) return;

                    for (var listIndex = 0; listIndex < character.RentedItems.Count; listIndex++)
                    {
                        var rentedItem = character.RentedItems[listIndex];
                        if (rentedItem == null) continue;

                        result.Add(new CharacterRentedItemRow
                        {
                            CharacterId = character.Index,
                            ListIndex = listIndex,
                            ItemId = ToDbInt64(rentedItem.ItemId, "rented_item_id"),
                            ItemName = rentedItem.ItemName ?? string.Empty,
                            RentingPlayerName = rentedItem.RentingPlayerName ?? string.Empty,
                            ItemReturnUtcMs = ToUtcMs(rentedItem.ItemReturnDate),
                        });
                    }
                });

                result.Sort((left, right) =>
                {
                    var compare = left.CharacterId.CompareTo(right.CharacterId);
                    return compare != 0 ? compare : left.ListIndex.CompareTo(right.ListIndex);
                });

                return result;
            }
        }

        private static IReadOnlyList<CharacterIntelligentCreatureRow> CaptureCharacterIntelligentCreatures(Envir envir)
        {
            if (envir == null) throw new ArgumentNullException(nameof(envir));

            lock (Envir.AccountLock)
            {
                var result = new List<CharacterIntelligentCreatureRow>();

                VisitAllPersistentCharacters(envir, character =>
                {
                    if (character?.IntelligentCreatures == null || character.IntelligentCreatures.Count == 0) return;

                    for (var listIndex = 0; listIndex < character.IntelligentCreatures.Count; listIndex++)
                    {
                        var creature = character.IntelligentCreatures[listIndex];
                        if (creature == null) continue;

                        var filter = creature.Filter ?? new IntelligentCreatureItemFilter();

                        result.Add(new CharacterIntelligentCreatureRow
                        {
                            CharacterId = character.Index,
                            SlotIndex = creature.SlotIndex,
                            PetType = (int)creature.PetType,
                            CustomName = creature.CustomName ?? string.Empty,
                            Fullness = creature.Fullness,
                            ExpireUtcMs = ToUtcMs(creature.Expire),
                            BlackstoneTime = creature.BlackstoneTime,
                            PickupMode = (int)creature.petMode,
                            FilterPickupAll = filter.PetPickupAll ? 1 : 0,
                            FilterPickupGold = filter.PetPickupGold ? 1 : 0,
                            FilterPickupWeapons = filter.PetPickupWeapons ? 1 : 0,
                            FilterPickupArmours = filter.PetPickupArmours ? 1 : 0,
                            FilterPickupHelmets = filter.PetPickupHelmets ? 1 : 0,
                            FilterPickupBoots = filter.PetPickupBoots ? 1 : 0,
                            FilterPickupBelts = filter.PetPickupBelts ? 1 : 0,
                            FilterPickupAccessories = filter.PetPickupAccessories ? 1 : 0,
                            FilterPickupOthers = filter.PetPickupOthers ? 1 : 0,
                            FilterPickupGrade = (int)filter.PickupGrade,
                            MaintainFoodTime = creature.MaintainFoodTime,
                        });
                    }
                });

                result.Sort((left, right) =>
                {
                    var compare = left.CharacterId.CompareTo(right.CharacterId);
                    return compare != 0 ? compare : left.SlotIndex.CompareTo(right.SlotIndex);
                });

                return result;
            }
        }

        private static IReadOnlyList<HeroDetailRow> CaptureHeroDetails(Envir envir)
        {
            if (envir == null) throw new ArgumentNullException(nameof(envir));

            lock (Envir.AccountLock)
            {
                if (envir.HeroList == null || envir.HeroList.Count == 0)
                    return Array.Empty<HeroDetailRow>();

                var result = new List<HeroDetailRow>(envir.HeroList.Count);

                for (var index = 0; index < envir.HeroList.Count; index++)
                {
                    var hero = envir.HeroList[index];
                    if (hero == null) continue;

                    result.Add(new HeroDetailRow
                    {
                        CharacterId = hero.Index,
                        AutoPot = hero.AutoPot ? 1 : 0,
                        Grade = hero.Grade,
                        HpItemIndex = hero.HPItemIndex,
                        MpItemIndex = hero.MPItemIndex,
                        AutoHpPercent = hero.AutoHPPercent,
                        AutoMpPercent = hero.AutoMPPercent,
                        SealCount = hero.SealCount,
                    });
                }

                result.Sort((left, right) => left.CharacterId.CompareTo(right.CharacterId));
                return result;
            }
        }

        private static IReadOnlyList<CharacterHeroSlotRow> CaptureCharacterHeroSlots(Envir envir)
        {
            if (envir == null) throw new ArgumentNullException(nameof(envir));

            lock (Envir.AccountLock)
            {
                var result = new List<CharacterHeroSlotRow>();

                if (envir.CharacterList == null || envir.CharacterList.Count == 0)
                    return result;

                for (var characterIndex = 0; characterIndex < envir.CharacterList.Count; characterIndex++)
                {
                    var character = envir.CharacterList[characterIndex];
                    if (character?.Heroes == null) continue;

                    for (var slotIndex = 0; slotIndex < character.Heroes.Length; slotIndex++)
                    {
                        var hero = character.Heroes[slotIndex];
                        if (hero == null) continue;

                        result.Add(new CharacterHeroSlotRow
                        {
                            CharacterId = character.Index,
                            SlotIndex = slotIndex,
                            HeroCharacterId = hero.Index,
                        });
                    }
                }

                result.Sort((left, right) =>
                {
                    var compare = left.CharacterId.CompareTo(right.CharacterId);
                    return compare != 0 ? compare : left.SlotIndex.CompareTo(right.SlotIndex);
                });

                return result;
            }
        }

        private static IReadOnlyList<CharacterBuffRow> CaptureCharacterBuffs(
            Envir envir,
            out IReadOnlyList<CharacterBuffStatRow> statRows,
            out IReadOnlyList<CharacterBuffValueRow> valueRows,
            out IReadOnlyList<CharacterBuffDataRow> dataRows)
        {
            if (envir == null) throw new ArgumentNullException(nameof(envir));

            lock (Envir.AccountLock)
            {
                var result = new List<CharacterBuffRow>();
                var stats = new List<CharacterBuffStatRow>();
                var values = new List<CharacterBuffValueRow>();
                var data = new List<CharacterBuffDataRow>();

                VisitAllPersistentCharacters(envir, character =>
                {
                    if (character?.Buffs == null || character.Buffs.Count == 0) return;

                    for (var listIndex = 0; listIndex < character.Buffs.Count; listIndex++)
                    {
                        var buff = character.Buffs[listIndex];
                        if (buff == null) continue;

                        result.Add(new CharacterBuffRow
                        {
                            CharacterId = character.Index,
                            ListIndex = listIndex,
                            BuffType = (int)buff.Type,
                            ObjectId = buff.ObjectID,
                            ExpireTime = buff.ExpireTime,
                            LastTime = buff.LastTime,
                            NextTime = buff.NextTime,
                            FlagForRemoval = buff.FlagForRemoval ? 1 : 0,
                            Paused = buff.Paused ? 1 : 0,
                        });

                        foreach (var stat in buff.Stats?.Values ?? new SortedDictionary<Stat, int>())
                            stats.Add(new CharacterBuffStatRow { CharacterId = character.Index, ListIndex = listIndex, StatId = (int)stat.Key, StatValue = stat.Value });
                        for (var valueIndex = 0; valueIndex < (buff.Values?.Length ?? 0); valueIndex++)
                            values.Add(new CharacterBuffValueRow { CharacterId = character.Index, ListIndex = listIndex, ValueIndex = valueIndex, ValueType = "int64", IntegerValue = buff.Values[valueIndex] });
                        foreach (var pair in buff.GetDataSnapshot())
                            data.Add(ToBuffDataRow(character.Index, listIndex, pair.Key, pair.Value));
                    }
                });

                result.Sort((left, right) =>
                {
                    var compare = left.CharacterId.CompareTo(right.CharacterId);
                    return compare != 0 ? compare : left.ListIndex.CompareTo(right.ListIndex);
                });

                statRows = stats;
                valueRows = values;
                dataRows = data;
                return result;
            }
        }

        private static CharacterBuffDataRow ToBuffDataRow(long characterId, int listIndex, string key, object value)
        {
            return value switch
            {
                bool boolean => new CharacterBuffDataRow { CharacterId = characterId, ListIndex = listIndex, DataKey = key, DataType = "bool", IntegerValue = boolean ? 1 : 0 },
                byte number => new CharacterBuffDataRow { CharacterId = characterId, ListIndex = listIndex, DataKey = key, DataType = "int64", IntegerValue = number },
                short number => new CharacterBuffDataRow { CharacterId = characterId, ListIndex = listIndex, DataKey = key, DataType = "int64", IntegerValue = number },
                int number => new CharacterBuffDataRow { CharacterId = characterId, ListIndex = listIndex, DataKey = key, DataType = "int64", IntegerValue = number },
                long number => new CharacterBuffDataRow { CharacterId = characterId, ListIndex = listIndex, DataKey = key, DataType = "int64", IntegerValue = number },
                float number => new CharacterBuffDataRow { CharacterId = characterId, ListIndex = listIndex, DataKey = key, DataType = "real", RealValue = number },
                double number => new CharacterBuffDataRow { CharacterId = characterId, ListIndex = listIndex, DataKey = key, DataType = "real", RealValue = number },
                string text => new CharacterBuffDataRow { CharacterId = characterId, ListIndex = listIndex, DataKey = key, DataType = "text", TextValue = text },
                _ => throw new NotSupportedException($"Buff typed data 不支持 {value?.GetType().FullName ?? "null"}：{key}"),
            };
        }

        private static IReadOnlyList<AuctionRow> LoadAuctionRows(SqlSession session)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));

            return session.Query<AuctionRow>(
                "SELECT " +
                "auction_id AS AuctionId, " +
                "item_id AS ItemId, " +
                "consignment_utc_ms AS ConsignmentUtcMs, " +
                "price AS Price, " +
                "current_bid AS CurrentBid, " +
                "seller_character_id AS SellerCharacterId, " +
                "current_buyer_character_id AS CurrentBuyerCharacterId, " +
                "expired AS Expired, " +
                "sold AS Sold, " +
                "item_type AS ItemType " +
                "FROM auctions " +
                "ORDER BY auction_id");
        }

        private static IReadOnlyList<MailRow> LoadMailRows(SqlSession session)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));

            return session.Query<MailRow>(
                "SELECT " +
                "mail_id AS MailId, " +
                "sender_name AS SenderName, " +
                "recipient_character_id AS RecipientCharacterId, " +
                "message AS Message, " +
                "gold AS Gold, " +
                "date_sent_utc_ms AS DateSentUtcMs, " +
                "date_opened_utc_ms AS DateOpenedUtcMs, " +
                "locked AS Locked, " +
                "collected AS Collected, " +
                "can_reply AS CanReply " +
                "FROM mails " +
                "ORDER BY mail_id");
        }

        private static IReadOnlyList<MailItemRow> LoadMailItemRows(SqlSession session)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));

            return session.Query<MailItemRow>(
                "SELECT mail_id AS MailId, slot_index AS SlotIndex, item_id AS ItemId FROM mail_items ORDER BY mail_id, slot_index");
        }

        private static IReadOnlyList<GameshopLogRow> LoadGameshopLogRows(SqlSession session)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));

            return session.Query<GameshopLogRow>(
                "SELECT item_index AS ItemIndex, count AS Count FROM gameshop_log ORDER BY item_index");
        }

        private static IReadOnlyList<RespawnSaveRow> LoadRespawnSaveRows(SqlSession session)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));

            return session.Query<RespawnSaveRow>(
                "SELECT respawn_index AS RespawnIndex, next_spawn_tick AS NextSpawnTick, spawned AS Spawned FROM respawn_saves ORDER BY respawn_index");
        }

        private static IReadOnlyList<CharacterMagicRow> LoadCharacterMagicRows(SqlSession session)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));

            return session.Query<CharacterMagicRow>(
                "SELECT " +
                "character_id AS CharacterId, " +
                "spell AS Spell, " +
                "magic_level AS MagicLevel, " +
                "magic_key AS MagicKey, " +
                "experience AS Experience, " +
                "is_temp_spell AS IsTempSpell, " +
                "cast_time AS CastTime " +
                "FROM character_magics " +
                "ORDER BY character_id, spell");
        }

        private static IReadOnlyList<CharacterCompletedQuestRow> LoadCharacterCompletedQuestRows(SqlSession session)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));

            return session.Query<CharacterCompletedQuestRow>(
                "SELECT character_id AS CharacterId, quest_id AS QuestId FROM character_completed_quests ORDER BY character_id, quest_id");
        }

        private static IReadOnlyList<CharacterFlagRow> LoadCharacterFlagRows(SqlSession session)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));

            return session.Query<CharacterFlagRow>(
                "SELECT character_id AS CharacterId, flag_index AS FlagIndex, flag_value AS FlagValue FROM character_flags ORDER BY character_id, flag_index");
        }

        private static IReadOnlyList<CharacterGameshopPurchaseRow> LoadCharacterGameshopPurchaseRows(SqlSession session)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));

            return session.Query<CharacterGameshopPurchaseRow>(
                "SELECT character_id AS CharacterId, item_index AS ItemIndex, purchase_count AS PurchaseCount FROM character_gameshop_purchases ORDER BY character_id, item_index");
        }

        private static IReadOnlyList<CurrentQuestRow> LoadCurrentQuestRows(SqlSession session)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));

            return session.Query<CurrentQuestRow>(
                "SELECT character_id AS CharacterId, slot_index AS SlotIndex, quest_id AS QuestId, start_utc_ms AS StartUtcMs, end_utc_ms AS EndUtcMs FROM character_current_quests ORDER BY character_id, slot_index");
        }

        private static IReadOnlyList<CurrentQuestKillTaskRow> LoadCurrentQuestKillTaskRows(SqlSession session)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));

            return session.Query<CurrentQuestKillTaskRow>(
                "SELECT character_id AS CharacterId, quest_id AS QuestId, monster_id AS MonsterId, task_count AS TaskCount FROM character_current_quest_kill_tasks ORDER BY character_id, quest_id, monster_id");
        }

        private static IReadOnlyList<CurrentQuestItemTaskRow> LoadCurrentQuestItemTaskRows(SqlSession session)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));

            return session.Query<CurrentQuestItemTaskRow>(
                "SELECT character_id AS CharacterId, quest_id AS QuestId, item_id AS ItemId, task_count AS TaskCount FROM character_current_quest_item_tasks ORDER BY character_id, quest_id, item_id");
        }

        private static IReadOnlyList<CurrentQuestFlagTaskRow> LoadCurrentQuestFlagTaskRows(SqlSession session)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));

            return session.Query<CurrentQuestFlagTaskRow>(
                "SELECT character_id AS CharacterId, quest_id AS QuestId, flag_number AS FlagNumber, flag_state AS FlagState FROM character_current_quest_flag_tasks ORDER BY character_id, quest_id, flag_number");
        }

        private static IReadOnlyList<CharacterPetRow> LoadCharacterPetRows(SqlSession session)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));

            return session.Query<CharacterPetRow>(
                "SELECT character_id AS CharacterId, list_index AS ListIndex, monster_id AS MonsterId, hp AS Hp, experience AS Experience, pet_level AS PetLevel, max_pet_level AS MaxPetLevel FROM character_pets ORDER BY character_id, list_index");
        }

        private static IReadOnlyList<CharacterFriendRow> LoadCharacterFriendRows(SqlSession session)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));

            return session.Query<CharacterFriendRow>(
                "SELECT character_id AS CharacterId, list_index AS ListIndex, friend_character_id AS FriendCharacterId, blocked AS Blocked, memo AS Memo FROM character_friends ORDER BY character_id, list_index");
        }

        private static IReadOnlyList<CharacterRentedItemRow> LoadCharacterRentedItemRows(SqlSession session)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));

            return session.Query<CharacterRentedItemRow>(
                "SELECT character_id AS CharacterId, list_index AS ListIndex, item_id AS ItemId, item_name AS ItemName, renting_player_name AS RentingPlayerName, item_return_utc_ms AS ItemReturnUtcMs FROM character_rented_items ORDER BY character_id, list_index");
        }

        private static IReadOnlyList<CharacterIntelligentCreatureRow> LoadCharacterIntelligentCreatureRows(SqlSession session)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));

            return session.Query<CharacterIntelligentCreatureRow>(
                "SELECT " +
                "character_id AS CharacterId, " +
                "slot_index AS SlotIndex, " +
                "pet_type AS PetType, " +
                "custom_name AS CustomName, " +
                "fullness AS Fullness, " +
                "expire_utc_ms AS ExpireUtcMs, " +
                "blackstone_time AS BlackstoneTime, " +
                "pickup_mode AS PickupMode, " +
                "filter_pickup_all AS FilterPickupAll, " +
                "filter_pickup_gold AS FilterPickupGold, " +
                "filter_pickup_weapons AS FilterPickupWeapons, " +
                "filter_pickup_armours AS FilterPickupArmours, " +
                "filter_pickup_helmets AS FilterPickupHelmets, " +
                "filter_pickup_boots AS FilterPickupBoots, " +
                "filter_pickup_belts AS FilterPickupBelts, " +
                "filter_pickup_accessories AS FilterPickupAccessories, " +
                "filter_pickup_others AS FilterPickupOthers, " +
                "filter_pickup_grade AS FilterPickupGrade, " +
                "maintain_food_time AS MaintainFoodTime " +
                "FROM character_intelligent_creatures " +
                "ORDER BY character_id, slot_index");
        }

        private static IReadOnlyList<HeroDetailRow> LoadHeroDetailRows(SqlSession session)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));

            return session.Query<HeroDetailRow>(
                "SELECT character_id AS CharacterId, auto_pot AS AutoPot, grade AS Grade, hp_item_index AS HpItemIndex, mp_item_index AS MpItemIndex, auto_hp_percent AS AutoHpPercent, auto_mp_percent AS AutoMpPercent, seal_count AS SealCount FROM hero_details ORDER BY character_id");
        }

        private static IReadOnlyList<CharacterHeroSlotRow> LoadCharacterHeroSlotRows(SqlSession session)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));

            return session.Query<CharacterHeroSlotRow>(
                "SELECT character_id AS CharacterId, slot_index AS SlotIndex, hero_character_id AS HeroCharacterId FROM character_hero_slots ORDER BY character_id, slot_index");
        }

        private static IReadOnlyList<CharacterBuffRow> LoadCharacterBuffRows(SqlSession session)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));

            return session.Query<CharacterBuffRow>(
                "SELECT character_id AS CharacterId, list_index AS ListIndex, buff_type AS BuffType, object_id AS ObjectId, expire_time AS ExpireTime, last_time AS LastTime, next_time AS NextTime, flag_for_removal AS FlagForRemoval, paused AS Paused FROM character_buffs ORDER BY character_id, list_index");
        }

        private static IReadOnlyList<CharacterBuffStatRow> LoadCharacterBuffStatRows(SqlSession session) =>
            session.Query<CharacterBuffStatRow>("SELECT character_id AS CharacterId, list_index AS ListIndex, stat_id AS StatId, stat_value AS StatValue FROM character_buff_stats ORDER BY character_id, list_index, stat_id");

        private static IReadOnlyList<CharacterBuffValueRow> LoadCharacterBuffValueRows(SqlSession session) =>
            session.Query<CharacterBuffValueRow>("SELECT character_id AS CharacterId, list_index AS ListIndex, value_index AS ValueIndex, value_type AS ValueType, integer_value AS IntegerValue, real_value AS RealValue, text_value AS TextValue FROM character_buff_values ORDER BY character_id, list_index, value_index");

        private static IReadOnlyList<CharacterBuffDataRow> LoadCharacterBuffDataRows(SqlSession session) =>
            session.Query<CharacterBuffDataRow>("SELECT character_id AS CharacterId, list_index AS ListIndex, data_key AS DataKey, data_type AS DataType, integer_value AS IntegerValue, real_value AS RealValue, text_value AS TextValue FROM character_buff_data ORDER BY character_id, list_index, data_key");

        private static void ReplaceAuctions(SqlSession session, IReadOnlyList<AuctionRow> auctions, long saveEpochUtcMs = 0)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));

            var nowMs = saveEpochUtcMs > 0 ? saveEpochUtcMs : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            if (auctions == null || auctions.Count == 0)
                auctions = Array.Empty<AuctionRow>();

            var sql = session.Dialect.BuildUpsert(
                tableName: "auctions",
                insertColumns:
                [
                    "auction_id",
                    "item_id",
                    "consignment_utc_ms",
                    "price",
                    "current_bid",
                    "seller_character_id",
                    "current_buyer_character_id",
                    "expired",
                    "sold",
                    "item_type",
                    "updated_utc_ms",
                ],
                keyColumns: ["auction_id"],
                updateColumns:
                [
                    "item_id",
                    "consignment_utc_ms",
                    "price",
                    "current_bid",
                    "seller_character_id",
                    "current_buyer_character_id",
                    "expired",
                    "sold",
                    "item_type",
                    "updated_utc_ms",
                ]);

            var batchSize = Settings.SaveBatchSize <= 0 ? 2000 : Settings.SaveBatchSize;

            for (var offset = 0; offset < auctions.Count; offset += batchSize)
            {
                var take = Math.Min(batchSize, auctions.Count - offset);
                var batch = new List<object>(take);

                for (var i = 0; i < take; i++)
                {
                    var row = auctions[offset + i];
                    if (row == null) continue;

                    batch.Add(new
                    {
                        auction_id = row.AuctionId,
                        item_id = row.ItemId,
                        consignment_utc_ms = row.ConsignmentUtcMs,
                        price = row.Price,
                        current_bid = row.CurrentBid,
                        seller_character_id = row.SellerCharacterId,
                        current_buyer_character_id = row.CurrentBuyerCharacterId,
                        expired = row.Expired,
                        sold = row.Sold,
                        item_type = row.ItemType,
                        updated_utc_ms = nowMs,
                    });
                }

                if (batch.Count > 0)
                    session.Execute(sql, batch);
            }

            session.Execute("DELETE FROM auctions WHERE updated_utc_ms <> @nowMs", new { nowMs });
        }

        private static void ReplaceMails(SqlSession session, IReadOnlyList<MailRow> mails, IReadOnlyList<MailItemRow> mailItems, long saveEpochUtcMs = 0)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));

            var nowMs = saveEpochUtcMs > 0 ? saveEpochUtcMs : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            var batchSize = Settings.SaveBatchSize <= 0 ? 2000 : Settings.SaveBatchSize;

            mails ??= Array.Empty<MailRow>();
            mailItems ??= Array.Empty<MailItemRow>();

            if (mails.Count > 0)
            {
                var sql = session.Dialect.BuildUpsert(
                    tableName: "mails",
                    insertColumns:
                    [
                        "mail_id",
                        "sender_name",
                        "recipient_character_id",
                        "message",
                        "gold",
                        "date_sent_utc_ms",
                        "date_opened_utc_ms",
                        "locked",
                        "collected",
                        "can_reply",
                        "updated_utc_ms",
                    ],
                    keyColumns: ["mail_id"],
                    updateColumns:
                    [
                        "sender_name",
                        "recipient_character_id",
                        "message",
                        "gold",
                        "date_sent_utc_ms",
                        "date_opened_utc_ms",
                        "locked",
                        "collected",
                        "can_reply",
                        "updated_utc_ms",
                    ]);

                for (var offset = 0; offset < mails.Count; offset += batchSize)
                {
                    var take = Math.Min(batchSize, mails.Count - offset);
                    var batch = new List<object>(take);

                    for (var i = 0; i < take; i++)
                    {
                        var row = mails[offset + i];
                        if (row == null) continue;

                        batch.Add(new
                        {
                            mail_id = row.MailId,
                            sender_name = row.SenderName ?? string.Empty,
                            recipient_character_id = row.RecipientCharacterId,
                            message = row.Message ?? string.Empty,
                            gold = row.Gold,
                            date_sent_utc_ms = row.DateSentUtcMs,
                            date_opened_utc_ms = row.DateOpenedUtcMs,
                            locked = row.Locked,
                            collected = row.Collected,
                            can_reply = row.CanReply,
                            updated_utc_ms = nowMs,
                        });
                    }

                    if (batch.Count > 0)
                        session.Execute(sql, batch);
                }
            }

            if (mailItems.Count > 0)
            {
                var sql = session.Dialect.BuildUpsert(
                    tableName: "mail_items",
                    insertColumns: ["mail_id", "slot_index", "item_id", "updated_utc_ms"],
                    keyColumns: ["mail_id", "slot_index"],
                    updateColumns: ["item_id", "updated_utc_ms"]);

                for (var offset = 0; offset < mailItems.Count; offset += batchSize)
                {
                    var take = Math.Min(batchSize, mailItems.Count - offset);
                    var batch = new List<object>(take);

                    for (var i = 0; i < take; i++)
                    {
                        var row = mailItems[offset + i];
                        if (row == null) continue;

                        batch.Add(new
                        {
                            mail_id = row.MailId,
                            slot_index = row.SlotIndex,
                            item_id = row.ItemId,
                            updated_utc_ms = nowMs,
                        });
                    }

                    if (batch.Count > 0)
                        session.Execute(sql, batch);
                }
            }

            session.Execute("DELETE FROM mail_items WHERE updated_utc_ms <> @nowMs", new { nowMs });
            session.Execute("DELETE FROM mails WHERE updated_utc_ms <> @nowMs", new { nowMs });
        }

        private static void ReplaceGameshopLog(SqlSession session, IReadOnlyList<GameshopLogRow> rows, long saveEpochUtcMs = 0)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));

            var nowMs = saveEpochUtcMs > 0 ? saveEpochUtcMs : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            rows ??= Array.Empty<GameshopLogRow>();

            var sql = session.Dialect.BuildUpsert(
                tableName: "gameshop_log",
                insertColumns: ["item_index", "count", "updated_utc_ms"],
                keyColumns: ["item_index"],
                updateColumns: ["count", "updated_utc_ms"]);

            var batchSize = Settings.SaveBatchSize <= 0 ? 2000 : Settings.SaveBatchSize;

            for (var offset = 0; offset < rows.Count; offset += batchSize)
            {
                var take = Math.Min(batchSize, rows.Count - offset);
                var batch = new List<object>(take);

                for (var i = 0; i < take; i++)
                {
                    var row = rows[offset + i];
                    if (row == null) continue;

                    batch.Add(new
                    {
                        item_index = row.ItemIndex,
                        count = row.Count,
                        updated_utc_ms = nowMs,
                    });
                }

                if (batch.Count > 0)
                    session.Execute(sql, batch);
            }

            session.Execute("DELETE FROM gameshop_log WHERE updated_utc_ms <> @nowMs", new { nowMs });
        }

        private static void ReplaceRespawnSaves(SqlSession session, IReadOnlyList<RespawnSaveRow> rows, long saveEpochUtcMs = 0)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));

            var nowMs = saveEpochUtcMs > 0 ? saveEpochUtcMs : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            rows ??= Array.Empty<RespawnSaveRow>();

            var sql = session.Dialect.BuildUpsert(
                tableName: "respawn_saves",
                insertColumns: ["respawn_index", "next_spawn_tick", "spawned", "updated_utc_ms"],
                keyColumns: ["respawn_index"],
                updateColumns: ["next_spawn_tick", "spawned", "updated_utc_ms"]);

            var batchSize = Settings.SaveBatchSize <= 0 ? 2000 : Settings.SaveBatchSize;

            for (var offset = 0; offset < rows.Count; offset += batchSize)
            {
                var take = Math.Min(batchSize, rows.Count - offset);
                var batch = new List<object>(take);

                for (var i = 0; i < take; i++)
                {
                    var row = rows[offset + i];
                    if (row == null) continue;

                    batch.Add(new
                    {
                        respawn_index = row.RespawnIndex,
                        next_spawn_tick = row.NextSpawnTick,
                        spawned = row.Spawned,
                        updated_utc_ms = nowMs,
                    });
                }

                if (batch.Count > 0)
                    session.Execute(sql, batch);
            }

            session.Execute("DELETE FROM respawn_saves WHERE updated_utc_ms <> @nowMs", new { nowMs });
        }

        private static void ReplaceCharacterMagics(SqlSession session, IReadOnlyList<CharacterMagicRow> rows, long saveEpochUtcMs = 0)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));

            var nowMs = saveEpochUtcMs > 0 ? saveEpochUtcMs : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            rows ??= Array.Empty<CharacterMagicRow>();

            var sql = session.Dialect.BuildUpsert(
                tableName: "character_magics",
                insertColumns: ["character_id", "spell", "magic_level", "magic_key", "experience", "is_temp_spell", "cast_time", "updated_utc_ms"],
                keyColumns: ["character_id", "spell"],
                updateColumns: ["magic_level", "magic_key", "experience", "is_temp_spell", "cast_time", "updated_utc_ms"]);

            var batchSize = Settings.SaveBatchSize <= 0 ? 2000 : Settings.SaveBatchSize;

            for (var offset = 0; offset < rows.Count; offset += batchSize)
            {
                var take = Math.Min(batchSize, rows.Count - offset);
                var batch = new List<object>(take);

                for (var index = 0; index < take; index++)
                {
                    var row = rows[offset + index];
                    if (row == null) continue;

                    batch.Add(new
                    {
                        character_id = row.CharacterId,
                        spell = row.Spell,
                        magic_level = row.MagicLevel,
                        magic_key = row.MagicKey,
                        experience = row.Experience,
                        is_temp_spell = row.IsTempSpell,
                        cast_time = row.CastTime,
                        updated_utc_ms = nowMs,
                    });
                }

                if (batch.Count > 0)
                    session.Execute(sql, batch);
            }

            session.Execute("DELETE FROM character_magics WHERE updated_utc_ms <> @nowMs", new { nowMs });
        }

        private static void ReplaceCharacterCompletedQuests(SqlSession session, IReadOnlyList<CharacterCompletedQuestRow> rows, long saveEpochUtcMs = 0)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));

            var nowMs = saveEpochUtcMs > 0 ? saveEpochUtcMs : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            rows ??= Array.Empty<CharacterCompletedQuestRow>();

            var sql = session.Dialect.BuildUpsert(
                tableName: "character_completed_quests",
                insertColumns: ["character_id", "quest_id", "updated_utc_ms"],
                keyColumns: ["character_id", "quest_id"],
                updateColumns: ["updated_utc_ms"]);

            var batchSize = Settings.SaveBatchSize <= 0 ? 2000 : Settings.SaveBatchSize;

            for (var offset = 0; offset < rows.Count; offset += batchSize)
            {
                var take = Math.Min(batchSize, rows.Count - offset);
                var batch = new List<object>(take);

                for (var index = 0; index < take; index++)
                {
                    var row = rows[offset + index];
                    if (row == null) continue;

                    batch.Add(new
                    {
                        character_id = row.CharacterId,
                        quest_id = row.QuestId,
                        updated_utc_ms = nowMs,
                    });
                }

                if (batch.Count > 0)
                    session.Execute(sql, batch);
            }

            session.Execute("DELETE FROM character_completed_quests WHERE updated_utc_ms <> @nowMs", new { nowMs });
        }

        private static void ReplaceCharacterFlags(SqlSession session, IReadOnlyList<CharacterFlagRow> rows, long saveEpochUtcMs = 0)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));

            var nowMs = saveEpochUtcMs > 0 ? saveEpochUtcMs : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            rows ??= Array.Empty<CharacterFlagRow>();

            var sql = session.Dialect.BuildUpsert(
                tableName: "character_flags",
                insertColumns: ["character_id", "flag_index", "flag_value", "updated_utc_ms"],
                keyColumns: ["character_id", "flag_index"],
                updateColumns: ["flag_value", "updated_utc_ms"]);

            var batchSize = Settings.SaveBatchSize <= 0 ? 2000 : Settings.SaveBatchSize;

            for (var offset = 0; offset < rows.Count; offset += batchSize)
            {
                var take = Math.Min(batchSize, rows.Count - offset);
                var batch = new List<object>(take);

                for (var index = 0; index < take; index++)
                {
                    var row = rows[offset + index];
                    if (row == null) continue;

                    batch.Add(new
                    {
                        character_id = row.CharacterId,
                        flag_index = row.FlagIndex,
                        flag_value = row.FlagValue,
                        updated_utc_ms = nowMs,
                    });
                }

                if (batch.Count > 0)
                    session.Execute(sql, batch);
            }

            session.Execute("DELETE FROM character_flags WHERE updated_utc_ms <> @nowMs", new { nowMs });
        }

        private static void ReplaceCharacterGameshopPurchases(SqlSession session, IReadOnlyList<CharacterGameshopPurchaseRow> rows, long saveEpochUtcMs = 0)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));

            var nowMs = saveEpochUtcMs > 0 ? saveEpochUtcMs : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            rows ??= Array.Empty<CharacterGameshopPurchaseRow>();

            var sql = session.Dialect.BuildUpsert(
                tableName: "character_gameshop_purchases",
                insertColumns: ["character_id", "item_index", "purchase_count", "updated_utc_ms"],
                keyColumns: ["character_id", "item_index"],
                updateColumns: ["purchase_count", "updated_utc_ms"]);

            var batchSize = Settings.SaveBatchSize <= 0 ? 2000 : Settings.SaveBatchSize;

            for (var offset = 0; offset < rows.Count; offset += batchSize)
            {
                var take = Math.Min(batchSize, rows.Count - offset);
                var batch = new List<object>(take);

                for (var index = 0; index < take; index++)
                {
                    var row = rows[offset + index];
                    if (row == null) continue;

                    batch.Add(new
                    {
                        character_id = row.CharacterId,
                        item_index = row.ItemIndex,
                        purchase_count = row.PurchaseCount,
                        updated_utc_ms = nowMs,
                    });
                }

                if (batch.Count > 0)
                    session.Execute(sql, batch);
            }

            session.Execute("DELETE FROM character_gameshop_purchases WHERE updated_utc_ms <> @nowMs", new { nowMs });
        }

        private static void ReplaceCurrentQuests(
            SqlSession session,
            IReadOnlyList<CurrentQuestRow> currentQuests,
            IReadOnlyList<CurrentQuestKillTaskRow> currentQuestKillTasks,
            IReadOnlyList<CurrentQuestItemTaskRow> currentQuestItemTasks,
            IReadOnlyList<CurrentQuestFlagTaskRow> currentQuestFlagTasks,
            long saveEpochUtcMs = 0)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));

            var nowMs = saveEpochUtcMs > 0 ? saveEpochUtcMs : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            currentQuests ??= Array.Empty<CurrentQuestRow>();
            currentQuestKillTasks ??= Array.Empty<CurrentQuestKillTaskRow>();
            currentQuestItemTasks ??= Array.Empty<CurrentQuestItemTaskRow>();
            currentQuestFlagTasks ??= Array.Empty<CurrentQuestFlagTaskRow>();

            var batchSize = Settings.SaveBatchSize <= 0 ? 2000 : Settings.SaveBatchSize;

            if (currentQuests.Count > 0)
            {
                var sql = session.Dialect.BuildUpsert(
                    tableName: "character_current_quests",
                    insertColumns: ["character_id", "slot_index", "quest_id", "start_utc_ms", "end_utc_ms", "updated_utc_ms"],
                    keyColumns: ["character_id", "slot_index"],
                    updateColumns: ["quest_id", "start_utc_ms", "end_utc_ms", "updated_utc_ms"]);

                for (var offset = 0; offset < currentQuests.Count; offset += batchSize)
                {
                    var take = Math.Min(batchSize, currentQuests.Count - offset);
                    var batch = new List<object>(take);

                    for (var index = 0; index < take; index++)
                    {
                        var row = currentQuests[offset + index];
                        if (row == null) continue;

                        batch.Add(new
                        {
                            character_id = row.CharacterId,
                            slot_index = row.SlotIndex,
                            quest_id = row.QuestId,
                            start_utc_ms = row.StartUtcMs,
                            end_utc_ms = row.EndUtcMs,
                            updated_utc_ms = nowMs,
                        });
                    }

                    if (batch.Count > 0)
                        session.Execute(sql, batch);
                }
            }

            if (currentQuestKillTasks.Count > 0)
            {
                var sql = session.Dialect.BuildUpsert(
                    tableName: "character_current_quest_kill_tasks",
                    insertColumns: ["character_id", "quest_id", "monster_id", "task_count", "updated_utc_ms"],
                    keyColumns: ["character_id", "quest_id", "monster_id"],
                    updateColumns: ["task_count", "updated_utc_ms"]);

                for (var offset = 0; offset < currentQuestKillTasks.Count; offset += batchSize)
                {
                    var take = Math.Min(batchSize, currentQuestKillTasks.Count - offset);
                    var batch = new List<object>(take);

                    for (var index = 0; index < take; index++)
                    {
                        var row = currentQuestKillTasks[offset + index];
                        if (row == null) continue;

                        batch.Add(new
                        {
                            character_id = row.CharacterId,
                            quest_id = row.QuestId,
                            monster_id = row.MonsterId,
                            task_count = row.TaskCount,
                            updated_utc_ms = nowMs,
                        });
                    }

                    if (batch.Count > 0)
                        session.Execute(sql, batch);
                }
            }

            if (currentQuestItemTasks.Count > 0)
            {
                var sql = session.Dialect.BuildUpsert(
                    tableName: "character_current_quest_item_tasks",
                    insertColumns: ["character_id", "quest_id", "item_id", "task_count", "updated_utc_ms"],
                    keyColumns: ["character_id", "quest_id", "item_id"],
                    updateColumns: ["task_count", "updated_utc_ms"]);

                for (var offset = 0; offset < currentQuestItemTasks.Count; offset += batchSize)
                {
                    var take = Math.Min(batchSize, currentQuestItemTasks.Count - offset);
                    var batch = new List<object>(take);

                    for (var index = 0; index < take; index++)
                    {
                        var row = currentQuestItemTasks[offset + index];
                        if (row == null) continue;

                        batch.Add(new
                        {
                            character_id = row.CharacterId,
                            quest_id = row.QuestId,
                            item_id = row.ItemId,
                            task_count = row.TaskCount,
                            updated_utc_ms = nowMs,
                        });
                    }

                    if (batch.Count > 0)
                        session.Execute(sql, batch);
                }
            }

            if (currentQuestFlagTasks.Count > 0)
            {
                var sql = session.Dialect.BuildUpsert(
                    tableName: "character_current_quest_flag_tasks",
                    insertColumns: ["character_id", "quest_id", "flag_number", "flag_state", "updated_utc_ms"],
                    keyColumns: ["character_id", "quest_id", "flag_number"],
                    updateColumns: ["flag_state", "updated_utc_ms"]);

                for (var offset = 0; offset < currentQuestFlagTasks.Count; offset += batchSize)
                {
                    var take = Math.Min(batchSize, currentQuestFlagTasks.Count - offset);
                    var batch = new List<object>(take);

                    for (var index = 0; index < take; index++)
                    {
                        var row = currentQuestFlagTasks[offset + index];
                        if (row == null) continue;

                        batch.Add(new
                        {
                            character_id = row.CharacterId,
                            quest_id = row.QuestId,
                            flag_number = row.FlagNumber,
                            flag_state = row.FlagState,
                            updated_utc_ms = nowMs,
                        });
                    }

                    if (batch.Count > 0)
                        session.Execute(sql, batch);
                }
            }

            session.Execute("DELETE FROM character_current_quest_flag_tasks WHERE updated_utc_ms <> @nowMs", new { nowMs });
            session.Execute("DELETE FROM character_current_quest_item_tasks WHERE updated_utc_ms <> @nowMs", new { nowMs });
            session.Execute("DELETE FROM character_current_quest_kill_tasks WHERE updated_utc_ms <> @nowMs", new { nowMs });
            session.Execute("DELETE FROM character_current_quests WHERE updated_utc_ms <> @nowMs", new { nowMs });
        }

        private static void ReplaceCharacterPets(SqlSession session, IReadOnlyList<CharacterPetRow> rows, long saveEpochUtcMs = 0)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));

            var nowMs = saveEpochUtcMs > 0 ? saveEpochUtcMs : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            rows ??= Array.Empty<CharacterPetRow>();
            var batchSize = Settings.SaveBatchSize <= 0 ? 2000 : Settings.SaveBatchSize;

            var sql = session.Dialect.BuildUpsert(
                tableName: "character_pets",
                insertColumns: ["character_id", "list_index", "monster_id", "hp", "experience", "pet_level", "max_pet_level", "updated_utc_ms"],
                keyColumns: ["character_id", "list_index"],
                updateColumns: ["monster_id", "hp", "experience", "pet_level", "max_pet_level", "updated_utc_ms"]);

            for (var offset = 0; offset < rows.Count; offset += batchSize)
            {
                var take = Math.Min(batchSize, rows.Count - offset);
                var batch = new List<object>(take);

                for (var index = 0; index < take; index++)
                {
                    var row = rows[offset + index];
                    if (row == null) continue;

                    batch.Add(new
                    {
                        character_id = row.CharacterId,
                        list_index = row.ListIndex,
                        monster_id = row.MonsterId,
                        hp = row.Hp,
                        experience = row.Experience,
                        pet_level = row.PetLevel,
                        max_pet_level = row.MaxPetLevel,
                        updated_utc_ms = nowMs,
                    });
                }

                if (batch.Count > 0)
                    session.Execute(sql, batch);
            }

            session.Execute("DELETE FROM character_pets WHERE updated_utc_ms <> @nowMs", new { nowMs });
        }

        private static void ReplaceCharacterFriends(SqlSession session, IReadOnlyList<CharacterFriendRow> rows, long saveEpochUtcMs = 0)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));

            var nowMs = saveEpochUtcMs > 0 ? saveEpochUtcMs : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            rows ??= Array.Empty<CharacterFriendRow>();
            var batchSize = Settings.SaveBatchSize <= 0 ? 2000 : Settings.SaveBatchSize;

            var sql = session.Dialect.BuildUpsert(
                tableName: "character_friends",
                insertColumns: ["character_id", "list_index", "friend_character_id", "blocked", "memo", "updated_utc_ms"],
                keyColumns: ["character_id", "list_index"],
                updateColumns: ["friend_character_id", "blocked", "memo", "updated_utc_ms"]);

            for (var offset = 0; offset < rows.Count; offset += batchSize)
            {
                var take = Math.Min(batchSize, rows.Count - offset);
                var batch = new List<object>(take);

                for (var index = 0; index < take; index++)
                {
                    var row = rows[offset + index];
                    if (row == null) continue;

                    batch.Add(new
                    {
                        character_id = row.CharacterId,
                        list_index = row.ListIndex,
                        friend_character_id = row.FriendCharacterId,
                        blocked = row.Blocked,
                        memo = row.Memo ?? string.Empty,
                        updated_utc_ms = nowMs,
                    });
                }

                if (batch.Count > 0)
                    session.Execute(sql, batch);
            }

            session.Execute("DELETE FROM character_friends WHERE updated_utc_ms <> @nowMs", new { nowMs });
        }

        private static void ReplaceCharacterRentedItems(SqlSession session, IReadOnlyList<CharacterRentedItemRow> rows, long saveEpochUtcMs = 0)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));

            var nowMs = saveEpochUtcMs > 0 ? saveEpochUtcMs : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            rows ??= Array.Empty<CharacterRentedItemRow>();
            var batchSize = Settings.SaveBatchSize <= 0 ? 2000 : Settings.SaveBatchSize;

            var sql = session.Dialect.BuildUpsert(
                tableName: "character_rented_items",
                insertColumns: ["character_id", "list_index", "item_id", "item_name", "renting_player_name", "item_return_utc_ms", "updated_utc_ms"],
                keyColumns: ["character_id", "list_index"],
                updateColumns: ["item_id", "item_name", "renting_player_name", "item_return_utc_ms", "updated_utc_ms"]);

            for (var offset = 0; offset < rows.Count; offset += batchSize)
            {
                var take = Math.Min(batchSize, rows.Count - offset);
                var batch = new List<object>(take);

                for (var index = 0; index < take; index++)
                {
                    var row = rows[offset + index];
                    if (row == null) continue;

                    batch.Add(new
                    {
                        character_id = row.CharacterId,
                        list_index = row.ListIndex,
                        item_id = row.ItemId,
                        item_name = row.ItemName ?? string.Empty,
                        renting_player_name = row.RentingPlayerName ?? string.Empty,
                        item_return_utc_ms = row.ItemReturnUtcMs,
                        updated_utc_ms = nowMs,
                    });
                }

                if (batch.Count > 0)
                    session.Execute(sql, batch);
            }

            session.Execute("DELETE FROM character_rented_items WHERE updated_utc_ms <> @nowMs", new { nowMs });
        }

        private static void ReplaceCharacterIntelligentCreatures(SqlSession session, IReadOnlyList<CharacterIntelligentCreatureRow> rows, long saveEpochUtcMs = 0)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));

            var nowMs = saveEpochUtcMs > 0 ? saveEpochUtcMs : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            rows ??= Array.Empty<CharacterIntelligentCreatureRow>();
            var batchSize = Settings.SaveBatchSize <= 0 ? 2000 : Settings.SaveBatchSize;

            var sql = session.Dialect.BuildUpsert(
                tableName: "character_intelligent_creatures",
                insertColumns:
                [
                    "character_id",
                    "slot_index",
                    "pet_type",
                    "custom_name",
                    "fullness",
                    "expire_utc_ms",
                    "blackstone_time",
                    "pickup_mode",
                    "filter_pickup_all",
                    "filter_pickup_gold",
                    "filter_pickup_weapons",
                    "filter_pickup_armours",
                    "filter_pickup_helmets",
                    "filter_pickup_boots",
                    "filter_pickup_belts",
                    "filter_pickup_accessories",
                    "filter_pickup_others",
                    "filter_pickup_grade",
                    "maintain_food_time",
                    "updated_utc_ms",
                ],
                keyColumns: ["character_id", "slot_index"],
                updateColumns:
                [
                    "pet_type",
                    "custom_name",
                    "fullness",
                    "expire_utc_ms",
                    "blackstone_time",
                    "pickup_mode",
                    "filter_pickup_all",
                    "filter_pickup_gold",
                    "filter_pickup_weapons",
                    "filter_pickup_armours",
                    "filter_pickup_helmets",
                    "filter_pickup_boots",
                    "filter_pickup_belts",
                    "filter_pickup_accessories",
                    "filter_pickup_others",
                    "filter_pickup_grade",
                    "maintain_food_time",
                    "updated_utc_ms",
                ]);

            for (var offset = 0; offset < rows.Count; offset += batchSize)
            {
                var take = Math.Min(batchSize, rows.Count - offset);
                var batch = new List<object>(take);

                for (var index = 0; index < take; index++)
                {
                    var row = rows[offset + index];
                    if (row == null) continue;

                    batch.Add(new
                    {
                        character_id = row.CharacterId,
                        slot_index = row.SlotIndex,
                        pet_type = row.PetType,
                        custom_name = row.CustomName ?? string.Empty,
                        fullness = row.Fullness,
                        expire_utc_ms = row.ExpireUtcMs,
                        blackstone_time = row.BlackstoneTime,
                        pickup_mode = row.PickupMode,
                        filter_pickup_all = row.FilterPickupAll,
                        filter_pickup_gold = row.FilterPickupGold,
                        filter_pickup_weapons = row.FilterPickupWeapons,
                        filter_pickup_armours = row.FilterPickupArmours,
                        filter_pickup_helmets = row.FilterPickupHelmets,
                        filter_pickup_boots = row.FilterPickupBoots,
                        filter_pickup_belts = row.FilterPickupBelts,
                        filter_pickup_accessories = row.FilterPickupAccessories,
                        filter_pickup_others = row.FilterPickupOthers,
                        filter_pickup_grade = row.FilterPickupGrade,
                        maintain_food_time = row.MaintainFoodTime,
                        updated_utc_ms = nowMs,
                    });
                }

                if (batch.Count > 0)
                    session.Execute(sql, batch);
            }

            session.Execute("DELETE FROM character_intelligent_creatures WHERE updated_utc_ms <> @nowMs", new { nowMs });
        }

        private static void ReplaceHeroDetails(SqlSession session, IReadOnlyList<HeroDetailRow> rows, long saveEpochUtcMs = 0)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));

            var nowMs = saveEpochUtcMs > 0 ? saveEpochUtcMs : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            rows ??= Array.Empty<HeroDetailRow>();
            var batchSize = Settings.SaveBatchSize <= 0 ? 2000 : Settings.SaveBatchSize;

            var sql = session.Dialect.BuildUpsert(
                tableName: "hero_details",
                insertColumns: ["character_id", "auto_pot", "grade", "hp_item_index", "mp_item_index", "auto_hp_percent", "auto_mp_percent", "seal_count", "updated_utc_ms"],
                keyColumns: ["character_id"],
                updateColumns: ["auto_pot", "grade", "hp_item_index", "mp_item_index", "auto_hp_percent", "auto_mp_percent", "seal_count", "updated_utc_ms"]);

            for (var offset = 0; offset < rows.Count; offset += batchSize)
            {
                var take = Math.Min(batchSize, rows.Count - offset);
                var batch = new List<object>(take);

                for (var index = 0; index < take; index++)
                {
                    var row = rows[offset + index];
                    if (row == null) continue;

                    batch.Add(new
                    {
                        character_id = row.CharacterId,
                        auto_pot = row.AutoPot,
                        grade = row.Grade,
                        hp_item_index = row.HpItemIndex,
                        mp_item_index = row.MpItemIndex,
                        auto_hp_percent = row.AutoHpPercent,
                        auto_mp_percent = row.AutoMpPercent,
                        seal_count = row.SealCount,
                        updated_utc_ms = nowMs,
                    });
                }

                if (batch.Count > 0)
                    session.Execute(sql, batch);
            }

            session.Execute("DELETE FROM hero_details WHERE updated_utc_ms <> @nowMs", new { nowMs });
        }

        private static void ReplaceCharacterHeroSlots(SqlSession session, IReadOnlyList<CharacterHeroSlotRow> rows, long saveEpochUtcMs = 0)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));

            var nowMs = saveEpochUtcMs > 0 ? saveEpochUtcMs : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            rows ??= Array.Empty<CharacterHeroSlotRow>();
            var batchSize = Settings.SaveBatchSize <= 0 ? 2000 : Settings.SaveBatchSize;

            var sql = session.Dialect.BuildUpsert(
                tableName: "character_hero_slots",
                insertColumns: ["character_id", "slot_index", "hero_character_id", "updated_utc_ms"],
                keyColumns: ["character_id", "slot_index"],
                updateColumns: ["hero_character_id", "updated_utc_ms"]);

            for (var offset = 0; offset < rows.Count; offset += batchSize)
            {
                var take = Math.Min(batchSize, rows.Count - offset);
                var batch = new List<object>(take);

                for (var index = 0; index < take; index++)
                {
                    var row = rows[offset + index];
                    if (row == null) continue;

                    batch.Add(new
                    {
                        character_id = row.CharacterId,
                        slot_index = row.SlotIndex,
                        hero_character_id = row.HeroCharacterId,
                        updated_utc_ms = nowMs,
                    });
                }

                if (batch.Count > 0)
                    session.Execute(sql, batch);
            }

            session.Execute("DELETE FROM character_hero_slots WHERE updated_utc_ms <> @nowMs", new { nowMs });
        }

        private static void ReplaceCharacterBuffs(
            SqlSession session,
            IReadOnlyList<CharacterBuffRow> rows,
            IReadOnlyList<CharacterBuffStatRow> stats,
            IReadOnlyList<CharacterBuffValueRow> values,
            IReadOnlyList<CharacterBuffDataRow> data,
            long saveEpochUtcMs = 0)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));

            var nowMs = saveEpochUtcMs > 0 ? saveEpochUtcMs : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            rows ??= Array.Empty<CharacterBuffRow>();
            var batchSize = Settings.SaveBatchSize <= 0 ? 2000 : Settings.SaveBatchSize;

            var sql = session.Dialect.BuildUpsert(
                tableName: "character_buffs",
                insertColumns: ["character_id", "list_index", "buff_type", "object_id", "expire_time", "last_time", "next_time", "flag_for_removal", "paused", "updated_utc_ms", "snapshot_generation", "snapshot_active"],
                keyColumns: ["character_id", "list_index"],
                updateColumns: ["buff_type", "object_id", "expire_time", "last_time", "next_time", "flag_for_removal", "paused", "updated_utc_ms", "snapshot_generation", "snapshot_active"]);

            for (var offset = 0; offset < rows.Count; offset += batchSize)
            {
                var take = Math.Min(batchSize, rows.Count - offset);
                var batch = new List<object>(take);

                for (var index = 0; index < take; index++)
                {
                    var row = rows[offset + index];
                    if (row == null) continue;

                    batch.Add(new
                    {
                        character_id = row.CharacterId,
                        list_index = row.ListIndex,
                        buff_type = row.BuffType,
                        object_id = row.ObjectId,
                        expire_time = row.ExpireTime,
                        last_time = row.LastTime,
                        next_time = row.NextTime,
                        flag_for_removal = row.FlagForRemoval,
                        paused = row.Paused,
                        updated_utc_ms = nowMs,
                        snapshot_generation = nowMs,
                        snapshot_active = 1,
                    });
                }

                if (batch.Count > 0)
                    session.Execute(sql, batch);
            }

            foreach (var row in rows)
            {
                session.Execute("DELETE FROM character_buff_stats WHERE character_id=@CharacterId AND list_index=@ListIndex", row);
                session.Execute("DELETE FROM character_buff_values WHERE character_id=@CharacterId AND list_index=@ListIndex", row);
                session.Execute("DELETE FROM character_buff_data WHERE character_id=@CharacterId AND list_index=@ListIndex", row);
            }
            if (stats?.Count > 0)
                session.Execute("INSERT INTO character_buff_stats (character_id,list_index,stat_id,stat_value) VALUES (@CharacterId,@ListIndex,@StatId,@StatValue)", stats);
            if (values?.Count > 0)
                session.Execute("INSERT INTO character_buff_values (character_id,list_index,value_index,value_type,integer_value,real_value,text_value) VALUES (@CharacterId,@ListIndex,@ValueIndex,@ValueType,@IntegerValue,@RealValue,@TextValue)", values);
            if (data?.Count > 0)
                session.Execute("INSERT INTO character_buff_data (character_id,list_index,data_key,data_type,integer_value,real_value,text_value) VALUES (@CharacterId,@ListIndex,@DataKey,@DataType,@IntegerValue,@RealValue,@TextValue)", data);
        }

        private static Dictionary<int, CharacterInfo> BuildCharacterIndex(Envir envir)
        {
            var result = new Dictionary<int, CharacterInfo>();

            if (envir?.CharacterList != null)
            {
                for (var i = 0; i < envir.CharacterList.Count; i++)
                {
                    var character = envir.CharacterList[i];
                    if (character == null) continue;
                    result[character.Index] = character;
                }
            }

            if (envir?.HeroList != null)
            {
                for (var i = 0; i < envir.HeroList.Count; i++)
                {
                    var character = envir.HeroList[i];
                    if (character == null) continue;
                    result[character.Index] = character;
                }
            }

            return result;
        }

        private static void ApplyAuctions(Envir envir, IReadOnlyDictionary<long, UserItem> itemsById, IReadOnlyList<AuctionRow> auctions)
        {
            if (envir == null) throw new ArgumentNullException(nameof(envir));

            itemsById ??= new Dictionary<long, UserItem>();

            lock (Envir.AccountLock)
            {
                for (var i = 0; i < envir.AccountList.Count; i++)
                {
                    var account = envir.AccountList[i];
                    account?.Auctions?.Clear();
                }

                envir.Auctions?.Clear();

                if (auctions == null || auctions.Count == 0)
                    return;

                var characterById = BuildCharacterIndex(envir);

                for (var i = 0; i < auctions.Count; i++)
                {
                    var row = auctions[i];
                    if (row == null) continue;
                    if (row.AuctionId <= 0) continue;

                    if (row.SellerCharacterId <= 0 || row.SellerCharacterId > int.MaxValue) continue;

                    if (!characterById.TryGetValue((int)row.SellerCharacterId, out var seller) || seller == null)
                        continue;

                    CharacterInfo buyer = null;
                    if (row.CurrentBuyerCharacterId > 0 && row.CurrentBuyerCharacterId <= int.MaxValue)
                        characterById.TryGetValue((int)row.CurrentBuyerCharacterId, out buyer);

                    if (!itemsById.TryGetValue(row.ItemId, out var item) || item == null)
                        continue;

                    var auction = new AuctionInfo
                    {
                        AuctionID = (ulong)row.AuctionId,
                        Item = item,
                        ConsignmentDate = FromUtcMsToLocal(row.ConsignmentUtcMs),
                        Price = (uint)Math.Clamp(row.Price, 0, uint.MaxValue),
                        CurrentBid = (uint)Math.Clamp(row.CurrentBid, 0, uint.MaxValue),
                        SellerIndex = (int)row.SellerCharacterId,
                        SellerInfo = seller,
                        CurrentBuyerIndex = (int)Math.Clamp(row.CurrentBuyerCharacterId, 0, int.MaxValue),
                        CurrentBuyerInfo = buyer,
                        Expired = row.Expired != 0,
                        Sold = row.Sold != 0,
                        ItemType = (MarketItemType)row.ItemType,
                    };

                    if (auction.ItemType == MarketItemType.Auction && auction.CurrentBid < auction.Price)
                        auction.CurrentBid = auction.Price;

                    envir.Auctions.AddLast(auction);
                    seller.AccountInfo?.Auctions?.AddLast(auction);
                }
            }
        }

        private static void ApplyMails(
            Envir envir,
            IReadOnlyDictionary<long, UserItem> itemsById,
            IReadOnlyList<MailRow> mails,
            IReadOnlyList<MailItemRow> mailItems)
        {
            if (envir == null) throw new ArgumentNullException(nameof(envir));

            itemsById ??= new Dictionary<long, UserItem>();

            lock (Envir.AccountLock)
            {
                if (envir.CharacterList != null)
                {
                    for (var i = 0; i < envir.CharacterList.Count; i++)
                        envir.CharacterList[i]?.Mail?.Clear();
                }

                if (envir.HeroList != null)
                {
                    for (var i = 0; i < envir.HeroList.Count; i++)
                        envir.HeroList[i]?.Mail?.Clear();
                }

                if (mails == null || mails.Count == 0)
                    return;

                var itemsByMailId = new Dictionary<long, List<MailItemRow>>();
                if (mailItems != null)
                {
                    for (var i = 0; i < mailItems.Count; i++)
                    {
                        var row = mailItems[i];
                        if (row == null) continue;

                        if (!itemsByMailId.TryGetValue(row.MailId, out var list))
                        {
                            list = new List<MailItemRow>();
                            itemsByMailId[row.MailId] = list;
                        }
                        list.Add(row);
                    }
                }

                foreach (var pair in itemsByMailId)
                    pair.Value.Sort((a, b) => a.SlotIndex.CompareTo(b.SlotIndex));

                var characterById = BuildCharacterIndex(envir);

                for (var i = 0; i < mails.Count; i++)
                {
                    var row = mails[i];
                    if (row == null) continue;
                    if (row.MailId <= 0) continue;

                    if (row.RecipientCharacterId <= 0 || row.RecipientCharacterId > int.MaxValue) continue;

                    if (!characterById.TryGetValue((int)row.RecipientCharacterId, out var recipient) || recipient == null)
                        continue;

                    var mail = new MailInfo
                    {
                        MailID = (ulong)row.MailId,
                        Sender = row.SenderName ?? string.Empty,
                        RecipientIndex = (int)row.RecipientCharacterId,
                        RecipientInfo = recipient,
                        Message = row.Message ?? string.Empty,
                        Gold = (uint)Math.Clamp(row.Gold, 0, uint.MaxValue),
                        DateSent = FromUtcMsToLocal(row.DateSentUtcMs),
                        DateOpened = FromUtcMsToLocal(row.DateOpenedUtcMs),
                        Locked = row.Locked != 0,
                        Collected = row.Collected != 0,
                        CanReply = row.CanReply != 0,
                        Items = new List<UserItem>(),
                    };

                    if (itemsByMailId.TryGetValue(row.MailId, out var itemRows))
                    {
                        for (var j = 0; j < itemRows.Count; j++)
                        {
                            var itemRow = itemRows[j];
                            if (itemRow == null) continue;

                            if (!itemsById.TryGetValue(itemRow.ItemId, out var item) || item == null)
                                continue;

                            mail.Items.Add(item);
                        }
                    }

                    recipient.Mail.Add(mail);
                }
            }
        }

        private static void ApplyGameshopLog(Envir envir, IReadOnlyList<GameshopLogRow> rows)
        {
            if (envir == null) throw new ArgumentNullException(nameof(envir));

            lock (Envir.AccountLock)
            {
                envir.GameshopLog ??= new Dictionary<int, int>();
                envir.GameshopLog.Clear();

                if (rows == null || rows.Count == 0)
                    return;

                for (var i = 0; i < rows.Count; i++)
                {
                    var row = rows[i];
                    if (row == null) continue;
                    envir.GameshopLog[row.ItemIndex] = row.Count;
                }
            }
        }

        private static void ApplyRespawnSaves(Envir envir, IReadOnlyList<RespawnSaveRow> rows)
        {
            if (envir == null) throw new ArgumentNullException(nameof(envir));
            if (rows == null || rows.Count == 0) return;

            lock (Envir.LoadLock)
            {
                for (var i = 0; i < rows.Count; i++)
                {
                    var saved = rows[i];
                    if (saved == null) continue;

                    for (var j = 0; j < envir.SavedSpawns.Count; j++)
                    {
                        var respawn = envir.SavedSpawns[j];
                        if (respawn?.Info == null) continue;
                        if (respawn.Info.RespawnIndex != saved.RespawnIndex) continue;

                        if (saved.NextSpawnTick < 0) continue;
                        respawn.NextSpawnTick = (ulong)saved.NextSpawnTick;

                        if (saved.Spawned != 0 && respawn.Info.Count * envir.SpawnMultiplier > respawn.Count)
                        {
                            var mobcount = respawn.Info.Count * envir.SpawnMultiplier - respawn.Count;
                            for (var k = 0; k < mobcount; k++)
                            {
                                respawn.Spawn();
                            }
                        }

                        break;
                    }
                }
            }
        }

        private static void ApplyCharacterMagics(Envir envir, IReadOnlyList<CharacterMagicRow> rows)
        {
            if (envir == null) throw new ArgumentNullException(nameof(envir));

            lock (Envir.AccountLock)
            {
                var characterById = BuildCharacterIndex(envir);

                foreach (var character in characterById.Values)
                    character.Magics = new List<UserMagic>();

                if (rows == null || rows.Count == 0)
                    return;

                for (var index = 0; index < rows.Count; index++)
                {
                    var row = rows[index];
                    if (row == null) continue;
                    if (!characterById.TryGetValue((int)row.CharacterId, out var character) || character == null)
                        continue;

                    var magic = new UserMagic((Spell)row.Spell)
                    {
                        Level = (byte)Math.Clamp(row.MagicLevel, 0, byte.MaxValue),
                        Key = (byte)Math.Clamp(row.MagicKey, 0, byte.MaxValue),
                        Experience = (ushort)Math.Clamp(row.Experience, 0, ushort.MaxValue),
                        IsTempSpell = row.IsTempSpell != 0,
                        CastTime = row.CastTime,
                    };

                    if (magic.Info == null)
                        continue;

                    character.Magics.Add(magic);
                }
            }
        }

        private static void ApplyCharacterCompletedQuests(Envir envir, IReadOnlyList<CharacterCompletedQuestRow> rows)
        {
            if (envir == null) throw new ArgumentNullException(nameof(envir));

            lock (Envir.AccountLock)
            {
                var characterById = BuildCharacterIndex(envir);

                foreach (var character in characterById.Values)
                    character.CompletedQuests = new List<int>();

                if (rows == null || rows.Count == 0)
                    return;

                for (var index = 0; index < rows.Count; index++)
                {
                    var row = rows[index];
                    if (row == null) continue;
                    if (row.QuestId <= 0 || row.QuestId > int.MaxValue) continue;
                    if (!characterById.TryGetValue((int)row.CharacterId, out var character) || character == null)
                        continue;

                    character.CompletedQuests.Add((int)row.QuestId);
                }
            }
        }

        private static void ApplyCharacterFlags(Envir envir, IReadOnlyList<CharacterFlagRow> rows)
        {
            if (envir == null) throw new ArgumentNullException(nameof(envir));

            lock (Envir.AccountLock)
            {
                var characterById = BuildCharacterIndex(envir);

                foreach (var character in characterById.Values)
                    character.Flags = new bool[Globals.FlagIndexCount];

                if (rows == null || rows.Count == 0)
                    return;

                for (var index = 0; index < rows.Count; index++)
                {
                    var row = rows[index];
                    if (row == null) continue;
                    if (!characterById.TryGetValue((int)row.CharacterId, out var character) || character == null)
                        continue;
                    if (row.FlagIndex < 0 || row.FlagIndex >= character.Flags.Length)
                        continue;

                    character.Flags[row.FlagIndex] = row.FlagValue != 0;
                }
            }
        }

        private static void ApplyCharacterGameshopPurchases(Envir envir, IReadOnlyList<CharacterGameshopPurchaseRow> rows)
        {
            if (envir == null) throw new ArgumentNullException(nameof(envir));

            lock (Envir.AccountLock)
            {
                var characterById = BuildCharacterIndex(envir);

                foreach (var character in characterById.Values)
                    character.GSpurchases = new Dictionary<int, int>();

                if (rows == null || rows.Count == 0)
                    return;

                for (var index = 0; index < rows.Count; index++)
                {
                    var row = rows[index];
                    if (row == null) continue;
                    if (!characterById.TryGetValue((int)row.CharacterId, out var character) || character == null)
                        continue;

                    character.GSpurchases[row.ItemIndex] = row.PurchaseCount;
                }
            }
        }

        private static void ApplyCurrentQuests(
            Envir envir,
            IReadOnlyList<CurrentQuestRow> currentQuests,
            IReadOnlyList<CurrentQuestKillTaskRow> currentQuestKillTasks,
            IReadOnlyList<CurrentQuestItemTaskRow> currentQuestItemTasks,
            IReadOnlyList<CurrentQuestFlagTaskRow> currentQuestFlagTasks)
        {
            if (envir == null) throw new ArgumentNullException(nameof(envir));

            lock (Envir.AccountLock)
            {
                var characterById = BuildCharacterIndex(envir);

                foreach (var character in characterById.Values)
                    character.CurrentQuests = new List<QuestProgressInfo>();

                if (currentQuests == null || currentQuests.Count == 0)
                    return;

                var killTasksByQuest = new Dictionary<(long CharacterId, long QuestId), List<CurrentQuestKillTaskRow>>();
                if (currentQuestKillTasks != null)
                {
                    for (var index = 0; index < currentQuestKillTasks.Count; index++)
                    {
                        var row = currentQuestKillTasks[index];
                        if (row == null) continue;

                        var key = (row.CharacterId, row.QuestId);
                        if (!killTasksByQuest.TryGetValue(key, out var list))
                        {
                            list = new List<CurrentQuestKillTaskRow>();
                            killTasksByQuest[key] = list;
                        }

                        list.Add(row);
                    }
                }

                var itemTasksByQuest = new Dictionary<(long CharacterId, long QuestId), List<CurrentQuestItemTaskRow>>();
                if (currentQuestItemTasks != null)
                {
                    for (var index = 0; index < currentQuestItemTasks.Count; index++)
                    {
                        var row = currentQuestItemTasks[index];
                        if (row == null) continue;

                        var key = (row.CharacterId, row.QuestId);
                        if (!itemTasksByQuest.TryGetValue(key, out var list))
                        {
                            list = new List<CurrentQuestItemTaskRow>();
                            itemTasksByQuest[key] = list;
                        }

                        list.Add(row);
                    }
                }

                var flagTasksByQuest = new Dictionary<(long CharacterId, long QuestId), List<CurrentQuestFlagTaskRow>>();
                if (currentQuestFlagTasks != null)
                {
                    for (var index = 0; index < currentQuestFlagTasks.Count; index++)
                    {
                        var row = currentQuestFlagTasks[index];
                        if (row == null) continue;

                        var key = (row.CharacterId, row.QuestId);
                        if (!flagTasksByQuest.TryGetValue(key, out var list))
                        {
                            list = new List<CurrentQuestFlagTaskRow>();
                            flagTasksByQuest[key] = list;
                        }

                        list.Add(row);
                    }
                }

                for (var index = 0; index < currentQuests.Count; index++)
                {
                    var row = currentQuests[index];
                    if (row == null) continue;
                    if (row.CharacterId <= 0 || row.CharacterId > int.MaxValue) continue;
                    if (row.QuestId <= 0 || row.QuestId > int.MaxValue) continue;

                    if (!characterById.TryGetValue((int)row.CharacterId, out var character) || character == null)
                        continue;

                    if (character.CurrentQuests.Any(existing => existing?.Index == row.QuestId))
                        continue;

                    var quest = new QuestProgressInfo((int)row.QuestId)
                    {
                        StartDateTime = FromUtcMsToLocal(row.StartUtcMs),
                        EndDateTime = FromUtcMsToLocal(row.EndUtcMs),
                    };

                    if (killTasksByQuest.TryGetValue((row.CharacterId, row.QuestId), out var killRows))
                    {
                        for (var taskIndex = 0; taskIndex < killRows.Count; taskIndex++)
                        {
                            var taskRow = killRows[taskIndex];
                            if (taskRow == null) continue;

                            for (var progressIndex = 0; progressIndex < quest.KillTaskCount.Count; progressIndex++)
                            {
                                var progress = quest.KillTaskCount[progressIndex];
                                if (progress == null) continue;
                                if (progress.MonsterID != taskRow.MonsterId) continue;

                                progress.Count = taskRow.TaskCount;
                                break;
                            }
                        }
                    }

                    if (itemTasksByQuest.TryGetValue((row.CharacterId, row.QuestId), out var itemRows))
                    {
                        for (var taskIndex = 0; taskIndex < itemRows.Count; taskIndex++)
                        {
                            var taskRow = itemRows[taskIndex];
                            if (taskRow == null) continue;

                            for (var progressIndex = 0; progressIndex < quest.ItemTaskCount.Count; progressIndex++)
                            {
                                var progress = quest.ItemTaskCount[progressIndex];
                                if (progress == null) continue;
                                if (progress.ItemID != taskRow.ItemId) continue;

                                progress.Count = taskRow.TaskCount;
                                break;
                            }
                        }
                    }

                    if (flagTasksByQuest.TryGetValue((row.CharacterId, row.QuestId), out var flagRows))
                    {
                        for (var taskIndex = 0; taskIndex < flagRows.Count; taskIndex++)
                        {
                            var taskRow = flagRows[taskIndex];
                            if (taskRow == null) continue;

                            for (var progressIndex = 0; progressIndex < quest.FlagTaskSet.Count; progressIndex++)
                            {
                                var progress = quest.FlagTaskSet[progressIndex];
                                if (progress == null) continue;
                                if (progress.Number != taskRow.FlagNumber) continue;

                                progress.State = taskRow.FlagState != 0;
                                break;
                            }
                        }
                    }

                    character.CurrentQuests.Add(quest);
                }
            }
        }

        private static void ApplyCharacterPets(Envir envir, IReadOnlyList<CharacterPetRow> rows)
        {
            if (envir == null) throw new ArgumentNullException(nameof(envir));

            lock (Envir.AccountLock)
            {
                var characterById = BuildCharacterIndex(envir);

                foreach (var character in characterById.Values)
                    character.Pets = new List<PetInfo>();

                if (rows == null || rows.Count == 0)
                    return;

                for (var index = 0; index < rows.Count; index++)
                {
                    var row = rows[index];
                    if (row == null) continue;
                    if (!characterById.TryGetValue((int)row.CharacterId, out var character) || character == null)
                        continue;

                    character.Pets.Add(new PetInfo
                    {
                        MonsterIndex = row.MonsterId,
                        HP = row.Hp,
                        Experience = (uint)Math.Clamp(row.Experience, 0, uint.MaxValue),
                        Level = (byte)Math.Clamp(row.PetLevel, 0, byte.MaxValue),
                        MaxPetLevel = (byte)Math.Clamp(row.MaxPetLevel, 0, byte.MaxValue),
                    });
                }
            }
        }

        private static void ApplyCharacterFriends(Envir envir, IReadOnlyList<CharacterFriendRow> rows)
        {
            if (envir == null) throw new ArgumentNullException(nameof(envir));

            lock (Envir.AccountLock)
            {
                var characterById = BuildCharacterIndex(envir);

                foreach (var character in characterById.Values)
                    character.Friends = new List<FriendInfo>();

                if (rows == null || rows.Count == 0)
                    return;

                for (var index = 0; index < rows.Count; index++)
                {
                    var row = rows[index];
                    if (row == null) continue;
                    if (row.FriendCharacterId <= 0 || row.FriendCharacterId > int.MaxValue) continue;
                    if (!characterById.TryGetValue((int)row.CharacterId, out var character) || character == null)
                        continue;
                    if (!characterById.TryGetValue((int)row.FriendCharacterId, out var friendCharacter) || friendCharacter == null)
                        continue;

                    var friend = new FriendInfo(friendCharacter, row.Blocked != 0)
                    {
                        Memo = row.Memo ?? string.Empty,
                    };

                    character.Friends.Add(friend);
                }
            }
        }

        private static void ApplyCharacterRentedItems(Envir envir, IReadOnlyList<CharacterRentedItemRow> rows)
        {
            if (envir == null) throw new ArgumentNullException(nameof(envir));

            lock (Envir.AccountLock)
            {
                var characterById = BuildCharacterIndex(envir);
                var characterByName = new Dictionary<string, CharacterInfo>(StringComparer.OrdinalIgnoreCase);

                foreach (var pair in characterById)
                {
                    var character = pair.Value;
                    if (character == null) continue;

                    character.RentedItems = new List<ItemRentalInformation>();
                    character.RentedItemsToRemove = new List<ItemRentalInformation>();
                    character.HasRentedItem = false;

                    if (!string.IsNullOrWhiteSpace(character.Name))
                        characterByName[character.Name] = character;
                }

                if (rows == null || rows.Count == 0)
                    return;

                for (var index = 0; index < rows.Count; index++)
                {
                    var row = rows[index];
                    if (row == null) continue;
                    if (!characterById.TryGetValue((int)row.CharacterId, out var owner) || owner == null)
                        continue;

                    owner.RentedItems.Add(new ItemRentalInformation
                    {
                        ItemId = (ulong)Math.Max(0, row.ItemId),
                        ItemName = row.ItemName ?? string.Empty,
                        RentingPlayerName = row.RentingPlayerName ?? string.Empty,
                        ItemReturnDate = FromUtcMsToLocal(row.ItemReturnUtcMs),
                    });

                    if (!string.IsNullOrWhiteSpace(row.RentingPlayerName) && characterByName.TryGetValue(row.RentingPlayerName, out var rentingCharacter) && rentingCharacter != null)
                    {
                        rentingCharacter.HasRentedItem = true;
                    }
                }
            }
        }

        private static void ApplyCharacterIntelligentCreatures(Envir envir, IReadOnlyList<CharacterIntelligentCreatureRow> rows)
        {
            if (envir == null) throw new ArgumentNullException(nameof(envir));

            lock (Envir.AccountLock)
            {
                var characterById = BuildCharacterIndex(envir);

                foreach (var character in characterById.Values)
                    character.IntelligentCreatures = new List<UserIntelligentCreature>();

                if (rows == null || rows.Count == 0)
                    return;

                for (var index = 0; index < rows.Count; index++)
                {
                    var row = rows[index];
                    if (row == null) continue;
                    if (!characterById.TryGetValue((int)row.CharacterId, out var character) || character == null)
                        continue;

                    var slotIndex = Math.Max(0, row.SlotIndex);
                    var creature = new UserIntelligentCreature((IntelligentCreatureType)row.PetType, slotIndex)
                    {
                        CustomName = row.CustomName ?? string.Empty,
                        Fullness = row.Fullness,
                        SlotIndex = slotIndex,
                        Expire = FromUtcMsToLocal(row.ExpireUtcMs),
                        BlackstoneTime = row.BlackstoneTime,
                        petMode = (IntelligentCreaturePickupMode)row.PickupMode,
                        MaintainFoodTime = row.MaintainFoodTime,
                        Filter = new IntelligentCreatureItemFilter
                        {
                            PetPickupAll = row.FilterPickupAll != 0,
                            PetPickupGold = row.FilterPickupGold != 0,
                            PetPickupWeapons = row.FilterPickupWeapons != 0,
                            PetPickupArmours = row.FilterPickupArmours != 0,
                            PetPickupHelmets = row.FilterPickupHelmets != 0,
                            PetPickupBoots = row.FilterPickupBoots != 0,
                            PetPickupBelts = row.FilterPickupBelts != 0,
                            PetPickupAccessories = row.FilterPickupAccessories != 0,
                            PetPickupOthers = row.FilterPickupOthers != 0,
                            PickupGrade = (ItemGrade)row.FilterPickupGrade,
                        }
                    };

                    if (creature.Info == null)
                        continue;

                    character.IntelligentCreatures.Add(creature);
                }
            }
        }

        private static void ApplyHeroDetails(Envir envir, IReadOnlyList<HeroDetailRow> rows)
        {
            if (envir == null) throw new ArgumentNullException(nameof(envir));

            lock (Envir.AccountLock)
            {
                var characterById = BuildCharacterIndex(envir);

                if (rows == null || rows.Count == 0)
                    return;

                for (var index = 0; index < rows.Count; index++)
                {
                    var row = rows[index];
                    if (row == null) continue;
                    if (!characterById.TryGetValue((int)row.CharacterId, out var character) || character is not HeroInfo hero)
                        continue;

                    hero.AutoPot = row.AutoPot != 0;
                    hero.Grade = (byte)Math.Clamp(row.Grade, 0, byte.MaxValue);
                    hero.HPItemIndex = row.HpItemIndex;
                    hero.MPItemIndex = row.MpItemIndex;
                    hero.AutoHPPercent = (byte)Math.Clamp(row.AutoHpPercent, 0, byte.MaxValue);
                    hero.AutoMPPercent = (byte)Math.Clamp(row.AutoMpPercent, 0, byte.MaxValue);
                    hero.SealCount = (ushort)Math.Clamp(row.SealCount, 0, ushort.MaxValue);
                }
            }
        }

        private static void ApplyCharacterHeroSlots(Envir envir, IReadOnlyList<CharacterHeroSlotRow> rows)
        {
            if (envir == null) throw new ArgumentNullException(nameof(envir));

            lock (Envir.AccountLock)
            {
                if (envir.CharacterList == null || envir.CharacterList.Count == 0)
                    return;

                var characterById = BuildCharacterIndex(envir);

                for (var index = 0; index < envir.CharacterList.Count; index++)
                {
                    var character = envir.CharacterList[index];
                    if (character == null) continue;

                    var slotCount = Math.Max(1, character.MaximumHeroCount);
                    character.Heroes = new HeroInfo[slotCount];
                }

                if (rows == null || rows.Count == 0)
                    return;

                for (var index = 0; index < rows.Count; index++)
                {
                    var row = rows[index];
                    if (row == null) continue;
                    if (row.CharacterId <= 0 || row.CharacterId > int.MaxValue) continue;
                    if (row.HeroCharacterId <= 0 || row.HeroCharacterId > int.MaxValue) continue;
                    if (!characterById.TryGetValue((int)row.CharacterId, out var character) || character == null) continue;
                    if (!characterById.TryGetValue((int)row.HeroCharacterId, out var heroCharacter) || heroCharacter is not HeroInfo hero) continue;
                    if (row.SlotIndex < 0 || row.SlotIndex >= character.Heroes.Length) continue;

                    character.Heroes[row.SlotIndex] = hero;
                }
            }
        }

        private static void ApplyCharacterBuffs(
            Envir envir,
            IReadOnlyList<CharacterBuffRow> rows,
            IReadOnlyList<CharacterBuffStatRow> stats,
            IReadOnlyList<CharacterBuffValueRow> values,
            IReadOnlyList<CharacterBuffDataRow> data)
        {
            if (envir == null) throw new ArgumentNullException(nameof(envir));

            lock (Envir.AccountLock)
            {
                var characterById = BuildCharacterIndex(envir);

                foreach (var character in characterById.Values)
                    character.Buffs = new List<Buff>();

                if (rows == null || rows.Count == 0)
                    return;

                for (var index = 0; index < rows.Count; index++)
                {
                    var row = rows[index];
                    if (row == null) continue;
                    if (row.CharacterId <= 0 || row.CharacterId > int.MaxValue) continue;
                    if (!characterById.TryGetValue((int)row.CharacterId, out var character) || character == null)
                        continue;

                    var buff = new Buff((BuffType)row.BuffType)
                    {
                        ObjectID = (uint)Math.Clamp(row.ObjectId, 0, uint.MaxValue),
                        ExpireTime = row.ExpireTime,
                        LastTime = row.LastTime,
                        NextTime = row.NextTime,
                        FlagForRemoval = row.FlagForRemoval != 0,
                        Paused = row.Paused != 0,
                        Stats = new Stats(),
                    };
                    foreach (var stat in stats.Where(item => item.CharacterId == row.CharacterId && item.ListIndex == row.ListIndex))
                        buff.Stats[(Stat)stat.StatId] = (int)Math.Clamp(stat.StatValue, int.MinValue, int.MaxValue);
                    buff.Values = values
                        .Where(item => item.CharacterId == row.CharacterId && item.ListIndex == row.ListIndex)
                        .OrderBy(item => item.ValueIndex)
                        .Select(item => (int)Math.Clamp(item.IntegerValue ?? 0, int.MinValue, int.MaxValue))
                        .ToArray();
                    foreach (var item in data.Where(item => item.CharacterId == row.CharacterId && item.ListIndex == row.ListIndex))
                        buff.RestoreData(item.DataKey, FromBuffDataRow(item));
                    character.Buffs.Add(buff);
                }
            }
        }

        private static object FromBuffDataRow(CharacterBuffDataRow row) => row.DataType switch
        {
            "bool" => row.IntegerValue != 0,
            "int64" => row.IntegerValue ?? 0,
            "real" => row.RealValue ?? 0,
            "text" => row.TextValue ?? string.Empty,
            _ => throw new InvalidDataException($"未知 Buff typed data 类型：{row.DataType}"),
        };

        public bool LoadWorld(Envir envir)
        {
            if (envir == null) throw new ArgumentNullException(nameof(envir));

            EnsureInitialized();
            using var session = SqlSession.Open(_provider, _worldOptions, maxRetries: 3, baseRetryDelayMs: 200);
            var snapshot = SqlWorldRelationsLoader.LoadAll(session)
                ?? throw new InvalidOperationException("world.db 未包含完整的关系化 World Definition，拒绝二进制回退。");
            SqlWorldRelationsLoader.RestoreToEnvir(envir, snapshot);
            return true;
        }

        public void SaveWorld(Envir envir)
        {
            if (envir == null) throw new ArgumentNullException(nameof(envir));

            EnsureInitialized();

            try
            {
                var runner = new SqlDomainTransactionRunner(_provider, _worldOptions);
                var result = runner.RunWithSnapshot(
                    domain: SqlSaveDomain.WorldRelations,
                    snapshotFactory: () => SqlWorldRelationsStore.Capture(envir),
                    work: (session, snapshot) =>
                    {
                        ValidateWorldReferences(snapshot);
                        SqlWorldRelationsStore.ReplaceAll(session, snapshot);
                    });
                if (!result.Success)
                    throw result.Exception ?? new InvalidOperationException("World Definition 提交失败。");
            }
            catch (Exception ex)
            {
                // 保持与 legacy 保存一致：保存失败不应直接终止服务器主循环。
                MessageQueue.Instance.Enqueue($"[SQL:{_provider}] World 保存异常：{ex}");
            }
        }

        public void LoadAccounts(Envir envir)
        {
            if (envir == null) throw new ArgumentNullException(nameof(envir));

            EnsureInitialized();
            EnsureCharacterAccountRoots();
            ValidateCharacterSnapshotReadable();
            LoadAccountsAtomically(envir);
        }

        private void EnsureCharacterAccountRoots()
        {
            IReadOnlyList<long> accountIds;
            using (var identitySession = SqlSession.Open(_provider, _identityOptions, maxRetries: 3, baseRetryDelayMs: 200))
                accountIds = identitySession.Query<long>("SELECT account_id FROM accounts ORDER BY account_id");

            if (accountIds.Count == 0) return;

            var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            using var characterSession = SqlSession.Open(_provider, _databaseOptions, maxRetries: 3, baseRetryDelayMs: 200);
            characterSession.RunInTransaction(s =>
            {
                var walletSql = s.Dialect.BuildUpsert(
                    "account_wallets",
                    ["account_id", "gold", "credit", "updated_utc_ms"],
                    ["account_id"],
                    Array.Empty<string>());
                var storageSql = s.Dialect.BuildUpsert(
                    "account_storage",
                    ["account_id", "slot_count", "has_expanded_storage", "expanded_storage_expiry_utc_ms", "updated_utc_ms"],
                    ["account_id"],
                    Array.Empty<string>());

                s.Execute(walletSql, accountIds.Select(accountId => new { account_id = accountId, gold = 0, credit = 0, updated_utc_ms = nowMs }).ToArray());
                s.Execute(storageSql, accountIds.Select(accountId => new
                {
                    account_id = accountId,
                    slot_count = Globals.StorageGridSize,
                    has_expanded_storage = 0,
                    expanded_storage_expiry_utc_ms = 0,
                    updated_utc_ms = nowMs,
                }).ToArray());

                if (TryLoadServerMetaInt64(s, ServerMetaKeyAccountsRelationsEpochUtcMs) <= 0)
                    UpsertServerMeta(s, ServerMetaKeyAccountsRelationsEpochUtcMs, nowMs.ToString(), nowMs);
            });
        }

        private void ValidateCharacterSnapshotReadable()
        {
            var requiredTables = new[]
            {
                "next_ids", "characters", "item_instances", "item_added_stats", "item_awake_levels", "item_slot_links",
                "account_storage", "account_storage_slots", "character_containers", "character_container_slots", "auctions", "mails", "mail_items",
                "gameshop_log", "respawn_saves", "character_magics", "character_completed_quests", "character_flags",
                "character_gameshop_purchases", "character_current_quests", "character_current_quest_kill_tasks", "character_current_quest_item_tasks",
                "character_current_quest_flag_tasks", "character_pets", "character_friends", "character_rented_items",
                "character_intelligent_creatures", "hero_details", "character_hero_slots", "character_buffs",
                "character_buff_stats", "character_buff_values", "character_buff_data",
                "account_wallets", "item_locations", "conquest_runtime", "conquest_facilities",
                "guilds", "guild_ranks", "guild_members", "guild_notices", "guild_buffs", "guild_storage_slots",
                "npc_buybacks", "npc_used_goods",
            };

            using (var identitySession = SqlSession.Open(_provider, _identityOptions, maxRetries: 3, baseRetryDelayMs: 200))
                identitySession.RunInTransaction(s => s.ExecuteScalar<long>("SELECT COUNT(*) FROM accounts"));

            using var session = SqlSession.Open(_provider, _databaseOptions, maxRetries: 3, baseRetryDelayMs: 200);
            session.RunInTransaction(s =>
            {
                foreach (var table in requiredTables)
                    s.ExecuteScalar<long>($"SELECT COUNT(*) FROM {table}");

                var accountCount = s.ExecuteScalar<long>("SELECT COUNT(*) FROM account_wallets");
                var characterCount = s.ExecuteScalar<long>("SELECT COUNT(*) FROM characters");
                var epoch = TryLoadServerMetaInt64(s, ServerMetaKeyAccountsRelationsEpochUtcMs);
                if ((accountCount > 0 || characterCount > 0) && epoch <= 0)
                    throw new InvalidOperationException("Character 关系快照缺少完成 epoch，拒绝应用可能不完整的数据。");
            });
        }

        public void BeginSaveAccounts(Envir envir)
        {
            SaveAccounts(envir);
        }

        public CommitResult SaveAccounts(Envir envir)
        {
            if (envir == null) throw new ArgumentNullException(nameof(envir));

            EnsureInitialized();

            try
            {
                GuildRuntimeSnapshot guildRuntime = null;
                NpcGoodsSnapshot npcGoods = null;
                var runner = new SqlDomainTransactionRunner(_provider, _databaseOptions);
                var result = runner.RunWithSnapshot(
                    domain: SqlSaveDomain.Accounts,
                    snapshotFactory: () =>
                    {
                        var saveEpochUtcMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

                        CaptureItems(envir, out var items, out var itemStats, out var awakeLevels, out var slotLinks);
                        CaptureContainers(envir, out var storage, out var storageSlots, out var containers, out var containerSlots);
                        var auctions = CaptureAuctions(envir);
                        CaptureMails(envir, out var mails, out var mailItems);
                        guildRuntime = CaptureGuildRuntime(envir);
                        npcGoods = CaptureNpcGoods(envir);
                        var itemLocations = CaptureItemLocations(items, storageSlots, containerSlots, mailItems, auctions, slotLinks, guildRuntime.StorageSlots, npcGoods.Buybacks, npcGoods.UsedGoods);
                        var gameshopLog = CaptureGameshopLog(envir);
                        var respawnSaves = CaptureRespawnSaves(envir);
                        var characterMagics = CaptureCharacterMagics(envir);
                        var characterCompletedQuests = CaptureCharacterCompletedQuests(envir);
                        var characterFlags = CaptureCharacterFlags(envir);
                        var characterGameshopPurchases = CaptureCharacterGameshopPurchases(envir);
                        CaptureCurrentQuests(envir, out var currentQuests, out var currentQuestKillTasks, out var currentQuestItemTasks, out var currentQuestFlagTasks);
                        var characterPets = CaptureCharacterPets(envir);
                        var characterFriends = CaptureCharacterFriends(envir);
                        var characterRentedItems = CaptureCharacterRentedItems(envir);
                        var characterIntelligentCreatures = CaptureCharacterIntelligentCreatures(envir);
                        var heroDetails = CaptureHeroDetails(envir);
                        var characterHeroSlots = CaptureCharacterHeroSlots(envir);
                        var characterBuffs = CaptureCharacterBuffs(envir, out var characterBuffStats, out var characterBuffValues, out var characterBuffData);
                        CaptureConquestRuntime(envir, out var conquestRuntime, out var conquestFacilities);

                        if (Settings.TestServer)
                        {
                            MessageQueue.Instance.EnqueueDebugging($"[SQL:{_provider}] AccountsSnapshot：items={items.Count} auctions={auctions.Count} mails={mails.Count} mailItems={mailItems.Count} gameshopLog={gameshopLog.Count} respawnSaves={respawnSaves.Count} magics={characterMagics.Count} completedQuests={characterCompletedQuests.Count} flags={characterFlags.Count} gsPurchases={characterGameshopPurchases.Count} currentQuests={currentQuests.Count} pets={characterPets.Count} friends={characterFriends.Count} rented={characterRentedItems.Count} creatures={characterIntelligentCreatures.Count} heroDetails={heroDetails.Count} heroSlots={characterHeroSlots.Count} buffs={characterBuffs.Count}");
                        }

                        return new AccountsSnapshot(
                            saveEpochUtcMs: saveEpochUtcMs,
                            nextIds: CaptureAccountsNextIds(envir),
                            accounts: CaptureAccounts(envir),
                            characters: CaptureCharacters(envir),
                            items: items,
                            itemAddedStats: itemStats,
                            itemAwakeLevels: awakeLevels,
                            itemSlotLinks: slotLinks,
                            itemLocations: itemLocations,
                            accountStorage: storage,
                            accountStorageSlots: storageSlots,
                            characterContainers: containers,
                            characterContainerSlots: containerSlots,
                            auctions: auctions,
                            mails: mails,
                            mailItems: mailItems,
                            gameshopLog: gameshopLog,
                            respawnSaves: respawnSaves,
                            characterMagics: characterMagics,
                            characterCompletedQuests: characterCompletedQuests,
                            characterFlags: characterFlags,
                            characterGameshopPurchases: characterGameshopPurchases,
                            currentQuests: currentQuests,
                            currentQuestKillTasks: currentQuestKillTasks,
                            currentQuestItemTasks: currentQuestItemTasks,
                            currentQuestFlagTasks: currentQuestFlagTasks,
                            characterPets: characterPets,
                            characterFriends: characterFriends,
                            characterRentedItems: characterRentedItems,
                            characterIntelligentCreatures: characterIntelligentCreatures,
                            heroDetails: heroDetails,
                            characterHeroSlots: characterHeroSlots,
                            characterBuffs: characterBuffs,
                            characterBuffStats: characterBuffStats,
                            characterBuffValues: characterBuffValues,
                            characterBuffData: characterBuffData,
                            conquestRuntime: conquestRuntime,
                            conquestFacilities: conquestFacilities);
                    },
                    work: (session, snapshot) =>
                    {
                        void RunStep(string label, Action action)
                        {
                            if (action == null) return;

                            try
                            {
                                action();
                            }
                            catch (Exception ex)
                            {
                                MessageQueue.Instance.EnqueueDebugging($"[SQL:{_provider}] {label} 保存失败（事务将回滚）：{ex}");
                                throw;
                            }
                        }

                        RunStep("NextIds", () => UpsertNextIds(session, snapshot.NextIds));
                        RunStep("AccountWallets", () => UpsertAccountWallets(session, snapshot.Accounts, snapshot.SaveEpochUtcMs));
                        RunStep("Characters", () => UpsertCharacters(session, snapshot.Characters, snapshot.SaveEpochUtcMs));
                        RunStep("Items", () => ReplaceItems(session, snapshot.Items, snapshot.ItemAddedStats, snapshot.ItemAwakeLevels, snapshot.ItemSlotLinks, snapshot.SaveEpochUtcMs));
                        RunStep("ItemLocations", () => UpsertItemLocations(session, snapshot.ItemLocations, snapshot.SaveEpochUtcMs));
                        RunStep("Containers", () => ReplaceContainers(session, snapshot.AccountStorage, snapshot.AccountStorageSlots, snapshot.CharacterContainers, snapshot.CharacterContainerSlots, snapshot.SaveEpochUtcMs));
                        RunStep("Auctions", () => ReplaceAuctions(session, snapshot.Auctions, snapshot.SaveEpochUtcMs));
                        RunStep("Mails", () => ReplaceMails(session, snapshot.Mails, snapshot.MailItems, snapshot.SaveEpochUtcMs));
                        RunStep("GuildRuntime", () => UpsertGuildRuntime(session, guildRuntime, snapshot.SaveEpochUtcMs));
                        RunStep("NpcGoods", () => UpsertNpcGoods(session, npcGoods, snapshot.SaveEpochUtcMs));
                        RunStep("GameshopLog", () => ReplaceGameshopLog(session, snapshot.GameshopLog, snapshot.SaveEpochUtcMs));
                        RunStep("RespawnSaves", () => ReplaceRespawnSaves(session, snapshot.RespawnSaves, snapshot.SaveEpochUtcMs));
                        RunStep("CharacterMagics", () => ReplaceCharacterMagics(session, snapshot.CharacterMagics, snapshot.SaveEpochUtcMs));
                        RunStep("CharacterCompletedQuests", () => ReplaceCharacterCompletedQuests(session, snapshot.CharacterCompletedQuests, snapshot.SaveEpochUtcMs));
                        RunStep("CharacterFlags", () => ReplaceCharacterFlags(session, snapshot.CharacterFlags, snapshot.SaveEpochUtcMs));
                        RunStep("CharacterGameshopPurchases", () => ReplaceCharacterGameshopPurchases(session, snapshot.CharacterGameshopPurchases, snapshot.SaveEpochUtcMs));
                        RunStep("CurrentQuests", () => ReplaceCurrentQuests(session, snapshot.CurrentQuests, snapshot.CurrentQuestKillTasks, snapshot.CurrentQuestItemTasks, snapshot.CurrentQuestFlagTasks, snapshot.SaveEpochUtcMs));
                        RunStep("CharacterPets", () => ReplaceCharacterPets(session, snapshot.CharacterPets, snapshot.SaveEpochUtcMs));
                        RunStep("CharacterFriends", () => ReplaceCharacterFriends(session, snapshot.CharacterFriends, snapshot.SaveEpochUtcMs));
                        RunStep("CharacterRentedItems", () => ReplaceCharacterRentedItems(session, snapshot.CharacterRentedItems, snapshot.SaveEpochUtcMs));
                        RunStep("CharacterIntelligentCreatures", () => ReplaceCharacterIntelligentCreatures(session, snapshot.CharacterIntelligentCreatures, snapshot.SaveEpochUtcMs));
                        RunStep("HeroDetails", () => ReplaceHeroDetails(session, snapshot.HeroDetails, snapshot.SaveEpochUtcMs));
                        RunStep("CharacterHeroSlots", () => ReplaceCharacterHeroSlots(session, snapshot.CharacterHeroSlots, snapshot.SaveEpochUtcMs));
                        RunStep("CharacterBuffs", () => ReplaceCharacterBuffs(session, snapshot.CharacterBuffs, snapshot.CharacterBuffStats, snapshot.CharacterBuffValues, snapshot.CharacterBuffData, snapshot.SaveEpochUtcMs));
                        RunStep("ConquestRuntime", () => UpsertConquestRuntime(session, snapshot.ConquestRuntime, snapshot.ConquestFacilities, snapshot.SaveEpochUtcMs));
                        RunStep(
                            "AccountsRelationsEpoch",
                            () => UpsertServerMeta(session, ServerMetaKeyAccountsRelationsEpochUtcMs, snapshot.SaveEpochUtcMs.ToString(), snapshot.SaveEpochUtcMs));
                    });

                return new CommitResult
                {
                    Committed = result.Success,
                    Generation = result.Success ? Interlocked.Increment(ref _generation) : Volatile.Read(ref _generation),
                    Retryable = result.Exception != null && SqlTransientDetector.IsTransient(_provider, result.Exception),
                    ErrorCode = result.Success ? string.Empty : "character_commit_failed",
                    Diagnostics = result.Exception?.ToString() ?? string.Empty,
                };
            }
            catch (Exception ex)
            {
                MessageQueue.Instance.Enqueue($"[SQL:{_provider}] Accounts 保存异常：{ex}");
                return PersistenceResult.Failure<CommitResult>(
                    "character_commit_failed",
                    ex,
                    SqlTransientDetector.IsTransient(_provider, ex));
            }
        }

        private IdentityResult PersistIdentitySnapshot(Envir envir)
        {
            var runner = new SqlDomainTransactionRunner(_provider, _identityOptions);
            var result = runner.RunWithSnapshot(
                SqlSaveDomain.Accounts,
                () => CaptureAccounts(envir),
                (session, accounts) => UpsertAccounts(session, accounts));

            return new IdentityResult
            {
                Committed = result.Success,
                Generation = Volatile.Read(ref _generation),
                Retryable = result.Exception != null && SqlTransientDetector.IsTransient(_provider, result.Exception),
                ErrorCode = result.Success ? string.Empty : "identity_commit_failed",
                Diagnostics = result.Exception?.ToString() ?? string.Empty,
            };
        }

        private static void CaptureConquestRuntime(
            Envir envir,
            out IReadOnlyList<ConquestRuntimeRow> runtimeRows,
            out IReadOnlyList<ConquestFacilityRow> facilityRows)
        {
            var runtime = new List<ConquestRuntimeRow>();
            var facilities = new List<ConquestFacilityRow>();
            foreach (var conquest in envir.ConquestList ?? new List<ConquestGuildInfo>())
            {
                if (conquest?.Info == null) continue;
                runtime.Add(new ConquestRuntimeRow
                {
                    ConquestId = conquest.Info.Index,
                    OwnerGuildId = conquest.Owner,
                    AttackerGuildId = conquest.AttackerID,
                    Treasury = conquest.GoldStorage,
                    TaxRate = conquest.NPCRate,
                });

                foreach (var wall in conquest.WallList)
                    facilities.Add(Facility(conquest.Info.Index, "wall", wall.Index, wall.Wall?.HP ?? wall.Health, wall.Wall?.Stats[Stat.HP] ?? Math.Max(0, wall.Health)));
                foreach (var gate in conquest.GateList)
                    facilities.Add(Facility(conquest.Info.Index, "gate", gate.Index, gate.Gate?.HP ?? gate.Health, gate.Gate?.Stats[Stat.HP] ?? Math.Max(0, gate.Health)));
                foreach (var siege in conquest.SiegeList)
                    facilities.Add(Facility(conquest.Info.Index, "siege", siege.Index, siege.Gate?.HP ?? siege.Health, siege.Gate?.Stats[Stat.HP] ?? Math.Max(0, siege.Health)));
                foreach (var archer in conquest.ArcherList)
                    facilities.Add(Facility(conquest.Info.Index, "archer", archer.Index, archer.ArcherMonster != null && !archer.ArcherMonster.Dead ? 1 : archer.Alive ? 1 : 0, 1));
            }

            runtimeRows = runtime;
            facilityRows = facilities;
        }

        private static ConquestFacilityRow Facility(long conquestId, string kind, int index, long hp, long maxHp)
        {
            return new ConquestFacilityRow
            {
                ConquestId = conquestId,
                FacilityKind = kind,
                FacilityIndex = index,
                CurrentHp = Math.Max(0, hp),
                MaxHp = Math.Max(0, maxHp),
            };
        }

        private static void UpsertConquestRuntime(
            SqlSession session,
            IReadOnlyList<ConquestRuntimeRow> runtime,
            IReadOnlyList<ConquestFacilityRow> facilities,
            long saveEpochUtcMs)
        {
            runtime ??= Array.Empty<ConquestRuntimeRow>();
            facilities ??= Array.Empty<ConquestFacilityRow>();
            var nowMs = saveEpochUtcMs > 0 ? saveEpochUtcMs : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var runtimeSql = session.Dialect.BuildUpsert(
                "conquest_runtime",
                ["conquest_id", "owner_guild_id", "attacker_guild_id", "treasury", "tax_rate", "updated_utc_ms", "snapshot_generation", "snapshot_active"],
                ["conquest_id"],
                ["owner_guild_id", "attacker_guild_id", "treasury", "tax_rate", "updated_utc_ms", "snapshot_generation", "snapshot_active"]);
            if (runtime.Count > 0)
                session.Execute(runtimeSql, runtime.Select(row => new
                {
                    conquest_id = row.ConquestId,
                    owner_guild_id = row.OwnerGuildId,
                    attacker_guild_id = row.AttackerGuildId,
                    treasury = row.Treasury,
                    tax_rate = row.TaxRate,
                    updated_utc_ms = nowMs,
                    snapshot_generation = nowMs,
                    snapshot_active = 1,
                }).ToArray());

            var facilitySql = session.Dialect.BuildUpsert(
                "conquest_facilities",
                ["conquest_id", "facility_kind", "facility_index", "current_hp", "max_hp", "updated_utc_ms", "snapshot_generation", "snapshot_active"],
                ["conquest_id", "facility_kind", "facility_index"],
                ["current_hp", "max_hp", "updated_utc_ms", "snapshot_generation", "snapshot_active"]);
            if (facilities.Count > 0)
                session.Execute(facilitySql, facilities.Select(row => new
                {
                    conquest_id = row.ConquestId,
                    facility_kind = row.FacilityKind,
                    facility_index = row.FacilityIndex,
                    current_hp = row.CurrentHp,
                    max_hp = row.MaxHp,
                    updated_utc_ms = nowMs,
                    snapshot_generation = nowMs,
                    snapshot_active = 1,
                }).ToArray());
        }

        private CharacterResult LoadConquestRuntime()
        {
            IReadOnlyList<ConquestRuntimeRow> runtime = Array.Empty<ConquestRuntimeRow>();
            IReadOnlyList<ConquestFacilityRow> facilities = Array.Empty<ConquestFacilityRow>();
            using (var session = SqlSession.Open(_provider, _databaseOptions, maxRetries: 3, baseRetryDelayMs: 200))
            {
                session.RunInTransaction(s =>
                {
                    runtime = s.Query<ConquestRuntimeRow>(
                        "SELECT conquest_id AS ConquestId, owner_guild_id AS OwnerGuildId, attacker_guild_id AS AttackerGuildId, treasury AS Treasury, tax_rate AS TaxRate FROM conquest_runtime ORDER BY conquest_id");
                    facilities = s.Query<ConquestFacilityRow>(
                        "SELECT conquest_id AS ConquestId, facility_kind AS FacilityKind, facility_index AS FacilityIndex, current_hp AS CurrentHp, max_hp AS MaxHp FROM conquest_facilities ORDER BY conquest_id, facility_kind, facility_index");
                });
            }

            ApplyConquestRuntime(_statePort.Envir, runtime, facilities);
            return new CharacterResult { Committed = true, Generation = Volatile.Read(ref _generation) };
        }

        private static void ApplyConquestRuntime(
            Envir envir,
            IReadOnlyList<ConquestRuntimeRow> runtimeRows,
            IReadOnlyList<ConquestFacilityRow> facilityRows)
        {
            var runtimeById = runtimeRows.ToDictionary(row => row.ConquestId);
            var facilitiesById = facilityRows.GroupBy(row => row.ConquestId).ToDictionary(group => group.Key, group => group.ToArray());
            lock (Envir.LoadLock)
            {
                envir.Conquests.Clear();
                envir.ConquestList.Clear();

                foreach (var info in envir.ConquestInfoList)
                {
                    var map = envir.GetMap(info.MapIndex);
                    if (map == null) continue;
                    runtimeById.TryGetValue(info.Index, out var row);
                    var state = new ConquestGuildInfo
                    {
                        Info = info,
                        Owner = (int)(row?.OwnerGuildId ?? 0),
                        AttackerID = (int)(row?.AttackerGuildId ?? 0),
                        GoldStorage = (uint)Math.Clamp(row?.Treasury ?? 0, 0, uint.MaxValue),
                        NPCRate = (byte)Math.Clamp(row?.TaxRate ?? 0, 0, byte.MaxValue),
                        NeedSave = row == null,
                    };

                    if (facilitiesById.TryGetValue(info.Index, out var savedFacilities))
                    {
                        foreach (var facility in savedFacilities)
                        {
                            switch (facility.FacilityKind)
                            {
                                case "wall": state.WallList.Add(new ConquestGuildWallInfo { Index = facility.FacilityIndex, Health = (int)Math.Clamp(facility.CurrentHp, 0, int.MaxValue) }); break;
                                case "gate": state.GateList.Add(new ConquestGuildGateInfo { Index = facility.FacilityIndex, Health = (int)Math.Clamp(facility.CurrentHp, 0, int.MaxValue) }); break;
                                case "siege": state.SiegeList.Add(new ConquestGuildSiegeInfo { Index = facility.FacilityIndex, Health = (int)Math.Clamp(facility.CurrentHp, 0, int.MaxValue) }); break;
                                case "archer": state.ArcherList.Add(new ConquestGuildArcherInfo { Index = facility.FacilityIndex, Alive = facility.CurrentHp > 0 }); break;
                            }
                        }
                    }

                    var conquest = new ConquestObject(state) { ConquestMap = map };
                    var ownerGuild = envir.Guilds.FirstOrDefault(guild => guild.Guildindex == state.Owner);
                    if (ownerGuild != null)
                    {
                        conquest.Guild = ownerGuild;
                        ownerGuild.Conquest = conquest;
                    }
                    envir.ConquestList.Add(state);
                    envir.Conquests.Add(conquest);
                    map.Conquest.Add(conquest);
                    conquest.Bind();
                }
            }
        }

        public StartupLoadResult LoadStartup()
        {
            if (State == PersistenceModuleState.Ready)
                return new StartupLoadResult { Committed = true, Generation = Volatile.Read(ref _generation) };
            if (State == PersistenceModuleState.Loading)
                return PersistenceResult.Failure<StartupLoadResult>("startup_reentrant", new InvalidOperationException("持久化正在加载。"));
            if (State == PersistenceModuleState.Faulted)
                return PersistenceResult.Failure<StartupLoadResult>("startup_faulted", new InvalidOperationException("持久化已进入 Faulted 状态。"));

            State = PersistenceModuleState.Loading;
            try
            {
                EnsureInitialized();
                if (!LoadWorld(_statePort.Envir))
                    throw new InvalidOperationException("World Definition 加载失败。");

                LoadAccounts(_statePort.Envir);
                return new StartupLoadResult
                {
                    Committed = true,
                    Generation = Volatile.Read(ref _generation),
                };
            }
            catch (Exception exception)
            {
                State = PersistenceModuleState.Faulted;
                return PersistenceResult.Failure<StartupLoadResult>(
                    "startup_load_failed",
                    exception,
                    SqlTransientDetector.IsTransient(_provider, exception));
            }
        }

        public CommitResult Commit(CheckpointKind checkpoint, CommitReason reason)
        {
            if (State != PersistenceModuleState.Ready)
                return PersistenceResult.Failure<CommitResult>("persistence_not_ready", new InvalidOperationException($"当前状态为 {State}。"));

            if (checkpoint == CheckpointKind.CharacterRuntime)
                return SaveAccounts(_statePort.Envir);
            if (checkpoint != CheckpointKind.WorldDefinition)
                return PersistenceResult.Failure<CommitResult>("invalid_checkpoint", new ArgumentOutOfRangeException(nameof(checkpoint)));

            var runner = new SqlDomainTransactionRunner(_provider, _worldOptions);
            var result = runner.RunWithSnapshot(
                SqlSaveDomain.WorldRelations,
                () => SqlWorldRelationsStore.Capture(_statePort.Envir),
                (session, snapshot) =>
                {
                    ValidateWorldReferences(snapshot);
                    SqlWorldRelationsStore.ReplaceAll(session, snapshot);
                });
            return new CommitResult
            {
                Committed = result.Success,
                Generation = result.Success ? Interlocked.Increment(ref _generation) : Volatile.Read(ref _generation),
                Retryable = result.Exception != null && SqlTransientDetector.IsTransient(_provider, result.Exception),
                ErrorCode = result.Success ? string.Empty : "world_commit_failed",
                Diagnostics = result.Exception?.ToString() ?? string.Empty,
            };
        }

        public IdentityResult ExecuteIdentity(IdentityCommand command)
        {
            if (State != PersistenceModuleState.Ready)
                return PersistenceResult.Failure<IdentityResult>("persistence_not_ready", new InvalidOperationException($"当前状态为 {State}。"));
            if (command is not PersistIdentitySnapshotCommand)
                return PersistenceResult.Failure<IdentityResult>("identity_command_unsupported", new NotSupportedException(command?.GetType().Name));
            return PersistIdentitySnapshot(_statePort.Envir);
        }

        public CharacterResult ExecuteCharacter(CharacterCommand command)
        {
            var startupRuntimeCommand = command is LoadGuildRuntimeCommand or LoadNpcGoodsRuntimeCommand or LoadConquestRuntimeCommand;
            if (State != PersistenceModuleState.Ready && !(State == PersistenceModuleState.Loading && startupRuntimeCommand))
                return PersistenceResult.Failure<CharacterResult>("persistence_not_ready", new InvalidOperationException($"当前状态为 {State}。"));

            try
            {
                var result = command switch
                {
                    BackupCharacterCommand backup => BackupCharacter(backup.Character),
                    LoadCharacterBackupCommand load => LoadCharacterBackup(load.Name),
                    ArchiveCharacterCommand archive => ArchiveCharacter(archive.Character),
                    RestoreCharacterCommand restore => RestoreCharacter(restore.Name, restore.Account),
                    LoadGuildRuntimeCommand => LoadGuildRuntime(),
                    LoadNpcGoodsRuntimeCommand => LoadNpcGoodsRuntime(),
                    LoadConquestRuntimeCommand => LoadConquestRuntime(),
                    _ => PersistenceResult.Failure<CharacterResult>("character_command_unsupported", new NotSupportedException(command?.GetType().Name)),
                };
                if (State == PersistenceModuleState.Loading && command is LoadConquestRuntimeCommand && result.Committed)
                    State = PersistenceModuleState.Ready;
                return result;
            }
            catch (Exception exception)
            {
                if (State == PersistenceModuleState.Loading && startupRuntimeCommand)
                    State = PersistenceModuleState.Faulted;
                return PersistenceResult.Failure<CharacterResult>(
                    "character_command_failed",
                    exception,
                    SqlTransientDetector.IsTransient(_provider, exception));
            }
        }

        IdentityResult IIdentityStore.Execute(IdentityCommand command) => ExecuteIdentity(command);
        CharacterResult ICharacterStore.Execute(CharacterCommand command) => ExecuteCharacter(command);
        CommitResult ICharacterStore.Commit(CommitReason reason) => Commit(CheckpointKind.CharacterRuntime, reason);
        CommitResult IWorldStore.Commit(CommitReason reason) => Commit(CheckpointKind.WorldDefinition, reason);

        private CharacterResult ArchiveCharacter(CharacterInfo info)
        {
            if (info == null)
                return PersistenceResult.Failure<CharacterResult>("character_required", new ArgumentNullException(nameof(info)));

            var runner = new SqlDomainTransactionRunner(_provider, _databaseOptions);
            var result = runner.Run(SqlSaveDomain.Archive, session =>
            {
                var affected = session.Execute(
                    "UPDATE characters SET lifecycle_state='archived', archived_utc_ms=@nowMs, updated_utc_ms=@nowMs WHERE character_id=@id AND lifecycle_state<>'archived'",
                    new { id = info.Index, nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() });
                if (affected != 1)
                    throw new InvalidOperationException($"角色不存在或已经归档：{info.Name} ({info.Index})");
            });

            return new CharacterResult
            {
                Committed = result.Success,
                Generation = result.Success ? Interlocked.Increment(ref _generation) : Volatile.Read(ref _generation),
                Retryable = result.Exception != null && SqlTransientDetector.IsTransient(_provider, result.Exception),
                ErrorCode = result.Success ? string.Empty : "archive_failed",
                Diagnostics = result.Exception?.ToString() ?? string.Empty,
                Character = result.Success ? info : null,
            };
        }

        private CharacterResult RestoreCharacter(string name, AccountInfo account)
        {
            if (account == null)
                return PersistenceResult.Failure<CharacterResult>("account_required", new ArgumentNullException(nameof(account)));

            var runner = new SqlDomainTransactionRunner(_provider, _databaseOptions);
            var result = runner.Run(SqlSaveDomain.Archive, session =>
            {
                var affected = session.Execute(
                    "UPDATE characters SET lifecycle_state='active', archived_utc_ms=0, account_id=@accountId, deleted=0, delete_utc_ms=0, updated_utc_ms=@nowMs WHERE character_name=@name AND lifecycle_state='archived'",
                    new { accountId = account.Index, name = name?.Trim() ?? string.Empty, nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() });
                if (affected != 1)
                    throw new InvalidOperationException($"找不到唯一的已归档角色：{name}");
            });

            return new CharacterResult
            {
                Committed = result.Success,
                Generation = result.Success ? Interlocked.Increment(ref _generation) : Volatile.Read(ref _generation),
                Retryable = result.Exception != null && SqlTransientDetector.IsTransient(_provider, result.Exception),
                ErrorCode = result.Success ? string.Empty : "restore_failed",
                Diagnostics = result.Exception?.ToString() ?? string.Empty,
            };
        }

        private CharacterResult BackupCharacter(CharacterInfo info)
        {
            if (info == null)
                return PersistenceResult.Failure<CharacterResult>("character_required", new ArgumentNullException(nameof(info)));

            var json = JsonSerializer.Serialize(info, CharacterBackupJsonOptions);
            var sha = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant();
            var backupId = $"{info.Index}-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}-{sha[..12]}";
            var runner = new SqlDomainTransactionRunner(_provider, _databaseOptions);
            var result = runner.Run(SqlSaveDomain.Archive, session => session.Execute(
                "INSERT INTO character_backups (backup_id, character_id, character_name, format_version, canonical_json, sha256, created_utc_ms) VALUES (@backupId,@characterId,@name,1,@json,@sha,@nowMs)",
                new { backupId, characterId = info.Index, name = info.Name ?? string.Empty, json, sha, nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() }));

            return new CharacterResult
            {
                Committed = result.Success,
                Generation = result.Success ? Interlocked.Increment(ref _generation) : Volatile.Read(ref _generation),
                Retryable = result.Exception != null && SqlTransientDetector.IsTransient(_provider, result.Exception),
                ErrorCode = result.Success ? string.Empty : "backup_failed",
                Diagnostics = result.Exception?.ToString() ?? string.Empty,
                Character = result.Success ? info : null,
            };
        }

        private CharacterResult LoadCharacterBackup(string name)
        {
            name = name?.Trim() ?? string.Empty;
            var current = _statePort.Envir.GetCharacterInfo(name);
            if (current == null)
                return PersistenceResult.Failure<CharacterResult>("character_not_found", new InvalidOperationException(name));
            if (_statePort.Envir.Players.Any(player => player?.Info?.Index == current.Index))
                return PersistenceResult.Failure<CharacterResult>("character_online", new InvalidOperationException("LOADPLAYER 仅允许离线角色。"));

            using var session = SqlSession.Open(_provider, _databaseOptions, maxRetries: 3, baseRetryDelayMs: 200);
            var rows = session.Query<CharacterBackupRow>(
                "SELECT character_id AS CharacterId, canonical_json AS CanonicalJson, sha256 AS Sha256 FROM character_backups WHERE character_name=@name ORDER BY created_utc_ms DESC",
                new { name });
            if (rows.Count != 1)
                return PersistenceResult.Failure<CharacterResult>("backup_not_unique", new InvalidOperationException("请指定只有一个有效备份的角色。"));

            var row = rows[0];
            var actualSha = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(row.CanonicalJson ?? string.Empty))).ToLowerInvariant();
            if (!actualSha.Equals(row.Sha256, StringComparison.OrdinalIgnoreCase))
                return PersistenceResult.Failure<CharacterResult>("backup_checksum_mismatch", new InvalidDataException("备份 SHA-256 校验失败。"));
            if (row.CharacterId != current.Index)
                return PersistenceResult.Failure<CharacterResult>("character_id_mismatch", new InvalidOperationException("角色 ID 不匹配。"));

            var restored = JsonSerializer.Deserialize<CharacterInfo>(row.CanonicalJson, CharacterBackupJsonOptions);
            if (restored == null || restored.Index != current.Index)
                return PersistenceResult.Failure<CharacterResult>("backup_invalid", new InvalidDataException("备份内容无效。"));
            if (!BindCharacterBackupReferences(_statePort.Envir, restored))
                return PersistenceResult.Failure<CharacterResult>("backup_world_definition_missing", new InvalidDataException("备份物品引用的 World 模板不存在。"));

            var restoredItemIds = CollectCharacterOwnedItemIds(restored);
            if (restoredItemIds.Count > 0)
            {
                var locations = session.Query<ItemLocationRow>(
                    "SELECT item_id AS ItemId,location_kind AS LocationKind,owner_id AS OwnerId,container_kind AS ContainerKind,slot_index AS SlotIndex,parent_item_id AS ParentItemId FROM item_locations WHERE item_id IN @ids",
                    new { ids = restoredItemIds.ToArray() });
                foreach (var location in locations)
                {
                    var ownedByCharacter = location.LocationKind == "character" && location.OwnerId == current.Index;
                    var ownedSocket = location.LocationKind == "socket" && location.ParentItemId.HasValue && restoredItemIds.Contains(location.ParentItemId.Value);
                    if (!ownedByCharacter && !ownedSocket)
                        return PersistenceResult.Failure<CharacterResult>(
                            "backup_item_ownership_conflict",
                            new InvalidOperationException($"物品 {location.ItemId} 当前由 {location.LocationKind}:{location.OwnerId} 持有。"));
                }
            }

            return new CharacterResult
            {
                Committed = true,
                Generation = Volatile.Read(ref _generation),
                Character = restored,
            };
        }

        private static HashSet<long> CollectCharacterOwnedItemIds(CharacterInfo character)
        {
            var result = new HashSet<long>();
            void VisitItem(UserItem item)
            {
                if (item == null || item.UniqueID == 0) return;
                var itemId = ToDbInt64(item.UniqueID, "backup_item_id");
                if (!result.Add(itemId)) return;
                foreach (var child in item.Slots ?? Array.Empty<UserItem>()) VisitItem(child);
            }

            void VisitCharacter(CharacterInfo value)
            {
                if (value == null) return;
                foreach (var item in value.Inventory ?? Array.Empty<UserItem>()) VisitItem(item);
                foreach (var item in value.Equipment ?? Array.Empty<UserItem>()) VisitItem(item);
                foreach (var item in value.QuestInventory ?? Array.Empty<UserItem>()) VisitItem(item);
                VisitItem(value.CurrentRefine);
                foreach (var hero in value.Heroes ?? Array.Empty<HeroInfo>()) VisitCharacter(hero);
            }

            VisitCharacter(character);
            return result;
        }

        private static bool BindCharacterBackupReferences(Envir envir, CharacterInfo character)
        {
            var visited = new HashSet<ulong>();
            bool Bind(UserItem item)
            {
                if (item == null || item.UniqueID == 0 || !visited.Add(item.UniqueID)) return true;
                return envir.BindItem(item);
            }

            bool BindCharacter(CharacterInfo value)
            {
                if (value == null) return true;
                foreach (var item in value.Inventory ?? Array.Empty<UserItem>()) if (!Bind(item)) return false;
                foreach (var item in value.Equipment ?? Array.Empty<UserItem>()) if (!Bind(item)) return false;
                foreach (var item in value.QuestInventory ?? Array.Empty<UserItem>()) if (!Bind(item)) return false;
                if (!Bind(value.CurrentRefine)) return false;
                foreach (var magic in value.Magics ?? new List<UserMagic>())
                {
                    magic.Info = envir.MagicInfoList.FirstOrDefault(info => info != null && info.Spell == magic.Spell);
                    if (magic.Info == null) return false;
                }
                foreach (var quest in value.CurrentQuests ?? new List<QuestProgressInfo>())
                {
                    quest.Info = envir.QuestInfoList.FirstOrDefault(info => info != null && info.Index == quest.Index)?.CreateSnapshot();
                    if (quest.Info == null) return false;
                    foreach (var task in quest.KillTaskCount ?? new List<QuestKillTaskProgress>())
                        task.Info = quest.Info.KillTasks.FirstOrDefault(info => info?.Monster?.Index == task.MonsterID);
                    foreach (var task in quest.ItemTaskCount ?? new List<QuestItemTaskProgress>())
                        task.Info = quest.Info.ItemTasks.FirstOrDefault(info => info?.Item?.Index == task.ItemID);
                    foreach (var task in quest.FlagTaskSet ?? new List<QuestFlagTaskProgress>())
                        task.Info = quest.Info.FlagTasks.FirstOrDefault(info => info != null && info.Number == task.Number);
                }
                foreach (var creature in value.IntelligentCreatures ?? new List<UserIntelligentCreature>())
                {
                    creature.Info = IntelligentCreatureInfo.GetCreatureInfo(creature.PetType);
                    if (creature.Info == null) return false;
                }
                foreach (var hero in value.Heroes ?? Array.Empty<HeroInfo>()) if (!BindCharacter(hero)) return false;
                return true;
            }

            return BindCharacter(character);
        }

        private static readonly JsonSerializerOptions CharacterBackupJsonOptions = CreateCharacterBackupJsonOptions();

        private static JsonSerializerOptions CreateCharacterBackupJsonOptions()
        {
            var resolver = new DefaultJsonTypeInfoResolver();
            resolver.Modifiers.Add(typeInfo =>
            {
                if (typeof(CharacterInfo).IsAssignableFrom(typeInfo.Type))
                    RemoveJsonMembers(typeInfo, "AccountInfo", "Player", "Mail", "GuildIndex", "Rank", "Mount", "Poisons", "Buffs");
                if (typeInfo.Type == typeof(UserItem))
                    RemoveJsonMembers(typeInfo, "Info");
                if (typeInfo.Type == typeof(Buff))
                    RemoveJsonMembers(typeInfo, "Info", "Caster");
                if (typeInfo.Type == typeof(UserMagic))
                    RemoveJsonMembers(typeInfo, "Info");
                if (typeInfo.Type == typeof(QuestProgressInfo))
                    RemoveJsonMembers(typeInfo, "Owner", "Info");
                if (typeInfo.Type == typeof(QuestKillTaskProgress) || typeInfo.Type == typeof(QuestItemTaskProgress) || typeInfo.Type == typeof(QuestFlagTaskProgress))
                    RemoveJsonMembers(typeInfo, "Info");
                if (typeInfo.Type == typeof(UserIntelligentCreature))
                    RemoveJsonMembers(typeInfo, "Info");
            });

            return new JsonSerializerOptions
            {
                IncludeFields = true,
                IgnoreReadOnlyProperties = true,
                ReferenceHandler = ReferenceHandler.IgnoreCycles,
                TypeInfoResolver = resolver,
                WriteIndented = false,
                MaxDepth = 128,
            };
        }

        private static void RemoveJsonMembers(JsonTypeInfo typeInfo, params string[] names)
        {
            for (var index = typeInfo.Properties.Count - 1; index >= 0; index--)
                if (names.Contains(typeInfo.Properties[index].Name, StringComparer.Ordinal))
                    typeInfo.Properties.RemoveAt(index);
        }

    }
}
