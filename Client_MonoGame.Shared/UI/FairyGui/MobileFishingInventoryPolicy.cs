using System;
using System.Collections.Generic;
using System.Drawing;

namespace MonoShare;

/// <summary>
/// Shared candidate and layout policy for the mobile fishing picker.
/// Keeping this decision layer independent of FairyGUI makes stale inventory
/// filtering and the compact picker page bounds testable without a running UI.
/// </summary>
internal static class MobileFishingInventoryPolicy
{
    internal const int SlotCount = 5;
    internal const int DefaultPageSize = 4;

    internal readonly struct Candidate
    {
        internal Candidate(int inventoryIndex, UserItem item)
        {
            InventoryIndex = inventoryIndex;
            UniqueId = item?.UniqueID ?? 0;
            Name = item?.FriendlyName ?? string.Empty;
            Count = item?.Count ?? 0;
        }

        internal int InventoryIndex { get; }
        internal ulong UniqueId { get; }
        internal string Name { get; }
        internal ushort Count { get; }
    }

    internal static ItemType ExpectedItemType(int fishingSlot)
    {
        return fishingSlot switch
        {
            (int)FishingSlot.Hook => ItemType.鱼钩,
            (int)FishingSlot.Float => ItemType.鱼漂,
            (int)FishingSlot.Bait => ItemType.鱼饵,
            (int)FishingSlot.Finder => ItemType.探鱼器,
            (int)FishingSlot.Reel => ItemType.摇轮,
            _ => ItemType.Nothing,
        };
    }

    internal static bool IsValidSlot(int fishingSlot) =>
        fishingSlot >= 0 && fishingSlot < SlotCount && ExpectedItemType(fishingSlot) != ItemType.Nothing;

    internal static bool IsValidCandidate(UserItem item, int fishingSlot)
    {
        ItemType expected = ExpectedItemType(fishingSlot);
        return item != null && item.UniqueID != 0 && item.Count > 0 && item.Info != null &&
               expected != ItemType.Nothing && item.Info.Type == expected;
    }

    /// <summary>
    /// Returns only legal inventory candidates. Invalid entries are skipped
    /// before paging, so an invalid first item cannot hide a later valid one.
    /// </summary>
    internal static IReadOnlyList<Candidate> GetCandidates(UserItem[] inventory, int fishingSlot,
        int pageIndex, int pageSize, out bool hasPrevious, out bool hasNext)
    {
        pageSize = Math.Clamp(pageSize, 1, 16);
        pageIndex = Math.Max(0, pageIndex);
        int skip = pageIndex * pageSize;
        int validIndex = 0;
        var result = new List<Candidate>(pageSize);

        if (inventory != null && IsValidSlot(fishingSlot))
        {
            for (int i = 0; i < inventory.Length; i++)
            {
                UserItem item = inventory[i];
                if (!IsValidCandidate(item, fishingSlot))
                    continue;

                if (validIndex >= skip && result.Count < pageSize)
                    result.Add(new Candidate(i, item));
                validIndex++;
            }
        }

        hasPrevious = pageIndex > 0 && validIndex > 0;
        hasNext = validIndex > skip + result.Count;
        return result;
    }

    /// <summary>Re-checks both slot and UniqueID immediately before sending.</summary>
    internal static bool TryGetCurrentCandidate(UserItem[] inventory, int inventoryIndex,
        int fishingSlot, ulong expectedUniqueId, out UserItem item)
    {
        item = null;
        if (inventory == null || inventoryIndex < 0 || inventoryIndex >= inventory.Length || expectedUniqueId == 0)
            return false;

        UserItem current = inventory[inventoryIndex];
        if (current == null || current.UniqueID != expectedUniqueId || !IsValidCandidate(current, fishingSlot))
            return false;

        item = current;
        return true;
    }

    internal static bool MatchesInventoryIdentity(UserItem[] inventory, int inventoryIndex, ulong expectedUniqueId)
    {
        return inventory != null && inventoryIndex >= 0 && inventoryIndex < inventory.Length &&
               expectedUniqueId != 0 && inventory[inventoryIndex]?.UniqueID == expectedUniqueId;
    }

    internal static bool IsSameItemInfo(UserItem source, UserItem target)
    {
        if (source?.Info == null || target?.Info == null)
            return false;

        if (ReferenceEquals(source.Info, target.Info))
            return true;

        return source.Info.Index == target.Info.Index && source.Info.Type == target.Info.Type;
    }

    internal static bool CanMergeBait(UserItem source, UserItem target)
    {
        return IsValidCandidate(source, (int)FishingSlot.Bait) &&
               IsValidCandidate(target, (int)FishingSlot.Bait) &&
               IsSameItemInfo(source, target) && target.Count < target.Info.StackSize;
    }

    /// <summary>
    /// Applies an authoritative fishing merge to the two local arrays. This
    /// is intentionally a pure inventory seam so the packet handler can
    /// refresh the picker only after the source/target counts are reconciled.
    /// </summary>
    internal static bool TryApplyBaitMerge(UserItem[] inventory, UserItem[] fishingSlots,
        ulong sourceUniqueId, ulong targetUniqueId)
    {
        if (inventory == null || fishingSlots == null || sourceUniqueId == 0 || targetUniqueId == 0)
            return false;

        int sourceIndex = -1;
        UserItem source = null;
        for (int i = 0; i < inventory.Length; i++)
        {
            UserItem item = inventory[i];
            if (item == null || item.UniqueID != sourceUniqueId)
                continue;

            sourceIndex = i;
            source = item;
            break;
        }

        UserItem target = null;
        for (int i = 0; i < fishingSlots.Length; i++)
        {
            UserItem item = fishingSlots[i];
            if (item != null && item.UniqueID == targetUniqueId)
            {
                target = item;
                break;
            }
        }

        if (sourceIndex < 0 || !CanMergeBait(source, target))
            return false;

        int remaining = target.Info.StackSize - target.Count;
        if (source.Count <= remaining)
        {
            target.Count += source.Count;
            inventory[sourceIndex] = null;
        }
        else
        {
            source.Count -= (ushort)remaining;
            target.Count = target.Info.StackSize;
        }

        return true;
    }

    internal static RectangleF GetPickerBounds(float rootWidth, float rootHeight)
    {
        float width = Math.Max(1F, rootWidth);
        float height = Math.Max(1F, rootHeight);
        float pickerWidth = Math.Min(Math.Max(320F, width - 48F), 620F);
        float pickerHeight = Math.Min(Math.Max(300F, height - 48F), 430F);
        pickerWidth = Math.Min(pickerWidth, width);
        pickerHeight = Math.Min(pickerHeight, height);
        return new RectangleF((width - pickerWidth) / 2F, (height - pickerHeight) / 2F,
            pickerWidth, pickerHeight);
    }

    internal static RectangleF GetPickerItemBounds(RectangleF pickerBounds, int itemIndex)
    {
        if (itemIndex < 0 || itemIndex >= DefaultPageSize)
            return RectangleF.Empty;

        const float margin = 20F;
        const float gap = 10F;
        float itemHeight = 54F;
        float y = 70F + itemIndex * (itemHeight + gap);
        float width = Math.Max(1F, pickerBounds.Width - margin * 2F);
        return new RectangleF(pickerBounds.X + margin, pickerBounds.Y + y, width, itemHeight);
    }
}
