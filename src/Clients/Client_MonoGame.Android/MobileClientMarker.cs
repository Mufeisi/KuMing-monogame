namespace Client_MonoGame.Android;

/// <summary>
/// Android 客户端骨架占位类型。
/// 当前仅确保主解决方案内存在可编译的 Android 项目，
/// 后续再逐步迁入真正的移动端共享逻辑与渲染实现。
/// </summary>
internal static class MobileClientMarker
{
#if !REAL_ANDROID
    private static void Main()
    {
        _ = typeof(Packet);
    }
#endif
}
