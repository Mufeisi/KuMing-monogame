using System.Text;

namespace Shared.CustomGui;

public static class CustomGuiProtocolLimits
{
    public const byte Version = 1;
    public const int MaximumOpenPayloadBytes = 60 * 1024;
    public const int MaximumDeltaPayloadBytes = 60 * 1024;
    public const int MaximumActionPayloadBytes = 16 * 1024;
    public const int MaximumResultPayloadBytes = 8 * 1024;
    public const int MaximumIdentifierCharacters = 128;
    public const int MaximumBindingKeyCharacters = 128;
    public const int MaximumActionIdCharacters = 128;
    public const int MaximumInputCharacters = 2048;
    public const int MaximumMessageCharacters = 512;
    public const int MaximumStateTextCharacters = 2048;
    public const int MaximumListTextCharacters = 256;
    public const int MaximumStateEntryCount = 128;
    public const int MaximumListItemsPerBinding = 64;
    public const int MaximumTotalListItems = 256;
    public const int MaximumItemSlotsPerBinding = 32;
    public const int MaximumTotalItemSlots = 64;
    public const int MaximumSelectionCount = 64;
    public const int MaximumSubmittedItemCount = 32;
}

public enum CustomGuiActionKind : byte
{
    CloseWindow = 0,
    OpenWindow = 1,
    SwitchPage = 2,
    SubmitText = 3,
    SubmitSelection = 4,
    SubmitItems = 5,
    RequestAction = 6,
}

public enum CustomGuiStateKind : byte
{
    Text = 0,
    Boolean = 1,
    Integer = 2,
    Progress = 3,
    List = 4,
    ItemSlots = 5,
    ButtonVisible = 6,
    ButtonEnabled = 7,
}

public enum CustomGuiActionResultKind : byte
{
    Accepted = 0,
    Rejected = 1,
    Expired = 2,
    Stale = 3,
    Invalid = 4,
}

public enum CustomGuiCloseReason : byte
{
    Requested = 0,
    Expired = 1,
    Replaced = 2,
    VersionChanged = 3,
    ServerShutdown = 4,
    Invalidated = 5,
}

public sealed class CustomGuiStateListItem
{
    public CustomGuiStateListItem() { }
    public CustomGuiStateListItem(string id, string primaryText, string secondaryText, string assetId)
    {
        Id = id;
        PrimaryText = primaryText;
        SecondaryText = secondaryText;
        AssetId = assetId;
    }

    public string Id = string.Empty;
    public string PrimaryText = string.Empty;
    public string SecondaryText = string.Empty;
    public string AssetId = string.Empty;
}

public sealed class CustomGuiStateItemSlot
{
    public CustomGuiStateItemSlot() { }
    public CustomGuiStateItemSlot(string slotId, long itemId, string assetId, string displayName, uint quantity, bool enabled)
    {
        SlotId = slotId;
        ItemId = itemId;
        AssetId = assetId;
        DisplayName = displayName;
        Quantity = quantity;
        Enabled = enabled;
    }

    public string SlotId = string.Empty;
    public long ItemId;
    public string AssetId = string.Empty;
    public string DisplayName = string.Empty;
    public uint Quantity;
    public bool Enabled;
}

public sealed class CustomGuiStateEntry
{
    public string BindingKey = string.Empty;
    public CustomGuiStateKind Kind;
    public string TextValue = string.Empty;
    public bool BooleanValue;
    public long IntegerValue;
    public long CurrentValue;
    public long MaximumValue;
    public List<CustomGuiStateListItem> ListItems = new();
    public List<CustomGuiStateItemSlot> ItemSlots = new();

    public static CustomGuiStateEntry Text(string key, string value) => new() { BindingKey = key, Kind = CustomGuiStateKind.Text, TextValue = value };
    public static CustomGuiStateEntry Boolean(string key, bool value) => new() { BindingKey = key, Kind = CustomGuiStateKind.Boolean, BooleanValue = value };
    public static CustomGuiStateEntry Integer(string key, long value) => new() { BindingKey = key, Kind = CustomGuiStateKind.Integer, IntegerValue = value };
    public static CustomGuiStateEntry Progress(string key, long current, long maximum) => new() { BindingKey = key, Kind = CustomGuiStateKind.Progress, CurrentValue = current, MaximumValue = maximum };
    public static CustomGuiStateEntry List(string key, List<CustomGuiStateListItem> items) => new() { BindingKey = key, Kind = CustomGuiStateKind.List, ListItems = items ?? new() };
    public static CustomGuiStateEntry ForItemSlots(string key, List<CustomGuiStateItemSlot> items) => new() { BindingKey = key, Kind = CustomGuiStateKind.ItemSlots, ItemSlots = items ?? new() };
    public static CustomGuiStateEntry ButtonVisible(string key, bool value) => new() { BindingKey = key, Kind = CustomGuiStateKind.ButtonVisible, BooleanValue = value };
    public static CustomGuiStateEntry ButtonEnabled(string key, bool value) => new() { BindingKey = key, Kind = CustomGuiStateKind.ButtonEnabled, BooleanValue = value };
}

internal static class CustomGuiProtocolCodec
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    internal static void BeginWrite(BinaryWriter writer) => writer.Write(CustomGuiProtocolLimits.Version);

    internal static void BeginRead(BinaryReader reader, int maximumPayloadBytes)
    {
        if (reader.BaseStream.Length > maximumPayloadBytes)
            throw Limit("协议载荷超过上限");
        byte version = reader.ReadByte();
        if (version != CustomGuiProtocolLimits.Version)
            throw Protocol("不支持的动态 GUI 协议版本");
    }

    internal static void EndWrite(BinaryWriter writer, int maximumPayloadBytes)
    {
        if (writer.BaseStream.Position > maximumPayloadBytes)
            throw Limit("协议载荷超过上限");
    }

    internal static void EndRead(BinaryReader reader)
    {
        if (reader.BaseStream.Position != reader.BaseStream.Length)
            throw Frame("协议载荷包含未消费数据");
    }

    internal static void WriteSessionIdentity(BinaryWriter writer, ulong instanceId, string documentId, uint documentRevision, long packageSequence, Guid nonce)
    {
        if (instanceId == 0 || documentRevision == 0 || packageSequence <= 0 || nonce == Guid.Empty)
            throw Protocol("窗口身份字段无效");
        writer.Write(instanceId);
        WriteString(writer, documentId, CustomGuiProtocolLimits.MaximumIdentifierCharacters, "GUI 文档标识");
        writer.Write(documentRevision);
        writer.Write(packageSequence);
        writer.Write(nonce.ToByteArray());
    }

    internal static void ReadSessionIdentity(BinaryReader reader, out ulong instanceId, out string documentId, out uint documentRevision, out long packageSequence, out Guid nonce)
    {
        instanceId = reader.ReadUInt64();
        documentId = ReadString(reader, CustomGuiProtocolLimits.MaximumIdentifierCharacters, "GUI 文档标识");
        documentRevision = reader.ReadUInt32();
        packageSequence = reader.ReadInt64();
        nonce = new Guid(ReadExact(reader, 16));
        if (instanceId == 0 || documentRevision == 0 || packageSequence <= 0 || nonce == Guid.Empty)
            throw Protocol("窗口身份字段无效");
    }

    internal static void WriteState(BinaryWriter writer, List<CustomGuiStateEntry> state)
    {
        state ??= new();
        WriteCount(writer, state.Count, CustomGuiProtocolLimits.MaximumStateEntryCount, "状态绑定");
        int totalListItems = 0;
        int totalSlots = 0;
        var bindingKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (CustomGuiStateEntry entry in state)
        {
            if (entry == null) throw Protocol("状态绑定不能为空");
            if (!bindingKeys.Add(entry.BindingKey ?? string.Empty)) throw Protocol("状态绑定键重复");
            WriteString(writer, entry.BindingKey, CustomGuiProtocolLimits.MaximumBindingKeyCharacters, "状态绑定键");
            EnsureEnum(entry.Kind, "状态类型");
            writer.Write((byte)entry.Kind);
            switch (entry.Kind)
            {
                case CustomGuiStateKind.Text:
                    WriteString(writer, entry.TextValue, CustomGuiProtocolLimits.MaximumStateTextCharacters, "状态文本");
                    break;
                case CustomGuiStateKind.Boolean:
                case CustomGuiStateKind.ButtonVisible:
                case CustomGuiStateKind.ButtonEnabled:
                    WriteBoolean(writer, entry.BooleanValue);
                    break;
                case CustomGuiStateKind.Integer:
                    writer.Write(entry.IntegerValue);
                    break;
                case CustomGuiStateKind.Progress:
                    if (entry.MaximumValue <= 0 || entry.CurrentValue < 0 || entry.CurrentValue > entry.MaximumValue)
                        throw Protocol("进度状态范围无效");
                    writer.Write(entry.CurrentValue);
                    writer.Write(entry.MaximumValue);
                    break;
                case CustomGuiStateKind.List:
                    entry.ListItems ??= new();
                    totalListItems = checked(totalListItems + entry.ListItems.Count);
                    if (totalListItems > CustomGuiProtocolLimits.MaximumTotalListItems) throw Limit("列表状态总项数超过上限");
                    WriteCount(writer, entry.ListItems.Count, CustomGuiProtocolLimits.MaximumListItemsPerBinding, "列表状态");
                    var listIds = new HashSet<string>(StringComparer.Ordinal);
                    foreach (CustomGuiStateListItem item in entry.ListItems)
                    {
                        if (item == null) throw Protocol("列表状态项不能为空");
                        if (!listIds.Add(item.Id ?? string.Empty)) throw Protocol("列表状态项标识重复");
                        WriteString(writer, item.Id, CustomGuiProtocolLimits.MaximumIdentifierCharacters, "列表项标识");
                        WriteString(writer, item.PrimaryText, CustomGuiProtocolLimits.MaximumListTextCharacters, "列表主文本");
                        WriteString(writer, item.SecondaryText, CustomGuiProtocolLimits.MaximumListTextCharacters, "列表副文本", allowEmpty: true);
                        WriteString(writer, item.AssetId, CustomGuiProtocolLimits.MaximumIdentifierCharacters, "列表资源标识", allowEmpty: true);
                    }
                    break;
                case CustomGuiStateKind.ItemSlots:
                    entry.ItemSlots ??= new();
                    totalSlots = checked(totalSlots + entry.ItemSlots.Count);
                    if (totalSlots > CustomGuiProtocolLimits.MaximumTotalItemSlots) throw Limit("物品槽状态总项数超过上限");
                    WriteCount(writer, entry.ItemSlots.Count, CustomGuiProtocolLimits.MaximumItemSlotsPerBinding, "物品槽状态");
                    var slotIds = new HashSet<string>(StringComparer.Ordinal);
                    foreach (CustomGuiStateItemSlot item in entry.ItemSlots)
                    {
                        if (item == null || item.ItemId <= 0 || item.Quantity == 0) throw Protocol("物品槽状态无效");
                        if (!slotIds.Add(item.SlotId ?? string.Empty)) throw Protocol("物品槽状态标识重复");
                        WriteString(writer, item.SlotId, CustomGuiProtocolLimits.MaximumIdentifierCharacters, "物品槽标识");
                        writer.Write(item.ItemId);
                        WriteString(writer, item.AssetId, CustomGuiProtocolLimits.MaximumIdentifierCharacters, "物品资源标识");
                        WriteString(writer, item.DisplayName, CustomGuiProtocolLimits.MaximumListTextCharacters, "物品显示名");
                        writer.Write(item.Quantity);
                        WriteBoolean(writer, item.Enabled);
                    }
                    break;
                default:
                    throw EnumError("状态类型无效");
            }
        }
    }

    internal static List<CustomGuiStateEntry> ReadState(BinaryReader reader)
    {
        int count = ReadCount(reader, CustomGuiProtocolLimits.MaximumStateEntryCount, "状态绑定");
        var state = new List<CustomGuiStateEntry>(count);
        int totalListItems = 0;
        int totalSlots = 0;
        var bindingKeys = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < count; i++)
        {
            var entry = new CustomGuiStateEntry
            {
                BindingKey = ReadString(reader, CustomGuiProtocolLimits.MaximumBindingKeyCharacters, "状态绑定键"),
                Kind = ReadEnum<CustomGuiStateKind>(reader, "状态类型"),
            };
            if (!bindingKeys.Add(entry.BindingKey)) throw Protocol("状态绑定键重复");
            switch (entry.Kind)
            {
                case CustomGuiStateKind.Text:
                    entry.TextValue = ReadString(reader, CustomGuiProtocolLimits.MaximumStateTextCharacters, "状态文本", allowEmpty: true);
                    break;
                case CustomGuiStateKind.Boolean:
                case CustomGuiStateKind.ButtonVisible:
                case CustomGuiStateKind.ButtonEnabled:
                    entry.BooleanValue = ReadBoolean(reader);
                    break;
                case CustomGuiStateKind.Integer:
                    entry.IntegerValue = reader.ReadInt64();
                    break;
                case CustomGuiStateKind.Progress:
                    entry.CurrentValue = reader.ReadInt64();
                    entry.MaximumValue = reader.ReadInt64();
                    if (entry.MaximumValue <= 0 || entry.CurrentValue < 0 || entry.CurrentValue > entry.MaximumValue)
                        throw Protocol("进度状态范围无效");
                    break;
                case CustomGuiStateKind.List:
                    int listCount = ReadCount(reader, CustomGuiProtocolLimits.MaximumListItemsPerBinding, "列表状态");
                    totalListItems = checked(totalListItems + listCount);
                    if (totalListItems > CustomGuiProtocolLimits.MaximumTotalListItems) throw Limit("列表状态总项数超过上限");
                    var listIds = new HashSet<string>(StringComparer.Ordinal);
                    for (int j = 0; j < listCount; j++)
                    {
                        string id = ReadString(reader, CustomGuiProtocolLimits.MaximumIdentifierCharacters, "列表项标识");
                        if (!listIds.Add(id)) throw Protocol("列表状态项标识重复");
                        entry.ListItems.Add(new CustomGuiStateListItem(
                            id,
                            ReadString(reader, CustomGuiProtocolLimits.MaximumListTextCharacters, "列表主文本"),
                            ReadString(reader, CustomGuiProtocolLimits.MaximumListTextCharacters, "列表副文本", allowEmpty: true),
                            ReadString(reader, CustomGuiProtocolLimits.MaximumIdentifierCharacters, "列表资源标识", allowEmpty: true)));
                    }
                    break;
                case CustomGuiStateKind.ItemSlots:
                    int slotCount = ReadCount(reader, CustomGuiProtocolLimits.MaximumItemSlotsPerBinding, "物品槽状态");
                    totalSlots = checked(totalSlots + slotCount);
                    if (totalSlots > CustomGuiProtocolLimits.MaximumTotalItemSlots) throw Limit("物品槽状态总项数超过上限");
                    var slotIds = new HashSet<string>(StringComparer.Ordinal);
                    for (int j = 0; j < slotCount; j++)
                    {
                        string slotId = ReadString(reader, CustomGuiProtocolLimits.MaximumIdentifierCharacters, "物品槽标识");
                        if (!slotIds.Add(slotId)) throw Protocol("物品槽状态标识重复");
                        long itemId = reader.ReadInt64();
                        string assetId = ReadString(reader, CustomGuiProtocolLimits.MaximumIdentifierCharacters, "物品资源标识");
                        string name = ReadString(reader, CustomGuiProtocolLimits.MaximumListTextCharacters, "物品显示名");
                        uint quantity = reader.ReadUInt32();
                        bool enabled = ReadBoolean(reader);
                        if (itemId <= 0 || quantity == 0) throw Protocol("物品槽状态无效");
                        entry.ItemSlots.Add(new CustomGuiStateItemSlot(slotId, itemId, assetId, name, quantity, enabled));
                    }
                    break;
                default:
                    throw EnumError("状态类型无效");
            }
            state.Add(entry);
        }
        return state;
    }

    internal static void WriteString(BinaryWriter writer, string value, int maximumCharacters, string fieldName, bool allowEmpty = false)
    {
        value ??= string.Empty;
        if ((!allowEmpty && string.IsNullOrWhiteSpace(value)) || value.Length > maximumCharacters)
            throw Limit(fieldName + "为空或超过上限");
        byte[] bytes;
        try { bytes = StrictUtf8.GetBytes(value); }
        catch (EncoderFallbackException error) { throw Protocol(fieldName + "不是有效 UTF-8", error); }
        int maximumBytes = checked(maximumCharacters * 4);
        if (bytes.Length > maximumBytes || bytes.Length > ushort.MaxValue) throw Limit(fieldName + "编码后超过上限");
        writer.Write((ushort)bytes.Length);
        writer.Write(bytes);
    }

    internal static string ReadString(BinaryReader reader, int maximumCharacters, string fieldName, bool allowEmpty = false)
    {
        int byteCount = reader.ReadUInt16();
        if (byteCount > checked(maximumCharacters * 4)) throw Limit(fieldName + "编码后超过上限");
        string value;
        try { value = StrictUtf8.GetString(ReadExact(reader, byteCount)); }
        catch (DecoderFallbackException error) { throw Protocol(fieldName + "不是有效 UTF-8", error); }
        if ((!allowEmpty && string.IsNullOrWhiteSpace(value)) || value.Length > maximumCharacters)
            throw Limit(fieldName + "为空或超过上限");
        return value;
    }

    internal static void WriteStringList(BinaryWriter writer, List<string> values, int maximumCount, int maximumCharacters, string fieldName)
    {
        values ??= new();
        WriteCount(writer, values.Count, maximumCount, fieldName);
        var unique = new HashSet<string>(StringComparer.Ordinal);
        foreach (string value in values)
        {
            if (!unique.Add(value ?? string.Empty)) throw Protocol(fieldName + "包含重复项");
            WriteString(writer, value, maximumCharacters, fieldName + "项");
        }
    }

    internal static List<string> ReadStringList(BinaryReader reader, int maximumCount, int maximumCharacters, string fieldName)
    {
        int count = ReadCount(reader, maximumCount, fieldName);
        var values = new List<string>(count);
        var unique = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < count; i++)
        {
            string value = ReadString(reader, maximumCharacters, fieldName + "项");
            if (!unique.Add(value)) throw Protocol(fieldName + "包含重复项");
            values.Add(value);
        }
        return values;
    }

    internal static void WriteInt64List(BinaryWriter writer, List<long> values, int maximumCount, string fieldName)
    {
        values ??= new();
        WriteCount(writer, values.Count, maximumCount, fieldName);
        var unique = new HashSet<long>();
        foreach (long value in values)
        {
            if (value <= 0 || !unique.Add(value)) throw Protocol(fieldName + "包含无效或重复标识");
            writer.Write(value);
        }
    }

    internal static List<long> ReadInt64List(BinaryReader reader, int maximumCount, string fieldName)
    {
        int count = ReadCount(reader, maximumCount, fieldName);
        var values = new List<long>(count);
        var unique = new HashSet<long>();
        for (int i = 0; i < count; i++)
        {
            long value = reader.ReadInt64();
            if (value <= 0 || !unique.Add(value)) throw Protocol(fieldName + "包含无效或重复标识");
            values.Add(value);
        }
        return values;
    }

    internal static void EnsureEnum<T>(T value, string fieldName) where T : struct, Enum
    {
        if (!Enum.IsDefined(value)) throw EnumError(fieldName + "无效");
    }

    internal static void ValidateActionPayload(CustomGuiActionKind action, string text, List<string> selections, List<long> items)
    {
        text ??= string.Empty;
        selections ??= new();
        items ??= new();
        bool valid = action switch
        {
            CustomGuiActionKind.SubmitText => selections.Count == 0 && items.Count == 0,
            CustomGuiActionKind.SubmitSelection => text.Length == 0 && selections.Count > 0 && items.Count == 0,
            CustomGuiActionKind.SubmitItems => text.Length == 0 && selections.Count == 0 && items.Count > 0,
            _ => text.Length == 0 && selections.Count == 0 && items.Count == 0,
        };
        if (!valid) throw Protocol("动作类型与提交载荷不匹配");
    }

    private static void WriteBoolean(BinaryWriter writer, bool value) => writer.Write(value ? (byte)1 : (byte)0);

    private static bool ReadBoolean(BinaryReader reader)
    {
        byte value = reader.ReadByte();
        if (value > 1) throw Protocol("布尔状态无效");
        return value == 1;
    }

    internal static T ReadEnum<T>(BinaryReader reader, string fieldName) where T : struct, Enum
    {
        T value = (T)Enum.ToObject(typeof(T), reader.ReadByte());
        EnsureEnum(value, fieldName);
        return value;
    }

    private static void WriteCount(BinaryWriter writer, int count, int maximum, string fieldName)
    {
        if (count < 0 || count > maximum) throw Limit(fieldName + "数量超过上限");
        writer.Write((ushort)count);
    }

    private static int ReadCount(BinaryReader reader, int maximum, string fieldName)
    {
        int count = reader.ReadUInt16();
        if (count > maximum) throw Limit(fieldName + "数量超过上限");
        return count;
    }

    private static byte[] ReadExact(BinaryReader reader, int count)
    {
        byte[] value = reader.ReadBytes(count);
        if (value.Length != count) throw Frame("协议载荷被截断");
        return value;
    }

    private static InvalidDataException Protocol(string message, Exception inner = null) => new("GUI07-PROTOCOL-001：" + message, inner);
    private static InvalidDataException Limit(string message) => new("GUI07-LIMIT-001：" + message);
    private static InvalidDataException EnumError(string message) => new("GUI07-ENUM-001：" + message);
    private static InvalidDataException Frame(string message) => new("GUI07-FRAME-001：" + message);
}
