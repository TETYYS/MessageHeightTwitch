using System;
using System.Buffers;
using System.Collections.Generic;

using HarfBuzzSharp;

using MessageHeightTwitch.Measurement.EmoteRewriting;
using MessageHeightTwitch.Measurement.Shared.Shaping;

namespace MessageHeightTwitch.Measurement.Shared;

// Source identity belongs to the chat adapter. Shaping and line fitting use paragraph offsets.
internal enum PARAGRAPH_REGION
{
    LOGIN,
    DISPLAY_NAME,
    SEPARATOR,
    BODY
}

internal readonly record struct ParagraphItem(int Start, int End, bool Bold, bool BreakAll, PARAGRAPH_REGION Region)
{
    internal bool Italic { get; init; }
    internal EmoteLayout? Image { get; init; }
    internal int Group { get; init; }
}

internal readonly record struct ParagraphShapeGroup(int Start, int End, bool Bold, bool Italic, int PositionStart, Font Font, bool Marked);

/// <summary>
/// Retained output of stage 2 (inline collection) and stage 3 (paragraph shaping), consumed by stage 4 (line fitting).
/// Corresponds to Blink's InlineItemsData. These results survive shaping
/// scratch reuse and are consumed by the line breaker; iterator and fallback state belong to their stages.
/// </summary>
/// <remarks>
/// Cache usage: per measurement workspace, five rented text/position/mark arrays (about 62 KiB of payload
/// with usual power-of-two pool buckets), plus lists initially sized for 503 items, 512 groups and three sets of
/// 1,024 runs/items. Results are replaced per paragraph and capacities survive reuse. Array payload excludes
/// headers and list storage; Dispose returns the rented arrays to their shared pools.
/// </remarks>
internal sealed class InlineItemsData : IDisposable
{
    internal const char OBJECT_REPLACEMENT = '\uFFFC';
    internal char[] TextBuffer = ArrayPool<char>.Shared.Rent(MessageHeightTwitch.MAX_PARAGRAPH_UTF16 + 1);
    internal int Length;
    internal int BodyStart;
    internal bool Ascii;
    internal ReadOnlySpan<char> Text => TextBuffer.AsSpan(0, Length);
    // At most one item per message rune, plus two text names and the separator.
    internal readonly List<ParagraphItem> Items = new(MessageHeightTwitch.MAX_MESSAGE_RUNES + 3);
    internal readonly List<ParagraphShapeGroup> Groups = new(512);
    internal readonly List<FontRun> Runs = new(1024);
    internal readonly List<(int End, Direction Direction)> Bidi = new(1024);
    internal readonly List<LineItem> LineItems = new(1024);
    internal long[] GroupPositions = ArrayPool<long>.Shared.Rent(2048);
    internal int[] SourceOffsets = ArrayPool<int>.Shared.Rent(2048);
    internal long[] Positions = ArrayPool<long>.Shared.Rent(MessageHeightTwitch.MAX_PARAGRAPH_UTF16 * 2 + 2);
    internal ClusterMark[] Marks = ArrayPool<ClusterMark>.Shared.Rent(MessageHeightTwitch.MAX_PARAGRAPH_UTF16 + 1);

    internal void Clear()
    {
        Length = BodyStart = 0;
        Ascii = false;
        Items.Clear();
        Groups.Clear();
        Runs.Clear();
        Bidi.Clear();
        LineItems.Clear();
    }

    internal void SliceBidi(int Start, int End, List<(int End, Direction Direction)> Result, bool MergeDirections = false)
    {
        Result.Clear();
        foreach (var (end, direction) in Bidi) {
            if (end <= Start)
                continue;
            var stop = Math.Min(end, End) - Start;
            if (MergeDirections && Result.Count > 0 && Result[^1].Direction == direction)
                Result[^1] = (stop, direction);
            else
                Result.Add((stop, direction));
            if (end >= End)
                break;
        }
    }

    internal int NextCluster(int Offset, ParagraphShapeGroup Group)
    {
        if (Offset == Group.Start || Offset == Group.End)
            return Offset;
        while (Offset < Group.End && Marks[Offset] == ClusterMark.None)
            Offset++;
        return Offset;
    }

    internal int LineItemAt(int Offset)
    {
        int first = 0, after = LineItems.Count;
        while (first < after) {
            var middle = (first + after) / 2;
            if (LineItems[middle].End <= Offset)
                first = middle + 1;
            else
                after = middle;
        }
        return first;
    }

    internal long Position(int Item, int Offset)
    {
        var item = LineItems[Item];
        return Positions[item.PositionStart + Offset - item.Start];
    }

    internal ReadOnlySpan<char> ContextBefore(int Offset, Span<char> Scratch)
    {
        var take = Math.Min(Offset, Scratch.Length);
        Text.Slice(Offset - take, take).CopyTo(Scratch[^take..]);
        return Scratch[^take..];
    }

    public void Dispose()
    {
        ArrayPool<char>.Shared.Return(TextBuffer);
        ArrayPool<long>.Shared.Return(GroupPositions);
        ArrayPool<int>.Shared.Return(SourceOffsets);
        ArrayPool<long>.Shared.Return(Positions);
        ArrayPool<ClusterMark>.Shared.Return(Marks);
    }

    // One style and bidi level; Group identifies the compatible original shape.
    internal readonly record struct LineItem(int Start, int End, bool Rtl, bool Marked, int Group)
    {
        internal EmoteLayout? Image { get; init; }
        internal bool BreakAll { get; init; }
        internal int PositionStart { get; init; }
        internal Font? Font { get; init; }
    }
}
