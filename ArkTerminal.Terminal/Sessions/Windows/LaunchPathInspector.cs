namespace ArkTerminal.Terminal.Sessions.Windows;

public sealed record LaunchPathStatus(bool Ready, string? ExecutablePath, string? WorkingDirectory, string Message);

public static class LaunchPathInspector
{
    public static SessionLaunchRequest Resolve(SessionLaunchRequest request)
    {
        ShellLaunchAdapter.Validate(request);
        return ShellLaunchAdapter.ResolveRequest(request);
    }
    public static LaunchPathStatus Inspect(SessionLaunchRequest request)
    {
        string? executable = null;
        string? directory = null;
        try
        {
            executable = ShellLaunchAdapter.ResolveExecutablePath(request.ExecutablePath);
            directory = ShellLaunchAdapter.ResolveWorkingDirectory(request.WorkingDirectory);
            // Check path metadata; execution aliases need not support reading file contents.
            File.GetAttributes(executable);
            File.GetAttributes(directory);
            if (request.AgentRecipe is not null)
                return new(false, executable, directory, "Agent 配方请通过 AgentLauncher 校验适配器和实际 Shell 后端。");
            if (request.RunAsAdministrator && Elevation.ElevationSupport.UnavailableReason is { } reason)
                return new(false, executable, directory, reason);
            return new(true, executable, directory, "路径已解析；最终执行权限和启动结果以实际启动为准。" +
                (request.RunAsAdministrator ? " 管理员会话需确认本次目标并通过 Windows UAC。" : string.Empty));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or System.ComponentModel.Win32Exception or System.Security.SecurityException)
        {
            return new(false, executable, directory, "当前不可启动（允许保存配置）：" + exception.Message);
        }
    }
}
