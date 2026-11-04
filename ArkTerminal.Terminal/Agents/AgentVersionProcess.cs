using System.ComponentModel;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using ArkTerminal.Terminal.Sessions.Windows;
using Microsoft.Win32.SafeHandles;

namespace ArkTerminal.Terminal.Agents;

internal sealed record AgentVersionOutput(uint? ExitCode, string Text, bool TooLarge);

internal static class AgentVersionProcess
{
    internal static async Task<AgentVersionOutput> RunAsync(string executable, IReadOnlyList<string> arguments, int maxBytes, CancellationToken token)
    {
        using var output = new AnonymousPipeServerStream(PipeDirection.In, HandleInheritability.Inheritable);
        using var input = new AnonymousPipeServerStream(PipeDirection.Out, HandleInheritability.Inheritable);
        using var job = NativeMethods.CreateJobObjectW(IntPtr.Zero, null);
        Check(!job.IsInvalid);
        var limits = new NativeMethods.JobObjectExtendedLimitInformation();
        limits.BasicLimitInformation.LimitFlags = NativeMethods.JobObjectLimitKillOnJobClose;
        Check(NativeMethods.SetInformationJobObject(job, NativeMethods.JobObjectExtendedLimitInformationClass, ref limits, (uint)Marshal.SizeOf(limits)));
        var attributeList = IntPtr.Zero;
        var handles = Marshal.AllocHGlobal(IntPtr.Size * 2);
        SafeProcessHandle? process = null;
        SafeThreadHandle? thread = null;
        var initialized = false;
        Task<(string Text, bool TooLarge)>? reading = null;
        try
        {
            token.ThrowIfCancellationRequested();
            Marshal.WriteIntPtr(handles, 0, output.ClientSafePipeHandle.DangerousGetHandle());
            Marshal.WriteIntPtr(handles, IntPtr.Size, input.ClientSafePipeHandle.DangerousGetHandle());
            nuint size = 0;
            NativeMethods.InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref size);
            attributeList = Marshal.AllocHGlobal(checked((int)size));
            Check(NativeMethods.InitializeProcThreadAttributeList(attributeList, 1, 0, ref size)); initialized = true;
            Check(NativeMethods.UpdateProcThreadAttribute(attributeList, 0, 0x00020002, handles, (nuint)(IntPtr.Size * 2), IntPtr.Zero, IntPtr.Zero));
            var startup = new NativeMethods.StartupInfoEx
            {
                StartupInfo = new()
                {
                    Size = Marshal.SizeOf<NativeMethods.StartupInfoEx>(), Flags = NativeMethods.StartFUseStdHandles,
                    StdInput = input.ClientSafePipeHandle.DangerousGetHandle(), StdOutput = output.ClientSafePipeHandle.DangerousGetHandle(),
                    StdError = output.ClientSafePipeHandle.DangerousGetHandle()
                }, AttributeList = attributeList
            };
            var security = new NativeMethods.SecurityAttributes { Length = (uint)Marshal.SizeOf<NativeMethods.SecurityAttributes>() };
            Check(NativeMethods.CreateProcessW(executable, WindowsCommandLine.Build(executable, arguments), ref security, ref security, true,
                NativeMethods.CreateSuspended | NativeMethods.ExtendedStartupInfoPresent | 0x08000000, IntPtr.Zero,
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ref startup, out var information));
            process = new(information.Process, true); thread = new(information.Thread, true);
            // Join the job before executing any third-party instruction; descendants cannot escape a timeout race.
            Check(NativeMethods.AssignProcessToJobObject(job, process));
            output.DisposeLocalCopyOfClientHandle(); input.DisposeLocalCopyOfClientHandle(); input.Dispose();
            reading = Task.Run(() =>
            {
                var bytes = new byte[maxBytes + 1];
                var count = 0;
                try
                {
                    while (count < bytes.Length)
                    {
                        var read = output.Read(bytes, count, bytes.Length - count);
                        if (read == 0) break;
                        count += read;
                    }
                }
                catch (Exception error) when (error is IOException or ObjectDisposedException) { }
                return (Encoding.UTF8.GetString(bytes, 0, Math.Min(count, maxBytes)), count > maxBytes);
            }, CancellationToken.None);
            Check(NativeMethods.ResumeThread(thread) != uint.MaxValue);
            while (NativeMethods.WaitForSingleObject(process, 0) == NativeMethods.WaitTimeout)
            {
                token.ThrowIfCancellationRequested();
                if (reading.IsCompleted && (await reading.ConfigureAwait(false)).TooLarge)
                    return new(null, string.Empty, true);
                await Task.Delay(20, token).ConfigureAwait(false);
            }
            NativeMethods.GetExitCodeProcess(process, out var code);
            job.Dispose(); // Also close a child that inherited stdout after its parent exited.
            var result = await reading.WaitAsync(token).ConfigureAwait(false);
            return new(code, result.Text, result.TooLarge);
        }
        finally
        {
            if (process is not null && NativeMethods.WaitForSingleObject(process, 0) == NativeMethods.WaitTimeout)
                NativeMethods.TerminateProcess(process, 1);
            job.Dispose();
            if (reading is not null)
            {
                try { await reading.WaitAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false); }
                catch (Exception error) when (error is TimeoutException or IOException or ObjectDisposedException) { output.Dispose(); }
            }
            thread?.Dispose(); process?.Dispose();
            if (initialized) NativeMethods.DeleteProcThreadAttributeList(attributeList);
            if (attributeList != IntPtr.Zero) Marshal.FreeHGlobal(attributeList);
            Marshal.FreeHGlobal(handles);
        }
    }

    private static void Check(bool success) { if (!success) throw new Win32Exception(Marshal.GetLastWin32Error()); }
}
