using System.Buffers.Binary;
using System.Diagnostics;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using ArkTerminal.Core.Profiles;

namespace ArkTerminal.Terminal.Sessions.Elevation;

// A separate UAC process of the app image. It never initializes AppHost, reads profiles,
// opens the normal UI or accepts a second launch. The elevated process owns ConPTY and its kill job.
public static class ElevatedHost
{
    public static async Task<int> RunAsync(string[] arguments)
    {
        if (arguments.Length != 5 || arguments[0] != "--elevated-host" || !ElevationProtocol.IsElevated || ElevationSupport.UnavailableReason is not null ||
            !arguments[1].StartsWith("ArkTerminal.Elevation.", StringComparison.Ordinal) || arguments[1].Length != 54 ||
            !int.TryParse(arguments[2], out var parentId) || !long.TryParse(arguments[3], out var parentStart) || arguments[4].Length != 64) return 2;
        using var lifetime = new CancellationTokenSource();
        using var pipe = new NamedPipeClientStream(".", arguments[1], PipeDirection.InOut, PipeOptions.Asynchronous, System.Security.Principal.TokenImpersonationLevel.Identification);
        ConPtySession? session = null;
        Task? outputWriter = null;
        Task? parentWatch = null;
        ElevationProtocol.VerifiedPeer? parent = null;
        var ready = false;
        var output = Channel.CreateBounded<(ElevationMessage Type, byte[] Bytes)>(new BoundedChannelOptions(64) { SingleReader = true, FullMode = BoundedChannelFullMode.Wait });
        void QueueOutput(ElevationMessage type, byte[] bytes)
        {
            // This runs on session workers. Backpressure pauses ConPTY reads instead of dropping VT
            // bytes or ending an elevated job just because the UI consumer is temporarily slower.
            try { output.Writer.WriteAsync((type, bytes), lifetime.Token).AsTask().GetAwaiter().GetResult(); }
            catch (OperationCanceledException) { }
            catch (ChannelClosedException) { }
        }
        try
        {
            using var handshake = new CancellationTokenSource(ElevationProtocol.HandshakeTimeout);
            await pipe.ConnectAsync(handshake.Token).ConfigureAwait(false);
            parent = ElevationProtocol.VerifyPeer(pipe, false, parentId, parentStart, Environment.ProcessPath!, requireElevated: false);
            parentWatch = Task.Run(async () => { try { await parent.WaitForExitAsync(lifetime.Token).ConfigureAwait(false); lifetime.Cancel(); pipe.Dispose(); } catch (OperationCanceledException) { } });
            var frame = await ElevationProtocol.ReadAsync(pipe, handshake.Token).ConfigureAwait(false);
            if (frame.Type != ElevationMessage.Launch || !Convert.ToHexString(SHA256.HashData(frame.Payload)).Equals(arguments[4], StringComparison.Ordinal)) throw new IOException(Localize.T("Text0520"));
            var launch = JsonSerializer.Deserialize<ElevationLaunch>(frame.Payload) ?? throw new IOException(Localize.T("Text0521"));
            if (launch.Version != ElevationProtocol.Version || launch.SessionId == Guid.Empty || !launch.Request.RunAsAdministrator || launch.Request.AgentRecipe is not null ||
                launch.Columns is < 1 or > 32767 || launch.Rows is < 1 or > 32767) throw new IOException(Localize.T("Text0522"));
            var local = new SessionLaunchRequest
            {
                ProfileId = launch.Request.ProfileId, ExecutablePath = launch.Request.ExecutablePath,
                Arguments = launch.Request.Arguments, WorkingDirectory = launch.Request.WorkingDirectory,
                Environment = launch.Request.Environment, ScrollbackLines = 0, ShellIntegration = ShellIntegrationMode.Disabled
            };
            Windows.ShellLaunchAdapter.Validate(local);
            session = new ConPtySession(new TerminalSessionOptions { InitialColumns = launch.Columns, InitialRows = launch.Rows, MaxScrollbackLines = 0 });
            session.OutputReceived += (_, args) =>
            {
                QueueOutput(ElevationMessage.Output, args.Bytes.ToArray());
            };
            session.StateChanged += (_, args) =>
            {
                if (args.State is SessionState.Exited or SessionState.Failed)
                {
                    QueueOutput(args.State == SessionState.Failed ? ElevationMessage.Error : ElevationMessage.Exited,
                        args.State == SessionState.Failed ? Encoding.UTF8.GetBytes(Localize.T("Text0523")) : []);
                    output.Writer.TryComplete();
                }
            };
            await session.StartAsync(local, handshake.Token).ConfigureAwait(false);
            await ElevationProtocol.SendAsync(pipe, ElevationMessage.Ready, launch.SessionId.ToByteArray(), handshake.Token).ConfigureAwait(false);
            ready = true;
            outputWriter = Task.Run(async () =>
            {
                try
                {
                    await foreach (var message in output.Reader.ReadAllAsync(lifetime.Token).ConfigureAwait(false))
                        await ElevationProtocol.SendAsync(pipe, message.Type, message.Bytes, lifetime.Token).ConfigureAwait(false);
                }
                catch (Exception error) when (error is IOException or ObjectDisposedException or OperationCanceledException) { }
                finally { lifetime.Cancel(); pipe.Dispose(); }
            });
            while (!lifetime.IsCancellationRequested)
            {
                using var idle = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
                idle.CancelAfter(ElevationProtocol.HeartbeatTimeout);
                frame = await ElevationProtocol.ReadAsync(pipe, idle.Token).ConfigureAwait(false);
                switch (frame.Type)
                {
                    case ElevationMessage.Input when frame.Payload.Length <= ElevationProtocol.MaxIoBytes:
                        await session.WriteBytesAsync(frame.Payload, idle.Token).ConfigureAwait(false); break;
                    case ElevationMessage.Resize when frame.Payload.Length == 8:
                        await session.ResizeAsync(BinaryPrimitives.ReadInt32LittleEndian(frame.Payload), BinaryPrimitives.ReadInt32LittleEndian(frame.Payload.AsSpan(4)), idle.Token).ConfigureAwait(false); break;
                    case ElevationMessage.Ping when frame.Payload.Length == 0: break;
                    case ElevationMessage.Close when frame.Payload.Length == 0: return 0;
                    default: throw new IOException(Localize.T("Text0524"));
                }
            }
            return 0;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or OperationCanceledException or JsonException or ArgumentException)
        {
            if (!ready && pipe.IsConnected)
            {
                try
                {
                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(1));
                    await ElevationProtocol.SendAsync(pipe, ElevationMessage.Error, Encoding.UTF8.GetBytes(Localize.T("Text0525") + error.Message[..Math.Min(error.Message.Length, 1024)]), timeout.Token).ConfigureAwait(false);
                }
                catch (Exception failure) when (failure is IOException or ObjectDisposedException or OperationCanceledException) { }
            }
            return 1;
        }
        finally
        {
            lifetime.Cancel(); pipe.Dispose(); output.Writer.TryComplete();
            if (session is not null) await session.DisposeAsync().ConfigureAwait(false);
            if (outputWriter is not null) await outputWriter.ConfigureAwait(false);
            if (parentWatch is not null) await parentWatch.ConfigureAwait(false);
            parent?.Dispose();
        }
    }
}
