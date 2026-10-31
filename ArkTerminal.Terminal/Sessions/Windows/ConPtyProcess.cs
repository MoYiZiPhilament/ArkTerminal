using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace ArkTerminal.Terminal.Sessions.Windows;

internal sealed class ConPtyProcess : IDisposable
{
    private readonly SafePseudoConsoleHandle _pseudoConsole;
    private readonly SafeFileHandle _inputWrite;
    private readonly SafeFileHandle _outputRead;
    private readonly SafeProcessHandle _process;
    private readonly SafeJobObjectHandle _job;
    private bool _disposed;

    private ConPtyProcess(
        SafePseudoConsoleHandle pseudoConsole,
        SafeFileHandle inputWrite,
        SafeFileHandle outputRead,
        SafeProcessHandle process,
        SafeJobObjectHandle job)
    {
        _pseudoConsole = pseudoConsole;
        _inputWrite = inputWrite;
        _outputRead = outputRead;
        _process = process;
        _job = job;
    }

    internal SafeFileHandle InputWrite => _inputWrite;
    internal SafeFileHandle OutputRead => _outputRead;
    internal int ProcessId { get; private init; }

    internal static ConPtyProcess Start(SessionLaunchRequest request, int columns, int rows)
    {
        var executablePath = ShellLaunchAdapter.ResolveExecutablePath(request.ExecutablePath);
        var workingDirectory = ShellLaunchAdapter.ResolveWorkingDirectory(request.WorkingDirectory);
        var commandLine = WindowsCommandLine.Build(executablePath, request.Arguments);
        var environment = ShellLaunchAdapter.BuildEnvironmentBlock(request.Environment);

        SafeFileHandle? pseudoInputRead = null;
        SafeFileHandle? inputWrite = null;
        SafeFileHandle? outputRead = null;
        SafeFileHandle? pseudoOutputWrite = null;
        SafePseudoConsoleHandle? pseudoConsole = null;
        SafeJobObjectHandle? job = null;
        SafeProcessHandle? process = null;
        SafeThreadHandle? thread = null;
        var attributeList = IntPtr.Zero;
        var environmentPointer = IntPtr.Zero;
        var attributeListInitialized = false;

        try
        {
            Check(NativeMethods.CreatePipe(out pseudoInputRead, out inputWrite, IntPtr.Zero, 0));
            Check(NativeMethods.SetHandleInformation(inputWrite, NativeMethods.HandleFlagInherit, 0));
            Check(NativeMethods.CreatePipe(out outputRead, out pseudoOutputWrite, IntPtr.Zero, 0));
            Check(NativeMethods.SetHandleInformation(outputRead, NativeMethods.HandleFlagInherit, 0));

            var coordinate = new NativeMethods.Coordinate(checked((short)columns), checked((short)rows));
            var result = NativeMethods.CreatePseudoConsole(
                coordinate,
                pseudoInputRead,
                pseudoOutputWrite,
                0,
                out pseudoConsole);
            Marshal.ThrowExceptionForHR(result);

            job = NativeMethods.CreateJobObjectW(IntPtr.Zero, null);
            Check(!job.IsInvalid);
            var limits = new NativeMethods.JobObjectExtendedLimitInformation();
            limits.BasicLimitInformation.LimitFlags = NativeMethods.JobObjectLimitKillOnJobClose;
            Check(NativeMethods.SetInformationJobObject(
                job,
                NativeMethods.JobObjectExtendedLimitInformationClass,
                ref limits,
                (uint)Marshal.SizeOf<NativeMethods.JobObjectExtendedLimitInformation>()));

            nuint attributeSize = 0;
            NativeMethods.InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref attributeSize);
            attributeList = Marshal.AllocHGlobal(checked((int)attributeSize));
            Check(NativeMethods.InitializeProcThreadAttributeList(attributeList, 1, 0, ref attributeSize));
            attributeListInitialized = true;
            var pseudoConsolePointer = pseudoConsole.DangerousGetHandle();
            Check(NativeMethods.UpdateProcThreadAttribute(
                attributeList,
                0,
                NativeMethods.ProcThreadAttributePseudoConsole,
                pseudoConsolePointer,
                (nuint)IntPtr.Size,
                IntPtr.Zero,
                IntPtr.Zero));

            environmentPointer = Marshal.StringToHGlobalUni(environment);
            var startup = new NativeMethods.StartupInfoEx
            {
                StartupInfo = new NativeMethods.StartupInfo
                {
                    Size = Marshal.SizeOf<NativeMethods.StartupInfoEx>(),
                    Flags = NativeMethods.StartFUseStdHandles
                },
                AttributeList = attributeList
            };
            var flags = NativeMethods.ExtendedStartupInfoPresent |
                        NativeMethods.CreateSuspended |
                        NativeMethods.CreateUnicodeEnvironment;
            var processSecurity = new NativeMethods.SecurityAttributes
            {
                Length = (uint)Marshal.SizeOf<NativeMethods.SecurityAttributes>()
            };
            var threadSecurity = new NativeMethods.SecurityAttributes
            {
                Length = (uint)Marshal.SizeOf<NativeMethods.SecurityAttributes>()
            };
            Check(NativeMethods.CreateProcessW(
                executablePath,
                commandLine,
                ref processSecurity,
                ref threadSecurity,
                false,
                flags,
                environmentPointer,
                workingDirectory,
                ref startup,
                out var processInformation));

            pseudoInputRead?.Dispose();
            pseudoInputRead = null;
            pseudoOutputWrite?.Dispose();
            pseudoOutputWrite = null;
            process = new SafeProcessHandle(processInformation.Process, true);
            thread = new SafeThreadHandle(processInformation.Thread, true);
            Check(NativeMethods.AssignProcessToJobObject(job, process));
            Check(NativeMethods.ResumeThread(thread) != uint.MaxValue);

            var started = new ConPtyProcess(pseudoConsole, inputWrite, outputRead, process, job)
            {
                ProcessId = checked((int)processInformation.ProcessId)
            };
            pseudoConsole = null;
            inputWrite = null;
            outputRead = null;
            process = null;
            job = null;
            return started;
        }
        catch
        {
            if (process is not null)
            {
                NativeMethods.TerminateProcess(process, 1);
            }
            throw;
        }
        finally
        {
            if (attributeListInitialized)
            {
                NativeMethods.DeleteProcThreadAttributeList(attributeList);
            }
            if (attributeList != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(attributeList);
            }
            if (environmentPointer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(environmentPointer);
            }
            thread?.Dispose();
            process?.Dispose();
            job?.Dispose();
            pseudoConsole?.Dispose();
            pseudoOutputWrite?.Dispose();
            outputRead?.Dispose();
            inputWrite?.Dispose();
            pseudoInputRead?.Dispose();
        }
    }

    internal void Resize(int columns, int rows)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Marshal.ThrowExceptionForHR(NativeMethods.ResizePseudoConsole(
            _pseudoConsole,
            new NativeMethods.Coordinate(checked((short)columns), checked((short)rows))));
    }

    internal bool HasExited => NativeMethods.WaitForSingleObject(_process, 0) == NativeMethods.WaitObject0;

    internal uint? ExitCode => NativeMethods.GetExitCodeProcess(_process, out var exitCode) ? exitCode : null;

    internal void Wait(uint milliseconds) => NativeMethods.WaitForSingleObject(_process, milliseconds);

    internal void TerminateTree()
    {
        if (!_job.IsInvalid && !_job.IsClosed)
        {
            NativeMethods.TerminateJobObject(_job, 1);
        }
    }

    internal void CloseInput() => _inputWrite.Dispose();

    internal void CloseOutput() => _outputRead.Dispose();

    internal void ClosePseudoConsole() => _pseudoConsole.Dispose();

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        _inputWrite.Dispose();
        _outputRead.Dispose();
        _pseudoConsole.Dispose();
        _process.Dispose();
        _job.Dispose();
    }

    private static void Check(bool success)
    {
        if (!success)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }
    }
}
