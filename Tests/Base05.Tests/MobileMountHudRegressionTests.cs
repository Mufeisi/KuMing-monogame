using MonoShare;
using MonoShare.MirScenes;
using Xunit;

namespace Base05.Tests;

public sealed class MobileMountHudRegressionTests
{
    [Theory]
    [InlineData(320F, 640F)]
    [InlineData(720F, 1280F)]
    public void Mobile_hud_fallback_buttons_stay_in_safe_area_and_do_not_overlap(
        float rootWidth, float rootHeight)
    {
        MobileMainHudFallbackBounds mentor = MobileMainHudFallbackLayout.Mentor(rootWidth, rootHeight);
        MobileMainHudFallbackBounds relationship = MobileMainHudFallbackLayout.Relationship(rootWidth, rootHeight);
        MobileMainHudFallbackBounds mount = MobileMainHudFallbackLayout.Mount(rootWidth, rootHeight);

        AssertSafeArea(mentor, rootWidth, rootHeight);
        AssertSafeArea(relationship, rootWidth, rootHeight);
        AssertSafeArea(mount, rootWidth, rootHeight);

        Assert.False(Overlaps(mentor, relationship));
        Assert.False(Overlaps(mentor, mount));
        Assert.False(Overlaps(relationship, mount));
    }

    [Fact]
    public void Ordinary_repairs_do_not_mark_the_mount_slot_as_repaired()
    {
        Assert.True(MobileMountState.IsEquippedMountRepair(42, 42));
        Assert.False(MobileMountState.IsEquippedMountRepair(41, 42));
        Assert.False(MobileMountState.IsEquippedMountRepair(42, 0));
        Assert.False(MobileMountState.IsEquippedMountRepair(0, 42));
    }

    [Fact]
    public void Mount_accessory_unload_uses_package_region_and_rejects_full_package()
    {
        var inventory = new UserItem[6];
        for (int i = 2; i < inventory.Length; i++)
            inventory[i] = OccupiedItem();

        // Hotbar slots 0 and 1 are empty, but must not be selected when the
        // package region is full.
        Assert.Equal(-1, MobileMountInventoryPolicy.FindFirstEmptyPackageSlot(inventory, beltIdx: 2));

        inventory[2] = null;
        Assert.Equal(2, MobileMountInventoryPolicy.FindFirstEmptyPackageSlot(inventory, beltIdx: 2));
    }

    [Fact]
    public void Ride_button_eligibility_turns_on_after_cooldown_without_content_dirty()
    {
        Assert.False(MobileMountState.CanToggleAt(
            nowMs: 1_000,
            dead: false,
            mountType: 7,
            mountTime: 900,
            currentAction: MirAction.Standing,
            pendingRequest: false));

        Assert.True(MobileMountState.CanToggleAt(
            nowMs: 1_400,
            dead: false,
            mountType: 7,
            mountTime: 900,
            currentAction: MirAction.Standing,
            pendingRequest: false));
    }

    [Fact]
    public void Ride_button_eligibility_recovers_when_moving_returns_to_standing()
    {
        Assert.False(MobileMountState.CanToggleAt(
            nowMs: 2_000,
            dead: false,
            mountType: 7,
            mountTime: 1_000,
            currentAction: MirAction.Walking,
            pendingRequest: false));

        Assert.True(MobileMountState.CanToggleAt(
            nowMs: 2_000,
            dead: false,
            mountType: 7,
            mountTime: 1_000,
            currentAction: MirAction.Standing,
            pendingRequest: false));
    }

    private static void AssertSafeArea(
        MobileMainHudFallbackBounds bounds, float rootWidth, float rootHeight)
    {
        const float margin = 12F;
        Assert.True(bounds.X >= margin);
        Assert.True(bounds.Y >= margin);
        Assert.True(bounds.X + bounds.Width <= rootWidth - margin);
        Assert.True(bounds.Y + bounds.Height <= rootHeight - margin);
    }

    private static bool Overlaps(MobileMainHudFallbackBounds first, MobileMainHudFallbackBounds second)
    {
        return first.X < second.X + second.Width &&
               second.X < first.X + first.Width &&
               first.Y < second.Y + second.Height &&
               second.Y < first.Y + first.Height;
    }

    private static UserItem OccupiedItem() => new(new ItemInfo());
}
