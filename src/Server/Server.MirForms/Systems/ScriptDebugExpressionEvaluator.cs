using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;
using Server.MirEnvir;
using Server.MirObjects;
using Server.Scripting;

namespace Server.MirForms.Systems
{
    internal static class ScriptDebugExpressionEvaluator
    {
        private static readonly ScriptOptions Options = ScriptOptions.Default
            .AddReferences(
                typeof(object).Assembly,
                typeof(Enumerable).Assembly,
                typeof(Envir).Assembly,
                typeof(PlayerObject).Assembly,
                typeof(ScriptContext).Assembly)
            .AddImports(
                "System",
                "System.Linq",
                "Server",
                "Server.MirEnvir",
                "Server.MirObjects",
                "Server.Scripting");

        public static string Evaluate(string expression, object globals)
        {
            if (string.IsNullOrWhiteSpace(expression)) return "(empty)";
            if (globals == null) throw new ArgumentNullException(nameof(globals));

            try
            {
                var task = CSharpScript.EvaluateAsync<object?>(
                    expression,
                    Options,
                    globals,
                    globalsType: globals.GetType());

                task.Wait();

                var value = task.Result;
                return value == null ? "(null)" : value.ToString() ?? "(null)";
            }
            catch (CompilationErrorException cee)
            {
                return string.Join(Environment.NewLine, cee.Diagnostics.Select(d => d.ToString()));
            }
            catch (AggregateException ae)
            {
                return ae.Flatten().InnerException?.ToString() ?? ae.ToString();
            }
        }
    }
}

