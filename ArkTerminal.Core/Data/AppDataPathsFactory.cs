namespace ArkTerminal.Core.Data;

public static class AppDataPathsFactory
{
    public static async Task<AppDataPathsResult> CreateAsync(
        IEnumerable<string> arguments,
        IPackagingContext packagingContext,
        CancellationToken cancellationToken = default)
    {
        string? packageFamilyName;
        try
        {
            packageFamilyName = packagingContext.GetPackageFamilyName();
        }
        catch (Exception exception)
        {
            return Error(null, $"无法检测应用包身份：{exception.Message}");
        }

        var portableRequested = arguments.Contains("--portable", StringComparer.OrdinalIgnoreCase);
        if (packageFamilyName is not null && portableRequested)
        {
            return Error(AppContext.BaseDirectory, "MSIX 包身份下不适用便携模式；未写入其他数据目录。");
        }

        AppDataMode mode;
        string root;
        try
        {
            if (packageFamilyName is not null)
            {
                mode = AppDataMode.Msix;
                root = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Packages",
                    packageFamilyName,
                    "LocalState",
                    "ArkTerminal");
            }
            else if (portableRequested || File.Exists(Path.Combine(AppContext.BaseDirectory, "portable.flag")))
            {
                mode = AppDataMode.Portable;
                root = Path.Combine(AppContext.BaseDirectory, "data");
            }
            else
            {
                mode = AppDataMode.Standard;
                root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ArkTerminal");
            }
        }
        catch (Exception exception)
        {
            return Error(AppContext.BaseDirectory, $"无法确定数据目录：{exception.Message}");
        }

        var paths = new AppDataPaths(mode, Path.GetFullPath(root));
        try
        {
            await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                foreach (var directory in paths.RequiredDirectories())
                {
                    Directory.CreateDirectory(directory);
                }

                var probe = Path.Combine(paths.RootDirectory, $".write-probe-{Guid.NewGuid():N}.tmp");
                using (var stream = new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    stream.WriteByte(0);
                    stream.Flush(true);
                }
                File.Delete(probe);
            }, cancellationToken);
            return new AppDataPathsResult { Paths = paths };
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return Error(paths.RootDirectory, $"数据目录不可写：{exception.Message}");
        }
    }

    private static AppDataPathsResult Error(string? path, string message) => new() { ErrorPath = path, ErrorMessage = message };

}
