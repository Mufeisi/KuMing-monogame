using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;

public class GuildRank
{
    public List<GuildMember> Members = new List<GuildMember>();
    public string Name = "";
    public int Index = 0;
    public GuildRankOptions Options = (GuildRankOptions)0;

    public GuildRank() { }

    public GuildRank(BinaryReader reader, bool offline = false)
    {
        Name = reader.ReadString();
        Options = (GuildRankOptions)reader.ReadByte();

        if (!offline)
            Index = reader.ReadInt32();

        int membercount = reader.ReadInt32();
        for (int j = 0; j < membercount; j++)
            Members.Add(new GuildMember(reader, offline));
    }

    public void Save(BinaryWriter writer, bool save = false)
    {
        writer.Write(Name);
        writer.Write((byte)Options);

        if (!save)
            writer.Write(Index);

        writer.Write(Members.Count);
        for (int j = 0; j < Members.Count; j++)
            Members[j].Save(writer);
    }
}

public class GuildStorageItem
{
    public UserItem Item;
    public long UserId = 0;
    public GuildStorageItem() { }

    public GuildStorageItem(BinaryReader reader)
    {
        Item = new UserItem(reader);
        UserId = reader.ReadInt64();
    }
    public void Save(BinaryWriter writer)
    {
        Item.Save(writer);
        writer.Write(UserId);
    }
}

public class GuildMember
{
    public string Name = "";
    public string name
    {
        get => Name;
        set => Name = value;
    }

    public int Id;
    public object Player;
    public DateTime LastLogin;
    public bool hasvoted;
    public bool Online;

    public GuildMember() { }

    public GuildMember(BinaryReader reader, bool offline = false)
    {
        Name = reader.ReadString();
        Id = reader.ReadInt32();
        LastLogin = DateTime.FromBinary(reader.ReadInt64());
        hasvoted = reader.ReadBoolean();
        Online = reader.ReadBoolean();
        Online = offline ? false : Online;
    }

    public void Save(BinaryWriter writer)
    {
        writer.Write(Name);
        writer.Write(Id);
        writer.Write(LastLogin.ToBinary());
        writer.Write(hasvoted);
        writer.Write(Online);
    }
}

public class GuildBuffInfo
{
    public int Id;
    public int Icon = 0;
    public string Name = "";
    public string name
    {
        get => Name;
        set => Name = value;
    }

    public byte LevelRequirement;
    public byte PointsRequirement = 1;
    public int TimeLimit;
    public int ActivationCost;

    public Stats Stats;

    public GuildBuffInfo()
    {
        Stats = new Stats();
    }

    public GuildBuffInfo(BinaryReader reader)
    {
        Id = reader.ReadInt32();
        Icon = reader.ReadInt32();
        Name = reader.ReadString();
        LevelRequirement = reader.ReadByte();
        PointsRequirement = reader.ReadByte();
        TimeLimit = reader.ReadInt32();
        ActivationCost = reader.ReadInt32();

        Stats = new Stats(reader);
    }

    public GuildBuffInfo(InIReader reader, int i)
    {
        Id = reader.ReadInt32($"Buff-{i}", "Id", 0);
        Icon = reader.ReadInt32($"Buff-{i}", "Icon", 0);
        Name = reader.ReadString($"Buff-{i}", "Name", "");
        LevelRequirement = reader.ReadByte($"Buff-{i}", "LevelReq", 0);
        PointsRequirement = reader.ReadByte($"Buff-{i}", "PointsReq", 1);
        TimeLimit = reader.ReadInt32($"Buff-{i}", "TimeLimit", 0);
        ActivationCost = reader.ReadInt32($"Buff-{i}", "ActivationCost", 0);

        Stats = new Stats();
        Stats[Stat.MaxAC] = reader.ReadByte($"Buff-{i}", "BuffAc", 0);
        Stats[Stat.MaxMAC] = reader.ReadByte($"Buff-{i}", "BuffMAC", 0);
        Stats[Stat.MaxDC] = reader.ReadByte($"Buff-{i}", "BuffDc", 0);
        Stats[Stat.MaxMC] = reader.ReadByte($"Buff-{i}", "BuffMc", 0);
        Stats[Stat.MaxSC] = reader.ReadByte($"Buff-{i}", "BuffSc", 0);
        Stats[Stat.HP] = reader.ReadInt32($"Buff-{i}", "BuffMaxHp", 0);
        Stats[Stat.MP] = reader.ReadInt32($"Buff-{i}", "BuffMaxMp", 0);
        Stats[Stat.MineRatePercent] = reader.ReadByte($"Buff-{i}", "BuffMineRate", 0);
        Stats[Stat.GemRatePercent] = reader.ReadByte($"Buff-{i}", "BuffGemRate", 0);
        Stats[Stat.FishRatePercent] = reader.ReadByte($"Buff-{i}", "BuffFishRate", 0);
        Stats[Stat.ExpRatePercent] = reader.ReadByte($"Buff-{i}", "BuffExpRate", 0);
        Stats[Stat.CraftRatePercent] = reader.ReadByte($"Buff-{i}", "BuffCraftRate", 0);
        Stats[Stat.SkillGainMultiplier] = reader.ReadByte($"Buff-{i}", "BuffSkillRate", 0);
        Stats[Stat.HealthRecovery] = reader.ReadByte($"Buff-{i}", "BuffHpRegen", 0);
        Stats[Stat.SpellRecovery] = reader.ReadByte($"Buff-{i}", "BuffMpRegen", 0);
        Stats[Stat.AttackBonus] = reader.ReadByte($"Buff-{i}", "BuffAttack", 0);
        Stats[Stat.ItemDropRatePercent] = reader.ReadByte($"Buff-{i}", "BuffDropRate", 0);
        Stats[Stat.GoldDropRatePercent] = reader.ReadByte($"Buff-{i}", "BuffGoldRate", 0);
    }

    public void Save(InIReader reader, int i)
    {
        reader.Write($"Buff-{i}", "Id", Id);
        reader.Write($"Buff-{i}", "Icon", Icon);
        reader.Write($"Buff-{i}", "Name", Name);
        reader.Write($"Buff-{i}", "LevelReq", LevelRequirement);
        reader.Write($"Buff-{i}", "PointsReq", PointsRequirement);
        reader.Write($"Buff-{i}", "TimeLimit", TimeLimit);
        reader.Write($"Buff-{i}", "ActivationCost", ActivationCost);
        reader.Write($"Buff-{i}", "BuffAc", Stats[Stat.MaxAC]);
        reader.Write($"Buff-{i}", "BuffMAC", Stats[Stat.MaxMAC]);
        reader.Write($"Buff-{i}", "BuffDc", Stats[Stat.MaxDC]);
        reader.Write($"Buff-{i}", "BuffMc", Stats[Stat.MaxMC]);
        reader.Write($"Buff-{i}", "BuffSc", Stats[Stat.MaxSC]);
        reader.Write($"Buff-{i}", "BuffMaxHp", Stats[Stat.HP]);
        reader.Write($"Buff-{i}", "BuffMaxMp", Stats[Stat.MP]);
        reader.Write($"Buff-{i}", "BuffMineRate", Stats[Stat.MineRatePercent]);
        reader.Write($"Buff-{i}", "BuffGemRate", Stats[Stat.GemRatePercent]);
        reader.Write($"Buff-{i}", "BuffFishRate", Stats[Stat.FishRatePercent]);
        reader.Write($"Buff-{i}", "BuffExpRate", Stats[Stat.ExpRatePercent]);
        reader.Write($"Buff-{i}", "BuffCraftRate", Stats[Stat.CraftRatePercent]);
        reader.Write($"Buff-{i}", "BuffSkillRate", Stats[Stat.SkillGainMultiplier]);
        reader.Write($"Buff-{i}", "BuffHpRegen", Stats[Stat.HealthRecovery]);
        reader.Write($"Buff-{i}", "BuffMpRegen", Stats[Stat.SpellRecovery]);
        reader.Write($"Buff-{i}", "BuffAttack", Stats[Stat.AttackBonus]);
        reader.Write($"Buff-{i}", "BuffDropRate", Stats[Stat.ItemDropRatePercent]);
        reader.Write($"Buff-{i}", "BuffGoldRate", Stats[Stat.GoldDropRatePercent]);
    }

    public void Save(BinaryWriter writer)
    {
        writer.Write(Id);
        writer.Write(Icon);
        writer.Write(Name);
        writer.Write(LevelRequirement);
        writer.Write(PointsRequirement);
        writer.Write(TimeLimit);
        writer.Write(ActivationCost);

        Stats.Save(writer);
    }

    public override string ToString()
    {
        return string.Format("{0}: {1}", Id, Name);
    }

    public string ShowStats()
    {
        string text = string.Empty;

        foreach (var val in Stats.Values)
        {
            string action = val.Value < 0 ? "减少" : "增加";
            string statName = GetStatDescription(val.Key);
            string suffix = val.Key.ToString().Contains("Percent", StringComparison.Ordinal) ? "%" : "";
            text += $"{statName}{action}{val.Value}{suffix}.\n";
        }

        return text;
    }

    private static string GetStatDescription(Stat stat)
    {
        var field = typeof(Stat).GetField(stat.ToString());
        if (field == null)
            return stat.ToString();

        var attributes = (DescriptionAttribute[])field.GetCustomAttributes(typeof(DescriptionAttribute), false);
        return attributes.Length > 0 ? attributes[0].Description : stat.ToString();
    }
}

public class GuildBuff
{
    public int Id;
    public GuildBuffInfo Info;
    public bool Active = false;
    public int ActiveTimeRemaining;

    public bool UsingGuildSkillIcon
    {
        get { return Info != null && Info.Icon < 1000; }
    }

    public GuildBuff() { }

    public GuildBuff(BinaryReader reader)
    {
        Id = reader.ReadInt32();
        Active = reader.ReadBoolean();
        ActiveTimeRemaining = reader.ReadInt32();
    }
    public void Save(BinaryWriter writer)
    {
        writer.Write(Id);
        writer.Write(Active);
        writer.Write(ActiveTimeRemaining);
    }
}

//outdated but cant delete it or old db's wont load
public class GuildBuffOld
{
    public GuildBuffOld() { }

    public GuildBuffOld(BinaryReader reader)
    {
        reader.ReadByte();
        reader.ReadInt64();
    }
}
