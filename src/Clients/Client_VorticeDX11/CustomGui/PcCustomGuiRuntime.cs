#nullable enable

using Client.MirControls;
using Client.Bootstrap;
using Shared.CustomGui;

namespace Client.CustomGui;

internal static class PcCustomGuiRuntime
{
    private static CustomGuiAcceptedPackage? _package;
    private static PcCustomGuiHost? _host;
    private static CustomGuiClientStateSession? _session;
    private static Gui13SmokeStage _smokeStage;
    private static long _smokeDeadline;

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

    internal static bool TryRestoreAcceptedPackage()
    {
        IReadOnlyDictionary<string, Shared.Security.BootstrapManifestTrustedKey>? smokeKeys =
            PcSmokeTestAutomation.ResolveCustomGuiTrustedKeys();
        CustomGuiAcceptedPackage? accepted = CustomGuiAcceptedReleaseStore.TryLoadCurrent(new CustomGuiAcceptedReleaseStoreRequest
        {
            StoreRoot = PcBootstrapLayout.CustomGuiAcceptedRoot,
            AcceptanceStatePath = PcBootstrapLayout.ManifestSecurityStatePath,
            TrustedKeys = smokeKeys ?? PcBootstrapAcceptanceContext.TrustedKeys,
            CurrentClientVersion = PcBootstrapLayout.ClientCompatibilityVersion,
        });
        if (accepted is null) return false;
        RegisterAcceptedPackage(accepted);
        return true;
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
                    ContinueSmokeAfterResult(result);
                    break;
                case ServerPackets.CustomGuiClose close:
                    Close(close);
                    if (PcSmokeTestAutomation.CustomGuiActive && _smokeStage == Gui13SmokeStage.CloseSent)
                    {
                        _smokeStage = Gui13SmokeStage.Completed;
                        CMain.SaveError("GUI-13 阶段=关闭 结果=成功。 ");
                    }
                    break;
            }
        }
        catch (CustomGuiStateProjectionException error) { CMain.SaveError(error.Message); }
        catch (Exception error) { CMain.SaveError($"GUI10-STATE-TARGET：{error.GetType().Name}"); }
    }

    internal static void Reset() { CloseCurrent(); _package = null; _smokeStage = Gui13SmokeStage.None; _smokeDeadline = 0; }
    internal static void CloseAllWindows() => CloseCurrent();

    internal static bool ProcessSmokeTest()
    {
        if (!PcSmokeTestAutomation.CustomGuiActive) return true;
        if (_smokeStage == Gui13SmokeStage.Completed) return true;
        if (_smokeStage == Gui13SmokeStage.None)
        {
            _smokeStage = Gui13SmokeStage.OpenRequested;
            _smokeDeadline = CMain.Time + 30000;
            Client.MirNetwork.Network.Enqueue(new ClientPackets.Chat { Message = "@活动兑换" });
            CMain.SaveError("GUI-13 阶段=打开 动作=发送活动兑换命令。 ");
        }
        if (CMain.Time < _smokeDeadline) return false;
        CMain.SaveError($"GUI-13 阶段=协议 结果=失败 Stage={_smokeStage} 原因=30秒内未完成动态窗口闭环。 ");
        _smokeStage = Gui13SmokeStage.Failed;
        Program.Form.Close();
        return false;
    }

    private static void Open(ServerPackets.CustomGuiOpen packet, MirScene scene)
    {
        if (_package is null) TryRestoreAcceptedPackage();
        CustomGuiAcceptedPackage package = _package
            ?? throw new CustomGuiStateProjectionException("GUI10-STATE-PACKAGE", "客户端尚未接受签名 GUI 包");
        var replacementHost = PcCustomGuiAdapter.Create(package.Document, scene.Size);
        try
        {
            var replacementSession = new CustomGuiClientStateSession(package.Document, package.Sequence, replacementHost);
            replacementSession.Open(new CustomGuiOpenState(packet.WindowInstanceId, packet.DocumentId, packet.DocumentRevision,
                packet.PackageSequence, packet.SessionNonce, packet.ExpiresAtUnixMilliseconds, packet.StateRevision, packet.State));
            replacementHost.BindActions(replacementSession, action => Client.MirNetwork.Network.Enqueue(ToPacket(action)));
            replacementHost.AttachTo(scene);
            CloseCurrent();
            _host = replacementHost;
            _session = replacementSession;
            if (PcSmokeTestAutomation.CustomGuiActive && _smokeStage == Gui13SmokeStage.OpenRequested)
            {
                _smokeStage = Gui13SmokeStage.InvalidSent;
                _session.SendAction(action => Client.MirNetwork.Network.Enqueue(ToPacket(action)),
                    CustomGuiActionKind.SubmitSelection, CustomGuiActivityExchangeTemplate.SubmitActionId,
                    selectionIds: new[] { "gui13.invalid.offer" });
                CMain.SaveError("GUI-13 阶段=非法动作 动作=发送未授权兑换选项。 ");
            }
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

    private static void ContinueSmokeAfterResult(ServerPackets.CustomGuiActionResult result)
    {
        if (!PcSmokeTestAutomation.CustomGuiActive || _session is null || _host is null) return;
        if (_smokeStage == Gui13SmokeStage.InvalidSent)
        {
            if (result.Result == CustomGuiActionResultKind.Accepted)
                throw new CustomGuiStateProjectionException("GUI13-SMOKE-INVALID", "非法兑换动作被服务端接受");
            CMain.SaveError($"GUI-13 阶段=非法动作 结果=拒绝 Result={result.Result}。 ");
            _host.Select("exchange.options", CustomGuiActivityExchangeTemplate.OfferId);
            _smokeStage = Gui13SmokeStage.ValidSent;
            _host.Submit("exchange.submit", _session, action => Client.MirNetwork.Network.Enqueue(ToPacket(action)));
            CMain.SaveError("GUI-13 阶段=合法兑换 动作=提交已选活动项。 ");
            return;
        }
        if (_smokeStage == Gui13SmokeStage.ValidSent)
        {
            if (result.Result != CustomGuiActionResultKind.Accepted)
                throw new CustomGuiStateProjectionException("GUI13-SMOKE-VALID", "合法兑换动作未被服务端接受：" + result.Message);
            CMain.SaveError("GUI-13 阶段=合法兑换 结果=成功。 ");
            _smokeStage = Gui13SmokeStage.CloseSent;
            _session.SendAction(action => Client.MirNetwork.Network.Enqueue(ToPacket(action)),
                CustomGuiActionKind.CloseWindow, "window.close");
            CMain.SaveError("GUI-13 阶段=关闭 动作=发送关闭窗口。 ");
        }
    }

    private enum Gui13SmokeStage { None, OpenRequested, InvalidSent, ValidSent, CloseSent, Completed, Failed }

    private static ClientPackets.CustomGuiAction ToPacket(CustomGuiClientAction action) => new()
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
