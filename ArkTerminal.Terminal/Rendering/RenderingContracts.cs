namespace ArkTerminal.Terminal.Rendering;

public enum TerminalColorKind
{
    Default,
    Indexed,
    Rgb
}

public readonly record struct TerminalColor(TerminalColorKind Kind, byte Red, byte Green, byte Blue)
{
    public static TerminalColor Default => default;
    public static TerminalColor FromIndexed(byte index) => new(TerminalColorKind.Indexed, index, 0, 0);
    public static TerminalColor FromRgb(byte red, byte green, byte blue) => new(TerminalColorKind.Rgb, red, green, blue);
}

public sealed class TerminalRunSnapshot
{
    public required int StartColumn { get; init; }
    public required string Text { get; init; }
    public TerminalColor Foreground { get; init; }
    public TerminalColor Background { get; init; }
    public bool Bold { get; init; }
    public bool Underline { get; init; }
    public bool Inverse { get; init; }
}

public sealed class TerminalClusterSnapshot
{
    public required int StartColumn { get; init; }
    public required int ColumnWidth { get; init; }
    public required string Text { get; init; }
    public required bool IsOccupied { get; init; }
    public TerminalColor Foreground { get; init; }
    public TerminalColor Background { get; init; }
    public bool Bold { get; init; }
    public bool Underline { get; init; }
    public bool Inverse { get; init; }
}

public sealed class TerminalLineSnapshot
{
    public required long LineId { get; init; }
    public required string Text { get; init; }
    public bool IsSoftWrapped { get; init; }
    public IReadOnlyList<TerminalRunSnapshot> Runs { get; init; } = [];
    public IReadOnlyList<TerminalClusterSnapshot> Clusters { get; init; } = [];
}

public sealed class TerminalScreenSnapshot
{
    public required Guid SessionId { get; init; }
    public required long Generation { get; init; }
    public required int Columns { get; init; }
    public required int Rows { get; init; }
    public required int CursorColumn { get; init; }
    public required int CursorRow { get; init; }
    public bool CursorVisible { get; init; }
    public bool IsAlternateScreen { get; init; }
    public string ShellTitle { get; init; } = string.Empty;
    public int ScrollbackLines { get; init; }
    public int ScrollbackOffset { get; init; }
    public IReadOnlyList<TerminalLineSnapshot> Lines { get; init; } = [];
}

public sealed class TerminalLogicalLineSnapshot
{
    public required long LineId { get; init; }
    public required string Text { get; init; }
    public required bool IsSoftWrapped { get; init; }
    public IReadOnlyList<TerminalClusterSnapshot> Clusters { get; init; } = [];
}

public sealed class TerminalBufferTextSnapshot
{
    public required Guid SessionId { get; init; }
    public required long Generation { get; init; }
    public required bool IsAlternateScreen { get; init; }
    public IReadOnlyList<TerminalLogicalLineSnapshot> Lines { get; init; } = [];
}

public sealed class TerminalScreenChangedEventArgs : EventArgs
{
    public required Guid SessionId { get; init; }
    public required long Generation { get; init; }
    public int DirtyStartRow { get; init; }
    public int DirtyEndRow { get; init; }
}

public interface IScreenSnapshotProvider
{
    event EventHandler<TerminalScreenChangedEventArgs>? SnapshotChanged;
    TerminalScreenSnapshot GetSnapshot();
}

public interface ITerminalBufferReader : IScreenSnapshotProvider
{
    TerminalScreenSnapshot GetSnapshot(int scrollbackOffset);
    TerminalBufferTextSnapshot CaptureText();
}

public interface ITerminalInput
{
    Task SubmitInputAsync(ReadOnlyMemory<char> input, CancellationToken cancellationToken);
}

public interface ITerminalRenderer
{
    void Render(TerminalScreenSnapshot snapshot);
}
