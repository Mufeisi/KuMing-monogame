using System;

namespace MonoShare.MirSounds
{
    public class NullLibrary : ISoundLibrary, IDisposable
    {
        public int Index { get; set; }

        public NullLibrary(int index, string fileName, bool loop)
        {
            Index = index;
        }

        public void Dispose()
        {
        }

        public void Play()
        {
        }

        public void SetVolume(int vol)
        {
        }

        public void Stop()
        {
        }
    }
}
