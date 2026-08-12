namespace Server.Scripting
{
    public sealed class ScriptCompileResult
    {
        public bool HasScripts { get; }
        public bool Success { get; }
        public string AssemblyName { get; }
        public byte[] AssemblyBytes { get; }
        public byte[] PdbBytes { get; }
        public IReadOnlyList<ScriptDiagnostic> Diagnostics { get; }
        public long ElapsedMilliseconds { get; }

        public ScriptCompileResult(
            bool hasScripts,
            bool success,
            string assemblyName,
            byte[] assemblyBytes,
            byte[] pdbBytes,
            IReadOnlyList<ScriptDiagnostic> diagnostics,
            long elapsedMilliseconds)
        {
            HasScripts = hasScripts;
            Success = success;
            AssemblyName = assemblyName ?? string.Empty;
            AssemblyBytes = assemblyBytes ?? Array.Empty<byte>();
            PdbBytes = pdbBytes ?? Array.Empty<byte>();
            Diagnostics = diagnostics ?? Array.Empty<ScriptDiagnostic>();
            ElapsedMilliseconds = elapsedMilliseconds;
        }
    }
}

