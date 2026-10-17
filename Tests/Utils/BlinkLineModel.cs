using System;
using System.Collections.Generic;
using System.Linq;

using MessageHeightTwitch;
using MessageHeightTwitch.Measurement.Shared.Shaping;

using BiDi = Icu.BiDi;
using BreakIterator = Icu.BreakIterator;
using Buffer = HarfBuzzSharp.Buffer;
using Direction = HarfBuzzSharp.Direction;
using Feature = HarfBuzzSharp.Feature;

namespace Tests.Utils;

/// <summary>
/// Blink's line layout widths and breaks, computed from scratch as a reference for TextWidths, and the
/// shaping both this model and <see cref="DomRangeModel"/> build on.
/// </summary>
internal static class BlinkLineModel
{
    internal static readonly Icu.Locale BreakLocale = new("en-US");
    private static readonly Icu.Locale LineLocale = new("en-US@lb=normal");

    // One CSS layout unit is 1/64 px.
    private const float LAYOUT_UNITS_PER_PIXEL = 64f;
    private const double FIXED_LAYOUT_UNIT = HarfBuzz.FIXED_ONE / LAYOUT_UNITS_PER_PIXEL;

    /// <summary>
    /// Deliberately shape every intersection afresh as a reference for the incremental width cache.
    /// Blink's line layout width for text[range]: every bidi item and font run is shaped with the
    /// whole text as context, a left-to-right run's prefix takes its position from the run's full shape,
    /// and each item is rounded up to 1/64 px once, from exact 16.16 sums. Bidi items come from ICU
    /// directly and character positions from grouping glyphs by cluster.
    /// </summary>
    internal static float Width(ReadOnlySpan<char> Text, Range Range, List<FontRun> FontRuns, Buffer? Buffer = null)
    {
        var (start, length) = Range.GetOffsetAndLength(Text.Length);
        var end = start + length;
        if (end <= start || FontRuns.Count == 0)
            return 0;
        var types = CharTypes(Text, FontRuns);
        using var ownBuffer = Buffer is null ? new Buffer() : null;
        var buffer = Buffer ?? ownBuffer!;
        long total = 0;
        var itemStart = 0;
        foreach (var (itemEnd, direction) in BidiItems(Text)) {
            long itemWidth = 0;
            for (var index = 0; index < FontRuns.Count; index++) {
                var font = FontRuns[index];
                var runStart = Math.Max(start, Math.Max(itemStart, font.Range.Start.Value));
                var ownedEnd = Math.Min(itemEnd, font.Range.End.Value);
                var runEnd = Math.Min(end, ownedEnd);
                if (runEnd <= runStart)
                    continue;
                var shaper = FontCache.GetShaper(font.TypeFace);
                var source = font.ShapingRange ?? font.Range;
                source = Math.Max(start, source.Start.Value)..source.End;
                Shape(shaper, Text, source, types, buffer, direction, font.Script);
                itemWidth += CachedPrefix(shaper, buffer, runStart, ownedEnd, runEnd, direction);
            }
            total += CeilFixedToLayoutUnit(itemWidth);
            itemStart = itemEnd;
            if (itemStart >= end)
                break;
        }
        return HarfBuzz.FixedToFloat(total);
    }

    /// <summary>
    /// The first line or grapheme segment from ICU that ends past <paramref name="Maximum"/>, if any.
    /// </summary>
    internal static Range? Break(ReadOnlySpan<char> Text, bool Character, List<FontRun> FontRuns, float Maximum)
    {
        var text = Text.ToString();
        var type = Character ? BreakIterator.UBreakIteratorType.CHARACTER : BreakIterator.UBreakIteratorType.LINE;
        return BreakIterator.GetBoundaries(type, Character ? BreakLocale : LineLocale, text)
            .Where(segment => Width(text, ..segment.End, FontRuns) > Maximum)
            .Select(segment => (Range?)(segment.Start..segment.End))
            .FirstOrDefault();
    }

    /// <summary>
    /// Select the run's committed glyphs from its original candidate shape, then independently group their
    /// advances to recover CachedPositionForOffset. Glyphs outside the owned range belong to other font runs.
    /// </summary>
    private static long CachedPrefix(HarfBuzz Shaper, Buffer Buffer, int OwnedStart, int OwnedEnd, int End, Direction Direction)
    {
        var glyphs = Buffer.GetGlyphInfoSpan().ToArray().Zip(Buffer.GetGlyphPositionSpan().ToArray(),
            (info, position) => (Cluster: (int)info.Cluster, Advance: (long)Shaper.FixedAdvance(position.XAdvance), position.XOffset))
            .Where(glyph => glyph.Cluster >= OwnedStart && glyph.Cluster < OwnedEnd).ToArray();
        if (End == OwnedEnd)
            return glyphs.Sum(glyph => glyph.Advance);
        var advances = glyphs
            .GroupBy(glyph => glyph.Cluster)
            .ToDictionary(cluster => cluster.Key, cluster => cluster.Sum(glyph => glyph.Advance));
        if (Direction == Direction.RightToLeft) {
            // Blink's cached RTL positions count a placement kern toward the logically following cluster.
            for (var x = 0; x < glyphs.Length; x++) {
                if (glyphs[x].XOffset >= 0 || glyphs[x].Advance == 0)
                    continue;
                var previous = x - 1;
                while (previous >= 0 && glyphs[previous].Cluster == glyphs[x].Cluster)
                    previous--;
                if (previous < 0)
                    continue;
                var kern = Shaper.FixedAdvance(glyphs[x].XOffset);
                advances[glyphs[x].Cluster] -= kern;
                advances[glyphs[previous].Cluster] += kern;
            }
        }
        var boundary = advances.Keys.Where(cluster => cluster <= End).DefaultIfEmpty(OwnedStart).Max();
        return advances.Where(cluster => cluster.Key < boundary).Sum(cluster => cluster.Value);
    }

    /// <summary>
    /// The bidi items of a left-to-right paragraph as ICU resolves them: each item's end and direction.
    /// </summary>
    internal static List<(int End, Direction Direction)> BidiItems(ReadOnlySpan<char> Text)
    {
        using var bidi = new BiDi();
        bidi.SetPara(Text.ToString(), 0, null);
        var items = new List<(int End, Direction Direction)>();
        for (var start = 0; start < Text.Length;) {
            var end = bidi.GetLogicalRun(start, out var level);
            items.Add((end, level % 2 == 0 ? Direction.LeftToRight : Direction.RightToLeft));
            start = end;
        }
        return items;
    }

    /// <summary>
    /// Each character's Han kerning type in the font of the run that holds it.
    /// </summary>
    internal static HanKerning.CHAR_TYPE[] CharTypes(ReadOnlySpan<char> Text, List<FontRun> FontRuns)
    {
        var types = new HanKerning.CHAR_TYPE[Text.Length];
        foreach (var font in FontRuns) {
            var kerning = FontCache.GetShaper(font.TypeFace).Kerning;
            for (var x = font.Range.Start.Value; x < font.Range.End.Value; x++) types[x] = kerning.GetCharType(Text[x]);
        }
        return types;
    }

    /// <summary>
    /// Shapes <c>Text[Run]</c> into <paramref name="Buffer"/> with the whole text as context and the font's Han
    /// kerning features for <paramref name="Types"/>, and returns its exact 16.16 advance.
    /// </summary>
    internal static long Shape(HarfBuzz Shaper, ReadOnlySpan<char> Text, Range Run, HanKerning.CHAR_TYPE[] Types, Buffer Buffer, Direction? Direction = null, UScriptCode Script = UScriptCode.USCRIPT_INVALID_CODE)
    {
        var features = new Feature[Text.Length + 1];
        var count = Shaper.Kerning.GetFeatures(Text, Run, Types, features, 0);
        return Shaper.MeasureFixed(Text, Run, Buffer, Direction, features.AsSpan(0, count), Script);
    }

    /// <summary>
    /// Each glyph shaped into <paramref name="Buffer"/>: its cluster relative to <paramref name="RunStart"/> and its 16.16 advance.
    /// </summary>
    internal static (int Cluster, long Advance)[] Glyphs(HarfBuzz Shaper, Buffer Buffer, int RunStart) =>
        [.. Buffer.GetGlyphInfoSpan().ToArray().Zip(Buffer.GetGlyphPositionSpan().ToArray(),
            (info, position) => ((int)info.Cluster - RunStart, (long)Shaper.FixedAdvance(position.XAdvance)))];

    /// <summary>
    /// The clusters shaped into <paramref name="Buffer"/> in logical order, whatever the glyph order of the run's
    /// direction: each one's offset from <paramref name="RunStart"/>, the advance of the clusters before it and its own.
    /// </summary>
    internal static (int Cluster, long Position, long Advance)[] Clusters(HarfBuzz Shaper, Buffer Buffer, int RunStart)
    {
        var advances = Glyphs(Shaper, Buffer, RunStart)
            .GroupBy(glyph => glyph.Cluster, glyph => glyph.Advance)
            .OrderBy(cluster => cluster.Key)
            .Select(cluster => (Cluster: cluster.Key, Advance: cluster.Sum()))
            .ToArray();
        var clusters = new (int Cluster, long Position, long Advance)[advances.Length];
        long position = 0;
        for (var x = 0; x < advances.Length; x++) {
            clusters[x] = (advances[x].Cluster, position, advances[x].Advance);
            position += advances[x].Advance;
        }
        return clusters;
    }

    /// <summary>
    /// An exact 16.16 width rounded up to a whole layout unit, like Blink's LayoutUnit::FromFloatCeil.
    /// </summary>
    private static long CeilFixedToLayoutUnit(long Width) => (long)(Math.Ceiling(Width / FIXED_LAYOUT_UNIT) * FIXED_LAYOUT_UNIT);
}
