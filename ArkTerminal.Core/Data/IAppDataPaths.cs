namespace ArkTerminal.Core.Data;

public enum AppDataMode
{
    Standard,
    Portable,
    Msix
}

public interface IAppDataPaths
{
    AppDataMode Mode { get; }
    string RootDirectory { get; }
    string SettingsFile { get; }
    string ProfilesFile { get; }
    string RecentProjectsFile { get; }
    string SettingsDraftFile { get; }
    string ProfilesDraftFile { get; }
    string RecoveryStateFile { get; }
    string ExitMarkerFile { get; }
    string BackupDirectory { get; }
    string BackgroundsDirectory { get; }
    string DiagnosticsDirectory { get; }
    string AdaptersDirectory { get; }
    string AgentPreferencesFile { get; }
    string AgentDetectionCacheFile { get; }
    string HistoryDirectory { get; }
    string SessionSavesDirectory { get; }
    string SnapshotsDirectory { get; }
}

public interface IPackagingContext
{
    string? GetPackageFamilyName();
}

public sealed class AppDataPathsResult
{
    public IAppDataPaths? Paths { get; init; }
    public string? ErrorPath { get; init; }
    public string? ErrorMessage { get; init; }
    public bool Success => Paths is not null;
}
