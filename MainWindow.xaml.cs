using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;
using ArkTerminal.Commands;
using ArkTerminal.Core.Configuration;
using ArkTerminal.Core.Localization;
using ArkTerminal.Core.Profiles;
using ArkTerminal.Core.Storage;
using ArkTerminal.Infrastructure;
using ArkTerminal.Sessions;
using ArkTerminal.Visuals;

namespace ArkTerminal;

public partial class MainWindow : Window
{
    private readonly AppHost _host;
    private readonly TerminalTabsViewModel _tabs;
    private readonly CommandRegistry _commands = new();
    private readonly GlobalHotkeyService _hotkey = new();
    private readonly WindowCommandKeyService _commandKeys = new();
    private readonly WindowVisualCoordinator _visuals;
    private readonly HashSet<string> _importedBackgrounds = new(StringComparer.OrdinalIgnoreCase);
    private CancellationTokenSource? _draftDebounce;
    private string _previewBackgroundPath = string.Empty;
    private CancellationTokenSource? _layoutDebounce;
    private CancellationTokenSource? _detectCancellation;
    private TabViewModel? _attachedTab;
    private Guid? _attachedSessionId;
    private bool _initializing;
    private bool _closing;
    private bool _allowClose;
    private bool _focusMode;
    private bool _settingsVisible;
    private long _lastFocusToggle = -250;

    public MainWindow(AppHost host)
    {
        _host = host;
        InitializeComponent();
        _visuals = new WindowVisualCoordinator(this, TerminalView, BackgroundImage, BackgroundOverlay, ReadabilityOverlay, AuroraView, host.Paths);
        MotionPolicy.SetEnabled(_visuals.AnimationsEnabled);
        TerminalView.SetAnimationsEnabled(_visuals.AnimationsEnabled);
        _visuals.PresentationChanged += () =>
        {
            MotionPolicy.SetEnabled(_visuals.AnimationsEnabled);
            TerminalView.SetAnimationsEnabled(_visuals.AnimationsEnabled);
            VisualCapabilityText.Text = _visuals.CapabilityText;
            UpdateDraftControls();
            UpdatePerformanceButton();
            UpdateCaptionButtons();
            var style = TerminalView.VisualStyle;
            var inset = new SolidColorBrush(style.Background) { Opacity = style.BackgroundOpacity };
            inset.Freeze();
            TerminalCanvas.BorderBrush = inset;
        };
        _tabs = new TerminalTabsViewModel(host.Sessions);
        TabStrip.ItemsSource = _tabs.Tabs;
        _tabs.ActiveTabChanged += AttachActiveTab;
        _tabs.LayoutChanged += ScheduleLayoutSave;
        _tabs.TabDetaching += DetachTab;
        TabStrip.Dropped += TabStrip_Dropped;
        TerminalView.InputError += message => Dispatcher.BeginInvoke(() => { _host.Diagnostics.Record(ArkTerminal.Core.Diagnostics.DiagnosticComponent.Terminal, ArkTerminal.Core.Diagnostics.DiagnosticEventKind.InputFailed); StatusText.Text = message; });
        TerminalView.SearchRequested += ShowSearch;
        LocationChanged += (_, _) => TerminalView.InvalidateArrange();
        _hotkey.Invoked += () => Dispatcher.BeginInvoke(ShowCommandPalette);
        _hotkey.FocusInvoked += () => Dispatcher.BeginInvoke(ToggleFocusMode);
        _commandKeys.CommandPaletteRequested += () => Dispatcher.BeginInvoke(ShowCommandPalette);
        _commandKeys.FocusModeRequested += () => Dispatcher.BeginInvoke(ToggleFocusMode);
        RegisterCommands();
        InitializeStageSix();
        InitializeStageSeven();
        InitializeView();
        InitializeStageEight();
        InitializeStageNine();
        InitializeWindowChrome();
        Localize.Changed += () => Dispatcher.BeginInvoke(RefreshLocalizedContent);
    }

    private void RefreshLocalizedContent()
    {
        if (!_stageSixReady) return;
        _hotkey.RefreshLocalization();
        HotkeyStatusText.Text = _hotkey.StatusText;
        ModeText.Text = Localize.F("Text0201", _host.Paths.Mode);
        DataRootText.Text = Localize.F("Text0202", _host.Paths.RootDirectory);
        SettingsDataRootText.Text = Localize.F("Text0125", _host.Paths.Mode, _host.Paths.RootDirectory);
        RefreshDefaultProfiles();
        _profileEditor.Reload();
        _agentPicker.RefreshLocalization();
        ShortcutList.ItemsSource = _commands.Search(string.Empty).Where(command => !command.Id.StartsWith("tab.activate.", StringComparison.Ordinal) && !command.Id.StartsWith("profile.launch.", StringComparison.Ordinal)).ToArray();
        TabStrip.Items.Refresh();
        UpdateActiveStatus();
        UpdateFontPreviews();
        UpdatePerformanceButton();
        UpdateCaptionButtons();
        ConfigureWindowFallback();
        VisualCapabilityText.Text = _visuals.CapabilityText;
        if (CommandPalette.Visibility == Visibility.Visible) RefreshCommands();
    }

    private void InitializeView()
    {
        _initializing = true;
        ModeText.Text = Localize.F("Text0201", _host.Paths.Mode);
        DataRootText.Text = Localize.F("Text0202", _host.Paths.RootDirectory);
        ProfilesList.ItemsSource = _host.Catalog?.Profiles;
        DirectoryInput.Text = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        RefreshRecents();
        RefreshErrors();
        RecoveryPanel.Visibility = _host.RecoveryAvailable ? Visibility.Visible : Visibility.Collapsed;
        LoadSettingsControls(_host.Draft?.Settings ?? _host.FormalSettings);
        UpdateDraftControls();
        _initializing = false;
        if (_host.FormalSettings is not null)
        {
            ApplyTabPosition(_host.FormalSettings.Workbench.TabPosition);
        }
        AttachActiveTab();
    }

    private void RegisterCommands()
    {
        _commands.Register(() => _tabs.Tabs.Select(tab => new CommandDescriptor
        {
            Id = $"tab.activate.{tab.TabId:N}",
            Title = Localize.F("Text0203", tab.Title, tab.StatusText),
            Category = Localize.T("Text0204"),
            Keywords = $"{tab.ProfileName} {tab.WorkingDirectory}",
            ExecuteAsync = _ => { _tabs.Activate(tab); return Task.CompletedTask; }
        }).ToArray());
        _commands.Register(() => _host.Catalog?.Profiles.Select(profile => new CommandDescriptor
        {
            Id = $"profile.launch.{profile.Id}",
            Title = Localize.F("Text0205", profile.Name),
            Category = "Profile",
            Keywords = profile.WorkingDirectory,
            ExecuteAsync = _ => LaunchProfileAsync(profile, DirectoryInput.Text)
        }).ToArray() ?? []);
    }

    private void AttachActiveTab()
    {
        var tab = _tabs.ActiveTab;
        if (_attachedTab == tab && _attachedSessionId == tab.Session?.Id)
        {
            UpdateActiveStatus();
            return;
        }
        if (_attachedTab is not null)
        {
            CaptureViewState(_attachedTab);
            TerminalView.Detach();
        }
        _attachedTab = tab;
        _attachedSessionId = tab.Session?.Id;
        _settingsVisible = false;
        ShowActiveWorkbench();
        Title = $"ArkTerminal · {tab.Title}";
        if (!tab.IsNewPage && tab.Session is not null && tab.Launch is not null)
        {
            PlaceholderPanel.Visibility = Visibility.Collapsed;
            TerminalView.Visibility = Visibility.Visible;
            TerminalView.Attach(tab.Session, EffectiveFont(tab), tab.ScrollbackOffset);
            TerminalView.SetFontBaseline(EffectiveFont(tab), tab.ZoomFontSize);
            UpdateAuxiliaryDisplay();
            _initializing = true;
            SearchInput.Text = tab.SearchQuery;
            _initializing = false;
            if (string.IsNullOrEmpty(tab.SearchQuery)) MotionPolicy.Hide(SearchPanel, false);
            else MotionPolicy.Show(SearchPanel, false);
            if (!string.IsNullOrEmpty(tab.SearchQuery))
            {
                _ = RestoreSearchAsync(tab.SearchQuery);
            }
            Dispatcher.BeginInvoke(() => TerminalView.FocusInput());
        }
        else if (!tab.IsNewPage)
        {
            TerminalView.Visibility = Visibility.Collapsed;
            PlaceholderPanel.Visibility = Visibility.Visible;
            MotionPolicy.Hide(SearchPanel, false);
        }
        UpdateActiveStatus();
        TabCountText.Text = Localize.F("Text0206", _tabs.Tabs.Count);
        Dispatcher.BeginInvoke(() =>
        {
            if (TabStrip.ItemContainerGenerator.ContainerFromItem(tab) is FrameworkElement container)
            {
                container.BringIntoView();
            }
        });
    }

    private void ShowActiveWorkbench()
    {
        if (_tabs.ActiveTab.IsNewPage)
        {
            MotionPolicy.Show(NewPage);
            MotionPolicy.Hide(TerminalPanel);
        }
        else
        {
            MotionPolicy.Show(TerminalPanel);
            MotionPolicy.Hide(NewPage);
        }
        MotionPolicy.Hide(SettingsPanel);
    }

    private void UpdateActiveStatus()
    {
        var tab = _tabs.ActiveTab;
        Title = $"ArkTerminal · {tab.Title}";
        TabCountText.Text = Localize.F("Text0206", _tabs.Tabs.Count);
        TerminalStatusText.Text = tab.IsNewPage ? string.Empty : $"{tab.StatusText} · {tab.ProfileName}";
        PlaceholderTitle.Text = tab.Title;
        PlaceholderDetails.Text = Localize.F("Text0207", tab.StatusText, tab.WorkingDirectory);
        if (!string.IsNullOrEmpty(tab.Error)) PlaceholderDetails.Text += "\n" + tab.Error;
        UpdateWorkbenchChrome();
        UpdateShellDisplay();
    }

    private async Task RestoreSearchAsync(string query)
    {
        await TerminalView.SearchAsync(query);
        UpdateSearchStatus();
    }

    private void CaptureViewState(TabViewModel tab)
    {
        tab.ScrollbackOffset = TerminalView.CurrentSnapshot?.ScrollbackOffset ?? tab.ScrollbackOffset;
        tab.SearchQuery = SearchInput.Text;
        tab.ZoomFontSize = TerminalView.HasUserZoom ? TerminalView.CurrentFontSize : null;
    }

    private void DetachTab(TabViewModel tab)
    {
        if (_attachedTab != tab)
        {
            return;
        }
        CaptureViewState(tab);
        TerminalView.Detach();
        _attachedTab = null;
        _attachedSessionId = null;
    }

    private async Task LaunchProfileAsync(TerminalProfile profile, string workingDirectory)
    {
        try
        {
            var directory = _directoryExplicit ? workingDirectory : profile.AgentRecipe?.WorkingDirectory ?? profile.WorkingDirectory;
            var launch = TabLaunchConfiguration.FromProfile(profile, ExpandHome(directory), _host.FormalSettings?.Appearance, _directoryExplicit,
                isTransientAgent: profile.AgentRecipe is not null && _host.Catalog?.Profiles.Any(item => item.Id == profile.Id) != true);
            if (profile.AgentRecipe is null)
            {
                var readiness = await Task.Run(() => ArkTerminal.Terminal.Sessions.Windows.LaunchPathInspector.Inspect(launch.Request));
                if (!readiness.Ready) { StatusText.Text = readiness.Message; return; }
            }
            _settingsVisible = false;
            var tab = await _tabs.LaunchAsync(launch);
            if (tab.State == TabRuntimeState.Running)
            {
                await AddRecentAsync(launch.WorkingDirectory);
                StatusText.Text = Localize.F("Text0208", profile.Name) + (profile.AgentRecipe is null ? tab.Session?.ShellStatus.Detail : string.Empty);
            }
            else
            {
                StatusText.Text = tab.Error ?? Localize.T("Text0209");
            }
        }
        catch (Exception exception)
        {
            _host.Diagnostics.Record(ArkTerminal.Core.Diagnostics.DiagnosticComponent.Terminal, ArkTerminal.Core.Diagnostics.DiagnosticEventKind.StartFailed, exception.HResult);
            StatusText.Text = Localize.F("Text0210", exception.Message);
        }
    }

    private async void ProfileLaunch_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: TerminalProfile profile })
        {
            await LaunchProfileAsync(profile, DirectoryInput.Text);
        }
    }

    private async void StartPlaceholder_Click(object sender, RoutedEventArgs e)
    {
        var tab = _tabs.ActiveTab;
        await _tabs.StartAsync(tab);
        if (tab.State == TabRuntimeState.Running && tab.Launch is not null)
        {
            await AddRecentAsync(tab.Launch.WorkingDirectory);
        }
        AttachActiveTab();
        UpdateActiveStatus();
    }

    private async void DuplicateActive_Click(object sender, RoutedEventArgs e) => await DuplicateAsync(_tabs.ActiveTab);
    private async void RestartActive_Click(object sender, RoutedEventArgs e) => await RestartAsync(_tabs.ActiveTab);

    private async Task DuplicateAsync(TabViewModel tab)
    {
        if (tab.Launch is not null)
        {
            await _tabs.DuplicateAsync(tab);
        }
    }

    private async Task RestartAsync(TabViewModel tab)
    {
        if (tab.Launch is null || !ConfirmRunning(tab, Localize.T("Text0211")))
        {
            return;
        }
        if (tab.Launch.IsTransientAgent)
        {
            await _tabs.RestartAsync(tab, tab.Launch.Clone());
            AttachActiveTab();
            return;
        }
        var profile = _host.Catalog?.Profiles.FirstOrDefault(item => item.Id == tab.Launch.Request.ProfileId);
        if (profile is null) { StatusText.Text = Localize.T("Text0212"); return; }
        var directory = tab.Launch.WorkingDirectoryIsExplicit ? tab.WorkingDirectory : ExpandHome(profile.AgentRecipe?.WorkingDirectory ?? profile.WorkingDirectory);
        var replacement = TabLaunchConfiguration.FromProfile(profile, directory, _host.FormalSettings?.Appearance, tab.Launch.WorkingDirectoryIsExplicit);
        if (profile.AgentRecipe is null)
        {
            var readiness = await Task.Run(() => ArkTerminal.Terminal.Sessions.Windows.LaunchPathInspector.Inspect(replacement.Request));
            if (!readiness.Ready) { StatusText.Text = readiness.Message; return; }
        }
        await _tabs.RestartAsync(tab, replacement);
        AttachActiveTab();
    }

    private async Task CloseTabAsync(TabViewModel tab)
    {
        if (tab.IsNewPage || !ConfirmRunning(tab, Localize.T("Text0213")))
        {
            return;
        }
        await _tabs.CloseAsync(tab);
    }

    private bool ConfirmRunning(TabViewModel tab, string action)
    {
        if (tab.Session?.State != Terminal.Sessions.SessionState.Running || _host.FormalSettings?.Workbench.ConfirmCloseRunning == false)
        {
            return true;
        }
        return MessageBox.Show(this, Localize.F("Text0214", tab.Title, action), action, MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;
    }

    private void NewPage_Click(object sender, RoutedEventArgs e)
    {
        _settingsVisible = false;
        _tabs.ShowNewPage();
        if (_attachedTab == _tabs.ActiveTab)
        {
            AttachActiveTab();
            ShowActiveWorkbench();
        }
    }

    private void Tab_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: TabViewModel tab })
        {
            _tabs.Activate(tab);
            if (_settingsVisible && tab == _tabs.ActiveTab)
            {
                _settingsVisible = false;
                ShowActiveWorkbench();
                if (tab.Session is not null) TerminalView.FocusInput();
            }
            if (e.ClickCount == 1)
            {
                (sender as FrameworkElement)?.BringIntoView();
            }
        }
    }

    private void TabStrip_Dropped(TabViewModel tab, int index)
    {
        _tabs.Move(tab, index);
        Dispatcher.BeginInvoke(() =>
        {
            if (TabStrip.ItemContainerGenerator.ContainerFromItem(tab) is FrameworkElement container)
            {
                MotionPolicy.Drop(
                    container,
                    TabStrip.OrientationEx == Orientation.Horizontal ? 8 : 0,
                    TabStrip.OrientationEx == Orientation.Vertical ? 8 : 0,
                    _visuals.AnimationsEnabled);
            }
        });
    }

    private void PreviousTab_Click(object sender, RoutedEventArgs e) => _tabs.Next(-1);
    private void NextTab_Click(object sender, RoutedEventArgs e) => _tabs.Next(1);

    private async void CloseTab_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: TabViewModel tab })
        {
            await CloseTabAsync(tab);
        }
    }

    private async void DuplicateTab_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: TabViewModel tab }) await DuplicateAsync(tab);
    }

    private async void RestartTab_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: TabViewModel tab }) await RestartAsync(tab);
    }

    private void RenameTab_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: TabViewModel tab } || tab.IsNewPage) return;
        var input = new TextBox { Text = tab.FixedTitle ?? string.Empty, Margin = new Thickness(0, 12, 0, 16) };
        var panel = new StackPanel { Margin = new Thickness(20) };
        panel.Children.Add(new TextBlock { Text = Localize.T("Text0215"), FontSize = 18, FontWeight = FontWeights.SemiBold });
        panel.Children.Add(new TextBlock { Text = Localize.T("Text0216"), Margin = new Thickness(0, 6, 0, 0) });
        panel.Children.Add(input);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var save = new Button { Content = Localize.T("Text0217"), Style = (Style)FindResource("PrimaryButton"), IsDefault = true };
        var cancel = new Button { Content = Localize.T("Text0218"), IsCancel = true };
        actions.Children.Add(save);
        actions.Children.Add(cancel);
        panel.Children.Add(actions);
        var frame = new Controls.RoundedBorder { CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1), Child = panel };
        frame.SetResourceReference(Border.BackgroundProperty, "Theme.CardBackground");
        frame.SetResourceReference(Border.BorderBrushProperty, "Theme.BorderBrush");
        var dialog = new Window { Owner = this, Title = Localize.T("Text0215"), Width = 420, SizeToContent = SizeToContent.Height,
            WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false,
            Background = (Brush)FindResource("Theme.CardBackground"), FontFamily = FontFamily, FontSize = FontSize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, Content = frame };
        WindowChrome.SetWindowChrome(dialog, new WindowChrome { CaptionHeight = 44, ResizeBorderThickness = new Thickness(0), GlassFrameThickness = new Thickness(0), CornerRadius = new CornerRadius(24), UseAeroCaptionButtons = false });
        save.Click += (_, _) => { _tabs.Rename(tab, input.Text); dialog.Close(); };
        dialog.Loaded += (_, _) => { MotionPolicy.Show(frame); input.Focus(); input.SelectAll(); };
        dialog.ShowDialog();
    }

    private void BrowseDirectory_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = Localize.T("Text0219"), InitialDirectory = DirectoryInput.Text };
        if (dialog.ShowDialog(this) == true)
        {
            DirectoryInput.Text = dialog.FolderName;
            _ = DetectProjectAsync();
        }
    }

    private async void DetectProject_Click(object sender, RoutedEventArgs e) => await DetectProjectAsync();
    private void CancelDetect_Click(object sender, RoutedEventArgs e) => _detectCancellation?.Cancel();

    private async Task DetectProjectAsync()
    {
        _detectCancellation?.Cancel();
        _detectCancellation?.Dispose();
        _detectCancellation = new CancellationTokenSource();
        DetectionResults.ItemsSource = null;
        DetectionStatus.Text = Localize.T("Text0220");
        try
        {
            var result = await ProjectRootDetector.DetectAsync(DirectoryInput.Text, 5, _detectCancellation.Token);
            DetectionStatus.Text = result.Error ?? (result.Candidates.Count == 0 ? Localize.T("Text0221") : Localize.T("Text0222"));
            DetectionResults.ItemsSource = result.Candidates.Select(candidate => new { Path = candidate.Path, DisplayText = $"{candidate.Path} · {string.Join(", ", candidate.Markers)}" }).ToArray();
        }
        catch (OperationCanceledException)
        {
            DetectionStatus.Text = Localize.T("Text0223");
        }
    }

    private void ProjectCandidate_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string path }) DirectoryInput.Text = path;
    }

    private void RecentProject_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string path })
        {
            DirectoryInput.Text = path;
            _ = DetectProjectAsync();
        }
    }

    private async Task AddRecentAsync(string path)
    {
        var projects = _host.RecentProjects.Projects.Where(project => !project.Path.Equals(path, StringComparison.OrdinalIgnoreCase)).ToList();
        projects.Insert(0, new RecentProject { Path = path, DisplayName = new DirectoryInfo(path).Name });
        var result = await _host.SaveRecentProjectsAsync(new RecentProjectsDocument { Projects = projects.Take(RecentProjectsService.MaximumProjects).ToList() });
        if (result.Success) RefreshRecents(); else StatusText.Text = result.Error;
    }

    private async void RemoveRecent_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string path }) return;
        var document = new RecentProjectsDocument { Projects = _host.RecentProjects.Projects.Where(project => project.Path != path).ToList() };
        var result = await _host.SaveRecentProjectsAsync(document);
        if (result.Success) RefreshRecents(); else StatusText.Text = result.Error;
    }

    private async void ClearRecents_Click(object sender, RoutedEventArgs e)
    {
        var result = await _host.SaveRecentProjectsAsync(new RecentProjectsDocument());
        if (result.Success) RefreshRecents(); else StatusText.Text = result.Error;
    }

    private void RefreshRecents()
    {
        RecentsList.ItemsSource = _host.RecentProjects.Projects;
        RecentsEmpty.Visibility = _host.RecentProjects.Projects.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void RecoverButton_Click(object sender, RoutedEventArgs e)
    {
        await _host.AcceptRecoveryAsync();
        _initializing = true;
        if (_host.Recovery is not null && _host.Catalog is not null)
        {
            foreach (var state in _host.Recovery.Tabs.OrderBy(tab => tab.Order))
            {
                var profile = _host.Catalog.Profiles.FirstOrDefault(item => item.Id == state.ProfileId);
                if (profile is null)
                {
                    StatusText.Text = Localize.F("Text0224", state.ProfileId);
                    continue;
                }
                try
                {
                    var launch = TabLaunchConfiguration.FromProfile(profile, ExpandHome(state.WorkingDirectory), _host.FormalSettings?.Appearance, state.WorkingDirectoryIsExplicit ?? true);
                    var tab = _tabs.AddPlaceholder(launch, state.Title, recovered: true);
                    if (state.Id == _host.Recovery.ActiveTabId) _tabs.Activate(tab);
                }
                catch (Exception exception)
                {
                    StatusText.Text = Localize.F("Text0225", exception.Message);
                }
            }
        }
        _initializing = false;
        RecoveryPanel.Visibility = Visibility.Collapsed;
        LoadSettingsControls(_host.Draft?.Settings ?? _host.FormalSettings);
        UpdateDraftControls();
        RestoreSettingsEditor();
        _profileEditor.Reload();
        ScheduleLayoutSave();
    }

    private async void DiscardRecoveryButton_Click(object sender, RoutedEventArgs e)
    {
        CancelDraftDebounce();
        var result = await _host.DiscardRecoveryAsync();
        if (!ShowResult(result, Localize.T("Text0226"))) return;
        _tabs.RemoveRecoveredPlaceholders();
        RecoveryPanel.Visibility = Visibility.Collapsed;
        LoadSettingsControls(_host.FormalSettings);
        UpdateDraftControls();
    }

    private void ScheduleLayoutSave()
    {
        if (_initializing || _closing) return;
        _layoutDebounce?.Cancel();
        _layoutDebounce?.Dispose();
        _layoutDebounce = new CancellationTokenSource();
        _ = SaveLayoutAfterDelayAsync(_layoutDebounce.Token);
    }

    private async Task SaveLayoutAfterDelayAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(300, token);
            var result = await _host.SaveLayoutAsync(_tabs.CaptureLayout(), _tabs.ActiveTab.TabId.ToString("N"), token);
            if (!result.Success) StatusText.Text = result.Error;
        }
        catch (OperationCanceledException) { }
    }

    private void ShowSettings()
    {
        if (_focusMode) return;
        _settingsVisible = true;
        LoadSettingsControls(_host.Draft?.Settings ?? _host.FormalSettings);
        RestoreSettingsEditor();
        _profileEditor.Reload();
        UpdateDraftControls();
        UpdateWorkbenchChrome();
        MotionPolicy.Hide(NewPage);
        MotionPolicy.Hide(TerminalPanel);
        MotionPolicy.Show(SettingsPanel);
    }

    private void Settings_Click(object sender, RoutedEventArgs e) => ShowSettings();

    private static void SelectTaggedItem(ComboBox comboBox, string tag)
    {
        foreach (ComboBoxItem item in comboBox.Items)
        {
            if (string.Equals((string)item.Tag, tag, StringComparison.OrdinalIgnoreCase))
            {
                comboBox.SelectedItem = item;
                return;
            }
        }
    }

    private static string GetTag(ComboBox comboBox) => (comboBox.SelectedItem as ComboBoxItem)?.Tag as string ?? string.Empty;

    private void WorkbenchInput_Changed(object sender, RoutedEventArgs e) => PreviewDraft();
    private void VisualInput_Changed(object sender, RoutedEventArgs e) => PreviewDraft();

    private async void ImportBackground_Click(object sender, RoutedEventArgs e)
    {
        if (!_host.CanEdit) return;
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = Localize.T("Text0227"),
            Filter = Localize.T("Text0228"),
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog(this) != true) return;
        BackgroundStatusText.Text = Localize.T("Text0229");
        try
        {
            var result = await _visuals.ImportBackgroundAsync(dialog.FileName, CancellationToken.None);
            if (!result.Success)
            {
                BackgroundStatusText.Text = result.Error;
                return;
            }
            _previewBackgroundPath = result.ResourcePath;
            _importedBackgrounds.Add(result.ResourcePath);
            BackgroundPathText.Text = Path.GetFileName(result.ResourcePath);
            BackgroundStatusText.Text = Localize.T("Text0230");
            PreviewDraft();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            BackgroundStatusText.Text = Localize.F("Text0231", exception.Message);
        }
    }

    private void ClearBackground_Click(object sender, RoutedEventArgs e)
    {
        if (!_host.CanEdit) return;
        _previewBackgroundPath = string.Empty;
        BackgroundPathText.Text = Localize.T("Text0126");
        BackgroundStatusText.Text = Localize.T("Text0232");
        PreviewDraft();
    }

    private void ScheduleDraftSave()
    {
        _draftDebounce?.Cancel();
        _draftDebounce?.Dispose();
        _draftDebounce = new CancellationTokenSource();
        _ = SaveDraftAfterDelayAsync(_draftDebounce.Token);
    }

    private async Task SaveDraftAfterDelayAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(_host.StorageOptions.DraftDebounceMilliseconds, token);
            var result = await _host.PersistDraftAsync();
            if (!result.Success && !token.IsCancellationRequested) StatusText.Text = result.Error;
        }
        catch (OperationCanceledException) { }
    }

    private async void SaveDraftButton_Click(object sender, RoutedEventArgs e) => await SaveDraftAsync();

    private async Task<bool> SaveDraftCoreAsync()
    {
        CancelDraftDebounce();
        if (!PreviewDraft()) return false;
        CancelDraftDebounce();
        if (_host.Draft is null) return false;
        var old = _host.FormalSettings!;
        var previousBackground = old.Appearance.Visual.BackgroundImagePath;
        var desired = _host.Draft.Settings.Workbench;
        if (!_hotkey.Configure(desired, out var error))
        {
            StatusText.Text = Localize.F("Text0233", error);
            HotkeyStatusText.Text = _hotkey.StatusText;
            ConfigureWindowFallback();
            return false;
        }
        var result = await _host.SaveFormalAsync();
        if (!result.Success)
        {
            _hotkey.Configure(old.Workbench, out _);
            ConfigureWindowFallback();
            HotkeyStatusText.Text = _hotkey.StatusText;
            return ShowResult(result, string.Empty);
        }
        Localize.Configure(_host.FormalSettings?.Regional);
        LoadSettingsControls(_host.FormalSettings);
        UpdateDraftControls();
        HotkeyStatusText.Text = _hotkey.StatusText;
        await CleanupBackgroundCopiesAsync(previousBackground);
        ConfigureWindowFallback();
        return ShowResult(result, result.Warning ?? Localize.T("Text0234"));
    }

    private async void CancelDraftButton_Click(object sender, RoutedEventArgs e) => await CancelDraftAsync();

    private async Task<bool> CancelDraftAsync()
    {
        CancelDraftDebounce();
        var result = await _host.CancelDraftAsync();
        if (!ShowResult(result, Localize.T("Text0235"))) return false;
        LoadSettingsControls(_host.FormalSettings);
        UpdateDraftControls();
        await CleanupBackgroundCopiesAsync();
        return true;
    }

    private async Task CleanupBackgroundCopiesAsync(string? previousFormalBackground = null)
    {
        var candidates = _importedBackgrounds.ToList();
        if (!string.IsNullOrEmpty(previousFormalBackground))
        {
            candidates.Add(previousFormalBackground);
        }
        var retained = new[] { _host.FormalSettings?.Appearance.Visual.BackgroundImagePath, _host.Draft?.Settings.Appearance.Visual.BackgroundImagePath };
        try
        {
            foreach (var path in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                await _visuals.DeleteBackgroundAsync(path, retained, CancellationToken.None);
            }
            _importedBackgrounds.Clear();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            BackgroundStatusText.Text = Localize.F("Text0236", exception.Message);
        }
    }

    private void ApplyTabPosition(TerminalTabPosition position)
    {
        if (_tabLayoutInitialized && _tabPosition == position)
        {
            UpdateWorkbenchChrome();
            return;
        }
        _tabLayoutInitialized = true;
        TabStrip.CancelDrag();
        _tabPosition = position;
        var left = position == TerminalTabPosition.Left;
        TabHost.Width = double.NaN;
        TabHost.Margin = left ? new Thickness(8, 8, 0, 0) : position == TerminalTabPosition.Bottom ? new Thickness(10, 8, 10, 0) : new Thickness(52, 5, 310, 5);
        TabRailBackground.SetResourceReference(Border.BackgroundProperty, left ? "Theme.SidebarBackground" : "Theme.SurfaceBackground");
        Grid.SetRow(TabHost, left ? 2 : position == TerminalTabPosition.Bottom ? 3 : 0);
        Grid.SetColumn(TabHost, 0);
        Grid.SetColumnSpan(TabHost, left ? 1 : 3);
        Grid.SetRow(TabScrollViewer, left ? 1 : 0);
        Grid.SetColumn(TabScrollViewer, left ? 0 : 1);
        Grid.SetColumnSpan(TabScrollViewer, left ? 4 : 1);
        Grid.SetRow(TabNavigationButtons, 0);
        Grid.SetRow(NewTabButton, 0);
        TabCountText.Visibility = Visibility.Collapsed;
        TabScrollViewer.HorizontalScrollBarVisibility = left ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Hidden;
        TabScrollViewer.VerticalScrollBarVisibility = left ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled;
        TabStrip.OrientationEx = left ? Orientation.Vertical : Orientation.Horizontal;
        var factory = new FrameworkElementFactory(typeof(StackPanel));
        factory.SetValue(StackPanel.OrientationProperty, TabStrip.OrientationEx);
        TabStrip.ItemsPanel = new ItemsPanelTemplate(factory);
        var containerStyle = new Style(typeof(ContentPresenter));
        containerStyle.Setters.Add(new Setter(HorizontalContentAlignmentProperty, left ? HorizontalAlignment.Stretch : HorizontalAlignment.Left));
        containerStyle.Setters.Add(new Setter(VerticalContentAlignmentProperty, VerticalAlignment.Stretch));
        if (!left)
        {
            containerStyle.Setters.Add(new Setter(WidthProperty, 210d));
        }
        TabStrip.ItemContainerStyle = containerStyle;
        UpdateWorkbenchChrome();
    }

    private void FocusMode_Click(object sender, RoutedEventArgs e) => ToggleFocusMode();

    private void ToggleFocusMode()
    {
        var now = Environment.TickCount64;
        if (now - _lastFocusToggle < 250)
        {
            return;
        }
        _lastFocusToggle = now;
        _focusMode = !_focusMode;
        if (_focusMode)
        {
            _settingsVisible = false;
            ShowActiveWorkbench();
        }
        UpdateWorkbenchChrome();
        if (_attachedTab?.Session is not null) TerminalView.FocusInput();
    }

    private void PerformanceMode_Click(object sender, RoutedEventArgs e) => TogglePerformanceMode();

    private void TogglePerformanceMode()
    {
        _visuals.TogglePerformanceMode();
        UpdatePerformanceButton();
        StatusText.Text = _visuals.PerformanceModeEnabled
            ? Localize.T("Text0237")
            : Localize.T("Text0238");
    }

    private void UpdatePerformanceButton()
    {
        PerformanceModeButton.ToolTip = _visuals.PerformanceModeEnabled ? Localize.T("Text0239") : Localize.T("Text0240");
        PerformanceModeButton.SetResourceReference(ForegroundProperty, _visuals.PerformanceModeEnabled ? "Theme.AccentBrush" : "Theme.SecondaryText");
    }

    private void Window_SourceInitialized(object? sender, EventArgs e)
    {
        var source = (HwndSource)PresentationSource.FromVisual(this)!;
        _hotkey.Attach(source);
        _commandKeys.Attach(source);
        _visuals.Attach(source);
        UpdateCaptionButtons();
        VisualCapabilityText.Text = _visuals.CapabilityText;
        var settings = _host.FormalSettings?.Workbench;
        if (settings is not null)
        {
            _hotkey.Configure(settings, out var error);
            ConfigureWindowFallback();
            HotkeyStatusText.Text = _hotkey.StatusText;
            if (!string.IsNullOrEmpty(error)) StatusText.Text = Localize.F("Text0241", error);
        }
    }

    private void ShowCommandPalette_Click(object sender, RoutedEventArgs e) => ShowCommandPalette();

    private void ShowCommandPalette()
    {
        if (DiagnosticPreviewPanel.Visibility == Visibility.Visible) return;
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Show();
        Activate();
        MotionPolicy.Show(CommandPalette);
        CommandInput.Text = string.Empty;
        RefreshCommands();
        CommandInput.Focus();
    }

    private void CloseCommandPalette()
    {
        MotionPolicy.Hide(CommandPalette);
        if (_attachedTab?.Session is not null && !_settingsVisible) TerminalView.FocusInput();
    }

    private void RefreshCommands()
    {
        CommandResults.ItemsSource = _commands.Search(CommandInput.Text);
        CommandResults.SelectedIndex = CommandResults.Items.Count > 0 ? 0 : -1;
    }

    private void CommandInput_TextChanged(object sender, TextChangedEventArgs e) => RefreshCommands();

    private async void CommandInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { CloseCommandPalette(); e.Handled = true; }
        else if (e.Key == Key.Down) { CommandResults.Focus(); if (CommandResults.SelectedIndex < 0) CommandResults.SelectedIndex = 0; e.Handled = true; }
        else if (e.Key == Key.Enter) { await ExecuteSelectedCommand(); e.Handled = true; }
    }

    private async void CommandResults_MouseDoubleClick(object sender, MouseButtonEventArgs e) => await ExecuteSelectedCommand();

    private async Task ExecuteSelectedCommand()
    {
        if (CommandResults.SelectedItem is not CommandDescriptor command) return;
        CloseCommandPalette();
        try { await command.ExecuteAsync(CancellationToken.None); }
        catch (Exception exception) { StatusText.Text = Localize.F("Text0242", exception.Message); }
    }

    private async void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (DiagnosticPreviewPanel.Visibility == Visibility.Visible) return;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var terminalFocused = TerminalView.IsVisible && TerminalView.IsInputFocused && !TerminalView.IsComposing;
        var command = _commands.Match(key, Keyboard.Modifiers, terminalFocused);
        if (command is null) return;
        e.Handled = true;
        await ExecuteCommandAsync(command);
    }

    private void ReturnBottom_Click(object sender, RoutedEventArgs e) => TerminalView.ScrollToBottom();
    private void ShowSearch() { MotionPolicy.Show(SearchPanel); SearchInput.Focus(); SearchInput.SelectAll(); }

    private async void SearchInput_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_initializing) return;
        _tabs.ActiveTab.SearchQuery = SearchInput.Text;
        await TerminalView.SearchAsync(SearchInput.Text);
        UpdateSearchStatus();
    }

    private void SearchInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { TerminalView.NavigateSearch(Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? -1 : 1); UpdateSearchStatus(); e.Handled = true; }
        else if (e.Key == Key.Escape) { CloseSearch(); e.Handled = true; }
    }

    private void SearchPrevious_Click(object sender, RoutedEventArgs e) { TerminalView.NavigateSearch(-1); UpdateSearchStatus(); }
    private void SearchNext_Click(object sender, RoutedEventArgs e) { TerminalView.NavigateSearch(1); UpdateSearchStatus(); }
    private void CloseSearch_Click(object sender, RoutedEventArgs e) => CloseSearch();
    private async void CloseSearch() { MotionPolicy.Hide(SearchPanel); SearchInput.Text = string.Empty; await TerminalView.SearchAsync(string.Empty); UpdateSearchStatus(); TerminalView.FocusInput(); }
    private void UpdateSearchStatus() => SearchStatusText.Text = TerminalView.SearchMatchCount == 0 ? Localize.T("Text0243") : $"{TerminalView.ActiveSearchMatch + 1}/{TerminalView.SearchMatchCount}";

    private async void ResetButton_Click(object sender, RoutedEventArgs e)
    {
        if (ShowResult(await _host.ResetInvalidDocumentsAsync(), Localize.T("Text0244"))) InitializeView();
    }

    private void RefreshErrors()
    {
        ErrorPanel.Visibility = _host.StartupIssues.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        ErrorText.Text = string.Join(Environment.NewLine + Environment.NewLine, _host.StartupIssues);
    }

    private async void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_allowClose) return;
        e.Cancel = true;
        if (_closing) return;
        _closing = true;
        try
        {
            CancelDraftDebounce();
            if (_host.Draft is not null)
            {
                var choice = MessageBox.Show(this, Localize.T("Text0245"), Localize.T("Text0246"), MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
                if (choice == MessageBoxResult.Cancel) return;
                var completed = choice == MessageBoxResult.Yes ? await SaveDraftAsync() : await CancelDraftAsync();
                if (!completed) return;
            }
            if (_host.ProfilesDraft is not null)
            {
                var choice = MessageBox.Show(this, Localize.T("Text0247"), Localize.T("Text0248"), MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
                if (choice == MessageBoxResult.Cancel) return;
                if (!(choice == MessageBoxResult.Yes ? await SaveProfilesWithLockAsync() : await _profileEditor.CancelAsync())) return;
            }
            if (_host.FormalSettings?.Workbench.ConfirmCloseRunning == true && _tabs.RunningTabs.Any())
            {
                if (MessageBox.Show(this, Localize.T("Text0249"), Localize.T("Text0250"), MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            }
            _layoutDebounce?.Cancel();
            _detectCancellation?.Cancel();
            await _agentPicker.StopAsync();
            await _mcp.DisposeAsync();
            TabStrip.CancelDrag();
            _commandKeys.Dispose();
            _hotkey.Dispose();
            await _tabs.CloseAllAsync();
            var shutdown = await _host.ShutdownAsync();
            if (!ShowResult(shutdown, string.Empty)) return;
            _profileEditor.Dispose();
            _performanceTimer.Stop();
            FontPreview.Detach();
            _profileEditor.FontPreview.Detach();
            _visuals.Dispose();
            _allowClose = true;
            Close();
        }
        finally { _closing = false; }
    }

    private void CancelDraftDebounce()
    {
        _draftDebounce?.Cancel();
        _draftDebounce?.Dispose();
        _draftDebounce = null;
    }

    private bool ShowResult(DocumentSaveResult result, string successMessage)
    {
        if (!result.Success) { _host.Diagnostics.Record(ArkTerminal.Core.Diagnostics.DiagnosticComponent.Configuration, ArkTerminal.Core.Diagnostics.DiagnosticEventKind.WriteFailed); StatusText.Text = result.Error ?? Localize.T("Text0251"); return false; }
        if (!string.IsNullOrEmpty(successMessage)) StatusText.Text = successMessage;
        return true;
    }

    private static string ExpandHome(string path) => path.Replace("{{home}}", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), StringComparison.OrdinalIgnoreCase);
}
