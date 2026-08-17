using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

public class ItemInfo
{
    public int Index;
    public string Name = string.Empty;
    public ItemType Type;
    public ItemGrade Grade;
    public RequiredType RequiredType = RequiredType.Level;
    public RequiredClass RequiredClass = RequiredClass.全职业;
    public RequiredGender RequiredGender = RequiredGender.性别不限;
    public ItemSet Set;

    public short Shape;
    public byte Weight, Light, RequiredAmount;

    public ushort Image, Durability;

    public uint Price; 
    public ushort StackSize = 1;

    public bool StartItem;
    public byte Effect;

    public bool NeedIdentify, ShowGroupPickup, GlobalDropNotify;
    public bool ClassBased;
    public bool LevelBased;
    public bool CanMine;
    public bool CanFastRun;
    public bool CanAwakening;

    public BindMode Bind = BindMode.None;

    public SpecialItemMode Unique = SpecialItemMode.None;
    public byte RandomStatsId;
    public RandomItemStat RandomStats;
    public string ToolTip = string.Empty;

    public byte Slots;

    public Stats Stats;

    public bool IsConsumable
    {
        get { return Type == ItemType.Potion || Type == ItemType.Scroll || Type == ItemType.Food || Type == ItemType.Transform || Type == ItemType.Script; }
    }
    public bool IsFishingRod
    {
        get { return Globals.FishingRodShapes.Contains(Shape); }
    }

    public string FriendlyName
    {
        get
        {
            string temp = Name;
            temp = Regex.Replace(temp, @"\d+$", string.Empty); //hides end numbers
            temp = Regex.Replace(temp, @"\[[^]]*\]", string.Empty); //hides square brackets

            return temp;
        }
    }

    public ItemInfo() 
    {
        Stats = new Stats();
    }

    public ItemInfo(BinaryReader reader, int version = int.MaxValue, int customVersion = int.MaxValue)
    {
        Index = reader.ReadInt32();
        Name = reader.ReadString();
        Type = (ItemType)reader.ReadByte();
        Grade = (ItemGrade)reader.ReadByte();
        RequiredType = (RequiredType)reader.ReadByte();
        RequiredClass = (RequiredClass)reader.ReadByte();
        RequiredGender = (RequiredGender)reader.ReadByte();
        Set = (ItemSet)reader.ReadByte();

        Shape = reader.ReadInt16();
        Weight = reader.ReadByte();
        Light = reader.ReadByte();
        RequiredAmount = reader.ReadByte();

        Image = reader.ReadUInt16();
        Durability = reader.ReadUInt16();

        if (version <= 84)
        {
            StackSize = (ushort)reader.ReadUInt32();
        }
        else
        {
            StackSize = reader.ReadUInt16();
        }

        Price = reader.ReadUInt32();

        if (version <= 84)
        {
            Stats = new Stats();
            Stats[Stat.MinAC] = reader.ReadByte();
            Stats[Stat.MaxAC] = reader.ReadByte();
            Stats[Stat.MinMAC] = reader.ReadByte();
            Stats[Stat.MaxMAC] = reader.ReadByte();
            Stats[Stat.MinDC] = reader.ReadByte();
            Stats[Stat.MaxDC] = reader.ReadByte();
            Stats[Stat.MinMC] = reader.ReadByte();
            Stats[Stat.MaxMC] = reader.ReadByte();
            Stats[Stat.MinSC] = reader.ReadByte();
            Stats[Stat.MaxSC] = reader.ReadByte();
            Stats[Stat.HP] = reader.ReadUInt16();
            Stats[Stat.MP] = reader.ReadUInt16();
            Stats[Stat.Accuracy] = reader.ReadByte();
            Stats[Stat.Agility] = reader.ReadByte();

            Stats[Stat.Luck] = reader.ReadSByte();
            Stats[Stat.AttackSpeed] = reader.ReadSByte();
        }

        StartItem = reader.ReadBoolean();

        if (version <= 84)
        {
            Stats[Stat.BagWeight] = reader.ReadByte();
            Stats[Stat.HandWeight] = reader.ReadByte();
            Stats[Stat.WearWeight] = reader.ReadByte();
        }

        Effect = reader.ReadByte();

        if (version <= 84)
        {
            Stats[Stat.Strong] = reader.ReadByte();
            Stats[Stat.MagicResist] = reader.ReadByte();
            Stats[Stat.PoisonResist] = reader.ReadByte();
            Stats[Stat.HealthRecovery] = reader.ReadByte();
            Stats[Stat.SpellRecovery] = reader.ReadByte();
            Stats[Stat.PoisonRecovery] = reader.ReadByte();
            Stats[Stat.HPRatePercent] = reader.ReadByte();
            Stats[Stat.MPRatePercent] = reader.ReadByte();
            Stats[Stat.CriticalRate] = reader.ReadByte();
            Stats[Stat.CriticalDamage] = reader.ReadByte();
        }


        byte bools = reader.ReadByte();
        NeedIdentify = (bools & 0x01) == 0x01;
        ShowGroupPickup = (bools & 0x02) == 0x02;
        ClassBased = (bools & 0x04) == 0x04;
        LevelBased = (bools & 0x08) == 0x08;
        CanMine = (bools & 0x10) == 0x10;

        if (version >= 77)
        {
            GlobalDropNotify = (bools & 0x20) == 0x20;
        }

        if (version <= 84)
        {
            Stats[Stat.MaxACRatePercent] = reader.ReadByte();
            Stats[Stat.MaxMACRatePercent] = reader.ReadByte();
            Stats[Stat.Holy] = reader.ReadByte();
            Stats[Stat.Freezing] = reader.ReadByte();
            Stats[Stat.PoisonAttack] = reader.ReadByte();
        }

        Bind = (BindMode)reader.ReadInt16();

        if (version <= 84)
        {
            Stats[Stat.Reflect] = reader.ReadByte();
            Stats[Stat.HPDrainRatePercent] = reader.ReadByte();
        }

        Unique = (SpecialItemMode)reader.ReadInt16();
        RandomStatsId = reader.ReadByte();

        CanFastRun = reader.ReadBoolean();

        CanAwakening = reader.ReadBoolean();

        if (version > 83)
        {
            Slots = reader.ReadByte();
        }

        if (version > 84)
        {
            Stats = new Stats(reader, version, customVersion);
        }

        bool isTooltip = reader.ReadBoolean();
        if (isTooltip)
            ToolTip = reader.ReadString();

        if (version < 70) //before db version 70 all specialitems had wedding rings disabled, after that it became a server option
        {
            if ((Type == ItemType.Ring) && (Unique != SpecialItemMode.None))
                Bind |= BindMode.NoWeddingRing;
        }
    }



    public void Save(BinaryWriter writer)
    {
        writer.Write(Index);
        writer.Write(Name);
        writer.Write((byte)Type);
        writer.Write((byte)Grade);
        writer.Write((byte)RequiredType);
        writer.Write((byte)RequiredClass);
        writer.Write((byte)RequiredGender);
        writer.Write((byte)Set);

        writer.Write(Shape);
        writer.Write(Weight);
        writer.Write(Light);
        writer.Write(RequiredAmount);

        writer.Write(Image);
        writer.Write(Durability);

        writer.Write(StackSize);
        writer.Write(Price);

        writer.Write(StartItem);

        writer.Write(Effect);

        byte bools = 0;
        if (NeedIdentify) bools |= 0x01;
        if (ShowGroupPickup) bools |= 0x02;
        if (ClassBased) bools |= 0x04;
        if (LevelBased) bools |= 0x08;
        if (CanMine) bools |= 0x10;
        if (GlobalDropNotify) bools |= 0x20;
        writer.Write(bools);
        
        writer.Write((short)Bind);        
        writer.Write((short)Unique);

        writer.Write(RandomStatsId);

        writer.Write(CanFastRun);
        writer.Write(CanAwakening);
        writer.Write(Slots);

        Stats.Save(writer);

        writer.Write(ToolTip != null);
        if (ToolTip != null)
            writer.Write(ToolTip);

    }

    public static ItemInfo FromText(string text)
    {
        return null;
    }

    public string ToText()
    {
        return null;
    }

    public override string ToString()
    {
        return string.Format("{0}: {1}", Index, Name);
    }

}

public sealed class LingFengCustomItemAttribute
{
    public byte Colour;
    public byte Binding;
    public byte DisplayOrder;
    public byte Mode;
    public byte Module;
    public int Value1;
    public int Value2;
    public int Value3;

    public bool IsDefined => Colour != 0 || Binding != 0 || DisplayOrder != 0 ||
                             Mode != 0 || Module != 0 || Value1 != 0 || Value2 != 0 || Value3 != 0;

    public LingFengCustomItemAttribute Clone() => new()
    {
        Colour = Colour,
        Binding = Binding,
        DisplayOrder = DisplayOrder,
        Mode = Mode,
        Module = Module,
        Value1 = Value1,
        Value2 = Value2,
        Value3 = Value3
    };
}

public sealed class LingFengCustomItemProgressBar
{
    public bool Enabled { get; set; }
    public string Text { get; set; } = string.Empty;
    public byte Colour { get; set; }
    public byte FrameCount { get; set; }
    public byte DisplayMode { get; set; }
    public int Maximum { get; set; }
    public int Current { get; set; }

    public bool IsDefined => Enabled || Text.Length > 0 || Colour != 0 || FrameCount != 0 ||
                             DisplayMode != 0 || Maximum != 0 || Current != 0;

    public LingFengCustomItemProgressBar Clone() => new()
    {
        Enabled = Enabled,
        Text = Text,
        Colour = Colour,
        FrameCount = FrameCount,
        DisplayMode = DisplayMode,
        Maximum = Maximum,
        Current = Current
    };
}

public class UserItem
{
    public const int LingFengCustomAttributeLimit = 60;
    public const int LingFengCustomProgressBarLimit = 10;
    public const int LingFengNewItemValueLimit = 26;
    public ulong UniqueID;
    public int ItemIndex;

    public ItemInfo Info;
    public ushort CurrentDura, MaxDura;
    public ushort Count = 1,
                GemCount = 0;

    public RefinedValue RefinedValue = RefinedValue.None;
    public byte RefineAdded = 0;
    public int RefineSuccessChance = 0;

    public bool DuraChanged;
    public int SoulBoundId = -1;
    public bool Identified = false;
    public bool Cursed = false;

    public int WeddingRing = -1;

    public UserItem[] Slots = new UserItem[0];

    public DateTime BuybackExpiryDate;

    public ExpireInfo ExpireInfo;
    public RentalInformation RentalInformation;
    public SealedInfo SealedInfo;

    public bool IsShopItem;

    public Awake Awake = new Awake();

    public Stats AddedStats;
    private LingFengCustomItemAttribute[] _lingFengCustomAttributes =
        CreateLingFengCustomAttributes();
    private byte[] _lingFengByteMarks = new byte[20];
    private int[] _lingFengIntMarks = new int[10];
    private string[] _lingFengTextMarks = { string.Empty, string.Empty };
    private LingFengCustomItemProgressBar[] _lingFengCustomProgressBars =
        CreateLingFengCustomProgressBars();
    private string _lingFengCustomText = string.Empty;
    private byte _lingFengCustomTextColour;
    private ushort[] _lingFengItemEffects = new ushort[3];
    private int[] _lingFengNewItemValues = new int[LingFengNewItemValueLimit];
    public byte LingFengNameColour { get; private set; }
    public ushort? LingFengLooks { get; private set; }
    public short? LingFengShape { get; private set; }
    public BindMode LingFengBindingFlags { get; private set; }
    public byte LingFengUpgradeCount { get; private set; }
    public bool LingFengCannotTakeOff { get; private set; }

    public BindMode EffectiveBindingFlags =>
        (Info?.Bind ?? BindMode.None) | LingFengBindingFlags |
        (RentalInformation?.BindingFlags ?? BindMode.None);

    public bool HasBindingFlag(BindMode flag) => EffectiveBindingFlags.HasFlag(flag);

    public bool CanStackWith(UserItem other) =>
        other != null && ReferenceEquals(Info, other.Info) &&
        EffectiveBindingFlags == other.EffectiveBindingFlags &&
        LingFengCannotTakeOff == other.LingFengCannotTakeOff &&
        SoulBoundId == other.SoulBoundId &&
        CurrentDura == other.CurrentDura &&
        MaxDura == other.MaxDura &&
        !HasStackSensitiveInstanceState() &&
        !other.HasStackSensitiveInstanceState();

    private bool HasStackSensitiveInstanceState() =>
        GemCount != 0 || RefinedValue != RefinedValue.None || RefineAdded != 0 ||
        RefineSuccessChance != 0 || Identified || Cursed || WeddingRing != -1 ||
        Slots.Any(item => item != null) || AddedStats.Count != 0 ||
        Awake.Type != AwakeType.None || Awake.GetAwakeLevel() != 0 ||
        BuybackExpiryDate != default || ExpireInfo != null || RentalInformation != null ||
        SealedInfo != null || IsShopItem || GMMade ||
        _lingFengCustomAttributes.Any(attribute => attribute.IsDefined) ||
        _lingFengByteMarks.Any(value => value != 0) ||
        _lingFengIntMarks.Any(value => value != 0) ||
        _lingFengTextMarks.Any(value => !string.IsNullOrEmpty(value)) ||
        _lingFengCustomProgressBars.Any(progress => progress.IsDefined) ||
        !string.IsNullOrEmpty(_lingFengCustomText) || _lingFengCustomTextColour != 0 ||
        _lingFengItemEffects.Any(value => value != 0) ||
        _lingFengNewItemValues.Any(value => value != 0) ||
        LingFengNameColour != 0 || LingFengLooks.HasValue || LingFengShape.HasValue ||
        LingFengUpgradeCount != 0;

    public bool TrySetLingFengBindingFlags(BindMode flags)
    {
        const BindMode supported = BindMode.DontDrop | BindMode.DontTrade |
                                   BindMode.DontStore | BindMode.DontRepair |
                                   BindMode.DontSell | BindMode.DontDeathdrop |
                                   BindMode.DestroyOnDrop;
        if ((flags & ~supported) != 0) return false;
        LingFengBindingFlags = flags;
        return true;
    }

    public bool TrySetLingFengItemState(int stateIndex, bool enabled)
    {
        if (stateIndex == 7)
        {
            LingFengCannotTakeOff = enabled;
            return true;
        }

        BindMode flag = stateIndex switch
        {
            0 => BindMode.DontDrop,
            1 => BindMode.DontTrade,
            2 => BindMode.DontStore,
            3 => BindMode.DontRepair,
            4 => BindMode.DontSell,
            5 => BindMode.DontDeathdrop,
            6 => BindMode.DestroyOnDrop,
            _ => BindMode.None
        };
        if (flag == BindMode.None) return false;
        return TrySetLingFengBindingFlags(enabled
            ? LingFengBindingFlags | flag
            : LingFengBindingFlags & ~flag);
    }

    public bool HasLingFengItemState(int stateIndex) => stateIndex switch
    {
        0 => HasBindingFlag(BindMode.DontDrop),
        1 => HasBindingFlag(BindMode.DontTrade),
        2 => HasBindingFlag(BindMode.DontStore),
        3 => HasBindingFlag(BindMode.DontRepair),
        4 => HasBindingFlag(BindMode.DontSell),
        5 => HasBindingFlag(BindMode.DontDeathdrop),
        6 => HasBindingFlag(BindMode.DestroyOnDrop),
        7 => LingFengCannotTakeOff,
        _ => false
    };

    public bool IsAdded
    {
        get { return AddedStats.Count > 0 || Slots.Length > Info.Slots; }
    }

    public int Weight
    {
        get { return (Info.Type == ItemType.护身符 || Info.Type == ItemType.鱼饵) ? Info.Weight : Info.Weight * Count; }
    }

    public string FriendlyName
    {
        get { return Count > 1 ? string.Format("{0} ({1})", Info.FriendlyName, Count) : Info.FriendlyName; }
    }

    public bool GMMade { get; set; }

    public UserItem(ItemInfo info)
    {
        SoulBoundId = -1;
        ItemIndex = info.Index;
        Info = info;
        AddedStats = new Stats();

        SetSlotSize();
    }
    public UserItem(BinaryReader reader, int version = int.MaxValue, int customVersion = int.MaxValue)
    {
        UniqueID = reader.ReadUInt64();
        ItemIndex = reader.ReadInt32();

        CurrentDura = reader.ReadUInt16();
        MaxDura = reader.ReadUInt16();

        if (version <= 84)
        {
            Count = (ushort)reader.ReadUInt32();
        }
        else
        {
            Count = reader.ReadUInt16();
        }

        if (version <= 84)
        {
            AddedStats = new Stats();

            AddedStats[Stat.MaxAC] = reader.ReadByte();
            AddedStats[Stat.MaxMAC] = reader.ReadByte();
            AddedStats[Stat.MaxDC] = reader.ReadByte();
            AddedStats[Stat.MaxMC] = reader.ReadByte();
            AddedStats[Stat.MaxSC] = reader.ReadByte();

            AddedStats[Stat.Accuracy] = reader.ReadByte();
            AddedStats[Stat.Agility] = reader.ReadByte();
            AddedStats[Stat.HP] = reader.ReadByte();
            AddedStats[Stat.MP] = reader.ReadByte();

            AddedStats[Stat.AttackSpeed] = reader.ReadSByte();
            AddedStats[Stat.Luck] = reader.ReadSByte();
        }

        SoulBoundId = reader.ReadInt32();
        byte Bools = reader.ReadByte();
        Identified = (Bools & 0x01) == 0x01;
        Cursed = (Bools & 0x02) == 0x02;

        if (version <= 84)
        {
            AddedStats[Stat.Strong] = reader.ReadByte();
            AddedStats[Stat.MagicResist] = reader.ReadByte();
            AddedStats[Stat.PoisonResist] = reader.ReadByte();
            AddedStats[Stat.HealthRecovery] = reader.ReadByte();
            AddedStats[Stat.SpellRecovery] = reader.ReadByte();
            AddedStats[Stat.PoisonRecovery] = reader.ReadByte();
            AddedStats[Stat.CriticalRate] = reader.ReadByte();
            AddedStats[Stat.CriticalDamage] = reader.ReadByte();
            AddedStats[Stat.Freezing] = reader.ReadByte();
            AddedStats[Stat.PoisonAttack] = reader.ReadByte();
        }

        int count = reader.ReadInt32();

        SetSlotSize(count);

        for (int i = 0; i < count; i++)
        {
            if (reader.ReadBoolean()) continue;
            UserItem item = new UserItem(reader, version, customVersion);
            Slots[i] = item;
        }

        if (version <= 84)
        {
            GemCount = (ushort)reader.ReadUInt32();
        }
        else
        {
            GemCount = reader.ReadUInt16();
        }

        if (version > 84)
        {
            AddedStats = new Stats(reader, version, customVersion);
        }

        Awake = new Awake(reader);

        RefinedValue = (RefinedValue)reader.ReadByte();
        RefineAdded = reader.ReadByte();

        if (version > 85)
        {
            RefineSuccessChance = reader.ReadInt32();
        }

        WeddingRing = reader.ReadInt32();

        if (version < 65) return;

        if (reader.ReadBoolean())
            ExpireInfo = new ExpireInfo(reader, version, customVersion);

        if (version < 76)
            return;

        if (reader.ReadBoolean())
            RentalInformation = new RentalInformation(reader, version, customVersion);

        if (version < 83) return;

        IsShopItem = reader.ReadBoolean();

        if (version < 92) return;

        if (reader.ReadBoolean())
        {
            SealedInfo = new SealedInfo(reader, version, customVersion);
        }

        if (version > 107)
        {
            GMMade = reader.ReadBoolean();
        }

        if (customVersion >= 3)
            ReadLingFengCustomAttributes(
                reader,
                customVersion >= 4,
                customVersion >= 4,
                customVersion >= 5,
                customVersion >= 5,
                customVersion >= 6,
                customVersion >= 6);
    }

    public void Save(BinaryWriter writer)
    {
        writer.Write(UniqueID);
        writer.Write(ItemIndex);

        writer.Write(CurrentDura);
        writer.Write(MaxDura);

        writer.Write(Count);
       
        writer.Write(SoulBoundId);
        byte Bools = 0;
        if (Identified) Bools |= 0x01;
        if (Cursed) Bools |= 0x02;
        writer.Write(Bools);

        writer.Write(Slots.Length);
        for (int i = 0; i < Slots.Length; i++)
        {
            writer.Write(Slots[i] == null);
            if (Slots[i] == null) continue;

            Slots[i].Save(writer);
        }

        writer.Write(GemCount);


        AddedStats.Save(writer);
        Awake.Save(writer);

        writer.Write((byte)RefinedValue);
        writer.Write(RefineAdded);
        writer.Write(RefineSuccessChance);

        writer.Write(WeddingRing);

        writer.Write(ExpireInfo != null);
        ExpireInfo?.Save(writer);

        writer.Write(RentalInformation != null);
        RentalInformation?.Save(writer);

        writer.Write(IsShopItem);

        writer.Write(SealedInfo != null);
        SealedInfo?.Save(writer);

        writer.Write(GMMade);

        WriteLingFengCustomAttributes(writer);
    }

    public LingFengCustomItemAttribute GetLingFengCustomAttribute(int index)
    {
        if (index < 0 || index >= LingFengCustomAttributeLimit)
            throw new ArgumentOutOfRangeException(nameof(index));
        return _lingFengCustomAttributes[index];
    }

    public bool TrySetLingFengNameColour(int colour)
    {
        if (colour is < byte.MinValue or > byte.MaxValue) return false;
        LingFengNameColour = (byte)colour;
        return true;
    }

    public bool TryChangeLingFengUpgradeCount(string operation, int value)
    {
        if (value is < 0 or > byte.MaxValue ||
            !TryCalculate(LingFengUpgradeCount, operation, value, out int result) ||
            result is < 0 or > byte.MaxValue)
            return false;
        LingFengUpgradeCount = (byte)result;
        return true;
    }

    public bool TryChangeLingFengLooks(string operation, int value)
    {
        int current = LingFengLooks ?? Info.Image;
        if (!TryCalculate(current, operation, value, out int result) ||
            result is < ushort.MinValue or > ushort.MaxValue)
            return false;
        LingFengLooks = (ushort)result;
        return true;
    }

    public bool TryChangeLingFengShape(string operation, int value)
    {
        int current = LingFengShape ?? Info.Shape;
        if (!TryCalculate(current, operation, value, out int result) ||
            result is < short.MinValue or > short.MaxValue)
            return false;
        LingFengShape = (short)result;
        return true;
    }

    public bool TrySetLingFengCustomAbility(int index, int field, int value)
    {
        if (index < 0 || index >= LingFengCustomAttributeLimit || field is < 0 or > 4)
            return false;
        LingFengCustomItemAttribute attribute = _lingFengCustomAttributes[index];
        switch (field)
        {
            case 0 when value is >= 0 and <= 255:
                attribute.Colour = (byte)value;
                return true;
            case 1 when value is >= 0 and <= 60:
                attribute.Binding = (byte)value;
                return true;
            case 2 when value is >= 0 and <= 255:
                attribute.DisplayOrder = (byte)value;
                return true;
            case 3 when value is >= 0 and <= 2:
                attribute.Mode = (byte)value;
                return true;
            case 4 when value is >= 0 and <= 14:
                attribute.Module = (byte)value;
                return true;
            default:
                return false;
        }
    }

    public bool TryChangeLingFengCustomValues(
        int index, string operation, int value1, int value2 = 0, int value3 = 0)
    {
        if (index < 0 || index >= LingFengCustomAttributeLimit ||
            operation is not ("+" or "-" or "="))
            return false;
        LingFengCustomItemAttribute attribute = _lingFengCustomAttributes[index];
        if (!TryCalculate(attribute.Value1, operation, value1, out int next1) ||
            !TryCalculate(attribute.Value2, operation, value2, out int next2) ||
            !TryCalculate(attribute.Value3, operation, value3, out int next3))
            return false;
        attribute.Value1 = next1;
        attribute.Value2 = next2;
        attribute.Value3 = next3;
        return true;
    }

    public IReadOnlyList<string> GetLingFengCustomAttributeDisplayLines()
    {
        var lines = new List<string>();
        if (!string.IsNullOrWhiteSpace(_lingFengCustomText))
            lines.Add(_lingFengCustomText);
        lines.AddRange(_lingFengCustomAttributes
            .Select((attribute, index) => (attribute, index))
            .Where(entry => entry.attribute.IsDefined && entry.attribute.Binding > 0)
            .OrderBy(entry => entry.attribute.DisplayOrder)
            .ThenBy(entry => entry.index)
            .Select(entry => FormatLingFengCustomAttribute(entry.attribute)));
        lines.AddRange(_lingFengCustomProgressBars
            .Where(progress => progress.Enabled)
            .Select(FormatLingFengCustomProgressBar));
        lines.AddRange(_lingFengNewItemValues
            .Select((value, index) => (value, index))
            .Where(entry => entry.value != 0)
            .Select(entry => $"翎风新增属性[{entry.index}]: {entry.value}"));
        return lines;
    }

    public bool TrySetLingFengCustomText(string text, int colour)
    {
        if (text == null || text.Length > 120 || colour is < 0 or > byte.MaxValue) return false;
        _lingFengCustomText = text;
        _lingFengCustomTextColour = (byte)colour;
        return true;
    }

    public bool TrySetLingFengCustomText(string text) =>
        TrySetLingFengCustomText(text, _lingFengCustomTextColour);

    public bool TrySetLingFengCustomTextColour(int colour) =>
        TrySetLingFengCustomText(_lingFengCustomText, colour);

    public bool TrySetLingFengItemEffect(int position, int effect)
    {
        if (position < 0 || position >= _lingFengItemEffects.Length ||
            effect is < 0 or > ushort.MaxValue)
            return false;
        _lingFengItemEffects[position] = (ushort)effect;
        return true;
    }

    public ushort GetLingFengItemEffect(int position) =>
        position >= 0 && position < _lingFengItemEffects.Length
            ? _lingFengItemEffects[position]
            : (ushort)0;

    public bool TryChangeLingFengNewItemValue(int type, string operation, int value)
    {
        if (type < 0 || type >= LingFengNewItemValueLimit || value < 0 ||
            operation is not ("+" or "-" or "="))
            return false;
        if (!TryCalculate(_lingFengNewItemValues[type], operation, value, out int next) ||
            next is < 0 or > 1000)
            return false;
        _lingFengNewItemValues[type] = next;
        return true;
    }

    public bool TryGetLingFengNewItemValue(int type, out int value)
    {
        value = 0;
        if (type < 0 || type >= LingFengNewItemValueLimit) return false;
        value = _lingFengNewItemValues[type];
        return true;
    }

    public bool TrySetLingFengCustomProgressBar(int index, int field, string value)
    {
        if (index < 0 || index >= LingFengCustomProgressBarLimit || field is < 0 or > 4)
            return false;
        LingFengCustomItemProgressBar progress = _lingFengCustomProgressBars[index];
        switch (field)
        {
            case 0 when int.TryParse(value, out int enabled) && enabled is 0 or 1:
                progress.Enabled = enabled == 1;
                return true;
            case 1 when value != null && value.Length <= 120:
                progress.Text = value;
                return true;
            case 2 when byte.TryParse(value, out byte colour):
                progress.Colour = colour;
                return true;
            case 3 when byte.TryParse(value, out byte frames):
                progress.FrameCount = frames;
                return true;
            case 4 when byte.TryParse(value, out byte mode) && mode <= 2:
                progress.DisplayMode = mode;
                return true;
            default:
                return false;
        }
    }

    public bool TryChangeLingFengCustomProgressBarValue(
        int index, int valueKind, string operation, int operand)
    {
        if (index < 0 || index >= LingFengCustomProgressBarLimit || valueKind is < 0 or > 2)
            return false;
        LingFengCustomItemProgressBar progress = _lingFengCustomProgressBars[index];
        int current = valueKind switch
        {
            0 => progress.Maximum,
            1 => progress.Current,
            _ => progress.Maximum <= 0 ? 0 : checked((int)((long)progress.Current * 100 / progress.Maximum))
        };
        if (!TryCalculate(current, operation, operand, out int changed) || changed < 0)
            return false;
        if (valueKind == 0)
        {
            progress.Maximum = changed;
            if (progress.Current > changed) progress.Current = changed;
        }
        else if (valueKind == 1)
        {
            progress.Current = progress.Maximum > 0 ? Math.Min(changed, progress.Maximum) : changed;
        }
        else
        {
            if (changed > 100 || progress.Maximum < 0) return false;
            progress.Current = checked((int)((long)progress.Maximum * changed / 100));
        }
        return true;
    }

    public bool TryGetLingFengCustomProgressBarValue(int index, int valueKind, out int value)
    {
        value = 0;
        if (index < 0 || index >= LingFengCustomProgressBarLimit || valueKind is < 0 or > 2)
            return false;
        LingFengCustomItemProgressBar progress = _lingFengCustomProgressBars[index];
        value = valueKind switch
        {
            0 => progress.Maximum,
            1 => progress.Current,
            _ => progress.Maximum <= 0 ? 0 : checked((int)((long)progress.Current * 100 / progress.Maximum))
        };
        return true;
    }

    public bool TrySetLingFengByteMark(int index, int value)
    {
        if (index < 0 || index >= _lingFengByteMarks.Length || value is < 0 or > byte.MaxValue)
            return false;
        _lingFengByteMarks[index] = (byte)value;
        return true;
    }

    public bool TryGetLingFengByteMark(int index, out byte value)
    {
        if (index < 0 || index >= _lingFengByteMarks.Length)
        {
            value = 0;
            return false;
        }
        value = _lingFengByteMarks[index];
        return true;
    }

    public bool TrySetLingFengIntMark(int index, int value)
    {
        if (index < 0 || index >= _lingFengIntMarks.Length) return false;
        _lingFengIntMarks[index] = value;
        return true;
    }

    public bool TryGetLingFengIntMark(int index, out int value)
    {
        if (index < 0 || index >= _lingFengIntMarks.Length)
        {
            value = 0;
            return false;
        }
        value = _lingFengIntMarks[index];
        return true;
    }

    public bool TrySetLingFengTextMark(int index, string value)
    {
        if (index < 0 || index >= _lingFengTextMarks.Length || value == null || value.Length > 20)
            return false;
        _lingFengTextMarks[index] = value;
        return true;
    }

    public bool TryGetLingFengTextMark(int index, out string value)
    {
        if (index < 0 || index >= _lingFengTextMarks.Length)
        {
            value = string.Empty;
            return false;
        }
        value = _lingFengTextMarks[index];
        return true;
    }

    public void ApplyLingFengCustomStats(Stats target, Stats itemStats)
    {
        if (target == null || itemStats == null) return;
        foreach (LingFengCustomItemAttribute attribute in _lingFengCustomAttributes)
        {
            if (attribute.Binding is < 1 or > 7 || attribute.Mode > 1) continue;
            foreach (Stat stat in GetLingFengBoundStats(attribute.Binding))
            {
                long addition = attribute.Mode == 0
                    ? attribute.Value1
                    : (long)itemStats[stat] * attribute.Value1 / 100;
                target[stat] = SaturatingAdd(target[stat], addition);
            }
        }
    }

    public void AccumulateLingFengWholeBodyPercentages(Stats percentages)
    {
        if (percentages == null) return;
        foreach (LingFengCustomItemAttribute attribute in _lingFengCustomAttributes)
        {
            if (attribute.Binding is < 1 or > 7 || attribute.Mode != 2) continue;
            foreach (Stat stat in GetLingFengBoundStats(attribute.Binding))
                percentages[stat] = SaturatingAdd(percentages[stat], attribute.Value1);
        }
    }

    public static void ApplyLingFengWholeBodyPercentages(Stats target, Stats percentages)
    {
        if (target == null || percentages == null) return;
        foreach (KeyValuePair<Stat, int> pair in percentages.Values)
            target[pair.Key] = SaturatingAdd(target[pair.Key],
                (long)target[pair.Key] * pair.Value / 100);
    }

    public string SerializeLingFengCustomAttributes()
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true))
            WriteLingFengCustomAttributes(writer);
        return Convert.ToBase64String(stream.ToArray());
    }

    public bool TryDeserializeLingFengCustomAttributes(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            _lingFengCustomAttributes = CreateLingFengCustomAttributes();
            _lingFengByteMarks = new byte[20];
            _lingFengIntMarks = new int[10];
            _lingFengTextMarks = new[] { string.Empty, string.Empty };
            _lingFengCustomProgressBars = CreateLingFengCustomProgressBars();
            _lingFengCustomText = string.Empty;
            _lingFengCustomTextColour = 0;
            _lingFengItemEffects = new ushort[3];
            _lingFengNewItemValues = new int[LingFengNewItemValueLimit];
            LingFengNameColour = 0;
            LingFengLooks = null;
            LingFengShape = null;
            LingFengBindingFlags = BindMode.None;
            LingFengUpgradeCount = 0;
            LingFengCannotTakeOff = false;
            return true;
        }
        try
        {
            byte[] data = Convert.FromBase64String(value);
            using var stream = new MemoryStream(data, false);
            using var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, true);
            LingFengCustomItemAttribute[] previous = _lingFengCustomAttributes;
            byte[] previousByteMarks = _lingFengByteMarks;
            int[] previousIntMarks = _lingFengIntMarks;
            string[] previousTextMarks = _lingFengTextMarks;
            LingFengCustomItemProgressBar[] previousProgressBars = _lingFengCustomProgressBars;
            string previousCustomText = _lingFengCustomText;
            byte previousCustomTextColour = _lingFengCustomTextColour;
            ushort[] previousItemEffects = _lingFengItemEffects;
            int[] previousNewItemValues = _lingFengNewItemValues;
            byte previousNameColour = LingFengNameColour;
            ushort? previousLooks = LingFengLooks;
            short? previousShape = LingFengShape;
            BindMode previousBindingFlags = LingFengBindingFlags;
            byte previousUpgradeCount = LingFengUpgradeCount;
            bool previousCannotTakeOff = LingFengCannotTakeOff;
            try
            {
                ReadLingFengCustomAttributes(reader, true, false, true, false, true, false);
                if (stream.Position != stream.Length) throw new InvalidDataException();
                return true;
            }
            catch
            {
                _lingFengCustomAttributes = previous;
                _lingFengByteMarks = previousByteMarks;
                _lingFengIntMarks = previousIntMarks;
                _lingFengTextMarks = previousTextMarks;
                _lingFengCustomProgressBars = previousProgressBars;
                _lingFengCustomText = previousCustomText;
                _lingFengCustomTextColour = previousCustomTextColour;
                _lingFengItemEffects = previousItemEffects;
                _lingFengNewItemValues = previousNewItemValues;
                LingFengNameColour = previousNameColour;
                LingFengLooks = previousLooks;
                LingFengShape = previousShape;
                LingFengBindingFlags = previousBindingFlags;
                LingFengUpgradeCount = previousUpgradeCount;
                LingFengCannotTakeOff = previousCannotTakeOff;
                return false;
            }
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static LingFengCustomItemAttribute[] CreateLingFengCustomAttributes()
    {
        var values = new LingFengCustomItemAttribute[LingFengCustomAttributeLimit];
        for (int i = 0; i < values.Length; i++) values[i] = new LingFengCustomItemAttribute();
        return values;
    }

    private static LingFengCustomItemProgressBar[] CreateLingFengCustomProgressBars()
    {
        var values = new LingFengCustomItemProgressBar[LingFengCustomProgressBarLimit];
        for (int i = 0; i < values.Length; i++) values[i] = new LingFengCustomItemProgressBar();
        return values;
    }

    private void WriteLingFengCustomAttributes(BinaryWriter writer)
    {
        int count = _lingFengCustomAttributes.Count(value => value.IsDefined);
        writer.Write((byte)count);
        for (int index = 0; index < _lingFengCustomAttributes.Length; index++)
        {
            LingFengCustomItemAttribute attribute = _lingFengCustomAttributes[index];
            if (!attribute.IsDefined) continue;
            writer.Write((byte)index);
            writer.Write(attribute.Colour);
            writer.Write(attribute.Binding);
            writer.Write(attribute.DisplayOrder);
            writer.Write(attribute.Mode);
            writer.Write(attribute.Module);
            writer.Write(attribute.Value1);
            writer.Write(attribute.Value2);
            writer.Write(attribute.Value3);
        }
        writer.Write(_lingFengByteMarks);
        foreach (int value in _lingFengIntMarks) writer.Write(value);
        foreach (string value in _lingFengTextMarks) writer.Write(value ?? string.Empty);
        writer.Write(_lingFengCustomText ?? string.Empty);
        writer.Write(_lingFengCustomTextColour);
        int progressCount = _lingFengCustomProgressBars.Count(value => value.IsDefined);
        writer.Write((byte)progressCount);
        for (int index = 0; index < _lingFengCustomProgressBars.Length; index++)
        {
            LingFengCustomItemProgressBar progress = _lingFengCustomProgressBars[index];
            if (!progress.IsDefined) continue;
            writer.Write((byte)index);
            writer.Write(progress.Enabled);
            writer.Write(progress.Text ?? string.Empty);
            writer.Write(progress.Colour);
            writer.Write(progress.FrameCount);
            writer.Write(progress.DisplayMode);
            writer.Write(progress.Maximum);
            writer.Write(progress.Current);
        }
        foreach (ushort effect in _lingFengItemEffects) writer.Write(effect);
        foreach (int value in _lingFengNewItemValues) writer.Write(value);
        writer.Write(LingFengNameColour);
        writer.Write(LingFengLooks.HasValue);
        if (LingFengLooks.HasValue) writer.Write(LingFengLooks.Value);
        writer.Write(LingFengShape.HasValue);
        if (LingFengShape.HasValue) writer.Write(LingFengShape.Value);
        writer.Write((short)LingFengBindingFlags);
        writer.Write(LingFengUpgradeCount);
        writer.Write(LingFengCannotTakeOff);
    }

    private void ReadLingFengCustomAttributes(
        BinaryReader reader,
        bool readBindingFlags,
        bool requireBindingFlags,
        bool readUpgradeCount,
        bool requireUpgradeCount,
        bool readCannotTakeOff,
        bool requireCannotTakeOff)
    {
        int count = reader.ReadByte();
        if (count > LingFengCustomAttributeLimit) throw new InvalidDataException();
        var values = CreateLingFengCustomAttributes();
        var seen = new HashSet<int>();
        for (int entry = 0; entry < count; entry++)
        {
            int index = reader.ReadByte();
            if (index >= LingFengCustomAttributeLimit || !seen.Add(index))
                throw new InvalidDataException();
            var attribute = new LingFengCustomItemAttribute
            {
                Colour = reader.ReadByte(),
                Binding = reader.ReadByte(),
                DisplayOrder = reader.ReadByte(),
                Mode = reader.ReadByte(),
                Module = reader.ReadByte(),
                Value1 = reader.ReadInt32(),
                Value2 = reader.ReadInt32(),
                Value3 = reader.ReadInt32()
            };
            if (attribute.Binding > 60 || attribute.Mode > 2 || attribute.Module > 14)
                throw new InvalidDataException();
            values[index] = attribute;
        }
        byte[] byteMarks = reader.ReadBytes(20);
        if (byteMarks.Length != 20) throw new EndOfStreamException();
        var intMarks = new int[10];
        for (int index = 0; index < intMarks.Length; index++) intMarks[index] = reader.ReadInt32();
        var textMarks = new string[2];
        for (int index = 0; index < textMarks.Length; index++)
        {
            textMarks[index] = reader.ReadString();
            if (textMarks[index].Length > 20) throw new InvalidDataException();
        }
        string customText = reader.ReadString();
        if (customText.Length > 120) throw new InvalidDataException();
        byte customTextColour = reader.ReadByte();
        int progressCount = reader.ReadByte();
        if (progressCount > LingFengCustomProgressBarLimit) throw new InvalidDataException();
        LingFengCustomItemProgressBar[] progressBars = CreateLingFengCustomProgressBars();
        var seenProgress = new HashSet<int>();
        for (int entry = 0; entry < progressCount; entry++)
        {
            int index = reader.ReadByte();
            if (index >= LingFengCustomProgressBarLimit || !seenProgress.Add(index))
                throw new InvalidDataException();
            var progress = new LingFengCustomItemProgressBar
            {
                Enabled = reader.ReadBoolean(),
                Text = reader.ReadString(),
                Colour = reader.ReadByte(),
                FrameCount = reader.ReadByte(),
                DisplayMode = reader.ReadByte(),
                Maximum = reader.ReadInt32(),
                Current = reader.ReadInt32()
            };
            if (progress.Text.Length > 120 || progress.DisplayMode > 2 ||
                progress.Maximum < 0 || progress.Current < 0 ||
                progress.Maximum > 0 && progress.Current > progress.Maximum)
                throw new InvalidDataException();
            progressBars[index] = progress;
        }
        var itemEffects = new ushort[3];
        for (int index = 0; index < itemEffects.Length; index++)
            itemEffects[index] = reader.ReadUInt16();
        var newItemValues = new int[LingFengNewItemValueLimit];
        for (int index = 0; index < newItemValues.Length; index++)
        {
            newItemValues[index] = reader.ReadInt32();
            if (newItemValues[index] is < 0 or > 1000) throw new InvalidDataException();
        }
        byte nameColour = reader.ReadByte();
        ushort? looks = reader.ReadBoolean() ? reader.ReadUInt16() : null;
        short? shape = reader.ReadBoolean() ? reader.ReadInt16() : null;
        BindMode bindingFlags = BindMode.None;
        if (readBindingFlags)
        {
            if (reader.BaseStream.Position < reader.BaseStream.Length)
                bindingFlags = (BindMode)reader.ReadInt16();
            else if (requireBindingFlags)
                throw new EndOfStreamException();
            const BindMode supported = BindMode.DontDrop | BindMode.DontTrade |
                                       BindMode.DontStore | BindMode.DontRepair |
                                       BindMode.DontSell | BindMode.DontDeathdrop |
                                       BindMode.DestroyOnDrop;
            if ((bindingFlags & ~supported) != 0) throw new InvalidDataException();
        }
        byte upgradeCount = 0;
        if (readUpgradeCount)
        {
            if (reader.BaseStream.Position < reader.BaseStream.Length)
                upgradeCount = reader.ReadByte();
            else if (requireUpgradeCount)
                throw new EndOfStreamException();
        }
        bool cannotTakeOff = false;
        if (readCannotTakeOff)
        {
            if (reader.BaseStream.Position < reader.BaseStream.Length)
            {
                byte encoded = reader.ReadByte();
                if (encoded > 1) throw new InvalidDataException();
                cannotTakeOff = encoded == 1;
            }
            else if (requireCannotTakeOff)
                throw new EndOfStreamException();
        }
        _lingFengCustomAttributes = values;
        _lingFengByteMarks = byteMarks;
        _lingFengIntMarks = intMarks;
        _lingFengTextMarks = textMarks;
        _lingFengCustomText = customText;
        _lingFengCustomTextColour = customTextColour;
        _lingFengCustomProgressBars = progressBars;
        _lingFengItemEffects = itemEffects;
        _lingFengNewItemValues = newItemValues;
        LingFengNameColour = nameColour;
        LingFengLooks = looks;
        LingFengShape = shape;
        LingFengBindingFlags = bindingFlags;
        LingFengUpgradeCount = upgradeCount;
        LingFengCannotTakeOff = cannotTakeOff;
    }

    private static bool TryCalculate(int current, string operation, int value, out int result)
    {
        long calculated = operation switch
        {
            "+" => (long)current + value,
            "-" => (long)current - value,
            "=" => value,
            _ => long.MaxValue
        };
        if (calculated is < int.MinValue or > int.MaxValue)
        {
            result = 0;
            return false;
        }
        result = (int)calculated;
        return true;
    }

    private static IEnumerable<Stat> GetLingFengBoundStats(int binding) => binding switch
    {
        1 => new[] { Stat.MinAC, Stat.MaxAC },
        2 => new[] { Stat.MinMAC, Stat.MaxMAC },
        3 => new[] { Stat.MinDC, Stat.MaxDC },
        4 => new[] { Stat.MinMC, Stat.MaxMC },
        5 => new[] { Stat.MinSC, Stat.MaxSC },
        6 => new[] { Stat.HP },
        7 => new[] { Stat.MP },
        _ => Array.Empty<Stat>()
    };

    private static string FormatLingFengCustomAttribute(LingFengCustomItemAttribute attribute)
    {
        string label = attribute.Binding switch
        {
            1 => "防御",
            2 => "魔防",
            3 => "攻击",
            4 => "魔法",
            5 => "道术",
            6 => "生命",
            7 => "魔法值",
            _ => $"自定义属性{attribute.Binding}"
        };
        string suffix = attribute.Mode == 0 ? string.Empty : "%";
        return $"{label}: {attribute.Value1}{suffix}/{attribute.Value2}{suffix}/{attribute.Value3}{suffix}";
    }

    private static string FormatLingFengCustomProgressBar(LingFengCustomItemProgressBar progress)
    {
        int percent = progress.Maximum <= 0 ? 0 :
            checked((int)((long)progress.Current * 100 / progress.Maximum));
        string text = string.IsNullOrEmpty(progress.Text) ? "进度" : progress.Text;
        text = text.Replace("%p", progress.Current.ToString(), StringComparison.OrdinalIgnoreCase)
            .Replace("%m", progress.Maximum.ToString(), StringComparison.OrdinalIgnoreCase)
            .Replace("%r", percent.ToString(), StringComparison.OrdinalIgnoreCase);
        return progress.DisplayMode switch
        {
            1 => $"{text}{percent}%",
            2 => $"{text}{progress.Current}/{progress.Maximum}",
            _ => text
        };
    }

    private static int SaturatingAdd(int current, long addition) =>
        (int)Math.Max(int.MinValue, Math.Min(int.MaxValue, (long)current + addition));

    public int GetTotal(Stat type)
    {
        return AddedStats[type] + Info.Stats[type];
    }

    public uint Price()
    {
        if (Info == null) return 0;

        uint p = Info.Price;


        if (Info.Durability > 0)
        {
            float r = ((Info.Price / 2F) / Info.Durability);

            p = (uint)(MaxDura * r);

            if (MaxDura > 0)
                r = CurrentDura / (float)MaxDura;
            else
                r = 0;

            p = (uint)Math.Floor(p / 2F + ((p / 2F) * r) + Info.Price / 2F);
        }


        p = (uint)(p * (AddedStats.Count * 0.1F + 1F));


        return p * Count;
    }
    public uint RepairPrice()
    {
        if (Info == null || Info.Durability == 0)
            return 0;

        var p = Info.Price;

        if (Info.Durability > 0)
        {
            p = (uint)Math.Floor(MaxDura * ((Info.Price / 2F) / Info.Durability) + Info.Price / 2F);
            p = (uint)(p * (AddedStats.Count * 0.1F + 1F));

        }

        var cost = p * Count - Price();

        if (RentalInformation == null)
            return cost;

        return cost * 2;
    }

    public uint Quality()
    {
        uint q = (uint)(AddedStats.Count + Awake.GetAwakeLevel() + 1);

        return q;
    }

    public uint AwakeningPrice()
    {
        if (Info == null) return 0;

        uint p = 1500;

        p = (uint)((p * (1 + Awake.GetAwakeLevel() * 2)) * (uint)Info.Grade);

        return p;
    }

    public uint DisassemblePrice()
    {
        if (Info == null) return 0;

        uint p = 1500 * (uint)Info.Grade;

        p = (uint)(p * ((AddedStats.Count + Awake.GetAwakeLevel()) * 0.1F + 1F));

        return p;
    }

    public uint DowngradePrice()
    {
        if (Info == null) return 0;

        uint p = 3000;

        p = (uint)((p * (1 + (Awake.GetAwakeLevel() + 1) * 2)) * (uint)Info.Grade);

        return p;
    }

    public uint ResetPrice()
    {
        if (Info == null) return 0;

        uint p = 3000 * (uint)Info.Grade;

        p = (uint)(p * (AddedStats.Count * 0.2F + 1F));

        return p;
    }
    public void SetSlotSize(int? size = null)
    {
        if (size == null)
        {
            switch (Info.Type)
            {
                case ItemType.坐骑:
                    if (Info.Shape < 7)
                        size = 4;
                    else if (Info.Shape < 13)
                        size = 5;
                    break;
                case ItemType.武器:
                    if (Info.Shape == 49 || Info.Shape == 50)
                        size = 5;
                    break;
            }
        }

        if (size == null && Info == null) return;
        if (size != null && size == Slots.Length) return;
        if (size == null && Info != null && Info.Slots == Slots.Length) return;

        Array.Resize(ref Slots, size ?? Info.Slots);
    }

    public ushort Image
    {
        get
        {
            if (LingFengLooks.HasValue) return LingFengLooks.Value;
            switch (Info.Type)
            {
                #region Amulet and Poison Stack Image changes
                case ItemType.护身符:
                    if (Info.StackSize > 0)
                    {
                        switch (Info.Shape)
                        {
                            case 0: //Amulet
                                if (Count >= 300) return 3662;
                                if (Count >= 200) return 3661;
                                if (Count >= 100) return 3660;
                                return 3660;
                            case 1: //Grey Poison
                                if (Count >= 150) return 3675;
                                if (Count >= 100) return 2960;
                                if (Count >= 50) return 3674;
                                return 3673;
                            case 2: //Yellow Poison
                                if (Count >= 150) return 3672;
                                if (Count >= 100) return 2961;
                                if (Count >= 50) return 3671;
                                return 3670;
                        }
                    }
                    break;
            }

            #endregion

            return Info.Image;
        }
    }

    public UserItem Clone()
    {
        UserItem item = new UserItem(Info)
        {
            UniqueID = UniqueID,
            CurrentDura = CurrentDura,
            MaxDura = MaxDura,
            Count = Count,
            GemCount = GemCount,
            DuraChanged = DuraChanged,
            SoulBoundId = SoulBoundId,
            Identified = Identified,
            Cursed = Cursed,
            Slots = Slots,
            AddedStats = new Stats(AddedStats),
            Awake = Awake,

            RefineAdded = RefineAdded,

            ExpireInfo = ExpireInfo,
            RentalInformation = RentalInformation,
            SealedInfo = SealedInfo,

            IsShopItem = IsShopItem,
            GMMade = GMMade
        };

        for (int index = 0; index < LingFengCustomAttributeLimit; index++)
            item._lingFengCustomAttributes[index] = _lingFengCustomAttributes[index].Clone();
        Array.Copy(_lingFengByteMarks, item._lingFengByteMarks, _lingFengByteMarks.Length);
        Array.Copy(_lingFengIntMarks, item._lingFengIntMarks, _lingFengIntMarks.Length);
        Array.Copy(_lingFengTextMarks, item._lingFengTextMarks, _lingFengTextMarks.Length);
        item._lingFengCustomText = _lingFengCustomText;
        item._lingFengCustomTextColour = _lingFengCustomTextColour;
        for (int index = 0; index < LingFengCustomProgressBarLimit; index++)
            item._lingFengCustomProgressBars[index] = _lingFengCustomProgressBars[index].Clone();
        Array.Copy(_lingFengItemEffects, item._lingFengItemEffects, _lingFengItemEffects.Length);
        Array.Copy(_lingFengNewItemValues, item._lingFengNewItemValues,
            _lingFengNewItemValues.Length);
        item.LingFengNameColour = LingFengNameColour;
        item.LingFengLooks = LingFengLooks;
        item.LingFengShape = LingFengShape;
        item.LingFengBindingFlags = LingFengBindingFlags;
        item.LingFengUpgradeCount = LingFengUpgradeCount;
        item.LingFengCannotTakeOff = LingFengCannotTakeOff;

        return item;
    }

}

public class ExpireInfo
{
    public DateTime ExpiryDate;

    public ExpireInfo() { }

    public ExpireInfo(BinaryReader reader, int version = int.MaxValue, int Customversion = int.MaxValue)
    {
        ExpiryDate = DateTime.FromBinary(reader.ReadInt64());
    }

    public void Save(BinaryWriter writer)
    {
        writer.Write(ExpiryDate.ToBinary());
    }
}

public class SealedInfo
{
    public DateTime ExpiryDate;
    public DateTime NextSealDate;

    public SealedInfo() { }

    public SealedInfo(BinaryReader reader, int version = int.MaxValue, int Customversion = int.MaxValue)
    {
        ExpiryDate = DateTime.FromBinary(reader.ReadInt64());

        if (version > 92)
        {
            NextSealDate = DateTime.FromBinary(reader.ReadInt64());
        }
    }

    public void Save(BinaryWriter writer)
    {
        writer.Write(ExpiryDate.ToBinary());
        writer.Write(NextSealDate.ToBinary());
    }
}

public class RentalInformation
{
    public string OwnerName;
    public BindMode BindingFlags = BindMode.None;
    public DateTime ExpiryDate;
    public bool RentalLocked;

    public RentalInformation() { }

    public RentalInformation(BinaryReader reader, int version = int.MaxValue, int CustomVersion = int.MaxValue)
    {
        OwnerName = reader.ReadString();
        BindingFlags = (BindMode)reader.ReadInt16();
        ExpiryDate = DateTime.FromBinary(reader.ReadInt64());
        RentalLocked = reader.ReadBoolean();
    }

    public void Save(BinaryWriter writer)
    {
        writer.Write(OwnerName);
        writer.Write((short)BindingFlags);
        writer.Write(ExpiryDate.ToBinary());
        writer.Write(RentalLocked);
    }
}

public class GameShopItem
{
    public int ItemIndex;
    public int GIndex;
    public ItemInfo Info;
    public uint GoldPrice = 0;
    public uint CreditPrice = 0;
    public ushort Count = 1;
    public string Class = "";
    public string Category = "";
    public int Stock = 0;
    public bool iStock = false;
    public bool Deal = false;
    public bool TopItem = false;
    public DateTime Date;
    public bool CanBuyGold = false;
    public bool CanBuyCredit = false;

    public GameShopItem()
    {
    }

    public GameShopItem(BinaryReader reader, int version = int.MaxValue, int Customversion = int.MaxValue)
    {
        ItemIndex = reader.ReadInt32();
        GIndex = reader.ReadInt32();
        GoldPrice = reader.ReadUInt32();
        CreditPrice = reader.ReadUInt32();
        if (version <= 84)
        {
            Count = (ushort)reader.ReadUInt32();
        }
        else
        {
            Count = reader.ReadUInt16();
        }
        Class = reader.ReadString();
        Category = reader.ReadString();
        Stock = reader.ReadInt32();
        iStock = reader.ReadBoolean();
        Deal = reader.ReadBoolean();
        TopItem = reader.ReadBoolean();
        Date = DateTime.FromBinary(reader.ReadInt64());
        if (version > 105)
        {
            CanBuyGold = reader.ReadBoolean();
            CanBuyCredit = reader.ReadBoolean();
        }
    }

    public GameShopItem(BinaryReader reader, bool packet = false)
    {
        ItemIndex = reader.ReadInt32();
        GIndex = reader.ReadInt32();
        Info = new ItemInfo(reader);
        GoldPrice = reader.ReadUInt32();
        CreditPrice = reader.ReadUInt32();
        Count = reader.ReadUInt16();
        Class = reader.ReadString();
        Category = reader.ReadString();
        Stock = reader.ReadInt32();
        iStock = reader.ReadBoolean();
        Deal = reader.ReadBoolean();
        TopItem = reader.ReadBoolean();
        Date = DateTime.FromBinary(reader.ReadInt64());
        CanBuyCredit = reader.ReadBoolean();
        CanBuyGold = reader.ReadBoolean();
    }

    public void Save(BinaryWriter writer, bool packet = false)
    {
        writer.Write(ItemIndex);
        writer.Write(GIndex);
        if (packet) Info.Save(writer);
        writer.Write(GoldPrice);
        writer.Write(CreditPrice);
        writer.Write(Count);
        writer.Write(Class);
        writer.Write(Category);
        writer.Write(Stock);
        writer.Write(iStock);
        writer.Write(Deal);
        writer.Write(TopItem);
        writer.Write(Date.ToBinary());
        writer.Write(CanBuyCredit);
        writer.Write(CanBuyGold);
    }

    public override string ToString()
    {
        return string.Format("{0}: {1}", GIndex, Info.Name);
    }

}

public class Awake
{
    //Awake Option
    public static byte AwakeSuccessRate = 70;
    public static byte AwakeHitRate = 70;
    public static int MaxAwakeLevel = 5;
    public static byte Awake_WeaponRate = 1;
    public static byte Awake_HelmetRate = 1;
    public static byte Awake_ArmorRate = 5;
    public static byte AwakeChanceMin = 1;
    public static float[] AwakeMaterialRate = new float[4] { 1.0F, 1.0F, 1.0F, 1.0F };
    public static byte[] AwakeChanceMax = new byte[4] { 1, 2, 3, 4 };
    public static List<List<byte>[]> AwakeMaterials = new List<List<byte>[]>();

    public AwakeType Type = AwakeType.None;
    readonly List<byte> listAwake = new List<byte>();

    public Awake() { }

    public Awake(BinaryReader reader)
    {
        Type = (AwakeType)reader.ReadByte();
        int count = reader.ReadInt32();
        for (int i = 0; i < count; i++)
        {
            listAwake.Add(reader.ReadByte());
        }
    }

    public void Save(BinaryWriter writer)
    {
        writer.Write((byte)Type);
        writer.Write(listAwake.Count);
        foreach (byte value in listAwake)
        {
            writer.Write(value);
        }
    }
    public bool IsMaxLevel() { return listAwake.Count == Awake.MaxAwakeLevel; }

    public int GetAwakeLevel() { return listAwake.Count; }

    public byte GetAwakeValue()
    {
        byte total = 0;

        foreach (byte value in listAwake)
        {
            total += value;
        }

        return total;
    }

    public bool CheckAwakening(UserItem item, AwakeType type)
    {
        if (item.Info.Bind.HasFlag(BindMode.DontUpgrade))
            return false;

        if (item.Info.CanAwakening != true)
            return false;

        if (item.Info.Grade == ItemGrade.None)
            return false;

        if (IsMaxLevel()) return false;

        if (this.Type == AwakeType.None)
        {
            if (item.Info.Type == ItemType.武器)
            {
                if (type == AwakeType.物理攻击 ||
                    type == AwakeType.魔法攻击 ||
                    type == AwakeType.道术攻击)
                {
                    this.Type = type;
                    return true;
                }
                else
                    return false;
            }
            else if (item.Info.Type == ItemType.头盔)
            {
                if (type == AwakeType.物理防御 ||
                    type == AwakeType.魔法防御)
                {
                    this.Type = type;
                    return true;
                }
                else
                    return false;
            }
            else if (item.Info.Type == ItemType.盔甲)
            {
                if (type == AwakeType.生命法力值)
                {
                    this.Type = type;
                    return true;
                }
                else
                    return false;
            }
            else
                return false;
        }
        else
        {
            if (this.Type == type)
                return true;
            else
                return false;
        }
    }

    public int UpgradeAwake(UserItem item, AwakeType type, out bool[] isHit)
    {
        //return -1 condition error, -1 = dont upgrade, 0 = failed, 1 = Succeed,  
        isHit = null;
        if (CheckAwakening(item, type) != true)
            return -1;

        Random rand = new Random(DateTime.Now.Millisecond);

        if (rand.Next(0, 100) <= AwakeSuccessRate)
        {
            isHit = Awakening(item);
            return 1;
        }
        else
        {
            isHit = MakeHit(1, out _);
            return 0;
        }
    }

    public int RemoveAwake()
    {
        if (listAwake.Count > 0)
        {
            listAwake.Remove(listAwake[listAwake.Count - 1]);

            if (listAwake.Count == 0)
                Type = AwakeType.None;

            return 1;
        }
        else
        {
            Type = AwakeType.None;
            return 0;
        }
    }

    public int GetAwakeLevelValue(int i) { return listAwake[i]; }

    public byte GetDC() { return (Type == AwakeType.物理攻击 ? GetAwakeValue() : (byte)0); }
    public byte GetMC() { return (Type == AwakeType.魔法攻击 ? GetAwakeValue() : (byte)0); }
    public byte GetSC() { return (Type == AwakeType.道术攻击 ? GetAwakeValue() : (byte)0); }
    public byte GetAC() { return (Type == AwakeType.物理防御 ? GetAwakeValue() : (byte)0); }
    public byte GetMAC() { return (Type == AwakeType.魔法防御 ? GetAwakeValue() : (byte)0); }
    public byte GetHPMP() { return (Type == AwakeType.生命法力值 ? GetAwakeValue() : (byte)0); }

    private bool[] MakeHit(int maxValue, out int makeValue)
    {
        float stepValue = (float)maxValue / 5.0f;
        float totalValue = 0.0f;
        bool[] isHit = new bool[5];
        Random rand = new Random(DateTime.Now.Millisecond);

        for (int i = 0; i < 5; i++)
        {
            if (rand.Next(0, 100) < AwakeHitRate)
            {
                totalValue += stepValue;
                isHit[i] = true;
            }
            else
            {
                isHit[i] = false;
            }
        }

        makeValue = totalValue <= 1.0f ? 1 : (int)totalValue;
        return isHit;
    }

    private bool[] Awakening(UserItem item)
    {
        int minValue = AwakeChanceMin;
        int maxValue = (AwakeChanceMax[(int)item.Info.Grade - 1] < minValue) ? minValue : AwakeChanceMax[(int)item.Info.Grade - 1];

        bool[] returnValue = MakeHit(maxValue, out int result);

        switch (item.Info.Type)
        {
            case ItemType.武器:
                result *= (int)Awake_WeaponRate;
                break;
            case ItemType.盔甲:
                result *= (int)Awake_ArmorRate;
                break;
            case ItemType.头盔:
                result *= (int)Awake_HelmetRate;
                break;
            default:
                result = 0;
                break;
        }

        listAwake.Add((byte)result);

        return returnValue;
    }
}


public class ItemRentalInformation
{
    public ulong ItemId;
    public string ItemName;
    public string RentingPlayerName;
    public DateTime ItemReturnDate;

    public ItemRentalInformation() { }

    public ItemRentalInformation(BinaryReader reader, int version = int.MaxValue, int customVersion = int.MaxValue)
    {
        ItemId = reader.ReadUInt64();
        ItemName = reader.ReadString();
        RentingPlayerName = reader.ReadString();
        ItemReturnDate = DateTime.FromBinary(reader.ReadInt64());
    }

    public void Save(BinaryWriter writer)
    {
        writer.Write(ItemId);
        writer.Write(ItemName);
        writer.Write(RentingPlayerName);
        writer.Write(ItemReturnDate.ToBinary());
    }
}


public class ItemSets
{
    public ItemSet Set;
    public List<ItemType> Type;
    private byte Amount
    {
        get
        {
            switch (Set)
            {
                case ItemSet.世轮套装:
                case ItemSet.绿翠套装:
                case ItemSet.道护套装:
                case ItemSet.贵人战套:
                case ItemSet.贵人法套:
                case ItemSet.贵人道套:
                case ItemSet.贵人刺套:
                case ItemSet.贵人弓套:
                    return 2;
                case ItemSet.赤兰套装:
                case ItemSet.密火套装:
                case ItemSet.破碎套装:
                case ItemSet.幻魔石套:
                case ItemSet.灵玉套装:
                case ItemSet.五玄套装:
                case ItemSet.白骨套装:
                case ItemSet.虫血套装:
                case ItemSet.鏃未套装:
                    return 3;
                case ItemSet.记忆套装:
                case ItemSet.神龙套装:
                    return 4;
                case ItemSet.祈祷套装:
                case ItemSet.白金套装:
                case ItemSet.强白金套:
                case ItemSet.红玉套装:
                case ItemSet.强红玉套:
                case ItemSet.软玉套装:
                case ItemSet.强软玉套:
                case ItemSet.龙血套装:
                case ItemSet.监视套装:
                case ItemSet.暴压套装:
                case ItemSet.贝玉套装:
                case ItemSet.黑术套装:
                case ItemSet.强青玉套:
                case ItemSet.青玉套装:
                case ItemSet.圣龙套装:
                    return 5;
                case ItemSet.天龙套装:
                    return 8;
                default:
                    return 0;
            }
        }
    }
    public byte Count;
    public bool SetComplete
    {
        get
        {
            return Count >= Amount;
        }
    }
}


public class RandomItemStat
{
    public byte MaxDuraChance, MaxDuraStatChance, MaxDuraMaxStat;
    public byte MaxAcChance, MaxAcStatChance, MaxAcMaxStat, MaxMacChance, MaxMacStatChance, MaxMacMaxStat, MaxDcChance, MaxDcStatChance, MaxDcMaxStat, MaxMcChance, MaxMcStatChance, MaxMcMaxStat, MaxScChance, MaxScStatChance, MaxScMaxStat;
    public byte AccuracyChance, AccuracyStatChance, AccuracyMaxStat, AgilityChance, AgilityStatChance, AgilityMaxStat, HpChance, HpStatChance, HpMaxStat, MpChance, MpStatChance, MpMaxStat, StrongChance, StrongStatChance, StrongMaxStat;
    public byte MagicResistChance, MagicResistStatChance, MagicResistMaxStat, PoisonResistChance, PoisonResistStatChance, PoisonResistMaxStat;
    public byte HpRecovChance, HpRecovStatChance, HpRecovMaxStat, MpRecovChance, MpRecovStatChance, MpRecovMaxStat, PoisonRecovChance, PoisonRecovStatChance, PoisonRecovMaxStat;
    public byte CriticalRateChance, CriticalRateStatChance, CriticalRateMaxStat, CriticalDamageChance, CriticalDamageStatChance, CriticalDamageMaxStat;
    public byte FreezeChance, FreezeStatChance, FreezeMaxStat, PoisonAttackChance, PoisonAttackStatChance, PoisonAttackMaxStat;
    public byte AttackSpeedChance, AttackSpeedStatChance, AttackSpeedMaxStat, LuckChance, LuckStatChance, LuckMaxStat;
    public byte CurseChance;
    public byte SlotChance, SlotStatChance, SlotMaxStat;

    public RandomItemStat(ItemType Type = ItemType.技能书)
    {
        switch (Type)
        {
            case ItemType.武器:
                SetWeapon();
                break;
            case ItemType.盔甲:
                SetArmour();
                break;
            case ItemType.头盔:
                SetHelmet();
                break;
            case ItemType.腰带:
            case ItemType.靴子:
                SetBeltBoots();
                break;
            case ItemType.项链:
                SetNecklace();
                break;
            case ItemType.手镯:
                SetBracelet();
                break;
            case ItemType.戒指:
                SetRing();
                break;
            case ItemType.坐骑:
                SetMount();
                break;
        }
    }

    public void SetWeapon()
    {
        MaxDuraChance = 2;
        MaxDuraStatChance = 13;
        MaxDuraMaxStat = 13;

        MaxDcChance = 15;
        MaxDcStatChance = 15;
        MaxDcMaxStat = 13;

        MaxMcChance = 20;
        MaxMcStatChance = 15;
        MaxMcMaxStat = 13;

        MaxScChance = 20;
        MaxScStatChance = 15;
        MaxScMaxStat = 13;

        AttackSpeedChance = 60;
        AttackSpeedStatChance = 30;
        AttackSpeedMaxStat = 3;

        StrongChance = 24;
        StrongStatChance = 20;
        StrongMaxStat = 2;

        AccuracyChance = 30;
        AccuracyStatChance = 20;
        AccuracyMaxStat = 2;

        SlotChance = 0;
        SlotStatChance = 0;
        SlotMaxStat = 4;
    }
    public void SetArmour()
    {
        MaxDuraChance = 2;
        MaxDuraStatChance = 10;
        MaxDuraMaxStat = 3;

        MaxAcChance = 30;
        MaxAcStatChance = 15;
        MaxAcMaxStat = 7;

        MaxMacChance = 30;
        MaxMacStatChance = 15;
        MaxMacMaxStat = 7;

        MaxDcChance = 40;
        MaxDcStatChance = 20;
        MaxDcMaxStat = 7;

        MaxMcChance = 40;
        MaxMcStatChance = 20;
        MaxMcMaxStat = 7;

        MaxScChance = 40;
        MaxScStatChance = 20;
        MaxScMaxStat = 7;

    }
    public void SetHelmet()
    {
        MaxDuraChance = 2;
        MaxDuraStatChance = 10;
        MaxDuraMaxStat = 3;

        MaxAcChance = 30;
        MaxAcStatChance = 15;
        MaxAcMaxStat = 7;

        MaxMacChance = 30;
        MaxMacStatChance = 15;
        MaxMacMaxStat = 7;

        MaxDcChance = 40;
        MaxDcStatChance = 20;
        MaxDcMaxStat = 7;

        MaxMcChance = 40;
        MaxMcStatChance = 20;
        MaxMcMaxStat = 7;

        MaxScChance = 40;
        MaxScStatChance = 20;
        MaxScMaxStat = 7;
    }
    public void SetBeltBoots()
    {
        MaxDuraChance = 2;
        MaxDuraStatChance = 10;
        MaxDuraMaxStat = 3;

        MaxAcChance = 30;
        MaxAcStatChance = 30;
        MaxAcMaxStat = 3;

        MaxMacChance = 30;
        MaxMacStatChance = 30;
        MaxMacMaxStat = 3;

        MaxDcChance = 30;
        MaxDcStatChance = 30;
        MaxDcMaxStat = 3;

        MaxMcChance = 30;
        MaxMcStatChance = 30;
        MaxMcMaxStat = 3;

        MaxScChance = 30;
        MaxScStatChance = 30;
        MaxScMaxStat = 3;

        AgilityChance = 60;
        AgilityStatChance = 30;
        AgilityMaxStat = 3;
    }
    public void SetNecklace()
    {
        MaxDuraChance = 2;
        MaxDuraStatChance = 10;
        MaxDuraMaxStat = 3;

        MaxDcChance = 15;
        MaxDcStatChance = 30;
        MaxDcMaxStat = 7;

        MaxMcChance = 15;
        MaxMcStatChance = 30;
        MaxMcMaxStat = 7;

        MaxScChance = 15;
        MaxScStatChance = 30;
        MaxScMaxStat = 7;

        AccuracyChance = 60;
        AccuracyStatChance = 30;
        AccuracyMaxStat = 7;

        AgilityChance = 60;
        AgilityStatChance = 30;
        AgilityMaxStat = 7;
    }
    public void SetBracelet()
    {
        MaxDuraChance = 2;
        MaxDuraStatChance = 10;
        MaxDuraMaxStat = 3;

        MaxAcChance = 20;
        MaxAcStatChance = 30;
        MaxAcMaxStat = 6;

        MaxMacChance = 20;
        MaxMacStatChance = 30;
        MaxMacMaxStat = 6;

        MaxDcChance = 30;
        MaxDcStatChance = 30;
        MaxDcMaxStat = 6;

        MaxMcChance = 30;
        MaxMcStatChance = 30;
        MaxMcMaxStat = 6;

        MaxScChance = 30;
        MaxScStatChance = 30;
        MaxScMaxStat = 6;
    }
    public void SetRing()
    {
        MaxDuraChance = 2;
        MaxDuraStatChance = 10;
        MaxDuraMaxStat = 3;

        MaxAcChance = 25;
        MaxAcStatChance = 20;
        MaxAcMaxStat = 6;

        MaxMacChance = 25;
        MaxMacStatChance = 20;
        MaxMacMaxStat = 6;

        MaxDcChance = 15;
        MaxDcStatChance = 30;
        MaxDcMaxStat = 6;

        MaxMcChance = 15;
        MaxMcStatChance = 30;
        MaxMcMaxStat = 6;

        MaxScChance = 15;
        MaxScStatChance = 30;
        MaxScMaxStat = 6;
    }

    public void SetMount()
    {
        SetRing();
    }
}

public class ChatItem
{
    public ulong UniqueID;
    public string Title;
    public MirGridType Grid;

    public string RegexInternalName
    {
        get { return $"<{Title.Replace("(", "\\(").Replace(")", "\\)")}>"; }
    }

    public string InternalName
    {
        get { return $"<{Title}/{UniqueID}>"; }
    }

    public ChatItem() { }

    public ChatItem(BinaryReader reader)
    {
        UniqueID = reader.ReadUInt64();
        Title = reader.ReadString();
        Grid = (MirGridType)reader.ReadByte();
    }

    public void Save(BinaryWriter writer)
    {
        writer.Write(UniqueID);
        writer.Write(Title);
        writer.Write((byte)Grid);
    }
}
