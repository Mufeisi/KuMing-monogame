namespace Server.Scripting.Debug
{
    public static class ScriptDebugHook
    {
        private static ScriptDebugSession _session;

        public static ScriptDebugSession Session
        {
            get => Volatile.Read(ref _session);
            set => Volatile.Write(ref _session, value);
        }

        public static void Step(string filePath, int line, int column = 0)
        {
            var session = Volatile.Read(ref _session);
            if (session == null) return;

            session.Step(filePath, line, column);
        }
    }
}

