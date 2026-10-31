namespace ArkTerminal.Core.Configuration;

public sealed class AppSettings
{
    public int SchemaVersion { get; set; } = ConfigurationDefaults.CurrentSchemaVersion;
    public AppearanceSettings Appearance { get; set; } = new();
    public string? DefaultProfileId { get; set; } = "cmd";
    public HistoryPolicy History { get; set; } = new();
    public TranscriptionPolicy Transcription { get; set; } = new();
    public WorkbenchSettings Workbench { get; set; } = new();

    public AppSettings DeepClone() => new()
    {
        SchemaVersion = SchemaVersion,
        DefaultProfileId = DefaultProfileId,
        Appearance = Appearance.DeepClone(),
        History = History.DeepClone(),
        Transcription = Transcription.DeepClone(),
        Workbench = Workbench.DeepClone()
    };
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
    public string AccentColor { get; set; } = "#4688F1";

    public AppearanceSettings DeepClone() => (AppearanceSettings)MemberwiseClone();
}

public sealed class HistoryPolicy
{
    public bool Enabled { get; set; }
    public int MaxEntries { get; set; } = 1000;
    public int RetentionDays { get; set; } = 30;

    public HistoryPolicy DeepClone() => (HistoryPolicy)MemberwiseClone();
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
