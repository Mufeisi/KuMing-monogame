using Server.MirNetwork;
using Server.MirEnvir;
using Server.Utils;
using C = ClientPackets;

namespace Server.MirDatabase
{
    public class AccountInfo
    {       
        protected static Envir Envir
        {
            get { return Envir.Main; }
        }
        protected static MessageQueue MessageQueue => MessageQueue.Instance;

        public int Index;

        public string AccountID = string.Empty;

        private string password = string.Empty;
        public string Password
        {
            get { return password; }
            set
            {                
                password = PasswordHasher.Hash(value ?? string.Empty);
                Salt = Array.Empty<byte>();
                
            }
        }

        public byte[] Salt = new byte[24];

        internal void SetPasswordHashAndSalt(string passwordHash, byte[] salt)
        {
            password = passwordHash ?? string.Empty;
            Salt = PasswordHasher.IsArgon2idHash(password)
                ? Array.Empty<byte>()
                : salt == null ? Array.Empty<byte>() : (byte[])salt.Clone();
        }

        internal PasswordVerificationResult VerifyPassword(string candidate)
        {
            var result = PasswordHasher.Verify(password, candidate ?? string.Empty, Salt);
            if (result == PasswordVerificationResult.ValidNeedsUpgrade)
            {
                Password = candidate ?? string.Empty;
            }

            return result;
        }

        public string UserName = string.Empty;
        public DateTime BirthDate;
        public string SecretQuestion = string.Empty;
        public string SecretAnswer = string.Empty;
        public string EMailAddress = string.Empty;

        public string CreationIP = string.Empty;
        public DateTime CreationDate;

        public bool Banned;
        public bool RequirePasswordChange;
        public string BanReason = string.Empty;
        public DateTime ExpiryDate;
        public int WrongPasswordCount;

        public string LastIP = string.Empty;
        public DateTime LastDate;

        public List<CharacterInfo> Characters = new List<CharacterInfo>();

        public UserItem[] Storage = new UserItem[80];
        public bool HasExpandedStorage;
        public DateTime ExpandedStorageExpiryDate;
        public uint Gold;
        public uint Credit;

        public MirConnection Connection;
        
        public LinkedList<AuctionInfo> Auctions = new LinkedList<AuctionInfo>();

        /// <summary>账号权限等级：0=普通玩家，&gt;0=管理员（按等级细分的命令权限随后续版本扩展）。</summary>
        public int AdminLevel;

        /// <summary>兼容布尔语义：是否具备管理员权限（权限等级 &gt; 0）。</summary>
        public bool AdminAccount => AdminLevel > 0;

        public AccountInfo()
        {

        }

        public AccountInfo(C.NewAccount p)
        {
            AccountID = p.AccountID;

            Password = p.Password;
            UserName = p.UserName;
            SecretQuestion = p.SecretQuestion;
            SecretAnswer = p.SecretAnswer;
            EMailAddress = p.EMailAddress;

            BirthDate = p.BirthDate;
            CreationDate = Envir.Now;
        }
        public AccountInfo(BinaryReader reader)
        {
            Index = reader.ReadInt32();

            AccountID = reader.ReadString();
            if (Envir.LoadVersion < 94)
                Password = reader.ReadString();
            else
                password = reader.ReadString();

            if (Envir.LoadVersion > 93)
                Salt = reader.ReadBytes(reader.ReadInt32());

            if (Envir.LoadVersion > 97)
                RequirePasswordChange = reader.ReadBoolean();

            UserName = reader.ReadString();
            BirthDate = DateTime.FromBinary(reader.ReadInt64());
            SecretQuestion = reader.ReadString();
            SecretAnswer = reader.ReadString();
            EMailAddress = reader.ReadString();

            CreationIP = reader.ReadString();
            CreationDate = DateTime.FromBinary(reader.ReadInt64());

            Banned = reader.ReadBoolean();
            BanReason = reader.ReadString();
            ExpiryDate = DateTime.FromBinary(reader.ReadInt64());

            LastIP = reader.ReadString();
            LastDate = DateTime.FromBinary(reader.ReadInt64());

            int count = reader.ReadInt32();

            for (int i = 0; i < count; i++)
            {
                var info = new CharacterInfo(reader, Envir.LoadVersion, Envir.LoadCustomVersion) { AccountInfo = this };

                if (info.Deleted && info.DeleteDate.AddMonths(Settings.ArchiveDeletedCharacterAfterMonths) <= Envir.Now)
                {
                    MessageQueue.Enqueue($"玩家 {info.Name} 由于已删除角色 {Settings.ArchiveDeletedCharacterAfterMonths} 月已存档处理");
                    Envir.SaveArchivedCharacter(info);
                    continue;
                }

                if (info.LastLoginDate == DateTime.MinValue && info.CreationDate.AddMonths(Settings.ArchiveInactiveCharacterAfterMonths) <= Envir.Now)
                {
                    MessageQueue.Enqueue($"玩家 {info.Name} 由于 {Settings.ArchiveInactiveCharacterAfterMonths} 月未登录已存档处理");
                    Envir.SaveArchivedCharacter(info);
                    continue;
                }
                
                if (info.LastLoginDate > DateTime.MinValue && info.LastLoginDate.AddMonths(Settings.ArchiveInactiveCharacterAfterMonths) <= Envir.Now)
                {
                    MessageQueue.Enqueue($"玩家 {info.Name} 由于 {Settings.ArchiveInactiveCharacterAfterMonths} 月未激活已存档处理");
                    Envir.SaveArchivedCharacter(info);
                    continue;
                }

                Characters.Add(info);
            }

            if (Envir.LoadVersion > 75)
            {
                HasExpandedStorage = reader.ReadBoolean();
                ExpandedStorageExpiryDate = DateTime.FromBinary(reader.ReadInt64());
            }
            
            Gold = reader.ReadUInt32();
            if (Envir.LoadVersion >= 63) Credit = reader.ReadUInt32();

            count = reader.ReadInt32();

            Array.Resize(ref Storage, count);

            for (int i = 0; i < count; i++)
            {
                if (!reader.ReadBoolean()) continue;
                UserItem item = new UserItem(reader, Envir.LoadVersion, Envir.LoadCustomVersion);
                if (Envir.BindItem(item) && i < Storage.Length)
                    Storage[i] = item;
            }

            if (Envir.LoadVersion >= 10) AdminLevel = reader.ReadBoolean() ? 1 : 0;
            if (!AdminAccount)
            {
                for (int i = 0; i < Characters.Count; i++)
                {
                    if (Characters[i] == null) continue;
                    if (Characters[i].Deleted) continue;
                    if ((Envir.Now - Characters[i].LastLogoutDate).TotalDays > 13) continue;
                    Envir.CheckRankUpdate(Characters[i]);
                }
            }
        }

        public void Save(BinaryWriter writer)
        {
            writer.Write(Index);
            writer.Write(AccountID);
            writer.Write(Password);
            writer.Write(Salt.Length);
            writer.Write(Salt);
            writer.Write(RequirePasswordChange);

            writer.Write(UserName);
            writer.Write(BirthDate.ToBinary());
            writer.Write(SecretQuestion);
            writer.Write(SecretAnswer);
            writer.Write(EMailAddress);

            writer.Write(CreationIP);
            writer.Write(CreationDate.ToBinary());

            writer.Write(Banned);
            writer.Write(BanReason);
            writer.Write(ExpiryDate.ToBinary());

            writer.Write(LastIP);
            writer.Write(LastDate.ToBinary());

            writer.Write(Characters.Count);
            for (int i = 0; i < Characters.Count; i++)
            {
                Characters[i].Save(writer);
            }

            writer.Write(HasExpandedStorage);
            writer.Write(ExpandedStorageExpiryDate.ToBinary());
            writer.Write(Gold);
            writer.Write(Credit);
            writer.Write(Storage.Length);
            for (int i = 0; i < Storage.Length; i++)
            {
                writer.Write(Storage[i] != null);
                if (Storage[i] == null) continue;

                Storage[i].Save(writer);
            }
            writer.Write(AdminLevel);
        }

        public List<SelectInfo> GetSelectInfo()
        {
            List<SelectInfo> list = new List<SelectInfo>();

            for (int i = 0; i < Characters.Count; i++)
            {
                if (Characters[i].Deleted) continue;
                list.Add(Characters[i].ToSelectInfo());
                if (list.Count >= Globals.MaxCharacterCount) break;
            }

            return list;
        }

        public int ExpandStorage()
        {
            if (!HasExpandedStorage)
            {
                if (Storage.Length == Globals.StorageGridSize)
                    Array.Resize(ref Storage, Storage.Length + Globals.StorageGridSize);
            }

            return Storage.Length;
        }

        public bool IsLingFengStorageOpen(int page)
        {
            if (page == 1) return true;
            return page is >= 2 and <= 4 && HasExpandedStorage &&
                   Storage != null && Storage.Length >= page * Globals.StorageGridSize;
        }

        public bool TryOpenLingFengStorage(int page)
        {
            if (page is < 2 or > 4) return false;
            int requiredLength = checked(page * Globals.StorageGridSize);
            if (Storage == null) Storage = new UserItem[Globals.StorageGridSize];
            if (Storage.Length < requiredLength)
                Array.Resize(ref Storage, requiredLength);
            HasExpandedStorage = true;
            ExpandedStorageExpiryDate = new DateTime(9990, 1, 1);
            return true;
        }

        public bool IsValidStorageIndex(int index)
        {
            return index >= 0 && Storage != null && index < Storage.Length &&
                   (index < Globals.StorageGridSize || HasExpandedStorage);
        }
    }
}
