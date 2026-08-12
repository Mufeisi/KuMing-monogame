#if REAL_IOS
using MonoShare;

namespace Client_MonoGame.iOS;

/// <summary>
/// iOS MonoGame 启动壳子：在 iOS 目标下直接复用共享层 <see cref="CMain"/> 启动链。
/// 注意：该类是否能在真机/模拟器完整运行仍需在 macOS+iOS workload 环境验证。
/// </summary>
internal sealed class IosBootstrapGame : CMain
{
    public IosBootstrapGame(string clientRootPath)
        : base(clientRootPath)
    {
        IsMouseVisible = false;
        IosRuntimeInsets.Start();
    }
}
#endif
