using System.IO;
using MonoShare;

namespace Microsoft.Xna.Framework;

public static class TitleContainer
{
    public static Stream OpenStream(string name)
    {
        return PackageResourceRegistry.OpenRequired(name);
    }
}
