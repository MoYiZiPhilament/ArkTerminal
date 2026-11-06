using System.Text;
using System.Text.Json;
using ArkTerminal.Core.Profiles;
using ArkTerminal.Terminal.Sessions;
using ArkTerminal.Terminal.Sessions.Windows;

namespace ArkTerminal.Terminal.Agents;

public sealed record AgentShellChoice(string ExecutablePath, AgentShellBackend Backend, bool IsFallback, string Description);

public static class AgentPowerShell
{
    public static AgentShellChoice ResolveBackend(AgentShellBackend requested, string? searchPath = null)
    {
        string? Find(string name)
        {
            foreach (var directory in (searchPath ?? Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator).Take(128))
            {
                if (string.IsNullOrWhiteSpace(directory)) continue;
                try
                {
                    if (!Path.IsPathFullyQualified(directory.Trim('"'))) continue;
                    var path = Path.Combine(directory.Trim('"'), name);
                    if (File.Exists(path)) return Path.GetFullPath(path);
                }
                catch (ArgumentException) { }
            }
            return null;
        }
        if (requested == AgentShellBackend.PowerShell7 && Find("pwsh.exe") is { } pwsh)
            return new(pwsh, AgentShellBackend.PowerShell7, false, "PowerShell 7（pwsh.exe）");
        var powershell = Find("powershell.exe") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "WindowsPowerShell", "v1.0", "powershell.exe");
        if (!File.Exists(powershell)) throw new FileNotFoundException(Localize.T("Text0474"));
        var fallback = requested == AgentShellBackend.PowerShell7;
        return new(powershell, AgentShellBackend.WindowsPowerShell, fallback, fallback ? Localize.T("Text0475") : Localize.T("Text0476"));
    }

    public static IReadOnlyList<string> BuildShellArguments(string executable, IReadOnlyList<string> arguments)
    {
        if (!Path.IsPathFullyQualified(executable) || executable.Any(char.IsControl) || arguments.Any(value => value.Contains('\0')))
            throw new ArgumentException(Localize.T("Text0477"));
        var extension = Path.GetExtension(executable).ToLowerInvariant();
        string script;
        if (extension == ".ps1")
        {
            var payload = Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(new { executable, arguments }));
            // Data never becomes source code. Splatting preserves PowerShell script argument objects on PS5 and PS7.
            script = "$ErrorActionPreference='Stop'; $d=ConvertFrom-Json ([Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('" + payload + "'))); " +
                "$a=@($d.arguments); try { & $d.executable @a; if ($null -ne $LASTEXITCODE) { exit $LASTEXITCODE }; if (-not $?) { exit 1 } } catch { [Console]::Error.WriteLine('Agent script failed.'); exit 1 }";
        }
        else
        {
            string fileName;
            string commandLine;
            if (extension == ".exe")
            {
                fileName = executable;
                commandLine = WindowsCommandLine.Build("_", arguments)[1..].TrimStart();
            }
            else if (extension == ".cmd")
            {
                // Batch files may reparse %*. Reject characters whose meaning depends on their contents.
                // Semicolons, dollars, backticks, apostrophes, Unicode and spaces are literal inside these quotes.
                if (arguments.Prepend(executable).Any(value => value.IndexOfAny(['%', '!', '^', '&', '|', '<', '>', '\r', '\n']) >= 0))
                    throw new ArgumentException(Localize.T("Text0478"));
                fileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "cmd.exe");
                commandLine = "/d /v:off /s /c \"\"" + executable + "\" " + string.Join(" ", arguments.Select(value => WindowsCommandLine.QuoteArgument(value, forCmd: true))) + "\"";
            }
            else throw new ArgumentException(Localize.T("Text0479"));
            var payload = Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(new { fileName, commandLine }));
            // ProcessStartInfo.Arguments uses Windows argv rules directly, including on Windows PowerShell 5.
            // It also leaves console descriptors inherited, so interactive I/O stays inside the same ConPTY.
            script = "$ErrorActionPreference='Stop'; $d=ConvertFrom-Json ([Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('" + payload + "'))); " +
                "$s=New-Object Diagnostics.ProcessStartInfo; $s.FileName=$d.fileName; $s.Arguments=$d.commandLine; $s.UseShellExecute=$false; " +
                "try { $p=[Diagnostics.Process]::Start($s); $p.WaitForExit(); $code=$p.ExitCode; $p.Dispose(); exit $code } catch { [Console]::Error.WriteLine('Agent process failed to start.'); exit 1 }";
        }
        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
        if (encoded.Length > 30000) throw new ArgumentException(Localize.T("Text0480"));
        return ["-NoLogo", "-NoProfile", "-EncodedCommand", encoded];
    }

    internal static SessionLaunchRequest Wrap(SessionLaunchRequest request, string executable, IReadOnlyList<string> arguments, AgentShellChoice shell) => new()
    {
        ProfileId = request.ProfileId, WorkspaceId = request.WorkspaceId, ExecutablePath = shell.ExecutablePath,
        Arguments = BuildShellArguments(executable, arguments), WorkingDirectory = request.WorkingDirectory,
        Environment = request.Environment, PostLaunchInput = request.PostLaunchInput, ScrollbackLines = request.ScrollbackLines,
        RunAsAdministrator = request.RunAsAdministrator, ShellIntegration = ShellIntegrationMode.Disabled
    };
}
