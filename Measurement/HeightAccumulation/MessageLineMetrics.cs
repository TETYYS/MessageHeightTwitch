using MessageHeightTwitch.Measurement.EmoteRewriting;

using System;
using System.Drawing;

namespace MessageHeightTwitch;

/// <summary>
/// Geometry helper for stage 4 (line fitting) and stage 5 (height accumulation): computes inline image and line-box
/// metrics for the fixed chat style.
/// </summary>
internal static class LineMetrics
{
    private const float EMOTE_VERTICAL_MARGINS = -10;

    internal static SizeF InlineSize(EmoteLayout Image, bool EmoteMargin)
    {
        var size = Image.Size;
        if (EmoteMargin)
            size.Height = MiddleAlignedLineHeight(size.Height + EMOTE_VERTICAL_MARGINS);
        return size;
    }

    internal static float MiddleAlignedLineHeight(float MarginBoxHeight)
    {
        const float halfXHeight = 3.8125f;
        var half = MarginBoxHeight / 2;
        return Math.Max(16, half + halfXHeight) + Math.Max(6, half - halfXHeight);
    }
}

/// <summary>
/// State for stage 5 (height accumulation): the line being filled and the total height of committed lines.
/// Item cursors belong to the line controller.
/// </summary>
internal struct LineLayoutState(SizeF FirstLine, float LineFloor)
{
    // The minimum height of a line.
    internal readonly float LineFloor = LineFloor;
    // The line being filled, and the height of the lines before it.
    internal SizeF Current = FirstLine;
    internal float Height;
    /*
     * Whether nothing has been laid out on the line yet, LineBreaker's WhitespaceState::kLeading: a collapsible
     * space there is skipped, and text there starts a wrapped line. Any text or atomic inline ends it, even
     * one without an advance.
     */
    internal bool Leading = true;

    /// <summary>
    /// Ends the current line and starts an empty <paramref name="Next"/>.
    /// </summary>
    internal void EndLine(SizeF Next)
    {
        Height += Math.Max(Current.Height, LineFloor);
        Current = Next;
        Leading = true;
    }

    /// <summary>
    /// The message's height, with the last line and <paramref name="Padding"/> around the whole message.
    /// </summary>
    internal readonly float Finish(float Padding) => Height + Math.Max(Current.Height, LineFloor) + Padding;
}
