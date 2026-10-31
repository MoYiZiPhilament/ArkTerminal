using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace ArkTerminal.Terminal.Sessions.Windows;

internal sealed class SafePseudoConsoleHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    public SafePseudoConsoleHandle() : base(true)
    {
    }

    protected override bool ReleaseHandle()
    {
        NativeMethods.ClosePseudoConsole(handle);
        return true;
    }
}

internal sealed class SafeJobObjectHandle() : SafeHandleZeroOrMinusOneIsInvalid(true)
{
    protected override bool ReleaseHandle() => NativeMethods.CloseHandle(handle);
}

internal sealed class SafeThreadHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    public SafeThreadHandle() : base(true)
    {
    }

    public SafeThreadHandle(IntPtr handle, bool ownsHandle) : base(ownsHandle)
    {
        SetHandle(handle);
    }

    protected override bool ReleaseHandle() => NativeMethods.CloseHandle(handle);
}
