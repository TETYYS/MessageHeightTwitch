using MessageHeightTwitch.Measurement.Shared;
using MessageHeightTwitch.Measurement.Shared.Shaping;

using System;
using System.Collections.Generic;

namespace MessageHeightTwitch.Measurement.LineFitting;

/// <summary>
/// Blink's LazyLineBreakIterator rules over collected text. Break opportunities belong to the paragraph, independently
/// of the font, style and bidi items that its line breaker visits. Cluster marks prevent splitting a shaped space.
/// </summary>
internal static class LineBreakIterator
{
    /// <summary>
    /// Projects paragraph opportunities onto an item's offsets. Keep the first opportunity beyond the item when
    /// its end is inside a word or grapheme; an inline item boundary does not create a break opportunity.
    /// </summary>
    internal static void Project(List<(int Start, int End)> Paragraph, int Offset, int Length,
        List<(int Start, int End)> Result)
    {
        Result.Clear();
        var start = 0;
        foreach (var (_, end) in Paragraph) {
            if (end <= Offset)
                continue;
            var stop = end - Offset;
            Result.Add((start, stop));
            start = stop;
            if (stop >= Length)
                break;
        }
    }

    /// <summary>
    /// Includes the isolated object following a text item in its paragraph's break opportunities.
    /// </summary>
    internal static ReadOnlySpan<char> WithFollowingObject(ReadOnlySpan<char> Text, LineBreakingWorkspace Workspace)
    {
        const string INLINE_OBJECT = "\u2066\uFFFC\u2069";
        PooledBuffers.Ensure(ref Workspace.InlineText, Text.Length + INLINE_OBJECT.Length);
        var result = Workspace.InlineText.AsSpan(0, Text.Length + INLINE_OBJECT.Length);
        Text.CopyTo(result);
        INLINE_OBJECT.CopyTo(result[Text.Length..]);
        return result;
    }

    /// <summary>
    /// Collects normal or break-all opportunities, using shaped clusters for spaces that carry marks.
    /// </summary>
    internal static void Fill(ReadOnlySpan<char> Text, ReadOnlySpan<ClusterMark> Marks, bool BreakAll,
        LineBreakingWorkspace Workspace, List<(int Start, int End)> Result, int StartOffset = 0)
    {
        var icu = Workspace.Boundaries;
        Workspace.Iterators.GetBreaks(Text, false, icu, StartOffset);
        Result.Clear();

        var start = StartOffset;
        var next = 0;
        for (var x = Math.Max(1, StartOffset + 1); x < Text.Length; x++) {
            var last = Text[x - 1];
            var ch = Text[x];
            if (IsSpace(ch))
                continue;

            bool breaks;
            if (IsSpace(last)) {
                if (last == ' ' && x < Marks.Length && Marks[x] == ClusterMark.None) {
                    if (x - 1 > start) {
                        Result.Add((start, x - 1));
                        start = x - 1;
                    }
                    continue;
                }
                breaks = true;
            } else {
                var fast = AsciiLineBreakPairs.Fast(x > StartOffset + 1 ? Text[x - 2] : '\0', last, ch, Workspace);

                if (fast == FastBreak.Break || BreakAll && !char.IsLowSurrogate(ch) && AsciiLineBreakPairs.BreaksAll(Text[StartOffset..], x - StartOffset))
                    breaks = true;
                else if (fast == FastBreak.NoBreak)
                    continue;
                else {
                    while (icu[next].End < x)
                        next++;
                    breaks = icu[next].End == x;
                }
            }
            if (breaks) {
                Result.Add((start, x));
                start = x;
            }
        }
        Result.Add((start, Text.Length));
    }

    /// <summary>
    /// The collapsible spaces handled by Blink's fast line-break rules.
    /// </summary>
    private static bool IsSpace(char Ch) => Ch is ' ' or '\t' or '\n';
}
