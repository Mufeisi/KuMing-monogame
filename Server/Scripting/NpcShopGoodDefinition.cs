namespace Server.Scripting
{
    public readonly struct NpcShopGoodDefinition
    {
        public string ItemName { get; }
        public ushort Count { get; }

        public NpcShopGoodDefinition(string itemName, ushort count = 1)
        {
            if (string.IsNullOrWhiteSpace(itemName))
                throw new ArgumentException("ItemName 不能为空。", nameof(itemName));

            ItemName = itemName;
            Count = count <= 0 ? (ushort)1 : count;
        }

        public override string ToString() => $"{ItemName} x{Count}";
    }
}

