using System.Text.RegularExpressions;

namespace ArkTerminal.Core.Recording;

public static class RecordingLimits
{
    public static readonly TimeSpan MaintenanceInterval = TimeSpan.FromHours(1);
    public const int QueueCapacity = 64;
    public const int MaxTextChunkCharacters = 32768;
    public const int MaxCommandCharacters = 4096;
    public const int MaxTranscriptFiles = 512;
    public const int MaxReadHistoryBytes = 16 * 1024 * 1024;
}

internal static class RecordingFiles
{
    // Only exact service-owned names are removed; never recurse or follow a reparse point.
    internal static void CheckDirectory(string directory)
    {
        for (var parent = new DirectoryInfo(directory); parent is not null; parent = parent.Parent)
            if (parent.Exists && (parent.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException(Localize.T("Text0672"));
        Directory.CreateDirectory(directory);
    }
    internal static void CheckFile(string path)
    {
        if (File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new IOException(Localize.T("Text0673"));
    }
    internal static List<FileInfo> Transcripts(string directory)
    {
        CheckDirectory(directory);
        return new DirectoryInfo(directory).EnumerateFiles("ark-output-*.txt")
            .Where(file => Regex.IsMatch(file.Name, @"^ark-output-[0-9a-f]{32}-[0-9a-f]{32}\.txt$", RegexOptions.CultureInvariant) &&
                (file.Attributes & FileAttributes.ReparsePoint) == 0).ToList();
    }
}
