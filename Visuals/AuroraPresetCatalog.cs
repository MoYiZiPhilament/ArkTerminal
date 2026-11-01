using System.Windows.Media;
using ArkTerminal.Core.Configuration;

namespace ArkTerminal.Visuals;

public sealed record AuroraPreset(string Id, string DisplayName, IReadOnlyList<Color> Colors);

public static class AuroraPresetCatalog
{
    public static IReadOnlyList<AuroraPreset> Presets { get; } =
    [
        new("borealis", "borealis · 青紫", [Parse("#5B4BDB"), Parse("#168AAD"), Parse("#52B788")]),
        new("dawn", "dawn · 晨曦", [Parse("#E76F51"), Parse("#E9A23B"), Parse("#C85C8E")]),
        new("ocean", "ocean · 深海", [Parse("#125E8A"), Parse("#2574A9"), Parse("#42C2B3")]),
        new("ember", "ember · 余烬", [Parse("#9D4EDD"), Parse("#D45050"), Parse("#F28C45")])
    ];

    public static IReadOnlyList<Color> Resolve(VisualSettings settings)
    {
        if (settings.AuroraPresetId.Equals("custom", StringComparison.OrdinalIgnoreCase))
        {
            return settings.AuroraCustomColors.Select(Parse).ToArray();
        }
        return Presets.FirstOrDefault(preset => preset.Id.Equals(settings.AuroraPresetId, StringComparison.OrdinalIgnoreCase))?.Colors
            ?? Presets[0].Colors;
    }

    private static Color Parse(string value) => ColorConverter.ConvertFromString(value) is Color color ? color : Colors.SlateBlue;
}
