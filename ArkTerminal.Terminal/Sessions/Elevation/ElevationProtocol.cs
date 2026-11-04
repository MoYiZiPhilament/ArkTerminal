using System.Buffers.Binary;
using System.ComponentModel;
using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace ArkTerminal.Terminal.Sessions.Elevation;

internal enum ElevationMessage : byte { Launch = 1, Ready, Output, Input, Resize, Ping, Close, Exited, Error }
internal sealed record ElevationLaunch(int Version, Guid SessionId, int Columns, int Rows, SessionLaunchRequest Request);

internal static class ElevationProtocol
{
    internal const int Version = 1;
    internal const int MaxFrameBytes = 256 * 1024;
    internal const int MaxIoBytes = 16 * 1024;
    internal static readonly TimeSpan HandshakeTimeout = TimeSpan.FromSeconds(30);
    internal static readonly TimeSpan HeartbeatTimeout = TimeSpan.FromSeconds(15);

    internal static NamedPipeServerStream CreateLocalServer(string name, int maximumInstances = 1, bool firstInstance = true)
    {
        using var identity = WindowsIdentity.GetCurrent();
        var descriptor = IntPtr.Zero;
        if (!ConvertStringSecurityDescriptorToSecurityDescriptorW("D:P(A;;GA;;;" + identity.User!.Value + ")", 1, out descriptor, out _))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        try
        {
            var security = new PipeSecurityAttributes { Length = Marshal.SizeOf<PipeSecurityAttributes>(), Descriptor = descriptor };
            // Duplex, overlapped, first instance. Reject network clients in the kernel, before authentication.
            var handle = CreateNamedPipeW(@"\\.\pipe\" + name, 3 | 0x40000000 | (firstInstance ? 0x00080000u : 0), 8, (uint)maximumInstances, 65536, 65536, 30000, ref security);
            if (handle.IsInvalid) { var error = Marshal.GetLastWin32Error(); handle.Dispose(); throw new Win32Exception(error); }
            try { return new NamedPipeServerStream(PipeDirection.InOut, true, false, handle); }
            catch { handle.Dispose(); throw; }
        }
        finally { LocalFree(descriptor); }
    }

    internal static async Task SendAsync(Stream pipe, ElevationMessage type, ReadOnlyMemory<byte> payload, CancellationToken token)
    {
        if (payload.Length + 1 > MaxFrameBytes) throw new IOException("提权消息超出长度限制。");
        var header = new byte[5];
        BinaryPrimitives.WriteInt32LittleEndian(header, payload.Length + 1); header[4] = (byte)type;
        await pipe.WriteAsync(header, token).ConfigureAwait(false);
        if (payload.Length > 0) await pipe.WriteAsync(payload, token).ConfigureAwait(false);
        await pipe.FlushAsync(token).ConfigureAwait(false);
    }
    internal static async Task<(ElevationMessage Type, byte[] Payload)> ReadAsync(Stream pipe, CancellationToken token)
    {
        var header = new byte[5];
        await pipe.ReadExactlyAsync(header, token).ConfigureAwait(false);
        var length = BinaryPrimitives.ReadInt32LittleEndian(header) - 1;
        if (length < 0 || length >= MaxFrameBytes || !Enum.IsDefined((ElevationMessage)header[4])) throw new IOException("提权协议消息无效。");
        var payload = new byte[length];
        await pipe.ReadExactlyAsync(payload, token).ConfigureAwait(false);
        return ((ElevationMessage)header[4], payload);
    }

    internal static VerifiedPeer VerifyPeer(PipeStream pipe, bool server, int expectedPid, long expectedStart, string executable, bool requireElevated)
    {
        var success = server ? GetNamedPipeClientProcessId(pipe.SafePipeHandle, out var pid) : GetNamedPipeServerProcessId(pipe.SafePipeHandle, out pid);
        if (!success || pid != expectedPid) throw new IOException("提权 IPC 对端进程身份不匹配。");
        var handle = OpenProcess(0x1000 | 0x00100000, false, expectedPid); // QUERY_LIMITED_INFORMATION | SYNCHRONIZE
        if (handle.IsInvalid) { var error = Marshal.GetLastWin32Error(); handle.Dispose(); throw new Win32Exception(error); }
        try
        {
            if (WaitForSingleObject(handle, 0) != 258 || !GetProcessTimes(handle, out var creation, out _, out _, out _) ||
                DateTime.FromFileTimeUtc(creation).Ticks != expectedStart ||
                !string.Equals(ProcessPath(handle), executable, StringComparison.OrdinalIgnoreCase)) throw new IOException("提权 IPC 对端映像或进程寿命不匹配。");
            if (!OpenProcessToken(handle, 8, out var token)) throw new Win32Exception(Marshal.GetLastWin32Error());
            using (token)
            using (var identity = new WindowsIdentity(token.DangerousGetHandle()))
            using (var current = WindowsIdentity.GetCurrent())
            {
                if (identity.User != current.User) throw new IOException("仅支持当前同一用户的 UAC 提权。");
                if (requireElevated && !IsElevatedToken(token)) throw new IOException("辅助器未获得真实提升令牌。");
            }
            return new VerifiedPeer(handle); // Hold the kernel object, not just a reusable PID.
        }
        catch { handle.Dispose(); throw; }
    }

    internal static int GetClientProcessId(PipeStream pipe) => GetNamedPipeClientProcessId(pipe.SafePipeHandle, out var pid)
        ? checked((int)pid) : throw new Win32Exception(Marshal.GetLastWin32Error());
    internal static string ProcessPath(SafeProcessHandle handle)
    {
        var path = new StringBuilder(32768); var length = path.Capacity;
        if (!QueryFullProcessImageNameW(handle, 0, path, ref length)) throw new Win32Exception(Marshal.GetLastWin32Error());
        return System.IO.Path.GetFullPath(path.ToString());
    }
    public static bool IsElevated
    {
        get
        {
            using var process = Process.GetCurrentProcess();
            if (!OpenProcessToken(process.SafeHandle, 8, out var token)) return false;
            using (token) return IsElevatedToken(token);
        }
    }
    private static bool IsElevatedToken(SafeAccessTokenHandle token) =>
        GetTokenInformation(token, 20, out var elevated, sizeof(int), out _) && elevated != 0;

    internal sealed class VerifiedPeer(SafeProcessHandle handle) : IDisposable
    {
        internal async Task WaitForExitAsync(CancellationToken token)
        {
            while (WaitForSingleObject(handle, 0) == 258) await Task.Delay(250, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
        }
        public void Dispose() => handle.Dispose();
    }

    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetNamedPipeClientProcessId(SafePipeHandle pipe, out uint pid);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern SafeProcessHandle OpenProcess(uint access, bool inherit, int pid);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern uint WaitForSingleObject(SafeProcessHandle handle, uint milliseconds);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetProcessTimes(SafeProcessHandle process, out long created, out long exited, out long kernel, out long user);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetNamedPipeServerProcessId(SafePipeHandle pipe, out uint pid);
    [StructLayout(LayoutKind.Sequential)] private struct PipeSecurityAttributes { internal int Length; internal IntPtr Descriptor; [MarshalAs(UnmanagedType.Bool)] internal bool Inherit; }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern SafePipeHandle CreateNamedPipeW(string name, uint access, uint mode, uint instances, uint output, uint input, uint timeout, ref PipeSecurityAttributes security);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool ConvertStringSecurityDescriptorToSecurityDescriptorW(string text, uint version, out IntPtr descriptor, out uint size);
    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr pointer);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool QueryFullProcessImageNameW(SafeProcessHandle process, uint flags, StringBuilder name, ref int size);
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool OpenProcessToken(SafeProcessHandle process, uint access, out SafeAccessTokenHandle token);
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool GetTokenInformation(SafeAccessTokenHandle token, int informationClass, out int information, int length, out int returned);
}

public static class ElevationSupport
{
    public static string? UnavailableReason
    {
        get
        {
            var length = 0;
            var result = GetCurrentPackageFullName(ref length, null);
            return result == 15700 ? null : "MSIX 包身份模式的提权尚未通过安装与 allowElevation 能力验证，已禁用管理员启动；普通会话可用。";
        }
    }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern int GetCurrentPackageFullName(ref int length, StringBuilder? name);
}
