using System.Text.Json;
using System.Text.RegularExpressions;
using ArkTerminal.Core.Profiles;

namespace ArkTerminal.Core.Agents;

public static class AgentRecipeRules
{
    public static bool ContainsSecret(string value) => Regex.IsMatch(value,
        @"(?i)(?:--(?:api[-_]?key|[a-z-]*token|password|secret))(?=$|[=\s""])|(?:\bapi[-_]?key|access[-_]?token|auth[-_]?token|password|secret)\s*[=:]|(?:\bBearer\s+)|(?:\bsk-[a-zA-Z0-9_-]{8,})|(?:\beyJ[a-zA-Z0-9_-]+\.[a-zA-Z0-9_-]+\.)",
        RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

    public static void ValidateBasic(AgentLaunchRecipe recipe)
    {
        if (!Enum.IsDefined(recipe.ShellBackend) || recipe.Arguments is null || recipe.Arguments.Count > AgentLimits.MaxRecipeArguments ||
            recipe.Arguments.Any(value => value is null || value.Length > AgentLimits.MaxArgumentCharacters || value.Any(char.IsControl)) ||
            recipe.Model?.Length > 128 || recipe.PermissionMode?.Length > 64)
            throw new ArgumentException("Agent 后端或参数超出限制。每项最多 2048 字符、最多 64 项，不能含控制字符。");
        var values = recipe.Arguments.Concat([recipe.Model ?? "", recipe.PermissionMode ?? ""]);
        if (values.Any(ContainsSecret)) throw new ArgumentException("配方不保存令牌、密码或 API key。请由 CLI 自身认证或继承进程环境。");
        if (!string.IsNullOrEmpty(recipe.Model) && !Regex.IsMatch(recipe.Model, @"\A[A-Za-z0-9][A-Za-z0-9._:/-]{0,127}\z"))
            throw new ArgumentException("模型须为模型标识符，不能包含脚本、凭据或空白。");
    }

    public static IReadOnlyList<string> BuildArguments(AgentDefinition definition, AgentLaunchRecipe recipe, string workingDirectory)
    {
        ValidateBasic(recipe);
        if (definition.Id != recipe.AgentId) throw new ArgumentException("配方与适配器 ID 不匹配。");
        if (!definition.Capabilities.InteractiveInput) throw new ArgumentException(definition.UnsupportedReason ?? "此适配器不支持交互终端。");
        var result = new List<string>();
        string Expand(string argument) => argument switch
        {
            "{{workingDirectory}}" => workingDirectory,
            "{{model}}" when !string.IsNullOrEmpty(recipe.Model) => recipe.Model,
            "{{model}}" => throw new ArgumentException("此模板要求填写模型。"),
            _ => argument
        };
        result.AddRange(definition.LaunchTemplate.Select(Expand));
        if (!string.IsNullOrEmpty(recipe.Model))
        {
            if (definition.Fields.Model.Count == 0) throw new ArgumentException("此适配器没有已核实的模型参数。");
            result.AddRange(definition.Fields.Model.Select(Expand));
        }
        if (!string.IsNullOrEmpty(recipe.PermissionMode))
        {
            if (!definition.Fields.PermissionModes.TryGetValue(recipe.PermissionMode, out var template))
                throw new ArgumentException("权限模式未在适配器中声明；请留空使用 CLI 原有设置，或选择已列出的模式。");
            result.AddRange(template.Select(Expand));
        }
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < recipe.Arguments.Count; index++)
        {
            var argument = recipe.Arguments[index];
            var option = definition.Fields.Arguments.FirstOrDefault(item => item.Flag == argument)
                ?? throw new ArgumentException("附加参数不在此适配器白名单中；不能保存未知开关或初始任务正文。");
            if (!seen.Add(argument) && !option.Repeatable) throw new ArgumentException("附加参数重复。");
            result.Add(argument);
            if (!option.TakesValue) continue;
            if (++index >= recipe.Arguments.Count) throw new ArgumentException("附加参数缺少值。");
            var value = recipe.Arguments[index];
            if (option.AllowedValues.Count > 0 && !option.AllowedValues.Contains(value, StringComparer.Ordinal))
                throw new ArgumentException("附加参数值不在白名单中。");
            result.Add(value);
        }
        if (result.Count > AgentLimits.MaxRecipeArguments || result.Sum(value => value.Length) > AgentLimits.MaxExpandedCharacters)
            throw new ArgumentException("展开后的启动参数超过 64 项或 8192 字符。");
        return result;
    }

    // Display is JSON, never an executable shell command or a log entry.
    public static string Preview(string executable, IEnumerable<string> arguments) =>
        JsonSerializer.Serialize(new { executable, arguments = arguments.ToArray() }, new JsonSerializerOptions
        { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
}
