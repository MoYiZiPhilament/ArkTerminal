using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.IO;
using ArkTerminal.Core.Configuration;
using ArkTerminal.Core.Profiles;
using ArkTerminal.Core.Recovery;
using ArkTerminal.Rendering;
using ArkTerminal.Terminal.Sessions;

namespace ArkTerminal.Sessions;

public enum TabRuntimeState
{
    NewPage,
    NotStarted,
    Starting,
    Running,
    Closing,
    Exited,
    Failed
}

public sealed class TabLaunchConfiguration
{
    public required SessionLaunchRequest Request { get; init; }
    public required string ProfileName { get; init; }
    public string Icon { get; init; } = string.Empty;
    public bool WorkingDirectoryIsExplicit { get; init; } = true;
    public bool IsTransientAgent { get; init; }
    public required TerminalFontOptions FontOptions { get; init; }
    public string WorkingDirectory => Request.WorkingDirectory;

    public static TabLaunchConfiguration FromProfile(TerminalProfile profile, string workingDirectory, AppearanceSettings? appearance, bool workingDirectoryIsExplicit = true, bool isTransientAgent = false)
    {
        var request = SessionLaunchRequest.FromProfile(profile);
        request = new SessionLaunchRequest
        {
            ProfileId = request.ProfileId,
            WorkspaceId = request.WorkspaceId,
            ExecutablePath = request.ExecutablePath,
            Arguments = request.Arguments,
            PostLaunchInput = request.PostLaunchInput,
            WorkingDirectory = Path.GetFullPath(Environment.ExpandEnvironmentVariables(workingDirectory)),
            Environment = request.Environment,
            ScrollbackLines = request.ScrollbackLines,
            RunAsAdministrator = request.RunAsAdministrator,
            ShellIntegration = request.ShellIntegration,
            AgentRecipe = request.AgentRecipe
        };
        return new TabLaunchConfiguration
        {
            Request = request,
            ProfileName = profile.Name,
            Icon = profile.Icon,
            WorkingDirectoryIsExplicit = workingDirectoryIsExplicit,
            IsTransientAgent = isTransientAgent,
            FontOptions = TerminalFontOptions.Resolve(appearance, profile)
        };
    }

    public TabLaunchConfiguration Clone() => new()
    {
        ProfileName = ProfileName,
        Icon = Icon,
        WorkingDirectoryIsExplicit = WorkingDirectoryIsExplicit,
        IsTransientAgent = IsTransientAgent,
        FontOptions = FontOptions,
        Request = new SessionLaunchRequest
        {
            ProfileId = Request.ProfileId,
            WorkspaceId = Request.WorkspaceId,
            ExecutablePath = Request.ExecutablePath,
            Arguments = Request.Arguments.ToArray(),
            PostLaunchInput = Request.PostLaunchInput,
            WorkingDirectory = Request.WorkingDirectory,
            Environment = new Dictionary<string, string>(Request.Environment, StringComparer.OrdinalIgnoreCase),
            ScrollbackLines = Request.ScrollbackLines,
            RunAsAdministrator = Request.RunAsAdministrator,
            ShellIntegration = Request.ShellIntegration,
            AgentRecipe = Request.AgentRecipe?.DeepClone()
        }
    };
}

public sealed class TabViewModel : INotifyPropertyChanged
{
    private TabRuntimeState _state;
    private string? _fixedTitle;
    private string _shellTitle = string.Empty;
    private string? _trustedForegroundProcess;
    private string? _error;
    private bool _isActive;

    public TabViewModel(TabLaunchConfiguration? launch = null, bool isNewPage = false)
    {
        Launch = launch;
        IsNewPage = isNewPage;
        _state = isNewPage ? TabRuntimeState.NewPage : TabRuntimeState.NotStarted;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public Guid TabId { get; } = Guid.NewGuid();
    public bool IsNewPage { get; }
    public bool IsRecoveredPlaceholder { get; init; }
    public TabLaunchConfiguration? Launch { get; internal set; }
    public ConPtySession? Session { get; internal set; }
    public long SessionGeneration { get; internal set; }
    public string AgentBackendDescription { get; internal set; } = string.Empty;
    internal SemaphoreSlim OperationGate { get; } = new(1, 1);
    public int ScrollbackOffset { get; set; }
    public string SearchQuery { get; set; } = string.Empty;
    public double? ZoomFontSize { get; set; }
    public string ProfileName => Launch?.ProfileName ?? Localize.T("Text0360");
    public string Icon => Launch?.Icon ?? string.Empty;
    public string WorkingDirectory => Launch?.WorkingDirectory ?? string.Empty;
    public string ToolTipText => $"{Title}\n{StatusText}\n{ProfileName}\n{WorkingDirectory}";
    public string Title => _fixedTitle is not null ? _fixedTitle :
        Launch?.Request.AgentRecipe is not null ? ProfileName :
        !string.IsNullOrWhiteSpace(_shellTitle) ? _shellTitle :
        !string.IsNullOrWhiteSpace(_trustedForegroundProcess) ? _trustedForegroundProcess : ProfileName;
    public string? FixedTitle { get => _fixedTitle; set { _fixedTitle = string.IsNullOrWhiteSpace(value) ? null : value.Trim(); OnPropertyChanged(); OnPropertyChanged(nameof(Title)); } }
    public string ShellTitle { get => _shellTitle; set { _shellTitle = value; OnPropertyChanged(nameof(Title)); } }
    public string? TrustedForegroundProcess { get => _trustedForegroundProcess; set { _trustedForegroundProcess = value; OnPropertyChanged(nameof(Title)); } }
    public string? Error { get => _error; set { _error = value; OnPropertyChanged(); OnPropertyChanged(nameof(StatusText)); } }
    public TabRuntimeState State { get => _state; set { _state = value; OnPropertyChanged(); OnPropertyChanged(nameof(StatusText)); OnPropertyChanged(nameof(HasSession)); } }
    public bool HasSession => Session is not null && State is TabRuntimeState.Running or TabRuntimeState.Starting;
    public bool IsActive { get => _isActive; set { _isActive = value; OnPropertyChanged(); } }
    public string StatusText => State switch
    {
        TabRuntimeState.NewPage => Localize.T("Text0078"),
        TabRuntimeState.NotStarted => Localize.T("Text0411"),
        TabRuntimeState.Starting => Localize.T("Text0412"),
        TabRuntimeState.Running => Localize.T("Text0413"),
        TabRuntimeState.Closing => Localize.T("Text0414"),
        TabRuntimeState.Exited => Localize.T("Text0415"),
        TabRuntimeState.Failed => string.IsNullOrWhiteSpace(Error) ? Localize.T("Text0416") : Localize.F("Text0417", Error),
        _ => Localize.T("Text0056")
    };

    public RecoveryTabState ToRecoveryState(int order) => new()
    {
        Id = TabId.ToString("N"),
        Title = FixedTitle ?? string.Empty,
        ProfileId = Launch!.Request.ProfileId,
        WorkingDirectory = Launch.WorkingDirectory,
        WorkingDirectoryIsExplicit = Launch.WorkingDirectoryIsExplicit,
        WorkspaceId = Launch.Request.WorkspaceId,
        Order = order
    };

    private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
