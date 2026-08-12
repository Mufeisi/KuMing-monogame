using System.Diagnostics;
using System.Reflection;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.Text;

namespace Server.Scripting
{
    public sealed class ScriptCompiler
    {
        private static readonly Encoding Utf8Strict = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
        private static readonly Lazy<IReadOnlyList<MetadataReference>> CachedMetadataReferences = new Lazy<IReadOnlyList<MetadataReference>>(
            CollectMetadataReferencesCore,
            isThreadSafe: true);

        private sealed class CachedSyntaxTree
        {
            public DateTime LastWriteTimeUtc;
            public long Length;
            public SyntaxTree Tree;
        }

        private readonly object _gate = new object();
        private string _cachedScriptsRootPath = string.Empty;
        private bool _cachedDebugBuild;
        private CSharpParseOptions _cachedParseOptions;
        private CSharpCompilationOptions _cachedCompilationOptions;
        private CSharpCompilation _cachedBaseCompilation;
        private IReadOnlyList<MetadataReference> _cachedReferences;
        private readonly Dictionary<string, CachedSyntaxTree> _cachedTreesByPath = new Dictionary<string, CachedSyntaxTree>(StringComparer.OrdinalIgnoreCase);

        public ScriptCompileResult CompileFromDirectory(string scriptsRootPath, string assemblyName, bool debugBuild)
        {
            if (string.IsNullOrWhiteSpace(scriptsRootPath))
                throw new ArgumentException("脚本根目录不能为空。", nameof(scriptsRootPath));

            var scriptFiles = Directory.GetFiles(scriptsRootPath, "*.cs", SearchOption.AllDirectories);
            return CompileCore(scriptsRootPath, scriptFiles, assemblyName, debugBuild, resetCacheWhenEmpty: true);
        }

        public ScriptCompileResult CompileInstrumentedFromDirectory(string scriptsRootPath, string assemblyName, bool debugBuild)
        {
            if (string.IsNullOrWhiteSpace(scriptsRootPath))
                throw new ArgumentException("脚本根目录不能为空。", nameof(scriptsRootPath));

            var scriptFiles = Directory.GetFiles(scriptsRootPath, "*.cs", SearchOption.AllDirectories);
            return CompileInstrumentedCore(scriptsRootPath, scriptFiles, assemblyName, debugBuild, resetCacheWhenEmpty: true);
        }

        public ScriptCompileResult CompileFromFiles(string scriptsRootPath, IEnumerable<string> scriptFiles, string assemblyName, bool debugBuild)
        {
            if (string.IsNullOrWhiteSpace(scriptsRootPath))
                throw new ArgumentException("脚本根目录不能为空。", nameof(scriptsRootPath));
            if (scriptFiles == null)
                throw new ArgumentNullException(nameof(scriptFiles));

            return CompileCore(scriptsRootPath, scriptFiles, assemblyName, debugBuild, resetCacheWhenEmpty: false);
        }

        public ScriptCompileResult CompileInstrumentedFromFiles(string scriptsRootPath, IEnumerable<string> scriptFiles, string assemblyName, bool debugBuild)
        {
            if (string.IsNullOrWhiteSpace(scriptsRootPath))
                throw new ArgumentException("脚本根目录不能为空。", nameof(scriptsRootPath));
            if (scriptFiles == null)
                throw new ArgumentNullException(nameof(scriptFiles));

            return CompileInstrumentedCore(scriptsRootPath, scriptFiles, assemblyName, debugBuild, resetCacheWhenEmpty: false);
        }

        private ScriptCompileResult CompileInstrumentedCore(string scriptsRootPath, IEnumerable<string> scriptFiles, string assemblyName, bool debugBuild, bool resetCacheWhenEmpty)
        {
            var sw = Stopwatch.StartNew();

            lock (_gate)
            {
                var normalizedScriptsRootPath = Path.GetFullPath(scriptsRootPath);
                var normalizedScriptFiles = scriptFiles
                    .Where(path => !string.IsNullOrWhiteSpace(path))
                    .Select(Path.GetFullPath)
                    .Where(File.Exists)
                    .Where(path => ShouldIncludeScriptFile(normalizedScriptsRootPath, path))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                if (normalizedScriptFiles.Length == 0)
                {
                    if (resetCacheWhenEmpty)
                        ResetCacheFor(normalizedScriptsRootPath, debugBuild);

                    sw.Stop();
                    return new ScriptCompileResult(
                        hasScripts: false,
                        success: true,
                        assemblyName: assemblyName,
                        assemblyBytes: Array.Empty<byte>(),
                        pdbBytes: Array.Empty<byte>(),
                        diagnostics: Array.Empty<ScriptDiagnostic>(),
                        elapsedMilliseconds: sw.ElapsedMilliseconds);
                }

                Array.Sort(normalizedScriptFiles, StringComparer.OrdinalIgnoreCase);

                var parseOptions = new CSharpParseOptions(
                    languageVersion: LanguageVersion.Latest,
                    preprocessorSymbols: debugBuild ? new[] { "DEBUG", "SCRIPTING" } : new[] { "SCRIPTING" });

                var references = CollectMetadataReferences();
                var compilationOptions = new CSharpCompilationOptions(
                    outputKind: OutputKind.DynamicallyLinkedLibrary,
                    optimizationLevel: debugBuild ? OptimizationLevel.Debug : OptimizationLevel.Release,
                    nullableContextOptions: NullableContextOptions.Disable);

                var syntaxTrees = new List<SyntaxTree>(normalizedScriptFiles.Length);

                for (var i = 0; i < normalizedScriptFiles.Length; i++)
                {
                    var path = normalizedScriptFiles[i];
                    var sourceText = ReadSourceTextWithRetry(path);

                    var tree = CSharpSyntaxTree.ParseText(sourceText, parseOptions, path: path);
                    var instrumenter = new global::Server.Scripting.Debug.ScriptDebugInstrumenter(path);
                    var instrumentedTree = instrumenter.Instrument(tree);

                    syntaxTrees.Add(instrumentedTree);
                }

                using var peStream = new MemoryStream();
                using var pdbStream = new MemoryStream();

                var emitOptions = new EmitOptions(debugInformationFormat: DebugInformationFormat.PortablePdb);
                var compilation = CSharpCompilation.Create(
                    assemblyName: assemblyName,
                    syntaxTrees: syntaxTrees,
                    references: references,
                    options: compilationOptions);

                var emitResult = compilation.Emit(
                    peStream: peStream,
                    pdbStream: pdbStream,
                    options: emitOptions);

                var diagnostics = ConvertDiagnostics(emitResult.Diagnostics);

                sw.Stop();

                if (!emitResult.Success)
                {
                    return new ScriptCompileResult(
                        hasScripts: true,
                        success: false,
                        assemblyName: assemblyName,
                        assemblyBytes: Array.Empty<byte>(),
                        pdbBytes: Array.Empty<byte>(),
                        diagnostics: diagnostics,
                        elapsedMilliseconds: sw.ElapsedMilliseconds);
                }

                return new ScriptCompileResult(
                    hasScripts: true,
                    success: true,
                    assemblyName: assemblyName,
                    assemblyBytes: peStream.ToArray(),
                    pdbBytes: pdbStream.ToArray(),
                    diagnostics: diagnostics,
                    elapsedMilliseconds: sw.ElapsedMilliseconds);
            }
        }

        private ScriptCompileResult CompileCore(string scriptsRootPath, IEnumerable<string> scriptFiles, string assemblyName, bool debugBuild, bool resetCacheWhenEmpty)
        {
            var sw = Stopwatch.StartNew();

            lock (_gate)
            {
                var normalizedScriptsRootPath = Path.GetFullPath(scriptsRootPath);
                var normalizedScriptFiles = scriptFiles
                    .Where(path => !string.IsNullOrWhiteSpace(path))
                    .Select(Path.GetFullPath)
                    .Where(File.Exists)
                    .Where(path => ShouldIncludeScriptFile(normalizedScriptsRootPath, path))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                if (normalizedScriptFiles.Length == 0)
                {
                    if (resetCacheWhenEmpty)
                        ResetCacheFor(normalizedScriptsRootPath, debugBuild);

                    sw.Stop();
                    return new ScriptCompileResult(
                        hasScripts: false,
                        success: true,
                        assemblyName: assemblyName,
                        assemblyBytes: Array.Empty<byte>(),
                        pdbBytes: Array.Empty<byte>(),
                        diagnostics: Array.Empty<ScriptDiagnostic>(),
                        elapsedMilliseconds: sw.ElapsedMilliseconds);
                }

                Array.Sort(normalizedScriptFiles, StringComparer.OrdinalIgnoreCase);

                EnsureCompileOptions(normalizedScriptsRootPath, debugBuild);

                // 计算差异并增量更新 syntax trees + compilation（大规模脚本时可显著降低热更编译耗时）。
                var currentPaths = new HashSet<string>(normalizedScriptFiles, StringComparer.OrdinalIgnoreCase);

                var removedTrees = new List<SyntaxTree>();

                var cachedPaths = _cachedTreesByPath.Keys.ToArray();
                for (var i = 0; i < cachedPaths.Length; i++)
                {
                    var path = cachedPaths[i];
                    if (currentPaths.Contains(path))
                        continue;

                    if (_cachedTreesByPath.TryGetValue(path, out var removed) && removed?.Tree != null)
                        removedTrees.Add(removed.Tree);

                    _cachedTreesByPath.Remove(path);
                }

                var addedTrees = new List<SyntaxTree>();
                var replacedTrees = new List<(SyntaxTree OldTree, SyntaxTree NewTree)>();
                var syntaxTrees = new List<SyntaxTree>(normalizedScriptFiles.Length);

                for (var i = 0; i < normalizedScriptFiles.Length; i++)
                {
                    var path = normalizedScriptFiles[i];

                    DateTime lastWriteTimeUtc;
                    long length;

                    try
                    {
                        var info = new FileInfo(path);
                        lastWriteTimeUtc = info.LastWriteTimeUtc;
                        length = info.Length;
                    }
                    catch
                    {
                        lastWriteTimeUtc = DateTime.MinValue;
                        length = -1;
                    }

                    if (_cachedTreesByPath.TryGetValue(path, out var cached) &&
                        cached != null &&
                        cached.Tree != null &&
                        cached.LastWriteTimeUtc == lastWriteTimeUtc &&
                        cached.Length == length)
                    {
                        syntaxTrees.Add(cached.Tree);
                        continue;
                    }

                    var sourceText = ReadSourceTextWithRetry(path);
                    var tree = CSharpSyntaxTree.ParseText(sourceText, _cachedParseOptions, path: path);

                    if (cached == null)
                    {
                        cached = new CachedSyntaxTree();
                        _cachedTreesByPath[path] = cached;
                        addedTrees.Add(tree);
                    }
                    else
                    {
                        if (cached.Tree != null)
                            replacedTrees.Add((cached.Tree, tree));
                    }

                    cached.LastWriteTimeUtc = lastWriteTimeUtc;
                    cached.Length = length;
                    cached.Tree = tree;

                    syntaxTrees.Add(tree);
                }

                if (_cachedBaseCompilation == null)
                {
                    _cachedBaseCompilation = CSharpCompilation.Create(
                        assemblyName: "LomScripts_Base",
                        syntaxTrees: syntaxTrees,
                        references: _cachedReferences,
                        options: _cachedCompilationOptions);
                }
                else
                {
                    Compilation updated = _cachedBaseCompilation;

                    if (removedTrees.Count > 0)
                        updated = updated.RemoveSyntaxTrees(removedTrees);

                    for (var i = 0; i < replacedTrees.Count; i++)
                    {
                        var pair = replacedTrees[i];
                        updated = updated.ReplaceSyntaxTree(pair.OldTree, pair.NewTree);
                    }

                    if (addedTrees.Count > 0)
                        updated = updated.AddSyntaxTrees(addedTrees);

                    _cachedBaseCompilation = (CSharpCompilation)updated;
                }

                using var peStream = new MemoryStream();
                using var pdbStream = new MemoryStream();

                var emitOptions = new EmitOptions(debugInformationFormat: DebugInformationFormat.PortablePdb);
                var compilation = _cachedBaseCompilation.WithAssemblyName(assemblyName);
                var emitResult = compilation.Emit(
                    peStream: peStream,
                    pdbStream: pdbStream,
                    options: emitOptions);

                var diagnostics = ConvertDiagnostics(emitResult.Diagnostics);

                sw.Stop();

                if (!emitResult.Success)
                {
                    return new ScriptCompileResult(
                        hasScripts: true,
                        success: false,
                        assemblyName: assemblyName,
                        assemblyBytes: Array.Empty<byte>(),
                        pdbBytes: Array.Empty<byte>(),
                        diagnostics: diagnostics,
                        elapsedMilliseconds: sw.ElapsedMilliseconds);
                }

                return new ScriptCompileResult(
                    hasScripts: true,
                    success: true,
                    assemblyName: assemblyName,
                    assemblyBytes: peStream.ToArray(),
                    pdbBytes: pdbStream.ToArray(),
                    diagnostics: diagnostics,
                    elapsedMilliseconds: sw.ElapsedMilliseconds);
            }
        }

        private void EnsureCompileOptions(string scriptsRootPath, bool debugBuild)
        {
            if (string.Equals(_cachedScriptsRootPath, scriptsRootPath, StringComparison.OrdinalIgnoreCase) &&
                _cachedDebugBuild == debugBuild &&
                _cachedBaseCompilation != null &&
                _cachedParseOptions != null &&
                _cachedCompilationOptions != null &&
                _cachedReferences != null)
            {
                return;
            }

            ResetCacheFor(scriptsRootPath, debugBuild);

            _cachedParseOptions = new CSharpParseOptions(
                languageVersion: LanguageVersion.Latest,
                preprocessorSymbols: debugBuild ? new[] { "DEBUG", "SCRIPTING" } : new[] { "SCRIPTING" });

            _cachedReferences = CollectMetadataReferences();

            _cachedCompilationOptions = new CSharpCompilationOptions(
                outputKind: OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: debugBuild ? OptimizationLevel.Debug : OptimizationLevel.Release,
                nullableContextOptions: NullableContextOptions.Disable);
        }

        private void ResetCacheFor(string scriptsRootPath, bool debugBuild)
        {
            _cachedScriptsRootPath = scriptsRootPath ?? string.Empty;
            _cachedDebugBuild = debugBuild;
            _cachedTreesByPath.Clear();
            _cachedBaseCompilation = null;
        }

        private static bool ShouldIncludeScriptFile(string scriptsRootPath, string scriptFilePath)
        {
            if (string.IsNullOrWhiteSpace(scriptsRootPath) || string.IsNullOrWhiteSpace(scriptFilePath))
                return false;

            try
            {
                var root = Path.GetFullPath(scriptsRootPath)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;

                var fullPath = Path.GetFullPath(scriptFilePath);

                if (!fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                    return false;

                var relative = fullPath.Substring(root.Length);
                var segments = relative.Split(
                    [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                    StringSplitOptions.RemoveEmptyEntries);

                // 约定：脚本根目录下以 "_" 开头的目录为辅助目录（如 _history/_ai_audit），不参与脚本编译。
                // 否则会导致历史快照等功能写入的 .cs 被再次编译，引发重复定义/误注册等问题。
                for (var i = 0; i < segments.Length - 1; i++)
                {
                    if (string.Equals(segments[i], "_LegacyTxt", StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (segments[i].StartsWith("_", StringComparison.Ordinal))
                        return false;
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        private static IReadOnlyList<MetadataReference> CollectMetadataReferences()
        {
            return CachedMetadataReferences.Value;
        }

        private static IReadOnlyList<MetadataReference> CollectMetadataReferencesCore()
        {
            var references = new List<MetadataReference>();
            var added = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            AddReferenceFromAssemblyLocation(references, added, typeof(object).Assembly);
            AddReferenceFromAssemblyLocation(references, added, typeof(Enumerable).Assembly);
            AddReferenceFromAssemblyLocation(references, added, typeof(ScriptManager).Assembly);
            AddReferenceFromAssemblyLocation(references, added, typeof(global::Globals).Assembly);

            var tpa = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string;
            if (!string.IsNullOrWhiteSpace(tpa))
            {
                var paths = tpa.Split(Path.PathSeparator);
                for (var i = 0; i < paths.Length; i++)
                {
                    var path = paths[i];
                    if (string.IsNullOrWhiteSpace(path)) continue;
                    if (!added.Add(path)) continue;
                    if (!File.Exists(path)) continue;

                    references.Add(MetadataReference.CreateFromFile(path));
                }
            }

            return references.ToArray();
        }

        private static void AddReferenceFromAssemblyLocation(List<MetadataReference> references, HashSet<string> added, Assembly assembly)
        {
            if (assembly == null) return;

            var location = assembly.Location;
            if (string.IsNullOrWhiteSpace(location)) return;
            if (!File.Exists(location)) return;
            if (!added.Add(location)) return;

            references.Add(MetadataReference.CreateFromFile(location));
        }

        private static SourceText ReadSourceTextWithRetry(string path)
        {
            const int maxRetries = 5;
            const int delayMs = 80;

            for (var attempt = 0; attempt < maxRetries; attempt++)
            {
                try
                {
                    var bytes = ReadAllBytesShared(path);

                    try
                    {
                        var text = Utf8Strict.GetString(bytes);
                        if (text.Length > 0 && text[0] == '\uFEFF')
                        {
                            text = text.Substring(1);
                        }
                        return SourceText.From(text, Utf8Strict);
                    }
                    catch (DecoderFallbackException)
                    {
                        var gbk = Encoding.GetEncoding(936);
                        var text = gbk.GetString(bytes);
                        return SourceText.From(text, gbk);
                    }
                }
                catch (IOException)
                {
                    Thread.Sleep(delayMs);
                }
                catch (UnauthorizedAccessException)
                {
                    Thread.Sleep(delayMs);
                }
            }

            // 最后一试：抛出异常以便上层记录诊断
            var lastBytes = ReadAllBytesShared(path);
            var lastText = Utf8Strict.GetString(lastBytes);
            if (lastText.Length > 0 && lastText[0] == '\uFEFF')
            {
                lastText = lastText.Substring(1);
            }
            return SourceText.From(lastText, Utf8Strict);
        }

        private static byte[] ReadAllBytesShared(string path)
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            return ms.ToArray();
        }

        private static IReadOnlyList<ScriptDiagnostic> ConvertDiagnostics(IEnumerable<Diagnostic> diagnostics)
        {
            var list = new List<ScriptDiagnostic>();

            foreach (var d in diagnostics)
            {
                var location = d.Location;
                var filePath = string.Empty;
                var line = 0;
                var column = 0;

                if (location != Location.None && location.IsInSource)
                {
                    var span = location.GetLineSpan();
                    filePath = span.Path ?? string.Empty;
                    line = span.StartLinePosition.Line + 1;
                    column = span.StartLinePosition.Character + 1;
                }

                list.Add(new ScriptDiagnostic(
                    id: d.Id,
                    severity: d.Severity.ToString(),
                    message: d.GetMessage(),
                    filePath: filePath,
                    line: line,
                    column: column));
            }

            return list;
        }
    }
}
