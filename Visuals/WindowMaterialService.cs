using System.Runtime.InteropServices;
using System.Windows.Interop;
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
    private const int DwmaUseImmersiveDarkMode = 20;
    private const int DwmaUseImmersiveDarkModeBefore20H1 = 19;
    private const int DwmaSystemBackdropType = 38;
    private const int DwmaCloaked = 14;
    private const int DwmsbtNone = 1;
    private const int DwmsbtMainWindow = 2;
    private const int DwmsbtTransientWindow = 3;
    private IntPtr _hwnd;

    public WindowMaterialCapabilities Capabilities { get; private set; } = Probe();

    public void Attach(HwndSource source)
    {
        _hwnd = source.Handle;
        RefreshCapabilities();
    }

    public void RefreshCapabilities() => Capabilities = Probe();

    public WindowMaterialResult Apply(WindowMaterialMode requested, bool desktopBackground, bool dark)
    {
        if (_hwnd == IntPtr.Zero)
        {
            return new(WindowMaterialMode.Off, false, "窗口句柄尚未就绪。", false);
        }
        SetDarkMode(dark);
        if (!Capabilities.CompositionEnabled)
        {
            ResetEffects();
            return new(WindowMaterialMode.Off, false, "DWM 合成不可用，已使用不透明背景。", false);
        }
        if (desktopBackground && !Capabilities.TransparencySupported ||
            (requested is WindowMaterialMode.Mica or WindowMaterialMode.Acrylic) && !Capabilities.SystemBackdropSupported)
        {
            ResetEffects();
            return new(WindowMaterialMode.Off, false, $"{Capabilities.Summary}，已使用不透明背景；保存的材质偏好不变。", false);
        }
        var material = desktopBackground ? WindowMaterialMode.Acrylic : requested == WindowMaterialMode.Auto
            ? Capabilities.SystemBackdropSupported ? WindowMaterialMode.Mica : WindowMaterialMode.Off
            : requested;
        if (material is WindowMaterialMode.Mica or WindowMaterialMode.Acrylic)
        {
            if (!Capabilities.SystemBackdropSupported)
            {
                return new(WindowMaterialMode.Off, false, "当前 Windows 不支持公开的系统 Backdrop API，已回退不透明。", false);
            }
            var type = material == WindowMaterialMode.Mica ? DwmsbtMainWindow : DwmsbtTransientWindow;
            var frameResult = ExtendFrame(true);
            var result = SetSystemBackdrop(type);
            if (result == 0 && frameResult == 0)
            {
                // Success describes the API request, not independent visual verification.
                var status = desktopBackground
                    ? "系统已接受 Acrylic 桌面后景请求；采样与模糊由系统决定。"
                    : $"系统已接受 {material} 材质请求。";
                return new(material, desktopBackground, status + " API 成功不代表移动后的视觉链路已通过验收。", true);
            }
            ResetEffects();
            return new(WindowMaterialMode.Off, false, $"{material} 设置失败（backdrop 0x{result:X8}，frame 0x{frameResult:X8}），已回退不透明。", false);
        }
        var backdropResult = Capabilities.SystemBackdropApiAvailable ? SetSystemBackdrop(DwmsbtNone) : 0;
        var clearFrameResult = ExtendFrame(false);
        if (backdropResult != 0 || clearFrameResult != 0)
        {
            return new(WindowMaterialMode.Off, false, $"系统材质清除失败（backdrop 0x{backdropResult:X8}，frame 0x{clearFrameResult:X8}）。", false);
        }
        return new(WindowMaterialMode.Off, false, "窗口材质已关闭，使用应用内背景。", true);
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
            ? "DWM 合成不可用"
            : backdrop
                ? "公开系统 Backdrop API 可用；实际视觉效果仍需在对应平台验收"
                : "无可用的公开系统 Mica/Acrylic Backdrop，真实后景已回退不透明";
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
