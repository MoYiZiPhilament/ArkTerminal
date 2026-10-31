namespace ArkTerminal.Core.Profiles;

public enum ShellIntegrationMode
{
    Disabled,
    Automatic
}

public enum AgentShellBackend
{
    PowerShell7,
    WindowsPowerShell
}

public sealed class AgentLaunchRecipe
{
    public string AgentId { get; set; } = string.Empty;
    public string? Model { get; set; }
    public string? PermissionMode { get; set; }
    public List<string> Arguments { get; set; } = [];
    public string WorkingDirectory { get; set; } = "{{home}}";
    public AgentShellBackend ShellBackend { get; set; } = AgentShellBackend.PowerShell7;
}

public sealed class ProfileTerminalSettings
{
    public string FontFamily { get; set; } = "Cascadia Mono";
    public double FontSize { get; set; } = 14;
    public string ForegroundColor { get; set; } = "#CCCCCC";
    public string BackgroundColor { get; set; } = "#1E1E1E";
    public int ScrollbackLines { get; set; } = 5000;
}

public sealed class TerminalProfile
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Icon { get; set; } = string.Empty;
    public string ExecutablePath { get; set; } = string.Empty;
    public List<string> Arguments { get; set; } = [];
    public string PostLaunchInput { get; set; } = string.Empty;
    public string WorkingDirectory { get; set; } = "{{home}}";
    public Dictionary<string, string> Environment { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public bool RunAsAdministrator { get; set; }
    public ShellIntegrationMode ShellIntegration { get; set; } = ShellIntegrationMode.Disabled;
    public ProfileTerminalSettings Terminal { get; set; } = new();
    public AgentLaunchRecipe? AgentRecipe { get; set; }
}

public sealed class ProfileCatalog
{
    public int SchemaVersion { get; set; } = Configuration.ConfigurationDefaults.CurrentSchemaVersion;
    public List<TerminalProfile> Profiles { get; set; } = [];
}

public static class ProfileDefaults
{
    public static ProfileCatalog CreateCatalog() => new()
    {
        Profiles =
        [
            new TerminalProfile
            {
                Id = "cmd",
                Name = "Command Prompt",
                ExecutablePath = "cmd.exe",
                Arguments = ["/d"],
                WorkingDirectory = "{{home}}"
            },
            new TerminalProfile
            {
                Id = "powershell5",
                Name = "Windows PowerShell 5",
                ExecutablePath = @"%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe",
                Arguments = ["-NoLogo"],
                WorkingDirectory = "{{home}}"
            },
            new TerminalProfile
            {
                Id = "powershell7",
                Name = "PowerShell 7",
                ExecutablePath = "pwsh.exe",
                Arguments = ["-NoLogo"],
                WorkingDirectory = "{{home}}"
            },
            new TerminalProfile
            {
                Id = "git-bash",
                Name = "Git Bash",
                ExecutablePath = @"%ProgramFiles%\Git\bin\bash.exe",
                Arguments = ["--login", "-i"],
                WorkingDirectory = "{{home}}"
            }
        ]
    };
}
