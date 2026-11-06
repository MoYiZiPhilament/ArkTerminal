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
                return new(false, executable, directory, Localize.T("Text0533"));
            if (request.RunAsAdministrator && Elevation.ElevationSupport.UnavailableReason is { } reason)
                return new(false, executable, directory, reason);
            return new(true, executable, directory, Localize.T("Text0534") +
                (request.RunAsAdministrator ? Localize.T("Text0535") : string.Empty));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or System.ComponentModel.Win32Exception or System.Security.SecurityException)
        {
            return new(false, executable, directory, Localize.T("Text0536") + exception.Message);
        }
    }
}
