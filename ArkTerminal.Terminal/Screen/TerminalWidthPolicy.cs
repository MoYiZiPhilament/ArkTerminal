using System.Globalization;
using System.Text;

namespace ArkTerminal.Terminal.Screen;

public static class TerminalWidthPolicy
{
    public const string PolicyVersion = "Unicode 16.0 width/emoji data; .NET StringInfo grapheme segmentation; Wide/Fullwidth=2, Ambiguous=1, emoji presentation/emoji ZWJ/RI pairs=2, single RI=1";

    public static int GetColumnWidth(string textElement)
    {
        if (string.IsNullOrEmpty(textElement))
        {
            return 0;
        }

        var hasBase = false;
        var wide = false;
        var hasEmojiPresentation = false;
        var currentEmojiPresentation = false;
        var pictographicBase = false;
        var joinerAfterPictographic = false;
        var emojiJoiner = false;
        var keycapBase = false;
        var previousValue = -1;
        var regionalIndicators = 0;
        foreach (var rune in textElement.EnumerateRunes())
        {
            var preceding = previousValue;
            previousValue = rune.Value;
            var category = Rune.GetUnicodeCategory(rune);
            if (rune.Value == 0x200D)
            {
                joinerAfterPictographic = pictographicBase;
                continue;
            }
            if (rune.Value is >= 0xFE00 and <= 0xFE0F || rune.Value is >= 0xE0100 and <= 0xE01EF)
            {
                if (Contains(UnicodeWidthData.EmojiVariationBase, preceding))
                {
                    currentEmojiPresentation = rune.Value == 0xFE0F;
                }
                continue;
            }
            if (rune.Value == 0x20E3 && keycapBase)
            {
                hasEmojiPresentation = true;
            }
            if (rune.Value is >= 0x1F1E6 and <= 0x1F1FF)
            {
                regionalIndicators++;
                hasBase = true;
                continue;
            }
            if (category is UnicodeCategory.NonSpacingMark or UnicodeCategory.EnclosingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.Format)
            {
                continue;
            }
            hasBase = true;
            wide |= Contains(UnicodeWidthData.Wide, rune.Value);
            hasEmojiPresentation |= currentEmojiPresentation;
            currentEmojiPresentation = Contains(UnicodeWidthData.EmojiPresentation, rune.Value);
            pictographicBase = Contains(UnicodeWidthData.ExtendedPictographic, rune.Value);
            emojiJoiner |= joinerAfterPictographic && pictographicBase;
            joinerAfterPictographic = false;
            keycapBase = rune.Value is '#' or '*' or >= '0' and <= '9';
        }

        if (!hasBase)
        {
            return 0;
        }
        return wide || hasEmojiPresentation || currentEmojiPresentation || emojiJoiner || regionalIndicators == 2 ? 2 : 1;
    }

    private static bool Contains(ReadOnlySpan<int> ranges, int value)
    {
        var low = 0;
        var high = ranges.Length / 2 - 1;
        while (low <= high)
        {
            var middle = (low + high) / 2;
            if (value < ranges[middle * 2])
            {
                high = middle - 1;
            }
            else if (value > ranges[middle * 2 + 1])
            {
                low = middle + 1;
            }
            else
            {
                return true;
            }
        }
        return false;
    }
}
