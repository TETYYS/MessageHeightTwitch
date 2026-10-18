// Adapted from Blink's shaping_line_breaker.cc at cfaadc5a132d78e1828635aa8405a499f3e14864.
using MessageHeightTwitch.Measurement.Shared.Shaping;

using System;
using System.Collections.Generic;

namespace MessageHeightTwitch.Measurement.LineFitting;

/// <summary>
/// Text-fragment helper for stage 4 (line fitting): selects a break from an item's original shape and reshapes unsafe
/// line edges. Item fitting, images, whitespace
/// consumption and overflow retries belong to the outer line breaker, as in Blink's LineBreaker.
/// </summary>
internal ref struct ShapingLineBreaker
{
    private TextWidths Widths;
    private readonly LineBreakingWorkspace Ws;
    private readonly ReadOnlySpan<char> Text => Widths.Text;
    private readonly int Start => Widths.Start;
    private readonly bool Rtl => Widths.Rtl;

    internal ShapingLineBreaker(TextWidths Widths, LineBreakingWorkspace Ws)
    {
        this.Widths = Widths;
        this.Ws = Ws;
    }

    /// <summary>A selected fragment, with its break opportunity separate from its non-hangable end.</summary>
    internal readonly record struct Result(int BreakOffset, int ContentEnd, float Width, bool IsOverflow)
    {
        internal bool HasTrailingSpaces => ContentEnd < BreakOffset;
        internal bool IsHyphenated { get; init; }
    }

    /// <summary>
    /// Reuses a fitting original shape before querying break opportunities; otherwise finds a break and returns
    /// the shaped fragment's width with its overflow status. Widths are snapped to LayoutUnit's 1/64 px.
    /// </summary>
    internal Result ShapeLine(Range Line, long Available, bool Anywhere = false, bool WrappedStart = false)
    {
        var (start, length) = Line.GetOffsetAndLength(Text.Length);
        var end = start + length;
        Available = Math.Max(0, Available);

        // Blink reuses the whole original result before initializing its position and break caches.
        if (start == Start && end == Text.Length && (!WrappedStart || Widths.IsSafeToBreak(start))) {
            var width = HarfBuzz.FixedToFloat(TextWidths.SnapFloat(Widths.ExactPrefix(end)));
            if (HarfBuzz.FloatToFixed(width) <= Available)
                return new(end, end, width, false) { IsHyphenated = end > start && Text[end - 1] == '\u00AD' };
        }

        var sign = Rtl ? -1 : 1;
        var startPosition = Widths.CachedPositionForOffset(start);
        var firstSafe = start > Start || WrappedStart ? Widths.NextSafeToBreakOffset(start, end) : start;
        var startWidth = firstSafe > start ? Widths.Shape(start..firstSafe, LineStart: true) : 0;
        if (firstSafe != start) {
            var oldWidth = sign * (Widths.CachedPositionForOffset(firstSafe) - startPosition);
            Available = Math.Max(0, Available + oldWidth - TextWidths.SnapFloat(startWidth));
        }

        var endPosition = startPosition + sign * Available;
        var candidate = start;
        while (candidate < end && sign * (Widths.CachedPositionForOffset(candidate + 1) - endPosition) <= 0)
            candidate++;

        /* 
         * Blink tries a closing Han character with line-end kerning before
         * rejecting the cached candidate. Reshaping also resolves its fallback
         * independently of the earlier missing cluster in the original shape.
         */
        var trimmedEnd = -1;
        var trimmedSafe = -1;
        long trimmedWidth = 0;
        if (candidate < end && HanKerning.MaybeClose(Text[candidate]) &&
            PreviousBoundary(candidate + 1, start, Anywhere) == candidate + 1) {
            var safe = Widths.PreviousSafeToBreakOffset(candidate, start);
            var reshaped = Widths.Shape(safe..(candidate + 1), HanKerningEnd: true);
            var preceding = sign * (Widths.CachedPositionForOffset(safe) - startPosition);
            if (preceding + TextWidths.FloatWidth(reshaped) <= Available) {
                trimmedSafe = safe;
                trimmedWidth = reshaped;
                trimmedEnd = ++candidate;
            }
        }

        if (candidate >= end)
            return LineResult(start, end, firstSafe, startWidth, false, WrappedStart, trimmedSafe, trimmedWidth, trimmedEnd == end);

        // A candidate in a trailing space run may use the opportunity after that run.
        var opportunity = PreviousBoundary(candidate, start, Anywhere);
        if (CollapseSpaces && IsBreakableSpace(Text[candidate])) {
            var next = NextBoundary(Math.Max(candidate, start + 1), Anywhere);
            if (next <= end && ContentEnd(next, start) <= candidate)
                opportunity = next;
        }
        var overflow = opportunity <= start;
        if (overflow)
            opportunity = NextBoundary(Math.Max(candidate, start + 1), Anywhere);

        // The paragraph's next break may be beyond this item's shape. ShapeLine never measures past the item.
        opportunity = Math.Min(opportunity, end);
        if (firstSafe >= opportunity || overflow)
            return LineResult(start, opportunity, firstSafe, startWidth, overflow, WrappedStart, opportunity == trimmedEnd ? trimmedSafe : -1, opportunity == trimmedEnd ? trimmedWidth : 0, opportunity == trimmedEnd);

        var fits = opportunity;
        while (true) {
            var contentEnd = ContentEnd(opportunity, start);
            var lastSafe = opportunity == trimmedEnd ? trimmedSafe : contentEnd < Text.Length ? Widths.PreviousSafeToBreakOffset(contentEnd, start) : contentEnd;
            var endWidth = opportunity == trimmedEnd ? trimmedWidth : lastSafe < contentEnd ? Widths.Shape(lastSafe..contentEnd) : 0;
            if (lastSafe == contentEnd || TextWidths.FloatWidth(endWidth) <= sign * (endPosition - Widths.CachedPositionForOffset(lastSafe)))
                return LineResult(start, opportunity, firstSafe, startWidth, false, WrappedStart, lastSafe, endWidth, opportunity == trimmedEnd);

            opportunity = PreviousBoundary(opportunity - 1, start, Anywhere);
            if (opportunity <= start)
                return LineResult(start, fits, firstSafe, startWidth, true, WrappedStart);
        }
    }

    /// <summary>Concatenates the reshaped edges with the unchanged middle, retaining their exact advances.</summary>
    private Result LineResult(int StartOffset, int BreakOffset, int FirstSafe, long StartWidth, bool Overflow, bool WrappedStart, int LastSafe = -1, long EndWidth = 0, bool HanKerningEnd = false)
    {
        var end = ContentEnd(BreakOffset, StartOffset);
        if (LastSafe < 0) {
            LastSafe = end < Text.Length ? Widths.PreviousSafeToBreakOffset(end, StartOffset) : end;
            EndWidth = LastSafe < end ? Widths.Shape(LastSafe..end) : 0;
        }

        long width;
        if (FirstSafe > end || LastSafe < FirstSafe)
            width = Widths.Shape(StartOffset..end, LineStart: StartOffset > Start || WrappedStart, HanKerningEnd: HanKerningEnd);
        else
            width = StartWidth + Widths.ExactPrefix(LastSafe) - Widths.ExactPrefix(FirstSafe) + EndWidth;
        return new(BreakOffset, end, HarfBuzz.FixedToFloat(TextWidths.SnapFloat(width)), Overflow) {
            IsHyphenated = BreakOffset > StartOffset && Text[BreakOffset - 1] == '\u00AD'
        };
    }

    /// <summary>Whether the iterator permits a break at this item's end.</summary>
    internal bool CanBreakAfter(int End) => PreviousBoundary(End, End - 1, Anywhere: false) == End;

    /// <summary>
    /// The width of the line text[line] of a single bidi item when a word breaks: the fragment Blink's
    /// ShapingLineBreaker::ConcatShapeResults builds. A wrapped line (one that starts after Start) whose start is not
    /// safe to break before is reshaped up to the next safe offset, a line that ends at an unsafe offset before the
    /// text's end is reshaped from the previous safe offset, and the rest keeps the original shape. With no safe
    /// offset inside, the whole line is reshaped.
    /// </summary>
    internal float ShapeLineAt(Range Line, bool WrappedStart = false)
    {
        var (start, length) = Line.GetOffsetAndLength(Text.Length);
        var end = start + length;

        var firstSafe = start > Start || WrappedStart ? Widths.NextSafeToBreakOffset(start, end) : start;
        var lastSafe = end < Text.Length ? Widths.PreviousSafeToBreakOffset(end, start) : end;

        long width;
        if (firstSafe >= end && firstSafe != start || lastSafe < firstSafe)
            width = Widths.Shape(Line, LineStart: start > Start || WrappedStart);
        else
            width = (firstSafe > start ? Widths.Shape(start..firstSafe, LineStart: start > Start || WrappedStart) : 0) + Widths.ExactPrefix(lastSafe) - Widths.ExactPrefix(firstSafe) + (lastSafe < end ? Widths.Shape(lastSafe..end) : 0);
        return HarfBuzz.FixedToFloat(TextWidths.SnapFloat(width));
    }

    /// <summary>The last break opportunity at or before offset that is after floor, or floor when there is none.</summary>
    private int PreviousBoundary(int Offset, int Floor, bool Anywhere)
    {
        var best = Floor;
        foreach (var (_, end) in Opportunities(Anywhere)) {
            if (end > Offset)
                break;
            if (end > Floor)
                best = end;
        }
        return best;
    }

    /// <summary>The first break opportunity at or after offset; a shared paragraph opportunity can lie beyond the item.</summary>
    private int NextBoundary(int Offset, bool Anywhere)
    {
        var opportunities = Opportunities(Anywhere);
        var index = 0;
        while (opportunities[index].End < Offset)
            index++;
        return opportunities[index].End;
    }

    /// <summary>Whether line-edge spaces can hang outside the available width.</summary>
    internal bool CollapseSpaces { get; init; }

    /// <summary>Blink's shaping-space predicate for chat: U+0020 and kIdeographicSpace (U+3000).</summary>
    internal static bool IsBreakableSpace(char Ch) => Ch is ' ' or '\u3000';

    /// <summary>The non-hangable end of a candidate fragment, as ShapingLineBreaker keeps it.</summary>
    internal readonly int ContentEnd(int End, int Floor)
    {
        if (CollapseSpaces)
            while (End > Floor && IsBreakableSpace(Text[End - 1]))
                End--;
        return End;
    }

    private bool GraphemesReady, LineBreaksReady;

    // word-break: break-all, which the chat line's names have: breaks inside words, between Blink's pairs of classes.
    internal bool BreakAll { get; init; }

    // An inline object that follows the text (an inline emoji) takes part in its line breaking.
    internal bool ObjectFollows { get; init; }

    // Paragraph opportunities projected onto this item's offsets, as LineBreaker shares one iterator across items.
    internal ReadOnlySpan<char> ParagraphText { get; init; }
    internal ReadOnlySpan<ClusterMark> ParagraphMarks { get; init; }
    internal int ParagraphOffset { get; init; }

    /// <summary>Grapheme boundaries, or the text's line break opportunities.</summary>
    private List<(int Start, int End)> Opportunities(bool Anywhere)
    {
        if (Anywhere && !GraphemesReady) {
            if (ParagraphText.IsEmpty)
                Ws.Iterators.GetBreaks(Text, true, Ws.ItemGraphemes);
            else {
                var graphemes = Ws.ParagraphGraphemes;
                if (graphemes.Count == 0)
                    Ws.Iterators.GetBreaks(ParagraphText, true, graphemes, Ws.StartOffset);
                LineBreakIterator.Project(graphemes, ParagraphOffset, Text.Length, Ws.ItemGraphemes);
            }
            GraphemesReady = true;
        } else if (!Anywhere && !LineBreaksReady) {
            if (ParagraphText.IsEmpty)
                FillLineBreaks(ObjectFollows ? LineBreakIterator.WithFollowingObject(Text, Ws) : Text, Ws.ItemBreaks);
            else {
                var breaks = BreakAll ? Ws.BreakAllBreaks : Ws.NormalBreaks;
                if (breaks.Count == 0)
                    LineBreakIterator.Fill(ParagraphText, ParagraphMarks, BreakAll, Ws, breaks, Ws.StartOffset);
                LineBreakIterator.Project(breaks, ParagraphOffset, Text.Length, Ws.ItemBreaks);
            }
            LineBreaksReady = true;
        }
        return Anywhere ? Ws.ItemGraphemes : Ws.ItemBreaks;
    }

    /// <summary>
    /// Blink's LazyLineBreakIterator for normal line breaking: a break after a run of spaces and never before a space;
    /// between two characters from '!' to U+00FF the pair table (Blink's compatibility rules for ASCII, ICU's pairs
    /// otherwise), with '-' before a digit breakable only after a letter or digit; ICU's line breaking for the rest.
    /// </summary>
    private void FillLineBreaks(ReadOnlySpan<char> BreakText, List<(int Start, int End)> Result)
    {
        LineBreakIterator.Fill(BreakText, Widths.BreakMarks(), BreakAll, Ws, Result);
    }

}
