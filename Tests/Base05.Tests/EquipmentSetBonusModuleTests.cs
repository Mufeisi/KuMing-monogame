using Server.MirObjects;
using Xunit;

namespace Base05.Tests;

public sealed class EquipmentSetBonusModuleTests
{
    [Fact]
    public void 破碎套装按已装备部位叠加局部加成()
    {
        var stats = new Stats();
        var sets = new[]
        {
            new ItemSets
            {
                Set = ItemSet.破碎套装,
                Count = 3,
                Type = [ItemType.项链, ItemType.戒指, ItemType.手镯],
            },
        };

        EquipmentSetBonusModule.ApplyItemSetBonuses(stats, sets, []);

        Assert.Equal(1, stats[Stat.MinDC]);
        Assert.Equal(3, stats[Stat.MaxDC]);
        Assert.Equal(2, stats[Stat.攻击速度]);
    }

    [Fact]
    public void 圣龙双花双绿戒指不依赖左右顺序()
    {
        var leftFlower = CreateEquipment("双花圣龙戒指", ItemSet.圣龙套装, ItemType.戒指);
        var rightGreen = CreateEquipment("双绿圣龙戒指", ItemSet.圣龙套装, ItemType.戒指);

        Stats forward = ApplyHolyDragonRings(leftFlower, rightGreen);
        Stats reverse = ApplyHolyDragonRings(rightGreen, leftFlower);

        foreach (Stats stats in new[] { forward, reverse })
        {
            Assert.Equal(5, stats[Stat.MaxDC]);
            Assert.Equal(5, stats[Stat.MaxMC]);
            Assert.Equal(5, stats[Stat.MaxSC]);
        }
    }

    [Fact]
    public void 神龙完整套装同时应用部位与完整套装加成()
    {
        var stats = new Stats();
        var sets = new[]
        {
            new ItemSets
            {
                Set = ItemSet.神龙套装,
                Count = 4,
                Type = [ItemType.盔甲, ItemType.戒指, ItemType.手镯, ItemType.项链],
            },
        };

        EquipmentSetBonusModule.ApplyItemSetBonuses(stats, sets, []);

        Assert.Equal(8, stats[Stat.MaxDC]);
        Assert.Equal(8, stats[Stat.MaxMC]);
        Assert.Equal(8, stats[Stat.MaxSC]);
        Assert.Equal(20, stats[Stat.最大防御数率]);
    }

    [Fact]
    public void 完整强青玉套装应用固定加成表()
    {
        var stats = new Stats();
        EquipmentSetBonusModule.ApplyItemSetBonuses(stats,
        [
            new ItemSets
            {
                Set = ItemSet.强青玉套,
                Count = 5,
                Type = [],
            },
        ], []);

        Assert.Equal(1, stats[Stat.MinDC]);
        Assert.Equal(2, stats[Stat.MaxDC]);
        Assert.Equal(2, stats[Stat.MaxMC]);
        Assert.Equal(1, stats[Stat.准确]);
        Assert.Equal(50, stats[Stat.HP]);
    }

    [Fact]
    public void 破碎套装攻击速度分支保持原有提前结束语义()
    {
        var stats = new Stats();
        EquipmentSetBonusModule.ApplyItemSetBonuses(stats,
        [
            new ItemSets
            {
                Set = ItemSet.破碎套装,
                Count = 2,
                Type = [ItemType.戒指, ItemType.手镯],
            },
            new ItemSets
            {
                Set = ItemSet.世轮套装,
                Count = 2,
                Type = [],
            },
        ], []);

        Assert.Equal(2, stats[Stat.攻击速度]);
        Assert.Equal(0, stats[Stat.HP]);
    }

    [Fact]
    public void 天龙全套累积武器防具首饰与完整套装四层加成()
    {
        var stats = new Stats();
        EquipmentSetBonusModule.ApplyMirSetBonuses(stats,
        [
            EquipmentSlot.武器,
            EquipmentSlot.盔甲,
            EquipmentSlot.头盔,
            EquipmentSlot.靴子,
            EquipmentSlot.腰带,
            EquipmentSlot.项链,
            EquipmentSlot.左手镯,
            EquipmentSlot.右戒指,
        ]);

        Assert.Equal(15, stats[Stat.武器增伤]);
        Assert.Equal(2, stats[Stat.MinDC]);
        Assert.Equal(9, stats[Stat.MaxDC]);
        Assert.Equal(2, stats[Stat.MinMC]);
        Assert.Equal(9, stats[Stat.MaxMC]);
        Assert.Equal(2, stats[Stat.MinSC]);
        Assert.Equal(9, stats[Stat.MaxSC]);
        Assert.Equal(2, stats[Stat.MinAC]);
        Assert.Equal(6, stats[Stat.MaxAC]);
        Assert.Equal(1, stats[Stat.MinMAC]);
        Assert.Equal(4, stats[Stat.MaxMAC]);
        Assert.Equal(2, stats[Stat.幸运]);
        Assert.Equal(100, stats[Stat.HP]);
        Assert.Equal(100, stats[Stat.MP]);
        Assert.Equal(50, stats[Stat.腕力负重]);
        Assert.Equal(30, stats[Stat.装备负重]);
        Assert.Equal(60, stats[Stat.背包负重]);
        Assert.Equal(2, stats[Stat.攻击速度]);
        Assert.Equal(2, stats[Stat.中毒恢复]);
    }

    private static Stats ApplyHolyDragonRings(UserItem left, UserItem right)
    {
        var equipment = new UserItem[Enum.GetValues<EquipmentSlot>().Length];
        equipment[(int)EquipmentSlot.左戒指] = left;
        equipment[(int)EquipmentSlot.右戒指] = right;
        var stats = new Stats();
        var sets = new[]
        {
            new ItemSets
            {
                Set = ItemSet.圣龙套装,
                Count = 2,
                Type = [ItemType.戒指],
            },
        };

        EquipmentSetBonusModule.ApplyItemSetBonuses(stats, sets, equipment);
        return stats;
    }

    private static UserItem CreateEquipment(string name, ItemSet set, ItemType type) =>
        new(new ItemInfo
        {
            Name = name,
            Set = set,
            Type = type,
        });
}
