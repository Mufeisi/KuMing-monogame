using Server.MirDatabase;
using Server.MirEnvir;

namespace Server.Persistence.Sql
{
    public sealed partial class SqlServerPersistence
    {
        private void LoadAccountsAtomically(Envir envir)
        {
            AccountsSnapshot snapshot = null;
            IReadOnlyList<AccountRow> identityRows = null;
            if (_provider == DatabaseProviderKind.MySql)
            {
                using var identitySession = SqlSession.Open(_provider, _identityOptions, maxRetries: 3, baseRetryDelayMs: 200);
                identitySession.RunInTransaction(s => identityRows = LoadIdentityAccountRows(s));
            }

            using (var session = SqlSession.Open(_provider, _databaseOptions, maxRetries: 3, baseRetryDelayMs: 200))
            {
                session.RunInTransaction(s =>
                {
                    var accountRows = _provider == DatabaseProviderKind.Sqlite
                        ? LoadAccountRows(s)
                        : MergeAccountWallets(identityRows ?? Array.Empty<AccountRow>(), LoadAccountWalletRows(s));
                    var characterRows = LoadCharacterRows(s);
                    var accountCount = accountRows.Count;
                    var characterCount = characterRows.Count;
                    var epoch = TryLoadServerMetaInt64(s, ServerMetaKeyAccountsRelationsEpochUtcMs);
                    if ((accountCount > 0 || characterCount > 0) && epoch <= 0)
                        throw new InvalidOperationException("Character 关系快照缺少完成 epoch，拒绝应用可能不完整的数据。");

                    var itemRows = LoadItemRows(s);
                    var auctionRows = LoadAuctionRows(s);
                    var mailRows = LoadMailRows(s);
                    var buffRows = LoadCharacterBuffRows(s);
                    snapshot = new AccountsSnapshot(
                        saveEpochUtcMs: epoch,
                        nextIds: LoadNextIds(s, AccountsNextIdKeys),
                        accounts: accountRows,
                        characters: characterRows,
                        items: itemRows,
                        itemAddedStats: LoadItemAddedStatRows(s),
                        itemAwakeLevels: LoadItemAwakeLevelRows(s),
                        itemSlotLinks: LoadItemSlotLinkRows(s),
                        itemLocations: LoadItemLocationRows(s),
                        accountStorage: LoadAccountStorageRows(s),
                        accountStorageSlots: LoadAccountStorageSlotRows(s),
                        characterContainers: LoadCharacterContainerRows(s),
                        characterContainerSlots: LoadCharacterContainerSlotRows(s),
                        auctions: auctionRows,
                        mails: mailRows,
                        mailItems: LoadMailItemRows(s),
                        gameshopLog: LoadGameshopLogRows(s),
                        respawnSaves: LoadRespawnSaveRows(s),
                        characterMagics: LoadCharacterMagicRows(s),
                        characterCompletedQuests: LoadCharacterCompletedQuestRows(s),
                        characterFlags: LoadCharacterFlagRows(s),
                        characterGameshopPurchases: LoadCharacterGameshopPurchaseRows(s),
                        currentQuests: LoadCurrentQuestRows(s),
                        currentQuestKillTasks: LoadCurrentQuestKillTaskRows(s),
                        currentQuestItemTasks: LoadCurrentQuestItemTaskRows(s),
                        currentQuestFlagTasks: LoadCurrentQuestFlagTaskRows(s),
                        characterPets: LoadCharacterPetRows(s),
                        characterFriends: LoadCharacterFriendRows(s),
                        characterRentedItems: LoadCharacterRentedItemRows(s),
                        characterIntelligentCreatures: LoadCharacterIntelligentCreatureRows(s),
                        heroDetails: LoadHeroDetailRows(s),
                        characterHeroSlots: LoadCharacterHeroSlotRows(s),
                        characterBuffs: buffRows,
                        characterBuffStats: LoadCharacterBuffStatRows(s),
                        characterBuffValues: LoadCharacterBuffValueRows(s),
                        characterBuffData: LoadCharacterBuffDataRows(s),
                        conquestRuntime: Array.Empty<ConquestRuntimeRow>(),
                        conquestFacilities: Array.Empty<ConquestFacilityRow>());
                });
            }

            var completedSnapshot = snapshot ?? throw new InvalidOperationException("Character 启动快照未生成。");
            ValidateAccountsSnapshot(envir, completedSnapshot);
            ApplyAccountsSnapshot(envir, completedSnapshot);
        }

        private static IReadOnlyList<ItemLocationRow> LoadItemLocationRows(SqlSession session) =>
            session.Query<ItemLocationRow>(
                "SELECT item_id AS ItemId,location_kind AS LocationKind,owner_id AS OwnerId,container_kind AS ContainerKind,slot_index AS SlotIndex,parent_item_id AS ParentItemId FROM item_locations ORDER BY item_id");

        private static IReadOnlyList<AccountRow> LoadIdentityAccountRows(SqlSession session) =>
            session.Query<AccountRow>(
                "SELECT account_id AS AccountId,account_name AS AccountName,password_hash AS PasswordHash,password_salt AS PasswordSalt," +
                "require_password_change AS RequirePasswordChange,user_name AS UserName,birth_utc_ms AS BirthUtcMs,secret_question AS SecretQuestion," +
                "secret_answer AS SecretAnswer,email_address AS EmailAddress,creation_ip AS CreationIp,creation_utc_ms AS CreationUtcMs," +
                "banned AS Banned,ban_reason AS BanReason,expiry_utc_ms AS ExpiryUtcMs,last_ip AS LastIp,last_utc_ms AS LastUtcMs," +
                "wrong_password_count AS WrongPasswordCount,admin_account AS AdminAccount FROM accounts ORDER BY account_id");

        private sealed class AccountWalletLoadRow
        {
            public long AccountId { get; set; }
            public long Gold { get; set; }
            public long Credit { get; set; }
        }

        private static IReadOnlyList<AccountWalletLoadRow> LoadAccountWalletRows(SqlSession session) =>
            session.Query<AccountWalletLoadRow>("SELECT account_id AS AccountId,gold AS Gold,credit AS Credit FROM account_wallets ORDER BY account_id");

        private static IReadOnlyList<AccountRow> MergeAccountWallets(
            IReadOnlyList<AccountRow> accounts,
            IReadOnlyList<AccountWalletLoadRow> wallets)
        {
            var walletByAccount = wallets.ToDictionary(row => row.AccountId);
            foreach (var account in accounts)
            {
                if (!walletByAccount.TryGetValue(account.AccountId, out var wallet)) continue;
                account.Gold = wallet.Gold;
                account.Credit = wallet.Credit;
            }
            return accounts;
        }

        private static void ValidateAccountsSnapshot(Envir envir, AccountsSnapshot snapshot)
        {
            var accountIds = UniqueIds(snapshot.Accounts.Select(row => row.AccountId), "account");
            var characterIds = UniqueIds(snapshot.Characters.Select(row => row.CharacterId), "character");
            var itemIds = UniqueIds(snapshot.Items.Select(row => row.ItemId), "item");

            foreach (var character in snapshot.Characters)
            {
                if (character.CharacterKind == (int)CharacterEntityKind.Player && !accountIds.Contains(character.AccountId))
                    throw new InvalidDataException($"活跃角色 {character.CharacterId} 引用缺失的 Identity 账号 {character.AccountId}。");
                if (character.CharacterKind is not (int)CharacterEntityKind.Player and not (int)CharacterEntityKind.Hero)
                    throw new InvalidDataException($"角色 {character.CharacterId} 的 character_kind 无效：{character.CharacterKind}。");
            }

            var worldItemIds = envir.ItemInfoList.Where(info => info != null).Select(info => info.Index).ToHashSet();
            foreach (var item in snapshot.Items)
                if (!worldItemIds.Contains(item.ItemIndex))
                    throw new InvalidDataException($"物品实例 {item.ItemId} 引用缺失的 World 物品模板 {item.ItemIndex}。");

            foreach (var link in snapshot.ItemSlotLinks)
                if (!itemIds.Contains(link.ParentItemId) || !itemIds.Contains(link.ChildItemId))
                    throw new InvalidDataException($"镶嵌关系 {link.ParentItemId}:{link.SlotIndex}->{link.ChildItemId} 引用缺失物品。");

            foreach (var location in snapshot.ItemLocations)
            {
                if (!itemIds.Contains(location.ItemId))
                    throw new InvalidDataException($"物品位置引用缺失物品 {location.ItemId}。");
                if (location.ParentItemId.HasValue && !itemIds.Contains(location.ParentItemId.Value))
                    throw new InvalidDataException($"物品位置 {location.ItemId} 引用缺失父物品 {location.ParentItemId.Value}。");
            }

            foreach (var slot in snapshot.AccountStorageSlots)
                if (!accountIds.Contains(slot.AccountId) || !itemIds.Contains(slot.ItemId))
                    throw new InvalidDataException($"账号仓库槽位 {slot.AccountId}:{slot.SlotIndex} 引用无效。");

            foreach (var auction in snapshot.Auctions)
            {
                if (!itemIds.Contains(auction.ItemId) || !characterIds.Contains(auction.SellerCharacterId))
                    throw new InvalidDataException($"拍卖 {auction.AuctionId} 引用无效物品或卖家。");
                if (auction.CurrentBuyerCharacterId > 0 && !characterIds.Contains(auction.CurrentBuyerCharacterId))
                    throw new InvalidDataException($"拍卖 {auction.AuctionId} 引用缺失买家 {auction.CurrentBuyerCharacterId}。");
            }
        }

        private static HashSet<long> UniqueIds(IEnumerable<long> values, string domain)
        {
            var result = new HashSet<long>();
            foreach (var value in values)
                if (value <= 0 || !result.Add(value))
                    throw new InvalidDataException($"{domain} ID 无效或重复：{value}。");
            return result;
        }

        private void ApplyAccountsSnapshot(Envir envir, AccountsSnapshot snapshot)
        {
            lock (Envir.LoadLock)
            {
                ResetAccountLoadState(envir);
                var accountsById = new Dictionary<long, AccountInfo>();
                foreach (var row in snapshot.Accounts)
                {
                    var account = new AccountInfo { Index = (int)Math.Clamp(row.AccountId, int.MinValue, int.MaxValue) };
                    accountsById[row.AccountId] = account;
                    envir.AccountList.Add(account);
                }

                foreach (var row in snapshot.Characters)
                {
                    if (row.CharacterKind == (int)CharacterEntityKind.Hero)
                    {
                        envir.HeroList.Add(new HeroInfo
                        {
                            Index = (int)Math.Clamp(row.CharacterId, int.MinValue, int.MaxValue),
                            Inventory = new UserItem[10],
                            Equipment = new UserItem[14],
                            Magics = new List<UserMagic>(),
                        });
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

            ApplyAccountsNextIds(envir, snapshot.NextIds);
            ApplyAccounts(envir, snapshot.Accounts);
            ApplyCharacters(envir, snapshot.Characters);
            var itemsById = ApplyItems(envir, snapshot.Items, snapshot.ItemAddedStats, snapshot.ItemAwakeLevels, snapshot.ItemSlotLinks);
            ApplyContainers(envir, itemsById, snapshot.AccountStorage, snapshot.AccountStorageSlots, snapshot.CharacterContainers, snapshot.CharacterContainerSlots);
            ApplyAuctions(envir, itemsById, snapshot.Auctions);
            ApplyMails(envir, itemsById, snapshot.Mails, snapshot.MailItems);
            ApplyGameshopLog(envir, snapshot.GameshopLog);
            ApplyRespawnSaves(envir, snapshot.RespawnSaves);
            ApplyCharacterMagics(envir, snapshot.CharacterMagics);
            ApplyCharacterCompletedQuests(envir, snapshot.CharacterCompletedQuests);
            ApplyCharacterFlags(envir, snapshot.CharacterFlags);
            ApplyCharacterGameshopPurchases(envir, snapshot.CharacterGameshopPurchases);
            ApplyCurrentQuests(envir, snapshot.CurrentQuests, snapshot.CurrentQuestKillTasks, snapshot.CurrentQuestItemTasks, snapshot.CurrentQuestFlagTasks);
            ApplyCharacterPets(envir, snapshot.CharacterPets);
            ApplyCharacterFriends(envir, snapshot.CharacterFriends);
            ApplyCharacterRentedItems(envir, snapshot.CharacterRentedItems);
            ApplyCharacterIntelligentCreatures(envir, snapshot.CharacterIntelligentCreatures);
            ApplyHeroDetails(envir, snapshot.HeroDetails);
            ApplyCharacterHeroSlots(envir, snapshot.CharacterHeroSlots);
            ApplyCharacterBuffs(envir, snapshot.CharacterBuffs, snapshot.CharacterBuffStats, snapshot.CharacterBuffValues, snapshot.CharacterBuffData);
            _startupItemsById = itemsById;
        }
    }
}
