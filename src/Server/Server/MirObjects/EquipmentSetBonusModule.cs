namespace Server.MirObjects;

/// <summary>
/// 装备套装加成深模块：集中普通套装、圣龙戒指组合和天龙部位组合的累积规则。
/// </summary>
internal static class EquipmentSetBonusModule
{
    internal static void ApplyItemSetBonuses(
        Stats stats,
        IEnumerable<ItemSets> itemSets,
        IReadOnlyList<UserItem> equipment)
    {
        foreach (ItemSets set in itemSets)
        {
            if (set.Set == ItemSet.破碎套装)
            {
                if (set.Type.Contains(ItemType.项链) && set.Type.Contains(ItemType.戒指) && set.Type.Contains(ItemType.手镯))
                {
                    stats[Stat.MinDC] += 1;
                    stats[Stat.MaxDC] += 3;
                }
                if (set.Type.Contains(ItemType.戒指) && set.Type.Contains(ItemType.手镯))
                {
                    stats[Stat.攻击速度] += 2;
                    return;
                }
            }

            if (set.Set == ItemSet.灵玉套装 && set.Type.Contains(ItemType.戒指) && set.Type.Contains(ItemType.手镯))
                stats[Stat.神圣] += 3;

            if (set.Set == ItemSet.幻魔石套 && set.Type.Contains(ItemType.戒指) && set.Type.Contains(ItemType.手镯))
            {
                stats[Stat.装备负重] += 5;
                stats[Stat.背包负重] += 20;
            }

            if (set.Set == ItemSet.鏃未套装 && set.Type.Contains(ItemType.项链) && set.Type.Contains(ItemType.手镯))
                stats[Stat.HP] += 25;

            if (set.Set == ItemSet.圣龙套装)
            {
                UserItem leftRing = equipment[(int)EquipmentSlot.左戒指];
                UserItem rightRing = equipment[(int)EquipmentSlot.右戒指];
                if (leftRing != null && rightRing != null)
                {
                    bool activateRing =
                        leftRing.Info.Name.StartsWith("双花") && leftRing.Info.Set == ItemSet.圣龙套装 &&
                        rightRing.Info.Name.StartsWith("双绿") && rightRing.Info.Set == ItemSet.圣龙套装 ||
                        rightRing.Info.Name.StartsWith("双花") && rightRing.Info.Set == ItemSet.圣龙套装 &&
                        leftRing.Info.Name.StartsWith("双绿") && leftRing.Info.Set == ItemSet.圣龙套装;

                    if (activateRing)
                    {
                        stats[Stat.MaxDC] += 5;
                        stats[Stat.MaxMC] += 5;
                        stats[Stat.MaxSC] += 5;
                        return;
                    }
                }
            }

            if (set.Set == ItemSet.神龙套装)
            {
                if (set.Type.Contains(ItemType.戒指) && set.Type.Contains(ItemType.项链))
                {
                    stats[Stat.MaxDC] += 8;
                    stats[Stat.MaxMC] += 8;
                    stats[Stat.MaxSC] += 8;
                }
                if (set.Type.Contains(ItemType.盔甲) && set.Type.Contains(ItemType.戒指) && set.Type.Contains(ItemType.手镯) && set.Type.Contains(ItemType.项链))
                    stats[Stat.最大防御数率] += 20;
            }

            if (!set.SetComplete) continue;

            switch (set.Set)
            {
                case ItemSet.世轮套装:
                    stats[Stat.HP] += 50;
                    break;
                case ItemSet.绿翠套装:
                    stats[Stat.MP] += 50;
                    break;
                case ItemSet.道护套装:
                    stats[Stat.HP] += 30;
                    stats[Stat.MP] += 30;
                    break;
                case ItemSet.赤兰套装:
                    stats[Stat.准确] += 2;
                    stats[Stat.吸血数率] += 10;
                    break;
                case ItemSet.密火套装:
                    stats[Stat.HP] += 50;
                    stats[Stat.MP] -= 50;
                    break;
                case ItemSet.幻魔石套:
                    stats[Stat.MinMC] += 1;
                    stats[Stat.MaxMC] += 2;
                    break;
                case ItemSet.灵玉套装:
                    stats[Stat.MinSC] += 1;
                    stats[Stat.MaxSC] += 2;
                    break;
                case ItemSet.五玄套装:
                    stats[Stat.生命值数率] += 30;
                    stats[Stat.MinAC] += 2;
                    stats[Stat.MaxAC] += 2;
                    break;
                case ItemSet.祈祷套装:
                    stats[Stat.MinDC] += 2;
                    stats[Stat.MaxDC] += 5;
                    stats[Stat.攻击速度] += 2;
                    break;
                case ItemSet.白骨套装:
                    stats[Stat.MaxAC] += 2;
                    stats[Stat.MaxMC] += 1;
                    stats[Stat.MaxSC] += 1;
                    break;
                case ItemSet.虫血套装:
                    stats[Stat.MaxDC] += 1;
                    stats[Stat.MaxMC] += 1;
                    stats[Stat.MaxSC] += 1;
                    stats[Stat.MaxMAC] += 1;
                    stats[Stat.毒物躲避] += 1;
                    break;
                case ItemSet.白金套装:
                    stats[Stat.MaxDC] += 2;
                    stats[Stat.MaxAC] += 2;
                    break;
                case ItemSet.强白金套:
                    stats[Stat.MaxDC] += 3;
                    stats[Stat.HP] += 30;
                    stats[Stat.攻击速度] += 2;
                    break;
                case ItemSet.红玉套装:
                    stats[Stat.MaxMC] += 2;
                    stats[Stat.MaxMAC] += 2;
                    break;
                case ItemSet.强红玉套:
                    stats[Stat.MaxMC] += 2;
                    stats[Stat.MP] += 40;
                    stats[Stat.敏捷] += 2;
                    break;
                case ItemSet.软玉套装:
                    stats[Stat.MaxSC] += 2;
                    stats[Stat.MaxAC] += 1;
                    stats[Stat.MaxMAC] += 1;
                    break;
                case ItemSet.强软玉套:
                    stats[Stat.MaxSC] += 2;
                    stats[Stat.HP] += 15;
                    stats[Stat.MP] += 20;
                    stats[Stat.神圣] += 1;
                    stats[Stat.准确] += 1;
                    break;
                case ItemSet.贵人战套:
                    stats[Stat.MaxDC] += 1;
                    stats[Stat.背包负重] += 25;
                    break;
                case ItemSet.贵人法套:
                    stats[Stat.MaxMC] += 1;
                    stats[Stat.背包负重] += 17;
                    break;
                case ItemSet.贵人道套:
                    stats[Stat.MaxSC] += 1;
                    stats[Stat.背包负重] += 17;
                    break;
                case ItemSet.贵人刺套:
                    stats[Stat.MaxDC] += 1;
                    stats[Stat.背包负重] += 20;
                    break;
                case ItemSet.贵人弓套:
                    stats[Stat.MaxDC] += 1;
                    stats[Stat.背包负重] += 17;
                    break;
                case ItemSet.龙血套装:
                    stats[Stat.MaxSC] += 2;
                    stats[Stat.HP] += 15;
                    stats[Stat.MP] += 20;
                    stats[Stat.神圣] += 1;
                    stats[Stat.准确] += 1;
                    break;
                case ItemSet.监视套装:
                    stats[Stat.魔法躲避] += 1;
                    stats[Stat.毒物躲避] += 1;
                    break;
                case ItemSet.暴压套装:
                    stats[Stat.MaxAC] += 1;
                    stats[Stat.敏捷] += 1;
                    break;
                case ItemSet.青玉套装:
                    stats[Stat.MinDC] += 1;
                    stats[Stat.MaxDC] += 1;
                    stats[Stat.MinMC] += 1;
                    stats[Stat.MaxMC] += 1;
                    stats[Stat.腕力负重] += 1;
                    stats[Stat.装备负重] += 2;
                    break;
                case ItemSet.强青玉套:
                    stats[Stat.MinDC] += 1;
                    stats[Stat.MaxDC] += 2;
                    stats[Stat.MaxMC] += 2;
                    stats[Stat.准确] += 1;
                    stats[Stat.HP] += 50;
                    break;
                case ItemSet.鏃未套装:
                    stats[Stat.MP] += 25;
                    stats[Stat.攻击速度] += 2;
                    break;
            }
        }
    }

    internal static void ApplyMirSetBonuses(Stats stats, IReadOnlyCollection<EquipmentSlot> equippedSlots)
    {
        if (equippedSlots.Contains(EquipmentSlot.武器) && equippedSlots.Contains(EquipmentSlot.盔甲))
            stats[Stat.武器增伤] += 15;

        if (equippedSlots.Contains(EquipmentSlot.头盔) && equippedSlots.Contains(EquipmentSlot.靴子) && equippedSlots.Contains(EquipmentSlot.腰带))
        {
            stats[Stat.MaxDC] += 3;
            stats[Stat.MaxMC] += 3;
            stats[Stat.MaxSC] += 3;
            stats[Stat.腕力负重] += 20;
        }

        if (equippedSlots.Contains(EquipmentSlot.项链) &&
            (equippedSlots.Contains(EquipmentSlot.左手镯) || equippedSlots.Contains(EquipmentSlot.右手镯)) &&
            (equippedSlots.Contains(EquipmentSlot.左戒指) || equippedSlots.Contains(EquipmentSlot.右戒指)))
        {
            stats[Stat.MinDC] += 2;
            stats[Stat.MaxDC] += 6;
            stats[Stat.MinMC] += 2;
            stats[Stat.MaxMC] += 6;
            stats[Stat.MinSC] += 2;
            stats[Stat.MaxSC] += 6;
            stats[Stat.攻击速度] += 2;
            stats[Stat.背包负重] += 60;
            stats[Stat.装备负重] += 30;
            stats[Stat.腕力负重] += 30;
        }

        if (equippedSlots.Contains(EquipmentSlot.盔甲) &&
            equippedSlots.Contains(EquipmentSlot.武器) &&
            equippedSlots.Contains(EquipmentSlot.头盔) &&
            equippedSlots.Contains(EquipmentSlot.靴子) &&
            equippedSlots.Contains(EquipmentSlot.腰带) &&
            equippedSlots.Contains(EquipmentSlot.项链) &&
            (equippedSlots.Contains(EquipmentSlot.左手镯) || equippedSlots.Contains(EquipmentSlot.右手镯)) &&
            (equippedSlots.Contains(EquipmentSlot.左戒指) || equippedSlots.Contains(EquipmentSlot.右戒指)))
        {
            stats[Stat.MinAC] += 2;
            stats[Stat.MaxAC] += 6;
            stats[Stat.MinMAC] += 1;
            stats[Stat.MaxMAC] += 4;
            stats[Stat.幸运] += 2;
            stats[Stat.HP] += 100;
            stats[Stat.MP] += 100;
            stats[Stat.中毒恢复] += 2;
        }
    }
}
