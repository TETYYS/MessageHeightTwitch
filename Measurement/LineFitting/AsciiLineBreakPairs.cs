using System;
using System.Text;

namespace MessageHeightTwitch.Measurement.LineFitting;

/// <summary>
/// Blink's ShouldBreakFast result for a pair of characters
/// </summary>
internal enum FastBreak
{
    NoBreak,
    Break,
    // The fast table does not cover the pair: ICU's line breaking decides.
    Icu
}

/// <summary>
/// Blink's fast line break table (character_property_data_generator.cc, LineBreakData) for pairs of characters from
/// '!' to U+00FF: ICU's line breaking for each pair, with printable ASCII pairs replaced by Blink's compatibility rules
/// (breaks before '(' '&lt;' '[' '{' and after '-' '?', with exceptions).
/// </summary>
/// <remarks>
/// Cache usage: one process-wide 223 x 223 byte table (49,729 bytes, about 48.6 KiB, excluding its header).
/// </remarks>
internal static class AsciiLineBreakPairs
{
    private const char MIN_CHAR = '!';
    private const char MAX_CHAR = '\u00FF';
    private const int COUNT = MAX_CHAR - MIN_CHAR + 1;
    // Whether each pair breaks; computed pairs are the same on every thread.
    private static readonly CachedBool[] Pairs = new CachedBool[COUNT * COUNT];

    /// <summary>Blink's ShouldBreakFast.</summary>
    internal static FastBreak Fast(char LastLast, char Last, char Ch, LineBreakingWorkspace Workspace)
    {
        if (Last < MIN_CHAR || Ch < MIN_CHAR)
            return FastBreak.NoBreak;

        if (Last == '-') {
            if (Ch > 0x7F)
                return FastBreak.Icu;
            if (Ch is >= '0' and <= '9')
                return char.IsAsciiLetterOrDigit(LastLast) ? FastBreak.Break : FastBreak.NoBreak;
        }

        if (Last > MAX_CHAR || Ch > MAX_CHAR)
            return FastBreak.Icu;

        ref var pair = ref Pairs[(Last - MIN_CHAR) * COUNT + Ch - MIN_CHAR];

        if (pair == CachedBool.Unknown)
            pair = Compute(Last, Ch, Workspace) ? CachedBool.True : CachedBool.False;
        return pair == CachedBool.True ? FastBreak.Break : FastBreak.NoBreak;
    }

    /// <summary>
    /// Whether a line can break between the end of a text and an emoji image after it. Chrome 155 computes a break
    /// opportunity where the image's bidi isolate starts (LineBreaker::HandleBidiControlItem with
    /// LineBreakBidiControlEnter), and LazyLineBreakIterator gives ICU's line breaking between the text and the image's
    /// U+FFFC: a break unless the text ends in a character that holds on to what follows (an opening bracket, a
    /// quotation mark, a no-break space, a word joiner).
    /// </summary>
    internal static bool BreaksBeforeObject(ReadOnlySpan<char> Text, LineBreakingWorkspace Workspace)
    {
        if (Text[^1] is ' ' or '\t' or '\n')
            return true;

        // A few characters of context, starting at a whole code point.
        var start = Math.Max(0, Text.Length - 4);
        if (start > 0 && char.IsLowSurrogate(Text[start]))
            start--;

        var context = Text[start..];
        Span<char> buffer = stackalloc char[context.Length + 1];
        context.CopyTo(buffer);

        buffer[^1] = (char)0xFFFC;
        var boundaries = Workspace.PairBoundaries;
        Workspace.Iterators.GetBreaks(buffer, false, boundaries);

        foreach (var boundary in boundaries)
            if (boundary.End == context.Length)
                return true;

        return false;
    }

    // UAX #14 line breaking classes, as ICU numbers them.
    private const int ALPHABETIC = 2;
    private const int BREAK_AFTER = 4;
    private const int COMBINING_MARK = 9;

    /*
     * Blink's kBreakAllLineBreakClassTable for word-break: break-all (text_break_iterator.cc): a row of 7 bytes for the
     * class before a break, one bit for the class after it, the first class in the high bit. A set bit adds a break; a
     * clear one leaves normal line breaking to decide.
     */
    private const int BREAK_ALL_ROW_BYTES = 7;
    private static readonly byte[] BREAK_ALL_TABLE = [
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, // XX
        0x68, 0x04, 0x1A, 0x80, 0x02, 0x00, 0x00, // AI
        0x68, 0x04, 0x1A, 0x80, 0x02, 0x00, 0x00, // AL
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, // B2
        0x68, 0x04, 0x1A, 0x80, 0x02, 0x00, 0x00, // BA
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, // BB
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, // BK
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, // CB
        0x68, 0x04, 0x12, 0x00, 0x02, 0x00, 0x00, // CL
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, // CM
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, // CR
        0x68, 0x04, 0x16, 0x00, 0x02, 0x00, 0x00, // EX
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, // GL
        0x00, 0x00, 0x10, 0x00, 0x00, 0x00, 0x00, // HY
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, // ID
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, // IN
        0x68, 0x04, 0x10, 0x00, 0x02, 0x00, 0x00, // IS
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, // LF
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, // NS
        0x68, 0x04, 0x1A, 0x80, 0x02, 0x00, 0x00, // NU
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, // OP
        0x68, 0x04, 0x16, 0x00, 0x02, 0x00, 0x00, // PO
        0x00, 0x00, 0x04, 0x00, 0x00, 0x00, 0x00, // PR
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, // QU
        0x68, 0x04, 0x1A, 0x80, 0x02, 0x00, 0x00, // SA
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, // SG
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, // SP
        0x68, 0x04, 0x1A, 0x80, 0x02, 0x00, 0x00, // SY
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, // ZW
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, // NL
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, // WJ
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, // H2
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, // H3
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, // JL
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, // JT
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, // JV
        0x68, 0x04, 0x12, 0x80, 0x02, 0x00, 0x00, // CP
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, // CJ
        0x68, 0x04, 0x1A, 0x80, 0x02, 0x00, 0x00, // HL
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, // RI
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, // EB
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, // EM
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, // ZWJ
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, // AK
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, // AP
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, // AS
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, // VF
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, // VI
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, // HH
    ];

    /// <summary>
    /// Blink's ShouldBreakAfterBreakAll at normal strictness: whether word-break: break-all adds a break before
    /// <paramref name="Text"/>[<paramref name="Index"/>], a code point's start. The class before is the last one that
    /// is not a combining mark.
    /// </summary>
    internal static bool BreaksAll(ReadOnlySpan<char> Text, int Index)
    {
        Rune.DecodeFromUtf16(Text[Index..], out var rune, out _);

        var after = LineBreakClass(rune.Value);
        var before = COMBINING_MARK;

        for (var x = Index; x > 0 && before == COMBINING_MARK;) {
            Rune.DecodeLastFromUtf16(Text[..x], out var previous, out var length);
            before = LineBreakClass(previous.Value);
            x -= length;
        }

        if (before * BREAK_ALL_ROW_BYTES >= BREAK_ALL_TABLE.Length || after >= BREAK_ALL_ROW_BYTES * 8 || (BREAK_ALL_TABLE[before * BREAK_ALL_ROW_BYTES + after / 8] & (0x80 >> (after % 8))) == 0)
            return false;

        // LB21: no break before a Break After character, except U+007C.
        return after != BREAK_AFTER || rune.Value == '|';
    }

    /// <summary>Blink's LineBreakPropertyValue: ICU's line breaking class, with '+' tailored to AL as IE does.</summary>
    private static int LineBreakClass(int Codepoint) => Codepoint == '+' ? ALPHABETIC : Icu.Character.GetIntPropertyValue(Codepoint, Icu.Character.UProperty.LINE_BREAK);

    private static bool Compute(char Last, char Ch, LineBreakingWorkspace Workspace)
    {
        if (Last <= 0x7F && Ch <= 0x7F)
            return Ascii(Last, Ch);

        Span<char> text = [Last, Ch];

        var boundaries = Workspace.PairBoundaries;
        Workspace.Iterators.GetBreaks(text, false, boundaries);
        return boundaries.Count > 1;
    }

    /// <summary>
    /// LineBreakData::FillAscii, in its order: later rules override earlier ones. Its rule against breaking
    /// between '-' and a digit is not needed here: Fast decides that pair from the character before the '-'.
    /// </summary>
    private static bool Ascii(char A, char B)
    {
        var value = B is '(' or '<' or '[' or '{' || A is '-' or '?';

        if (A == '-' && B == '$')
            value = false;
        if (B == '!')
            value = false;
        if (A == '?' && B is '"' or '\'')
            value = false;
        if (B is ')' or ',' or '.' or '/' or ':' or ';' or '?' or ']' or '}')
            value = false;
        if (A is '$' or '\'' or '(' or '/' or >= '0' and <= '9' or '<' or '@' or >= 'A' and <= 'Z' or '[' or >= '^' and <= '`' or >= 'a' and <= 'z' or '{' or '\u007F')
            value = false;

        return value;
    }
}
