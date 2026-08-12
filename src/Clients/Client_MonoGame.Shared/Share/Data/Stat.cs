using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;

public sealed class Stats : IEquatable<Stats>
{
    public SortedDictionary<Stat, int> Values { get; set; } = new SortedDictionary<Stat, int>();
    public int Count => Values.Sum(pair => Math.Abs(pair.Value));

    public int this[Stat stat]
    {
        get
        {
            return !Values.TryGetValue(stat, out int result) ? 0 : result;
        }
        set
        {
            if (value == 0)
            {
                if (Values.ContainsKey(stat))
                {
                    Values.Remove(stat);
                }

                return;
            }

            Values[stat] = value;
        }
    }

    public Stats() { }

    public Stats(Stats stats)
    {
        foreach (KeyValuePair<Stat, int> pair in stats.Values)
            this[pair.Key] += pair.Value;
    }

    public Stats(BinaryReader reader, int version = int.MaxValue, int customVersion = int.MaxValue)
    {
        int count = reader.ReadInt32();

        for (int i = 0; i < count; i++)
            Values[(Stat)reader.ReadByte()] = reader.ReadInt32();
    }

    public void Add(Stats stats)
    {
        foreach (KeyValuePair<Stat, int> pair in stats.Values)
            this[pair.Key] += pair.Value;
    }

    public void Save(BinaryWriter writer)
    {
        writer.Write(Values.Count);

        foreach (KeyValuePair<Stat, int> pair in Values)
        {
            writer.Write((byte)pair.Key);
            writer.Write(pair.Value);
        }
    }

    public void Clear()
    {
        Values.Clear();
    }

    public bool Equals(Stats other)
    {
        if (Values.Count != other.Values.Count) return false;

        foreach (KeyValuePair<Stat, int> value in Values)
            if (other[value.Key] != value.Value) return false;

        return true;
    }
}

public enum StatFormula : byte
{
    Health,
    Mana,
    Weight,
    Stat
}

public enum Stat : byte
{
    [Description("防御")]
    MinAC = 0,
    [Description("防御")]
    MaxAC = 1,
    [Description("魔防")]
    MinMAC = 2,
    [Description("魔防")]
    MaxMAC = 3,
    [Description("攻击")]
    MinDC = 4,
    [Description("攻击")]
    MaxDC = 5,
    [Description("魔法")]
    MinMC = 6,
    [Description("魔法")]
    MaxMC = 7,
    [Description("道术")]
    MinSC = 8,
    [Description("道术")]
    MaxSC = 9,

    [Description("准确")]
    Accuracy = 10,
    准确 = Accuracy,
    [Description("敏捷")]
    Agility = 11,
    敏捷 = Agility,
    [Description("生命值")]
    HP = 12,
    [Description("魔法值")]
    MP = 13,
    [Description("攻击速度")]
    AttackSpeed = 14,
    攻击速度 = AttackSpeed,
    [Description("幸运")]
    Luck = 15,
    幸运 = Luck,
    [Description("负重")]
    BagWeight = 16,
    背包负重 = BagWeight,
    [Description("腕力")]
    HandWeight = 17,
    腕力负重 = HandWeight,
    [Description("穿戴重量")]
    WearWeight = 18,
    装备负重 = WearWeight,
    [Description("反弹")]
    Reflect = 19,
    反弹伤害 = Reflect,
    [Description("强壮")]
    Strong = 20,
    强度 = Strong,
    [Description("神圣")]
    Holy = 21,
    神圣 = Holy,
    [Description("冰冻")]
    Freezing = 22,
    冰冻伤害 = Freezing,
    [Description("毒素")]
    PoisonAttack = 23,
    毒素伤害 = PoisonAttack,

    [Description("魔法抵抗")]
    MagicResist = 30,
    魔法躲避 = MagicResist,
    [Description("中毒躲避")]
    PoisonResist = 31,
    毒物躲避 = PoisonResist,
    [Description("生命值恢复")]
    HealthRecovery = 32,
    生命恢复 = HealthRecovery,
    [Description("魔法值恢复")]
    SpellRecovery = 33,
    法力恢复 = SpellRecovery,
    [Description("中毒恢复")]
    PoisonRecovery = 34,
    中毒恢复 = PoisonRecovery,
    [Description("暴击率")]
    CriticalRate = 35,
    暴击倍率 = CriticalRate,
    [Description("暴击伤害")]
    CriticalDamage = 36,
    暴击伤害 = CriticalDamage,

    [Description("防御加成")]
    MaxACRatePercent = 40,
    最大防御数率 = MaxACRatePercent,
    [Description("魔防加成")]
    MaxMACRatePercent = 41,
    最大魔御数率 = MaxMACRatePercent,
    [Description("攻击加成")]
    MaxDCRatePercent = 42,
    最大物理攻击数率 = MaxDCRatePercent,
    [Description("魔法加成")]
    MaxMCRatePercent = 43,
    最大魔法攻击数率 = MaxMCRatePercent,
    [Description("道术加成")]
    MaxSCRatePercent = 44,
    最大道术攻击数率 = MaxSCRatePercent,
    [Description("攻击速度加成")]
    AttackSpeedRatePercent = 45,
    攻击速度数率 = AttackSpeedRatePercent,
    [Description("生命值加成")]
    HPRatePercent = 46,
    生命值数率 = HPRatePercent,
    [Description("魔法值加成")]
    MPRatePercent = 47,
    法力值数率 = MPRatePercent,
    [Description("生命值减少")]
    HPDrainRatePercent = 48,
    吸血数率 = HPDrainRatePercent,

    [Description("经验倍数")]
    ExpRatePercent = 100,
    经验增长数率 = ExpRatePercent,
    [Description("爆率")]
    ItemDropRatePercent = 101,
    物品掉落数率 = ItemDropRatePercent,
    [Description("金币爆率")]
    GoldDropRatePercent = 102,
    金币收益数率 = GoldDropRatePercent,
    [Description("挖矿机率")]
    MineRatePercent = 103,
    采矿出矿数率 = MineRatePercent,
    [Description("宝玉成功率")]
    GemRatePercent = 104,
    宝石成功数率 = GemRatePercent,
    [Description("钓鱼成功率")]
    FishRatePercent = 105,
    钓鱼成功数率 = FishRatePercent,
    [Description("工艺机率")]
    CraftRatePercent = 106,
    大师概率数率 = CraftRatePercent,
    [Description("技能增益")]
    SkillGainMultiplier = 107,
    技能熟练度倍率 = SkillGainMultiplier,
    [Description("攻击加成")]
    AttackBonus = 108,
    武器增伤 = AttackBonus,

    [Description("夫妻经验倍数")]
    LoverExpRatePercent = 120,
    伴侣专享经验数率 = LoverExpRatePercent,
    [Description("师徒伤害加成")]
    MentorDamageRatePercent = 121,
    师徒专享伤害数率 = MentorDamageRatePercent,
    [Description("师徒经验倍数")]
    MentorExpRatePercent = 123,
    师徒专享经验数率 = MentorExpRatePercent,
    [Description("伤害降低")]
    DamageReductionPercent = 124,
    伤害减免数率 = DamageReductionPercent,
    [Description("保护")]
    EnergyShieldPercent = 125,
    气功盾恢复数率 = EnergyShieldPercent,
    [Description("生命值保护增益")]
    EnergyShieldHPGain = 126,
    气功盾恢复生命值 = EnergyShieldHPGain,
    [Description("圣剑处罚")]
    ManaPenaltyPercent = 127,
    法力值消耗数率 = ManaPenaltyPercent,
    [Description("传送圣剑处罚")]
    TeleportManaPenaltyPercent = 128,
    传送技法力消耗数率 = TeleportManaPenaltyPercent,
    Hero = 129,

    [Description("未知")]
    Unknown = 255
}
