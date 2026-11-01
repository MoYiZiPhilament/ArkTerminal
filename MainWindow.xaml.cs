using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using ArkTerminal.Commands;
using ArkTerminal.Core.Configuration;
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
        _visuals.PresentationChanged += () =>
        {
            VisualCapabilityText.Text = _visuals.CapabilityText;
            UpdateDraftControls();
            UpdatePerformanceButton();
        };
        _tabs = new TerminalTabsViewModel(host.Sessions);
        TabStrip.ItemsSource = _tabs.Tabs;
        _tabs.ActiveTabChanged += AttachActiveTab;
        _tabs.LayoutChanged += ScheduleLayoutSave;
        _tabs.TabDetaching += DetachTab;
        TabStrip.Dropped += TabStrip_Dropped;
        TerminalView.InputError += message => Dispatcher.BeginInvoke(() => StatusText.Text = message);
        TerminalView.SearchRequested += ShowSearch;
        DpiChanged += (_, _) => TerminalView.NotifyDpiChanged();
        LocationChanged += (_, _) => TerminalView.InvalidateArrange();
        _hotkey.Invoked += () => Dispatcher.BeginInvoke(ShowCommandPalette);
        _hotkey.FocusInvoked += () => Dispatcher.BeginInvoke(ToggleFocusMode);
        _commandKeys.CommandPaletteRequested += () => Dispatcher.BeginInvoke(ShowCommandPalette);
        _commandKeys.FocusModeRequested += () => Dispatcher.BeginInvoke(ToggleFocusMode);
        RegisterCommands();
        InitializeView();
    }

    private void InitializeView()
    {
        _initializing = true;
        ModeText.Text = $"模式：{_host.Paths.Mode}";
        DataRootText.Text = $"实际数据目录：{_host.Paths.RootDirectory}";
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
        _commands.Register(new CommandDescriptor { Id = "workbench.new", Title = "新建页", Category = "标签", Keywords = "new shell agent", ExecuteAsync = _ => { _tabs.ShowNewPage(); return Task.CompletedTask; } });
        _commands.Register(new CommandDescriptor { Id = "workbench.settings", Title = "打开设置", Category = "设置", Keywords = "外观 快捷键 草稿", ExecuteAsync = _ => { ShowSettings(); return Task.CompletedTask; } });
        _commands.Register(new CommandDescriptor { Id = "workbench.focus", Title = "切换专注模式", Category = "视图", Keywords = "focus Ctrl+Shift+F11", ExecuteAsync = _ => { ToggleFocusMode(); return Task.CompletedTask; } });
        _commands.Register(new CommandDescriptor { Id = "workbench.performance", Title = "切换一键性能模式", Category = "视图", Keywords = "performance 极光 透明 模糊 动效", ExecuteAsync = _ => { TogglePerformanceMode(); return Task.CompletedTask; } });
        _commands.Register(() => _tabs.Tabs.Select(tab => new CommandDescriptor
        {
            Id = $"tab.activate.{tab.TabId:N}",
            Title = $"切换到：{tab.Title} · {tab.StatusText}",
            Category = "标签",
            Keywords = $"{tab.ProfileName} {tab.WorkingDirectory}",
            ExecuteAsync = _ => { _tabs.Activate(tab); return Task.CompletedTask; }
        }).ToArray());
        _commands.Register(() => _host.Catalog?.Profiles.Select(profile => new CommandDescriptor
        {
            Id = $"profile.launch.{profile.Id}",
            Title = $"启动 Profile：{profile.Name}",
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
        NewPage.Visibility = tab.IsNewPage ? Visibility.Visible : Visibility.Collapsed;
        SettingsPanel.Visibility = Visibility.Collapsed;
        TerminalPanel.Visibility = tab.IsNewPage ? Visibility.Collapsed : Visibility.Visible;
        MotionPolicy.Show(tab.IsNewPage ? NewPage : TerminalPanel, _visuals.AnimationsEnabled);
        Title = $"ArkTerminal · {tab.Title}";
        if (!tab.IsNewPage && tab.Session is not null && tab.Launch is not null)
        {
            PlaceholderPanel.Visibility = Visibility.Collapsed;
            TerminalView.Visibility = Visibility.Visible;
            TerminalView.Attach(tab.Session, tab.Launch.FontOptions, tab.ScrollbackOffset);
            if (tab.ZoomFontSize is { } size)
            {
                TerminalView.ApplyFontOptions(tab.Launch.FontOptions with { FontSize = size });
            }
            _initializing = true;
            SearchInput.Text = tab.SearchQuery;
            _initializing = false;
            SearchPanel.Visibility = string.IsNullOrEmpty(tab.SearchQuery) ? Visibility.Collapsed : Visibility.Visible;
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
            SearchPanel.Visibility = Visibility.Collapsed;
        }
        UpdateActiveStatus();
        TabCountText.Text = $"{_tabs.Tabs.Count} 个标签";
        Dispatcher.BeginInvoke(() =>
        {
            if (TabStrip.ItemContainerGenerator.ContainerFromItem(tab) is FrameworkElement container)
            {
                container.BringIntoView();
            }
        });
    }

    private void UpdateActiveStatus()
    {
        var tab = _tabs.ActiveTab;
        Title = $"ArkTerminal · {tab.Title}";
        TabCountText.Text = $"{_tabs.Tabs.Count} 个标签";
        TerminalStatusText.Text = tab.IsNewPage ? string.Empty : $"{tab.StatusText} · {tab.ProfileName}";
        PlaceholderTitle.Text = tab.Title;
        PlaceholderDetails.Text = $"{tab.StatusText}\n{tab.WorkingDirectory}\n启动前不会执行启动后输入，也不会触发 UAC。";
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
        tab.ZoomFontSize = TerminalView.CurrentFontSize;
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
            var launch = TabLaunchConfiguration.FromProfile(profile, ExpandHome(workingDirectory), _host.FormalSettings?.Appearance);
            _settingsVisible = false;
            var tab = await _tabs.LaunchAsync(launch);
            if (tab.State == TabRuntimeState.Running)
            {
                await AddRecentAsync(launch.WorkingDirectory);
                StatusText.Text = $"已启动 {profile.Name}。";
            }
            else
            {
                StatusText.Text = tab.Error ?? "会话未能启动。";
            }
        }
        catch (Exception exception)
        {
            StatusText.Text = $"启动失败：{exception.Message}";
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
        if (tab.Launch is null || !ConfirmRunning(tab, "重启"))
        {
            return;
        }
        await _tabs.RestartAsync(tab);
        AttachActiveTab();
    }

    private async Task CloseTabAsync(TabViewModel tab)
    {
        if (tab.IsNewPage || !ConfirmRunning(tab, "关闭"))
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
        return MessageBox.Show(this, $"“{tab.Title}”仍在运行。确定{action}吗？", action, MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;
    }

    private void NewPage_Click(object sender, RoutedEventArgs e)
    {
        _settingsVisible = false;
        _tabs.ShowNewPage();
        if (_attachedTab == _tabs.ActiveTab)
        {
            AttachActiveTab();
            NewPage.Visibility = Visibility.Visible;
            TerminalPanel.Visibility = Visibility.Collapsed;
            SettingsPanel.Visibility = Visibility.Collapsed;
        }
    }

    private void Tab_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: TabViewModel tab })
        {
            _tabs.Activate(tab);
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
        var input = new TextBox { Text = tab.FixedTitle ?? string.Empty, Width = 320, Margin = new Thickness(0, 0, 0, 10) };
        var dialog = new Window { Owner = this, Title = "固定标签名称（留空清除）", Width = 380, Height = 160, WindowStartupLocation = WindowStartupLocation.CenterOwner, Content = input };
        input.KeyDown += (_, args) => { if (args.Key == Key.Enter) { _tabs.Rename(tab, input.Text); dialog.Close(); } else if (args.Key == Key.Escape) dialog.Close(); };
        dialog.ShowDialog();
    }

    private void BrowseDirectory_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "选择项目目录", InitialDirectory = DirectoryInput.Text };
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
        DetectionStatus.Text = "正在识别当前目录及最多 5 层父目录…";
        try
        {
            var result = await ProjectRootDetector.DetectAsync(DirectoryInput.Text, 5, _detectCancellation.Token);
            DetectionStatus.Text = result.Error ?? (result.Candidates.Count == 0 ? "未发现项目标记；仍可使用当前目录。" : "选择建议根目录只改变下次启动目录：");
            DetectionResults.ItemsSource = result.Candidates.Select(candidate => new { Path = candidate.Path, DisplayText = $"{candidate.Path} · {string.Join(", ", candidate.Markers)}" }).ToArray();
        }
        catch (OperationCanceledException)
        {
            DetectionStatus.Text = "项目识别已取消。";
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
                    StatusText.Text = $"恢复占位缺少 Profile：{state.ProfileId}";
                    continue;
                }
                try
                {
                    var launch = TabLaunchConfiguration.FromProfile(profile, ExpandHome(state.WorkingDirectory), _host.FormalSettings?.Appearance);
                    var tab = _tabs.AddPlaceholder(launch, state.Title, recovered: true);
                    if (state.Id == _host.Recovery.ActiveTabId) _tabs.Activate(tab);
                }
                catch (Exception exception)
                {
                    StatusText.Text = $"恢复标签失败：{exception.Message}";
                }
            }
        }
        _initializing = false;
        RecoveryPanel.Visibility = Visibility.Collapsed;
        LoadSettingsControls(_host.Draft?.Settings ?? _host.FormalSettings);
        UpdateDraftControls();
        ScheduleLayoutSave();
    }

    private async void DiscardRecoveryButton_Click(object sender, RoutedEventArgs e)
    {
        CancelDraftDebounce();
        var result = await _host.DiscardRecoveryAsync();
        if (!ShowResult(result, "恢复数据已丢弃；正式设置未改变。")) return;
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
        NewPage.Visibility = Visibility.Collapsed;
        TerminalPanel.Visibility = Visibility.Collapsed;
        SettingsPanel.Visibility = Visibility.Visible;
        LoadSettingsControls(_host.Draft?.Settings ?? _host.FormalSettings);
        UpdateDraftControls();
        MotionPolicy.Show(SettingsPanel, _visuals.AnimationsEnabled);
    }

    private void Settings_Click(object sender, RoutedEventArgs e) => ShowSettings();

    private void LoadSettingsControls(AppSettings? settings)
    {
        if (settings is null) return;
        _initializing = true;
        FontSizeInput.Text = settings.Appearance.UiFontSize.ToString(CultureInfo.InvariantCulture);
        FontSize = settings.Appearance.UiFontSize;
        SelectTaggedItem(TabPositionInput, settings.Workbench.TabPosition.ToString());
        ConfirmCloseInput.IsChecked = settings.Workbench.ConfirmCloseRunning;
        HotkeyEnabledInput.IsChecked = settings.Workbench.CommandPaletteHotkeyEnabled;
        HotkeyInput.Text = settings.Workbench.CommandPaletteHotkey;
        var visual = settings.Appearance.Visual;
        SelectTaggedItem(ThemeInput, visual.Theme.ToString());
        SelectTaggedItem(MaterialInput, visual.Material.ToString());
        DesktopBackgroundInput.IsChecked = visual.DesktopBackgroundEnabled;
        AuroraEnabledInput.IsChecked = visual.AuroraEnabled;
        SelectTaggedItem(AuroraPresetInput, visual.AuroraPresetId);
        AuroraColorsInput.Text = string.Join(", ", visual.AuroraCustomColors);
        _previewBackgroundPath = visual.BackgroundImagePath;
        BackgroundPathText.Text = string.IsNullOrEmpty(_previewBackgroundPath) ? "未设置" : Path.GetFileName(_previewBackgroundPath);
        BackgroundOverlayInput.Value = visual.BackgroundOverlayOpacity;
        BackgroundBlurInput.Value = visual.BackgroundBlurRadius;
        BackgroundBrightnessInput.Value = visual.BackgroundBrightness;
        BackgroundSaturationInput.Value = visual.BackgroundSaturation;
        SelectTaggedItem(BackgroundFitInput, visual.BackgroundFit.ToString());
        SelectTaggedItem(BackgroundPositionInput, visual.BackgroundPosition.ToString());
        TerminalOpacityInput.Value = visual.TerminalBackgroundOpacity;
        _initializing = false;
        ApplyTabPosition(settings.Workbench.TabPosition);
        _visuals.Apply(settings.Appearance);
        VisualCapabilityText.Text = _visuals.CapabilityText;
    }

    private void UpdateDraftControls()
    {
        var editing = _host.Draft is not null;
        FontSizeInput.IsEnabled = editing;
        VisualSettingsControls.IsEnabled = editing;
        MaterialInput.IsEnabled = editing && _visuals.SupportsSystemBackdrop;
        DesktopBackgroundInput.IsEnabled = editing && _visuals.SupportsDesktopBackground;
        SaveDraftButton.IsEnabled = editing;
        CancelDraftButton.IsEnabled = editing;
        EditDraftButton.IsEnabled = _host.CanEdit && !editing;
    }

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

    private async void EditDraftButton_Click(object sender, RoutedEventArgs e)
    {
        if (ShowResult(await _host.BeginDraftAsync(), "草稿已创建；修改将自动保存到独立恢复文件。")) UpdateDraftControls();
    }

    private void FontSizeInput_TextChanged(object sender, TextChangedEventArgs e) => PreviewDraft();
    private void WorkbenchInput_Changed(object sender, RoutedEventArgs e) => PreviewDraft();
    private void VisualInput_Changed(object sender, RoutedEventArgs e) => PreviewDraft();

    private void PreviewDraft()
    {
        if (_initializing || _host.Draft is null) return;
        if (!double.TryParse(FontSizeInput.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var size) || size is < 6 or > 96 || TabPositionInput.SelectedItem is not ComboBoxItem position)
        {
            StatusText.Text = "请输入有效设置后再保存。";
            return;
        }
        var preview = _host.Draft.Settings.DeepClone();
        preview.Appearance.UiFontSize = size;
        preview.Workbench.TabPosition = Enum.Parse<TerminalTabPosition>((string)position.Tag);
        preview.Workbench.ConfirmCloseRunning = ConfirmCloseInput.IsChecked == true;
        preview.Workbench.CommandPaletteHotkeyEnabled = HotkeyEnabledInput.IsChecked == true;
        preview.Workbench.CommandPaletteHotkey = HotkeyInput.Text;
        var visual = preview.Appearance.Visual;
        visual.Theme = Enum.Parse<AppThemeMode>(GetTag(ThemeInput));
        if (_visuals.SupportsSystemBackdrop)
        {
            visual.Material = Enum.Parse<WindowMaterialMode>(GetTag(MaterialInput));
        }
        if (_visuals.SupportsDesktopBackground)
        {
            visual.DesktopBackgroundEnabled = DesktopBackgroundInput.IsChecked == true;
        }
        visual.AuroraEnabled = AuroraEnabledInput.IsChecked == true;
        visual.AuroraPresetId = GetTag(AuroraPresetInput);
        visual.AuroraCustomColors = AuroraColorsInput.Text.Split([',', ';', '，'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToList();
        visual.BackgroundImagePath = _previewBackgroundPath;
        visual.BackgroundOverlayOpacity = BackgroundOverlayInput.Value;
        visual.BackgroundBlurRadius = BackgroundBlurInput.Value;
        visual.BackgroundBrightness = BackgroundBrightnessInput.Value;
        visual.BackgroundSaturation = BackgroundSaturationInput.Value;
        visual.BackgroundFit = Enum.Parse<BackgroundImageFit>(GetTag(BackgroundFitInput));
        visual.BackgroundPosition = Enum.Parse<BackgroundImagePosition>(GetTag(BackgroundPositionInput));
        visual.TerminalBackgroundOpacity = TerminalOpacityInput.Value;
        var validation = ConfigurationValidator.ValidateSettings(preview);
        if (!validation.IsValid)
        {
            StatusText.Text = validation.Summary;
            return;
        }
        _host.UpdateDraft(preview);
        FontSize = size;
        ApplyTabPosition(preview.Workbench.TabPosition);
        _visuals.Apply(preview.Appearance);
        ScheduleDraftSave();
    }

    private async void ImportBackground_Click(object sender, RoutedEventArgs e)
    {
        if (_host.Draft is null) return;
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "选择背景图（副本将保存到当前数据根）",
            Filter = "支持的图片|*.png;*.jpg;*.jpeg;*.bmp|PNG|*.png|JPEG|*.jpg;*.jpeg|BMP|*.bmp",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog(this) != true) return;
        BackgroundStatusText.Text = "正在后台验证并复制图片…";
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
            BackgroundStatusText.Text = "图片副本已就绪；保存设置后正式使用。";
            PreviewDraft();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            BackgroundStatusText.Text = $"背景图导入失败：{exception.Message}";
        }
    }

    private void ClearBackground_Click(object sender, RoutedEventArgs e)
    {
        if (_host.Draft is null) return;
        _previewBackgroundPath = string.Empty;
        BackgroundPathText.Text = "未设置";
        BackgroundStatusText.Text = "预览已清除背景图；应用拥有的未引用副本将在保存或取消后清理。";
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
            if (!result.Success) StatusText.Text = result.Error;
        }
        catch (OperationCanceledException) { }
    }

    private async void SaveDraftButton_Click(object sender, RoutedEventArgs e) => await SaveDraftAsync();

    private async Task<bool> SaveDraftAsync()
    {
        CancelDraftDebounce();
        PreviewDraft();
        if (_host.Draft is null) return false;
        var old = _host.FormalSettings!;
        var previousBackground = old.Appearance.Visual.BackgroundImagePath;
        var desired = _host.Draft.Settings.Workbench;
        var changed = old.Workbench.CommandPaletteHotkeyEnabled != desired.CommandPaletteHotkeyEnabled || old.Workbench.CommandPaletteHotkey != desired.CommandPaletteHotkey;
        if (changed && !_hotkey.Configure(desired.CommandPaletteHotkeyEnabled, desired.CommandPaletteHotkey, out var error))
        {
            StatusText.Text = $"热键未更改：{error}";
            HotkeyStatusText.Text = _hotkey.StatusText;
            return false;
        }
        var result = await _host.SaveFormalAsync();
        if (!result.Success)
        {
            if (changed) _hotkey.Configure(old.Workbench.CommandPaletteHotkeyEnabled, old.Workbench.CommandPaletteHotkey, out _);
            return ShowResult(result, string.Empty);
        }
        LoadSettingsControls(_host.FormalSettings);
        UpdateDraftControls();
        HotkeyStatusText.Text = _hotkey.StatusText;
        await CleanupBackgroundCopiesAsync(previousBackground);
        return ShowResult(result, "正式设置已保存；草稿已清理。");
    }

    private async void CancelDraftButton_Click(object sender, RoutedEventArgs e) => await CancelDraftAsync();

    private async Task<bool> CancelDraftAsync()
    {
        CancelDraftDebounce();
        var result = await _host.CancelDraftAsync();
        if (!ShowResult(result, "预览已取消，草稿已删除。")) return false;
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
            BackgroundStatusText.Text = $"设置已生效，但未引用的背景副本清理失败：{exception.Message}";
        }
    }

    private void ApplyTabPosition(TerminalTabPosition position)
    {
        TabStrip.CancelDrag();
        var left = position == TerminalTabPosition.Left;
        TabHost.Width = double.NaN;
        LeftTabColumn.Width = left ? new GridLength(280) : GridLength.Auto;
        LeftTabColumn.MinWidth = left ? 180 : 0;
        LeftTabColumn.MaxWidth = left ? 520 : double.PositiveInfinity;
        LeftTabSplitter.Visibility = left ? Visibility.Visible : Visibility.Collapsed;
        Grid.SetRow(TabHost, left ? 2 : position == TerminalTabPosition.Bottom ? 3 : 0);
        Grid.SetColumn(TabHost, 0);
        Grid.SetColumnSpan(TabHost, left ? 1 : 3);
        Grid.SetRow(TabScrollViewer, left ? 1 : 0);
        Grid.SetColumn(TabScrollViewer, left ? 0 : 1);
        Grid.SetColumnSpan(TabScrollViewer, left ? 4 : 1);
        Grid.SetRow(TabNavigationButtons, 0);
        Grid.SetRow(NewTabButton, 0);
        TabCountText.Visibility = left ? Visibility.Collapsed : Visibility.Visible;
        TabScrollViewer.HorizontalScrollBarVisibility = left ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto;
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
            containerStyle.Setters.Add(new Setter(WidthProperty, 180d));
        }
        TabStrip.ItemContainerStyle = containerStyle;
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
        NavigationPanel.Visibility = _focusMode ? Visibility.Collapsed : Visibility.Visible;
        if (_focusMode)
        {
            _settingsVisible = false;
            SettingsPanel.Visibility = Visibility.Collapsed;
            if (_attachedTab?.IsNewPage == false) TerminalPanel.Visibility = Visibility.Visible;
        }
        if (_attachedTab?.Session is not null) TerminalView.FocusInput();
    }

    private void PerformanceMode_Click(object sender, RoutedEventArgs e) => TogglePerformanceMode();

    private void TogglePerformanceMode()
    {
        _visuals.TogglePerformanceMode();
        UpdatePerformanceButton();
        StatusText.Text = _visuals.PerformanceModeEnabled
            ? "性能模式已启用：极光、透明/材质、模糊和非必要动效已临时关闭。"
            : "性能模式已退出：已恢复当前正式设置或草稿的视觉偏好。";
    }

    private void UpdatePerformanceButton()
    {
        PerformanceModeButton.Content = _visuals.PerformanceModeEnabled ? "退出性能模式" : "性能模式";
    }

    private void Window_SourceInitialized(object? sender, EventArgs e)
    {
        var source = (HwndSource)PresentationSource.FromVisual(this)!;
        _hotkey.Attach(source);
        _commandKeys.Attach(source);
        _visuals.Attach(source);
        VisualCapabilityText.Text = _visuals.CapabilityText;
        var settings = _host.FormalSettings?.Workbench;
        if (settings is not null)
        {
            _hotkey.Configure(settings.CommandPaletteHotkeyEnabled, settings.CommandPaletteHotkey, out var error);
            HotkeyStatusText.Text = _hotkey.StatusText;
            if (!string.IsNullOrEmpty(error)) StatusText.Text = $"命令面板仍可从窗口按钮打开；{error}";
        }
    }

    private void ShowCommandPalette_Click(object sender, RoutedEventArgs e) => ShowCommandPalette();

    private void ShowCommandPalette()
    {
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Show();
        Activate();
        CommandPalette.Visibility = Visibility.Visible;
        MotionPolicy.Show(CommandPalette, _visuals.AnimationsEnabled, 12);
        CommandInput.Text = string.Empty;
        RefreshCommands();
        CommandInput.Focus();
    }

    private void CloseCommandPalette()
    {
        CommandPalette.Visibility = Visibility.Collapsed;
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
        catch (Exception exception) { StatusText.Text = $"命令失败：{exception.Message}"; }
    }

    private async void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var modifiers = Keyboard.Modifiers;
        if (modifiers.HasFlag(ModifierKeys.Control) && e.Key == Key.Tab) { _tabs.Next(modifiers.HasFlag(ModifierKeys.Shift) ? -1 : 1); e.Handled = true; }
        else if (modifiers.HasFlag(ModifierKeys.Control) && modifiers.HasFlag(ModifierKeys.Shift) && e.Key == Key.T) { _tabs.ShowNewPage(); e.Handled = true; }
        else if (modifiers.HasFlag(ModifierKeys.Control) && modifiers.HasFlag(ModifierKeys.Shift) && e.Key == Key.W) { await CloseTabAsync(_tabs.ActiveTab); e.Handled = true; }
    }

    private void ReturnBottom_Click(object sender, RoutedEventArgs e) => TerminalView.ScrollToBottom();
    private void ShowSearch() { SearchPanel.Visibility = Visibility.Visible; MotionPolicy.Show(SearchPanel, _visuals.AnimationsEnabled, 4); SearchInput.Focus(); SearchInput.SelectAll(); }

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
    private async void CloseSearch() { SearchPanel.Visibility = Visibility.Collapsed; SearchInput.Text = string.Empty; await TerminalView.SearchAsync(string.Empty); UpdateSearchStatus(); TerminalView.FocusInput(); }
    private void UpdateSearchStatus() => SearchStatusText.Text = TerminalView.SearchMatchCount == 0 ? "无匹配" : $"{TerminalView.ActiveSearchMatch + 1}/{TerminalView.SearchMatchCount}";

    private async void ResetButton_Click(object sender, RoutedEventArgs e)
    {
        if (ShowResult(await _host.ResetInvalidDocumentsAsync(), "无效文档已恢复默认值；原损坏备份保留。")) InitializeView();
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
                var choice = MessageBox.Show(this, "存在未保存设置草稿。保存后关闭、放弃草稿，还是取消关闭？", "未保存设置", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
                if (choice == MessageBoxResult.Cancel) return;
                var completed = choice == MessageBoxResult.Yes ? await SaveDraftAsync() : await CancelDraftAsync();
                if (!completed) return;
            }
            if (_host.FormalSettings?.Workbench.ConfirmCloseRunning == true && _tabs.RunningTabs.Any())
            {
                if (MessageBox.Show(this, "仍有运行中的会话。关闭窗口将结束这些会话，是否继续？", "关闭 ArkTerminal", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            }
            _layoutDebounce?.Cancel();
            _detectCancellation?.Cancel();
            TabStrip.CancelDrag();
            _commandKeys.Dispose();
            _hotkey.Dispose();
            await _tabs.CloseAllAsync();
            var shutdown = await _host.ShutdownAsync();
            if (!ShowResult(shutdown, string.Empty)) return;
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
        if (!result.Success) { StatusText.Text = result.Error ?? "操作失败。"; return false; }
        if (!string.IsNullOrEmpty(successMessage)) StatusText.Text = successMessage;
        return true;
    }

    private static string ExpandHome(string path) => path.Replace("{{home}}", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), StringComparison.OrdinalIgnoreCase);
}
