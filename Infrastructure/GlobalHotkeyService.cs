using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Interop;

namespace ArkTerminal.Infrastructure;

public sealed class GlobalHotkeyService : IDisposable
{
    private const int HotkeyId = 0x4152;
    private const int FocusHotkeyId = 0x4153;
    private const int ContextHotkeyId = 0x4154;
    private const int WmHotkey = 0x0312;
    private HwndSource? _source;
    private bool _registered;
    private bool _focusRegistered;
    private bool _contextRegistered;
    private bool _disposed;
    private ArkTerminal.Core.Configuration.WorkbenchSettings _settings = new();
    public event Action? Invoked;
    public event Action? FocusInvoked;
    public event Action? ContextInvoked;
    public string StatusText { get; private set; } = string.Empty;
    public void RefreshLocalization() { if (!string.IsNullOrEmpty(StatusText)) StatusText = Describe(); }
    public bool IsRegistered => _registered;
    public bool IsFocusRegistered => _focusRegistered;
    public bool IsContextRegistered => _contextRegistered;
    public void Attach(HwndSource source)
    {
        _source = source; source.AddHook(OnWindowMessage);
        _contextRegistered = RegisterHotKey(source.Handle, ContextHotkeyId, ModControl | ModShift | ModNoRepeat, 0x78);
    }

    public static bool Validate(ArkTerminal.Core.Configuration.WorkbenchSettings settings, out string error)
    {
        error = string.Empty;
        foreach (var gesture in new[] { settings.CommandPaletteHotkey, settings.FocusModeHotkey })
        {
            if (!TryParse(gesture, out var modifiers, out var key)) { error = Localize.T("Text0401"); return false; }
            if (IsReserved(modifiers, key)) { error = Localize.T("Text0402"); return false; }
        }
        TryParse(settings.CommandPaletteHotkey, out var pm, out var pk);
        TryParse(settings.FocusModeHotkey, out var fm, out var fk);
        if (settings.CommandPaletteHotkeyEnabled && settings.FocusModeHotkeyEnabled && pm == fm && pk == fk)
        { error = Localize.T("Text0403"); return false; }
        return true;
    }

    public bool Configure(bool enabled, string gesture, out string error)
    {
        var candidate = _settings.DeepClone();
        candidate.CommandPaletteHotkeyEnabled = enabled;
        candidate.CommandPaletteHotkey = gesture;
        return Configure(candidate, out error);
    }

    public bool Configure(ArkTerminal.Core.Configuration.WorkbenchSettings settings, out string error)
    {
        if (_disposed || _source is null) { error = Localize.T("Text0404"); return false; }
        if (!Validate(settings, out error)) return false;
        if (_settings.CommandPaletteHotkeyEnabled == settings.CommandPaletteHotkeyEnabled && _settings.CommandPaletteHotkey == settings.CommandPaletteHotkey &&
            _settings.FocusModeHotkeyEnabled == settings.FocusModeHotkeyEnabled && _settings.FocusModeHotkey == settings.FocusModeHotkey &&
            (_registered || !settings.CommandPaletteHotkeyEnabled) && (_focusRegistered || !settings.FocusModeHotkeyEnabled)) return true;
        var previous = _settings.DeepClone();
        Unregister();
        if (!Register(settings, out error))
        {
            Unregister();
            var restored = Register(previous, out _);
            StatusText = Localize.T("Text0405") + error + (restored ? Localize.T("Text0406") : Localize.T("Text0407")) + Describe();
            return false;
        }
        _settings = settings.DeepClone();
        StatusText = Describe();
        return true;
    }

    private bool Register(ArkTerminal.Core.Configuration.WorkbenchSettings settings, out string error)
    {
        error = string.Empty;
        if (settings.CommandPaletteHotkeyEnabled)
        {
            TryParse(settings.CommandPaletteHotkey, out var modifiers, out var key);
            _registered = RegisterHotKey(_source!.Handle, HotkeyId, modifiers | ModNoRepeat, key);
            if (!_registered) { error = new Win32Exception(Marshal.GetLastWin32Error()).Message; return false; }
        }
        if (settings.FocusModeHotkeyEnabled)
        {
            TryParse(settings.FocusModeHotkey, out var modifiers, out var key);
            _focusRegistered = RegisterHotKey(_source!.Handle, FocusHotkeyId, modifiers | ModNoRepeat, key);
            if (!_focusRegistered) { error = new Win32Exception(Marshal.GetLastWin32Error()).Message; return false; }
        }
        return true;
    }

    private string Describe() => Localize.F("Text0408", (_registered ? Localize.T("Text0837") : Localize.T("Text0838")), (_focusRegistered ? Localize.T("Text0837") : Localize.T("Text0838")), (_contextRegistered ? Localize.T("Text0837") : Localize.T("Text0839")));
    public void Dispose()
    {
        if (_disposed) return;
        Unregister();
        if (_contextRegistered && _source is not null) UnregisterHotKey(_source.Handle, ContextHotkeyId);
        _contextRegistered = false;
        _source?.RemoveHook(OnWindowMessage);
        _source = null;
        _disposed = true;
    }
    private IntPtr OnWindowMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message != WmHotkey) return IntPtr.Zero;
        if (wParam.ToInt32() == HotkeyId && _registered) { Invoked?.Invoke(); handled = true; }
        if (wParam.ToInt32() == FocusHotkeyId && _focusRegistered) { FocusInvoked?.Invoke(); handled = true; }
        if (wParam.ToInt32() == ContextHotkeyId && _contextRegistered) { ContextInvoked?.Invoke(); handled = true; }
        return IntPtr.Zero;
    }
    private void Unregister()
    {
        if (_source is not null)
        {
            if (_registered) UnregisterHotKey(_source.Handle, HotkeyId);
            if (_focusRegistered) UnregisterHotKey(_source.Handle, FocusHotkeyId);
        }
        _registered = _focusRegistered = false;
    }
    private static bool IsReserved(uint modifiers, uint key)
    {
        if (modifiers == (ModControl | ModShift) && key == 0x78) return true;
        if (modifiers == ModControl && key is 0x41 or 0x43 or 0x56 or 0x58 or 0x46 or 0x09 or 0x30 or 0xBB or 0xBD or 0x6B or 0x6D) return true;
        if (modifiers == (ModControl | ModShift) && key is 0x41 or 0x43 or 0x56 or 0x54 or 0x57 or 0x4B or 0x09 or 0x24 or 0x23 or 0xBB) return true;
        return modifiers == ModShift && key is 0x21 or 0x22;
    }

    public static bool TryParse(string gesture, out uint modifiers, out uint key)
    {
        modifiers = 0;
        key = 0;
        var parts = gesture.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
        {
            return false;
        }
        foreach (var part in parts[..^1])
        {
            var modifier = part.ToLowerInvariant() switch
            {
                "ctrl" or "control" => ModControl,
                "shift" => ModShift,
                "alt" => ModAlt,
                "win" => ModWin,
                _ => 0u
            };
            if (modifier == 0 || (modifiers & modifier) != 0)
            {
                return false;
            }
            modifiers |= modifier;
        }
        try
        {
            var converted = new KeyConverter().ConvertFromString(parts[^1]);
            if (converted is not Key parsed)
            {
                return false;
            }
            if (parsed is Key.None or Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin)
            {
                return false;
            }
            key = (uint)KeyInterop.VirtualKeyFromKey(parsed);
            return key != 0;
        }
        catch (Exception exception) when (exception is NotSupportedException or FormatException or ArgumentException)
        {
            return false;
        }
    }

    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const uint ModWin = 0x0008;
    private const uint ModNoRepeat = 0x4000;

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint virtualKey);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr hwnd, int id);
}
