using Server.MirEnvir;

namespace Server.Scripting
{
    public sealed class ScriptContext
    {
        public ScriptContext()
        {
            Api = new ScriptApi();
        }

        public Envir Envir => Envir.Main;

        public ScriptApi Api { get; }

        public void Log(string message)
        {
            MessageQueue.Instance.Enqueue(message);
        }

        public void Log(Exception exception)
        {
            MessageQueue.Instance.Enqueue(exception);
        }
    }
}
