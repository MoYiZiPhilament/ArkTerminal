using System.Collections;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace ArkTerminal.Terminal.Sessions.Windows;

internal static class ShellLaunchAdapter
{
    internal static void Validate(SessionLaunchRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.RunAsAdministrator)
        {
            throw new PlatformNotSupportedException("管理员会话将在阶段 7 提供；未启动进程。");
        }
        if (string.IsNullOrWhiteSpace(request.ExecutablePath))
        {
            throw new ArgumentException("可执行文件不能为空。", nameof(request));
        }
        if (request.ExecutablePath.Contains('\0') || request.WorkingDirectory.Contains('\0'))
        {
            throw new ArgumentException("启动路径包含非法 NUL 字符。", nameof(request));
        }
        foreach (var argument in request.Arguments)
        {
            if (argument.Contains('\0'))
            {
                throw new ArgumentException("进程参数包含非法 NUL 字符。", nameof(request));
            }
        }
    }

    internal static string ResolveExecutablePath(string executablePath)
    {
        var expanded = Environment.ExpandEnvironmentVariables(executablePath);
        if (Path.IsPathRooted(expanded))
        {
            var fullPath = Path.GetFullPath(expanded);
            if (!File.Exists(fullPath))
            {
                throw new FileNotFoundException("找不到 Shell 可执行文件。", fullPath);
            }
            return fullPath;
        }

        var buffer = new StringBuilder(32768);
        var length = NativeMethods.SearchPathW(null, expanded, null, (uint)buffer.Capacity, buffer, IntPtr.Zero);
        if (length == 0 || length >= buffer.Capacity)
        {
            throw new FileNotFoundException($"找不到 Shell 可执行文件：{expanded}", expanded);
        }
        return buffer.ToString();
    }

    internal static string ResolveWorkingDirectory(string workingDirectory)
    {
        var expanded = Environment.ExpandEnvironmentVariables(workingDirectory);
        var fullPath = Path.GetFullPath(expanded);
        if (!Directory.Exists(fullPath))
        {
            throw new DirectoryNotFoundException($"启动目录不存在：{fullPath}");
        }
        return fullPath;
    }

    internal static string BuildEnvironmentBlock(IReadOnlyDictionary<string, string> overrides)
    {
        var environment = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            if (entry.Key is string name && entry.Value is string value)
            {
                environment[name] = value;
            }
        }
        foreach (var (name, value) in overrides)
        {
            if (string.IsNullOrEmpty(name) || name.Contains('=') || name.Contains('\0') || value.Contains('\0'))
            {
                throw new ArgumentException("环境变量名或值无效。", nameof(overrides));
            }
            environment[name] = value;
        }

        var builder = new StringBuilder();
        foreach (var pair in environment.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
        {
            builder.Append(pair.Key).Append('=').Append(pair.Value).Append('\0');
        }
        return builder.Append('\0').ToString();
    }
}
