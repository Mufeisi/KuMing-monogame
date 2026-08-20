using System.Text;

namespace Server.Scripting.Ai
{
    public sealed class ScriptPromptTemplateBuilder
    {
        public string BuildPrompt(ScriptGenerationRequest request, ScriptGenerationContext context)
        {
            request ??= new ScriptGenerationRequest();
            context ??= new ScriptGenerationContext();

            var sb = new StringBuilder();
            sb.AppendLine("你是《Lom_mir2_net8ZH》的服务器脚本生成助手。");
            sb.AppendLine("请严格生成一个可编译的 C# 脚本模块。");
            sb.AppendLine();
            sb.AppendLine("输出约束：");
            sb.AppendLine("1. 只输出一个 .cs 模块，不要解释。");
            sb.AppendLine("2. 必须包含命名空间、类、ScriptModuleAttribute，以及可直接注册的脚本逻辑。");
            sb.AppendLine("3. 目标 Key 必须与请求一致，且目录/文件名需要与 Key 对应。");
            sb.AppendLine("4. 代码必须兼容现有 Server.Scripting API，不要引入未知程序集。");
            sb.AppendLine();
            sb.AppendLine("禁止项：");
            sb.AppendLine("1. 不允许访问文件系统、网络、进程、注册表或危险反射。");
            sb.AppendLine("2. 不允许调用与脚本沙箱无关的系统 API。");
            sb.AppendLine("3. 不允许输出多个类文件、伪代码或 TODO 占位。");
            sb.AppendLine();
            sb.AppendLine("生成目标：");
            sb.AppendLine($"- 类型: {request.Kind}");
            sb.AppendLine($"- 目标 Key: {request.TargetKey}");
            sb.AppendLine($"- 自然语言需求: {request.NaturalLanguageDescription}");

            if (!string.IsNullOrWhiteSpace(request.AdditionalRequirements))
                sb.AppendLine($"- 附加要求: {request.AdditionalRequirements}");

            if (!string.IsNullOrWhiteSpace(request.ExistingScriptContent))
            {
                sb.AppendLine();
                sb.AppendLine("现有脚本参考：");
                sb.AppendLine(request.ExistingScriptContent);
            }

            if (request.Diagnostics.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("编译诊断参考：");
                foreach (var diagnostic in request.Diagnostics)
                    sb.AppendLine("- " + diagnostic);
            }

            if (context.Documentation.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("文档上下文：");
                foreach (var snippet in context.Documentation)
                {
                    sb.AppendLine($"### {snippet.Source}");
                    sb.AppendLine(snippet.Content);
                    sb.AppendLine();
                }
            }

            return sb.ToString().Trim();
        }
    }
}
