using System.Text;

namespace Server.Scripting.Ai
{
    public sealed class ScriptDraftScaffolder
    {
        public ScriptDraft CreateDraft(ScriptGenerationRequest request, ScriptGenerationContext context, string prompt, string providerName, string modelName)
        {
            request ??= new ScriptGenerationRequest();
            context ??= new ScriptGenerationContext();

            var relativePath = BuildRelativePath(request.TargetKey);
            var moduleKey = request.TargetKey?.Trim().Replace('\\', '/') ?? string.Empty;
            var namespaceName = BuildNamespace(moduleKey);
            var className = BuildClassName(relativePath);
            var generatedCode = BuildCode(request, moduleKey, namespaceName, className);

            return new ScriptDraft
            {
                Success = true,
                ProviderName = providerName,
                ModelName = modelName,
                Prompt = prompt ?? string.Empty,
                GeneratedCode = generatedCode,
                SuggestedRelativePath = relativePath,
                Warnings = BuildWarnings(context),
            };
        }

        private static IReadOnlyList<string> BuildWarnings(ScriptGenerationContext context)
        {
            var warnings = new List<string>();
            if (context.Options != null && !context.Options.Enabled)
                warnings.Add("当前为离线模板草稿，尚未接入真实 AI Provider。");
            return warnings;
        }

        private static string BuildRelativePath(string targetKey)
        {
            if (string.IsNullOrWhiteSpace(targetKey))
                return Path.Combine("Generated", "NewScript.cs");

            var normalized = targetKey.Trim().Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
            return normalized + ".cs";
        }

        private static string BuildNamespace(string moduleKey)
        {
            var firstSegment = (moduleKey ?? string.Empty)
                .Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(SanitizeIdentifier)
                .FirstOrDefault(segment => !string.IsNullOrWhiteSpace(segment));

            if (string.IsNullOrWhiteSpace(firstSegment))
                return "LomScripts.Generated";

            if (!char.IsLetter(firstSegment[0]) && firstSegment[0] != '_')
                firstSegment = "_" + firstSegment;

            return "LomScripts." + firstSegment;
        }

        private static string BuildClassName(string relativePath)
        {
            var name = Path.GetFileNameWithoutExtension(relativePath) ?? "GeneratedScript";
            var sanitized = SanitizeIdentifier(name);
            if (string.IsNullOrWhiteSpace(sanitized))
                return "GeneratedScript";

            if (!char.IsLetter(sanitized[0]) && sanitized[0] != '_')
                sanitized = "_" + sanitized;

            return sanitized;
        }

        private static string BuildCode(ScriptGenerationRequest request, string moduleKey, string namespaceName, string className)
        {
            var sb = new StringBuilder();
            sb.AppendLine("using Server.MirObjects;");
            sb.AppendLine("using Server.Scripting;");
            sb.AppendLine();
            sb.AppendLine($"namespace {namespaceName}");
            sb.AppendLine("{");
            sb.AppendLine($"    [ScriptModule(\"{moduleKey}\")]");
            sb.AppendLine($"    public sealed class {className} : IScriptModule");
            sb.AppendLine("    {");
            sb.AppendLine("        public void Register(ScriptRegistry registry)");
            sb.AppendLine("        {");

            foreach (var line in BuildRegisterLines(request.Kind))
                sb.AppendLine("            " + line);

            sb.AppendLine("        }");
            sb.AppendLine();

            foreach (var block in BuildHandlerBlocks(request))
            {
                foreach (var line in block)
                    sb.AppendLine("        " + line);
                sb.AppendLine();
            }

            sb.AppendLine("    }");
            sb.AppendLine("}");
            return sb.ToString().TrimEnd();
        }

        private static IReadOnlyList<string> BuildRegisterLines(ScriptGenerationKind kind)
        {
            return kind switch
            {
                ScriptGenerationKind.Dialog => new[] { "registry.RegisterOnPlayerTrigger(OnTrigger);", "// 可按需改为 NPC 页面 / 输入处理模块" },
                ScriptGenerationKind.DropTable => new[] { "var table = new DropTableDefinition(\"Drops/Example\");", "// TODO: 按需求填充掉落表", "registry.Drops.Register(table);" },
                ScriptGenerationKind.Quest => new[] { "registry.RegisterOnPlayerAcceptQuest(OnAcceptQuest);", "registry.RegisterOnPlayerFinishQuest(OnFinishQuest);" },
                ScriptGenerationKind.Route => new[] { "// TODO: 构建 RouteDefinition 并注册到 registry.Routes" },
                ScriptGenerationKind.ValueTable => new[] { "// TODO: 构建 ValueTableDefinition 并注册到 registry.Values" },
                ScriptGenerationKind.NameList => new[] { "// TODO: 构建 NameListDefinition 并注册到 registry.NameLists" },
                ScriptGenerationKind.TextFile => new[] { "// TODO: 构建 TextFileDefinition 并注册到 registry.TextFiles" },
                _ => new[] { "registry.RegisterOnPlayerTrigger(OnTrigger);" },
            };
        }

        private static IReadOnlyList<string[]> BuildHandlerBlocks(ScriptGenerationRequest request)
        {
            return request.Kind switch
            {
                ScriptGenerationKind.Quest => new[]
                {
                    new[]
                    {
                        "private static bool OnAcceptQuest(ScriptContext context, PlayerObject player, int questIndex)",
                        "{",
                        $"    context?.Log(\"[Scripts][AiDraft] AcceptQuest: {EscapeForString(request.TargetKey)} index=\" + questIndex);",
                        "    return false;",
                        "}",
                    },
                    new[]
                    {
                        "private static bool OnFinishQuest(ScriptContext context, PlayerObject player, int questIndex)",
                        "{",
                        $"    context?.Log(\"[Scripts][AiDraft] FinishQuest: {EscapeForString(request.TargetKey)} index=\" + questIndex);",
                        "    return false;",
                        "}",
                    },
                },
                _ => new[]
                {
                    new[]
                    {
                        "private static bool OnTrigger(ScriptContext context, PlayerObject player, string triggerKey)",
                        "{",
                        $"    context?.Log(\"[Scripts][AiDraft] Trigger: {EscapeForString(request.TargetKey)} trigger=\" + triggerKey);",
                        "    return false;",
                        "}",
                    },
                },
            };
        }

        private static string SanitizeIdentifier(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            var sb = new StringBuilder(value.Length);
            foreach (var ch in value)
            {
                if (char.IsLetterOrDigit(ch) || ch == '_')
                    sb.Append(ch);
                else
                    sb.Append('_');
            }

            return sb.ToString();
        }

        private static string EscapeForString(string value)
        {
            return (value ?? string.Empty).Replace("\\", "\\\\").Replace("\"", "\\\"");
        }
    }
}
