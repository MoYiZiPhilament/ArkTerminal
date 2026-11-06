using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ArkTerminal.Controls;
using ArkTerminal.Core.Configuration;
using ArkTerminal.Core.Profiles;
using ArkTerminal.Infrastructure;
using ArkTerminal.Rendering;
using ArkTerminal.Sessions;
using ArkTerminal.Visuals;

namespace ArkTerminal;

public partial class MainWindow
{
    private ProfileEditorControl _profileEditor = null!;
    private bool _stageSixReady;
    private bool _settingsValid = true;
    private bool _directoryExplicit;
    private bool _savingSettings;

    private TextBox[] SettingsTextInputs => [UiFontInput, FontSizeInput, TerminalFontInput, TerminalFontSizeInput, TerminalWeightInput,
        TerminalLineHeightInput, TerminalSpacingInput, AccentInput, HotkeyInput, FocusHotkeyInput, AuroraColorsInput, LongTaskSecondsInput];
    private CheckBox[] SettingsCheckInputs => [LigaturesInput, TimestampsInput, LineNumbersInput, DurationInput, HotkeyEnabledInput,
        FocusHotkeyEnabledInput, ConfirmCloseInput, DesktopBackgroundInput, AuroraEnabledInput, NotifyCompletedInput, NotifyLongTaskInput, NotifyReadyInput];
    private ComboBox[] SettingsChoiceInputs => [TabPositionInput, DefaultProfileInput, LanguageInput, TimeZoneInput, ThemeInput, MaterialInput, AuroraPresetInput, BackgroundFitInput, BackgroundPositionInput];
    private Slider[] SettingsSliderInputs => [BackgroundOverlayInput, BackgroundBlurInput, BackgroundBrightnessInput, BackgroundSaturationInput, TerminalOpacityInput];

    private void InitializeStageSix()
    {
        _profileEditor = new(_host);
        ProfileEditorHost.Content = _profileEditor;
        _profileEditor.StatusChanged += message => StatusText.Text = message;
        _profileEditor.PreviewChanged += UpdateFontPreviews;
        _profileEditor.CatalogSaved += () =>
        {
            ProfilesList.ItemsSource = _host.Catalog?.Profiles;
            RefreshDefaultProfiles();
            UpdateFontPreviews();
        };
        TerminalView.ViewportChanged += UpdateAuxiliaryDisplay;
        _visuals.PresentationChanged += UpdateFontPreviews;
        InitializeStageSixCommands();
        TimeZoneInput.ItemsSource = TimeZoneInfo.GetSystemTimeZones();
        SettingsDataRootText.Text = Localize.F("Text0125", _host.Paths.Mode, _host.Paths.RootDirectory);
        _stageSixReady = true;
        StateChanged += (_, _) => UpdatePerformanceSampling();
        IsVisibleChanged += (_, _) => UpdatePerformanceSampling();
        Activated += (_, _) => UpdatePerformanceSampling();
    }

    private void RefreshDefaultProfiles()
    {
        var initializing = _initializing;
        _initializing = true;
        var selected = DefaultProfileInput.SelectedValue as string ?? _host.FormalSettings?.DefaultProfileId ?? string.Empty;
        DefaultProfileInput.ItemsSource = new[] { new TerminalProfile { Id = string.Empty, Name = Localize.T("Text0126") } }.Concat(_host.Catalog?.Profiles ?? []);
        DefaultProfileInput.SelectedValue = selected;
        _initializing = initializing;
    }

    private void LoadSettingsControls(AppSettings? settings)
    {
        if (settings is null) return;
        _initializing = true;
        var appearance = settings.Appearance;
        UiFontInput.Text = appearance.UiFontFamily;
        FontSizeInput.Text = appearance.UiFontSize.ToString(CultureInfo.InvariantCulture);
        TerminalFontInput.Text = appearance.TerminalFontFamily;
        TerminalFontSizeInput.Text = appearance.TerminalFontSize.ToString(CultureInfo.InvariantCulture);
        TerminalWeightInput.Text = appearance.TerminalFontWeight.ToString(CultureInfo.InvariantCulture);
        TerminalLineHeightInput.Text = appearance.TerminalLineHeight.ToString(CultureInfo.InvariantCulture);
        TerminalSpacingInput.Text = appearance.TerminalCharacterSpacing.ToString(CultureInfo.InvariantCulture);
        AccentInput.Text = appearance.AccentColor;
        LigaturesInput.IsChecked = appearance.TerminalLigatures;
        TimestampsInput.IsChecked = appearance.ShowTimestamps;
        LineNumbersInput.IsChecked = appearance.ShowLogicalLineNumbers;
        DurationInput.IsChecked = appearance.ShowCommandDuration;
        SelectTaggedItem(TabPositionInput, settings.Workbench.TabPosition.ToString());
        ConfirmCloseInput.IsChecked = settings.Workbench.ConfirmCloseRunning;
        HotkeyEnabledInput.IsChecked = settings.Workbench.CommandPaletteHotkeyEnabled;
        HotkeyInput.Text = settings.Workbench.CommandPaletteHotkey;
        FocusHotkeyEnabledInput.IsChecked = settings.Workbench.FocusModeHotkeyEnabled;
        FocusHotkeyInput.Text = settings.Workbench.FocusModeHotkey;
        RefreshDefaultProfiles();
        DefaultProfileInput.SelectedValue = settings.DefaultProfileId ?? string.Empty;
        LanguageInput.SelectedValue = settings.Regional.Language;
        TimeZoneInput.SelectedValue = settings.Regional.TimeZoneId;
        HistoryEnabledInput.IsChecked = _host.CommandHistory.Enabled;
        TranscriptionEnabledInput.IsChecked = _tabs.ActiveTab.Session is { } recordingSession && _host.Transcripts.GetStatus(recordingSession.Id).Recording;
        NotifyCompletedInput.IsChecked = settings.ShellNotifications.CommandCompletedEnabled;
        NotifyLongTaskInput.IsChecked = settings.ShellNotifications.LongTaskEnabled;
        NotifyReadyInput.IsChecked = settings.ShellNotifications.ReadyForInputEnabled;
        LongTaskSecondsInput.Text = settings.ShellNotifications.LongTaskSeconds.ToString(CultureInfo.InvariantCulture);
        var visual = appearance.Visual;
        SelectTaggedItem(ThemeInput, visual.Theme.ToString());
        SelectTaggedItem(MaterialInput, visual.Material.ToString());
        DesktopBackgroundInput.IsChecked = visual.DesktopBackgroundEnabled;
        AuroraEnabledInput.IsChecked = visual.AuroraEnabled;
        SelectTaggedItem(AuroraPresetInput, visual.AuroraPresetId);
        AuroraColorsInput.Text = string.Join(", ", visual.AuroraCustomColors);
        _previewBackgroundPath = visual.BackgroundImagePath;
        BackgroundPathText.Text = string.IsNullOrEmpty(_previewBackgroundPath) ? Localize.T("Text0126") : System.IO.Path.GetFileName(_previewBackgroundPath);
        BackgroundOverlayInput.Value = visual.BackgroundOverlayOpacity;
        BackgroundBlurInput.Value = visual.BackgroundBlurRadius;
        BackgroundBrightnessInput.Value = visual.BackgroundBrightness;
        BackgroundSaturationInput.Value = visual.BackgroundSaturation;
        SelectTaggedItem(BackgroundFitInput, visual.BackgroundFit.ToString());
        SelectTaggedItem(BackgroundPositionInput, visual.BackgroundPosition.ToString());
        TerminalOpacityInput.Value = visual.TerminalBackgroundOpacity;
        _settingsValid = true;
        SettingsValidationText.Text = string.Empty;
        foreach (var input in SettingsTextInputs) { input.ClearValue(BorderBrushProperty); input.ToolTip = null; }
        _initializing = false;
        ApplySettingsPreview(settings);
    }

    private Dictionary<string, string> CaptureSettingsEditor()
    {
        var result = SettingsTextInputs.ToDictionary(input => input.Name, input => input.Text);
        foreach (var input in SettingsCheckInputs) result[input.Name] = (input.IsChecked == true).ToString();
        foreach (var input in SettingsChoiceInputs) result[input.Name] = !string.IsNullOrEmpty(input.SelectedValuePath)
            ? input.SelectedValue as string ?? string.Empty
            : GetTag(input);
        foreach (var input in SettingsSliderInputs) result[input.Name] = input.Value.ToString(CultureInfo.InvariantCulture);
        result["BackgroundImagePath"] = _previewBackgroundPath;
        return result;
    }

    private void ApplyEditorValues(Dictionary<string, string> values)
    {
        _initializing = true;
        foreach (var (name, value) in values)
        {
            switch (FindName(name))
            {
                case TextBox text: text.Text = value; break;
                case CheckBox check when bool.TryParse(value, out var flag): check.IsChecked = flag; break;
                case ComboBox combo when !string.IsNullOrEmpty(combo.SelectedValuePath): combo.SelectedValue = value; break;
                case ComboBox combo: SelectTaggedItem(combo, value); break;
                case Slider slider when double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number): slider.Value = number; break;
            }
        }
        _previewBackgroundPath = values.GetValueOrDefault("BackgroundImagePath", _previewBackgroundPath);
        BackgroundPathText.Text = System.IO.Path.GetFileName(_previewBackgroundPath);
        _initializing = false;
    }

    private void RestoreSettingsEditor()
    {
        if (_host.Draft is null || _host.Draft.EditorValues.Count == 0) return;
        ApplyEditorValues(_host.Draft.EditorValues);
        PreviewDraft();
    }

    private bool PreviewDraft()
    {
        if (!_stageSixReady || _initializing || !_host.CanEdit) return false;
        _host.EnsureSettingsDraft();
        _host.Draft!.CommitRequested = false;
        _host.Draft.EditorValues = CaptureSettingsEditor();
        var preview = _host.Draft.Settings.DeepClone();
        var errors = new List<string>();
        foreach (var input in SettingsTextInputs) { input.ClearValue(BorderBrushProperty); input.ToolTip = null; }
        void Error(TextBox input, string message) { errors.Add(message); input.BorderBrush = Brushes.IndianRed; input.ToolTip = message; }
        double Number(TextBox input, double min, double max)
        {
            if (!double.TryParse(input.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) || !double.IsFinite(value) || value < min || value > max)
            {
                var label = input.Name switch
                {
                    "FontSizeInput" => Localize.T("Text0127"), "TerminalFontSizeInput" => Localize.T("Text0128"), "TerminalWeightInput" => Localize.T("Text0129"),
                    "TerminalLineHeightInput" => Localize.T("Text0130"), "TerminalSpacingInput" => Localize.T("Text0131"), _ => Localize.T("Text0132")
                };
                Error(input, Localize.F("Text0133", label, min, max)); return min;
            }
            return value;
        }
        preview.Appearance.UiFontFamily = UiFontInput.Text;
        preview.Appearance.UiFontSize = Number(FontSizeInput, 6, 96);
        preview.Appearance.TerminalFontFamily = TerminalFontInput.Text;
        preview.Appearance.TerminalFontSize = Number(TerminalFontSizeInput, 6, 96);
        var weight = Number(TerminalWeightInput, 100, 1000);
        if (weight != Math.Truncate(weight)) Error(TerminalWeightInput, Localize.T("Text0134"));
        preview.Appearance.TerminalFontWeight = (int)weight;
        preview.Appearance.TerminalLineHeight = Number(TerminalLineHeightInput, 0.75, 3);
        preview.Appearance.TerminalCharacterSpacing = Number(TerminalSpacingInput, -4, 16);
        preview.Appearance.TerminalLigatures = LigaturesInput.IsChecked == true;
        preview.Appearance.ShowTimestamps = TimestampsInput.IsChecked == true;
        preview.Appearance.ShowLogicalLineNumbers = LineNumbersInput.IsChecked == true;
        preview.Appearance.ShowCommandDuration = DurationInput.IsChecked == true;
        preview.Appearance.AccentColor = AccentInput.Text;
        preview.DefaultProfileId = DefaultProfileInput.SelectedValue as string;
        if (string.IsNullOrEmpty(preview.DefaultProfileId)) preview.DefaultProfileId = null;
        preview.Regional.Language = LanguageInput.SelectedValue as string ?? LocalizationDefaults.English;
        preview.Regional.TimeZoneId = TimeZoneInput.SelectedValue as string ?? TimeZoneInfo.Local.Id;
        preview.Workbench.TabPosition = Enum.Parse<TerminalTabPosition>(GetTag(TabPositionInput));
        preview.Workbench.ConfirmCloseRunning = ConfirmCloseInput.IsChecked == true;
        preview.Workbench.CommandPaletteHotkeyEnabled = HotkeyEnabledInput.IsChecked == true;
        preview.Workbench.CommandPaletteHotkey = HotkeyInput.Text;
        preview.Workbench.FocusModeHotkeyEnabled = FocusHotkeyEnabledInput.IsChecked == true;
        preview.Workbench.FocusModeHotkey = FocusHotkeyInput.Text;
        preview.ShellNotifications.CommandCompletedEnabled = NotifyCompletedInput.IsChecked == true;
        preview.ShellNotifications.LongTaskEnabled = NotifyLongTaskInput.IsChecked == true;
        preview.ShellNotifications.ReadyForInputEnabled = NotifyReadyInput.IsChecked == true;
        var longTask = Number(LongTaskSecondsInput, 1, 86400);
        if (longTask != Math.Truncate(longTask)) Error(LongTaskSecondsInput, Localize.T("Text0135"));
        preview.ShellNotifications.LongTaskSeconds = (int)longTask;
        if (!GlobalHotkeyService.Validate(preview.Workbench, out var hotkeyError)) Error(HotkeyInput, hotkeyError);
        var visual = preview.Appearance.Visual;
        visual.Theme = Enum.Parse<AppThemeMode>(GetTag(ThemeInput));
        if (_visuals.SupportsSystemBackdrop) visual.Material = Enum.Parse<WindowMaterialMode>(GetTag(MaterialInput));
        if (_visuals.SupportsDesktopBackground) visual.DesktopBackgroundEnabled = DesktopBackgroundInput.IsChecked == true;
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
        var validation = ConfigurationValidator.ValidateSettings(preview, _host.Catalog!.Profiles.Select(profile => profile.Id).ToArray());
        errors.AddRange(validation.Issues.Select(issue => issue.Message));
        _settingsValid = errors.Count == 0;
        SettingsValidationText.Text = string.Join("\n", errors.Distinct());
        if (_settingsValid) { _host.UpdateDraft(preview); ApplySettingsPreview(preview); }
        UpdateDraftControls();
        ScheduleDraftSave();
        return _settingsValid;
    }

    private void ApplySettingsPreview(AppSettings settings)
    {
        FontFamily = new FontFamily(settings.Appearance.UiFontFamily);
        FontSize = settings.Appearance.UiFontSize;
        ApplyTabPosition(settings.Workbench.TabPosition);
        _visuals.Apply(settings.Appearance);
        VisualCapabilityText.Text = _visuals.CapabilityText;
        UpdateFontPreviews();
    }

    private TerminalFontOptions EffectiveFont(TabViewModel tab)
    {
        var catalog = _host.ProfilesDraft?.Catalog ?? _host.Catalog;
        var profile = catalog?.Profiles.FirstOrDefault(profile => profile.Id == tab.Launch?.Request.ProfileId);
        return profile is null ? tab.Launch?.FontOptions ?? new() : TerminalFontOptions.Resolve((_host.Draft?.Settings ?? _host.FormalSettings)?.Appearance, profile);
    }

    private void UpdateFontPreviews()
    {
        if (!_stageSixReady) return;
        var appearance = (_host.Draft?.Settings ?? _host.FormalSettings)?.Appearance;
        FontPreview.AttachPreview(TerminalFontOptions.Resolve(appearance));
        FontPreview.ApplyVisualStyle(TerminalView.VisualStyle);
        _profileEditor.FontPreview.AttachPreview(TerminalFontOptions.Resolve(appearance, _profileEditor.PreviewProfile));
        _profileEditor.FontPreview.ApplyVisualStyle(TerminalView.VisualStyle);
        FontPreviewSource.Text = Localize.T("Text0136");
        if (_attachedTab?.Session is not null)
            TerminalView.SetFontBaseline(EffectiveFont(_attachedTab), TerminalView.HasUserZoom ? TerminalView.CurrentFontSize : null);
        UpdateAuxiliaryDisplay();
        UpdateDraftControls();
    }

    private void UpdateDraftControls()
    {
        if (!_stageSixReady) return;
        foreach (var input in SettingsTextInputs) input.IsEnabled = _host.CanEdit;
        foreach (var input in SettingsCheckInputs) input.IsEnabled = _host.CanEdit;
        foreach (var input in SettingsChoiceInputs) input.IsEnabled = _host.CanEdit;
        foreach (var input in SettingsSliderInputs) input.IsEnabled = _host.CanEdit;
        SaveDraftButton.IsEnabled = _host.Draft is not null && _settingsValid;
        CancelDraftButton.IsEnabled = _host.Draft is not null;
        SaveProfilesButton.IsEnabled = CancelProfilesButton.IsEnabled = _host.ProfilesDraft is not null && _host.CanEdit;
        MaterialInput.IsEnabled = _host.CanEdit && _visuals.SupportsSystemBackdrop;
        DesktopBackgroundInput.IsEnabled = _host.CanEdit && _visuals.SupportsDesktopBackground;
    }

    private sealed record MetadataRow(string Text, double Height);
    private void UpdateAuxiliaryDisplay()
    {
        if (!_stageSixReady) return;
        var appearance = (_host.Draft?.Settings ?? _host.FormalSettings)?.Appearance;
        var snapshot = TerminalView.CurrentSnapshot;
        var font = _attachedTab is null ? TerminalFontOptions.Resolve(appearance) : EffectiveFont(_attachedTab);
        if (LineMetadataItems.FontFamily.Source != font.FontFamily) LineMetadataItems.FontFamily = new FontFamily(font.FontFamily);
        LineMetadataItems.FontSize = TerminalView.CurrentFontSize;
        LineMetadataItems.FontWeight = System.Windows.FontWeight.FromOpenTypeWeight(Math.Min(font.FontWeight, 999));
        LineMetadataItems.ItemsSource = snapshot is null || appearance is null || (!appearance.ShowTimestamps && !appearance.ShowLogicalLineNumbers) ? null :
            snapshot.Lines.Select(line => new MetadataRow(snapshot.IsAlternateScreen ? "—" : line.LogicalLineNumber is null && line.FirstOutputAtUtc is null ? string.Empty :
                (appearance.ShowLogicalLineNumbers ? (line.LogicalLineNumber?.ToString(CultureInfo.InvariantCulture) ?? "—") + "  " : string.Empty) +
                (appearance.ShowTimestamps ? line.FirstOutputAtUtc is { } outputAt ? Localize.ToConfiguredTime(outputAt).ToString("HH:mm:ss.fff", Localize.Current.Culture) : "—" : string.Empty), TerminalView.LineHeight)).ToArray();
        CommandDurationText.Visibility = appearance?.ShowCommandDuration == true ? Visibility.Visible : Visibility.Collapsed;
        CommandDurationText.Text = Localize.T("Text0137");
        UpdateShellDisplay();
    }

    private void SettingsCategory_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_stageSixReady || e.Source != SettingsCategories) return;
        SettingsSectionTitle.Text = (SettingsCategories.SelectedItem as TabItem)?.Header as string ?? Localize.T("Text0039");
        ProfileSaveBar.Visibility = SettingsCategories.SelectedIndex == 2 ? Visibility.Visible : Visibility.Collapsed;
        UpdateFontPreviews();
        if (SettingsCategories.Template.FindName("PART_SelectedContentHost", SettingsCategories) is FrameworkElement content)
        {
            MotionPolicy.Show(content);
        }
    }

    private void RestoreDefaults_Click(object sender, RoutedEventArgs e)
    {
        if (!_host.CanEdit) return;
        var index = SettingsCategories.SelectedIndex;
        if (index > 2) { StatusText.Text = Localize.T("Text0138"); return; }
        if (MessageBox.Show(this, Localize.T("Text0139"), Localize.T("Text0140"), MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        RestoreCategoryDefaults(index);
    }

    private void RestoreCategoryDefaults(int index)
    {
        _host.EnsureSettingsDraft();
        var raw = CaptureSettingsEditor();
        var candidate = _host.Draft!.Settings.DeepClone();
        var defaults = ConfigurationDefaults.CreateSettings();
        if (index == 0) candidate.Appearance = defaults.Appearance;
        else if (index == 1)
        {
            candidate.Workbench.CommandPaletteHotkeyEnabled = defaults.Workbench.CommandPaletteHotkeyEnabled;
            candidate.Workbench.CommandPaletteHotkey = defaults.Workbench.CommandPaletteHotkey;
            candidate.Workbench.FocusModeHotkeyEnabled = defaults.Workbench.FocusModeHotkeyEnabled;
            candidate.Workbench.FocusModeHotkey = defaults.Workbench.FocusModeHotkey;
        }
        else
        {
            candidate.DefaultProfileId = _host.Catalog!.Profiles.Any(profile => profile.Id == "cmd") ? "cmd" : null;
            candidate.Workbench.TabPosition = defaults.Workbench.TabPosition;
            candidate.Workbench.ConfirmCloseRunning = defaults.Workbench.ConfirmCloseRunning;
            candidate.ShellNotifications = defaults.ShellNotifications;
        }
        _host.UpdateDraft(candidate);
        LoadSettingsControls(candidate);
        var refreshed = CaptureSettingsEditor();
        string[] shortcutNames = ["HotkeyEnabledInput", "HotkeyInput", "FocusHotkeyEnabledInput", "FocusHotkeyInput"];
        string[] generalNames = ["DefaultProfileInput", "TabPositionInput", "ConfirmCloseInput", "NotifyCompletedInput", "NotifyLongTaskInput", "NotifyReadyInput", "LongTaskSecondsInput"];
        foreach (var (key, value) in raw)
        {
            var group = shortcutNames.Contains(key) ? 1 : generalNames.Contains(key) ? 2 : 0;
            if (group != index) refreshed[key] = value;
        }
        ApplyEditorValues(refreshed);
        PreviewDraft();
    }

    private async void SaveProfiles_Click(object sender, RoutedEventArgs e) => await SaveProfilesWithLockAsync();
    private async void CancelProfiles_Click(object sender, RoutedEventArgs e) => await _profileEditor.CancelAsync();
    private async void RetryCleanup_Click(object sender, RoutedEventArgs e) => ShowResult(await _host.RetryDraftCleanupAsync(), Localize.T("Text0141"));
    private async void ReloadFormal_Click(object sender, RoutedEventArgs e)
    {
        if (!ShowResult(await _host.ReloadFormalAsync(), Localize.T("Text0142"))) return;
        ProfilesList.ItemsSource = _host.Catalog?.Profiles;
        LoadSettingsControls(_host.Draft?.Settings ?? _host.FormalSettings);
        RestoreSettingsEditor();
        _profileEditor.Reload();
        if (_host.FormalSettings is not null) _hotkey.Configure(_host.FormalSettings.Workbench, out _);
        ConfigureWindowFallback();
        HotkeyStatusText.Text = _hotkey.StatusText;
        UpdateDraftControls();
    }
    private void ConfigureWindowFallback()
    {
        if (_host.FormalSettings is null) return;
        _commandKeys.Configure(_host.FormalSettings.Workbench, _hotkey.IsRegistered, _hotkey.IsFocusRegistered);
        CommandPaletteButton.ToolTip = Localize.T("Text0080") + (_host.FormalSettings.Workbench.CommandPaletteHotkeyEnabled ? "  " + _host.FormalSettings.Workbench.CommandPaletteHotkey : string.Empty);
        TerminalShortcutHint.Text = Localize.T("Text0143");
    }

    private async Task<bool> SaveDraftAsync()
    {
        if (_savingSettings) return false;
        _savingSettings = true;
        SettingsPanel.IsEnabled = false;
        try { return await SaveDraftCoreAsync(); }
        finally { _savingSettings = false; SettingsPanel.IsEnabled = true; }
    }

    private async Task<bool> SaveProfilesWithLockAsync()
    {
        if (_savingSettings) return false;
        _savingSettings = true;
        SettingsPanel.IsEnabled = false;
        try { return await _profileEditor.SaveAsync(); }
        finally { _savingSettings = false; SettingsPanel.IsEnabled = true; }
    }

    private void DirectoryInput_TextChanged(object sender, TextChangedEventArgs e) { if (_stageSixReady && !_initializing) _directoryExplicit = true; }
    private void UseProfileDirectory_Click(object sender, RoutedEventArgs e) { _directoryExplicit = false; StatusText.Text = Localize.T("Text0144"); }
}
