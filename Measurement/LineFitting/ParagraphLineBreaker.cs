using MessageHeightTwitch.Measurement.EmoteRewriting;
using MessageHeightTwitch.Measurement.ParagraphShaping;
using MessageHeightTwitch.Measurement.Shared;
using MessageHeightTwitch.Measurement.Shared.Shaping;

using System;
using System.Drawing;
using System.Runtime.InteropServices;

using Direction = HarfBuzzSharp.Direction;

namespace MessageHeightTwitch.Measurement.LineFitting;

/// <summary>
/// Stage 4 (line fitting): fits the paragraph prepared by <see cref="ParagraphShaper"/> into lines using
/// line-breaking scratch and explicit shaping services, returning line geometry for stage 5 (height accumulation).
/// </summary>
internal readonly ref struct ParagraphLineBreaker
{
    private const int CONTEXT_LENGTH = 5;
    private readonly InlineItemsData Paragraph;
    private readonly ParagraphShaper Shaper;
    private readonly ShapingWorkspace Shaping;
    private readonly LineBreakingWorkspace Workspace;
    private readonly float AvailableWidth;
    private readonly bool EmoteMargin;
    private ReadOnlySpan<char> Text => Paragraph.Text;
    private int ParagraphLength => Paragraph.Length;

    internal ParagraphLineBreaker(InlineItemsData Paragraph, ParagraphShaper Shaper, ShapingWorkspace Shaping,
        LineBreakingWorkspace Workspace, float AvailableWidth, bool EmoteMargin)
    {
        this.Paragraph = Paragraph;
        this.Shaper = Shaper;
        this.Shaping = Shaping;
        this.Workspace = Workspace;
        this.AvailableWidth = AvailableWidth;
        this.EmoteMargin = EmoteMargin;
    }

    private SizeF InlineSize(EmoteLayout Image) => LineMetrics.InlineSize(Image, EmoteMargin);

    /// <summary>
    /// Fits all paragraph items with per-item break policy, rewinding before committing a line.
    /// </summary>
    internal ParagraphLine NextLine(int Start, int Limit, SizeF Initial, bool Leading)
    {
        Workspace.SetStartOffset(Start);
        var anywhere = false;
        Span<char> context = stackalloc char[CONTEXT_LENGTH];
        while (true) {
            var size = Initial;
            var at = Leading ? ConsumeLineSpace(Start, Limit) : Start;
            var empty = Leading;
            Workspace.ItemResults.Clear();
            var retry = false;

            while (at < Limit) {
                var index = Paragraph.LineItemAt(at);
                var item = Paragraph.LineItems[index];
                if (item.Image is { } image) {
                    var imageSize = InlineSize(image);
                    if (size.Width + imageSize.Width > AvailableWidth && !empty) {
                        var after = new SizeF(size.Width + imageSize.Width, Math.Max(size.Height, imageSize.Height));
                        Workspace.ItemResults.Add(new(index, at, item.End, size, after, false, false, empty));
                        if (HandleOverflow(Start, Limit, after.Width, anywhere, context) is { } rewound)
                            return rewound;
                        if (anywhere && at > Start)
                            return ParagraphLineAt(at, Limit, size);
                        retry = true;
                        break;
                    }
                    var before = size;
                    size.Width += imageSize.Width;
                    size.Height = Math.Max(size.Height, imageSize.Height);
                    empty = false;
                    at = item.End;
                    Workspace.ItemResults.Add(new(index, item.Start, at, before, size,
                        !image.InlineImage || CanBreakAfterItem(at, item.BreakAll, anywhere), false, false));
                    continue;
                }

                var breaker = ItemLineBreaker(index, at, empty, context);
                var from = at - item.Start;
                var to = Math.Min(item.End, Limit) - item.Start;
                var available = HarfBuzz.FloatToFixed(AvailableWidth - size.Width);
                var trailing = item.End < Limit && index + 1 < Paragraph.LineItems.Count &&
                    Paragraph.LineItems[index + 1].Image is { InlineImage: true } following ? following.Size.Width : 0;
                var result = BreakText(ref breaker, item.Font!.Value, from..to, available, HarfBuzz.FloatToFixed(trailing), anywhere, empty);
                if (!result.IsOverflow && size.Width + result.Width > AvailableWidth) {
                    var reduced = Math.Min(available, HarfBuzz.FloatToFixed(result.Width - 1));
                    var earlier = BreakText(ref breaker, item.Font!.Value, from..to, reduced, HarfBuzz.FloatToFixed(trailing), anywhere, empty);
                    result = !earlier.IsOverflow && earlier.BreakOffset > from && earlier.BreakOffset < result.BreakOffset
                        ? earlier : result with { IsOverflow = true };
                }

                if (result.IsOverflow) {
                    var overflowWidth = Math.Max(result.Width, AvailableWidth - size.Width + 1);
                    Workspace.ItemResults.Add(new(index, at, item.Start + result.BreakOffset, size,
                        new(size.Width + overflowWidth, size.Height), false, false, empty));
                    if (HandleOverflow(Start, Limit, size.Width + overflowWidth, anywhere, context) is { } rewound)
                        return rewound;
                    Workspace.ItemResults.RemoveAt(Workspace.ItemResults.Count - 1);
                    if (anywhere && at > Start)
                        return ParagraphLineAt(at, Limit, size);
                    if (!anywhere) {
                        retry = true;
                        break;
                    }
                }
                if (result.BreakOffset < to || result.HasTrailingSpaces &&
                    size.Width + breaker.ShapeLineAt(from..result.BreakOffset, empty) > AvailableWidth) {
                    size.Width += result.Width;
                    return ParagraphLineAt(item.Start + result.BreakOffset, Limit, size);
                }

                /*
                 * A hyphen belongs to a taken break. Continuing into another item (or ending the paragraph)
                 * removes it; an overflow retry shapes its selected break again.
                 */
                var completeWidth = result.HasTrailingSpaces || result.IsHyphenated
                    ? breaker.ShapeLineAt(from..to, empty) : result.Width;
                if (item.Start + to == Limit) {
                    size.Width += completeWidth;
                    return new(Limit, Limit, size);
                }

                var beforeText = size;
                size.Width += completeWidth;
                Workspace.ItemResults.Add(new(index, at, item.End, beforeText, size,
                    BreakAfterText(index) || CanBreakAfterItem(item.End, item.BreakAll, anywhere), !result.IsOverflow, empty));
                empty = false;
                at = item.End;
            }

            if (!retry)
                return new(Limit, Limit, size);
            if (!Leading)
                return new(Start, Start, Initial);
            anywhere = true;
        }
    }

    /// <summary>
    /// LineBreaker::HandleOverflow walks committed item results backwards. It
    /// retries an earlier text item below its old width instead of selecting a
    /// saved internal opportunity whose width may still equal the full item.
    /// </summary>
    private ParagraphLine? HandleOverflow(int Start, int Limit, float Position, bool Anywhere, Span<char> Context)
    {
        var results = Workspace.ItemResults;
        var widthToRewind = Position - AvailableWidth;
        var breakBefore = -1;
        for (var index = results.Count - 1; index >= 0; index--) {
            var previous = results[index];
            if (index < results.Count - 1 && previous.CanBreakAfter && previous.End > Start) {
                if (widthToRewind <= 0)
                    return ParagraphLineAt(previous.End, Limit, previous.After);
                breakBefore = index;
            }

            var previousWidth = previous.After.Width - previous.Before.Width;
            widthToRewind -= previousWidth;
            if (widthToRewind >= 0 || !previous.MayBreakInside)
                continue;

            var item = Paragraph.LineItems[previous.Index];
            var breaker = ItemLineBreaker(previous.Index, previous.Start, previous.WrappedStart, Context);
            var from = previous.Start - item.Start;
            var to = previous.End - item.Start;
            var available = -widthToRewind;
            var cap = previousWidth - 1;
            ShapingLineBreaker.Result result;
            if (cap <= 0) {
                var opportunity = PreviousParagraphBreak(previous.End - 1, previous.Start, item.BreakAll);
                if (opportunity <= previous.Start)
                    continue;
                var end = opportunity - item.Start;
                result = new(end, end, breaker.ShapeLineAt(from..end, previous.WrappedStart), false);
            } else {
                result = BreakText(ref breaker, item.Font!.Value, from..to,
                    HarfBuzz.FloatToFixed(Math.Min(available, cap)), 0, Anywhere, previous.WrappedStart);
            }
            if (!result.IsOverflow && result.Width <= available && result.BreakOffset > from && result.BreakOffset < to) {
                var size = previous.Before;
                size.Width += result.Width;
                return ParagraphLineAt(item.Start + result.BreakOffset, Limit, size);
            }
        }
        return breakBefore >= 0 ? ParagraphLineAt(results[breakBefore].End, Limit, results[breakBefore].After) : null;
    }

    private readonly int ConsumeLineSpace(int Start, int Limit)
    {
        if (Start >= Limit || Text[Start] != ' ')
            return Start;
        var index = Paragraph.LineItemAt(Start);
        var item = Paragraph.LineItems[index];
        if (item.Image != null)
            return Start;
        // HandleTrailingSpaces consumes the collapsed U+0020 independently of HarfBuzz clusters.
        return Start + 1;
    }

    private readonly ParagraphLine ParagraphLineAt(int End, int Limit, SizeF Size)
    {
        var end = ConsumeLineSpace(End, Limit);
        // HandleTrailingSpaces keeps preserved U+3000 on this line, independently of leading-space collapse.
        while (end < Limit && ShapingLineBreaker.IsBreakableSpace(Text[end]))
            end++;
        return new(end, end, Size);
    }

    private readonly void EnsureParagraphBreaks(bool BreakAll)
    {
        var breaks = BreakAll ? Workspace.BreakAllBreaks : Workspace.NormalBreaks;
        if (breaks.Count == 0)
            LineBreakIterator.Fill(Text, Paragraph.Marks.AsSpan(0, ParagraphLength), BreakAll, Workspace, breaks, Workspace.StartOffset);
    }

    private readonly bool BreakAfterText(int Item) =>
        Item + 1 < Paragraph.LineItems.Count && Paragraph.LineItems[Item + 1].Image is { InlineImage: false };

    private readonly bool IsParagraphBreak(int Offset, bool BreakAll) =>
        PreviousParagraphBreak(Offset, Offset - 1, BreakAll) == Offset;

    private readonly bool CanBreakAfterItem(int Offset, bool BreakAll, bool Anywhere)
    {
        if (!Anywhere)
            return IsParagraphBreak(Offset, BreakAll);

        /*
         * Blink's overflow-wrap retry changes the shared iterator to
         * kBreakCharacter; CanBreakAfter must use that same active policy.
         */
        var graphemes = Workspace.ParagraphGraphemes;
        if (graphemes.Count == 0)
            Workspace.Iterators.GetBreaks(Text, true, graphemes, Workspace.StartOffset);
        foreach (var (_, end) in graphemes) {
            if (end >= Offset)
                return end == Offset;
        }
        return false;
    }

    private readonly int PreviousParagraphBreak(int End, int Floor, bool BreakAll)
    {
        EnsureParagraphBreaks(BreakAll);
        var result = Floor;
        foreach (var (_, end) in BreakAll ? Workspace.BreakAllBreaks : Workspace.NormalBreaks) {
            if (end > End)
                break;
            if (end > Floor)
                result = end;
        }
        return result;
    }

    private readonly ShapingLineBreaker ItemLineBreaker(int Item, int Start, bool WrappedStart, Span<char> Context)
    {
        if (WrappedStart)
            Shaper.EnsureItemShape(Item);
        var item = Paragraph.LineItems[Item];
        var widths = new TextWidths(ItemClassification(Item), Shaping, Workspace.Widths, Start - item.Start,
            Paragraph.Position(Item, Start), Paragraph.ContextBefore(item.Start, Context)) {
            ItemShape = Paragraph.Positions.AsSpan(item.PositionStart, item.End - item.Start + 1),
            ItemMarks = item.Marked ? Paragraph.Marks.AsSpan(item.Start, item.End - item.Start) : default,
            RightToLeftItemEnd = item.Rtl ? Paragraph.Position(Item, item.End) : null
        };
        return new ShapingLineBreaker(widths, Workspace) {
            ParagraphText = Text,
            ParagraphMarks = Paragraph.Marks.AsSpan(0, ParagraphLength),
            ParagraphOffset = item.Start,
            BreakAll = item.BreakAll,
            CollapseSpaces = true
        };
    }

    /// <summary>
    /// Retries a text fragment when its attached atomic item leaves no fitting break at the item edge.
    /// </summary>
    private ShapingLineBreaker.Result BreakText(scoped ref ShapingLineBreaker Breaker, Font Font, Range Line,
        long Available, long Trailing, bool Anywhere, bool WrappedStart)
    {
        var result = Breaker.ShapeLine(Line, Available, Anywhere, WrappedStart);
        if (result.BreakOffset == Line.End.Value && Trailing > 0 &&
            HarfBuzz.FloatToFixed(result.Width) + Trailing > Available && !Breaker.CanBreakAfter(result.BreakOffset)) {
            var reduced = Math.Min(Available, HarfBuzz.FloatToFixed(result.Width - 1));
            result = Breaker.ShapeLine(Line, reduced, Anywhere, WrappedStart);
        }
        if (result.IsHyphenated) {
            var hyphen = HyphenWidth(Font);
            // LineBreaker::BreakText reserves the separately shaped hyphen and retries ShapeLine once.
            if (!result.IsOverflow && HarfBuzz.FloatToFixed(result.Width) <= Available &&
                HarfBuzz.FloatToFixed(result.Width + hyphen) > Available)
                result = Breaker.ShapeLine(Line, Available - HarfBuzz.FloatToFixed(hyphen), Anywhere, WrappedStart);
            if (result.IsHyphenated)
                result = result with {
                    Width = result.Width + hyphen,
                    IsOverflow = result.IsOverflow || HarfBuzz.FloatToFixed(result.Width + hyphen) > Available
                };
        }
        return result;
    }

    private float HyphenWidth(Font Font)
    {
        // ComputedStyle::HyphenString prefers U+2010 when the primary font has it; HyphenResult shapes
        // the string independently in the item's CSS font, rather than reusing a fallback glyph from the word.
        var primary = (Font.Bold ? Font.FallbackList.AsciiBold : Font.FallbackList.AsciiRegular).Typeface;
        ReadOnlySpan<char> text = primary.HasGlyph(0x2010) ? "\u2010" : "-";
        var classified = new HarfBuzzShaper(text).ShapeSegment(Font, UScriptCode.USCRIPT_COMMON, Direction.LeftToRight, Shaping);
        long width = 0;
        foreach (var run in classified.Runs)
            width += FontCache.GetShaper(run.TypeFace).MeasureFixed(text, run.Range, Shaping.Buffer,
                Direction.LeftToRight, Script: run.Script);
        return HarfBuzz.FixedToFloat(TextWidths.SnapFloat(width));
    }

    internal readonly record struct ParagraphLine(int End, int Next, SizeF Size);
    private readonly ClassifiedText ItemClassification(int Item)
    {
        var item = Paragraph.LineItems[Item];
        var runs = Workspace.PieceRuns;
        runs.Clear();
        foreach (var run in Paragraph.Runs) {
            if (run.Range.End.Value <= item.Start)
                continue;
            if (run.Range.Start.Value >= item.End)
                break;

            /*
             * Original item positions already retain the complete group's shape. Range reshaping performs
             * fresh fallback on its own complete range, so these projected font runs need no replay provenance.
             */
            runs.Add(run with {
                Range = (Math.Max(run.Range.Start.Value, item.Start) - item.Start)..(Math.Min(run.Range.End.Value, item.End) - item.Start),
                ShapingRange = null
            });
        }
        Workspace.PieceBidi.Clear();
        Workspace.PieceBidi.Add((item.End - item.Start, item.Rtl ? Direction.RightToLeft : Direction.LeftToRight));
        return new(Text[item.Start..item.End], CollectionsMarshal.AsSpan(runs), CollectionsMarshal.AsSpan(Workspace.PieceBidi)) {
            Font = item.Font
        };
    }


}
