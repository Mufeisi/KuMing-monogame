using System.Runtime.CompilerServices;
using System.Reflection;
using Server.CustomGui;
using Server.MirDatabase;
using Server.MirEnvir;
using Server.MirObjects;
using Server.Scripting.Variables;

namespace Server.Scripting
{
    public sealed class ScriptManager : IDisposable
    {
        private readonly object _gate = new object();
        private ScriptRegistry _currentRegistry;
        private ScriptLoadContext _currentLoadContext;
        private readonly ScriptCompiler _compiler = new ScriptCompiler();
        private readonly ScriptContext _context = new ScriptContext();
        private ScriptWatcher _watcher;
        private string _scriptsRootPath = string.Empty;
        private long _compileCounter;
        private string _lastCompileFailureFingerprint = string.Empty;
        private DateTime _lastCompileFailureLogUtc = DateTime.MinValue;
        private int _suppressedCompileFailureCount;
        private static readonly TimeSpan CompileFailureLogSuppressWindow = TimeSpan.FromSeconds(10);
        private ScriptVariableDeclarationSnapshot _txtVariableDeclarations =
            ScriptVariableDeclarationSnapshot.Empty;
        private ScriptVariableDeclarationSnapshot _effectiveVariableDeclarations;

        public ScriptManager()
            : this(new ServerScriptVariableStore(), null, null, null, null)
        {
        }

        internal ScriptManager(
            ServerScriptVariableStore serverVariables,
            Action requestAccountAutoSave,
            Action requestServerVariableAutoSave,
            Func<long> currentDailyPeriod,
            Func<bool> canAccessVariables)
        {
            _currentRegistry = CreateRegistry();
            _currentRegistry.SealVariableDeclarations();
            _effectiveVariableDeclarations = _currentRegistry.VariableDeclarations;
            VariableModule = new ScriptVariableModule(
                () => Volatile.Read(ref _effectiveVariableDeclarations),
                canWrite: canAccessVariables,
                requestAutoSave: requestAccountAutoSave,
                serverPersistent: serverVariables,
                requestServerAutoSave: requestServerVariableAutoSave,
                currentDailyPeriod: currentDailyPeriod);
            VariableCommands = new ScriptVariableCommands(VariableModule);
        }

        public bool Enabled { get; private set; }
        public long Version { get; private set; }
        public DateTime? LastSuccessUtc { get; private set; }
        public DateTime? LastFailureUtc { get; private set; }
        public string LastError { get; private set; } = string.Empty;
        public IReadOnlyList<ScriptDiagnostic> LastDiagnostics { get; private set; } = Array.Empty<ScriptDiagnostic>();
        public int LastRegisteredHandlerCount { get; private set; }
        public bool LogDiagnostics { get; set; } = true;
        public IScriptVariableModule VariableModule { get; }
        public ScriptVariableCommands VariableCommands { get; }
        public ScriptVariableDeclarationSnapshot EffectiveVariableDeclarations =>
            Volatile.Read(ref _effectiveVariableDeclarations);

        public ScriptRegistry CurrentRegistry
        {
            get
            {
                return Volatile.Read(ref _currentRegistry);
            }
        }

        public void Start(string scriptsRootPath, bool hotReloadEnabled, int debounceMs)
        {
            lock (_gate)
            {
                StopInternal();

                if (!RuntimeFeature.IsDynamicCodeSupported)
                {
                    Enabled = false;
                    LastError = "当前运行环境不支持动态代码（可能为 NativeAOT/受限运行时）。已禁用 C# 脚本系统。";
                    LastFailureUtc = DateTime.UtcNow;
                    MessageQueue.Instance.Enqueue(LastError);
                    return;
                }

                _scriptsRootPath = scriptsRootPath;

                Enabled = true;
                LastError = string.Empty;

                if (hotReloadEnabled)
                {
                    _watcher = new ScriptWatcher(scriptsRootPath, debounceMs);
                    _watcher.ScriptsChanged += Reload;
                    _watcher.Start();
                }

                Reload();
            }
        }

        public void Reload()
        {
            lock (_gate)
            {
                if (!Enabled) return;

                try
                {
                    var attempt = Interlocked.Increment(ref _compileCounter);
                    var assemblyName = $"LomScripts_{attempt}";

                    MessageQueue.Instance.Enqueue($"[Scripts] 编译开始：{_scriptsRootPath}");

                    var compileResult = _compiler.CompileFromDirectory(
                        scriptsRootPath: _scriptsRootPath,
                        assemblyName: assemblyName,
                        debugBuild: true);

                    LastDiagnostics = compileResult.Diagnostics;

                    if (!compileResult.Success)
                    {
                        var nowUtc = DateTime.UtcNow;
                        LastFailureUtc = nowUtc;
                        LastError = $"脚本编译失败（{compileResult.ElapsedMilliseconds}ms）";

                        if (ShouldLogCompileFailure(compileResult.Diagnostics, nowUtc, out var suppressedLogLine))
                        {
                            MessageQueue.Instance.Enqueue("[Scripts] " + LastError);

                            if (LogDiagnostics)
                            {
                                foreach (var d in compileResult.Diagnostics)
                                {
                                    MessageQueue.Instance.Enqueue("[Scripts] " + d);
                                }
                            }
                        }
                        else if (!string.IsNullOrWhiteSpace(suppressedLogLine))
                        {
                            MessageQueue.Instance.Enqueue(suppressedLogLine);
                        }

                        return;
                    }

                    if (!compileResult.HasScripts)
                    {
                        ResetCompileFailureNoiseGate();

                        var emptyRegistry = CreateRegistry();
                        var oldRegistry = PublishRegistry(emptyRegistry);
                        oldRegistry = null;

                        var oldContext = _currentLoadContext;
                        _currentLoadContext = null;
                        TryUnload(oldContext);

                        Version++;
                        LastRegisteredHandlerCount = 0;
                        LastSuccessUtc = DateTime.UtcNow;
                        LastError = string.Empty;

                        MessageQueue.Instance.Enqueue($"[Scripts] 无脚本文件，已清空处理器（{compileResult.ElapsedMilliseconds}ms）");

                        try
                        {
                            Envir.Main.InvokeOnMainThread(() =>
                            {
                            Envir.Main.ApplyCSharpQuestDefinitions(emptyRegistry);
                            Envir.Main.ApplyCSharpDropTables(emptyRegistry);
                            Envir.Main.ApplyCSharpRecipeDefinitions(emptyRegistry);
                            Envir.Main.ApplyCSharpValueDefinitions(emptyRegistry);
                            Envir.Main.ApplyCSharpNameListDefinitions(emptyRegistry);
                            Envir.Main.ApplyCSharpRouteDefinitions(emptyRegistry);
                            Envir.Main.ApplyCSharpRootConfigDefinitions(emptyRegistry);
                            Envir.Main.ApplyCSharpTextFileDefinitions(emptyRegistry);
                            return true;
                        });
                    }
                    catch (Exception ex)
                    {
                            MessageQueue.Instance.Enqueue("[Scripts] 清空 C# 脚本数据失败：" + ex);
                        }

                        return;
                    }

                    ScriptLoadContext newContext = null;
                    ScriptRegistry newRegistry = null;

                    try
                    {
                        newContext = new ScriptLoadContext();

                        Assembly scriptAssembly;
                        using (var peStream = new MemoryStream(compileResult.AssemblyBytes))
                        using (var pdbStream = new MemoryStream(compileResult.PdbBytes))
                        {
                            scriptAssembly = newContext.LoadFromStream(peStream, pdbStream);
                        }

                        newRegistry = CreateRegistry();
                        RegisterModules(scriptAssembly, newRegistry);
                    }
                    catch
                    {
                        TryUnload(newContext);
                        throw;
                    }

                    var oldRegistry2 = PublishRegistry(newRegistry);
                    oldRegistry2 = null;

                    var oldContext2 = _currentLoadContext;
                    _currentLoadContext = newContext;
                    TryUnload(oldContext2);

                    ResetCompileFailureNoiseGate();

                    Version++;
                    LastRegisteredHandlerCount = newRegistry.Count;
                    LastSuccessUtc = DateTime.UtcNow;
                    LastError = string.Empty;

                    MessageQueue.Instance.Enqueue($"[Scripts] 编译并加载成功 v{Version}（{compileResult.ElapsedMilliseconds}ms，handlers={newRegistry.Count}）");

                    try
                    {
                        Envir.Main.InvokeOnMainThread(() =>
                        {
                            Envir.Main.ApplyCSharpQuestDefinitions(newRegistry);
                            Envir.Main.ApplyCSharpDropTables(newRegistry);
                            Envir.Main.ApplyCSharpRecipeDefinitions(newRegistry);
                            Envir.Main.ApplyCSharpValueDefinitions(newRegistry);
                            Envir.Main.ApplyCSharpNameListDefinitions(newRegistry);
                            Envir.Main.ApplyCSharpRouteDefinitions(newRegistry);
                            Envir.Main.ApplyCSharpRootConfigDefinitions(newRegistry);
                            Envir.Main.ApplyCSharpTextFileDefinitions(newRegistry);
                            return true;
                        });
                    }
                    catch (Exception ex)
                    {
                        MessageQueue.Instance.Enqueue("[Scripts] 应用 C# 脚本数据失败：" + ex);
                    }
                }
                catch (Exception ex)
                {
                    LastFailureUtc = DateTime.UtcNow;
                    LastError = ex.ToString();
                    MessageQueue.Instance.Enqueue(ex);
                }
            }
        }

        private void ResetCompileFailureNoiseGate()
        {
            _lastCompileFailureFingerprint = string.Empty;
            _lastCompileFailureLogUtc = DateTime.MinValue;
            _suppressedCompileFailureCount = 0;
        }

        public CustomGuiScriptOpenResult OpenCustomGui(PlayerObject player, string documentId)
        {
            if (!Enabled)
                return CustomGuiScriptOpenResult.Rejected("GUI11-HOOK-DISABLED：C# 脚本系统未启用");
            if (player == null || player.Connection == null)
                return CustomGuiScriptOpenResult.Rejected("GUI11-HOOK-PLAYER：玩家连接无效");

            ScriptRegistry registry = CurrentRegistry;
            try
            {
                CustomGuiScriptOpenResult result = Envir.Main.InvokeOnMainThread(() =>
                {
                    CustomGuiScriptPlanResult prepared = registry.CustomGui.PrepareOpen(
                        _context, player, documentId, DateTimeOffset.UtcNow);
                    if (!prepared.Success)
                        return CustomGuiScriptOpenResult.Rejected(prepared.Diagnostic);
                    return CustomGuiScriptOpenResult.Accepted(
                        player.Connection.OpenCustomGuiScriptSession(prepared.Plan));
                });
                return result ?? CustomGuiScriptOpenResult.Rejected(
                    "GUI11-HOOK-MAINTHREAD：游戏主线程不可用，脚本窗口未打开");
            }
            catch (Exception error)
            {
                ReportCustomGuiError("GUI11-HOOK-OPEN", error);
                return CustomGuiScriptOpenResult.Rejected("GUI11-HOOK-OPEN：脚本窗口打开失败");
            }
        }

        private ScriptRegistry CreateRegistry()
        {
            var registry = new ScriptRegistry(ReportCustomGuiError);
            ActivityExchangeWindow.Register(registry.CustomGui, packageSequence: Settings.CustomGuiPackageSequence);
            return registry;
        }

        private ScriptRegistry PublishRegistry(ScriptRegistry next)
        {
            next.SealVariableDeclarations();
            ScriptRegistry[] published = Envir.Main.InvokeOnMainThread(() =>
            {
                ScriptRegistry previous = CurrentRegistry;
                ScriptVariableDeclarationSnapshot candidateDeclarations =
                    ScriptVariableDeclarationSnapshot.Merge(
                        next.VariableDeclarations,
                        Volatile.Read(ref _txtVariableDeclarations));
                ScriptVariableCatalogReloadResult variableCompatibility =
                    EffectiveVariableDeclarations.ValidateCompatibleTransitionTo(candidateDeclarations);
                if (!variableCompatibility.Success)
                    throw new InvalidOperationException(variableCompatibility.Diagnostic);

                var affectedDocuments = new HashSet<string>(
                    previous.CustomGui.DocumentIds.Concat(next.CustomGui.DocumentIds),
                    StringComparer.Ordinal);
                if (affectedDocuments.Count > 0)
                {
                    lock (Envir.Main.Connections)
                    {
                        foreach (var connection in Envir.Main.Connections.ToArray())
                            connection.InvalidateCustomGuiScriptDocuments(affectedDocuments);
                    }
                }
                Interlocked.Exchange(ref _currentRegistry, next);
                Interlocked.Exchange(ref _effectiveVariableDeclarations, candidateDeclarations);
                return new[] { previous };
            });
            return published?[0] ?? throw new InvalidOperationException(
                "GUI11-HOOK-MAINTHREAD：游戏主线程不可用，脚本注册表未发布");
        }

        public ScriptVariableCatalogReloadResult ValidateTxtVariableDeclarations(ITextFileProvider provider)
        {
            try
            {
                ScriptVariableDeclarationSnapshot candidateTxt =
                    ScriptVariableTextDeclarationParser.CreateSnapshot(provider);
                ScriptVariableDeclarationSnapshot candidateEffective =
                    ScriptVariableDeclarationSnapshot.Merge(
                        CurrentRegistry.VariableDeclarations,
                        candidateTxt);
                return EffectiveVariableDeclarations.ValidateCompatibleTransitionTo(candidateEffective);
            }
            catch (Exception error) when (error is ArgumentException or InvalidOperationException)
            {
                return ScriptVariableCatalogReloadResult.Fail(
                    ScriptVariableErrorCode.DeclarationConflict, error.Message);
            }
        }

        public ScriptVariableCatalogReloadResult PublishTxtVariableDeclarations(ITextFileProvider provider)
        {
            try
            {
                ScriptVariableDeclarationSnapshot candidateTxt =
                    ScriptVariableTextDeclarationParser.CreateSnapshot(provider);
                ScriptVariableDeclarationSnapshot candidateEffective =
                    ScriptVariableDeclarationSnapshot.Merge(
                        CurrentRegistry.VariableDeclarations,
                        candidateTxt);
                ScriptVariableCatalogReloadResult compatibility =
                    EffectiveVariableDeclarations.ValidateCompatibleTransitionTo(candidateEffective);
                if (!compatibility.Success) return compatibility;

                Interlocked.Exchange(ref _txtVariableDeclarations, candidateTxt);
                Interlocked.Exchange(ref _effectiveVariableDeclarations, candidateEffective);
                return ScriptVariableCatalogReloadResult.Ok();
            }
            catch (Exception error) when (error is ArgumentException or InvalidOperationException)
            {
                return ScriptVariableCatalogReloadResult.Fail(
                    ScriptVariableErrorCode.DeclarationConflict, error.Message);
            }
        }

        private static void ReportCustomGuiError(string code, Exception error)
        {
            try
            {
                MessageQueue.Instance.Enqueue($"[Scripts][{code}] 动态 GUI 旁路异常：{error.GetType().Name}");
            }
            catch { }
        }

        private bool ShouldLogCompileFailure(IReadOnlyList<ScriptDiagnostic> diagnostics, DateTime nowUtc, out string suppressedLogLine)
        {
            suppressedLogLine = string.Empty;

            var fingerprint = GetCompileFailureFingerprint(diagnostics);

            if (!string.IsNullOrWhiteSpace(fingerprint)
                && string.Equals(fingerprint, _lastCompileFailureFingerprint, StringComparison.Ordinal)
                && nowUtc - _lastCompileFailureLogUtc < CompileFailureLogSuppressWindow)
            {
                _suppressedCompileFailureCount++;

                if (_suppressedCompileFailureCount == 1)
                {
                    suppressedLogLine = "[Scripts] 脚本编译失败（重复错误已抑制，使用 @ScriptStatus 查看详情）";
                }

                return false;
            }

            if (_suppressedCompileFailureCount > 0)
            {
                MessageQueue.Instance.Enqueue($"[Scripts] 重复编译错误在 {CompileFailureLogSuppressWindow.TotalSeconds:0}s 内被抑制 {_suppressedCompileFailureCount} 次。");
                _suppressedCompileFailureCount = 0;
            }

            _lastCompileFailureFingerprint = fingerprint;
            _lastCompileFailureLogUtc = nowUtc;
            return true;
        }

        private static string GetCompileFailureFingerprint(IReadOnlyList<ScriptDiagnostic> diagnostics)
        {
            if (diagnostics == null || diagnostics.Count == 0) return string.Empty;

            var hash = new HashCode();
            var anyError = false;

            for (var i = 0; i < diagnostics.Count; i++)
            {
                var d = diagnostics[i];

                if (!string.Equals(d.Severity, "Error", StringComparison.OrdinalIgnoreCase))
                    continue;

                anyError = true;
                hash.Add(d.Id ?? string.Empty, StringComparer.OrdinalIgnoreCase);
                hash.Add(d.FilePath ?? string.Empty, StringComparer.OrdinalIgnoreCase);
                hash.Add(d.Line);
                hash.Add(d.Column);
                hash.Add(d.Message ?? string.Empty, StringComparer.Ordinal);
            }

            return anyError ? hash.ToHashCode().ToString() : string.Empty;
        }

        public void Stop()
        {
            lock (_gate)
            {
                StopInternal();
            }
        }

        private void StopInternal()
        {
            _watcher?.Dispose();
            _watcher = null;

            VariableModule.Reset(
                ScriptVariableContext.ForServer(),
                ScriptVariableSelector.ScopeOnly(ScriptVariableScope.I));

            ScriptRegistry oldRegistry;
            try
            {
                oldRegistry = PublishRegistry(CreateRegistry());
            }
            catch when (!Envir.Main.Running)
            {
                var emptyRegistry = CreateRegistry();
                emptyRegistry.SealVariableDeclarations();
                oldRegistry = Interlocked.Exchange(ref _currentRegistry, emptyRegistry);
                Interlocked.Exchange(
                    ref _effectiveVariableDeclarations,
                    ScriptVariableDeclarationSnapshot.Merge(
                        emptyRegistry.VariableDeclarations,
                        Volatile.Read(ref _txtVariableDeclarations)));
            }
            oldRegistry = null;

            var oldContext = _currentLoadContext;
            _currentLoadContext = null;
            TryUnload(oldContext);

            Enabled = false;
        }

        internal static void RegisterModules(Assembly scriptAssembly, ScriptRegistry registry)
        {
            if (scriptAssembly == null) throw new ArgumentNullException(nameof(scriptAssembly));
            if (registry == null) throw new ArgumentNullException(nameof(registry));

            Type[] types;

            try
            {
                types = scriptAssembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                types = ex.Types.Where(t => t != null).ToArray();
            }

            var moduleTypes = types.Where(t =>
                t != null &&
                typeof(IScriptModule).IsAssignableFrom(t) &&
                !t.IsAbstract &&
                t.GetConstructor(Type.EmptyTypes) != null).ToArray();

            var moduleCatalog = new Dictionary<string, ScriptRegistry.ScriptModuleCatalogEntry>(StringComparer.Ordinal);
            var autoRegisterModuleTypes = new List<Type>(moduleTypes.Length);

            for (var i = 0; i < moduleTypes.Length; i++)
            {
                var type = moduleTypes[i];

                var attr = type.GetCustomAttribute<ScriptModuleAttribute>(inherit: false);
                var moduleKey = attr?.Key ?? string.Empty;
                var autoRegister = attr?.AutoRegister ?? true;

                if (!string.IsNullOrWhiteSpace(moduleKey))
                {
                    if (!LogicKey.TryNormalize(moduleKey, out var normalizedModuleKey))
                        throw new InvalidOperationException($"脚本模块 Key 无效：{moduleKey}（type={type.FullName}）");

                    if (moduleCatalog.TryGetValue(normalizedModuleKey, out var existing))
                        throw new InvalidOperationException($"重复的脚本模块 Key：{normalizedModuleKey}（{existing.ModuleType.FullName} / {type.FullName}）");

                    moduleCatalog.Add(normalizedModuleKey, new ScriptRegistry.ScriptModuleCatalogEntry(type, autoRegister));
                }

                if (autoRegister)
                {
                    autoRegisterModuleTypes.Add(type);
                }
            }

            registry.SetModuleCatalog(moduleCatalog);

            for (var i = 0; i < autoRegisterModuleTypes.Count; i++)
            {
                var type = autoRegisterModuleTypes[i];
                var instance = (IScriptModule)Activator.CreateInstance(type);
                instance.Register(registry);
            }

            var npcModuleTypes = types.Where(t =>
                t != null &&
                typeof(INpcScriptModule).IsAssignableFrom(t) &&
                !t.IsAbstract &&
                t.GetConstructor(Type.EmptyTypes) != null).ToArray();

            if (npcModuleTypes.Length > 0)
            {
                var npcRegistry = new NpcRegistry(registry);

                for (var i = 0; i < npcModuleTypes.Length; i++)
                {
                    var type = npcModuleTypes[i];
                    var instance = (INpcScriptModule)Activator.CreateInstance(type);
                    instance.Register(npcRegistry);
                }
            }
        }

        private bool TryInvoke<TDelegate>(string key, Func<TDelegate, bool> invoke) where TDelegate : class
        {
            if (!Enabled) return false;

            var registry = CurrentRegistry;
            if (!registry.TryGet<TDelegate>(key, out var handler)) return false;

            try
            {
                // 保证脚本 Hook 只在主逻辑线程执行（若从非主线程触发则投递到主线程队列）。
                return Envir.Main.InvokeOnMainThread(() =>
                {
                    if (!Settings.ScriptsRuntimeMetricsEnabled)
                        return invoke(handler);

                    var start = ScriptRuntimeMetrics.GetTimestamp();

                    try
                    {
                        return invoke(handler);
                    }
                    finally
                    {
                        var elapsed = ScriptRuntimeMetrics.GetTimestamp() - start;
                        ScriptRuntimeMetrics.RecordCSharpHandler(key, elapsed);
                    }
                });
            }
            catch (Exception ex)
            {
                MessageQueue.Instance.Enqueue("[Scripts] Hook 执行异常：" + ex);
                return false;
            }
        }

        public bool TryHandlePlayerLogin(PlayerObject player) =>
            TryHandlePlayerLogin(player, out _);

        public bool TryHandlePlayerLogin(PlayerObject player, out bool faulted)
        {
            faulted = false;
            bool cSharpEligible = Settings.CSharpScriptsEnabled && Enabled;
            bool handlerExists = HasHandler<OnPlayerLoginHook>(ScriptHookKeys.OnPlayerLogin);
            bool completed = false;
            bool handled = TryInvoke<OnPlayerLoginHook>(
                ScriptHookKeys.OnPlayerLogin, h =>
                {
                    bool result = h(_context, player);
                    completed = true;
                    return result;
                });
            if (handlerExists && !completed)
            {
                faulted = true;
                return false;
            }
            return LingFengTxtSystemHookAdapter.TryDispatchAfterCSharp(
                handled, Envir.Main.TextFileProvider, ScriptHookKeys.OnPlayerLogin, player, cSharpEligible);
        }

        private bool HasHandler<TDelegate>(string key) where TDelegate : class =>
            Enabled && CurrentRegistry.TryGet<TDelegate>(key, out _);

        public bool TryHandlePlayerLevelUp(PlayerObject player) =>
            TryHandlePlayerLevelUp(player, out _);

        public bool TryHandlePlayerLevelUp(PlayerObject player, out bool faulted)
        {
            faulted = false;
            bool cSharpEligible = Settings.CSharpScriptsEnabled && Enabled;
            bool handlerExists = HasHandler<OnPlayerLevelUpHook>(ScriptHookKeys.OnPlayerLevelUp);
            bool completed = false;
            bool handled = TryInvoke<OnPlayerLevelUpHook>(
                ScriptHookKeys.OnPlayerLevelUp, h =>
                {
                    bool result = h(_context, player);
                    completed = true;
                    return result;
                });
            if (handlerExists && !completed)
            {
                faulted = true;
                return false;
            }
            return LingFengTxtSystemHookAdapter.TryDispatchAfterCSharp(
                handled, Envir.Main.TextFileProvider, ScriptHookKeys.OnPlayerLevelUp, player, cSharpEligible);
        }

        public bool TryHandlePlayerDie(PlayerObject player)
        {
            bool cSharpEligible = Settings.CSharpScriptsEnabled && Enabled;
            bool handlerExists = cSharpEligible && HasHandler<OnPlayerDieHook>(ScriptHookKeys.OnPlayerDie);
            bool handled = handlerExists && TryInvoke<OnPlayerDieHook>(
                ScriptHookKeys.OnPlayerDie, h => h(_context, player));
            LingFengTxtSystemHookAdapter.TryDispatchPlayerDeath(
                handlerExists,
                Envir.Main.TextFileProvider,
                player,
                player?.LastHitter,
                cSharpEligible);
            return handled;
        }

        public bool TryHandlePlayerUseItem(PlayerObject player, int itemShape)
        {
            if (TryInvoke<OnPlayerUseItemHook>(ScriptHookKeys.OnPlayerUseItemShape(itemShape), h => h(_context, player, itemShape)))
                return true;

            return TryInvoke<OnPlayerUseItemHook>(ScriptHookKeys.OnPlayerUseItem, h => h(_context, player, itemShape));
        }

        public bool TryHandlePlayerMapEnter(PlayerObject player, string mapFileName) =>
            TryInvoke<OnPlayerMapEnterHook>(ScriptHookKeys.OnPlayerMapEnter, h => h(_context, player, mapFileName));

        public bool TryHandlePlayerMapCoord(PlayerObject player, string mapFileName, int x, int y) =>
            TryInvoke<OnPlayerMapCoordHook>(ScriptHookKeys.OnPlayerMapCoord, h => h(_context, player, mapFileName, x, y));

        public bool TryHandlePlayerMapLeaveBefore(PlayerObject player, PlayerMapLeaveRequest request)
        {
            if (player == null) return false;
            if (request == null) throw new ArgumentNullException(nameof(request));

            var mapFileName = request.FromMap?.Info?.FileName ?? string.Empty;

            if (!string.IsNullOrWhiteSpace(mapFileName))
            {
                if (TryInvoke<OnPlayerMapLeaveBeforeHook>(ScriptHookKeys.OnPlayerMapLeaveBeforeMap(mapFileName), h =>
                {
                    h(_context, player, request);
                    return request.Decision != ScriptHookDecision.Continue;
                }))
                {
                    return true;
                }
            }

            return TryInvoke<OnPlayerMapLeaveBeforeHook>(ScriptHookKeys.OnPlayerMapLeaveBefore, h =>
            {
                h(_context, player, request);
                return request.Decision != ScriptHookDecision.Continue;
            });
        }

        public bool TryHandlePlayerMapEnterAfter(PlayerObject player, PlayerMapEnterResult result)
        {
            if (player == null) return false;
            if (result == null) throw new ArgumentNullException(nameof(result));

            var mapFileName = result.ToMap?.Info?.FileName ?? string.Empty;

            if (!string.IsNullOrWhiteSpace(mapFileName))
            {
                if (TryInvoke<OnPlayerMapEnterAfterHook>(ScriptHookKeys.OnPlayerMapEnterAfterMap(mapFileName), h =>
                {
                    h(_context, player, result);
                    return true;
                }))
                {
                    return true;
                }
            }

            return TryInvoke<OnPlayerMapEnterAfterHook>(ScriptHookKeys.OnPlayerMapEnterAfter, h =>
            {
                h(_context, player, result);
                return true;
            });
        }

        public bool TryHandlePlayerRegionEnter(PlayerObject player, PlayerRegionEvent e)
        {
            if (player == null) return false;
            if (e == null) throw new ArgumentNullException(nameof(e));

            if (TryInvoke<OnPlayerRegionEnterHook>(ScriptHookKeys.OnPlayerRegionEnterKey(e.Region.MapFileName, e.Region.RegionKey), h =>
            {
                h(_context, player, e);
                return true;
            }))
            {
                return true;
            }

            return TryInvoke<OnPlayerRegionEnterHook>(ScriptHookKeys.OnPlayerRegionEnter, h =>
            {
                h(_context, player, e);
                return true;
            });
        }

        public bool TryHandlePlayerRegionLeave(PlayerObject player, PlayerRegionEvent e)
        {
            if (player == null) return false;
            if (e == null) throw new ArgumentNullException(nameof(e));

            if (TryInvoke<OnPlayerRegionLeaveHook>(ScriptHookKeys.OnPlayerRegionLeaveKey(e.Region.MapFileName, e.Region.RegionKey), h =>
            {
                h(_context, player, e);
                return true;
            }))
            {
                return true;
            }

            return TryInvoke<OnPlayerRegionLeaveHook>(ScriptHookKeys.OnPlayerRegionLeave, h =>
            {
                h(_context, player, e);
                return true;
            });
        }

        public bool TryHandleActivityProgressBefore(ActivityProgressRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));

            if (TryInvoke<OnActivityProgressBeforeHook>(ScriptHookKeys.OnActivityProgressBeforeKey(request.Descriptor.SourceType, request.Descriptor.ActivityKey), h =>
            {
                h(_context, request);
                return request.Decision != ScriptHookDecision.Continue;
            }))
            {
                return true;
            }

            return TryInvoke<OnActivityProgressBeforeHook>(ScriptHookKeys.OnActivityProgressBefore, h =>
            {
                h(_context, request);
                return request.Decision != ScriptHookDecision.Continue;
            });
        }

        public bool TryHandleActivityProgressAfter(ActivityProgressResult result)
        {
            if (result == null) throw new ArgumentNullException(nameof(result));

            if (TryInvoke<OnActivityProgressAfterHook>(ScriptHookKeys.OnActivityProgressAfterKey(result.Descriptor.SourceType, result.Descriptor.ActivityKey), h =>
            {
                h(_context, result);
                return true;
            }))
            {
                return true;
            }

            return TryInvoke<OnActivityProgressAfterHook>(ScriptHookKeys.OnActivityProgressAfter, h =>
            {
                h(_context, result);
                return true;
            });
        }

        public bool TryHandleActivityResultBefore(ActivityResultRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));

            if (TryInvoke<OnActivityResultBeforeHook>(ScriptHookKeys.OnActivityResultBeforeKey(request.Descriptor.SourceType, request.Descriptor.ActivityKey), h =>
            {
                h(_context, request);
                return request.Decision != ScriptHookDecision.Continue;
            }))
            {
                return true;
            }

            return TryInvoke<OnActivityResultBeforeHook>(ScriptHookKeys.OnActivityResultBefore, h =>
            {
                h(_context, request);
                return request.Decision != ScriptHookDecision.Continue;
            });
        }

        public bool TryHandleActivityResultAfter(ActivityResult result)
        {
            if (result == null) throw new ArgumentNullException(nameof(result));

            if (TryInvoke<OnActivityResultAfterHook>(ScriptHookKeys.OnActivityResultAfterKey(result.Descriptor.SourceType, result.Descriptor.ActivityKey), h =>
            {
                h(_context, result);
                return true;
            }))
            {
                return true;
            }

            return TryInvoke<OnActivityResultAfterHook>(ScriptHookKeys.OnActivityResultAfter, h =>
            {
                h(_context, result);
                return true;
            });
        }

        public bool TryHandleActivityRewardBefore(ActivityRewardRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));

            if (TryInvoke<OnActivityRewardBeforeHook>(ScriptHookKeys.OnActivityRewardBeforeKey(request.Descriptor.SourceType, request.Descriptor.ActivityKey), h =>
            {
                h(_context, request);
                return request.Decision != ScriptHookDecision.Continue;
            }))
            {
                return true;
            }

            return TryInvoke<OnActivityRewardBeforeHook>(ScriptHookKeys.OnActivityRewardBefore, h =>
            {
                h(_context, request);
                return request.Decision != ScriptHookDecision.Continue;
            });
        }

        public bool TryHandleActivityRewardAfter(ActivityRewardResult result)
        {
            if (result == null) throw new ArgumentNullException(nameof(result));

            if (TryInvoke<OnActivityRewardAfterHook>(ScriptHookKeys.OnActivityRewardAfterKey(result.Descriptor.SourceType, result.Descriptor.ActivityKey), h =>
            {
                h(_context, result);
                return true;
            }))
            {
                return true;
            }

            return TryInvoke<OnActivityRewardAfterHook>(ScriptHookKeys.OnActivityRewardAfter, h =>
            {
                h(_context, result);
                return true;
            });
        }

        public bool TryHandleShopPriceBefore(ShopPriceRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));

            if (request.NPC?.Info != null)
            {
                if (TryInvoke<OnShopPriceBeforeHook>(ScriptHookKeys.OnShopPriceBeforeNpc(request.Operation, request.NPC.Info.Index), h =>
                {
                    h(_context, request);
                    return request.Decision != ScriptHookDecision.Continue;
                }))
                {
                    return true;
                }
            }

            if (TryInvoke<OnShopPriceBeforeHook>(ScriptHookKeys.OnShopPriceBeforeOperation(request.Operation), h =>
            {
                h(_context, request);
                return request.Decision != ScriptHookDecision.Continue;
            }))
            {
                return true;
            }

            return TryInvoke<OnShopPriceBeforeHook>(ScriptHookKeys.OnShopPriceBefore, h =>
            {
                h(_context, request);
                return request.Decision != ScriptHookDecision.Continue;
            });
        }

        public bool TryHandleShopPriceAfter(ShopPriceResult result)
        {
            if (result == null) throw new ArgumentNullException(nameof(result));

            if (result.NPC?.Info != null)
            {
                if (TryInvoke<OnShopPriceAfterHook>(ScriptHookKeys.OnShopPriceAfterNpc(result.Operation, result.NPC.Info.Index), h =>
                {
                    h(_context, result);
                    return true;
                }))
                {
                    return true;
                }
            }

            if (TryInvoke<OnShopPriceAfterHook>(ScriptHookKeys.OnShopPriceAfterOperation(result.Operation), h =>
            {
                h(_context, result);
                return true;
            }))
            {
                return true;
            }

              return TryInvoke<OnShopPriceAfterHook>(ScriptHookKeys.OnShopPriceAfter, h =>
              {
                  h(_context, result);
                  return true;
              });
          }

        public bool TryHandleMarketFeeBefore(MarketFeeRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));

            if (request.NPC?.Info != null)
            {
                if (TryInvoke<OnMarketFeeBeforeHook>(ScriptHookKeys.OnMarketFeeBeforeNpc(request.Operation, request.NPC.Info.Index), h =>
                {
                    h(_context, request);
                    return request.Decision != ScriptHookDecision.Continue;
                }))
                {
                    return true;
                }
            }

            if (TryInvoke<OnMarketFeeBeforeHook>(ScriptHookKeys.OnMarketFeeBeforeOperation(request.Operation), h =>
            {
                h(_context, request);
                return request.Decision != ScriptHookDecision.Continue;
            }))
            {
                return true;
            }

            return TryInvoke<OnMarketFeeBeforeHook>(ScriptHookKeys.OnMarketFeeBefore, h =>
            {
                h(_context, request);
                return request.Decision != ScriptHookDecision.Continue;
            });
        }

        public bool TryHandleMarketFeeAfter(MarketFeeResult result)
        {
            if (result == null) throw new ArgumentNullException(nameof(result));

            if (result.NPC?.Info != null)
            {
                if (TryInvoke<OnMarketFeeAfterHook>(ScriptHookKeys.OnMarketFeeAfterNpc(result.Operation, result.NPC.Info.Index), h =>
                {
                    h(_context, result);
                    return true;
                }))
                {
                    return true;
                }
            }

            if (TryInvoke<OnMarketFeeAfterHook>(ScriptHookKeys.OnMarketFeeAfterOperation(result.Operation), h =>
            {
                h(_context, result);
                return true;
            }))
            {
                return true;
            }

            return TryInvoke<OnMarketFeeAfterHook>(ScriptHookKeys.OnMarketFeeAfter, h =>
            {
                h(_context, result);
                return true;
            });
        }

        public bool TryHandleMailCostBefore(MailCostRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));

            var handled = false;

            if (TryInvoke<OnMailCostBeforeHook>(ScriptHookKeys.OnMailCostBeforeOperation(request.Operation), h =>
            {
                h(_context, request);
                return true;
            }))
            {
                handled = true;
            }

            if (TryInvoke<OnMailCostBeforeHook>(ScriptHookKeys.OnMailCostBefore, h =>
            {
                h(_context, request);
                return true;
            }))
            {
                handled = true;
            }

            return handled;
        }

        public bool TryHandleMailCostAfter(MailCostResult result)
        {
            if (result == null) throw new ArgumentNullException(nameof(result));

            var handled = false;

            if (TryInvoke<OnMailCostAfterHook>(ScriptHookKeys.OnMailCostAfterOperation(result.Operation), h =>
            {
                h(_context, result);
                return true;
            }))
            {
                handled = true;
            }

            if (TryInvoke<OnMailCostAfterHook>(ScriptHookKeys.OnMailCostAfter, h =>
            {
                h(_context, result);
                return true;
            }))
            {
                handled = true;
            }

            return handled;
        }

        public bool TryHandleEconomyRateBefore(EconomyRateRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));

            if (TryInvoke<OnEconomyRateBeforeHook>(ScriptHookKeys.OnEconomyRateBeforeType(request.Type), h =>
            {
                h(_context, request);
                return request.Decision != ScriptHookDecision.Continue;
            }))
            {
                return true;
            }

            return TryInvoke<OnEconomyRateBeforeHook>(ScriptHookKeys.OnEconomyRateBefore, h =>
            {
                h(_context, request);
                return request.Decision != ScriptHookDecision.Continue;
            });
        }

        public bool TryHandleEconomyRateAfter(EconomyRateResult result)
        {
            if (result == null) throw new ArgumentNullException(nameof(result));

            if (TryInvoke<OnEconomyRateAfterHook>(ScriptHookKeys.OnEconomyRateAfterType(result.Type), h =>
            {
                h(_context, result);
                return true;
            }))
            {
                return true;
            }

            return TryInvoke<OnEconomyRateAfterHook>(ScriptHookKeys.OnEconomyRateAfter, h =>
            {
                h(_context, result);
                return true;
            });
        }

        public bool TryHandlePlayerTrigger(PlayerObject player, string triggerKey) =>
            TryInvoke<OnPlayerTriggerHook>(ScriptHookKeys.OnPlayerTrigger, h => h(_context, player, triggerKey));

        public bool TryHandlePlayerChatCommand(PlayerObject player, string commandLine, string command, IReadOnlyList<string> args)
        {
            commandLine ??= string.Empty;
            command ??= string.Empty;
            args ??= Array.Empty<string>();

            if (command.Length > 0)
            {
                if (TryInvoke<OnPlayerChatCommandHook>(ScriptHookKeys.OnPlayerChatCommandName(command), h => h(_context, player, commandLine, command, args)))
                    return true;
            }

            return TryInvoke<OnPlayerChatCommandHook>(ScriptHookKeys.OnPlayerChatCommand, h => h(_context, player, commandLine, command, args));
        }

        public bool TryHandlePlayerMagicCastBefore(PlayerObject player, PlayerMagicCastRequest request)
        {
            if (player == null) return false;
            if (request == null) throw new ArgumentNullException(nameof(request));

            if (TryInvoke<OnPlayerMagicCastBeforeHook>(ScriptHookKeys.OnPlayerMagicCastBeforeSpell(request.Spell), h =>
            {
                h(_context, player, request);
                return request.Decision != ScriptHookDecision.Continue;
            }))
            {
                return true;
            }

            return TryInvoke<OnPlayerMagicCastBeforeHook>(ScriptHookKeys.OnPlayerMagicCastBefore, h =>
            {
                h(_context, player, request);
                return request.Decision != ScriptHookDecision.Continue;
            });
        }

        public bool TryHandlePlayerMagicCastAfter(PlayerObject player, PlayerMagicCastResult result)
        {
            if (player == null) return false;
            if (result == null) throw new ArgumentNullException(nameof(result));

            if (TryInvoke<OnPlayerMagicCastAfterHook>(ScriptHookKeys.OnPlayerMagicCastAfterSpell(result.Spell), h =>
            {
                h(_context, player, result);
                return true;
            }))
            {
                return true;
            }

            return TryInvoke<OnPlayerMagicCastAfterHook>(ScriptHookKeys.OnPlayerMagicCastAfter, h =>
            {
                h(_context, player, result);
                return true;
            });
        }

        public bool TryHandlePlayerDamageBefore(PlayerObject player, PlayerDamageRequest request, bool allowCSharp = true)
        {
            if (player == null) return false;
            if (request == null) throw new ArgumentNullException(nameof(request));

            var specificKey = request.Perspective == PlayerDamagePerspective.Outgoing
                ? ScriptHookKeys.OnPlayerDamageBeforeOut
                : ScriptHookKeys.OnPlayerDamageBeforeIn;

            bool cSharpInvoked = false;
            if (allowCSharp && HasHandler<OnPlayerDamageBeforeHook>(specificKey))
            {
                cSharpInvoked = true;
                if (TryInvoke<OnPlayerDamageBeforeHook>(specificKey, h =>
                {
                    h(_context, player, request);
                    return request.Decision != ScriptHookDecision.Continue;
                }))
                    return true;
            }

            if (allowCSharp && HasHandler<OnPlayerDamageBeforeHook>(ScriptHookKeys.OnPlayerDamageBefore))
            {
                cSharpInvoked = true;
                TryInvoke<OnPlayerDamageBeforeHook>(ScriptHookKeys.OnPlayerDamageBefore, h =>
                {
                    h(_context, player, request);
                    return request.Decision != ScriptHookDecision.Continue;
                });
            }

            if (cSharpInvoked)
                return request.Decision != ScriptHookDecision.Continue;

            return LingFengTxtSystemHookAdapter.TryDispatchPlayerDamageBefore(
                false, Envir.Main.TextFileProvider, player, request,
                allowCSharp && Settings.CSharpScriptsEnabled && Enabled);
        }

        public bool TryHandlePlayerDamageAfter(PlayerObject player, PlayerDamageResult result, bool allowCSharp = true)
        {
            if (player == null) return false;
            if (result == null) throw new ArgumentNullException(nameof(result));

            var specificKey = result.Perspective == PlayerDamagePerspective.Outgoing
                ? ScriptHookKeys.OnPlayerDamageAfterOut
                : ScriptHookKeys.OnPlayerDamageAfterIn;

            bool cSharpInvoked = false;
            bool cSharpHandled = false;
            bool specificSucceeded = false;
            if (allowCSharp && HasHandler<OnPlayerDamageAfterHook>(specificKey))
            {
                cSharpInvoked = true;
                specificSucceeded = cSharpHandled = TryInvoke<OnPlayerDamageAfterHook>(specificKey, h =>
                {
                    h(_context, player, result);
                    return true;
                });
            }
            if (!specificSucceeded && allowCSharp && HasHandler<OnPlayerDamageAfterHook>(ScriptHookKeys.OnPlayerDamageAfter))
            {
                cSharpInvoked = true;
                cSharpHandled = TryInvoke<OnPlayerDamageAfterHook>(ScriptHookKeys.OnPlayerDamageAfter, h =>
                {
                    h(_context, player, result);
                    return true;
                });
            }

            if (cSharpInvoked) return cSharpHandled;
            return LingFengTxtSystemHookAdapter.TryDispatchPlayerDamageAfter(
                false, Envir.Main.TextFileProvider, player, result,
                allowCSharp && Settings.CSharpScriptsEnabled && Enabled);
        }

        public bool TryHandlePlayerDeathPenaltyBefore(PlayerObject player, PlayerDeathPenaltyRequest request)
        {
            if (player == null) return false;
            if (request == null) throw new ArgumentNullException(nameof(request));

            return TryInvoke<OnPlayerDeathPenaltyBeforeHook>(ScriptHookKeys.OnPlayerDeathPenaltyBefore, h =>
            {
                h(_context, player, request);
                return request.Decision != ScriptHookDecision.Continue;
            });
        }

        public bool TryHandlePlayerDeathPenaltyAfter(PlayerObject player, PlayerDeathPenaltyResult result)
        {
            if (player == null) return false;
            if (result == null) throw new ArgumentNullException(nameof(result));

            return TryInvoke<OnPlayerDeathPenaltyAfterHook>(ScriptHookKeys.OnPlayerDeathPenaltyAfter, h =>
            {
                h(_context, player, result);
                return true;
            });
        }

        public bool TryHandlePlayerItemPickupCheck(PlayerObject player, PlayerItemPickupCheckRequest request)
        {
            if (player == null) return false;
            if (request == null) throw new ArgumentNullException(nameof(request));

            var itemIndex = request.Item?.Info?.Index ?? 0;
            if (itemIndex > 0)
            {
                if (TryInvoke<OnPlayerItemPickupCheckHook>(ScriptHookKeys.OnPlayerItemPickupCheckIndex(itemIndex), h =>
                {
                    h(_context, player, request);
                    return request.Decision != ScriptHookDecision.Continue;
                }))
                {
                    return true;
                }
            }

            return TryInvoke<OnPlayerItemPickupCheckHook>(ScriptHookKeys.OnPlayerItemPickupCheck, h =>
            {
                h(_context, player, request);
                return request.Decision != ScriptHookDecision.Continue;
            });
        }

        public bool TryHandlePlayerItemPickupAfter(PlayerObject player, PlayerItemPickupResult result, bool allowCSharp = true)
        {
            if (player == null) return false;
            if (result == null) throw new ArgumentNullException(nameof(result));

            int itemIndex = result.Item?.Info?.Index ?? 0;
            string specificKey = itemIndex > 0 ? ScriptHookKeys.OnPlayerItemPickupAfterIndex(itemIndex) : null;
            bool cSharpInvoked = false;
            bool cSharpHandled = false;
            bool specificSucceeded = false;

            if (allowCSharp && specificKey != null && HasHandler<OnPlayerItemPickupAfterHook>(specificKey))
            {
                cSharpInvoked = true;
                specificSucceeded = cSharpHandled = TryInvoke<OnPlayerItemPickupAfterHook>(specificKey, h =>
                {
                    h(_context, player, result);
                    return true;
                });
            }
            if (!specificSucceeded && allowCSharp && HasHandler<OnPlayerItemPickupAfterHook>(ScriptHookKeys.OnPlayerItemPickupAfter))
            {
                cSharpInvoked = true;
                cSharpHandled = TryInvoke<OnPlayerItemPickupAfterHook>(ScriptHookKeys.OnPlayerItemPickupAfter, h =>
                {
                    h(_context, player, result);
                    return true;
                });
            }

            if (cSharpInvoked) return cSharpHandled;
            return LingFengTxtSystemHookAdapter.TryDispatchPlayerItemPickupAfter(
                false, Envir.Main.TextFileProvider, player, result,
                allowCSharp && Settings.CSharpScriptsEnabled && Enabled);
        }

        public bool TryHandlePlayerItemUseCheck(PlayerObject player, PlayerItemUseCheckRequest request)
        {
            if (player == null) return false;
            if (request == null) throw new ArgumentNullException(nameof(request));

            var itemIndex = request.Item?.Info?.Index ?? 0;
            if (itemIndex > 0)
            {
                if (TryInvoke<OnPlayerItemUseCheckHook>(ScriptHookKeys.OnPlayerItemUseCheckIndex(itemIndex), h =>
                {
                    h(_context, player, request);
                    return request.Decision != ScriptHookDecision.Continue;
                }))
                {
                    return true;
                }
            }

            return TryInvoke<OnPlayerItemUseCheckHook>(ScriptHookKeys.OnPlayerItemUseCheck, h =>
            {
                h(_context, player, request);
                return request.Decision != ScriptHookDecision.Continue;
            });
        }

        public bool TryHandlePlayerCustomCommand(PlayerObject player, string command) =>
            TryInvoke<OnPlayerCustomCommandHook>(ScriptHookKeys.OnPlayerCustomCommand, h => h(_context, player, command));

        public bool TryHandlePlayerAcceptQuest(PlayerObject player, int questIndex) =>
            TryInvoke<OnPlayerAcceptQuestHook>(ScriptHookKeys.OnPlayerAcceptQuest, h => h(_context, player, questIndex));

        public bool TryHandlePlayerFinishQuest(PlayerObject player, int questIndex) =>
            TryInvoke<OnPlayerFinishQuestHook>(ScriptHookKeys.OnPlayerFinishQuest, h => h(_context, player, questIndex));

        public bool TryHandlePlayerDaily(PlayerObject player) =>
            TryInvoke<OnPlayerDailyHook>(ScriptHookKeys.OnPlayerDaily, h => h(_context, player));

        public bool TryHandleClientEvent(PlayerObject player, object payload) =>
            TryInvoke<OnClientEventHook>(ScriptHookKeys.OnClientEvent, h => h(_context, player, payload));

        public bool TryHandlePlayerTimerExpired(PlayerObject player, string timerKey, byte timerType)
        {
            timerKey = timerKey ?? string.Empty;

            if (timerKey.Length > 0)
            {
                if (TryInvoke<OnPlayerTimerExpiredHook>(ScriptHookKeys.OnPlayerTimerExpiredKey(timerKey), h => h(_context, player, timerKey, timerType)))
                    return true;
            }

            return TryInvoke<OnPlayerTimerExpiredHook>(ScriptHookKeys.OnPlayerTimerExpired, h => h(_context, player, timerKey, timerType));
        }

        public bool TryHandleMonsterSpawn(MonsterObject monster)
        {
            if (monster != null)
            {
                if (TryInvoke<OnMonsterSpawnHook>(ScriptHookKeys.OnMonsterSpawnIndex(monster.Info.Index), h => h(_context, monster)))
                    return true;
            }

            return TryInvoke<OnMonsterSpawnHook>(ScriptHookKeys.OnMonsterSpawn, h => h(_context, monster));
        }

        public bool TryHandleMonsterDie(MonsterObject monster, bool allowCSharp = true)
        {
            bool cSharpInvoked = false;
            bool cSharpHandled = false;
            if (monster != null)
            {
                string specificKey = ScriptHookKeys.OnMonsterDieIndex(monster.Info.Index);
                cSharpInvoked = allowCSharp && HasHandler<OnMonsterDieHook>(specificKey);
                if (cSharpInvoked)
                    cSharpHandled = TryInvoke<OnMonsterDieHook>(specificKey, h => h(_context, monster));
            }

            if (!cSharpHandled && allowCSharp && HasHandler<OnMonsterDieHook>(ScriptHookKeys.OnMonsterDie))
            {
                cSharpInvoked = true;
                cSharpHandled = TryInvoke<OnMonsterDieHook>(ScriptHookKeys.OnMonsterDie, h => h(_context, monster));
            }

            if (cSharpInvoked) return cSharpHandled;
            LingFengTxtSystemHookAdapter.TryDispatchMonsterDie(
                false, Envir.Main.TextFileProvider, monster,
                allowCSharp && Settings.CSharpScriptsEnabled && Enabled);
            // @KILLMON 是归属玩家的 QFunction 事件，不替代怪物自身的 [@_DIE(index)]。
            return false;
        }

        public bool TryHandleMonsterDropBefore(MonsterObject monster, MonsterDropRequest request)
        {
            if (monster == null) return false;
            if (request == null) throw new ArgumentNullException(nameof(request));

            if (TryInvoke<OnMonsterDropBeforeHook>(ScriptHookKeys.OnMonsterDropBeforeIndex(monster.Info.Index), h =>
            {
                h(_context, monster, request);
                return request.Decision != ScriptHookDecision.Continue;
            }))
            {
                return true;
            }

            return TryInvoke<OnMonsterDropBeforeHook>(ScriptHookKeys.OnMonsterDropBefore, h =>
            {
                h(_context, monster, request);
                return request.Decision != ScriptHookDecision.Continue;
            });
        }

        public bool TryHandleMonsterDropAfter(MonsterObject monster, MonsterDropResult result, bool allowCSharp = true)
        {
            if (monster == null) return false;
            if (result == null) throw new ArgumentNullException(nameof(result));

            string specificKey = ScriptHookKeys.OnMonsterDropAfterIndex(monster.Info.Index);
            bool cSharpInvoked = false;
            bool cSharpHandled = false;
            bool specificSucceeded = false;
            if (allowCSharp && HasHandler<OnMonsterDropAfterHook>(specificKey))
            {
                cSharpInvoked = true;
                specificSucceeded = cSharpHandled = TryInvoke<OnMonsterDropAfterHook>(specificKey, h =>
                {
                    h(_context, monster, result);
                    return true;
                });
            }
            if (!specificSucceeded && allowCSharp && HasHandler<OnMonsterDropAfterHook>(ScriptHookKeys.OnMonsterDropAfter))
            {
                cSharpInvoked = true;
                cSharpHandled = TryInvoke<OnMonsterDropAfterHook>(ScriptHookKeys.OnMonsterDropAfter, h =>
                {
                    h(_context, monster, result);
                    return true;
                });
            }

            if (cSharpInvoked) return cSharpHandled;
            return LingFengTxtSystemHookAdapter.TryDispatchMonsterDropAfter(
                false, Envir.Main.TextFileProvider, result,
                allowCSharp && Settings.CSharpScriptsEnabled && Enabled);
        }

        public bool TryHandleMonsterRespawnBefore(MonsterInfo monster, MonsterRespawnRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));

            if (monster != null)
            {
                if (TryInvoke<OnMonsterRespawnBeforeHook>(ScriptHookKeys.OnMonsterRespawnBeforeIndex(monster.Index), h =>
                {
                    h(_context, monster, request);
                    return request.Decision != ScriptHookDecision.Continue;
                }))
                {
                    return true;
                }
            }

            return TryInvoke<OnMonsterRespawnBeforeHook>(ScriptHookKeys.OnMonsterRespawnBefore, h =>
            {
                h(_context, monster, request);
                return request.Decision != ScriptHookDecision.Continue;
            });
        }

        public bool TryHandleMonsterRespawnAfter(MonsterInfo monster, MonsterRespawnResult result)
        {
            if (result == null) throw new ArgumentNullException(nameof(result));

            if (monster != null)
            {
                if (TryInvoke<OnMonsterRespawnAfterHook>(ScriptHookKeys.OnMonsterRespawnAfterIndex(monster.Index), h =>
                {
                    h(_context, monster, result);
                    return true;
                }))
                {
                    return true;
                }
            }

            return TryInvoke<OnMonsterRespawnAfterHook>(ScriptHookKeys.OnMonsterRespawnAfter, h =>
            {
                h(_context, monster, result);
                return true;
            });
        }

        public bool TryHandleMonsterAiTargetBefore(MonsterObject monster, MonsterAiTargetSelectRequest request)
        {
            if (monster == null) return false;
            if (request == null) throw new ArgumentNullException(nameof(request));

            if (TryInvoke<OnMonsterAiTargetBeforeHook>(ScriptHookKeys.OnMonsterAiTargetBeforeIndex(monster.Info.Index), h =>
            {
                h(_context, monster, request);
                return request.Decision != ScriptHookDecision.Continue;
            }))
            {
                return true;
            }

            return TryInvoke<OnMonsterAiTargetBeforeHook>(ScriptHookKeys.OnMonsterAiTargetBefore, h =>
            {
                h(_context, monster, request);
                return request.Decision != ScriptHookDecision.Continue;
            });
        }

        public bool TryHandleMonsterAiTargetAfter(MonsterObject monster, MonsterAiTargetSelectResult result)
        {
            if (monster == null) return false;
            if (result == null) throw new ArgumentNullException(nameof(result));

            if (TryInvoke<OnMonsterAiTargetAfterHook>(ScriptHookKeys.OnMonsterAiTargetAfterIndex(monster.Info.Index), h =>
            {
                h(_context, monster, result);
                return true;
            }))
            {
                return true;
            }

            return TryInvoke<OnMonsterAiTargetAfterHook>(ScriptHookKeys.OnMonsterAiTargetAfter, h =>
            {
                h(_context, monster, result);
                return true;
            });
        }

        public bool TryHandleMonsterAiSkillBefore(MonsterObject monster, MonsterAiSkillSelectRequest request)
        {
            if (monster == null) return false;
            if (request == null) throw new ArgumentNullException(nameof(request));

            if (TryInvoke<OnMonsterAiSkillBeforeHook>(ScriptHookKeys.OnMonsterAiSkillBeforeIndex(monster.Info.Index), h =>
            {
                h(_context, monster, request);
                return request.Decision != ScriptHookDecision.Continue;
            }))
            {
                return true;
            }

            return TryInvoke<OnMonsterAiSkillBeforeHook>(ScriptHookKeys.OnMonsterAiSkillBefore, h =>
            {
                h(_context, monster, request);
                return request.Decision != ScriptHookDecision.Continue;
            });
        }

        public bool TryHandleMonsterAiSkillAfter(MonsterObject monster, MonsterAiSkillSelectResult result)
        {
            if (monster == null) return false;
            if (result == null) throw new ArgumentNullException(nameof(result));

            if (TryInvoke<OnMonsterAiSkillAfterHook>(ScriptHookKeys.OnMonsterAiSkillAfterIndex(monster.Info.Index), h =>
            {
                h(_context, monster, result);
                return true;
            }))
            {
                return true;
            }

            return TryInvoke<OnMonsterAiSkillAfterHook>(ScriptHookKeys.OnMonsterAiSkillAfter, h =>
            {
                h(_context, monster, result);
                return true;
            });
        }

        public bool TryHandleMonsterAiMoveBefore(MonsterObject monster, MonsterAiMoveRequest request)
        {
            if (monster == null) return false;
            if (request == null) throw new ArgumentNullException(nameof(request));

            if (TryInvoke<OnMonsterAiMoveBeforeHook>(ScriptHookKeys.OnMonsterAiMoveBeforeIndex(monster.Info.Index), h =>
            {
                h(_context, monster, request);
                return request.Decision != ScriptHookDecision.Continue;
            }))
            {
                return true;
            }

            return TryInvoke<OnMonsterAiMoveBeforeHook>(ScriptHookKeys.OnMonsterAiMoveBefore, h =>
            {
                h(_context, monster, request);
                return request.Decision != ScriptHookDecision.Continue;
            });
        }

        public bool TryHandleMonsterAiMoveAfter(MonsterObject monster, MonsterAiMoveResult result)
        {
            if (monster == null) return false;
            if (result == null) throw new ArgumentNullException(nameof(result));

            if (TryInvoke<OnMonsterAiMoveAfterHook>(ScriptHookKeys.OnMonsterAiMoveAfterIndex(monster.Info.Index), h =>
            {
                h(_context, monster, result);
                return true;
            }))
            {
                return true;
            }

            return TryInvoke<OnMonsterAiMoveAfterHook>(ScriptHookKeys.OnMonsterAiMoveAfter, h =>
            {
                h(_context, monster, result);
                return true;
            });
        }

        public bool TryHandleNpcPage(PlayerObject player, string npcFileName, uint npcObjectID, int npcScriptID, string pageKey, out NpcPageCall call, out NpcDialog dialog)
        {
            call = null;
            dialog = null;

            if (player == null) return false;

            if (string.IsNullOrWhiteSpace(npcFileName))
                npcFileName = string.Empty;

            pageKey = pageKey ?? string.Empty;

            var definitionKey = ParseDefinitionKey(pageKey, out var args);

            var input = string.Empty;
            if (player.NPCData != null && player.NPCData.TryGetValue("NPCInputStr", out object value) && value != null)
                input = value.ToString() ?? string.Empty;

            call = new NpcPageCall(npcFileName, npcObjectID, npcScriptID, pageKey, definitionKey, args, input);

            // 优先尝试原始文件名；未命中时再尝试“别名”（仅对最后一个路径段做 - 插入/移除）。
            foreach (var candidateFileName in NpcFileNameAliases.Enumerate(npcFileName))
            {
                if (TryInvokeNpcPage(player, candidateFileName, pageKey, call, out dialog))
                    return true;

                if (!string.Equals(definitionKey, pageKey, StringComparison.CurrentCultureIgnoreCase))
                {
                    if (TryInvokeNpcPage(player, candidateFileName, definitionKey, call, out dialog))
                        return true;
                }
            }

            return false;
        }

        private bool TryInvokeNpcPage(PlayerObject player, string npcFileName, string pageKey, NpcPageCall call, out NpcDialog dialog)
        {
            dialog = null;

            NpcDialog handledDialog = null;

            var handled = TryInvoke<OnNpcPageHook>(ScriptHookKeys.OnNpcPage(npcFileName, pageKey), h =>
            {
                var d = new NpcDialog();
                var ok = h(_context, player, call, d);

                if (ok)
                {
                    d.ImportAllowedKeysFromLines();
                    handledDialog = d;
                }

                return ok;
            });

            if (!handled) return false;

            dialog = handledDialog ?? new NpcDialog();
            return true;
        }

        private static string ParseDefinitionKey(string pageKey, out IReadOnlyList<string> args)
        {
            args = Array.Empty<string>();

            if (string.IsNullOrWhiteSpace(pageKey)) return string.Empty;

            // 对齐 NPCPage.ArgumentParse：Default NPC page 不使用参数占位机制
            if (pageKey.StartsWith("[@_", StringComparison.OrdinalIgnoreCase))
                return pageKey;

            var r = new System.Text.RegularExpressions.Regex(@"\((.*)\)");
            var match = r.Match(pageKey);
            if (!match.Success) return pageKey;

            var strValues = match.Groups[1].Value;
            var arrValues = strValues.Split(',');

            var list = new List<string>(arrValues.Length);
            for (var i = 0; i < arrValues.Length; i++)
                list.Add(arrValues[i]);

            args = list;

            return System.Text.RegularExpressions.Regex.Replace(pageKey, r.ToString(), "()");
        }

        internal static void TryUnload(ScriptLoadContext context)
        {
            if (context == null) return;

            var weakRef = new WeakReference(context);

            try
            {
                context.Unload();
            }
            catch (Exception ex)
            {
                MessageQueue.Instance.Enqueue("[Scripts] Unload 失败：" + ex);
                return;
            }

            for (var i = 0; i < 5 && weakRef.IsAlive; i++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                Thread.Sleep(10);
            }

            if (weakRef.IsAlive)
            {
                MessageQueue.Instance.Enqueue("[Scripts] 脚本上下文未能及时卸载（可能仍被引用）。");
            }
        }

        public void Dispose()
        {
            Stop();
        }
    }
}
