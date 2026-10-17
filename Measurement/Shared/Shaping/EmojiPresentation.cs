using System;
using System.Collections.Generic;
using System.Text;

using Icu;

namespace MessageHeightTwitch.Measurement.Shared.Shaping;

/*
 * Presentation is independent of addon emoji recognition and Unicode blocks.
 * Matches Chromium's emoji-segmenter 0.3.0 grammar (955936be):
 * https://github.com/google/emoji-segmenter/blob/955936be8b391e00835257059607d7c5b72ce744/emoji_presentation_scanner.rl
 */
/// <summary>
/// Segments text by Unicode emoji presentation priority
/// </summary>
internal static class EmojiPresentation
{
    internal enum PRIORITY
    {
        TEXT,
        EMOJI,
        TEXT_WITH_VS,
        EMOJI_WITH_VS
    }

    private const int TEXT_VARIATION_SELECTOR = 0xFE0E;
    private const int EMOJI_VARIATION_SELECTOR = 0xFE0F;
    private const int ZERO_WIDTH_JOINER = 0x200D;
    private const int COMBINING_ENCLOSING_KEYCAP = 0x20E3;
    private const int COMBINING_ENCLOSING_CIRCLE_BACKSLASH = 0x20E0;
    private const int WAVING_BLACK_FLAG = 0x1F3F4;
    private const int CANCEL_TAG = 0xE007F;
    private const int REGIONAL_INDICATOR_FIRST = 0x1F1E6;
    private const int REGIONAL_INDICATOR_LAST = 0x1F1FF;
    private const int TAG_DIGIT_ZERO = 0xE0030;
    private const int TAG_DIGIT_NINE = 0xE0039;
    private const int TAG_SMALL_LETTER_A = 0xE0061;
    private const int TAG_SMALL_LETTER_Z = 0xE007A;

    internal static bool IsEmoji(Rune Rune) => Property(Rune.Value, Character.UProperty.EMOJI);
    private static bool Property(int Value, Character.UProperty Property) => Character.GetIntPropertyValue(Value, Property) != 0;
    private static bool Modifier(int Value) => Property(Value, Character.UProperty.EMOJI_MODIFIER);
    private static bool ModifierBase(int Value) => Property(Value, Character.UProperty.EMOJI_MODIFIER_BASE);
    private static bool Regional(int Value) => Value is >= REGIONAL_INDICATOR_FIRST and <= REGIONAL_INDICATOR_LAST;
    private static bool KeycapBase(int Value) => Value is >= '0' and <= '9' or '#' or '*';
    private static bool AnyEmoji(int Value) => Property(Value, Character.UProperty.EMOJI) && !Modifier(Value) && !Regional(Value);
    private static bool Tag(int Value) => Value is >= TAG_DIGIT_ZERO and <= TAG_DIGIT_NINE or >= TAG_SMALL_LETTER_A and <= TAG_SMALL_LETTER_Z;

    private static bool At(ReadOnlySpan<Rune> Runes, int Index, int Value) => Index < Runes.Length && Runes[Index].Value == Value;

    /// <summary>
    /// Splits text into presentation runs the way Chromium's emoji segmenter does.
    /// Adjacent scanner tokens with the same priority merge into one run.
    /// </summary>
    /// <param name="Runes">The text to segment.</param>
    /// <param name="Result">Cleared, then filled with the runs in text order. Ranges are offsets into the text.</param>
    internal static void FillRuns(ReadOnlySpan<Rune> Runes, List<(Range Range, PRIORITY Priority)> Result)
    {
        Result.Clear();
        int offset = 0;

        for (var x = 0;x < Runes.Length;) {
            var end = TokenEnd(Runes, x, out var priority);
            var start = offset;
            while (x < end)
                offset += Runes[x++].Utf16SequenceLength;

            if (Result.Count > 0 && Result[^1].Priority == priority)
                Result[^1] = (Result[^1].Range.Start..offset, priority);
            else
                Result.Add((start..offset, priority));
        }
    }

    /// <summary>
    /// Returns the end of the scanner token that starts at <paramref name="X"/>.
    /// The scanner takes the longest token. On a tie, the token listed first wins:
    /// text_emoji_run_with_vs, emoji_run_with_vs, emoji_run, text_run.
    /// </summary>
    private static int TokenEnd(ReadOnlySpan<Rune> Runes, int X, out PRIORITY Priority)
    {
        var value = Runes[X].Value;
        var next = X + 1;

        // emoji_flag_sequence is the only emoji token that does not start with any_emoji.
        if (Regional(value) && next < Runes.Length && Regional(Runes[next].Value)) {
            Priority = PRIORITY.EMOJI;
            return next + 1;
        }

        if (!AnyEmoji(value)) {
            Priority = PRIORITY.TEXT;
            return next;
        }

        var sequenceEnd = EmojiSequenceEnd(Runes, X);

        if (At(Runes, next, TEXT_VARIATION_SELECTOR) || At(Runes, next, EMOJI_VARIATION_SELECTOR)) {
            // text_emoji_keycap_sequence and emoji_keycap_sequence add U+20E3 after the selector.
            var variationEnd = KeycapBase(value) && At(Runes, next + 1, COMBINING_ENCLOSING_KEYCAP) ? next + 2 : next + 1;
            if (variationEnd >= sequenceEnd) {
                Priority = Runes[next].Value == TEXT_VARIATION_SELECTOR ? PRIORITY.TEXT_WITH_VS : PRIORITY.EMOJI_WITH_VS;
                return variationEnd;
            }
        } else if (sequenceEnd == next) {
            // A lone emoji is an emoji_run only when it defaults to emoji presentation.
            Priority = ModifierBase(value) || Property(value, Character.UProperty.EMOJI_PRESENTATION) ? PRIORITY.EMOJI : PRIORITY.TEXT;
            return next;
        }

        Priority = PRIORITY.EMOJI;
        return sequenceEnd;
    }

    /// <summary>
    /// Returns the end of the longest multi-rune emoji_run sequence that starts with the any_emoji rune at <paramref name="X"/>,
    /// or <c>x + 1</c> when there is none.
    /// </summary>
    private static int EmojiSequenceEnd(ReadOnlySpan<Rune> Runes, int X)
    {
        /*
         * The rune after the base decides which sequence can match. U+20E0 and tags
         * never extend a ZWJ element, so these sequences cannot overlap.
         */
        if (At(Runes, X + 1, COMBINING_ENCLOSING_CIRCLE_BACKSLASH))
            return X + 2;

        if (Runes[X].Value == WAVING_BLACK_FLAG) {
            var tagEnd = TagSequenceEnd(Runes, X);
            if (tagEnd > X + 1)
                return tagEnd;
        }

        // A ZWJ sequence with no joiners is a lone emoji_presentation_sequence or emoji_modifier_sequence.
        var end = ZwjElementEnd(Runes, X);
        while (At(Runes, end, ZERO_WIDTH_JOINER) && end + 1 < Runes.Length && AnyEmoji(Runes[end + 1].Value))
            end = ZwjElementEnd(Runes, end + 1);

        return end;
    }

    /// <summary>
    /// Returns the end of the emoji_zwj_element that starts with the any_emoji rune at <paramref name="Idx"/>:
    /// the rune and an optional VS16, or a modifier base and its emoji modifier.
    /// </summary>
    private static int ZwjElementEnd(ReadOnlySpan<Rune> Runes, int Idx)
    {
        var end = Idx + 1;
        if (At(Runes, end, EMOJI_VARIATION_SELECTOR) || (end < Runes.Length && ModifierBase(Runes[Idx].Value) && Modifier(Runes[end].Value)))
            end++;

        return end;
    }

    /// <summary>
    /// Returns the end of the emoji_tag_sequence (base, one or more tags, cancel tag) that starts at <paramref name="X"/>,
    /// or <c>x + 1</c> when there are no tags or no cancel tag.
    /// </summary>
    private static int TagSequenceEnd(ReadOnlySpan<Rune> Runes, int X)
    {
        var end = X + 1;
        while (end < Runes.Length && Tag(Runes[end].Value))
            end++;

        return end > X + 1 && At(Runes, end, CANCEL_TAG) ? end + 1 : X + 1;
    }
}
