using System.Drawing;

public readonly record struct LingFengMapEffectPresentationPlan(
    int Duration,
    uint AnchorObjectId,
    Point PixelOffset,
    bool Repeat,
    long RepeatUntil)
{
    public static bool TryCreate(
        ServerPackets.LingFengMapEffect packet,
        long currentTime,
        out LingFengMapEffectPresentationPlan plan)
    {
        plan = default;
        if (packet == null || packet.StartIndex < 0 || packet.FrameCount <= 0 ||
            packet.FrameDelay <= 0 || packet.Layer > 2 || packet.Light > 5)
            return false;

        int duration;
        try
        {
            duration = checked(packet.FrameCount * packet.FrameDelay);
        }
        catch (OverflowException)
        {
            return false;
        }

        plan = new LingFengMapEffectPresentationPlan(
            duration,
            packet.AnchorObjectId,
            packet.PixelOffset,
            packet.RepeatCount <= 0 || packet.RepeatCount > 1,
            packet.RepeatCount > 1
                ? currentTime + (long)duration * packet.RepeatCount
                : 0);
        return true;
    }

    public T ResolveAnchor<T>(
        IEnumerable<T> objects,
        Func<T, uint> objectId)
        where T : class
    {
        if (AnchorObjectId == 0 || objects == null || objectId == null) return null;
        uint anchorObjectId = AnchorObjectId;
        return objects.FirstOrDefault(value => objectId(value) == anchorObjectId);
    }
}
