using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace ArkTerminal.Infrastructure;

public sealed class WindowCommandKeyService : IDisposable
{
    private const int WmKeyDown = 0x0100;
    private const int WmSysKeyDown = 0x0104;
    private HwndSource? _source;
    private long _lastFocusInvocation = -250;
    private ArkTerminal.Core.Configuration.WorkbenchSettings _settings = new();
    private bool _paletteRegistered;
    private bool _focusRegistered;

    public void Configure(ArkTerminal.Core.Configuration.WorkbenchSettings settings, bool paletteRegistered, bool focusRegistered)
    {
        _settings = settings.DeepClone();
        _paletteRegistered = paletteRegistered;
        _focusRegistered = focusRegistered;
    }

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
        var win = (GetKeyState(0x5B) & 0x8000) != 0 || (GetKeyState(0x5C) & 0x8000) != 0;
        var modifiers = (control ? 2u : 0u) | (shift ? 4u : 0u) | (alt ? 1u : 0u) | (win ? 8u : 0u);
        GlobalHotkeyService.TryParse(_settings.CommandPaletteHotkey, out var pm, out var pk);
        GlobalHotkeyService.TryParse(_settings.FocusModeHotkey, out var fm, out var fk);
        if (_settings.CommandPaletteHotkeyEnabled && !_paletteRegistered && modifiers == pm && (uint)wParam.ToInt32() == pk)
        {
            CommandPaletteRequested?.Invoke();
        }
        else if (_settings.FocusModeHotkeyEnabled && !_focusRegistered && modifiers == fm && (uint)wParam.ToInt32() == fk)
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
