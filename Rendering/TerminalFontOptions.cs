namespace ArkTerminal.Rendering;

public sealed record TerminalFontOptions
{
    public static TerminalFontOptions Resolve(Core.Configuration.AppearanceSettings? appearance, Core.Profiles.TerminalProfile? profile = null)
    {
        appearance ??= new();
        var local = profile?.Terminal;
        return new()
        {
            FontFamily = local is { UseGlobalFont: false } ? local.FontFamily : appearance.TerminalFontFamily,
            FontSize = local is { UseGlobalFont: false } ? local.FontSize : appearance.TerminalFontSize,
            FontWeight = appearance.TerminalFontWeight,
            LineHeightMultiplier = appearance.TerminalLineHeight,
            CharacterSpacing = appearance.TerminalCharacterSpacing,
            Ligatures = appearance.TerminalLigatures
        };
    }
    public string FontFamily { get; init; } = "Cascadia Mono";
    public double FontSize { get; init; } = 14;
    public int FontWeight { get; init; } = 400;
    public double LineHeightMultiplier { get; init; } = 1.15;
    public double CharacterSpacing { get; init; }
    public bool Ligatures { get; init; }
}

public sealed class TerminalViewLimits
{
    public int MaxCachedLayouts { get; init; } = 512;
    public int MaxTrueColorBrushes { get; init; } = 256;
    public int MaxSearchResults { get; init; } = 1000;
    public TimeSpan SearchDebounce { get; init; } = TimeSpan.FromMilliseconds(250);
}
