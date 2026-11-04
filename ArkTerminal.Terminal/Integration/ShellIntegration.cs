using System.Diagnostics;
using System.Text;
using ArkTerminal.Core.Profiles;
using ArkTerminal.Terminal.Sessions;

namespace ArkTerminal.Terminal.Integration;

[Flags]
public enum ShellCapabilities { None = 0, WorkingDirectory = 1, CommandBoundaries = 2, ExitStatus = 4, Prompt = 8 }
public enum ShellCommandState { Unknown, Ready, Running }
public enum ShellEventKind { Metadata, Started, Completed, Prompt }

// These are untrusted process metadata, never authority to execute or read anything.
public sealed record ShellStatus(string Shell, ShellCapabilities Capabilities, ShellCommandState State,
    string? WorkingDirectory, long CommandId, string? Command, int? ExitCode, bool? Succeeded,
    TimeSpan? Duration, string Detail);
public sealed class ShellIntegrationEventArgs(ShellEventKind kind, ShellStatus status) : EventArgs
{
    public ShellEventKind Kind { get; } = kind;
    public ShellStatus Status { get; } = status;
}
public interface IShellIntegrationSource
{
    ShellStatus ShellStatus { get; }
    event EventHandler<ShellIntegrationEventArgs>? ShellIntegrationChanged;
}
public interface ITerminalTextSource
{
    event Action<string>? TextReceived;
}

internal sealed class ShellIntegrationTracker
{
    private readonly object _sync = new();
    private string _token = string.Empty;
    private long _started;
    private ShellStatus _status = new("未知", ShellCapabilities.None, ShellCommandState.Unknown, null, 0, null, null, null, null, "集成已关闭；状态未知。");
    internal ShellStatus Status { get { lock (_sync) return _status; } }
    internal event EventHandler<ShellIntegrationEventArgs>? Changed;

    internal void Configure(string token, string shell, ShellCapabilities capabilities, string detail)
    {
        lock (_sync)
        {
            _token = token;
            _status = new(shell, capabilities, ShellCommandState.Unknown, null, 0, null, null, null, null, detail);
        }
    }

    internal void Accept(string osc)
    {
        ShellIntegrationEventArgs? update = null;
        lock (_sync)
        {
            if (_token.Length == 0 || !osc.StartsWith("633;Ark;" + _token + ";", StringComparison.Ordinal)) return;
            var parts = osc.Split(';', 5);
            if (parts.Length != 5) return;
            var kind = parts[3];
            var payload = parts[4];
            var eventKind = ShellEventKind.Metadata;
            switch (kind)
            {
                case "cap":
                    if (!int.TryParse(payload, out var flags) || flags < 0 || (flags & ~(int)_status.Capabilities) != 0) return;
                    _status = _status with { Capabilities = (ShellCapabilities)flags };
                    break;
                case "cwd":
                case "cwdraw":
                    if (!_status.Capabilities.HasFlag(ShellCapabilities.WorkingDirectory)) return;
                    var cwd = kind == "cwd" ? Decode(payload) : payload;
                    _status = _status with { WorkingDirectory = Clean(cwd, 4096) };
                    break;
                case "start":
                    if (!_status.Capabilities.HasFlag(ShellCapabilities.CommandBoundaries)) return;
                    _started = Stopwatch.GetTimestamp();
                    _status = _status with { State = ShellCommandState.Running, CommandId = _status.CommandId + 1,
                        Command = Clean(Decode(payload), 4096, allowNewlines: true), ExitCode = null, Succeeded = null, Duration = null };
                    eventKind = ShellEventKind.Started;
                    break;
                case "end":
                    if (_status.State != ShellCommandState.Running) return;
                    var values = payload.Split(',');
                    int? code = values.Length > 0 && int.TryParse(values[0], out var parsed) ? parsed : null;
                    bool? success = values.Length > 1 && bool.TryParse(values[1], out var flag) ? flag : code is not null ? code == 0 : null;
                    _status = _status with { State = ShellCommandState.Ready, ExitCode = code, Succeeded = success,
                        Duration = Stopwatch.GetElapsedTime(_started) };
                    eventKind = ShellEventKind.Completed;
                    break;
                case "prompt":
                    if (!_status.Capabilities.HasFlag(ShellCapabilities.Prompt)) return;
                    _status = _status with { State = ShellCommandState.Ready };
                    eventKind = ShellEventKind.Prompt;
                    break;
                default: return;
            }
            update = new(eventKind, _status);
        }
        Changed?.Invoke(this, update);
    }

    private static string? Decode(string value)
    {
        try { return new UTF8Encoding(false, true).GetString(Convert.FromBase64String(value)); }
        catch (Exception error) when (error is FormatException or DecoderFallbackException) { return null; }
    }
    private static string? Clean(string? value, int maximum, bool allowNewlines = false) =>
        string.IsNullOrWhiteSpace(value) || value.Length > maximum || value.Any(c => char.IsControl(c) && !(allowNewlines && c is '\r' or '\n' or '\t')) ||
        value.Any(c => c is >= '\u202a' and <= '\u202e' or >= '\u2066' and <= '\u2069') ? null : value;
}

internal sealed record ShellInjection(SessionLaunchRequest Request, string Token, string Shell, ShellCapabilities Capabilities, string Detail);

internal static class ShellIntegrationInjector
{
    internal static ShellInjection Prepare(SessionLaunchRequest request)
    {
        var name = Path.GetFileNameWithoutExtension(request.ExecutablePath).ToLowerInvariant();
        ShellInjection Disabled(string detail) => new(request, string.Empty, name, ShellCapabilities.None, detail);
        if (request.ShellIntegration == ShellIntegrationMode.Disabled) return Disabled("集成已关闭；未注入脚本，命令状态与当前目录未知。");
        var token = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(16));
        var arguments = request.Arguments.ToList();
        var environment = new Dictionary<string, string>(request.Environment, StringComparer.OrdinalIgnoreCase);
        var capabilities = ShellCapabilities.WorkingDirectory;
        string detail;
        if (name is "powershell" or "pwsh")
        {
            if (arguments.Any(a => a.StartsWith("-c", StringComparison.OrdinalIgnoreCase) || a.StartsWith("-f", StringComparison.OrdinalIgnoreCase) ||
                a.StartsWith("-e", StringComparison.OrdinalIgnoreCase) || a.Equals("-NonInteractive", StringComparison.OrdinalIgnoreCase)))
                return Disabled("非交互或自带命令的 PowerShell 启动参数：未注入，状态未知。");
            var script = Resource("powershell.ps1").Replace("__ARK_TOKEN__", token);
            arguments.AddRange(["-NoExit", "-EncodedCommand", Convert.ToBase64String(Encoding.Unicode.GetBytes(script))]);
            capabilities |= ShellCapabilities.CommandBoundaries | ShellCapabilities.ExitStatus | ShellCapabilities.Prompt;
            detail = "PowerShell ReadLine / prompt 边界；成功状态可靠，简单原生命令提供退出码，其余退出码未知。PSReadLine 不可用时无命令边界。";
        }
        else if (name == "cmd")
        {
            if (arguments.Any(a => a.StartsWith("/c", StringComparison.OrdinalIgnoreCase) || a.StartsWith("/k", StringComparison.OrdinalIgnoreCase)))
                return Disabled("CMD 自带启动命令：未注入，状态未知。");
            var prompt = environment.GetValueOrDefault("PROMPT") ?? Environment.GetEnvironmentVariable("PROMPT") ?? "$P$G";
            environment["PROMPT"] = "$E]633;Ark;" + token + ";cwdraw;$P$E\\" + prompt;
            detail = "CMD 仅以 PROMPT 获取目录，标题使用原生 OSC；无可靠命令边界、退出码、等待事件、历史或耗时。";
        }
        else if (name == "bash")
        {
            if (arguments.Any(a => a is not ("--login" or "-l" or "-i" or "--noprofile" or "--norc")))
                return Disabled("Bash 自定义启动参数：未覆盖参数或注入，状态未知。");
            var script = Resource("bash.bash").Replace("__ARK_TOKEN__", token)
                .Replace("__ARK_LOGIN__", arguments.Any(a => a is "--login" or "-l") ? "1" : "0")
                .Replace("__ARK_PROFILE__", arguments.Contains("--noprofile") ? "0" : "1")
                .Replace("__ARK_RC__", arguments.Contains("--norc") ? "0" : "1");
            var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(script));
            var bashEnv = environment.GetValueOrDefault("BASH_ENV") ?? Environment.GetEnvironmentVariable("BASH_ENV");
            var saved = "ARK_BASH_ENV_" + token;
            if (bashEnv is not null) environment[saved] = bashEnv;
            environment["BASH_ENV"] = string.Empty;
            var restore = bashEnv is null ? "unset BASH_ENV; " : "export BASH_ENV=\"${" + saved + "}\"; unset " + saved + "; ";
            // The script travels in the approved command line, never through an elevation-writable rc file.
            arguments = ["--noprofile", "--norc", "-c", restore + "exec \"$BASH\" --rcfile <(printf '%s' '" + encoded + "' | base64 -d) -i"];
            capabilities |= ShellCapabilities.CommandBoundaries | ShellCapabilities.ExitStatus | ShellCapabilities.Prompt;
            detail = "Git Bash 4.4+ PS0 / PROMPT_COMMAND；启动适配器按参数加载 login / rc 脚本，但不保留 login_shell 标志。忽略或禁用 Shell 历史时命令文本未知。";
        }
        else return Disabled("此可执行文件没有适配器；未注入，状态未知。");
        var prepared = new SessionLaunchRequest
        {
            ProfileId = request.ProfileId, WorkspaceId = request.WorkspaceId, ExecutablePath = request.ExecutablePath,
            Arguments = arguments, Environment = environment, WorkingDirectory = request.WorkingDirectory,
            PostLaunchInput = request.PostLaunchInput, ScrollbackLines = request.ScrollbackLines,
            RunAsAdministrator = request.RunAsAdministrator, ShellIntegration = request.ShellIntegration, AgentRecipe = request.AgentRecipe
        };
        return new(prepared, token, name, capabilities, detail);
    }

    private static string Resource(string file)
    {
        using var stream = typeof(ShellIntegrationInjector).Assembly.GetManifestResourceStream("ArkTerminal.Terminal.Integration.Scripts." + file)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
