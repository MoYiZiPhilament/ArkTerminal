using ArkTerminal.Core.Localization;
using ArkTerminal.Core.Profiles;
using ArkTerminal.Core.Validation;

namespace ArkTerminal.Core.Configuration;

public static class ConfigurationValidator
{
    public static ValidationResult ValidateSettings(AppSettings? settings, IReadOnlyCollection<string>? profileIds = null)
    {
        if (settings is null)
        {
            return Invalid("settings", Localize.T("Text0592"));
        }

        var issues = new List<ValidationIssue>();
        if (settings.SchemaVersion != ConfigurationDefaults.CurrentSchemaVersion)
        {
            issues.Add(new("schemaVersion", Localize.T("Text0593")));
        }

        if (settings.Appearance is null)
        {
            issues.Add(new("appearance", Localize.T("Text0594")));
        }
        else
        {
            ValidateFont(settings.Appearance.UiFontFamily, settings.Appearance.UiFontSize, "appearance.ui", issues);
            ValidateFont(settings.Appearance.TerminalFontFamily, settings.Appearance.TerminalFontSize, "appearance.terminal", issues);
            if (settings.Appearance.TerminalFontWeight is < 100 or > 1000 ||
                !double.IsFinite(settings.Appearance.TerminalLineHeight) || settings.Appearance.TerminalLineHeight is < 0.75 or > 3 ||
                !double.IsFinite(settings.Appearance.TerminalCharacterSpacing) || settings.Appearance.TerminalCharacterSpacing is < -4 or > 16)
            {
                issues.Add(new("appearance.terminal", Localize.T("Text0595")));
            }
            ValidateColor(settings.Appearance.AccentColor, "appearance.accentColor", issues);
            ValidateVisual(settings.Appearance.Visual, issues);
        }

        if (settings.Regional is null || !LocalizationDefaults.IsSupportedLanguage(settings.Regional.Language) ||
            string.IsNullOrWhiteSpace(settings.Regional.TimeZoneId) ||
            LocalizationDefaults.ResolveTimeZone(settings.Regional.TimeZoneId).Id != settings.Regional.TimeZoneId)
        {
            issues.Add(new("regional", Localize.T("Text0596")));
        }

        if (settings.ShellNotifications is null || settings.ShellNotifications.LongTaskSeconds is < 1 or > 86400)
            issues.Add(new("shellNotifications", Localize.T("Text0597")));

        if (settings.History is null || settings.History.MaxEntries is < 1 or > 1_000_000 || settings.History.RetentionDays is < 1 or > 3650 || settings.History.MaxSizeBytes is < 1024 or > 16 * 1024 * 1024)
        {
            issues.Add(new("history", Localize.T("Text0598")));
        }

        if (settings.Transcription is null ||
            settings.Transcription.MaxFileSizeBytes < 1024 ||
            settings.Transcription.MaxTotalSizeBytes < settings.Transcription.MaxFileSizeBytes ||
            settings.Transcription.RetentionDays is < 1 or > 3650)
        {
            issues.Add(new("transcription", Localize.T("Text0599")));
        }

        if (settings.Workbench is null ||
            !Enum.IsDefined(settings.Workbench.TabPosition) ||
            string.IsNullOrWhiteSpace(settings.Workbench.CommandPaletteHotkey) ||
            settings.Workbench.CommandPaletteHotkey.Length > 64 ||
            string.IsNullOrWhiteSpace(settings.Workbench.FocusModeHotkey) || settings.Workbench.FocusModeHotkey.Length > 64)
        {
            issues.Add(new("workbench", Localize.T("Text0600")));
        }

        if (profileIds is not null && !string.IsNullOrWhiteSpace(settings.DefaultProfileId) && !profileIds.Contains(settings.DefaultProfileId))
        {
            issues.Add(new("defaultProfileId", Localize.T("Text0601")));
        }

        return ValidationResult.FromIssues(issues);
    }

    public static ValidationResult ValidateProfiles(ProfileCatalog? catalog)
    {
        if (catalog is null)
        {
            return Invalid("profiles", Localize.T("Text0602"));
        }

        var issues = new List<ValidationIssue>();
        if (catalog.SchemaVersion != ConfigurationDefaults.CurrentSchemaVersion)
        {
            issues.Add(new("schemaVersion", Localize.T("Text0603")));
        }

        if (catalog.Profiles is null)
        {
            issues.Add(new("profiles", Localize.T("Text0604")));
            return ValidationResult.FromIssues(issues);
        }

        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < catalog.Profiles.Count; index++)
        {
            var profile = catalog.Profiles[index];
            var path = $"profiles[{index}]";
            if (profile is null)
            {
                issues.Add(new(path, Localize.T("Text0605")));
                continue;
            }

            if (!IsStableId(profile.Id))
            {
                issues.Add(new($"{path}.id", Localize.T("Text0606")));
            }
            else if (!ids.Add(profile.Id))
            {
                issues.Add(new($"{path}.id", Localize.T("Text0607")));
            }

            if (string.IsNullOrWhiteSpace(profile.Name) || profile.Name.Length > 128 || profile.Name.Any(char.IsControl))
            {
                issues.Add(new($"{path}.name", Localize.T("Text0608")));
            }

            if (string.IsNullOrWhiteSpace(profile.ExecutablePath) || profile.ExecutablePath.Contains('\0'))
            {
                issues.Add(new($"{path}.executablePath", Localize.T("Text0609")));
            }

            ValidatePathToken(profile.WorkingDirectory, $"{path}.workingDirectory", issues);
            if (profile.Icon is null || profile.Icon.Length > 64 || profile.Icon.Any(char.IsControl))
                issues.Add(new($"{path}.icon", Localize.T("Text0610")));
            if (profile.PostLaunchInput is null || profile.PostLaunchInput.Contains('\0'))
                issues.Add(new($"{path}.postLaunchInput", Localize.T("Text0611")));
            if (!Enum.IsDefined(profile.ShellIntegration))
                issues.Add(new($"{path}.shellIntegration", Localize.T("Text0612")));
            if (profile.ExecutablePath?.Contains("{{", StringComparison.Ordinal) == true || profile.ExecutablePath?.Contains("}}", StringComparison.Ordinal) == true)
                issues.Add(new($"{path}.executablePath", Localize.T("Text0613")));
            if (profile.Arguments is null || profile.Arguments.Any(argument => argument is null || argument.Contains('\0')))
            {
                issues.Add(new($"{path}.arguments", Localize.T("Text0614")));
            }

            if (profile.Environment is null)
            {
                issues.Add(new($"{path}.environment", Localize.T("Text0615")));
            }
            else
            {
                var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var pair in profile.Environment)
                {
                    if (string.IsNullOrWhiteSpace(pair.Key) || pair.Key.IndexOfAny(['=', '\0', '\r', '\n']) >= 0 || pair.Value is null || pair.Value.Contains('\0') || !names.Add(pair.Key))
                    {
                        issues.Add(new($"{path}.environment", Localize.T("Text0616")));
                    }
                }
            }

            if (profile.Terminal is null)
            {
                issues.Add(new($"{path}.terminal", Localize.T("Text0617")));
            }
            else
            {
                ValidateFont(profile.Terminal.FontFamily, profile.Terminal.FontSize, $"{path}.terminal", issues);
                ValidateColor(profile.Terminal.ForegroundColor, $"{path}.terminal.foregroundColor", issues);
                ValidateColor(profile.Terminal.BackgroundColor, $"{path}.terminal.backgroundColor", issues);
                if (profile.Terminal.ScrollbackLines is < 0 or > 1_000_000)
                {
                    issues.Add(new($"{path}.terminal.scrollbackLines", Localize.T("Text0618")));
                }
            }

            if (profile.AgentRecipe is not null)
            {
                if (!IsStableId(profile.AgentRecipe.AgentId))
                {
                    issues.Add(new($"{path}.agentRecipe.agentId", Localize.T("Text0619")));
                }
                ValidatePathToken(profile.AgentRecipe.WorkingDirectory, $"{path}.agentRecipe.workingDirectory", issues);
                if (!Enum.IsDefined(profile.AgentRecipe.ShellBackend) || profile.AgentRecipe.Arguments is null ||
                    profile.AgentRecipe.Arguments.Any(value => value is null || value.Contains('\0')) ||
                    profile.AgentRecipe.Model?.Contains('\0') == true || profile.AgentRecipe.PermissionMode?.Contains('\0') == true)
                    issues.Add(new($"{path}.agentRecipe", Localize.T("Text0620")));
            }
        }

        return ValidationResult.FromIssues(issues);
    }

    private static void ValidateVisual(VisualSettings? visual, List<ValidationIssue> issues)
    {
        if (visual is null)
        {
            issues.Add(new("appearance.visual", Localize.T("Text0621")));
            return;
        }
        if (!Enum.IsDefined(visual.Theme) || !Enum.IsDefined(visual.Material) ||
            !Enum.IsDefined(visual.BackgroundFit) || !Enum.IsDefined(visual.BackgroundPosition))
        {
            issues.Add(new("appearance.visual", Localize.T("Text0622")));
        }
        if (!IsStableId(visual.AuroraPresetId) || visual.AuroraCustomColors is null ||
            visual.AuroraCustomColors.Count is < 3 or > 6)
        {
            issues.Add(new("appearance.visual.aurora", Localize.T("Text0623")));
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
            issues.Add(new("appearance.visual", Localize.T("Text0624")));
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
            issues.Add(new("appearance.visual.backgroundImagePath", Localize.T("Text0625")));
        }
    }

    private static void ValidateFont(string? family, double size, string path, List<ValidationIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(family) || family.IndexOfAny(['/', '\\', '#', '\0', '\r', '\n']) >= 0 || !double.IsFinite(size) || size is < 6 or > 96)
        {
            issues.Add(new(path, Localize.T("Text0626")));
        }
    }

    private static void ValidateColor(string? color, string path, List<ValidationIssue> issues)
    {
        if (color is null || color.Length != 7 || color[0] != '#' || !color.Skip(1).All(Uri.IsHexDigit))
        {
            issues.Add(new(path, Localize.T("Text0627")));
        }
    }

    private static void ValidatePathToken(string? path, string field, List<ValidationIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Contains('\0'))
        {
            issues.Add(new(field, Localize.T("Text0628")));
            return;
        }

        var remainder = path.Replace("{{home}}", string.Empty, StringComparison.OrdinalIgnoreCase);
        if (remainder.Contains("{{", StringComparison.Ordinal) || remainder.Contains("}}", StringComparison.Ordinal))
        {
            issues.Add(new(field, Localize.T("Text0629")));
        }
    }

    private static bool IsStableId(string? value) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= 64 && value.All(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '-');

    private static ValidationResult Invalid(string path, string message) => ValidationResult.FromIssues([new ValidationIssue(path, message)]);
}
