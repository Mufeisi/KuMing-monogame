using Microsoft.Xna.Framework.Audio;
using System;
using System.IO;

namespace MonoShare.MirSounds
{
    public class WavLibrary : ISoundLibrary, IDisposable
    {
        public int Index { get; set; }

        private Stream _stream;
        private bool _loop;
        private readonly byte[] _data;
        private SoundEffect soundEffect;
        private SoundEffectInstance soundInstance;

        public static WavLibrary TryCreate(int index, string fileName, bool loop)
        {
            string soundName = Path.GetFileNameWithoutExtension(fileName) + ".wav";
            fileName = Settings.ResolveSoundFile(soundName);

            if (File.Exists(fileName))
            {
                return new WavLibrary(index, fileName, loop);
            }

            MicroSoundHelper.QueueSoundDownload(soundName, fileName);

            return null;
        }

        public WavLibrary(int index, string fileName, bool loop)
        {
            Index = index;

            using (_stream = new FileStream(fileName, FileMode.Open))
            {
                soundEffect = SoundEffect.FromStream(_stream);
            }

            _loop = loop;

            Play();
        }

        public void Play()
        {
            if (_stream == null) return;
            soundInstance = soundEffect.CreateInstance();
            soundInstance.Play();
        }

        public void Stop()
        {
            soundInstance.Stop();
        }

        public void Dispose()
        {
            if (_stream != null)
                _stream.Dispose();
            _stream = null;

            _loop = false;
        }

        public void SetVolume(int vol)
        {
        }
    }
}
