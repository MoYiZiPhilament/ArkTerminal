using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Interop;

namespace ArkTerminal.Infrastructure;

public sealed class GlobalHotkeyService : IDisposable
{
    private const int HotkeyId = 0x4152;
    private const int FocusHotkeyId = 0x4153;
    private const int WmHotkey = 0x0312;
    private HwndSource? _source;
    private bool _registered;
    private bool _focusRegistered;
    private bool _enabled;
    private string _gesture = string.Empty;
    private uint _modifiers;
    private uint _key;
    private bool _disposed;

    public event Action? Invoked;
    public event Action? FocusInvoked;
    public string StatusText { get; private set; } = string.Empty;
    public bool IsRegistered => _registered;

    public void Attach(HwndSource source)
    {
        _source = source;
        _source.AddHook(OnWindowMessage);
        _focusRegistered = RegisterHotKey(source.Handle, FocusHotkeyId, ModControl | ModShift | ModNoRepeat, 0x7A);
    }

    public bool Configure(bool enabled, string gesture, out string error)
    {
        error = string.Empty;
        if (_disposed || _source is null)
        {
            error = "窗口尚未准备好注册全局热键。";
            return false;
        }
        if (_enabled == enabled && _gesture == gesture && (_registered || !enabled))
        {
            return true;
        }
        if (!enabled)
        {
            Unregister();
            _enabled = false;
            _gesture = gesture;
            StatusText = "全局热键已禁用。";
            return true;
        }
        if (!TryParse(gesture, out var modifiers, out var key))
        {
            error = "热键须使用 Ctrl/Shift/Alt/Win 修饰键和一个普通键。";
            return false;
        }
        var hadRegistration = _registered;
        var oldModifiers = _modifiers;
        var oldKey = _key;
        Unregister();
        if (!RegisterHotKey(_source.Handle, HotkeyId, modifiers | ModNoRepeat, key))
        {
            error = new Win32Exception(Marshal.GetLastWin32Error()).Message;
            _registered = hadRegistration && RegisterHotKey(_source.Handle, HotkeyId, oldModifiers, oldKey);
            StatusText = $"热键注册失败：{error}" + (_registered ? " 已保留旧热键。" : " 旧热键未恢复。");
            return false;
        }
        _registered = true;
        _enabled = true;
        _gesture = gesture;
        _modifiers = modifiers | ModNoRepeat;
        _key = key;
        StatusText = $"全局热键：{gesture}";
        return true;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        Unregister();
        if (_focusRegistered && _source is not null)
        {
            UnregisterHotKey(_source.Handle, FocusHotkeyId);
        }
        _focusRegistered = false;
        _source?.RemoveHook(OnWindowMessage);
        _source = null;
        _disposed = true;
    }

    private IntPtr OnWindowMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == WmHotkey && wParam.ToInt32() == HotkeyId)
        {
            Invoked?.Invoke();
            handled = true;
        }
        else if (message == WmHotkey && wParam.ToInt32() == FocusHotkeyId)
        {
            FocusInvoked?.Invoke();
            handled = true;
        }
        return IntPtr.Zero;
    }

    private void Unregister()
    {
        if (_registered && _source is not null)
        {
            UnregisterHotKey(_source.Handle, HotkeyId);
        }
        _registered = false;
    }

    private static bool TryParse(string gesture, out uint modifiers, out uint key)
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
