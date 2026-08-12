using Server.MirEnvir;
using Server.MirObjects;

namespace Server.MirDatabase
{
    public class RecipeInfo
    {
        protected static Envir Envir
        {
            get { return Envir.Main; }
        }

        protected static MessageQueue MessageQueue
        {
            get { return MessageQueue.Instance; }
        }

        public UserItem Item;
        public List<UserItem> Ingredients;
        public List<UserItem> Tools;

        public List<int> RequiredFlag = new List<int>();
        public ushort? RequiredLevel = null;
        public List<int> RequiredQuest = new List<int>();
        public List<MirClass> RequiredClass = new List<MirClass>();
        public MirGender? RequiredGender = null;

        public byte Chance = 100;
        public uint Gold = 0;

        internal RecipeInfo()
        {
            Tools = new List<UserItem>();
            Ingredients = new List<UserItem>();
        }

        public RecipeInfo(string name)
            : this(name, ++Envir.NextRecipeID)
        {
        }

        internal RecipeInfo(string name, ulong uniqueId)
        {
            ItemInfo itemInfo = Envir.GetItemInfo(name);
            if (itemInfo == null)
            {
                MessageQueue.Enqueue(string.Format("缺少物品: {0}", name));
                return;
            }

            Item = Envir.CreateShopItem(itemInfo, uniqueId);
        }

        public bool MatchItem(int index)
        {
            return Item != null && Item.ItemIndex == index;
        }

        public bool CanCraft(PlayerObject player)
        {
            if (RequiredLevel != null && RequiredLevel.Value > player.Level)
                return false;

            if (RequiredGender != null && RequiredGender.Value != player.Gender)
                return false;

            if (RequiredClass.Count > 0 && !RequiredClass.Contains(player.Class))
                return false;

            if (RequiredFlag.Count > 0)
            {
                foreach (var flag in RequiredFlag)
                {
                     if(!player.Info.Flags[flag])
                        return false;
                }
            }

            if (RequiredQuest.Count > 0)
            {
                foreach (var quest in RequiredQuest)
                {
                    if (!player.Info.CompletedQuests.Contains(quest))
                        return false;
                }
            }

            return true;
        }

        public ClientRecipeInfo CreateClientRecipeInfo()
        {
            ClientRecipeInfo clientInfo = new ClientRecipeInfo
            {
                Gold = Gold,
                Chance = Chance,
                Item = Item.Clone(),
                Tools = Tools.Select(x => x).ToList(),
                Ingredients = Ingredients.Select(x => x).ToList()
            };

            return clientInfo;
        }
    }
}
