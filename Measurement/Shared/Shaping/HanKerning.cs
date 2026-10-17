/*
 * Copyright 2016, 2023 The Chromium Authors
 * Adapted from Blink's han_kerning.cc/.h and SetHanKerning in
 * character_property_data_generator.cc.
 */

using System;
using HarfBuzzSharp;
using Icu;

namespace MessageHeightTwitch.Measurement.Shared.Shaping;

/// <summary>
/// Horizontal text-spacing-trim for the library's fixed en-US text style. Among the bundled fonts only
/// Yu Gothic has alternate half widths (halt); none has contextual half-width spacing (chws).
/// </summary>
internal sealed class HanKerning
{
    internal enum CHAR_TYPE : byte
    {
        OTHER,
        OPEN,
        CLOSE,
        MIDDLE,
        OPEN_NARROW,
        CLOSE_NARROW
    }

    private static readonly Tag HALT = new('h', 'a', 'l', 't'); // alternate half widths

    /*
     * Blink classifies the font's dots, colon and semicolon by where their ink sits within the advance,
     * shaping a punctuation sample with halt. For Yu Gothic, the only bundled font with halt, the dots
     * 、。，． have their ink in the left half (CLOSE) and the colon and semicolon are narrow and centered
     * (MIDDLE), in both weights. Its quotes are proportional, never fullwidth (Blink's is_quote_fullwidth).
     */
    private const CHAR_TYPE DOT = CHAR_TYPE.CLOSE;
    private const CHAR_TYPE COLON = CHAR_TYPE.MIDDLE;
    private const CHAR_TYPE SEMICOLON = CHAR_TYPE.MIDDLE;

    private readonly bool HasAlternateSpacing;

    internal HanKerning(OpenType Tables) => HasAlternateSpacing = Tables.HasFeature(HALT);

    /// <summary>
    /// Check for whether the text contains any character that <see cref="GetCharType" />
    /// may classify as something other than <see cref="CHAR_TYPE.OTHER" />.
    /// </summary>
    /// <param name="Text">The text to check.</param>
    /// <returns>True if any character may need Han kerning.</returns>
    internal static bool MayApply(ReadOnlySpan<char> Text)
    {
        foreach (var ch in Text) {
            if (ch is >= '\u2018' and <= '\u301f' or >= '\uff08' and <= '\uff60')
                return true;
        }
        return false;
    }

    // Character::MaybeHanKerningClose: kClose or kCloseQuote, independently
    // of the current fallback font's ability to apply alternate spacing.
    internal static bool MaybeClose(char Ch) =>
        Ch is '\u2019' or '\u201D' ||
        Ch is >= '\u2018' and <= '\u301F' or >= '\uFF08' and <= '\uFF60' &&
        Character.GetCharType(Ch) == Character.UCharCategory.END_PUNCTUATION &&
        (Ch is >= '\u3000' and <= '\u303F' ||
            Character.GetIntPropertyValue(Ch, Character.UProperty.EAST_ASIAN_WIDTH) == (int)Character.UEastAsianWidth.FULLWIDTH
        );

    internal CHAR_TYPE GetCharType(int Ch)
    {
        switch (Ch) {
            case 0x2018: // ‘
            case 0x201C: // “
                return CHAR_TYPE.OPEN_NARROW;
            case 0x2019: // ’
            case 0x201D: // ”
                return CHAR_TYPE.CLOSE_NARROW;
            case 0x3000: // ideographic space
            case 0x00B7: // ·
            case 0x2027: // ‧
            case 0x30FB: // ・
                return CHAR_TYPE.MIDDLE;
            case 0x3001: // 、
            case 0x3002: // 。
            case 0xFF0C: // ，
            case 0xFF0E: // ．
                return DOT;
            case 0xFF1A: // ：
                return COLON;
            case 0xFF1B: // ；
                return SEMICOLON;
        }

        var category = Character.GetCharType(Ch);
        var isOpen = category == Character.UCharCategory.START_PUNCTUATION;
        var isClose = category == Character.UCharCategory.END_PUNCTUATION;
        if (!isOpen && !isClose)
            return CHAR_TYPE.OTHER;

        var fullwidth = Ch is >= 0x3000 and <= 0x303F || Character.GetIntPropertyValue(Ch, Character.UProperty.EAST_ASIAN_WIDTH) == (int)Character.UEastAsianWidth.FULLWIDTH;
        if (isOpen)
            return fullwidth ? CHAR_TYPE.OPEN : CHAR_TYPE.OPEN_NARROW;

        return fullwidth ? CHAR_TYPE.CLOSE : CHAR_TYPE.CLOSE_NARROW;
    }

    /// <summary>
    /// Whether an opening character is trimmed because of the character before it
    /// </summary>
    /// <param name="Type">Type of the opening character.</param>
    /// <param name="Previous">Type of the character before it.</param>
    /// <returns>True if the opening character should be kerned.</returns>
    private static bool ShouldKern(CHAR_TYPE Type, CHAR_TYPE Previous) => Type == CHAR_TYPE.OPEN && Previous is CHAR_TYPE.OPEN or CHAR_TYPE.MIDDLE or CHAR_TYPE.CLOSE or CHAR_TYPE.OPEN_NARROW;

    /// <summary>
    /// Whether a closing character is trimmed because of the character after it
    /// </summary>
    /// <param name="Next">Type of the character after it.</param>
    /// <param name="Type">Type of the closing character.</param>
    /// <returns>True if the closing character should be kerned.</returns>
    private static bool ShouldKernLast(CHAR_TYPE Next, CHAR_TYPE Type) => Type == CHAR_TYPE.CLOSE && Next is CHAR_TYPE.CLOSE or CHAR_TYPE.MIDDLE or CHAR_TYPE.CLOSE_NARROW;

    /// <summary>
    /// Whether a character of <paramref name="Type"/> after one of <paramref name="Previous"/> is kerned because of it,
    /// or kerns that one: Blink's HanKerning marks it unsafe to break before, since a line break between them changes
    /// both glyphs.
    /// </summary>
    internal bool UnsafeBefore(CHAR_TYPE Type, CHAR_TYPE Previous) => HasAlternateSpacing && (ShouldKern(Type, Previous) || ShouldKernLast(Type, Previous));

    /// <summary>
    /// Writes the features for shaping Text[Run] and returns their count.
    /// <paramref name="ActualTypes"/> holds <see cref="GetCharType"/> results for
    /// the whole text, so neighbors outside the run are considered too.
    /// </summary>
    /// <param name="Text">The full text.</param>
    /// <param name="Run">The run within <paramref name="Text"/>.</param>
    /// <param name="ActualTypes">Character types for the whole text, indexed like <paramref name="Text"/>.</param>
    /// <param name="Features">Receives the features to apply.</param>
    /// <param name="ClusterOffset">Subtracted from text indices to get cluster values for the run.</param>
    /// <param name="LineStart">The run starts a wrapped line, so the character before it is on another line (Blink's
    /// is_line_start).</param>
    /// <returns>The number of features written to <paramref name="Features"/>.</returns>
    internal int GetFeatures(ReadOnlySpan<char> Text, Range Run, ReadOnlySpan<CHAR_TYPE> ActualTypes, Span<Feature> Features, int ClusterOffset, bool LineStart = false, bool HanKerningEnd = false)
    {
        if (!HasAlternateSpacing || !MayApply(Text[Run]))
            return 0;

        var (start, length) = Run.GetOffsetAndLength(Text.Length);
        var count = 0;
        for (var x = start; x < start + length; x++) {
            var type = GetCharType(Text[x]);
            var kern = x > 0 && !(LineStart && x == start) && ShouldKern(type, ActualTypes[x - 1]);
            kern |= x + 1 < Text.Length && ShouldKernLast(ActualTypes[x + 1], type);
            kern |= HanKerningEnd && x == start + length - 1;
            if (kern) {
                var cluster = (uint)(x - ClusterOffset);
                Features[count++] = new Feature(HALT, 1, cluster, cluster + 1);
            }
        }
        return count;
    }
}
