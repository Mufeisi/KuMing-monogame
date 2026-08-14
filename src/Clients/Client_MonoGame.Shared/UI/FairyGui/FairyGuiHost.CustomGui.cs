#if ANDROID || IOS
using MonoShare.CustomGui;
using Shared.CustomGui;

namespace MonoShare;

internal static partial class FairyGuiHost
{
    internal static MobileCustomGuiHost AttachCustomGui(CustomGuiRuntimeDocument document)
    {
        if (_stage is null || !_initialized || _uiManager is null)
            throw new InvalidOperationException("FairyGUI 尚未初始化");
        EnsureMobileOverlaySafeAreaLayout(force: true);
        FairyGUI.GComponent parent = _mobileOverlaySafeAreaRoot ?? _uiManager.OverlayLayer ?? FairyGUI.GRoot.inst;
        int width = Math.Max(1, (int)Math.Round(parent.width));
        int height = Math.Max(1, (int)Math.Round(parent.height));
        MobileCustomGuiHost host = FairyGuiCustomGuiAdapter.Create(document, width, height);
        FairyGuiCustomGuiAdapter.AttachTo(host, parent);
        return host;
    }
}
#endif
