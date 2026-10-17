using System;
using System.Buffers;
using System.Collections.Generic;
using System.Drawing;

using HarfBuzzSharp;

using MessageHeightTwitch.Measurement.Shared.Shaping;

namespace MessageHeightTwitch.Measurement.LineFitting;

/// <summary>
/// Scratch for stage 4 (line fitting), including lazy break opportunities for one paragraph.
/// </summary>
/// <remarks>
/// Cache usage: per measurement workspace, one WidthBuffers, eight lists initially sized for 1,024 entries,
/// a four-entry pair-boundary list, a 32-entry overflow-results list (about 1 KiB initially),
/// at least 1,083 rented chars (about 4 KiB in the usual pool bucket), and two
/// native ICU iterators. Break results are cleared per paragraph, while capacities survive reuse. Dispose
/// releases the iterators and returns rented arrays to their shared pools; native iterator sizes vary.
/// </remarks>
internal sealed class LineBreakingWorkspace : IDisposable
{
    internal readonly TextBreakIterators Iterators = new();
    internal readonly TextWidths.WidthBuffers Widths = new();
    internal readonly List<FontRun> PieceRuns = new(1024);
    internal readonly List<(int End, Direction Direction)> PieceBidi = new(1024);
    internal readonly List<(int Start, int End)> Boundaries = new(1024);
    internal readonly List<(int Start, int End)> PairBoundaries = new(4);
    internal readonly List<(int Start, int End)> NormalBreaks = new(1024);
    internal readonly List<(int Start, int End)> BreakAllBreaks = new(1024);
    internal readonly List<(int Start, int End)> ParagraphGraphemes = new(1024);
    internal readonly List<(int Start, int End)> ItemBreaks = new(1024);
    internal readonly List<(int Start, int End)> ItemGraphemes = new(1024);
    internal readonly List<LineItemResult> ItemResults = new(32);
    internal readonly record struct LineItemResult(int Index, int Start, int End, SizeF Before, SizeF After,
        bool CanBreakAfter, bool MayBreakInside, bool WrappedStart);
    internal char[] InlineText = ArrayPool<char>.Shared.Rent(MessageHeightTwitch.MAX_PARAGRAPH_UTF16 + 3);
#if TEST
    internal List<int>? LineStarts;
#endif

    internal int StartOffset { get; private set; }

    // LazyLineBreakIterator::SetStartOffset releases both ICU iterators. Shaping keeps the full paragraph.
    internal void SetStartOffset(int Offset)
    {
        if (StartOffset == Offset)
            return;
        StartOffset = Offset;
        NormalBreaks.Clear();
        BreakAllBreaks.Clear();
        ParagraphGraphemes.Clear();
    }

    internal void BeginParagraph()
    {
        StartOffset = 0;
        NormalBreaks.Clear();
        BreakAllBreaks.Clear();
        ParagraphGraphemes.Clear();
        ItemBreaks.Clear();
        ItemGraphemes.Clear();
        PieceRuns.Clear();
        PieceBidi.Clear();
        ItemResults.Clear();
    }

    public void Dispose()
    {
        Iterators.Dispose();
        Widths.Return();
        ArrayPool<char>.Shared.Return(InlineText);
    }
}
