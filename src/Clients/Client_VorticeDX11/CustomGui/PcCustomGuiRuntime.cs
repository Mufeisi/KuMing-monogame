#nullable enable

using Client.MirControls;
using Shared.CustomGui;

namespace Client.CustomGui;

internal static class PcCustomGuiRuntime
{
    private static CustomGuiAcceptedPackage? _package;
    private static PcCustomGuiHost? _host;
    private static CustomGuiClientStateSession? _session;

    internal static bool IsOpen => _session?.IsOpen == true;
    internal static uint StateRevision => _session?.StateRevision ?? 0;

    internal static void RegisterAcceptedPackage(CustomGuiAcceptedPackage package)
    {
        ArgumentNullException.ThrowIfNull(package);
        if (_package is not null && package.Sequence < _package.Sequence)
            throw new CustomGuiStateProjectionException("GUI10-STATE-PACKAGE", "拒绝降级已接受 GUI 包");
        CloseCurrent();
        _package = package;
    }

    internal static void Process(Packet packet, MirScene scene)
    {
        ArgumentNullException.ThrowIfNull(packet);
        ArgumentNullException.ThrowIfNull(scene);
        try
        {
            switch (packet)
            {
                case ServerPackets.CustomGuiOpen open: Open(open, scene); break;
                case ServerPackets.CustomGuiStateDelta delta: Delta(delta); break;
                case ServerPackets.CustomGuiActionResult result:
                    (_session ?? throw new CustomGuiStateProjectionException("GUI10-STATE-CLOSED", "动态窗口尚未打开"))
                        .AcceptActionResult(result.WindowInstanceId, result.RequestSequence, result.StateRevision, result.Result, result.Message);
                    break;
                case ServerPackets.CustomGuiClose close: Close(close); break;
            }
        }
        catch (CustomGuiStateProjectionException error) { CMain.SaveError(error.Message); }
        catch (Exception error) { CMain.SaveError($"GUI10-STATE-TARGET：{error.GetType().Name}"); }
    }

    internal static void Reset() { CloseCurrent(); _package = null; }
    internal static void CloseAllWindows() => CloseCurrent();

    private static void Open(ServerPackets.CustomGuiOpen packet, MirScene scene)
    {
        CustomGuiAcceptedPackage package = _package
            ?? throw new CustomGuiStateProjectionException("GUI10-STATE-PACKAGE", "客户端尚未接受签名 GUI 包");
        var replacementHost = PcCustomGuiAdapter.Create(package.Document, scene.Size);
        try
        {
            var replacementSession = new CustomGuiClientStateSession(package.Document, package.Sequence, replacementHost);
            replacementSession.Open(new CustomGuiOpenState(packet.WindowInstanceId, packet.DocumentId, packet.DocumentRevision,
                packet.PackageSequence, packet.SessionNonce, packet.ExpiresAtUnixMilliseconds, packet.StateRevision, packet.State));
            replacementHost.AttachTo(scene);
            CloseCurrent();
            _host = replacementHost;
            _session = replacementSession;
        }
        catch { replacementHost.Dispose(); throw; }
    }

    private static void Delta(ServerPackets.CustomGuiStateDelta packet) =>
        (_session ?? throw new CustomGuiStateProjectionException("GUI10-STATE-CLOSED", "动态窗口尚未打开"))
        .ApplyDelta(new CustomGuiDeltaState(packet.WindowInstanceId, packet.DocumentId, packet.DocumentRevision,
            packet.PackageSequence, packet.SessionNonce, packet.StateRevision, packet.State));

    private static void Close(ServerPackets.CustomGuiClose packet)
    {
        if (_session?.Close(packet.WindowInstanceId) == true) CloseCurrent();
    }

    private static void CloseCurrent() { _host?.Dispose(); _host = null; _session = null; }
}
