using System.Runtime.InteropServices;

namespace ArkTerminal.Infrastructure;

internal sealed class ShellNotificationService(IntPtr window) : IDisposable
{
    private bool _added;
    internal static bool IsApplicationFocused
    {
        get { GetWindowThreadProcessId(GetForegroundWindow(), out var process); return process == Environment.ProcessId; }
    }
    internal bool Show(string text)
    {
        var data = Data();
        if (!_added) { data.Flags = 2 | 4; data.Icon = LoadIconW(IntPtr.Zero, new IntPtr(32512)); data.Tip = "ArkTerminal"; _added = Shell_NotifyIconW(0, ref data); }
        if (!_added) return false;
        data.Flags = 0x10; data.Info = text; data.InfoTitle = "ArkTerminal"; data.InfoFlags = 1 | 0x10; // NIIF_NOSOUND
        var flash = new FlashInfo { Size = (uint)Marshal.SizeOf<FlashInfo>(), Window = window, Flags = 2, Count = 3 };
        FlashWindowEx(ref flash);
        return Shell_NotifyIconW(1, ref data);
    }
    private IconData Data() => new() { Size = Marshal.SizeOf<IconData>(), Window = window, Id = 7, Tip = string.Empty, Info = string.Empty, InfoTitle = string.Empty };
    public void Dispose()
    {
        if (!_added) return;
        var data = Data(); Shell_NotifyIconW(2, ref data); _added = false;
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct IconData
    {
        internal int Size; internal IntPtr Window; internal uint Id, Flags, Callback; internal IntPtr Icon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] internal string Tip;
        internal uint State, StateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] internal string Info;
        internal uint Timeout;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] internal string InfoTitle;
        internal uint InfoFlags; internal Guid Guid; internal IntPtr BalloonIcon;
    }
    [StructLayout(LayoutKind.Sequential)] private struct FlashInfo { internal uint Size; internal IntPtr Window; internal uint Flags, Count, Timeout; }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern bool Shell_NotifyIconW(uint message, ref IconData data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr LoadIconW(IntPtr instance, IntPtr name);
    [DllImport("user32.dll")] private static extern bool FlashWindowEx(ref FlashInfo info);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out int process);
}
