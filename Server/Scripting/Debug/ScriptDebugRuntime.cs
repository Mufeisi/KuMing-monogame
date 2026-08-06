using System.Reflection;
using Server;
using Server.MirEnvir;

namespace Server.Scripting.Debug
{
    public sealed class ScriptDebugRuntime : IDisposable
    {
        private readonly object _gate = new object();
        private readonly ScriptCompiler _compiler = new ScriptCompiler();
        private readonly ScriptContext _context = new ScriptContext();
        private ScriptLoadContext _loadContext;
        private ScriptRegistry _registry = new ScriptRegistry();
        private long _compileCounter;

        public ScriptContext Context => _context;

        public ScriptRegistry Registry => _registry;

        public ScriptCompileResult LastCompileResult { get; private set; }

        public string LastError { get; private set; } = string.Empty;

        public void ReloadInstrumented(string scriptsRootPath)
        {
            if (string.IsNullOrWhiteSpace(scriptsRootPath))
                throw new ArgumentException("脚本根目录不能为空。", nameof(scriptsRootPath));

            lock (_gate)
            {
                var attempt = Interlocked.Increment(ref _compileCounter);
                var assemblyName = $"LomScripts_Debug_{attempt}";

                var result = _compiler.CompileInstrumentedFromDirectory(
                    scriptsRootPath: scriptsRootPath,
                    assemblyName: assemblyName,
                    debugBuild: true);

                LastCompileResult = result;

                if (!result.Success)
                {
                    LastError = $"脚本插桩编译失败（{result.ElapsedMilliseconds}ms）";
                    ReplaceRegistry(new ScriptRegistry(), unload: true);
                    return;
                }

                if (!result.HasScripts)
                {
                    LastError = string.Empty;
                    ReplaceRegistry(new ScriptRegistry(), unload: true);
                    return;
                }

                ScriptLoadContext newContext = null;
                ScriptRegistry newRegistry = null;

                try
                {
                    newContext = new ScriptLoadContext();

                    Assembly asm;
                    using (var peStream = new MemoryStream(result.AssemblyBytes))
                    using (var pdbStream = new MemoryStream(result.PdbBytes))
                    {
                        asm = newContext.LoadFromStream(peStream, pdbStream);
                    }

                    newRegistry = new ScriptRegistry();
                    ScriptManager.RegisterModules(asm, newRegistry);
                }
                catch (Exception ex)
                {
                    ScriptManager.TryUnload(newContext);
                    LastError = "脚本插桩加载失败：" + ex;
                    ReplaceRegistry(new ScriptRegistry(), unload: true);
                    return;
                }

                LastError = string.Empty;
                ReplaceRegistry(newRegistry, unload: true, newContext: newContext);
            }
        }

        public bool TryInvoke<TDelegate>(string key, Func<TDelegate, bool> invoke) where TDelegate : class
        {
            if (string.IsNullOrWhiteSpace(key)) return false;
            if (invoke == null) throw new ArgumentNullException(nameof(invoke));

            var registry = _registry;
            if (!registry.TryGet<TDelegate>(key, out var handler)) return false;

            try
            {
                return Envir.Main.InvokeOnMainThread(() => invoke(handler));
            }
            catch (Exception ex)
            {
                MessageQueue.Instance.Enqueue("[ScriptsDebug] Hook 执行异常：" + ex);
                return false;
            }
        }

        private void ReplaceRegistry(ScriptRegistry registry, bool unload, ScriptLoadContext newContext = null)
        {
            var oldContext = _loadContext;
            _loadContext = newContext;

            _registry = registry ?? new ScriptRegistry();

            if (unload)
            {
                ScriptManager.TryUnload(oldContext);
            }
        }

        public void Dispose()
        {
            lock (_gate)
            {
                ReplaceRegistry(new ScriptRegistry(), unload: true, newContext: null);
            }
        }
    }
}
