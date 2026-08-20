using System.IO;

namespace FairyGUI
{
	public interface IFairyResourceLoader
	{
		byte[] ReadPackageBytes(string packageName);

		Stream OpenTextureStream(string textureName);

		Stream OpenSoundStream(string soundName);

		Stream OpenBinaryStream(string fileName);
	}

	public static class FairyResourceLoader
	{
		public static IFairyResourceLoader Current { get; set; }
	}
}

