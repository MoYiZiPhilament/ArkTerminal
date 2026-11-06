using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Shell;
using ArkTerminal.Core.Configuration;

namespace ArkTerminal.Visuals;

public sealed record WindowMaterialCapabilities(
    bool CompositionEnabled,
    bool SystemBackdropSupported,
    bool TransparencySupported,
    string Summary)
{
    public bool SystemBackdropApiAvailable { get; init; }
}

public sealed record WindowMaterialResult(
    WindowMaterialMode AppliedMaterial,
    bool DesktopBackgroundApplied,
    string Status,
    bool Success);

public sealed class WindowMaterialService
{
    private const int DwmaNcRenderingPolicy = 2;
    private const int DwmncrpDisabled = 1;
    private const int DwmaUseImmersiveDarkMode = 20;
    private const int DwmaUseImmersiveDarkModeBefore20H1 = 19;
    private const int DwmaWindowCornerPreference = 33;
    private const int DwmwcpRound = 2;
    private const int DwmaSystemBackdropType = 38;
    private const int DwmaCloaked = 14;
    private const int DwmsbtNone = 1;
    private const int DwmsbtMainWindow = 2;
    private const int DwmsbtTransientWindow = 3;
    private IntPtr _hwnd;
    private Window? _window;
    private bool _customCaption;

    public WindowMaterialCapabilities Capabilities { get; private set; } = Probe();
    internal bool UsesNativeCorners { get; private set; }

    public void Attach(HwndSource source)
    {
        _hwnd = source.Handle;
        _window = source.RootVisual as Window;
        _customCaption = _window is not null && WindowChrome.GetWindowChrome(_window)?.UseAeroCaptionButtons == false;
        RefreshCapabilities();
    }

    public void RefreshCapabilities() => Capabilities = Probe();

    public WindowMaterialResult Apply(WindowMaterialMode requested, bool desktopBackground, bool dark)
    {
        if (_hwnd == IntPtr.Zero)
        {
            return new(WindowMaterialMode.Off, false, Localize.T("Text0433"), false);
        }
        SetDarkMode(dark);
        var cornerResult = ApplyWindowCorners();
        if (!Capabilities.CompositionEnabled)
        {
            ResetEffects();
            return new(WindowMaterialMode.Off, false, Localize.T("Text0434"), false);
        }
        if (cornerResult != 0)
        {
            ResetEffects();
            return new(WindowMaterialMode.Off, false, Localize.F("Text0435", cornerResult), false);
        }
        if (_customCaption)
        {
            // An extended DWM frame otherwise masks the area occupied by our caption buttons.
            var policy = DwmncrpDisabled;
            var result = DwmSetWindowAttribute(_hwnd, DwmaNcRenderingPolicy, ref policy, sizeof(int));
            if (result != 0)
            {
                ResetEffects();
                return new(WindowMaterialMode.Off, false, Localize.F("Text0436", result), false);
            }
        }
        if (desktopBackground && !Capabilities.TransparencySupported ||
            (requested is WindowMaterialMode.Mica or WindowMaterialMode.Acrylic) && !Capabilities.SystemBackdropSupported)
        {
            ResetEffects();
            return new(WindowMaterialMode.Off, false, Localize.F("Text0437", Capabilities.Summary), false);
        }
        var material = desktopBackground ? WindowMaterialMode.Acrylic : requested == WindowMaterialMode.Auto
            ? Capabilities.SystemBackdropSupported ? WindowMaterialMode.Mica : WindowMaterialMode.Off
            : requested;
        if (material is WindowMaterialMode.Mica or WindowMaterialMode.Acrylic)
        {
            if (!Capabilities.SystemBackdropSupported)
            {
                return new(WindowMaterialMode.Off, false, Localize.T("Text0438"), false);
            }
            var type = material == WindowMaterialMode.Mica ? DwmsbtMainWindow : DwmsbtTransientWindow;
            var frameResult = ExtendFrame(true);
            var result = SetSystemBackdrop(type);
            if (result == 0 && frameResult == 0)
            {
                // Success describes the API request, not independent visual verification.
                var status = desktopBackground
                    ? Localize.T("Text0439")
                    : Localize.F("Text0440", material);
                return new(material, desktopBackground, status + Localize.T("Text0441"), true);
            }
            ResetEffects();
            return new(WindowMaterialMode.Off, false, Localize.F("Text0442", material, result, frameResult), false);
        }
        var backdropResult = Capabilities.SystemBackdropApiAvailable ? SetSystemBackdrop(DwmsbtNone) : 0;
        var clearFrameResult = ExtendFrame(false);
        if (backdropResult != 0 || clearFrameResult != 0)
        {
            return new(WindowMaterialMode.Off, false, Localize.F("Text0443", backdropResult, clearFrameResult), false);
        }
        return new(WindowMaterialMode.Off, false, Localize.T("Text0444"), true);
    }

    public bool IsCloaked()
    {
        if (_hwnd == IntPtr.Zero)
        {
            return false;
        }
        return DwmGetWindowAttribute(_hwnd, DwmaCloaked, out var value, sizeof(int)) == 0 && value != 0;
    }

    public void ResetEffects()
    {
        if (_hwnd == IntPtr.Zero)
        {
            return;
        }
        if (Capabilities.SystemBackdropApiAvailable)
        {
            SetSystemBackdrop(DwmsbtNone);
        }
        ExtendFrame(false);
    }

    private int SetSystemBackdrop(int type)
    {
        if (!Capabilities.SystemBackdropApiAvailable)
        {
            return unchecked((int)0x80070032);
        }
        return DwmSetWindowAttribute(_hwnd, DwmaSystemBackdropType, ref type, sizeof(int));
    }

    private int ApplyWindowCorners()
    {
        UsesNativeCorners = false;
        if (!_customCaption || _window is null || WindowChrome.GetWindowChrome(_window) is not { } chrome)
        {
            return 0;
        }
        var result = 0;
        if (Capabilities.CompositionEnabled && OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
        {
            var preference = DwmwcpRound;
            result = DwmSetWindowAttribute(_hwnd, DwmaWindowCornerPreference, ref preference, sizeof(int));
            UsesNativeCorners = result == 0;
        }
        // A nonzero glass frame makes WindowChrome clear its HRGN and leaves the outer silhouette to DWM.
        chrome.CornerRadius = new CornerRadius(UsesNativeCorners ? 0 : 24);
        chrome.GlassFrameThickness = new Thickness(UsesNativeCorners ? -1 : 0);
        return result;
    }

    private int ExtendFrame(bool enabled)
    {
        var margins = new Margins(enabled ? -1 : 0);
        return DwmExtendFrameIntoClientArea(_hwnd, ref margins);
    }

    private void SetDarkMode(bool dark)
    {
        var value = dark ? 1 : 0;
        if (DwmSetWindowAttribute(_hwnd, DwmaUseImmersiveDarkMode, ref value, sizeof(int)) != 0)
        {
            DwmSetWindowAttribute(_hwnd, DwmaUseImmersiveDarkModeBefore20H1, ref value, sizeof(int));
        }
    }

    private static WindowMaterialCapabilities Probe()
    {
        var composition = DwmIsCompositionEnabled(out var enabled) == 0 && enabled;
        var build = Environment.OSVersion.Version.Build;
        var backdropApi = build >= 22621;
        var backdrop = composition && backdropApi;
        var summary = !composition
            ? Localize.T("Text0445")
            : backdrop
                ? Localize.T("Text0446")
                : Localize.T("Text0447");
        return new(composition, backdrop, backdrop, summary) { SystemBackdropApiAvailable = backdropApi };
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Margins(int value)
    {
        public int Left = value;
        public int Right = value;
        public int Top = value;
        public int Bottom = value;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmIsCompositionEnabled(out bool enabled);
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out int value, int size);
    [DllImport("dwmapi.dll")]
    private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref Margins margins);
}
