using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace ArkTerminal.Infrastructure;

public sealed class WindowCommandKeyService : IDisposable
{
    private const int WmKeyDown = 0x0100;
    private const int WmSysKeyDown = 0x0104;
    private const int VkSpace = 0x20;
    private const int VkF11 = 0x7A;
    private HwndSource? _source;
    private long _lastFocusInvocation = -250;

    public event Action? CommandPaletteRequested;
    public event Action? FocusModeRequested;

    public void Attach(HwndSource source)
    {
        _source = source;
        _source.AddHook(OnWindowMessage);
    }

    public void Dispose()
    {
        _source?.RemoveHook(OnWindowMessage);
        _source = null;
    }

    private IntPtr OnWindowMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message is not (WmKeyDown or WmSysKeyDown) || (lParam.ToInt64() & 0x40000000) != 0)
        {
            return IntPtr.Zero;
        }
        var control = (GetKeyState(0x11) & 0x8000) != 0;
        var shift = (GetKeyState(0x10) & 0x8000) != 0;
        var alt = (GetKeyState(0x12) & 0x8000) != 0;
        if (!control || !shift || alt)
        {
            return IntPtr.Zero;
        }
        if (wParam.ToInt32() == VkSpace)
        {
            CommandPaletteRequested?.Invoke();
        }
        else if (wParam.ToInt32() == VkF11)
        {
            RequestFocusMode();
        }
        else
        {
            return IntPtr.Zero;
        }
        handled = true;
        return IntPtr.Zero;
    }

    private void RequestFocusMode()
    {
        var now = Environment.TickCount64;
        if (now - _lastFocusInvocation < 250)
        {
            return;
        }
        _lastFocusInvocation = now;
        FocusModeRequested?.Invoke();
    }

    [DllImport("user32.dll")]
    private static extern short GetKeyState(int virtualKey);
}
