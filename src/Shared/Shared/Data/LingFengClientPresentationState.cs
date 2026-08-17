using System.Drawing;

public readonly record struct LingFengScreenEffectKey(
    int X, int Y, int IconPackage, int StartIndex, int Layer);

public readonly record struct LingFengScreenEffectSnapshot(
    LingFengScreenEffectKey Key,
    int FrameCount,
    int LoopCount,
    int FrameDelay,
    int BlendMode,
    int Reserved);

public readonly record struct LingFengDialogSnapshot(
    int DialogId,
    int IconPackage,
    int ImageIndex,
    bool Movable,
    int X,
    int Y,
    int OffsetX,
    int OffsetY,
    int Position,
    string ExternalTextFile,
    bool AbsolutePath,
    bool NpcStyle,
    string LibraryName,
    bool ShowCloseButton,
    int CloseButtonX,
    int CloseButtonY,
    bool ContinueNpcStyle);

public sealed class LingFengClientPresentationState
{
    private readonly Dictionary<LingFengScreenEffectKey, LingFengScreenEffectSnapshot> _screenEffects = new();
    private readonly Dictionary<int, LingFengDialogSnapshot> _dialogs = new();

    public IReadOnlyDictionary<LingFengScreenEffectKey, LingFengScreenEffectSnapshot> ScreenEffects =>
        _screenEffects;
    public IReadOnlyDictionary<int, LingFengDialogSnapshot> Dialogs => _dialogs;

    public static Point ResolveNpcDialogLocation(
        Size viewport,
        Size dialog,
        int position,
        int offsetX,
        int offsetY)
    {
        int x = position is 1 or 3
            ? viewport.Width - dialog.Width
            : position == 4 ? (viewport.Width - dialog.Width) / 2 : 0;
        int y = position is 2 or 3
            ? viewport.Height - dialog.Height
            : position == 4 ? (viewport.Height - dialog.Height) / 2 : 0;
        return new Point(x + offsetX, y + offsetY);
    }

    public void Apply(ServerPackets.LingFengScreenEffect packet)
    {
        if (packet == null) throw new ArgumentNullException(nameof(packet));
        var key = new LingFengScreenEffectKey(
            packet.X, packet.Y, packet.IconPackage, packet.StartIndex, packet.Layer);
        if (packet.Stop)
        {
            _screenEffects.Remove(key);
            return;
        }

        _screenEffects[key] = new LingFengScreenEffectSnapshot(
            key, packet.FrameCount, packet.LoopCount, packet.FrameDelay,
            packet.BlendMode, packet.Reserved);
    }

    public void Apply(ServerPackets.LingFengDialog packet)
    {
        if (packet == null) throw new ArgumentNullException(nameof(packet));
        if (packet.Remove)
        {
            _dialogs.Remove(packet.DialogId);
            return;
        }

        _dialogs[packet.DialogId] = new LingFengDialogSnapshot(
            packet.DialogId, packet.IconPackage, packet.ImageIndex, packet.Movable,
            packet.X, packet.Y, packet.OffsetX, packet.OffsetY, packet.Position,
            packet.ExternalTextFile ?? string.Empty, packet.AbsolutePath,
            packet.NpcStyle, packet.LibraryName ?? string.Empty,
            packet.ShowCloseButton, packet.CloseButtonX, packet.CloseButtonY,
            packet.ContinueNpcStyle);
    }
}
