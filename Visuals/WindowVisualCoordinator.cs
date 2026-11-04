using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using ArkTerminal.Core.Configuration;
using ArkTerminal.Core.Data;
using ArkTerminal.Rendering;

namespace ArkTerminal.Visuals;

public sealed record VisualDiagnosticsSnapshot(
    bool PerformanceModeEnabled,
    WindowMaterialMode AppliedMaterial,
    bool DesktopBackgroundApplied,
    bool AuroraAnimating,
    long AuroraFrameCount);

public sealed class WindowVisualCoordinator : IDisposable
{
    private readonly Window _window;
    private readonly DirectWriteTerminalControl _terminal;
    private readonly Image _backgroundImage;
    private readonly Rectangle _backgroundOverlay;
    private readonly Rectangle _readabilityOverlay;
    private readonly AuroraBackgroundControl _aurora;
    private readonly ThemeService _theme = new();
    private readonly WindowMaterialService _material = new();
    private readonly BackgroundImageService _backgrounds;
    private readonly DispatcherTimer _materialRefresh = new() { Interval = TimeSpan.FromMilliseconds(120) };
    private AppearanceSettings _appearance = new();
    private HwndSource? _source;
    private CancellationTokenSource? _backgroundCancellation;
    private WindowMaterialResult _materialResult = new(WindowMaterialMode.Off, false, "窗口材质尚未应用。", true);
    private string _backgroundKey = string.Empty;
    private string _paletteKey = string.Empty;
    private string _materialKey = string.Empty;
    private AppThemeMode? _appliedTheme;
    private string _accent = string.Empty;
    private bool _focused = true;
    private bool _visible = true;
    private bool _suspended;
    private bool _disposed;

    public WindowVisualCoordinator(
        Window window,
        DirectWriteTerminalControl terminal,
        Image backgroundImage,
        Rectangle backgroundOverlay,
        Rectangle readabilityOverlay,
        AuroraBackgroundControl aurora,
        IAppDataPaths paths)
    {
        _window = window;
        _terminal = terminal;
        _backgroundImage = backgroundImage;
        _backgroundOverlay = backgroundOverlay;
        _readabilityOverlay = readabilityOverlay;
        _aurora = aurora;
        _backgrounds = new BackgroundImageService(paths);
        _theme.SystemThemeChanged += OnSystemThemeChanged;
        _materialRefresh.Tick += (_, _) =>
        {
            _materialRefresh.Stop();
            _material.RefreshCapabilities();
            _materialKey = string.Empty;
            ApplyMaterial();
            PresentationChanged?.Invoke();
        };
        SystemParameters.StaticPropertyChanged += OnSystemParameterChanged;
        _window.Activated += OnActivated;
        _window.Deactivated += OnDeactivated;
        _window.StateChanged += OnStateChanged;
        _window.IsVisibleChanged += OnVisibilityChanged;
        _window.DpiChanged += OnDpiChanged;
    }

    public event Action? PresentationChanged;
    public bool PerformanceModeEnabled { get; private set; }
    public bool IsPresentationSuspended => _window.WindowState == WindowState.Minimized || !_window.IsVisible || _material.IsCloaked();
    public bool SupportsSystemBackdrop => _material.Capabilities.SystemBackdropSupported;
    public bool SupportsDesktopBackground => _material.Capabilities.TransparencySupported;
    internal bool UsesNativeWindowCorners => _material.UsesNativeCorners;
    public bool AnimationsEnabled => !PerformanceModeEnabled && SystemParameters.ClientAreaAnimation;
    public string CapabilityText => $"{_material.Capabilities.Summary}。{_materialResult.Status}" +
        (string.IsNullOrEmpty(_appearance.Visual.BackgroundImagePath) ? string.Empty : " 背景图另含固定可读性遮罩（浅色 65% / 深色 45%）。");
    public VisualDiagnosticsSnapshot Diagnostics => new(
        PerformanceModeEnabled,
        _materialResult.AppliedMaterial,
        _materialResult.DesktopBackgroundApplied,
        _aurora.IsAnimating,
        _aurora.FrameCount);

    public void Attach(HwndSource source)
    {
        _source = source;
        _source.AddHook(OnWindowMessage);
        _theme.Attach(source);
        _material.Attach(source);
        _materialKey = string.Empty;
        ApplyMaterial();
        UpdateActivity();
    }

    public void Apply(AppearanceSettings appearance)
    {
        if (_disposed)
        {
            return;
        }
        _appearance = appearance.DeepClone();
        var visual = _appearance.Visual;
        if (_appliedTheme != visual.Theme || !string.Equals(_accent, _appearance.AccentColor, StringComparison.Ordinal))
        {
            _theme.Apply(visual.Theme, _appearance.AccentColor);
            _appliedTheme = visual.Theme;
            _accent = _appearance.AccentColor;
            _materialKey = string.Empty;
        }
        _backgroundOverlay.Opacity = visual.BackgroundOverlayOpacity;
        _readabilityOverlay.Opacity = string.IsNullOrEmpty(visual.BackgroundImagePath) ? 0 : _theme.IsDark ? 0.45 : 0.65;
        _aurora.SetTheme(_theme.IsDark);
        ApplyImageLayout(visual);
        ApplyMaterial();
        _aurora.SetState(visual.AuroraEnabled, _focused, _visible && !_suspended, !SystemParameters.ClientAreaAnimation, PerformanceModeEnabled);
        var paletteKey = string.Join(',', AuroraPresetCatalog.Resolve(visual).Select(color => color.ToString()));
        if (!string.Equals(_paletteKey, paletteKey, StringComparison.Ordinal))
        {
            _paletteKey = paletteKey;
            _ = _aurora.SetPaletteAsync(AuroraPresetCatalog.Resolve(visual));
        }
        var backgroundKey = $"{visual.BackgroundImagePath}|{visual.BackgroundBrightness:R}|{visual.BackgroundSaturation:R}|{(PerformanceModeEnabled ? 0 : visual.BackgroundBlurRadius):R}";
        if (!string.Equals(_backgroundKey, backgroundKey, StringComparison.Ordinal))
        {
            _backgroundKey = backgroundKey;
            _ = LoadBackgroundAsync(visual);
        }
        PresentationChanged?.Invoke();
    }

    public void TogglePerformanceMode()
    {
        PerformanceModeEnabled = !PerformanceModeEnabled;
        _materialKey = string.Empty;
        _backgroundKey = string.Empty;
        Apply(_appearance);
    }

    public string? ResolveBackgroundPath(string resourcePath) => _backgrounds.ResolvePath(resourcePath);

    public Task<BackgroundImportResult> ImportBackgroundAsync(string sourcePath, CancellationToken cancellationToken) =>
        _backgrounds.ImportAsync(sourcePath, cancellationToken);

    public Task DeleteBackgroundAsync(string resourcePath, IEnumerable<string?> retainedPaths, CancellationToken cancellationToken) =>
        _backgrounds.DeleteOwnedAsync(resourcePath, retainedPaths, cancellationToken);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        _materialRefresh.Stop();
        _source?.RemoveHook(OnWindowMessage);
        _window.Activated -= OnActivated;
        _window.Deactivated -= OnDeactivated;
        _window.StateChanged -= OnStateChanged;
        _window.IsVisibleChanged -= OnVisibilityChanged;
        _window.DpiChanged -= OnDpiChanged;
        _backgroundCancellation?.Cancel();
        _backgroundCancellation?.Dispose();
        _aurora.Dispose();
        _theme.Dispose();
        _material.ResetEffects();
        SystemParameters.StaticPropertyChanged -= OnSystemParameterChanged;
    }

    private void RefreshMaterial()
    {
        if (!_disposed && _source is not null)
        {
            _materialRefresh.Stop();
            _materialRefresh.Start();
        }
    }

    private IntPtr OnWindowMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == 0x031e) // WM_DWMCOMPOSITIONCHANGED
        {
            RefreshMaterial();
        }
        return IntPtr.Zero;
    }

    private void OnActivated(object? sender, EventArgs e) { _focused = true; UpdateActivity(); }
    private void OnDeactivated(object? sender, EventArgs e) { _focused = false; UpdateActivity(); }
    private void OnVisibilityChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        _visible = _window.IsVisible;
        UpdateActivity();
    }
    private void OnStateChanged(object? sender, EventArgs e)
    {
        UpdateActivity();
        if (_window.WindowState != WindowState.Minimized)
        {
            RefreshMaterial();
        }
    }
    private void OnDpiChanged(object sender, DpiChangedEventArgs e) => RefreshMaterial();

    private void ApplyMaterial()
    {
        var visual = _appearance.Visual;
        var key = $"{visual.Material}|{visual.DesktopBackgroundEnabled}|{PerformanceModeEnabled}|{_theme.IsDark}";
        if (string.Equals(_materialKey, key, StringComparison.Ordinal))
        {
            ApplyTerminalStyle();
            return;
        }
        _materialKey = key;
        var transparentWindow = _source is not null && !PerformanceModeEnabled &&
            (visual.DesktopBackgroundEnabled && SupportsDesktopBackground ||
             visual.Material != WindowMaterialMode.Off && SupportsSystemBackdrop);
        // WPF's composition surface must be transparent before extending the DWM frame.
        ApplyWindowBackground(transparentWindow);
        if (_source is not null)
        {
            _materialResult = PerformanceModeEnabled
                ? _material.Apply(WindowMaterialMode.Off, false, _theme.IsDark)
                : _material.Apply(visual.Material, visual.DesktopBackgroundEnabled, _theme.IsDark);
            transparentWindow = _materialResult.DesktopBackgroundApplied || _materialResult.AppliedMaterial is WindowMaterialMode.Mica or WindowMaterialMode.Acrylic;
        }
        else
        {
            _materialResult = new(WindowMaterialMode.Off, false, "窗口初始化后将探测并应用系统材质。", true);
        }
        ApplyWindowBackground(transparentWindow);
        ApplyTerminalStyle();
    }

    private void ApplyWindowBackground(bool transparentWindow)
    {
        _window.Background = transparentWindow ? Brushes.Transparent : FindBrush("Theme.WindowBackground");
        if (_source?.CompositionTarget is HwndTarget target)
        {
            target.BackgroundColor = transparentWindow ? Colors.Transparent : _theme.GetColor("Theme.TerminalBackground");
        }
    }

    private void ApplyTerminalStyle()
    {
        var opacity = PerformanceModeEnabled || _source is not null && _appearance.Visual.DesktopBackgroundEnabled && !_materialResult.DesktopBackgroundApplied
            ? 1
            : _appearance.Visual.TerminalBackgroundOpacity;
        _terminal.ApplyVisualStyle(new TerminalVisualStyle(
            _theme.GetColor("Theme.TerminalBackground"),
            _theme.GetColor("Theme.TerminalForeground"),
            _theme.GetColor("Theme.TerminalSelection"),
            _theme.GetColor("Theme.TerminalSearch"),
            _theme.GetColor("Theme.TerminalCursor"),
            opacity));
    }

    private async Task LoadBackgroundAsync(VisualSettings visual)
    {
        _backgroundCancellation?.Cancel();
        _backgroundCancellation?.Dispose();
        var cancellation = new CancellationTokenSource();
        _backgroundCancellation = cancellation;
        if (string.IsNullOrEmpty(visual.BackgroundImagePath))
        {
            _backgroundImage.Source = null;
            _backgroundImage.Visibility = Visibility.Collapsed;
            return;
        }
        try
        {
            var result = await _backgrounds.LoadProcessedAsync(
                visual.BackgroundImagePath,
                visual.BackgroundBrightness,
                visual.BackgroundSaturation,
                PerformanceModeEnabled ? 0 : visual.BackgroundBlurRadius,
                cancellation.Token);
            if (cancellation.IsCancellationRequested || !string.Equals(_appearance.Visual.BackgroundImagePath, visual.BackgroundImagePath, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
            _backgroundImage.Source = result.Image;
            _backgroundImage.Visibility = result.Image is null ? Visibility.Collapsed : Visibility.Visible;
            if (result.Error is not null)
            {
                _materialResult = _materialResult with { Status = result.Error };
                PresentationChanged?.Invoke();
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void ApplyImageLayout(VisualSettings visual)
    {
        _backgroundImage.Stretch = visual.BackgroundFit switch
        {
            BackgroundImageFit.Uniform => Stretch.Uniform,
            BackgroundImageFit.UniformToFill => Stretch.UniformToFill,
            BackgroundImageFit.Fill => Stretch.Fill,
            _ => Stretch.None
        };
        _backgroundImage.HorizontalAlignment = visual.BackgroundPosition switch
        {
            BackgroundImagePosition.TopLeft or BackgroundImagePosition.Left or BackgroundImagePosition.BottomLeft => HorizontalAlignment.Left,
            BackgroundImagePosition.TopRight or BackgroundImagePosition.Right or BackgroundImagePosition.BottomRight => HorizontalAlignment.Right,
            _ => HorizontalAlignment.Center
        };
        _backgroundImage.VerticalAlignment = visual.BackgroundPosition switch
        {
            BackgroundImagePosition.TopLeft or BackgroundImagePosition.Top or BackgroundImagePosition.TopRight => VerticalAlignment.Top,
            BackgroundImagePosition.BottomLeft or BackgroundImagePosition.Bottom or BackgroundImagePosition.BottomRight => VerticalAlignment.Bottom,
            _ => VerticalAlignment.Center
        };
    }

    private void UpdateActivity()
    {
        var suspended = _window.WindowState == WindowState.Minimized || !_window.IsVisible || _material.IsCloaked();
        if (_suspended != suspended)
        {
            _suspended = suspended;
            _terminal.SetActive(!suspended);
        }
        _aurora.SetState(
            _appearance.Visual.AuroraEnabled,
            _focused,
            _visible && !suspended,
            !SystemParameters.ClientAreaAnimation,
            PerformanceModeEnabled);
    }

    private void OnSystemThemeChanged()
    {
        if (_appearance.Visual.Theme == AppThemeMode.System)
        {
            _appliedTheme = null;
            _materialKey = string.Empty;
            Apply(_appearance);
        }
    }

    private void OnSystemParameterChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SystemParameters.ClientAreaAnimation))
        {
            UpdateActivity();
            PresentationChanged?.Invoke();
        }
    }

    private static Brush FindBrush(string key) => (Brush)Application.Current.Resources[key];
}
