using System.Collections.Immutable;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using RoslynPad.Roslyn;

namespace Server.MirForms.Systems
{
    internal static class RoslynPadScriptHost
    {
        public static RoslynHost CreateHost(bool debugBuild)
        {
            var additionalAssemblies = new[]
            {
                Assembly.Load("RoslynPad.Roslyn.Windows"),
                Assembly.Load("RoslynPad.Editor.Windows"),
            };

            var references = CreateScriptReferences();

            return new ScriptRoslynHost(additionalAssemblies, references, debugBuild);
        }

        public static RoslynHostReferences CreateScriptReferences()
        {
            var trustedPlatformAssemblies = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string;
            var assemblyPaths = string.IsNullOrWhiteSpace(trustedPlatformAssemblies)
                ? Array.Empty<string>()
                : trustedPlatformAssemblies.Split(Path.PathSeparator)
                    .Where(path => !string.IsNullOrWhiteSpace(path))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();

            return RoslynHostReferences.NamespaceDefault.With(
                assemblyReferences:
                [
                    typeof(object).Assembly,
                    typeof(Enumerable).Assembly,
                    typeof(Server.Scripting.ScriptManager).Assembly,
                    typeof(global::Globals).Assembly,
                ],
                assemblyPathReferences: assemblyPaths);
        }

        private sealed class ScriptRoslynHost : RoslynHost
        {
            private readonly bool _debugBuild;
            private bool _addedAnalyzers;

            public ScriptRoslynHost(
                IEnumerable<Assembly>? additionalAssemblies,
                RoslynHostReferences? references,
                bool debugBuild)
                : base(additionalAssemblies, references)
            {
                _debugBuild = debugBuild;
            }

            protected override ParseOptions CreateDefaultParseOptions()
            {
                return new CSharpParseOptions(
                    languageVersion: LanguageVersion.Latest,
                    preprocessorSymbols: _debugBuild ? new[] { "DEBUG", "SCRIPTING" } : new[] { "SCRIPTING" });
            }

            protected override IEnumerable<AnalyzerReference> GetSolutionAnalyzerReferences()
            {
                if (!_addedAnalyzers)
                {
                    _addedAnalyzers = true;
                    return base.GetSolutionAnalyzerReferences();
                }

                return Enumerable.Empty<AnalyzerReference>();
            }
        }
    }
}

