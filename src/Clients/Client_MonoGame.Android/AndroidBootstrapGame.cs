#if REAL_ANDROID
using MonoShare;

namespace Client_MonoGame.Android;

internal sealed class AndroidBootstrapGame : CMain
{
    public AndroidBootstrapGame(string clientRootPath)
        : base(clientRootPath)
    {
        IsMouseVisible = false;
    }
}
#endif
