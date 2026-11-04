using ArkTerminal.Core.Profiles;
using ArkTerminal.Core.Validation;

namespace ArkTerminal.Core.Configuration;

public static class ConfigurationValidator
{
    public static ValidationResult ValidateSettings(AppSettings? settings, IReadOnlyCollection<string>? profileIds = null)
    {
        if (settings is null)
        {
            return Invalid("settings", "配置不能为空。");
        }

        var issues = new List<ValidationIssue>();
        if (settings.SchemaVersion != ConfigurationDefaults.CurrentSchemaVersion)
        {
            issues.Add(new("schemaVersion", "配置版本不受支持。"));
        }

        if (settings.Appearance is null)
        {
            issues.Add(new("appearance", "外观配置不能为空。"));
        }
        else
        {
            ValidateFont(settings.Appearance.UiFontFamily, settings.Appearance.UiFontSize, "appearance.ui", issues);
            ValidateFont(settings.Appearance.TerminalFontFamily, settings.Appearance.TerminalFontSize, "appearance.terminal", issues);
            if (settings.Appearance.TerminalFontWeight is < 100 or > 1000 ||
                !double.IsFinite(settings.Appearance.TerminalLineHeight) || settings.Appearance.TerminalLineHeight is < 0.75 or > 3 ||
                !double.IsFinite(settings.Appearance.TerminalCharacterSpacing) || settings.Appearance.TerminalCharacterSpacing is < -4 or > 16)
            {
                issues.Add(new("appearance.terminal", "终端字重、行高或字距无效。"));
            }
            ValidateColor(settings.Appearance.AccentColor, "appearance.accentColor", issues);
            ValidateVisual(settings.Appearance.Visual, issues);
        }

        if (settings.ShellNotifications is null || settings.ShellNotifications.LongTaskSeconds is < 1 or > 86400)
            issues.Add(new("shellNotifications", "长任务门槛须为 1—86400 秒。"));

        if (settings.History is null || settings.History.MaxEntries is < 1 or > 1_000_000 || settings.History.RetentionDays is < 1 or > 3650 || settings.History.MaxSizeBytes is < 1024 or > 16 * 1024 * 1024)
        {
            issues.Add(new("history", "历史上限或保留时长无效。"));
        }

        if (settings.Transcription is null ||
            settings.Transcription.MaxFileSizeBytes < 1024 ||
            settings.Transcription.MaxTotalSizeBytes < settings.Transcription.MaxFileSizeBytes ||
            settings.Transcription.RetentionDays is < 1 or > 3650)
        {
            issues.Add(new("transcription", "转录大小上限或保留时长无效。"));
        }

        if (settings.Workbench is null ||
            !Enum.IsDefined(settings.Workbench.TabPosition) ||
            string.IsNullOrWhiteSpace(settings.Workbench.CommandPaletteHotkey) ||
            settings.Workbench.CommandPaletteHotkey.Length > 64 ||
            string.IsNullOrWhiteSpace(settings.Workbench.FocusModeHotkey) || settings.Workbench.FocusModeHotkey.Length > 64)
        {
            issues.Add(new("workbench", "标签位置或命令面板热键配置无效。"));
        }

        if (profileIds is not null && !string.IsNullOrWhiteSpace(settings.DefaultProfileId) && !profileIds.Contains(settings.DefaultProfileId))
        {
            issues.Add(new("defaultProfileId", "默认 Profile 不存在。"));
        }

        return ValidationResult.FromIssues(issues);
    }

    public static ValidationResult ValidateProfiles(ProfileCatalog? catalog)
    {
        if (catalog is null)
        {
            return Invalid("profiles", "Profile 文档不能为空。");
        }

        var issues = new List<ValidationIssue>();
        if (catalog.SchemaVersion != ConfigurationDefaults.CurrentSchemaVersion)
        {
            issues.Add(new("schemaVersion", "Profile 版本不受支持。"));
        }

        if (catalog.Profiles is null)
        {
            issues.Add(new("profiles", "Profile 列表不能为空。"));
            return ValidationResult.FromIssues(issues);
        }

        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < catalog.Profiles.Count; index++)
        {
            var profile = catalog.Profiles[index];
            var path = $"profiles[{index}]";
            if (profile is null)
            {
                issues.Add(new(path, "Profile 不能为空。"));
                continue;
            }

            if (!IsStableId(profile.Id))
            {
                issues.Add(new($"{path}.id", "ID 须为 1-64 位字母、数字、点、下划线或连字符。"));
            }
            else if (!ids.Add(profile.Id))
            {
                issues.Add(new($"{path}.id", "Profile ID 重复。"));
            }

            if (string.IsNullOrWhiteSpace(profile.Name) || profile.Name.Length > 128 || profile.Name.Any(char.IsControl))
            {
                issues.Add(new($"{path}.name", "名称须为 1—128 个字符且不含控制字符。"));
            }

            if (string.IsNullOrWhiteSpace(profile.ExecutablePath) || profile.ExecutablePath.Contains('\0'))
            {
                issues.Add(new($"{path}.executablePath", "可执行文件不能为空或包含 NUL。"));
            }

            ValidatePathToken(profile.WorkingDirectory, $"{path}.workingDirectory", issues);
            if (profile.Icon is null || profile.Icon.Length > 64 || profile.Icon.Any(char.IsControl))
                issues.Add(new($"{path}.icon", "图标须为不含控制字符的文字或 Unicode 符号（最多 64 字符）。"));
            if (profile.PostLaunchInput is null || profile.PostLaunchInput.Contains('\0'))
                issues.Add(new($"{path}.postLaunchInput", "启动后文本不能为 null 或包含 NUL。"));
            if (!Enum.IsDefined(profile.ShellIntegration))
                issues.Add(new($"{path}.shellIntegration", "Shell 集成选项无效。"));
            if (profile.ExecutablePath?.Contains("{{", StringComparison.Ordinal) == true || profile.ExecutablePath?.Contains("}}", StringComparison.Ordinal) == true)
                issues.Add(new($"{path}.executablePath", "此字段不支持模板；{{home}} 仅用于目录字段。"));
            if (profile.Arguments is null || profile.Arguments.Any(argument => argument is null || argument.Contains('\0')))
            {
                issues.Add(new($"{path}.arguments", "参数不能为 null 或包含 NUL。"));
            }

            if (profile.Environment is null)
            {
                issues.Add(new($"{path}.environment", "环境变量字典不能为空。"));
            }
            else
            {
                var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var pair in profile.Environment)
                {
                    if (string.IsNullOrWhiteSpace(pair.Key) || pair.Key.IndexOfAny(['=', '\0', '\r', '\n']) >= 0 || pair.Value is null || pair.Value.Contains('\0') || !names.Add(pair.Key))
                    {
                        issues.Add(new($"{path}.environment", "环境变量名称或值无效。"));
                    }
                }
            }

            if (profile.Terminal is null)
            {
                issues.Add(new($"{path}.terminal", "终端配置不能为空。"));
            }
            else
            {
                ValidateFont(profile.Terminal.FontFamily, profile.Terminal.FontSize, $"{path}.terminal", issues);
                ValidateColor(profile.Terminal.ForegroundColor, $"{path}.terminal.foregroundColor", issues);
                ValidateColor(profile.Terminal.BackgroundColor, $"{path}.terminal.backgroundColor", issues);
                if (profile.Terminal.ScrollbackLines is < 0 or > 1_000_000)
                {
                    issues.Add(new($"{path}.terminal.scrollbackLines", "回滚行数须在 0 到 1000000 之间。"));
                }
            }

            if (profile.AgentRecipe is not null)
            {
                if (!IsStableId(profile.AgentRecipe.AgentId))
                {
                    issues.Add(new($"{path}.agentRecipe.agentId", "Agent ID 无效。"));
                }
                ValidatePathToken(profile.AgentRecipe.WorkingDirectory, $"{path}.agentRecipe.workingDirectory", issues);
                if (!Enum.IsDefined(profile.AgentRecipe.ShellBackend) || profile.AgentRecipe.Arguments is null ||
                    profile.AgentRecipe.Arguments.Any(value => value is null || value.Contains('\0')) ||
                    profile.AgentRecipe.Model?.Contains('\0') == true || profile.AgentRecipe.PermissionMode?.Contains('\0') == true)
                    issues.Add(new($"{path}.agentRecipe", "Agent 后端或参数无效。"));
            }
        }

        return ValidationResult.FromIssues(issues);
    }

    private static void ValidateVisual(VisualSettings? visual, List<ValidationIssue> issues)
    {
        if (visual is null)
        {
            issues.Add(new("appearance.visual", "视觉配置不能为空。"));
            return;
        }
        if (!Enum.IsDefined(visual.Theme) || !Enum.IsDefined(visual.Material) ||
            !Enum.IsDefined(visual.BackgroundFit) || !Enum.IsDefined(visual.BackgroundPosition))
        {
            issues.Add(new("appearance.visual", "主题、材质或背景布局选项无效。"));
        }
        if (!IsStableId(visual.AuroraPresetId) || visual.AuroraCustomColors is null ||
            visual.AuroraCustomColors.Count is < 3 or > 6)
        {
            issues.Add(new("appearance.visual.aurora", "极光预设或自定义颜色数量无效。"));
        }
        else
        {
            for (var index = 0; index < visual.AuroraCustomColors.Count; index++)
            {
                ValidateColor(visual.AuroraCustomColors[index], $"appearance.visual.auroraCustomColors[{index}]", issues);
            }
        }
        if (!double.IsFinite(visual.BackgroundOverlayOpacity) || visual.BackgroundOverlayOpacity is < 0 or > VisualSettingsLimits.MaxOverlayOpacity ||
            !double.IsFinite(visual.BackgroundBlurRadius) || visual.BackgroundBlurRadius is < 0 or > VisualSettingsLimits.MaxBackgroundBlurRadius ||
            !double.IsFinite(visual.BackgroundBrightness) || visual.BackgroundBrightness is < VisualSettingsLimits.MinBackgroundBrightness or > VisualSettingsLimits.MaxBackgroundBrightness ||
            !double.IsFinite(visual.BackgroundSaturation) || visual.BackgroundSaturation is < 0 or > VisualSettingsLimits.MaxBackgroundSaturation ||
            !double.IsFinite(visual.TerminalBackgroundOpacity) || visual.TerminalBackgroundOpacity is < VisualSettingsLimits.MinTerminalBackgroundOpacity or > 1)
        {
            issues.Add(new("appearance.visual", "背景或终端不透明度参数超出允许范围。"));
        }
        ValidateBackgroundPath(visual.BackgroundImagePath, issues);
    }

    private static void ValidateBackgroundPath(string? path, List<ValidationIssue> issues)
    {
        if (string.IsNullOrEmpty(path))
        {
            return;
        }
        var normalized = path.Replace('\\', '/');
        var fileName = normalized.StartsWith("Backgrounds/", StringComparison.OrdinalIgnoreCase) ? normalized[12..] : string.Empty;
        if (path.Length > 260 || path.Contains('\0') || Path.IsPathRooted(path) ||
            string.IsNullOrWhiteSpace(fileName) || fileName is "." or ".." || fileName.Contains('/') ||
            fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            issues.Add(new("appearance.visual.backgroundImagePath", "背景图须是 Backgrounds 目录内的相对文件路径。"));
        }
    }

    private static void ValidateFont(string? family, double size, string path, List<ValidationIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(family) || family.IndexOfAny(['/', '\\', '#', '\0', '\r', '\n']) >= 0 || !double.IsFinite(size) || size is < 6 or > 96)
        {
            issues.Add(new(path, "字体不能为空，字号须在 6 到 96 之间。"));
        }
    }

    private static void ValidateColor(string? color, string path, List<ValidationIssue> issues)
    {
        if (color is null || color.Length != 7 || color[0] != '#' || !color.Skip(1).All(Uri.IsHexDigit))
        {
            issues.Add(new(path, "颜色须使用 #RRGGBB 格式。"));
        }
    }

    private static void ValidatePathToken(string? path, string field, List<ValidationIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Contains('\0'))
        {
            issues.Add(new(field, "路径不能为空或包含 NUL。"));
            return;
        }

        var remainder = path.Replace("{{home}}", string.Empty, StringComparison.OrdinalIgnoreCase);
        if (remainder.Contains("{{", StringComparison.Ordinal) || remainder.Contains("}}", StringComparison.Ordinal))
        {
            issues.Add(new(field, "路径只允许 {{home}} 占位符。"));
        }
    }

    private static bool IsStableId(string? value) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= 64 && value.All(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '-');

    private static ValidationResult Invalid(string path, string message) => ValidationResult.FromIssues([new ValidationIssue(path, message)]);
}
