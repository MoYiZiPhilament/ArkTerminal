using System.Windows.Media;

namespace ArkTerminal.Rendering;

public sealed record TerminalVisualStyle(
    Color Background,
    Color Foreground,
    Color Selection,
    Color Search,
    Color Cursor,
    double BackgroundOpacity);
