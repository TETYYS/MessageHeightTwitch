using System;
using System.Runtime.CompilerServices;
using System.Text;

namespace MessageHeightTwitch;

/// <summary>
/// A CSS unicode-range candidate and its upright and italic typefaces at regular and bold weights.
/// </summary>
public class FontDesc(Range[] Ranges, string Font, string BoldFont)
{
    public Range[] Ranges { get; } = Ranges;
    public string Font { get; } = Font;
    public Typeface TypeFace { get; set; } = null!;
    public string BoldFont { get; } = BoldFont;
    public Typeface BoldTypeFace { get; set; } = null!;
    internal Typeface ItalicTypeFace { get; set; } = null!;
    internal Typeface BoldItalicTypeFace { get; set; } = null!;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool RangeMatch(Rune In) => InRanges(Ranges, In.Value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool InRanges(Range[] Ranges, int Codepoint)
    {
        foreach (var range in Ranges)
            if (range.Start.Value <= Codepoint && Codepoint <= range.End.Value)
                return true;
        return false;
    }

    /// <summary>
    /// Blink's FontFallbackIterator::RangeSetContributesForHint: one character in the range makes this subset a
    /// candidate. Its range-limited HarfBuzz font decides whether shaping, including normalization, covers the text.
    /// </summary>
    public bool RangeMatch(ReadOnlySpan<char> In)
    {
        foreach (var rune in In.EnumerateRunes())
            if (RangeMatch(rune))
                return true;
        return false;
    }

    public Typeface GetTypeFace(bool Bold) => GetTypeFace(Bold, false);

    internal Typeface GetTypeFace(bool Bold, bool Italic) => Italic
        ? Bold ? BoldItalicTypeFace : ItalicTypeFace
        : Bold ? BoldTypeFace : TypeFace;
}
