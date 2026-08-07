using System.Drawing;
using MonoShare;
using Xunit;

namespace Base05.Tests;

public sealed class MobileFishingInventoryPolicyTests
{
    [Theory]
    [InlineData(0, ItemType.鱼钩)]
    [InlineData(1, ItemType.鱼漂)]
    [InlineData(2, ItemType.鱼饵)]
    [InlineData(3, ItemType.探鱼器)]
    [InlineData(4, ItemType.摇轮)]
    public void Slot_candidates_use_the_corresponding_fishing_type(int slot, ItemType expected)
    {
        Assert.Equal(expected, MobileFishingInventoryPolicy.ExpectedItemType(slot));

        UserItem valid = Item((ulong)(100 + slot), expected, 1, 20);
        UserItem wrong = Item((ulong)(200 + slot), ItemType.药水, 1, 20);
        UserItem empty = Item((ulong)(300 + slot), expected, 0, 20);
        Assert.True(MobileFishingInventoryPolicy.IsValidCandidate(valid, slot));
        Assert.False(MobileFishingInventoryPolicy.IsValidCandidate(wrong, slot));
        Assert.False(MobileFishingInventoryPolicy.IsValidCandidate(empty, slot));
    }

    [Fact]
    public void Paging_skips_invalid_first_entry_and_keeps_later_legal_candidates()
    {
        UserItem[] inventory =
        {
            Item(1, ItemType.药水, 1, 20),
            Item(2, ItemType.鱼钩, 1, 20),
            Item(3, ItemType.鱼钩, 1, 20),
            Item(4, ItemType.鱼漂, 1, 20),
            Item(5, ItemType.鱼钩, 1, 20),
        };

        var first = MobileFishingInventoryPolicy.GetCandidates(
            inventory, (int)FishingSlot.Hook, pageIndex: 0, pageSize: 2,
            out bool previous, out bool next);
        Assert.False(previous);
        Assert.True(next);
        Assert.Equal(new[] { 1, 2 }, first.Select(c => c.InventoryIndex).ToArray());

        var second = MobileFishingInventoryPolicy.GetCandidates(
            inventory, (int)FishingSlot.Hook, pageIndex: 1, pageSize: 2,
            out previous, out next);
        Assert.True(previous);
        Assert.False(next);
        Assert.Single(second);
        Assert.Equal(4, second[0].InventoryIndex);
    }

    [Fact]
    public void Bait_picker_requires_same_item_info_and_room_before_merge()
    {
        UserItem source = Item(10, ItemType.鱼饵, 3, 20, infoIndex: 501);
        UserItem same = Item(11, ItemType.鱼饵, 4, 20, infoIndex: 501);
        UserItem different = Item(12, ItemType.鱼饵, 4, 20, infoIndex: 502);
        UserItem full = Item(13, ItemType.鱼饵, 20, 20, infoIndex: 501);

        Assert.True(MobileFishingInventoryPolicy.CanMergeBait(source, same));
        Assert.False(MobileFishingInventoryPolicy.CanMergeBait(source, different));
        Assert.False(MobileFishingInventoryPolicy.CanMergeBait(source, full));
    }

    [Fact]
    public void Authoritative_bait_merge_updates_target_and_consumes_source_stack()
    {
        UserItem source = Item(10, ItemType.鱼饵, 3, 20, infoIndex: 501);
        UserItem target = Item(11, ItemType.鱼饵, 19, 20, infoIndex: 501);
        UserItem[] inventory = { source };
        UserItem[] fishingSlots = { target };

        Assert.True(MobileFishingInventoryPolicy.TryApplyBaitMerge(inventory, fishingSlots, 10, 11));
        Assert.Equal((ushort)2, inventory[0].Count);
        Assert.Equal((ushort)20, target.Count);

        UserItem sourcePartial = Item(12, ItemType.鱼饵, 5, 20, infoIndex: 501);
        UserItem targetPartial = Item(13, ItemType.鱼饵, 18, 20, infoIndex: 501);
        Assert.True(MobileFishingInventoryPolicy.TryApplyBaitMerge(
            new[] { sourcePartial }, new[] { targetPartial }, 12, 13));
        Assert.Equal((ushort)20, targetPartial.Count);
        Assert.Equal((ushort)3, sourcePartial.Count);
    }

    [Fact]
    public void Authoritative_bait_merge_clears_source_when_remaining_equals_source_stack()
    {
        UserItem source = Item(14, ItemType.鱼饵, 2, 20, infoIndex: 501);
        UserItem target = Item(15, ItemType.鱼饵, 18, 20, infoIndex: 501);
        UserItem[] inventory = { source };
        UserItem[] fishingSlots = { target };

        Assert.True(MobileFishingInventoryPolicy.TryApplyBaitMerge(inventory, fishingSlots, 14, 15));
        Assert.Null(inventory[0]);
        Assert.Equal((ushort)20, target.Count);
    }

    [Fact]
    public void Stale_unique_id_is_rejected_before_picker_action()
    {
        UserItem[] inventory = { Item(10, ItemType.鱼钩, 1, 20) };
        Assert.False(MobileFishingInventoryPolicy.TryGetCurrentCandidate(
            inventory, inventoryIndex: 0, fishingSlot: (int)FishingSlot.Hook,
            expectedUniqueId: 99, out _));
        Assert.True(MobileFishingInventoryPolicy.TryGetCurrentCandidate(
            inventory, inventoryIndex: 0, fishingSlot: (int)FishingSlot.Hook,
            expectedUniqueId: 10, out UserItem item));
        Assert.Equal((ulong)10, item.UniqueID);
        Assert.True(MobileFishingInventoryPolicy.MatchesInventoryIdentity(inventory, 0, 10));
        Assert.False(MobileFishingInventoryPolicy.MatchesInventoryIdentity(inventory, 0, 99));
        Assert.False(MobileFishingInventoryPolicy.MatchesInventoryIdentity(inventory, 1, 10));
    }

    [Fact]
    public void Picker_bounds_stay_inside_small_and_wide_roots()
    {
        foreach ((float Width, float Height) size in new[] { (320F, 480F), (1280F, 720F) })
        {
            RectangleF bounds = MobileFishingInventoryPolicy.GetPickerBounds(size.Width, size.Height);
            Assert.True(bounds.Left >= 0 && bounds.Top >= 0);
            Assert.True(bounds.Right <= size.Width + 0.01F);
            Assert.True(bounds.Bottom <= size.Height + 0.01F);

            for (int i = 0; i < MobileFishingInventoryPolicy.DefaultPageSize; i++)
            {
                RectangleF item = MobileFishingInventoryPolicy.GetPickerItemBounds(bounds, i);
                Assert.True(item.Left >= bounds.Left && item.Right <= bounds.Right + 0.01F);
                Assert.True(item.Top >= bounds.Top && item.Bottom <= bounds.Bottom + 0.01F);
            }
        }
    }

    private static UserItem Item(ulong uniqueId, ItemType type, ushort count, ushort stackSize, int infoIndex = 0)
    {
        return new UserItem(new ItemInfo
        {
            Index = infoIndex,
            Type = type,
            StackSize = stackSize,
            Name = type.ToString(),
        })
        {
            UniqueID = uniqueId,
            ItemIndex = infoIndex,
            Count = count,
        };
    }
}
