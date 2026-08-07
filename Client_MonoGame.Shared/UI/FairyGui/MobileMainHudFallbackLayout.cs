using System;

namespace MonoShare;

/// <summary>
/// Bounds for Android HUD fallback actions that are created when a
/// published package does not expose a matching target.  Keeping the offsets
/// in one small seam makes the fallback layout reviewable without instantiating
/// FairyGUI objects.
/// </summary>
internal readonly struct MobileMainHudFallbackBounds
{
    internal MobileMainHudFallbackBounds(float x, float y, float width, float height)
    {
        X = x;
        Y = y;
        Width = width;
        Height = height;
    }

    internal float X { get; }
    internal float Y { get; }
    internal float Width { get; }
    internal float Height { get; }
}

internal static class MobileMainHudFallbackLayout
{
    internal const float ButtonWidth = 112F;
    internal const float ButtonHeight = 48F;

    internal static MobileMainHudFallbackBounds Mentor(float rootWidth, float rootHeight) =>
        Create(rootWidth, rootHeight, bottomOffset: 16F);

    internal static MobileMainHudFallbackBounds Relationship(float rootWidth, float rootHeight) =>
        Create(rootWidth, rootHeight, bottomOffset: 74F);

    // Keep an 8 px gap above the relationship fallback while preserving the
    // existing bottom-right safe-area margin used by the other two buttons.
    internal static MobileMainHudFallbackBounds Mount(float rootWidth, float rootHeight) =>
        Create(rootWidth, rootHeight, bottomOffset: 130F);

    // Keep the seal/rental entry below the mount fallback so each generated
    // action remains independently touchable on packages without a matching
    // published HUD button.
    internal static MobileMainHudFallbackBounds SealRental(float rootWidth, float rootHeight) =>
        Create(rootWidth, rootHeight, bottomOffset: 186F);

    internal static MobileMainHudFallbackBounds Fishing(float rootWidth, float rootHeight) =>
        Create(rootWidth, rootHeight, bottomOffset: 242F);

    private static MobileMainHudFallbackBounds Create(float rootWidth, float rootHeight, float bottomOffset)
    {
        float x = Math.Max(12F, rootWidth - ButtonWidth - 16F);
        float y = Math.Max(12F, rootHeight - ButtonHeight - bottomOffset);
        return new MobileMainHudFallbackBounds(x, y, ButtonWidth, ButtonHeight);
    }
}
