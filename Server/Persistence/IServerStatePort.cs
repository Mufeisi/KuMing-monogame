using Server.MirEnvir;

namespace Server.Persistence
{
    public interface IServerStatePort
    {
        Envir Envir { get; }
    }

    internal sealed class EnvirServerStatePort : IServerStatePort
    {
        public Envir Envir { get; }

        public EnvirServerStatePort(Envir envir)
        {
            Envir = envir ?? throw new ArgumentNullException(nameof(envir));
        }
    }
}
