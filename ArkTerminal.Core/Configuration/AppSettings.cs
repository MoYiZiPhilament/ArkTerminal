using ArkTerminal.Core.Localization;

namespace ArkTerminal.Core.Configuration;

public sealed class AppSettings
{
    public int SchemaVersion { get; set; } = ConfigurationDefaults.CurrentSchemaVersion;
    public AppearanceSettings Appearance { get; set; } = new();
    public string? DefaultProfileId { get; set; } = "cmd";
    public RegionalSettings Regional { get; set; } = new();
    public HistoryPolicy History { get; set; } = new();
    public TranscriptionPolicy Transcription { get; set; } = new();
    public WorkbenchSettings Workbench { get; set; } = new();
    public ShellNotificationSettings ShellNotifications { get; set; } = new();

    public AppSettings DeepClone() => new()
    {
        SchemaVersion = SchemaVersion,
        DefaultProfileId = DefaultProfileId,
        Appearance = Appearance.DeepClone(),
        Regional = Regional.DeepClone(),
        History = History.DeepClone(),
        Transcription = Transcription.DeepClone(),
        Workbench = Workbench.DeepClone(),
        ShellNotifications = ShellNotifications.DeepClone()
    };
}

public sealed class RegionalSettings
{
    public string Language { get; set; } = LocalizationDefaults.SystemLanguage();
    public string TimeZoneId { get; set; } = TimeZoneInfo.Local.Id;
    public RegionalSettings DeepClone() => (RegionalSettings)MemberwiseClone();
}

public enum TerminalTabPosition
{
    Top,
    Bottom,
    Left
}

public sealed class WorkbenchSettings
{
    public TerminalTabPosition TabPosition { get; set; } = TerminalTabPosition.Top;
    public bool ConfirmCloseRunning { get; set; } = true;
    public bool CommandPaletteHotkeyEnabled { get; set; } = true;
    public string CommandPaletteHotkey { get; set; } = "Ctrl+Shift+Space";
    public bool FocusModeHotkeyEnabled { get; set; } = true;
    public string FocusModeHotkey { get; set; } = "Ctrl+Shift+F11";

    public WorkbenchSettings DeepClone() => (WorkbenchSettings)MemberwiseClone();
}

public sealed class AppearanceSettings
{
    public string UiFontFamily { get; set; } = "Segoe UI";
    public double UiFontSize { get; set; } = 14;
    public string TerminalFontFamily { get; set; } = "Cascadia Mono";
    public double TerminalFontSize { get; set; } = 14;
    public int TerminalFontWeight { get; set; } = 400;
    public double TerminalLineHeight { get; set; } = 1.15;
    public double TerminalCharacterSpacing { get; set; }
    public bool TerminalLigatures { get; set; }
    public bool ShowTimestamps { get; set; }
    public bool ShowLogicalLineNumbers { get; set; }
    public bool ShowCommandDuration { get; set; }
    public string AccentColor { get; set; } = "#4688F1";
    public VisualSettings Visual { get; set; } = new();

    public AppearanceSettings DeepClone()
    {
        var clone = (AppearanceSettings)MemberwiseClone();
        clone.Visual = Visual.DeepClone();
        return clone;
    }
}

public enum AppThemeMode
{
    System,
    Light,
    Dark
}

public enum WindowMaterialMode
{
    Auto,
    Off,
    Mica,
    Acrylic
}

public enum BackgroundImageFit
{
    Uniform,
    UniformToFill,
    Fill,
    None
}

public enum BackgroundImagePosition
{
    TopLeft,
    Top,
    TopRight,
    Left,
    Center,
    Right,
    BottomLeft,
    Bottom,
    BottomRight
}

public sealed class VisualSettings
{
    public AppThemeMode Theme { get; set; } = AppThemeMode.System;
    public WindowMaterialMode Material { get; set; } = WindowMaterialMode.Auto;
    public bool DesktopBackgroundEnabled { get; set; }
    public bool AuroraEnabled { get; set; } = true;
    public string AuroraPresetId { get; set; } = "borealis";
    public List<string> AuroraCustomColors { get; set; } = ["#7C3AED", "#2563EB", "#10B981"];
    public string BackgroundImagePath { get; set; } = string.Empty;
    public double BackgroundOverlayOpacity { get; set; } = 0.25;
    public double BackgroundBlurRadius { get; set; }
    public double BackgroundBrightness { get; set; } = 1;
    public double BackgroundSaturation { get; set; } = 1;
    public BackgroundImageFit BackgroundFit { get; set; } = BackgroundImageFit.UniformToFill;
    public BackgroundImagePosition BackgroundPosition { get; set; } = BackgroundImagePosition.Center;
    public double TerminalBackgroundOpacity { get; set; } = 0.9;

    public VisualSettings DeepClone()
    {
        var clone = (VisualSettings)MemberwiseClone();
        clone.AuroraCustomColors = [.. AuroraCustomColors];
        return clone;
    }
}

public static class VisualSettingsLimits
{
    public const double MaxOverlayOpacity = 0.85;
    public const double MaxBackgroundBlurRadius = 24;
    public const double MinBackgroundBrightness = 0.5;
    public const double MaxBackgroundBrightness = 1.5;
    public const double MaxBackgroundSaturation = 2;
    public const double MinTerminalBackgroundOpacity = 0.35;
    public const int MaxBackgroundFileBytes = 20 * 1024 * 1024;
    public const long MaxBackgroundPixels = 40_000_000;
}

public sealed class HistoryPolicy
{
    public bool Enabled { get; set; }
    public int MaxEntries { get; set; } = 1000;
    public int MaxSizeBytes { get; set; } = 1024 * 1024;
    public int RetentionDays { get; set; } = 30;

    public HistoryPolicy DeepClone() => (HistoryPolicy)MemberwiseClone();
}

public sealed class ShellNotificationSettings
{
    public bool CommandCompletedEnabled { get; set; }
    public bool LongTaskEnabled { get; set; } = true;
    public bool ReadyForInputEnabled { get; set; }
    public int LongTaskSeconds { get; set; } = 10;
    public ShellNotificationSettings DeepClone() => (ShellNotificationSettings)MemberwiseClone();
}

public sealed class TranscriptionPolicy
{
    public bool Enabled { get; set; }
    public long MaxFileSizeBytes { get; set; } = 10 * 1024 * 1024;
    public long MaxTotalSizeBytes { get; set; } = 100 * 1024 * 1024;
    public int RetentionDays { get; set; } = 7;

    public TranscriptionPolicy DeepClone() => (TranscriptionPolicy)MemberwiseClone();
}

public static class ConfigurationDefaults
{
    public const int CurrentSchemaVersion = 1;

    public static AppSettings CreateSettings() => new();
}
