using Icu;

using MessageHeightTwitch;
using MessageHeightTwitch.Measurement.Shared;
using MessageHeightTwitch.Measurement.Shared.Shaping;

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics.Metrics;
using System.Linq;
using System.Numerics;
using System.Text;

using Tests.Utils;

using static System.Runtime.InteropServices.JavaScript.JSType;

using BreakIterator = Icu.BreakIterator;
using Buffer = HarfBuzzSharp.Buffer;
using Direction = HarfBuzzSharp.Direction;

namespace Tests;

/// <summary>
/// Chrome's DOM Range boxes in a message's text, which production does not compute. Line layout takes a glyph
/// sequence's start where a Range shares its advance among its graphemes. The text is classified and positioned once
/// for every Range <see cref="Measure"/> reads from it.
/// </summary>
internal sealed class DomRangeModel
{
    // One CSS layout unit is 1/64 px.
    private const float LAYOUT_UNITS_PER_PIXEL = 64f;

    private readonly int TextLength;
    // The end of the first line's text, in the content, when the text wraps on the long line.
    private readonly int LineEnd = int.MaxValue;
    private readonly string Content;
    private readonly int Leading;
    // The characters at the start that joined the separator's cluster.
    private readonly int Lead;
    private readonly float WholeWidth;
    private readonly bool Bidi;
    // The body's caret positions, for a text of one left-to-right item.
    private readonly float[] Starts = [];
    private readonly float[] Ends = [];
    // DOM text fragments: text-node boundaries intersected with the paragraph's bidi items.
    private readonly List<(int End, Direction Direction)> Items = [];
    // Candidate ranges keep both ends of the original shape, including context outside an item's own glyphs.
    private readonly string ShapingText = "";
    private readonly List<FontRun> PreparedRuns = [];
    private readonly int BodyOffset;
    private readonly int SeparatorLength;
    // The caret positions of each left-to-right item's own shape, by the item's start.
    private readonly Dictionary<int, (float[] Starts, float[] Ends)> ItemPositions = [];

    internal DomRangeModel(string Text, int? FirstLineEnd = null, int[]? TextNodeEnds = null, bool IsAction = false)
    {
        /*
         * Spaces at the start collapse with the separator's space and spaces at the end of the block are removed;
         * an offset among them moves to the nearest remaining text.
         */
        TextLength = Text.Length;
        SeparatorLength = IsAction ? 1 : 2;
        var content = Text.TrimStart(' ');
        Leading = Text.Length - content.Length;
        Content = content.TrimEnd(' ');
        if (FirstLineEnd is { } lineEnd)
            LineEnd = lineEnd - Leading;
        if (Content.Length == 0)
            return;
        var fonts = FontFallbackList.Shared;
        var workspace = MeasurementWorkspace.Pool.Get();
        try {
            var classified = TextSetup.ClassifyMessage(Content, workspace, out Lead, out _, IsAction);
            BodyOffset = workspace.Paragraph.Items.Last(item => item.Region != PARAGRAPH_REGION.BODY).End;
            ShapingText = new string(workspace.Paragraph.TextBuffer, 0, workspace.Paragraph.Items[^1].End);
            PreparedRuns = [.. workspace.Paragraph.Runs];
            Bidi = classified.Bidi.Length > 1 || classified.Bidi[0].Direction != Direction.LeftToRight;

            var nodeEnds = (TextNodeEnds != null) switch {
                true => TextNodeEnds.Select(end => Math.Clamp(end - Leading, 0, Content.Length)).ToHashSet(),
                false => workspace.Paragraph.Items.Where(item => item.Region == PARAGRAPH_REGION.BODY).Select(item => item.End - BodyOffset).ToHashSet()
            };
            var itemStart = 0;
            foreach (var (paragraphEnd, direction) in BlinkLineModel.BidiItems((IsAction ? " " : ": ") + Content)) {
                var itemEnd = paragraphEnd - SeparatorLength;
                if (itemEnd <= 0)
                    continue;
                foreach (var boundary in nodeEnds.Where(end => itemStart < end && end < itemEnd).Order())
                    Items.Add((boundary + SeparatorLength, direction));
                Items.Add((paragraphEnd, direction));
                itemStart = itemEnd;
            }

            /*
             * The whole fragment takes its own width, shaped after the paragraph's text before it, as Blink shapes it
             * (and production shapes the message's items).
             */
            WholeWidth = RangeWidth(ShapingText, BodyOffset..ShapingText.Length, PreparedRuns, FirstItem: BodyOffset, Fragments: ParagraphFragments());
            if (!Bidi && Items.Count == 1) {
                var body = BodyOffset + Lead;
                var (starts, ends) = CaretPositions(ShapingText, CopiedRuns(body, ShapingText.Length), PreparedRuns);
                (Starts, Ends) = (starts[body..], ends[body..]);
            }
        } finally {
            MeasurementWorkspace.Pool.Return(workspace);
        }
    }

    /// <summary>
    /// Chrome's DOM Range box for text[range], by Blink's path: LayoutText::AbsoluteQuadsForRange maps the DOM offsets
    /// to the line's text (MapDOMOffsetToTextContentOffset) and FragmentItem::LocalRect takes
    /// ShapeResult::CaretPositionForOffset of each end relative to the fragment, rounded outward to a LayoutUnit. A
    /// text of several bidi items or of right-to-left text has a fragment for each item the range covers.
    /// </summary>
    internal float Measure(Range Range)
    {
        var (rangeStart, rangeLength) = Range.GetOffsetAndLength(TextLength);
        var start = Math.Clamp(rangeStart - Leading, 0, Content.Length);
        var end = Math.Clamp(rangeStart + rangeLength - Leading, 0, Content.Length);
        if (end <= start)
            return 0;
        if (start == 0 && end == Content.Length && !(Bidi && Lead == 0))
            return WholeWidth;
        if (Bidi || Items.Count > 1) {
            if (Lead > 0)
                return BidiRangeWidth(start..end);

            /*
             * A fragment for each bidi item of the paragraph the range covers: a left-to-right one that it covers
             * in part from caret positions measured from the item's start, as below, any other by RangeWidth.
             */
            float total = 0;
            var itemStart = 0;
            foreach (var (paragraphEnd, direction) in Items) {
                var itemEnd = paragraphEnd - SeparatorLength;
                if (itemEnd <= 0)
                    continue;
                var (a, b) = (Math.Max(start, itemStart), Math.Min(end, itemEnd));
                if (b > a) {
                    if (direction == Direction.LeftToRight) {
                        var (itemStarts, itemEnds) = ItemCaretPositions(itemStart, itemEnd);
                        total += Ceil(itemEnds[b]) - Floor(itemStarts[a]);
                    }
                    else {
                        total += RangeWidth(
                            ShapingText,
                            (BodyOffset + a)..(BodyOffset + b),
                            PreparedRuns,
                            FirstItem: BodyOffset + itemStart,
                            LineEnd: LineEnd == int.MaxValue ? int.MaxValue : BodyOffset + LineEnd,
                            Fragments: [(BodyOffset + itemEnd, direction)]
                        );
                    }
                }
                itemStart = itemEnd;
            }
            return total;
        }

        /*
         * The fragment's shape has runs for the body only: the leading characters are in the separator's
         * cluster, so ShapeResult::CopyRangeInternal gives them no run. ShapeResult::PositionForOffset counts an
         * offset through the runs from the fragment's start, so offset k takes the body's offset k; an offset
         * past the runs takes 0, except the fragment's end, which takes its width.
         */
        var bodyLength = Content.Length - Lead;
        float pos(float[] Positions, int Offset)
        {
            if (Offset < bodyLength)
                return Positions[Offset];
            return Offset == Content.Length ? Positions[bodyLength] : 0;
        }
        var from = pos(Starts, start);
        var to = pos(Ends, end);
        return from == to ? 0 : Ceil(MathF.Max(from, to)) - Floor(MathF.Min(from, to));
    }



    /// <summary>
    /// The item's own positions, from 0 at its start. InlineNode::ShapeText and HarfBuzzShaper::CommitGlyphs retain
    /// successful glyphs of the complete candidate shape. Replay that range and select this item's glyphs, rather
    /// than guessing context from adjacent font runs. Even cutting off an ignored character's following space can
    /// change the glyphs HarfBuzz produces.
    /// </summary>
    private (float[] Starts, float[] Ends) ItemCaretPositions(int ItemStart, int ItemEnd)
    {
        if (ItemPositions.TryGetValue(ItemStart, out var positions))
            return positions;

        var first = BodyOffset + ItemStart;
        var last = BodyOffset + ItemEnd;
        var pieces = CopiedRuns(first, last);
        var (paragraphStarts, paragraphEnds) = CaretPositions(ShapingText, pieces, PreparedRuns);
        var starts = paragraphStarts[BodyOffset..];
        var ends = paragraphEnds[BodyOffset..];
        var origin = starts[ItemStart];
        for (var offset = ItemStart; offset <= ItemEnd; offset++) {
            starts[offset] -= origin;
            ends[offset] -= origin;
        }

        return ItemPositions[ItemStart] = (starts, ends);
    }

    private List<FontRun> CopiedRuns(int First, int Last)
    {
        return [.. PreparedRuns
            .Where(run => run.Range.Start.Value < Last && run.Range.End.Value > First)
            .Select(run => run with {
                Range = Math.Max(First, run.Range.Start.Value)..Math.Min(Last, run.Range.End.Value),
                ShapingRange = run.ShapingRange ?? run.Range
            })];
    }

    /// <summary>
    /// A Range in the message's text node, after the separator, with a fragment for each bidi item it covers.
    /// </summary>
    private float BidiRangeWidth(Range Range)
    {
        var (start, length) = Range.GetOffsetAndLength(Content.Length);
        return RangeWidth(ShapingText, (BodyOffset + start)..(BodyOffset + start + length), PreparedRuns, FirstItem: BodyOffset,
            LineEnd: LineEnd == int.MaxValue ? int.MaxValue : BodyOffset + LineEnd, Fragments: ParagraphFragments());
    }

    private List<(int End, Direction Direction)> ParagraphFragments()
    {
        return Items.Select(item => (item.End - SeparatorLength + BodyOffset, item.Direction)).ToList();
    }

    /// <summary>
    /// Blink's ShapeResult::CaretPositionForOffset at every offset of a left-to-right text, from its start, with
    /// AdjustMidCluster::kToStart (which a DOM Range takes for its start) and kToEnd (for its end). Each font run is
    /// replayed over its complete original candidate range before selecting the committed glyphs. Positions are
    /// floats, as in ShapeResult::PositionForOffset: the float widths of the runs before
    /// (each run's exact advance as a float), plus the exact position in the run as a float.
    /// </summary>
    private static (float[] Start, float[] End) CaretPositions(ReadOnlySpan<char> Text, List<FontRun> FontRuns, List<FontRun> ParagraphFonts)
    {
        var starts = new float[Text.Length + 1];
        var ends = new float[Text.Length + 1];
        var types = BlinkLineModel.CharTypes(Text, ParagraphFonts);
        using var buffer = new Buffer();
        float x = 0;
        for (var index = 0; index < FontRuns.Count; index++) {
            var font = FontRuns[index];
            var (runStart, runEnd) = (font.Range.Start.Value, font.Range.End.Value);
            var shaper = FontCache.GetShaper(font.TypeFace);
            var source = font.ShapingRange ?? font.Range;
            BlinkLineModel.Shape(shaper, Text, source, types, buffer, Direction.LeftToRight, font.Script);

            // The run's own glyphs: a shaping that reaches into the next run counts only up to the run's end.
            var width = BlinkLineModel.Glyphs(shaper, buffer, runStart).Where(glyph => glyph.Cluster >= 0 && glyph.Cluster < runEnd - runStart).Sum(glyph => glyph.Advance);
            var graphemeStarts = BreakIterator.GetBoundaries(BreakIterator.UBreakIteratorType.CHARACTER, BlinkLineModel.BreakLocale, Text[runStart..runEnd].ToString()).Select(grapheme => grapheme.Start).ToHashSet();
            var (runStarts, runEnds) = CaretPositions(shaper, buffer, runStart, runEnd - runStart, graphemeStarts);
            for (var k = 0; k < runEnd - runStart; k++) {
                starts[runStart + k] = x + HarfBuzz.FixedToFloat(runStarts[k]);
                ends[runStart + k] = x + HarfBuzz.FixedToFloat(runEnds[k]);
            }
            x += HarfBuzz.FixedToFloat(width);
        }
        // The end of the last run, which may end before the text does.
        var last = FontRuns.Count > 0 ? FontRuns[^1].Range.End.Value : 0;
        starts[last] = ends[last] = x;
        return (starts, ends);
    }

    /// <summary>
    /// Blink's XPositionForOffset for every offset of a left-to-right run: the caret position a DOM Range
    /// measures, not the line layout position. A glyph sequence is the glyphs sharing a cluster and covers the
    /// characters up to the next cluster. Its advance is shared evenly by its graphemes; an offset inside a
    /// grapheme takes that grapheme's start with AdjustMidCluster::kToStart (Start) and its end with kToEnd (End).
    /// </summary>
    private static (long[] Start, long[] End) CaretPositions(HarfBuzz Shaper, Buffer Buffer, int RunStart, int Length, IReadOnlySet<int> GraphemeStarts)
    {
        var starts = new long[Length + 1];
        var ends = new long[Length + 1];
        var clusters = BlinkLineModel.Clusters(Shaper, Buffer, RunStart) .Where(cluster => cluster.Cluster >= 0 && cluster.Cluster < Length).ToArray();
        if (clusters.Length == 0)
            return (starts, ends);
        var origin = clusters[0].Position;

        // Characters before the first glyph belong to the first sequence.
        var sequences = clusters
            .Select((cluster, index) => (
                Start: index == 0 ? 0 : cluster.Cluster,
                End: index + 1 < clusters.Length ? clusters[index + 1].Cluster : Length,
                Position: cluster.Position - origin, cluster.Advance
            )).ToArray();

        // Graphemes that intersect [a, b): the first one, then one for each start after a.
        int numGraphemes(int a, int b) => b <= a ? 0 : 1 + GraphemeStarts.Count(start => a < start && start < b);

        // The sequence containing an offset starts at or before it and ends after it; the end takes the last one.
        (long Start, long End) positionIn((int Start, int End, long Position, long Advance) Sequence, int Offset)
        {
            var (s, e, position, advance) = Sequence;
            var graphemes = numGraphemes(s, e);
            if (graphemes <= 1)
                return (position, Offset == s ? position : position + advance);
            var next = Offset + (Offset == Length ? 0 : 1);
            var toOffset = numGraphemes(s, next) - 1;
            var atStart = Offset > 0 ? numGraphemes(Offset - 1, next) != 1 : Offset == s;
            var share = advance / graphemes;
            return (position + share * toOffset, position + share * toOffset + (atStart ? 0 : share));
        }

        foreach (var sequence in sequences) {
            for (var x = sequence.Start; x < sequence.End; x++) {
                (starts[x], ends[x]) = positionIn(sequence, x);
            }
        }
        (starts[Length], ends[Length]) = positionIn(sequences[^1], Length);
        return (starts, ends);
    }

    /// <summary>
    /// A DOM Range's width as Blink's FragmentItem::LocalRect reports it. Text fragments are bidi
    /// items starting no earlier than firstItem (the message's own text node). A fragment the
    /// range covers whole contributes its snapped size. A partial one contributes
    /// FromFloatEncompassRound of its caret positions: ceil(end) - floor(start), measured from
    /// the fragment start in the whole fragment's shaping. A text that wraps has fragments up to
    /// <paramref name="LineEnd"/> on its first line.
    /// </summary>
    private static float RangeWidth(ReadOnlySpan<char> Text, Range Range, List<FontRun> FontRuns, int FirstItem = 0, int LineEnd = int.MaxValue, List<(int End, Direction Direction)>? Fragments = null)
    {
        var (start, length) = Range.GetOffsetAndLength(Text.Length);
        var end = start + length;
        if (end <= start || FontRuns.Count == 0)
            return 0;
        var types = BlinkLineModel.CharTypes(Text, FontRuns);
        var graphemeStarts = new HashSet<int>(System.Globalization.StringInfo.ParseCombiningCharacters(Text.ToString()));
        using var buffer = new Buffer();
        float total = 0;
        var itemStart = FirstItem;
        foreach (var (itemEnd, direction) in Fragments ?? BlinkLineModel.BidiItems(Text)) {
            if (itemEnd <= itemStart)
                continue;
            // The item's fragment on the first line.
            var fragmentEnd = Math.Min(itemEnd, LineEnd);
            var from = Math.Max(start, itemStart);
            var to = Math.Min(end, itemEnd);
            if (to > from) {
                // Each run's glyphs and exact width.
                var runs = new List<(int Start, int End, (int Cluster, long Advance)[] Glyphs, long Exact)>();
                for (var index = 0; index < FontRuns.Count; index++) {
                    var font = FontRuns[index];
                    var runStart = Math.Max(itemStart, font.Range.Start.Value);
                    var runEnd = Math.Min(fragmentEnd, font.Range.End.Value);
                    if (runEnd <= runStart)
                        continue;
                    var shaper = FontCache.GetShaper(font.TypeFace);
                    var source = font.ShapingRange ?? font.Range;
                    if (fragmentEnd < itemEnd)
                        source = source.Start..Math.Min(source.End.Value, fragmentEnd);
                    BlinkLineModel.Shape(shaper, Text, source, types, buffer, direction, font.Script);
                    // The run's own glyphs: a shaping that reaches into the next run counts only up to the run's end.
                    var glyphs = BlinkLineModel.Glyphs(shaper, buffer, 0).Where(glyph => glyph.Cluster >= runStart && glyph.Cluster < runEnd).ToArray();
                    runs.Add((runStart, runEnd, glyphs, glyphs.Sum(glyph => glyph.Advance)));
                }
                {
                    var rtl = direction == Direction.RightToLeft;

                    /*
                     * ShapeResultRun::XPositionForOffset in 16.16: a cluster (glyphs of one character index) shares
                     * its advance evenly among its graphemes, an offset takes the grapheme that holds it, and an end
                     * inside a grapheme (AdjustMidCluster::kToEnd) moves past it. Right to left, positions count
                     * from the run's left edge, and an offset is at its grapheme's right side.
                     */
                    long runPosition((int Cluster, long Advance)[] Glyphs, int RunStart, int RunEnd, long Exact, int Offset, bool Start)
                    {
                        var cluster = Glyphs.Select(glyph => glyph.Cluster).Where(c => c <= Offset).Append(RunStart).Max();
                        var clusterEnd = Glyphs.Select(glyph => glyph.Cluster).Where(c => c > cluster).Append(RunEnd).Min();
                        var before = Glyphs.Where(glyph => glyph.Cluster < cluster).Sum(glyph => glyph.Advance);
                        var advance = Glyphs.Where(glyph => glyph.Cluster == cluster).Sum(glyph => glyph.Advance);
                        var graphemes = graphemeStarts.Count(g => g >= cluster && g < clusterEnd);
                        var index = graphemeStarts.Count(g => g > cluster && g <= Offset);
                        var atStart = Offset == cluster || graphemeStarts.Contains(Offset);
                        var share = graphemes > 1 ? advance / graphemes : advance;
                        if (!rtl)
                            return before + (graphemes > 1 ? share * index : 0) + (!Start && !atStart ? share : 0);
                        var left = Exact - before - advance;
                        return left + (graphemes > 1 ? share * (graphemes - index - 1) : 0) - (!Start && !atStart ? share : 0) + share;
                    }

                    /*
                     * ShapeResult::PositionForOffset: a float x that adds each run's float width in visual order (left
                     * to right, so a right-to-left item's runs from its last), then the offset's exact position in its
                     * run, a float once (XPositionForOffset). The fragment's end is at its width, or 0 right to left. A
                     * range over the whole fragment takes the same path, so right to left its width is a float sum in
                     * visual order.
                     */
                    float pos(int Offset, bool Start)
                    {
                        float x = 0;
                        if (Offset >= fragmentEnd) {
                            if (!rtl) {
                                foreach (var run in runs) {
                                    x += HarfBuzz.FixedToFloat(run.Exact);
                                }
                            }
                            return x;
                        }
                        foreach (var run in rtl ? Enumerable.Reverse(runs) : runs) {
                            if (run.Start <= Offset && Offset < run.End) {
                                return x + HarfBuzz.FixedToFloat(runPosition(run.Glyphs, run.Start, run.End, run.Exact, Offset, Start));
                            }
                            x += HarfBuzz.FixedToFloat(run.Exact);
                        }
                        return x;
                    }
                    
                    var a = pos(from, Start: true);
                    var b = pos(to, Start: false);

                    // Characters without an advance there (a bidi control, which has no fragment) add no box.
                    if (a != b)
                        total += Ceil(MathF.Max(a, b)) - Floor(MathF.Min(a, b));
                }
            }
            itemStart = itemEnd;
            if (itemStart >= end)
                break;
        }
        return total;
    }

    // LayoutUnit::FromFloatEncompassRound: floor the smaller position and ceil the larger to a layout unit.
    private static float Floor(float Pixels) => MathF.Floor(Pixels * LAYOUT_UNITS_PER_PIXEL) / LAYOUT_UNITS_PER_PIXEL;
    private static float Ceil(float Pixels) => MathF.Ceiling(Pixels * LAYOUT_UNITS_PER_PIXEL) / LAYOUT_UNITS_PER_PIXEL;
}
