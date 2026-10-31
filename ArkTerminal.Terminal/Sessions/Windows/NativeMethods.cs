using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace ArkTerminal.Terminal.Sessions.Windows;

internal static class NativeMethods
{
    internal const uint ExtendedStartupInfoPresent = 0x00080000;
    internal const uint CreateSuspended = 0x00000004;
    internal const uint CreateUnicodeEnvironment = 0x00000400;
    internal const int StartFUseStdHandles = 0x00000100;
    internal const uint ProcThreadAttributePseudoConsole = 0x00020016;
    internal const uint HandleFlagInherit = 0x00000001;
    internal const uint Infinite = 0xFFFFFFFF;
    internal const uint WaitObject0 = 0;
    internal const uint WaitTimeout = 0x00000102;
    internal const int JobObjectExtendedLimitInformationClass = 9;
    internal const uint JobObjectLimitKillOnJobClose = 0x00002000;
    internal const uint ThreadTerminate = 0x0001;

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern bool CreatePipe(
        out SafeFileHandle readPipe,
        out SafeFileHandle writePipe,
        IntPtr pipeAttributes,
        uint size);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern bool SetHandleInformation(SafeFileHandle handle, uint mask, uint flags);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern int CreatePseudoConsole(
        Coordinate size,
        SafeFileHandle input,
        SafeFileHandle output,
        uint flags,
        out SafePseudoConsoleHandle pseudoConsole);

    [DllImport("kernel32.dll")]
    internal static extern void ClosePseudoConsole(IntPtr pseudoConsole);

    [DllImport("kernel32.dll")]
    internal static extern int ResizePseudoConsole(SafePseudoConsoleHandle pseudoConsole, Coordinate size);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern bool InitializeProcThreadAttributeList(
        IntPtr attributeList,
        uint attributeCount,
        uint flags,
        ref nuint size);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern bool UpdateProcThreadAttribute(
        IntPtr attributeList,
        uint flags,
        nuint attribute,
        IntPtr value,
        nuint size,
        IntPtr previousValue,
        IntPtr returnSize);

    [DllImport("kernel32.dll")]
    internal static extern void DeleteProcThreadAttributeList(IntPtr attributeList);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern bool CreateProcessW(
        string? applicationName,
        string commandLine,
        ref SecurityAttributes processAttributes,
        ref SecurityAttributes threadAttributes,
        bool inheritHandles,
        uint creationFlags,
        IntPtr environment,
        string currentDirectory,
        ref StartupInfoEx startupInfo,
        out ProcessInformation processInformation);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern bool TerminateProcess(SafeProcessHandle process, uint exitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern uint ResumeThread(SafeThreadHandle thread);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern uint WaitForSingleObject(SafeProcessHandle process, uint milliseconds);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern bool GetExitCodeProcess(SafeProcessHandle process, out uint exitCode);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern SafeJobObjectHandle CreateJobObjectW(IntPtr jobAttributes, string? name);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern bool SetInformationJobObject(
        SafeJobObjectHandle job,
        int informationClass,
        ref JobObjectExtendedLimitInformation information,
        uint length);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern bool AssignProcessToJobObject(SafeJobObjectHandle job, SafeProcessHandle process);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern bool TerminateJobObject(SafeJobObjectHandle job, uint exitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll")]
    internal static extern uint GetCurrentThreadId();

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern SafeThreadHandle OpenThread(uint desiredAccess, bool inheritHandle, uint threadId);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern bool CancelSynchronousIo(SafeThreadHandle thread);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern uint SearchPathW(
        string? path,
        string fileName,
        string? extension,
        uint bufferLength,
        System.Text.StringBuilder buffer,
        IntPtr filePart);

    [StructLayout(LayoutKind.Sequential)]
    internal struct SecurityAttributes
    {
        internal uint Length;
        internal IntPtr SecurityDescriptor;
        internal bool InheritHandle;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Coordinate(short x, short y)
    {
        internal short X = x;
        internal short Y = y;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct StartupInfo
    {
        internal int Size;
        internal string? Reserved;
        internal string? Desktop;
        internal string? Title;
        internal int X;
        internal int Y;
        internal int XSize;
        internal int YSize;
        internal int XCountChars;
        internal int YCountChars;
        internal int FillAttribute;
        internal int Flags;
        internal short ShowWindow;
        internal short Reserved2Size;
        internal IntPtr Reserved2;
        internal IntPtr StdInput;
        internal IntPtr StdOutput;
        internal IntPtr StdError;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct StartupInfoEx
    {
        internal StartupInfo StartupInfo;
        internal IntPtr AttributeList;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct ProcessInformation
    {
        internal IntPtr Process;
        internal IntPtr Thread;
        internal uint ProcessId;
        internal uint ThreadId;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct JobObjectBasicLimitInformation
    {
        internal long PerProcessUserTimeLimit;
        internal long PerJobUserTimeLimit;
        internal uint LimitFlags;
        internal nuint MinimumWorkingSetSize;
        internal nuint MaximumWorkingSetSize;
        internal uint ActiveProcessLimit;
        internal nuint Affinity;
        internal uint PriorityClass;
        internal uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct IoCounters
    {
        internal ulong ReadOperationCount;
        internal ulong WriteOperationCount;
        internal ulong OtherOperationCount;
        internal ulong ReadTransferCount;
        internal ulong WriteTransferCount;
        internal ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct JobObjectExtendedLimitInformation
    {
        internal JobObjectBasicLimitInformation BasicLimitInformation;
        internal IoCounters IoInfo;
        internal nuint ProcessMemoryLimit;
        internal nuint JobMemoryLimit;
        internal nuint PeakProcessMemoryUsed;
        internal nuint PeakJobMemoryUsed;
    }
}
