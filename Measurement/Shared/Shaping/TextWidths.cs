using System;
using System.Buffers;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace MessageHeightTwitch.Measurement.Shared.Shaping;

/// <summary>
/// Width-measurement helper for stage 3 (paragraph shaping) and stage 4 (line fitting).
/// Measures <c>text[start..end]</c> for any end, the way the browser lays out text:
/// the slice is cut into runs where both the font and the bidi direction stay
/// the same, each run is shaped separately, and every bidi item's total width is
/// rounded up to 1/64 px before it is added to the line.
/// </summary>
/// <remarks>
/// Callers probe many ends of the same text (whole word, word without its
/// trailing space, every break opportunity), so results and the width before
/// every run are cached. Like Blink's HarfBuzzShaper, every run is shaped with
/// the whole text as context, so joining across run boundaries (a trailing ZWJ
/// in its own bidi run, a letter beside another font) and at a slice end match
/// the browser.
/// </remarks>
internal ref struct TextWidths
{
    /// <summary>
    /// Reusable width, position, cluster and Han-kerning buffers for one TextWidths at a time.
    /// </summary>
    /// <remarks>
    /// Cache usage: seven rented arrays, about 94 KiB of payload after construction with usual power-of-two
    /// pool buckets, plus a run list initially sized for 1,024 entries.
    /// </remarks>
    internal sealed class WidthBuffers
    {
        internal readonly List<WidthRun> Runs = new(1024);
        // Widths[end]: the width of the text up to end, NaN until measured.
        internal float[] Widths = ArrayPool<float>.Shared.Rent(1024);
        // Prefixes[i]: the width of everything before run i.
        internal RunPrefix[] Prefixes = ArrayPool<RunPrefix>.Shared.Rent(1024);
        internal long[] RunPositions = ArrayPool<long>.Shared.Rent(1024);
        // Whether a cluster starts at each character of that run, and whether it is safe to break before.
        internal ClusterMark[] RunMarks = ArrayPool<ClusterMark>.Shared.Rent(1024);
        internal ClusterMark[] BreakMarks = ArrayPool<ClusterMark>.Shared.Rent(1024);
        internal HanKerning.CHAR_TYPE[] Types = ArrayPool<HanKerning.CHAR_TYPE>.Shared.Rent(1024);
        internal HarfBuzzSharp.Feature[] Features = ArrayPool<HarfBuzzSharp.Feature>.Shared.Rent(1024);

        internal WidthBuffers() => Ensure(MessageHeightTwitch.MAX_PARAGRAPH_UTF16, MessageHeightTwitch.MAX_PARAGRAPH_UTF16);

        internal void Ensure(int Length, int Runs)
        {
            PooledBuffers.Ensure(ref Types, Length);
            PooledBuffers.Ensure(ref Features, Length + 1);
            PooledBuffers.Ensure(ref Widths, Length + 1);
            PooledBuffers.Ensure(ref Prefixes, Runs + 1);
            PooledBuffers.Ensure(ref RunPositions, Length + 2);
            PooledBuffers.Ensure(ref RunMarks, Length + 1);
            PooledBuffers.Ensure(ref BreakMarks, Length + 1);
        }

        internal void Return()
        {
            ArrayPool<float>.Shared.Return(Widths);
            ArrayPool<RunPrefix>.Shared.Return(Prefixes);
            ArrayPool<long>.Shared.Return(RunPositions);
            ArrayPool<ClusterMark>.Shared.Return(RunMarks);
            ArrayPool<ClusterMark>.Shared.Return(BreakMarks);
            ArrayPool<HanKerning.CHAR_TYPE>.Shared.Return(Types);
            ArrayPool<HarfBuzzSharp.Feature>.Shared.Return(Features);
        }
    }

    // A slice of text with one font and one bidi direction; ItemEnd marks the last run of a bidi item.
    internal readonly record struct WidthRun(Range Range, Typeface Typeface, HarfBuzzSharp.Direction Direction, bool ItemEnd, UScriptCode Script)
    {
        internal Range? ShapingRange { get; init; }
        // Where the run's positions start in ShapePositions, when classification kept them.
        internal int? CachedShape { get; init; }
    }

    /*
     * Width of everything before a run: finished bidi items (already rounded)
     * plus the not yet rounded width of the bidi item that is still open.
     */
    internal readonly record struct RunPrefix(long Rounded, long Open);

    internal readonly ReadOnlySpan<char> Text;
    internal readonly int Start;
    private readonly ShapingWorkspace Shaping;
    private readonly AsciiFastPath? Ascii;
    internal readonly WidthBuffers Buffers;

    // The runs measured so far: Buffers.Prefixes[0..MeasuredRuns] are filled in.
    private int MeasuredRuns;

    // The left-to-right run whose character positions are in Buffers.RunPositions, by its start.
    private int PositionRunStart = -1;

    /*
     * The position of Start within its bidi item, in 16.16 px from the item's start (its left edge, or its right
     * edge for a right-to-left item), when the item is known. Widths follow the algorithm in Blink's
     * ShapingLineBreaker::ShapeLine: a segment's width is the difference of two positions snapped to 1/64 px,
     * rather than the snapped width of the segment alone.
     */
    private readonly long? Origin;
    internal readonly bool Rtl;

    /// <summary>Widths from the ASCII table, which needs no workspace.</summary>
    internal TextWidths(ReadOnlySpan<char> Text, AsciiFastPath Ascii)
    {
        /*
         * The fast path measures a text only when it measured the whole text (AsciiFastPath.TryMeasure); every
         * prefix then passes the same checks, so a text it rejects means the caller broke that contract.
         */
        if (!Ascii.Accepts(Text))
            throw new System.Diagnostics.UnreachableException("The ASCII fast path was given a text it rejects.");

        this.Text = Text;
        this.Ascii = Ascii;
        Shaping = null!;
        Buffers = null!;
    }

    /*
     * The paragraph's text before Text, which a run at Text's start is shaped after (HarfBuzz's context), as Blink
     * shapes ranges of the whole paragraph. Empty where it cannot change the shape.
     */
    private readonly ReadOnlySpan<char> Before;

    /// <summary>
    /// Widths of <c>Classified.Text[Start..]</c>, shaped in the runs that classification found, after
    /// <paramref name="Before"/>
    /// </summary>
    internal TextWidths(ClassifiedText Classified, ShapingWorkspace Shaping, WidthBuffers Buffers, int Start = 0, long? Origin = null, ReadOnlySpan<char> Before = default)
    {
        Text = Classified.Text;
        this.Shaping = Shaping;
        this.Start = Start;
        this.Before = Before;
        Font = Classified.Font;
        ShapePositions = Classified.ShapePositions;
        ShapeMarks = Classified.ShapeMarks;

        this.Buffers = Buffers;

        BuildRuns(Classified.Runs, Classified.Bidi);

        Buffers.Ensure(Text.Length, Buffers.Runs.Count);

        HanKerned = HanKerning.MayApply(Text);
        ClassifyHanKerning(Classified.Runs);

        // Origins apply to one bidi item; a text across items keeps per-item widths.
        var singleItem = Classified.Bidi.Length == 1;

        this.Origin = singleItem ? Origin : null;
        Rtl = singleItem && Classified.Bidi[0].Direction == HarfBuzzSharp.Direction.RightToLeft;
        Buffers.Widths.AsSpan(0, Text.Length + 1).Fill(float.NaN);
        Buffers.Prefixes[0] = default;

        // Classification may already have shaped the whole text as one run, without what comes before it.
        if (Start == 0 && this.Origin == null && HarfBuzz.ContextLength(Before) == 0 && Classified.ShapedWidth is { } whole)
            Buffers.Widths[Text.Length] = whole;
    }

    /// <summary>Intersects the font runs from classification with the bidi items.</summary>
    private readonly void BuildRuns(ReadOnlySpan<FontRun> FontRuns, ReadOnlySpan<(int End, HarfBuzzSharp.Direction Direction)> Items)
    {
        var runs = Buffers.Runs;
        runs.Clear();
        var font = 0;
        var item = 0;

        while (font < FontRuns.Length && FontRuns[font].Range.End.Value <= Start)
            font++;
        while (item < Items.Length && Items[item].End <= Start)
            item++;

        for (var runStart = Start;runStart < Text.Length;) {
            var fontEnd = FontRuns[font].Range.End.Value;
            var itemEnd = Items[item].End;
            var runEnd = Math.Min(fontEnd, itemEnd);

            runs.Add(new(runStart..runEnd, FontRuns[font].TypeFace, Items[item].Direction, ItemEnd: runEnd == itemEnd, FontRuns[font].Script) {
                ShapingRange = FontRuns[font].ShapingRange is { } shaped ? Math.Max(Start, shaped.Start.Value)..shaped.End.Value : null,
                // Kept positions cover the whole font run as classified.
                CachedShape = Start == 0 && FontRuns[font].Range.Equals(runStart..runEnd) ? FontRuns[font].CachedShape : null
            });
            runStart = runEnd;

            if (runEnd == fontEnd)
                font++;
            if (runEnd == itemEnd)
                item++;
        }
    }

    private readonly void ClassifyHanKerning(ReadOnlySpan<FontRun> FontRuns)
    {
        if (!HanKerning.MayApply(Text))
            return;
        foreach (var (range, typeface, _) in FontRuns)
            ClassifyHanKerning(range, typeface);
    }

    /// <summary>Writes the Han kerning types of <paramref name="Range"/> in <paramref name="Typeface"/> to the workspace.</summary>
    private readonly void ClassifyHanKerning(Range Range, Typeface Typeface)
    {
        var kerning = FontCache.GetShaper(Typeface).Kerning;
        for (var i = Range.Start.Value;i < Range.End.Value;i++)
            Buffers.Types[i] = kerning.GetCharType(Text[i]);
    }

    internal float Measure(int End)
    {
        if (End <= Start)
            return 0;
        /*
         * Positioned texts use ExactPrefix and ShapingLineBreaker's fragment widths instead.
         * The kern to the following character belongs to this prefix, as in Blink.
         */
        if (Ascii != null)
            return HarfBuzz.FixedToFloat(SnapFixed(Ascii.PrefixFixed(Text[Start..], End - Start)));
        ref var result = ref Buffers.Widths[End];
        if (float.IsNaN(result))
            result = MeasureUncached(End);
        return result;
    }

    /// <summary>
    /// A reshaped width as Blink keeps it, a float (ShapeResult::Width), in 16.16 units: at a few hundred px a
    /// float has no room for the last 16.16 unit, which rounds to the nearest float.
    /// </summary>
    internal static long FloatWidth(long Exact) => HarfBuzz.FloatToFixed(HarfBuzz.FixedToFloat(Exact));

    /// <summary>That width rounded up to 1/64 px (SnappedWidth).</summary>
    internal static long SnapFloat(long Exact) => SnapFixed(FloatWidth(Exact));

    /// <summary>
    /// A character's position in its item, rounded up to 1/64 px (ShapeResult::CachedPositionForOffset), as an x that
    /// grows to the right. Without an item origin the text is an item of its own. Blink positions a right-to-left item
    /// from its left edge, its end (ComputePositionData accumulates advances in visual order), so where the item's
    /// width is not a whole 1/64 px its positions round apart from positions from its start.
    /// </summary>
    internal long CachedPositionForOffset(int Offset)
    {
        var position = (Origin ?? 0) + ExactPrefix(Offset);
        if (!Rtl)
            return SnapFixed(position);
        var itemEnd = RightToLeftItemEnd ?? (Origin ?? 0) + ExactPrefix(Text.Length);
        return SnapFixed(itemEnd - position);
    }

    /// <summary>
    /// The exact advance of a right-to-left item from its start to its end, in the units of the item origin: Blink
    /// rounds its positions from that end. Without it the text ends its item.
    /// </summary>
    internal long? RightToLeftItemEnd { get; init; }

    /// <summary>
    /// Whether a break before text[offset], inside the text, needs no reshaping: at a run boundary, or where a
    /// cluster starts that HarfBuzz marks safe, in the item's shape when the text has one (<see cref="ItemMarks"/>).
    /// Without it the text's start is safe.
    /// </summary>
    internal bool IsSafeToBreak(int Offset)
    {
        var runs = Buffers.Runs;
        // A run that starts at offset follows one that ends there.
        var index = CollectionsMarshal.AsSpan(runs).BinarySearch(new RunEnd(Offset));
        if (index >= 0 || Offset == Start && ItemMarks.IsEmpty)
            return true;

        var run = runs[~index];
        if (ItemMarks.IsEmpty) {
            EnsureRunPositions(run);
            if (Buffers.RunMarks[Offset - run.Range.Start.Value] != ClusterMark.Safe)
                return false;
        } else if (ItemMarks[Offset] != ClusterMark.Safe)
            return false;

        // Characters that Han kerning sets with their neighbors: a break between them changes both.
        return Offset == 0 || !HanKerned || !FontCache.GetShaper(run.Typeface).Kerning.UnsafeBefore(Buffers.Types[Offset], Buffers.Types[Offset - 1]);
    }

    internal int PreviousSafeToBreakOffset(int Offset, int Floor)
    {
        for (var i = Offset; i > Floor; i--)
            if (IsSafeToBreak(i)) return i;
        return Floor;
    }

    internal int NextSafeToBreakOffset(int Offset, int Ceiling)
    {
        for (var i = Offset; i < Ceiling; i++)
            if (IsSafeToBreak(i)) return i;
        return Ceiling;
    }

    /// <summary>The exact advance of text[part] shaped on its own, with the text around it as context (Blink's Shape).</summary>
    internal long Shape(Range Part, bool LineStart = false, bool HanKerningEnd = false)
    {
        var (from, length) = Part.GetOffsetAndLength(Text.Length);
        var to = from + length;
        long width = 0;
        var runs = Buffers.Runs;
        for (var index = 0;index < runs.Count;index++) {
            var run = runs[index];
            var a = Math.Max(from, run.Range.Start.Value);
            var b = Math.Min(to, run.Range.End.Value);
            if (b <= a) continue;
            if (Font is not { } font) {
                width += MeasureRun(run, a..b, LineStart && a == from, HanKerningEnd && b == to);
                continue;
            }
            // One segment goes on through the runs of its script in its bidi item, whatever their fonts.
            while (b == run.Range.End.Value && b < to && !run.ItemEnd && index + 1 < runs.Count && runs[index + 1].Script == run.Script) {
                run = runs[++index];
                b = Math.Min(to, run.Range.End.Value);
            }
            width += ReshapeSegment(a..b, run.Script, run.Direction, LineStart && a == from, font, HanKerningEnd && b == to);
        }
        return width;
    }

    /// <summary>
    /// The width of one segment of a text item shaped anew, as LineBreaker::ShapeText reshapes a line edge:
    /// HarfBuzzShaper falls back again over the segment's text, hinted by that text alone
    /// (<see cref="HarfBuzzShaper.ShapeSegment"/>), rather than keeping the item's fonts.
    /// </summary>
    private long ReshapeSegment(Range Segment, UScriptCode Script, HarfBuzzSharp.Direction Direction, bool LineStart, Font Font, bool HanKerningEnd)
    {
        var start = Segment.Start.Value;
        var classified = new HarfBuzzShaper(Text[Segment]).ShapeSegment(Font, Script, Direction, Shaping);
        long width = 0;
        foreach (var source in classified.Runs) {
            var (range, typeface, script) = source;
            var run = new WidthRun((start + range.Start.Value)..(start + range.End.Value), typeface, Direction, false, script);
            if (HanKerned)
                ClassifyHanKerning(run.Range, typeface);
            // Classification's offsets are relative to the segment. Its candidate range can extend on either
            // side of a committed run, preserving advances from the original successful glyphs.
            var shapedRange = source.ShapingRange ?? range;
            var shaped = (start + shapedRange.Start.Value)..(start + shapedRange.End.Value);
            var shaper = FontCache.GetShaper(typeface);
            var count = shaper.Kerning.GetFeatures(Text, shaped, Buffers.Types, Buffers.Features, ContextOffset(shaped), LineStart && shaped.Start.Value == start, HanKerningEnd && shaped.End.Value == Segment.End.Value);
            shaper.CharacterPositions(Text, shaped, Shaping.Buffer, Buffers.Features.AsSpan(0, count), script, Direction, Buffers.RunPositions, Buffers.RunMarks, Before, run.Range);
            width += Buffers.RunPositions[range.End.Value - range.Start.Value];
        }

        // The text's own runs keep their Han kerning types.
        if (HanKerned)
            foreach (var run in Buffers.Runs)
                if (run.Range.Start.Value < Segment.End.Value && run.Range.End.Value > start)
                    ClassifyHanKerning(Math.Max(start, run.Range.Start.Value)..Math.Min(Segment.End.Value, run.Range.End.Value), run.Typeface);
        PositionRunStart = -1;
        return width;
    }

    // Classification's positions of the runs it committed (ClassifiedText.ShapePositions); empty for given runs.
    private readonly ReadOnlySpan<long> ShapePositions;
    private readonly ReadOnlySpan<ClusterMark> ShapeMarks;

    // Whether the text's Han kerning types are in the workspace (ClassifyHanKerning).
    private readonly bool HanKerned;

    // The font the text's runs were found for, which a reshaped part falls back in again; none when its runs are given.
    private readonly Font? Font;

    /// <summary>The original shape's cluster marks for the line-break iterator.</summary>
    internal ReadOnlySpan<ClusterMark> BreakMarks()
    {
        if (!ItemMarks.IsEmpty)
            return ItemMarks;
        foreach (var run in Buffers.Runs) {
            EnsureRunPositions(run);
            var (start, length) = run.Range.GetOffsetAndLength(Text.Length);
            Buffers.RunMarks.AsSpan(0, length).CopyTo(Buffers.BreakMarks.AsSpan(start, length));
        }
        return Buffers.BreakMarks.AsSpan(0, Text.Length);
    }

    /// <summary>
    /// The exact positions of the whole text, each run shaped once as Blink's HarfBuzzShaper shapes it:
    /// <paramref name="Prefixes"/>[k] is the advance before text[k], [length] the total, nothing snapped, and
    /// <paramref name="Marks"/>[k] whether a cluster starts at k and is safe to break before.
    /// </summary>
    internal void FillPositions(Span<long> Prefixes, Span<ClusterMark> Marks)
    {
        long total = 0;
        foreach (var run in Buffers.Runs) {
            EnsureRunPositions(run);
            var (start, length) = run.Range.GetOffsetAndLength(Text.Length);
            for (var x = 0;x < length;x++) {
                Prefixes[start + x] = total + Buffers.RunPositions[x];
                Marks[start + x] = Buffers.RunMarks[x];
            }
            total += Buffers.RunPositions[length];
        }
        Prefixes[Text.Length] = total;
    }

    /*
     * The text's positions in the shape of its item, Blink's original ShapeResult: ItemShape[i] is the exact 16.16
     * advance before text[i] (from any origin), and ItemMarks[i] its cluster mark. A text without them is shaped run by
     * run. Reshaped parts of a line are shaped alone either way.
     */
    internal ReadOnlySpan<long> ItemShape { get; init; }
    internal ReadOnlySpan<ClusterMark> ItemMarks { get; init; }

    /// <summary>The exact 16.16 advance of text[Start..end) for a single bidi item, with nothing snapped. Not for the fast path.</summary>
    internal long ExactPrefix(int End)
    {
        if (!ItemShape.IsEmpty)
            return ItemShape[End] - ItemShape[Start];
        var (rounded, open) = PrefixAt(End);
        return rounded + open;
    }

    /// <summary>
    /// Blink sums glyph advances as exact 16.16 values (InlineLayoutUnit) and rounds up to
    /// 1/64 px only where it snaps: once per bidi item. Float sums across runs can land a
    /// unit below an exact boundary, so the accumulation stays in fixed point.
    /// </summary>
    private float MeasureUncached(int End)
    {
        var (rounded, open) = PrefixAt(End);
        return HarfBuzz.FixedToFloat(rounded + SnapFixed(open));
    }

    /// <summary>
    /// The prefix as (snapped bidi items before it, unsnapped advance of the current item).
    /// </summary>
    private (long Rounded, long Open) PrefixAt(int End)
    {
        var runs = Buffers.Runs;
        /*
         * Runs before `cut` lie entirely within the slice; run `cut`, if any, is cut short by `end`.
         * Run ends strictly increase, so an exact match at k means runs 0..k end by `end`.
         */
        var index = CollectionsMarshal.AsSpan(runs).BinarySearch(new RunEnd(End));
        var cut = index >= 0 ? index + 1 : ~index;
        for (;MeasuredRuns < cut;MeasuredRuns++) {
            var i = MeasuredRuns;
            var before = Buffers.Prefixes[i];
            var open = before.Open + RunAdvance(runs[i]);
            // A positioned text keeps its item open: its last width is an exact difference, not a snapped one.
            Buffers.Prefixes[i + 1] = runs[i].ItemEnd && Origin == null ? new(before.Rounded + SnapFixed(open), 0) : new(before.Rounded, open);
        }
        var prefix = Buffers.Prefixes[cut];
        var partial = cut < runs.Count ? PartialRun(runs[cut], End) : 0;
        return (prefix.Rounded, prefix.Open + partial);
    }

    /// <summary>
    /// A prefix that ends inside a run takes its position from the whole run's shape, as Blink's
    /// CachedPositionForOffset does (HarfBuzz.CharacterPositions).
    /// </summary>
    private long PartialRun(in WidthRun Run, int End)
    {
        var stop = Math.Min(End, Run.Range.End.Value);
        if (stop <= Run.Range.Start.Value)
            return 0;
        EnsureRunPositions(Run);
        return Buffers.RunPositions[stop - Run.Range.Start.Value];
    }

    /// <summary>
    /// The run's advance in its original shape: from its character positions when they are at hand, or when the text
    /// after the run shares its original candidate shape.
    /// </summary>
    private long RunAdvance(in WidthRun Run)
    {
        if (PositionRunStart != Run.Range.Start.Value && (Run.ShapingRange == null || Run.Range.Equals(Run.ShapingRange.Value)))
            return MeasureRun(Run, Run.Range);
        EnsureRunPositions(Run);
        return Buffers.RunPositions[Run.Range.End.Value - Run.Range.Start.Value];
    }

    private void EnsureRunPositions(in WidthRun Run)
    {
        if (PositionRunStart == Run.Range.Start.Value)
            return;

        var shaper = FontCache.GetShaper(Run.Typeface);
        var shaped = Run.ShapingRange ?? Run.Range;
        var length = Run.Range.End.Value - Run.Range.Start.Value;
        var count = shaper.Kerning.GetFeatures(Text, shaped, Buffers.Types, Buffers.Features, ContextOffset(shaped));

        // Classification shaped the run without features or the text before it: the same shape when neither applies.
        if (Run.CachedShape is { } cached && !ShapePositions.IsEmpty && count == 0 && ContextOffset(shaped) == 0) {
            ShapePositions.Slice(cached, length + 1).CopyTo(Buffers.RunPositions);
            ShapeMarks.Slice(cached, length).CopyTo(Buffers.RunMarks);
            PositionRunStart = Run.Range.Start.Value;
            return;
        }

        shaper.CharacterPositions(Text, shaped, Shaping.Buffer, Buffers.Features.AsSpan(0, count), Run.Script, Run.Direction,
            Buffers.RunPositions.AsSpan(0, length + 1), Buffers.RunMarks.AsSpan(0, length), Before, Run.Range);
        PositionRunStart = Run.Range.Start.Value;
    }

    private readonly struct RunEnd(int End) : IComparable<WidthRun>
    {
        public int CompareTo(WidthRun Run) => End.CompareTo(Run.Range.End.Value);
    }

    private readonly long MeasureRun(in WidthRun Run, Range Part, bool LineStart = false, bool HanKerningEnd = false)
    {
        var shaper = FontCache.GetShaper(Run.Typeface);
        var count = shaper.Kerning.GetFeatures(Text, Part, Buffers.Types, Buffers.Features, ContextOffset(Part), LineStart, HanKerningEnd);
        return shaper.MeasureFixed(Text, Part, Shaping.Buffer, Run.Direction, Buffers.Features.AsSpan(0, count), Run.Script, Before);
    }

    // A part at the text's start is shaped after its context, so its clusters count from the context's start.
    private readonly int ContextOffset(Range Part) => Part.GetOffsetAndLength(Text.Length).Offset == 0 ? -HarfBuzz.ContextLength(Before) : 0;


    // LayoutUnit's 1/64 px granularity in 16.16 units.
    private const int LAYOUT_UNIT_FIXED = HarfBuzz.FIXED_ONE / 64;

    /// <summary>
    /// Rounds 16.16 units up to a whole 1/64 px, LayoutUnit's granularity, negative widths included. The unit is a
    /// power of two, so masking its low bits floors in two's complement where division would truncate toward zero.
    /// </summary>
    internal static long SnapFixed(long Units) => (Units + LAYOUT_UNIT_FIXED - 1) & -LAYOUT_UNIT_FIXED;
}
