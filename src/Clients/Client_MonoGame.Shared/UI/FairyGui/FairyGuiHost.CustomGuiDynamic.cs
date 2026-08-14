#if ANDROID || IOS
using MonoShare.CustomGui;
using Shared.CustomGui;

namespace MonoShare;

internal static partial class FairyGuiHost
{
    private static CustomGuiAcceptedPackage? _customGuiPackage;
    private static MobileCustomGuiHost? _customGuiHost;
    private static CustomGuiClientStateSession? _customGuiSession;

    internal static void RegisterAcceptedCustomGuiPackage(CustomGuiAcceptedPackage package)
    {
        ArgumentNullException.ThrowIfNull(package);
        if (_customGuiPackage is not null && package.Sequence < _customGuiPackage.Sequence)
            throw new CustomGuiStateProjectionException("GUI10-STATE-PACKAGE", "拒绝降级已接受 GUI 包");
        CloseDynamicCustomGui();
        _customGuiPackage = package;
    }

    internal static bool TryRestoreAcceptedCustomGuiPackage()
    {
        CustomGuiAcceptedPackage? accepted = CustomGuiAcceptedReleaseStore.TryLoadCurrent(new CustomGuiAcceptedReleaseStoreRequest
        {
            StoreRoot = ClientResourceLayout.CustomGuiAcceptedRoot,
            AcceptanceStatePath = ClientResourceLayout.ManifestSecurityStatePath,
            TrustedKeys = BootstrapAcceptanceContext.TrustedKeys,
            CurrentClientVersion = ClientResourceLayout.BootstrapClientCompatibilityVersion,
        });
        if (accepted is null) return false;
        RegisterAcceptedCustomGuiPackage(accepted);
        return true;
    }

    internal static void ProcessCustomGuiPacket(Packet packet)
    {
        try
        {
            switch (packet)
            {
                case ServerPackets.CustomGuiOpen open: OpenDynamicCustomGui(open); break;
                case ServerPackets.CustomGuiStateDelta delta:
                    (_customGuiSession ?? throw new CustomGuiStateProjectionException("GUI10-STATE-CLOSED", "动态窗口尚未打开"))
                        .ApplyDelta(new CustomGuiDeltaState(delta.WindowInstanceId, delta.DocumentId, delta.DocumentRevision,
                            delta.PackageSequence, delta.SessionNonce, delta.StateRevision, delta.State));
                    break;
                case ServerPackets.CustomGuiActionResult result:
                    (_customGuiSession ?? throw new CustomGuiStateProjectionException("GUI10-STATE-CLOSED", "动态窗口尚未打开"))
                        .AcceptActionResult(result.WindowInstanceId, result.RequestSequence, result.StateRevision, result.Result, result.Message);
                    break;
                case ServerPackets.CustomGuiClose close when _customGuiSession?.Close(close.WindowInstanceId) == true:
                    CloseDynamicCustomGui();
                    break;
            }
        }
        catch (CustomGuiStateProjectionException error) { CMain.SaveError(error.Message); }
        catch (Exception error) { CMain.SaveError($"GUI10-STATE-TARGET：{error.GetType().Name}"); }
    }

    internal static void CloseAllDynamicCustomGuiWindows() => CloseDynamicCustomGui();

    private static void OpenDynamicCustomGui(ServerPackets.CustomGuiOpen packet)
    {
        if (_customGuiPackage is null) TryRestoreAcceptedCustomGuiPackage();
        CustomGuiAcceptedPackage package = _customGuiPackage
            ?? throw new CustomGuiStateProjectionException("GUI10-STATE-PACKAGE", "客户端尚未接受签名 GUI 包");
        MobileCustomGuiHost replacementHost = AttachCustomGui(package.Document);
        try
        {
            var replacementSession = new CustomGuiClientStateSession(package.Document, package.Sequence, replacementHost);
            replacementSession.Open(new CustomGuiOpenState(packet.WindowInstanceId, packet.DocumentId, packet.DocumentRevision,
                packet.PackageSequence, packet.SessionNonce, packet.ExpiresAtUnixMilliseconds, packet.StateRevision, packet.State));
            replacementHost.BindActions(replacementSession, action => MirNetwork.Network.Enqueue(ToCustomGuiPacket(action)));
            CloseDynamicCustomGui();
            _customGuiHost = replacementHost;
            _customGuiSession = replacementSession;
        }
        catch { replacementHost.Dispose(); throw; }
    }

    private static void CloseDynamicCustomGui()
    {
        _customGuiHost?.Dispose();
        _customGuiHost = null;
        _customGuiSession = null;
    }

    private static ClientPackets.CustomGuiAction ToCustomGuiPacket(CustomGuiClientAction action) => new()
    {
        WindowInstanceId = action.WindowInstanceId,
        DocumentId = action.DocumentId,
        DocumentRevision = action.DocumentRevision,
        PackageSequence = action.PackageSequence,
        SessionNonce = action.SessionNonce,
        RequestSequence = action.RequestSequence,
        Action = action.Action,
        ActionId = action.ActionId,
        TextValue = action.TextValue,
        SelectionIds = action.SelectionIds.ToList(),
        ItemIds = action.ItemIds.ToList()
    };
}
#endif
