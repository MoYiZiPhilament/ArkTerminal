namespace ArkTerminal.Core.Data;

public sealed class AppDataPaths : IAppDataPaths
{
    public AppDataPaths(AppDataMode mode, string root)
    {
        Mode = mode;
        RootDirectory = Path.GetFullPath(root);
        SettingsFile = Path.Combine(RootDirectory, "Config", "settings.json");
        ProfilesFile = Path.Combine(RootDirectory, "Config", "profiles.json");
        RecentProjectsFile = Path.Combine(RootDirectory, "Config", "recent-projects.json");
        SettingsDraftFile = Path.Combine(RootDirectory, "Recovery", "settings-draft.json");
        RecoveryStateFile = Path.Combine(RootDirectory, "Recovery", "recovery.json");
        ExitMarkerFile = Path.Combine(RootDirectory, "Recovery", "running.marker");
        BackupDirectory = Path.Combine(RootDirectory, "Backups");
        BackgroundsDirectory = Path.Combine(RootDirectory, "Backgrounds");
        DiagnosticsDirectory = Path.Combine(RootDirectory, "Diagnostics");
        AdaptersDirectory = Path.Combine(RootDirectory, "Adapters");
        HistoryDirectory = Path.Combine(RootDirectory, "History");
        SessionSavesDirectory = Path.Combine(RootDirectory, "Sessions");
    }

    public AppDataMode Mode { get; }
    public string RootDirectory { get; }
    public string SettingsFile { get; }
    public string ProfilesFile { get; }
    public string RecentProjectsFile { get; }
    public string SettingsDraftFile { get; }
    public string RecoveryStateFile { get; }
    public string ExitMarkerFile { get; }
    public string BackupDirectory { get; }
    public string BackgroundsDirectory { get; }
    public string DiagnosticsDirectory { get; }
    public string AdaptersDirectory { get; }
    public string HistoryDirectory { get; }
    public string SessionSavesDirectory { get; }

    internal IEnumerable<string> RequiredDirectories()
    {
        yield return RootDirectory;
        yield return Path.GetDirectoryName(SettingsFile)!;
        yield return Path.GetDirectoryName(SettingsDraftFile)!;
        yield return BackupDirectory;
        yield return BackgroundsDirectory;
        yield return DiagnosticsDirectory;
        yield return AdaptersDirectory;
        yield return HistoryDirectory;
        yield return SessionSavesDirectory;
    }
}
