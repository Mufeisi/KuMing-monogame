using Server;
using Server.MirDatabase;
using Server.MirEnvir;
using Server.Persistence;
using Server.Persistence.Sql;
using Xunit;

namespace Base05.Tests;

public sealed class SqlPersistenceRoundTripTests
{
    [Fact]
    public void Sqlite_round_trips_account_character_inventory_storage_and_mail()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"base05-{Guid.NewGuid():N}.db");
        var options = new SqlDatabaseOptions { SqlitePath = databasePath };
        var persistence = new SqlServerPersistence(DatabaseProviderKind.Sqlite, options);

        try
        {
            var source = new Envir();
            var account = new AccountInfo
            {
                Index = 101,
                AccountID = "base05-account",
                UserName = "Base05",
                Gold = 1234,
            };
            var character = new CharacterInfo
            {
                Index = 202,
                Name = "base05-character",
                Level = 12,
                AccountInfo = account,
            };
            account.Characters.Add(character);
            source.AccountList.Add(account);
            source.CharacterList.Add(character);

            var itemInfo = new ItemInfo { Index = 303, Name = "base05-item", StackSize = 99 };
            source.ItemInfoList.Add(itemInfo);
            var inventoryItem = new UserItem(itemInfo) { UniqueID = 404, Count = 3, CurrentDura = 7, MaxDura = 9 };
            var storageItem = new UserItem(itemInfo) { UniqueID = 405, Count = 2, CurrentDura = 5, MaxDura = 9 };
            character.Inventory[0] = inventoryItem;
            account.Storage[0] = storageItem;

            var mailItem = new UserItem(itemInfo) { UniqueID = 406, Count = 1, CurrentDura = 4, MaxDura = 9 };
            var mail = new MailInfo
            {
                MailID = 407,
                Sender = "base05-sender",
                RecipientIndex = character.Index,
                Message = "base05-mail",
                Gold = 88,
                DateSent = new DateTime(2026, 8, 5, 12, 0, 0, DateTimeKind.Utc),
                CanReply = true,
            };
            mail.Items.Add(mailItem);
            character.Mail.Add(mail);

            persistence.SaveAccounts(source);

            var restored = new Envir();
            restored.ItemInfoList.Add(new ItemInfo { Index = 303, Name = "base05-item", StackSize = 99 });
            persistence.LoadAccounts(restored);

            var restoredAccount = Assert.Single(restored.AccountList);
            var restoredCharacter = Assert.Single(restored.CharacterList);
            Assert.Equal("base05-account", restoredAccount.AccountID);
            Assert.Equal(1234u, restoredAccount.Gold);
            Assert.Equal(101, restoredAccount.Index);
            Assert.Equal("base05-character", restoredCharacter.Name);
            Assert.Equal(202, restoredCharacter.Index);
            Assert.Same(restoredAccount, restoredCharacter.AccountInfo);
            Assert.Same(restoredCharacter, Assert.Single(restoredAccount.Characters));

            var restoredInventoryItem = Assert.IsType<UserItem>(restoredCharacter.Inventory[0]);
            Assert.Equal(404ul, restoredInventoryItem.UniqueID);
            Assert.Equal(303, restoredInventoryItem.ItemIndex);
            Assert.Equal(303, restoredInventoryItem.Info.Index);
            Assert.Equal(3, restoredInventoryItem.Count);
            Assert.Null(restoredCharacter.Inventory[1]);

            var restoredStorageItem = Assert.IsType<UserItem>(restoredAccount.Storage[0]);
            Assert.Equal(405ul, restoredStorageItem.UniqueID);
            Assert.Equal(303, restoredStorageItem.ItemIndex);
            Assert.Equal(303, restoredStorageItem.Info.Index);
            Assert.Equal(2, restoredStorageItem.Count);
            Assert.Null(restoredAccount.Storage[1]);

            var restoredMail = Assert.Single(restoredCharacter.Mail);
            Assert.Equal(407ul, restoredMail.MailID);
            Assert.Equal(202, restoredMail.RecipientIndex);
            Assert.Equal("base05-mail", restoredMail.Message);
            Assert.Equal(88u, restoredMail.Gold);
            var restoredMailItem = Assert.Single(restoredMail.Items);
            Assert.Equal(406ul, restoredMailItem.UniqueID);
            Assert.Equal(303, restoredMailItem.ItemIndex);
            Assert.Equal(303, restoredMailItem.Info.Index);
        }
        finally
        {
            TryDelete(databasePath);
            TryDelete(databasePath + "-wal");
            TryDelete(databasePath + "-shm");
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch
        {
            // Best effort cleanup; the test database is isolated under the temp directory.
        }
    }
}
