using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Text;
using Server.MirDatabase;
using Server.MirEnvir;
using Server.MirObjects;

namespace Server.Persistence.Sql
{
    /// <summary>
    /// SQL 持久化入口（SQLite/MySQL）。
    /// 当前阶段：仅用于把调用链切换到“持久化层”，具体各域表结构与加载/保存闭环将按升级计划逐步落地。
    /// </summary>
    public sealed class SqlServerPersistence : IServerPersistence
    {
        private const string LegacyDomainWorld = "world";

        private const string LegacyFilesDomainGuilds = "guilds";
        private const string LegacyFilesDomainConquests = "conquests";
        private const string LegacyFilesDomainGoods = "goods";
        private const string LegacyFilesDomainArchive = "archive";

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

        private sealed class LegacyFileRow
        {
            public string RelativePath { get; set; }

            public byte[] Payload { get; set; }
        }

        private sealed class LegacyBlobRow
        {
            public byte[] Payload { get; set; }

            public long UpdatedUtcMs { get; set; }
        }

        private sealed class ServerMetaValueRow
        {
            public string MetaValue { get; set; }
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
            public byte[] Payload { get; set; }
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

            public AccountsSnapshot(
                long saveEpochUtcMs,
                IReadOnlyDictionary<string, long> nextIds,
                IReadOnlyList<AccountRow> accounts,
                IReadOnlyList<CharacterRow> characters,
                IReadOnlyList<ItemRow> items,
                IReadOnlyList<ItemAddedStatRow> itemAddedStats,
                IReadOnlyList<ItemAwakeLevelRow> itemAwakeLevels,
                IReadOnlyList<ItemSlotLinkRow> itemSlotLinks,
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
                IReadOnlyList<CharacterBuffRow> characterBuffs)
            {
                SaveEpochUtcMs = saveEpochUtcMs <= 0 ? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() : saveEpochUtcMs;
                NextIds = nextIds ?? new Dictionary<string, long>(StringComparer.Ordinal);
                Accounts = accounts ?? Array.Empty<AccountRow>();
                Characters = characters ?? Array.Empty<CharacterRow>();
                Items = items ?? Array.Empty<ItemRow>();
                ItemAddedStats = itemAddedStats ?? Array.Empty<ItemAddedStatRow>();
                ItemAwakeLevels = itemAwakeLevels ?? Array.Empty<ItemAwakeLevelRow>();
                ItemSlotLinks = itemSlotLinks ?? Array.Empty<ItemSlotLinkRow>();
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
            }
        }

        private readonly DatabaseProviderKind _provider;
        private readonly SqlDatabaseOptions _databaseOptions;
        private readonly ISchemaMigrator _schemaMigrator;
        private readonly object _initGate = new object();
        private bool _initialized;

        public DatabaseProviderKind Provider => _provider;

        public SqlServerPersistence(DatabaseProviderKind provider)
        {
            if (provider == DatabaseProviderKind.Legacy)
                throw new ArgumentException("Legacy 不应使用 SqlServerPersistence。", nameof(provider));

            _provider = provider;
            _databaseOptions = new SqlDatabaseOptions
            {
                SqlitePath = Settings.SqlitePath,
                MySqlConnectionString = Settings.MySqlConnectionString,
                MySqlPooling = Settings.MySqlPooling,
                MySqlMinPoolSize = Settings.MySqlMinPoolSize,
                MySqlMaxPoolSize = Settings.MySqlMaxPoolSize,
                MySqlConnectionTimeoutSeconds = Settings.MySqlConnectionTimeoutSeconds,
                MySqlKeepAliveSeconds = Settings.MySqlKeepAliveSeconds,
                MySqlConnectionIdleTimeoutSeconds = Settings.MySqlConnectionIdleTimeoutSeconds,
                MySqlConnectionLifeTimeSeconds = Settings.MySqlConnectionLifeTimeSeconds,
                CommandTimeoutSeconds = 30,
            };

            _schemaMigrator = new SchemaMigrator(SchemaMigrator.CreateDefaultMigrations(), commandTimeoutSeconds: _databaseOptions.CommandTimeoutSeconds);
        }

        private void EnsureInitialized()
        {
            if (_initialized) return;

            lock (_initGate)
            {
                if (_initialized) return;

                if (Settings.AutoApplySchemaOnStartup)
                {
                    var version = typeof(SqlServerPersistence).Assembly.GetName().Version?.ToString() ?? string.Empty;
                    var commit = string.Empty;

                    using var session = SqlSession.Open(_provider, _databaseOptions, maxRetries: 3, baseRetryDelayMs: 200);
                    _schemaMigrator.ApplyPendingMigrations(session.Connection, session.Dialect, version, commit);
                }

                _initialized = true;
            }
        }

        private static string SchemaNotReadyMessage(DatabaseProviderKind provider, Exception ex)
        {
            return
                $"SQL 持久化表结构未就绪（Provider={provider}）。" +
                $"请在 `Setup.ini` 的 `[Database]` 段启用 `AutoApplySchemaOnStartup=True`（开发/测试推荐），" +
                $"或先运行后续将补齐的 `Tools/DbMigrator` 来建表/迁移。原始错误：{ex.GetType().Name}: {ex.Message}";
        }

        private static void UpsertLegacyBlob(SqlSession session, string domain, byte[] payload)
        {
            UpsertLegacyBlob(session, domain, payload, updatedUtcMs: 0);
        }

        private static void UpsertLegacyBlob(SqlSession session, string domain, byte[] payload, long updatedUtcMs)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));
            if (string.IsNullOrWhiteSpace(domain)) throw new ArgumentException("domain 不能为空。", nameof(domain));
            payload ??= Array.Empty<byte>();

            if (updatedUtcMs <= 0)
                updatedUtcMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            var sql = session.Dialect.BuildUpsert(
                tableName: "legacy_blobs",
                insertColumns: ["domain", "payload", "updated_utc_ms"],
                keyColumns: ["domain"],
                updateColumns: ["payload", "updated_utc_ms"]);

            session.Execute(
                sql,
                new
                {
                    domain = domain.Trim(),
                    payload,
                    updated_utc_ms = updatedUtcMs,
                });
        }

        private static byte[] TryLoadLegacyBlob(SqlSession session, string domain)
        {
            return TryLoadLegacyBlob(session, domain, out _);
        }

        private static byte[] TryLoadLegacyBlob(SqlSession session, string domain, out long updatedUtcMs)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));
            if (string.IsNullOrWhiteSpace(domain)) throw new ArgumentException("domain 不能为空。", nameof(domain));

            updatedUtcMs = 0;

            var rows = session.Query<LegacyBlobRow>(
                "SELECT payload AS Payload, updated_utc_ms AS UpdatedUtcMs FROM legacy_blobs WHERE domain=@Domain",
                new { Domain = domain.Trim() });

            if (rows.Count == 0 || rows[0] == null)
                return null;

            updatedUtcMs = rows[0].UpdatedUtcMs;
            return rows[0].Payload;
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

        private static string NormalizeLegacyRelativePath(string path)
        {
            path ??= string.Empty;

            path = path.Replace('\\', '/');
            path = path.TrimStart('/');

            while (path.StartsWith("./", StringComparison.Ordinal))
                path = path.Substring(2);

            return path;
        }

        private static IReadOnlyList<LegacyFileRow> LoadLegacyFiles(SqlSession session, string domain)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));
            if (string.IsNullOrWhiteSpace(domain)) throw new ArgumentException("domain 不能为空。", nameof(domain));

            return session.Query<LegacyFileRow>(
                "SELECT relative_path AS RelativePath, payload AS Payload FROM legacy_files WHERE domain=@Domain",
                new { Domain = domain.Trim() });
        }

        private static void ReplaceLegacyFiles(SqlSession session, string domain, IReadOnlyList<LegacyFileRow> files)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));
            if (string.IsNullOrWhiteSpace(domain)) throw new ArgumentException("domain 不能为空。", nameof(domain));

            domain = domain.Trim();

            session.Execute("DELETE FROM legacy_files WHERE domain=@domain", new { domain });

            if (files == null || files.Count == 0)
                return;

            var nowUtcMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var upsert = session.Dialect.BuildUpsert(
                tableName: "legacy_files",
                insertColumns: ["domain", "relative_path", "payload", "updated_utc_ms"],
                keyColumns: ["domain", "relative_path"],
                updateColumns: ["payload", "updated_utc_ms"]);

            for (var i = 0; i < files.Count; i++)
            {
                var file = files[i];
                if (file == null) continue;

                var relativePath = NormalizeLegacyRelativePath(file.RelativePath);
                if (string.IsNullOrWhiteSpace(relativePath)) continue;
                if (relativePath.Length > 512)
                    throw new NotSupportedException($"legacy_files.relative_path 超过 512 字符：domain={domain}, path={relativePath}");

                session.Execute(
                    upsert,
                    new
                    {
                        domain,
                        relative_path = relativePath,
                        payload = file.Payload ?? Array.Empty<byte>(),
                        updated_utc_ms = nowUtcMs,
                    });
            }
        }

        private static void UpsertLegacyFile(SqlSession session, string domain, LegacyFileRow file)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));
            if (string.IsNullOrWhiteSpace(domain)) throw new ArgumentException("domain 不能为空。", nameof(domain));
            if (file == null) return;

            domain = domain.Trim();

            var relativePath = NormalizeLegacyRelativePath(file.RelativePath);
            if (string.IsNullOrWhiteSpace(relativePath)) return;
            if (relativePath.Length > 512)
                throw new NotSupportedException($"legacy_files.relative_path 超过 512 字符：domain={domain}, path={relativePath}");

            var nowUtcMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var upsert = session.Dialect.BuildUpsert(
                tableName: "legacy_files",
                insertColumns: ["domain", "relative_path", "payload", "updated_utc_ms"],
                keyColumns: ["domain", "relative_path"],
                updateColumns: ["payload", "updated_utc_ms"]);

            session.Execute(
                upsert,
                new
                {
                    domain,
                    relative_path = relativePath,
                    payload = file.Payload ?? Array.Empty<byte>(),
                    updated_utc_ms = nowUtcMs,
                });
        }

        private static IReadOnlyList<LegacyFileRow> CaptureLegacyFilesFromDirectory(string sourceDir)
        {
            sourceDir = string.IsNullOrWhiteSpace(sourceDir) ? string.Empty : Path.GetFullPath(sourceDir);
            if (!Directory.Exists(sourceDir))
                return Array.Empty<LegacyFileRow>();

            var paths = Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories);
            if (paths.Length == 0)
                return Array.Empty<LegacyFileRow>();

            Array.Sort(paths, StringComparer.OrdinalIgnoreCase);

            var result = new List<LegacyFileRow>(paths.Length);
            for (var i = 0; i < paths.Length; i++)
            {
                var path = paths[i];
                var relative = NormalizeLegacyRelativePath(Path.GetRelativePath(sourceDir, path));
                if (string.IsNullOrWhiteSpace(relative)) continue;

                result.Add(new LegacyFileRow
                {
                    RelativePath = relative,
                    Payload = File.ReadAllBytes(path),
                });
            }

            return result;
        }

        private static void RestoreLegacyFilesToDirectory(IReadOnlyList<LegacyFileRow> files, string targetDir)
        {
            if (files == null || files.Count == 0) return;
            if (string.IsNullOrWhiteSpace(targetDir)) return;

            var baseDir = Path.GetFullPath(targetDir);
            Directory.CreateDirectory(baseDir);

            for (var i = 0; i < files.Count; i++)
            {
                var file = files[i];
                if (file == null) continue;

                var relativePath = NormalizeLegacyRelativePath(file.RelativePath);
                if (string.IsNullOrWhiteSpace(relativePath)) continue;

                if (relativePath.Contains("..", StringComparison.Ordinal))
                    throw new NotSupportedException($"legacy_files.relative_path 不允许包含 '..'：{relativePath}");

                var localRelative = relativePath.Replace('/', Path.DirectorySeparatorChar);
                var fullPath = Path.GetFullPath(Path.Combine(baseDir, localRelative));

                if (!fullPath.StartsWith(baseDir, StringComparison.OrdinalIgnoreCase))
                    throw new NotSupportedException($"legacy_files.relative_path 路径越界：base={baseDir}, relative={relativePath}");

                var fullDir = Path.GetDirectoryName(fullPath);
                if (!string.IsNullOrWhiteSpace(fullDir))
                    Directory.CreateDirectory(fullDir);

                File.WriteAllBytes(fullPath, file.Payload ?? Array.Empty<byte>());
            }
        }

        private static IReadOnlyList<LegacyFileRow> CaptureGuildLegacyFiles(Envir envir)
        {
            if (envir == null) throw new ArgumentNullException(nameof(envir));

            var guilds = envir.GuildList ?? new List<GuildInfo>();
            if (guilds.Count == 0)
                return Array.Empty<LegacyFileRow>();

            var result = new List<LegacyFileRow>(guilds.Count);

            for (var i = 0; i < guilds.Count; i++)
            {
                var guild = guilds[i];
                if (guild == null) continue;

                using var ms = new MemoryStream();
                using (var writer = new BinaryWriter(ms))
                {
                    guild.Save(writer);
                    writer.Flush();
                }

                result.Add(new LegacyFileRow
                {
                    RelativePath = i + ".mgd",
                    Payload = ms.ToArray(),
                });
            }

            return result;
        }

        private static IReadOnlyList<LegacyFileRow> CaptureConquestLegacyFiles(Envir envir)
        {
            if (envir == null) throw new ArgumentNullException(nameof(envir));

            var conquests = envir.ConquestList ?? new List<ConquestGuildInfo>();
            if (conquests.Count == 0)
                return Array.Empty<LegacyFileRow>();

            var result = new List<LegacyFileRow>(conquests.Count);

            for (var i = 0; i < conquests.Count; i++)
            {
                var conquest = conquests[i];
                if (conquest?.Info == null) continue;

                using var ms = new MemoryStream();
                using (var writer = new BinaryWriter(ms))
                {
                    conquest.Save(writer);
                    writer.Flush();
                }

                result.Add(new LegacyFileRow
                {
                    RelativePath = conquest.Info.Index + ".mcd",
                    Payload = ms.ToArray(),
                });
            }

            return result;
        }

        private static IReadOnlyList<LegacyFileRow> CaptureGoodsLegacyFiles(Envir envir, bool forced)
        {
            if (envir == null) throw new ArgumentNullException(nameof(envir));

            if (envir.MapList == null || envir.MapList.Count == 0)
                return Array.Empty<LegacyFileRow>();

            var result = new List<LegacyFileRow>();
            var seen = new HashSet<int>();

            for (var i = 0; i < envir.MapList.Count; i++)
            {
                var map = envir.MapList[i];
                if (map?.NPCs == null || map.NPCs.Count == 0) continue;

                 for (var j = 0; j < map.NPCs.Count; j++)
                 {
                     var npc = map.NPCs[j];
                     if (npc?.Info == null) continue;

                    var npcIndex = npc.Info.Index;
                    if (!seen.Add(npcIndex)) continue;

                    if (forced)
                    {
                        npc.ProcessGoods(clear: true);
                    }

                    if (npc.UsedGoods == null || npc.UsedGoods.Count == 0)
                        continue;

                    using var ms = new MemoryStream();
                    using (var writer = new BinaryWriter(ms))
                    {
                        writer.Write(9999);
                        writer.Write(Envir.Version);
                        writer.Write(Envir.CustomVersion);
                        writer.Write(npc.UsedGoods.Count);

                        for (var k = 0; k < npc.UsedGoods.Count; k++)
                        {
                            npc.UsedGoods[k]?.Save(writer);
                        }

                        writer.Flush();
                    }

                    result.Add(new LegacyFileRow
                    {
                        RelativePath = npcIndex + ".msd",
                        Payload = ms.ToArray(),
                    });
                }
            }

            return result;
        }

        private static void ApplyGuildsFromLegacyFiles(Envir envir, IReadOnlyList<LegacyFileRow> files)
        {
            if (envir == null) throw new ArgumentNullException(nameof(envir));

            envir.GuildList.Clear();
            envir.Guilds.Clear();

            if (files == null || files.Count == 0)
            {
                envir.GuildCount = 0;
                return;
            }

            var byIndex = new SortedDictionary<int, LegacyFileRow>();
            for (var i = 0; i < files.Count; i++)
            {
                var row = files[i];
                if (row?.Payload == null || row.Payload.Length == 0) continue;

                var relative = NormalizeLegacyRelativePath(row.RelativePath);
                if (!relative.EndsWith(".mgd", StringComparison.OrdinalIgnoreCase)) continue;

                var name = Path.GetFileNameWithoutExtension(relative);
                if (!int.TryParse(name, out var index)) continue;
                if (index < 0) continue;

                byIndex[index] = row;
            }

            var loaded = 0;

            foreach (var kvp in byIndex)
            {
                var row = kvp.Value;

                using var ms = new MemoryStream(row.Payload);
                using var reader = new BinaryReader(ms);
                var guildInfo = new GuildInfo(reader);

                envir.GuildList.Add(guildInfo);
                _ = new GuildObject(guildInfo);

                loaded++;
            }

            envir.GuildCount = loaded;
        }

        private static void ApplyConquestsFromLegacyFiles(Envir envir, IReadOnlyList<LegacyFileRow> files)
        {
            if (envir == null) throw new ArgumentNullException(nameof(envir));

            envir.Conquests.Clear();
            envir.ConquestList.Clear();

            var byIndex = new Dictionary<int, LegacyFileRow>();
            if (files != null)
            {
                for (var i = 0; i < files.Count; i++)
                {
                    var row = files[i];
                    if (row?.Payload == null || row.Payload.Length == 0) continue;

                    var relative = NormalizeLegacyRelativePath(row.RelativePath);
                    if (!relative.EndsWith(".mcd", StringComparison.OrdinalIgnoreCase)) continue;

                    var name = Path.GetFileNameWithoutExtension(relative);
                    if (!int.TryParse(name, out var index)) continue;
                    if (index < 0) continue;

                    byIndex[index] = row;
                }
            }

            for (var i = 0; i < envir.ConquestInfoList.Count; i++)
            {
                ConquestObject newConquest;
                ConquestGuildInfo conquestGuildInfo;
                var info = envir.ConquestInfoList[i];
                var tempMap = envir.GetMap(info.MapIndex);

                if (tempMap == null) continue;

                if (byIndex.TryGetValue(info.Index, out var row))
                {
                    using var ms = new MemoryStream(row.Payload);
                    using var reader = new BinaryReader(ms);
                    conquestGuildInfo = new ConquestGuildInfo(reader) { Info = info };

                    newConquest = new ConquestObject(conquestGuildInfo)
                    {
                        ConquestMap = tempMap
                    };

                    for (var k = 0; k < envir.Guilds.Count; k++)
                    {
                        if (conquestGuildInfo.Owner != envir.Guilds[k].Guildindex) continue;
                        newConquest.Guild = envir.Guilds[k];
                        envir.Guilds[k].Conquest = newConquest;
                    }
                }
                else
                {
                    conquestGuildInfo = new ConquestGuildInfo { Info = info, NeedSave = true };
                    newConquest = new ConquestObject(conquestGuildInfo)
                    {
                        ConquestMap = tempMap
                    };
                }

                envir.ConquestList.Add(conquestGuildInfo);
                envir.Conquests.Add(newConquest);
                tempMap.Conquest.Add(newConquest);

                newConquest.Bind();
            }
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
                    "admin_account",
                    "gold",
                    "credit",
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
                    "admin_account",
                    "gold",
                    "credit",
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
                        admin_account = account.AdminAccount,
                        gold = account.Gold,
                        credit = account.Credit,
                        updated_utc_ms = nowMs,
                    });
                }

                if (batch.Count > 0)
                    session.Execute(sql, batch);
            }
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

        private static void UpsertCharacters(SqlSession session, IReadOnlyList<CharacterRow> characters)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));
            if (characters == null || characters.Count == 0) return;

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
                    "updated_utc_ms",
                ]);

            var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
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
                "FROM characters " +
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
            session.Execute("DELETE FROM item_instances WHERE updated_utc_ms <> @nowMs", new { nowMs });
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
            }

            return result;
        }

        private static Awake BuildAwake(int awakeType, IReadOnlyList<ItemAwakeLevelRow> levels)
        {
            using var ms = new MemoryStream();
            using (var writer = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true))
            {
                writer.Write((byte)awakeType);
                writer.Write(levels?.Count ?? 0);

                if (levels != null)
                {
                    for (var i = 0; i < levels.Count; i++)
                    {
                        var level = levels[i];
                        writer.Write((byte)Math.Clamp(level?.LevelValue ?? 0, 0, byte.MaxValue));
                    }
                }
            }

            ms.Position = 0;
            using var reader = new BinaryReader(ms, Encoding.UTF8, leaveOpen: true);
            return new Awake(reader);
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

        private static byte[] SerializeBuffPayload(Buff buff)
        {
            if (buff == null) return Array.Empty<byte>();

            using var ms = new MemoryStream();
            using (var writer = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true))
            {
                buff.Save(writer);
                writer.Flush();
            }

            return ms.ToArray();
        }

        private static Buff DeserializeBuffPayload(byte[] payload)
        {
            payload ??= Array.Empty<byte>();
            using var ms = new MemoryStream(payload);
            using var reader = new BinaryReader(ms, Encoding.UTF8, leaveOpen: true);
            return new Buff(reader, Envir.Version, Envir.CustomVersion);
        }

        private static IReadOnlyList<CharacterBuffRow> CaptureCharacterBuffs(Envir envir)
        {
            if (envir == null) throw new ArgumentNullException(nameof(envir));

            lock (Envir.AccountLock)
            {
                var result = new List<CharacterBuffRow>();

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
                            Payload = SerializeBuffPayload(buff),
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
                "SELECT character_id AS CharacterId, list_index AS ListIndex, buff_type AS BuffType, payload AS Payload FROM character_buffs ORDER BY character_id, list_index");
        }

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

        private static void ReplaceCharacterBuffs(SqlSession session, IReadOnlyList<CharacterBuffRow> rows, long saveEpochUtcMs = 0)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));

            var nowMs = saveEpochUtcMs > 0 ? saveEpochUtcMs : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            rows ??= Array.Empty<CharacterBuffRow>();
            var batchSize = Settings.SaveBatchSize <= 0 ? 2000 : Settings.SaveBatchSize;

            var sql = session.Dialect.BuildUpsert(
                tableName: "character_buffs",
                insertColumns: ["character_id", "list_index", "buff_type", "payload", "updated_utc_ms"],
                keyColumns: ["character_id", "list_index"],
                updateColumns: ["buff_type", "payload", "updated_utc_ms"]);

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
                        payload = row.Payload ?? Array.Empty<byte>(),
                        updated_utc_ms = nowMs,
                    });
                }

                if (batch.Count > 0)
                    session.Execute(sql, batch);
            }

            session.Execute("DELETE FROM character_buffs WHERE updated_utc_ms <> @nowMs", new { nowMs });
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

        private static void ApplyCharacterBuffs(Envir envir, IReadOnlyList<CharacterBuffRow> rows)
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

                    try
                    {
                        var buff = DeserializeBuffPayload(row.Payload);
                        if (buff == null || buff.Info == null)
                            continue;

                        character.Buffs.Add(buff);
                    }
                    catch
                    {
                        // 单条坏数据不应阻塞整体加载，保持与 legacy 行为一致尽量跳过。
                    }
                }
            }
        }

        public bool LoadWorld(Envir envir)
        {
            if (envir == null) throw new ArgumentNullException(nameof(envir));

            EnsureInitialized();

            try
            {
                using var session = SqlSession.Open(_provider, _databaseOptions, maxRetries: 3, baseRetryDelayMs: 200);
                var goodsFiles = LoadLegacyFiles(session, LegacyFilesDomainGoods);
                if (goodsFiles.Count > 0)
                {
                    RestoreLegacyFilesToDirectory(goodsFiles, Settings.GoodsPath);
                }
                else if (Settings.AutoImportLegacyOnEmpty)
                {
                    var diskFiles = CaptureLegacyFilesFromDirectory(Settings.GoodsPath);
                    if (diskFiles.Count > 0)
                    {
                        session.RunInTransaction(s => ReplaceLegacyFiles(s, LegacyFilesDomainGoods, diskFiles));
                    }
                }
            }
            catch (Exception ex)
            {
                MessageQueue.Instance.EnqueueDebugging($"[SQL:{_provider}] Goods legacy_files 同步异常（将继续依赖文件）：{ex}");
            }

            SqlWorldRelationsSnapshot relationsSnapshot = null;
            try
            {
                using var session = SqlSession.Open(_provider, _databaseOptions, maxRetries: 3, baseRetryDelayMs: 200);
                relationsSnapshot = SqlWorldRelationsLoader.LoadAll(session);

                if (relationsSnapshot != null)
                {
                    SqlWorldRelationsLoader.RestoreToEnvir(envir, relationsSnapshot);
                    return true;
                }
            }
            catch (Exception ex)
            {
                MessageQueue.Instance.EnqueueDebugging(
                    $"[SQL:{_provider}] World 关系表加载异常（将回退到 legacy 导入；WorldBlobFallback={(Settings.WorldLegacyBlobReadFallbackEnabled ? "On" : "Off")}）：{ex}");
            }

            byte[] payload = null;

            try
            {
                using var session = SqlSession.Open(_provider, _databaseOptions, maxRetries: 3, baseRetryDelayMs: 200);

                if (Settings.WorldLegacyBlobReadFallbackEnabled)
                {
                    payload = TryLoadLegacyBlob(session, LegacyDomainWorld);
                }

                if (payload == null || payload.Length == 0)
                {
                    if (Settings.AutoImportLegacyOnEmpty && File.Exists(Envir.DatabasePath))
                    {
                        payload = File.ReadAllBytes(Envir.DatabasePath);
                    }
                    else
                    {
                        payload = envir.Legacy_SaveDBToBytes();
                    }

                    if (Settings.WorldLegacyBlobWriteEnabled)
                    {
                        session.RunInTransaction(s => UpsertLegacyBlob(s, LegacyDomainWorld, payload));
                    }
                }
            }
            catch (Exception ex)
            {
                throw new NotSupportedException(SchemaNotReadyMessage(_provider, ex), ex);
            }

            var loaded = false;
            using (var ms = new MemoryStream(payload))
            {
                loaded = envir.Legacy_LoadDBFromStream(ms);
            }

            if (loaded && relationsSnapshot == null)
            {
                try
                {
                    var runner = new SqlDomainTransactionRunner(_provider, _databaseOptions);
                    runner.RunWithSnapshot(
                        domain: SqlSaveDomain.WorldRelations,
                        snapshotFactory: () => SqlWorldRelationsStore.Capture(envir),
                        work: (session, snapshot) => SqlWorldRelationsStore.ReplaceAll(session, snapshot));
                }
                catch (Exception ex)
                {
                    MessageQueue.Instance.EnqueueDebugging($"[SQL:{_provider}] World 关系表回填异常（将继续使用当前内存世界库；下次启动可能仍需 legacy 导入）：{ex}");
                }
            }

            return loaded;
        }

        public void SaveWorld(Envir envir)
        {
            if (envir == null) throw new ArgumentNullException(nameof(envir));

            EnsureInitialized();

            try
            {
                var runner = new SqlDomainTransactionRunner(_provider, _databaseOptions);

                if (Settings.WorldLegacyBlobWriteEnabled)
                {
                    runner.RunWithSnapshot(
                        domain: SqlSaveDomain.World,
                        snapshotFactory: () => envir.Legacy_SaveDBToBytes(),
                        work: (session, payload) => UpsertLegacyBlob(session, LegacyDomainWorld, payload));
                }

                runner.RunWithSnapshot(
                    domain: SqlSaveDomain.WorldRelations,
                    snapshotFactory: () => SqlWorldRelationsStore.Capture(envir),
                    work: (session, snapshot) => SqlWorldRelationsStore.ReplaceAll(session, snapshot));
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

            var builtFromRelations = false;

            try
            {
                using var session = SqlSession.Open(_provider, _databaseOptions, maxRetries: 3, baseRetryDelayMs: 200);
                builtFromRelations = TryBuildAccountsGraphFromRelations(session, envir);
            }
            catch (Exception ex)
            {
                throw new NotSupportedException(SchemaNotReadyMessage(_provider, ex), ex);
            }

            if (!builtFromRelations)
            {
                byte[] payload;
                if (Settings.AutoImportLegacyOnEmpty && File.Exists(Envir.AccountPath))
                {
                    payload = File.ReadAllBytes(Envir.AccountPath);
                }
                else
                {
                    payload = envir.Legacy_SaveAccountsToBytes();
                }

                using var ms = new MemoryStream(payload);
                envir.Legacy_LoadAccountsFromStream(ms);
            }

            var shouldApplyRelations = builtFromRelations;
            var allowSeedFromMemory = !shouldApplyRelations;

            try
            {
                using var session = SqlSession.Open(_provider, _databaseOptions, maxRetries: 3, baseRetryDelayMs: 200);

                var loadedNextIds = LoadNextIds(session, AccountsNextIdKeys);
                ApplyAccountsNextIds(envir, loadedNextIds);

                var currentNextIds = CaptureAccountsNextIds(envir);
                session.RunInTransaction(s => UpsertNextIds(s, currentNextIds));
            }
            catch (Exception ex)
            {
                MessageQueue.Instance.Enqueue($"[SQL:{_provider}] NextIds 同步异常：{ex}");
            }

            try
            {
                using var session = SqlSession.Open(_provider, _databaseOptions, maxRetries: 3, baseRetryDelayMs: 200);

                var rows = LoadAccountRows(session);
                if (rows.Count > 0)
                {
                    if (shouldApplyRelations)
                        ApplyAccounts(envir, rows);
                }
                else if (allowSeedFromMemory)
                {
                    var accounts = CaptureAccounts(envir);
                    session.RunInTransaction(s => UpsertAccounts(s, accounts));
                }
            }
            catch (Exception ex)
            {
                MessageQueue.Instance.EnqueueDebugging($"[SQL:{_provider}] Accounts 关系表同步异常（将保持当前内存数据）：{ex}");
            }

            try
            {
                using var session = SqlSession.Open(_provider, _databaseOptions, maxRetries: 3, baseRetryDelayMs: 200);

                var rows = LoadCharacterRows(session);
                if (rows.Count > 0)
                {
                    if (shouldApplyRelations)
                        ApplyCharacters(envir, rows);
                }
                else if (allowSeedFromMemory)
                {
                    var characters = CaptureCharacters(envir);
                    session.RunInTransaction(s => UpsertCharacters(s, characters));
                }
            }
            catch (Exception ex)
            {
                MessageQueue.Instance.EnqueueDebugging($"[SQL:{_provider}] Characters 关系表同步异常（将保持当前内存数据）：{ex}");
            }

            Dictionary<long, UserItem> itemsById = null;

            try
            {
                using var session = SqlSession.Open(_provider, _databaseOptions, maxRetries: 3, baseRetryDelayMs: 200);

                var itemRows = LoadItemRows(session);
                if (itemRows.Count > 0)
                {
                    if (shouldApplyRelations)
                    {
                        var statRows = LoadItemAddedStatRows(session);
                        var awakeRows = LoadItemAwakeLevelRows(session);
                        var slotRows = LoadItemSlotLinkRows(session);

                        itemsById = ApplyItems(envir, itemRows, statRows, awakeRows, slotRows);
                    }
                    else
                    {
                        itemsById = CollectInMemoryItems(envir);
                    }
                }
                else if (allowSeedFromMemory)
                {
                    CaptureItems(envir, out var items, out var itemStats, out var awakeLevels, out var slotLinks);
                    session.RunInTransaction(s => ReplaceItems(s, items, itemStats, awakeLevels, slotLinks));
                    itemsById = CollectInMemoryItems(envir);
                }
                else
                {
                    itemsById = CollectInMemoryItems(envir);
                }
            }
            catch (Exception ex)
            {
                MessageQueue.Instance.EnqueueDebugging($"[SQL:{_provider}] Items 关系表同步异常（将保持当前内存数据）：{ex}");
                itemsById ??= CollectInMemoryItems(envir);
            }

            try
            {
                using var session = SqlSession.Open(_provider, _databaseOptions, maxRetries: 3, baseRetryDelayMs: 200);

                var storageRows = LoadAccountStorageRows(session);
                var containerRows = LoadCharacterContainerRows(session);

                if (storageRows.Count > 0 || containerRows.Count > 0)
                {
                    if (shouldApplyRelations)
                    {
                        var storageSlotRows = LoadAccountStorageSlotRows(session);
                        var containerSlotRows = LoadCharacterContainerSlotRows(session);

                        ApplyContainers(envir, itemsById, storageRows, storageSlotRows, containerRows, containerSlotRows);
                    }
                }
                else if (allowSeedFromMemory)
                {
                    CaptureContainers(envir, out var storage, out var storageSlots, out var containers, out var containerSlots);
                    session.RunInTransaction(s => ReplaceContainers(s, storage, storageSlots, containers, containerSlots));
                }
            }
            catch (Exception ex)
            {
                MessageQueue.Instance.EnqueueDebugging($"[SQL:{_provider}] Containers 关系表同步异常（将保持当前内存数据）：{ex}");
            }

            try
            {
                using var session = SqlSession.Open(_provider, _databaseOptions, maxRetries: 3, baseRetryDelayMs: 200);

                var auctionRows = LoadAuctionRows(session);
                if (auctionRows.Count > 0)
                {
                    if (shouldApplyRelations)
                        ApplyAuctions(envir, itemsById, auctionRows);
                }
                else if (allowSeedFromMemory)
                {
                    var auctions = CaptureAuctions(envir);
                    session.RunInTransaction(s => ReplaceAuctions(s, auctions));
                }

                var mailRows = LoadMailRows(session);
                var mailItemRows = (shouldApplyRelations && mailRows.Count > 0) ? LoadMailItemRows(session) : Array.Empty<MailItemRow>();

                if (mailRows.Count > 0)
                {
                    if (shouldApplyRelations)
                        ApplyMails(envir, itemsById, mailRows, mailItemRows);
                }
                else if (allowSeedFromMemory)
                {
                    CaptureMails(envir, out var mails, out var items);
                    session.RunInTransaction(s => ReplaceMails(s, mails, items));
                }

                var gameshopRows = LoadGameshopLogRows(session);
                if (gameshopRows.Count > 0)
                {
                    if (shouldApplyRelations)
                        ApplyGameshopLog(envir, gameshopRows);
                }
                else if (allowSeedFromMemory)
                {
                    var gameshop = CaptureGameshopLog(envir);
                    session.RunInTransaction(s => ReplaceGameshopLog(s, gameshop));
                }

                var respawnRows = LoadRespawnSaveRows(session);
                if (respawnRows.Count > 0)
                {
                    if (shouldApplyRelations)
                        ApplyRespawnSaves(envir, respawnRows);
                }
                else if (allowSeedFromMemory)
                {
                    var saves = CaptureRespawnSaves(envir);
                    session.RunInTransaction(s => ReplaceRespawnSaves(s, saves));
                }

                var magicRows = LoadCharacterMagicRows(session);
                if (magicRows.Count > 0)
                {
                    if (shouldApplyRelations)
                        ApplyCharacterMagics(envir, magicRows);
                }
                else if (allowSeedFromMemory)
                {
                    var magics = CaptureCharacterMagics(envir);
                    session.RunInTransaction(s => ReplaceCharacterMagics(s, magics));
                }

                var completedQuestRows = LoadCharacterCompletedQuestRows(session);
                if (completedQuestRows.Count > 0)
                {
                    if (shouldApplyRelations)
                        ApplyCharacterCompletedQuests(envir, completedQuestRows);
                }
                else if (allowSeedFromMemory)
                {
                    var completedQuests = CaptureCharacterCompletedQuests(envir);
                    session.RunInTransaction(s => ReplaceCharacterCompletedQuests(s, completedQuests));
                }

                var flagRows = LoadCharacterFlagRows(session);
                if (flagRows.Count > 0)
                {
                    if (shouldApplyRelations)
                        ApplyCharacterFlags(envir, flagRows);
                }
                else if (allowSeedFromMemory)
                {
                    var flags = CaptureCharacterFlags(envir);
                    session.RunInTransaction(s => ReplaceCharacterFlags(s, flags));
                }

                var gameshopPurchaseRows = LoadCharacterGameshopPurchaseRows(session);
                if (gameshopPurchaseRows.Count > 0)
                {
                    if (shouldApplyRelations)
                        ApplyCharacterGameshopPurchases(envir, gameshopPurchaseRows);
                }
                else if (allowSeedFromMemory)
                {
                    var purchases = CaptureCharacterGameshopPurchases(envir);
                    session.RunInTransaction(s => ReplaceCharacterGameshopPurchases(s, purchases));
                }

                var currentQuestRows = LoadCurrentQuestRows(session);
                if (currentQuestRows.Count > 0)
                {
                    if (shouldApplyRelations)
                    {
                        var currentQuestKillRows = LoadCurrentQuestKillTaskRows(session);
                        var currentQuestItemRows = LoadCurrentQuestItemTaskRows(session);
                        var currentQuestFlagRows = LoadCurrentQuestFlagTaskRows(session);

                        ApplyCurrentQuests(envir, currentQuestRows, currentQuestKillRows, currentQuestItemRows, currentQuestFlagRows);
                    }
                }
                else if (allowSeedFromMemory)
                {
                    CaptureCurrentQuests(envir, out var currentQuests, out var currentQuestKillTasks, out var currentQuestItemTasks, out var currentQuestFlagTasks);
                    session.RunInTransaction(s => ReplaceCurrentQuests(s, currentQuests, currentQuestKillTasks, currentQuestItemTasks, currentQuestFlagTasks));
                }

                var petRows = LoadCharacterPetRows(session);
                if (petRows.Count > 0)
                {
                    if (shouldApplyRelations)
                        ApplyCharacterPets(envir, petRows);
                }
                else if (allowSeedFromMemory)
                {
                    var pets = CaptureCharacterPets(envir);
                    session.RunInTransaction(s => ReplaceCharacterPets(s, pets));
                }

                var friendRows = LoadCharacterFriendRows(session);
                if (friendRows.Count > 0)
                {
                    if (shouldApplyRelations)
                        ApplyCharacterFriends(envir, friendRows);
                }
                else if (allowSeedFromMemory)
                {
                    var friends = CaptureCharacterFriends(envir);
                    session.RunInTransaction(s => ReplaceCharacterFriends(s, friends));
                }

                var rentedItemRows = LoadCharacterRentedItemRows(session);
                if (rentedItemRows.Count > 0)
                {
                    if (shouldApplyRelations)
                        ApplyCharacterRentedItems(envir, rentedItemRows);
                }
                else if (allowSeedFromMemory)
                {
                    var rentedItems = CaptureCharacterRentedItems(envir);
                    session.RunInTransaction(s => ReplaceCharacterRentedItems(s, rentedItems));
                }

                var creatureRows = LoadCharacterIntelligentCreatureRows(session);
                if (creatureRows.Count > 0)
                {
                    if (shouldApplyRelations)
                        ApplyCharacterIntelligentCreatures(envir, creatureRows);
                }
                else if (allowSeedFromMemory)
                {
                    var creatures = CaptureCharacterIntelligentCreatures(envir);
                    session.RunInTransaction(s => ReplaceCharacterIntelligentCreatures(s, creatures));
                }

                var heroDetailRows = LoadHeroDetailRows(session);
                if (heroDetailRows.Count > 0)
                {
                    if (shouldApplyRelations)
                        ApplyHeroDetails(envir, heroDetailRows);
                }
                else if (allowSeedFromMemory)
                {
                    var heroDetails = CaptureHeroDetails(envir);
                    session.RunInTransaction(s => ReplaceHeroDetails(s, heroDetails));
                }

                var heroSlotRows = LoadCharacterHeroSlotRows(session);
                if (heroSlotRows.Count > 0)
                {
                    if (shouldApplyRelations)
                        ApplyCharacterHeroSlots(envir, heroSlotRows);
                }
                else if (allowSeedFromMemory)
                {
                    var heroSlots = CaptureCharacterHeroSlots(envir);
                    session.RunInTransaction(s => ReplaceCharacterHeroSlots(s, heroSlots));
                }

                var buffRows = LoadCharacterBuffRows(session);
                if (buffRows.Count > 0)
                {
                    if (shouldApplyRelations)
                        ApplyCharacterBuffs(envir, buffRows);
                }
                else if (allowSeedFromMemory)
                {
                    var buffs = CaptureCharacterBuffs(envir);
                    session.RunInTransaction(s => ReplaceCharacterBuffs(s, buffs));
                }
            }
            catch (Exception ex)
            {
                MessageQueue.Instance.EnqueueDebugging($"[SQL:{_provider}] Accounts 扩展关系表同步异常（Auctions/Mails/GameshopLog/RespawnSaves/CharacterProgress，将保持当前内存数据）：{ex}");
            }
        }

        public void BeginSaveAccounts(Envir envir)
        {
            SaveAccounts(envir);
        }

        public void SaveAccounts(Envir envir)
        {
            if (envir == null) throw new ArgumentNullException(nameof(envir));

            EnsureInitialized();

            try
            {
                var runner = new SqlDomainTransactionRunner(_provider, _databaseOptions);
                runner.RunWithSnapshot(
                    domain: SqlSaveDomain.Accounts,
                    snapshotFactory: () =>
                    {
                        var saveEpochUtcMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

                        CaptureItems(envir, out var items, out var itemStats, out var awakeLevels, out var slotLinks);
                        CaptureContainers(envir, out var storage, out var storageSlots, out var containers, out var containerSlots);
                        var auctions = CaptureAuctions(envir);
                        CaptureMails(envir, out var mails, out var mailItems);
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
                        var characterBuffs = CaptureCharacterBuffs(envir);

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
                            characterBuffs: characterBuffs);
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
                        RunStep("Accounts", () => UpsertAccounts(session, snapshot.Accounts));
                        RunStep("Characters", () => UpsertCharacters(session, snapshot.Characters));
                        RunStep("Items", () => ReplaceItems(session, snapshot.Items, snapshot.ItemAddedStats, snapshot.ItemAwakeLevels, snapshot.ItemSlotLinks, snapshot.SaveEpochUtcMs));
                        RunStep("Containers", () => ReplaceContainers(session, snapshot.AccountStorage, snapshot.AccountStorageSlots, snapshot.CharacterContainers, snapshot.CharacterContainerSlots, snapshot.SaveEpochUtcMs));
                        RunStep("Auctions", () => ReplaceAuctions(session, snapshot.Auctions, snapshot.SaveEpochUtcMs));
                        RunStep("Mails", () => ReplaceMails(session, snapshot.Mails, snapshot.MailItems, snapshot.SaveEpochUtcMs));
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
                        RunStep("CharacterBuffs", () => ReplaceCharacterBuffs(session, snapshot.CharacterBuffs, snapshot.SaveEpochUtcMs));
                        RunStep(
                            "AccountsRelationsEpoch",
                            () => UpsertServerMeta(session, ServerMetaKeyAccountsRelationsEpochUtcMs, snapshot.SaveEpochUtcMs.ToString(), snapshot.SaveEpochUtcMs));
                    });
            }
            catch (Exception ex)
            {
                // 保持与 legacy 保存一致：保存失败不应直接终止服务器主循环。
                MessageQueue.Instance.Enqueue($"[SQL:{_provider}] Accounts 保存异常：{ex}");
            }
        }

        public void LoadGuilds(Envir envir)
        {
            if (envir == null) throw new ArgumentNullException(nameof(envir));

            EnsureInitialized();

            IReadOnlyList<LegacyFileRow> files = null;
            try
            {
                using var session = SqlSession.Open(_provider, _databaseOptions, maxRetries: 3, baseRetryDelayMs: 200);
                files = LoadLegacyFiles(session, LegacyFilesDomainGuilds);
            }
            catch (Exception ex)
            {
                MessageQueue.Instance.EnqueueDebugging($"[SQL:{_provider}] Guilds legacy_files 读取失败，回退到文件：{ex}");
                envir.Legacy_LoadGuilds();
                return;
            }

            if (files == null || files.Count == 0)
            {
                envir.Legacy_LoadGuilds();

                if (Settings.AutoImportLegacyOnEmpty)
                {
                    var runner = new SqlDomainTransactionRunner(_provider, _databaseOptions);
                    runner.RunWithSnapshot(
                        domain: SqlSaveDomain.Guilds,
                        snapshotFactory: () => CaptureGuildLegacyFiles(envir),
                        work: (session, snapshot) => ReplaceLegacyFiles(session, LegacyFilesDomainGuilds, snapshot));
                }

                return;
            }

            try
            {
                ApplyGuildsFromLegacyFiles(envir, files);
            }
            catch (Exception ex)
            {
                MessageQueue.Instance.Enqueue($"[SQL:{_provider}] Guilds 从 DB 加载失败，回退到文件：{ex}");
                envir.Legacy_LoadGuilds();
            }
        }

        public void SaveGuilds(Envir envir, bool forced)
        {
            if (envir == null) throw new ArgumentNullException(nameof(envir));

            EnsureInitialized();

            var runner = new SqlDomainTransactionRunner(_provider, _databaseOptions);
            runner.RunWithSnapshot(
                domain: SqlSaveDomain.Guilds,
                snapshotFactory: () => CaptureGuildLegacyFiles(envir),
                work: (session, snapshot) => ReplaceLegacyFiles(session, LegacyFilesDomainGuilds, snapshot));
        }

        public void SaveGoods(Envir envir, bool forced)
        {
            if (envir == null) throw new ArgumentNullException(nameof(envir));

            EnsureInitialized();

            var runner = new SqlDomainTransactionRunner(_provider, _databaseOptions);
            runner.RunWithSnapshot(
                domain: SqlSaveDomain.Goods,
                snapshotFactory: () => CaptureGoodsLegacyFiles(envir, forced),
                work: (session, snapshot) => ReplaceLegacyFiles(session, LegacyFilesDomainGoods, snapshot));
        }

        public void LoadConquests(Envir envir)
        {
            if (envir == null) throw new ArgumentNullException(nameof(envir));

            EnsureInitialized();

            IReadOnlyList<LegacyFileRow> files = null;
            try
            {
                using var session = SqlSession.Open(_provider, _databaseOptions, maxRetries: 3, baseRetryDelayMs: 200);
                files = LoadLegacyFiles(session, LegacyFilesDomainConquests);
            }
            catch (Exception ex)
            {
                MessageQueue.Instance.EnqueueDebugging($"[SQL:{_provider}] Conquests legacy_files 读取失败，回退到文件：{ex}");
                envir.Legacy_LoadConquests();
                return;
            }

            if (files == null || files.Count == 0)
            {
                envir.Legacy_LoadConquests();

                if (Settings.AutoImportLegacyOnEmpty)
                {
                    var runner = new SqlDomainTransactionRunner(_provider, _databaseOptions);
                    runner.RunWithSnapshot(
                        domain: SqlSaveDomain.Conquests,
                        snapshotFactory: () => CaptureConquestLegacyFiles(envir),
                        work: (session, snapshot) => ReplaceLegacyFiles(session, LegacyFilesDomainConquests, snapshot));
                }

                return;
            }

            try
            {
                ApplyConquestsFromLegacyFiles(envir, files);
            }
            catch (Exception ex)
            {
                MessageQueue.Instance.Enqueue($"[SQL:{_provider}] Conquests 从 DB 加载失败，回退到文件：{ex}");
                envir.Legacy_LoadConquests();
            }
        }

        public void SaveConquests(Envir envir, bool forced)
        {
            if (envir == null) throw new ArgumentNullException(nameof(envir));

            EnsureInitialized();

            var runner = new SqlDomainTransactionRunner(_provider, _databaseOptions);
            runner.RunWithSnapshot(
                domain: SqlSaveDomain.Conquests,
                snapshotFactory: () => CaptureConquestLegacyFiles(envir),
                work: (session, snapshot) => ReplaceLegacyFiles(session, LegacyFilesDomainConquests, snapshot));
        }

        public void SaveArchivedCharacter(Envir envir, CharacterInfo info)
        {
            if (envir == null) throw new ArgumentNullException(nameof(envir));
            if (info == null) return;

            EnsureInitialized();

            var now = envir.Now;
            var relativePath = $"{info.Name}{now:_MMddyyyy_HHmmss}.MirCA";
            var payload = Array.Empty<byte>();

            try
            {
                using var ms = new MemoryStream();
                using (var writer = new BinaryWriter(ms))
                {
                    writer.Write(Envir.Version);
                    writer.Write(Envir.CustomVersion);
                    info.Save(writer);
                    writer.Flush();
                }

                payload = ms.ToArray();
            }
            catch (Exception ex)
            {
                MessageQueue.Instance.EnqueueDebugging($"[SQL:{_provider}] Archive payload 序列化失败：{ex}");
                return;
            }

            try
            {
                var runner = new SqlDomainTransactionRunner(_provider, _databaseOptions);
                runner.RunWithSnapshot(
                    domain: SqlSaveDomain.Archive,
                    snapshotFactory: () => new LegacyFileRow
                    {
                        RelativePath = relativePath,
                        Payload = payload,
                    },
                    work: (session, row) => UpsertLegacyFile(session, LegacyFilesDomainArchive, row));
            }
            catch (Exception ex)
            {
                MessageQueue.Instance.EnqueueDebugging($"[SQL:{_provider}] Archive legacy_files 写入失败（将继续依赖文件）：{ex}");
            }
        }

        public CharacterInfo GetArchivedCharacter(Envir envir, string name)
        {
            if (envir == null) throw new ArgumentNullException(nameof(envir));

            EnsureInitialized();

            name = (name ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(name))
                return null;

            try
            {
                using var session = SqlSession.Open(_provider, _databaseOptions, maxRetries: 3, baseRetryDelayMs: 200);

                var prefix = NormalizeLegacyRelativePath(name) + "%";
                var files = session.Query<LegacyFileRow>(
                    "SELECT relative_path AS RelativePath, payload AS Payload FROM legacy_files WHERE domain=@Domain AND relative_path LIKE @Prefix",
                    new { Domain = LegacyFilesDomainArchive, Prefix = prefix });

                files = files
                    .Where(f => f != null && !string.IsNullOrWhiteSpace(f.RelativePath) && f.RelativePath.EndsWith(".MirCA", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (files.Count == 0)
                    return envir.Legacy_GetArchivedCharacter(name);

                if (files.Count != 1)
                    return null;

                var payload = files[0].Payload ?? Array.Empty<byte>();
                using var ms = new MemoryStream(payload);
                using var reader = new BinaryReader(ms);

                var version = reader.ReadInt32();
                var customVersion = reader.ReadInt32();
                return new CharacterInfo(reader, version, customVersion);
            }
            catch (Exception ex)
            {
                MessageQueue.Instance.EnqueueDebugging($"[SQL:{_provider}] Archive 从 DB 读取失败，回退到文件：{ex}");
                return envir.Legacy_GetArchivedCharacter(name);
            }
        }
    }
}
