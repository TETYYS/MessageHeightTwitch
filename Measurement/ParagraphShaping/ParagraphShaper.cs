using MessageHeightTwitch.Measurement.Shared;
using MessageHeightTwitch.Measurement.Shared.Shaping;

using System;
using System.Runtime.InteropServices;

using Direction = HarfBuzzSharp.Direction;

namespace MessageHeightTwitch.Measurement.ParagraphShaping;

/// <summary>
/// Stage 3 (paragraph shaping): resolves bidirectional text runs, groups adjacent text of the same font weight,
/// and shapes each group with font fallback. Stores font runs, glyph-cluster positions and safe-to-break markers
/// in <see cref="InlineItemsData"/>, then projects the measured advances onto items with a single style and direction
/// for stage 4 (<see cref="ParagraphLineBreaker"/>).
/// </summary>
internal readonly ref struct ParagraphShaper
{
    private const int CONTEXT_LENGTH = 5;
    private readonly FontFallbackList Fonts;
    private readonly InlineItemsData Paragraph;
    private readonly ParagraphShapingWorkspace Ws;
    private readonly ShapingWorkspace Shaping;
    private readonly Range? LoginRange;
    private ReadOnlySpan<char> Text => Paragraph.Text;
    private int ParagraphLength => Paragraph.Length;
    private bool AsciiMessage => Paragraph.Ascii;

    internal ParagraphShaper(FontFallbackList Fonts, InlineItemsData Paragraph, ParagraphShapingWorkspace Ws, ShapingWorkspace Shaping, Range? LoginRange = null)
    {
        this.Fonts = Fonts;
        this.Paragraph = Paragraph;
        this.Ws = Ws;
        this.Shaping = Shaping;
        this.LoginRange = LoginRange;
    }

    /// <summary>Resolves the complete paragraph, shapes compatible text, then projects actual clusters onto items.</summary>
    internal void Prepare()
    {
        Shaping.BidiResolver.GetBidi(Text, Paragraph.Bidi);
        PooledBuffers.Ensure(ref Paragraph.Marks, ParagraphLength + 1);
        Paragraph.Marks.AsSpan(0, ParagraphLength).Fill(ClusterMark.Safe);
        PooledBuffers.Ensure(ref Paragraph.GroupPositions, ParagraphLength + Paragraph.Items.Count + 1);
        var items = Paragraph.Items;
        var groups = Paragraph.Groups;
        var position = 0;
        for (var first = 0; first < items.Count;) {
            if (items[first].Image != null) {
                first++;
                continue;
            }
            var last = first + 1;
            while (last < items.Count && items[last].Image == null &&
                items[last].Start == items[last - 1].End && items[last].Bold == items[first].Bold && items[last].Italic == items[first].Italic)
                last++;
            var index = groups.Count;
            groups.Add(new(items[first].Start, items[last - 1].End, items[first].Bold, items[first].Italic, position, default, false));
            for (var item = first; item < last; item++)
                items[item] = items[item] with { Group = index };
            position += items[last - 1].End - items[first].Start + 1;
            first = last;
        }

        Ws.Script.Reset();
        for (var index = 0; index < groups.Count; index++) {
            var group = groups[index];
            var text = Text[group.Start..group.End];
            groups[index] = group with { Font = new(Fonts, group.Bold, group.Italic) };
            ShapeParagraphGroup(index, AllowAscii: true, CollectRuns: true);
            AdvanceScript(text);
        }

        BuildLineItems();
    }

    /// <summary>Shapes one complete compatible range; font fallback sees the same range as HarfBuzz.</summary>
    private readonly void ShapeParagraphGroup(int Index, bool AllowAscii, bool CollectRuns)
    {
        var group = Paragraph.Groups[Index];
        var text = Text[group.Start..group.End];

        Paragraph.SliceBidi(group.Start, group.End, Ws.Bidi, MergeDirections: true);
        var classified = new HarfBuzzShaper(text).Shape(group.Font, Ws.Script, Shaping, CollectionsMarshal.AsSpan(Ws.Bidi));
        if (CollectRuns) {
            foreach (var run in classified.Runs) {
                Paragraph.Runs.Add(run with {
                    Range = (group.Start + run.Range.Start.Value)..(group.Start + run.Range.End.Value),
                    ShapingRange = run.ShapingRange is { } shape
                        ? (group.Start + shape.Start.Value)..(group.Start + shape.End.Value) : null
                });
            }
        }

        var positions = Paragraph.GroupPositions.AsSpan(group.PositionStart, text.Length + 1);
        var table = group.Bold ? Fonts.AsciiBold : Fonts.AsciiRegular;
        var login = LoginRange is { } range && group.Start == range.Start.Value && group.End == range.End.Value;

        var marked = !AllowAscii || (login ? !table.TryFillLoginPrefixes(text, positions) : !AsciiMessage || !table.TryFillPrefixes(text, positions));
        if (marked) {
            Span<char> scratch = stackalloc char[CONTEXT_LENGTH];
            var widths = new TextWidths(classified, Shaping, Ws.Widths, Before: Paragraph.ContextBefore(group.Start, scratch));
            widths.FillPositions(positions, Paragraph.Marks.AsSpan(group.Start, text.Length));
        } else
            Paragraph.Marks.AsSpan(group.Start, text.Length).Fill(ClusterMark.Safe);
        Paragraph.Groups[Index] = group with {
            Marked = marked
        };
    }

    /// <summary>Style and bidi boundaries retain distinct widths even when they share a shaping group.</summary>
    private readonly void BuildLineItems()
    {
        var lineItems = Paragraph.LineItems;
        lineItems.Clear();
        PooledBuffers.Ensure(ref Paragraph.Positions, ParagraphLength + Paragraph.Items.Count + Paragraph.Bidi.Count + 1);
        var position = 0;
        foreach (var item in Paragraph.Items) {
            if (item.Image is { } image) {
                lineItems.Add(new(item.Start, item.End, false, true, -1) {
                    Image = image, BreakAll = item.BreakAll, PositionStart = position
                });
                continue;
            }
            var group = Paragraph.Groups[item.Group];
            for (var at = item.Start; at < item.End;) {
                var bidi = 0;
                while (Paragraph.Bidi[bidi].End <= at)
                    bidi++;
                var end = Math.Min(item.End, Paragraph.Bidi[bidi].End);
                var lineItem = new InlineItemsData.LineItem(at, end,
                    Paragraph.Bidi[bidi].Direction == Direction.RightToLeft, group.Marked, item.Group) {
                    BreakAll = item.BreakAll, PositionStart = position,
                    Font = group.Font
                };
                lineItems.Add(lineItem);
                ProjectShape(lineItems.Count - 1);
                position += end - at + 1;
                at = end;
            }
        }
    }

    /// <summary>
    /// CopyRange assigns a cluster's advance to the item containing its first character. Following items keep
    /// their continuation text and zero original advance until the next cluster, so a wrapped edge can reshape it.
    /// </summary>
    private readonly void ProjectShape(int Item)
    {
        var item = Paragraph.LineItems[Item];
        var group = Paragraph.Groups[item.Group];
        var original = Paragraph.GroupPositions.AsSpan(group.PositionStart, group.End - group.Start + 1);
        var ownedStart = Paragraph.NextCluster(item.Start, group);
        var ownedEnd = Paragraph.NextCluster(item.End, group);
        var origin = original[ownedStart - group.Start];
        var output = Paragraph.Positions.AsSpan(item.PositionStart, item.End - item.Start + 1);
        for (var at = item.Start; at < item.End; at++)
            output[at - item.Start] = at < ownedStart ? 0 : original[at - group.Start] - origin;
        output[^1] = original[ownedEnd - group.Start] - origin;
        Paragraph.LineItems[Item] = item with { Marked = group.Marked };
    }

    /// <summary>
    /// Materializes a table-backed shape only when a wrapped or unsafe edge needs its actual marks.
    /// </summary>
    internal void EnsureItemShape(int Item)
    {
        var item = Paragraph.LineItems[Item];
        if (item.Marked || item.Image != null)
            return;
        Ws.Script.Reset();
        for (var index = 0; index < item.Group; index++) {
            var previous = Paragraph.Groups[index];
            AdvanceScript(Text[previous.Start..previous.End]);
        }
        ShapeParagraphGroup(item.Group, AllowAscii: false, CollectRuns: false);
        for (var index = 0; index < Paragraph.LineItems.Count; index++)
            if (Paragraph.LineItems[index].Group == item.Group)
                ProjectShape(index);
    }

    private readonly void AdvanceScript(ReadOnlySpan<char> Part)
    {
        if (AsciiMessage)
            Ws.Script.AdvanceAsciiMessage(Part);
        else
            Ws.Script.Segment(Part, Shaping.Scripts, Shaping.Scratch);
    }


}
