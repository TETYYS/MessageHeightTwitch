using System;

using HarfBuzzSharp;

namespace MessageHeightTwitch.Measurement.Shared.Shaping;

/// <summary>
/// A classified run: one font and one script, which Blink passes to HarfBuzz.
/// </summary>
/// <remarks>
/// Runs built from a range and font alone leave the script to HarfBuzz's guess.
/// </remarks>
internal readonly record struct FontRun(Range Range, Typeface TypeFace, UScriptCode Script = UScriptCode.USCRIPT_INVALID_CODE)
{
    // The candidate range whose successful HarfBuzz clusters produced this run. Replaying it preserves the
    // original glyph advances when neighboring missing clusters subsequently take another font.
    internal Range? ShapingRange { get; init; }
    // Where the run's positions start in ClassifiedText.ShapePositions, read from the candidate's shape.
    internal int? CachedShape { get; init; }
}

/// <summary>
/// Temporary font-shaping result used in stage 3 (paragraph shaping) and stage 4 (line fitting).
/// A text classified for measuring (<see cref="HarfBuzzShaper.Shape"/>): its runs of one font and one script, its
/// bidi items, and its width when classification already shaped it as one run. The runs and items are kept in the
/// workspace that classified the text, until that workspace classifies another.
/// </summary>
internal readonly ref struct ClassifiedText(ReadOnlySpan<char> Text, ReadOnlySpan<FontRun> Runs, ReadOnlySpan<(int End, Direction Direction)> Bidi, float? ShapedWidth = null)
{
    internal ReadOnlySpan<char> Text { get; } = Text;
    internal ReadOnlySpan<FontRun> Runs { get; } = Runs;
    internal ReadOnlySpan<(int End, Direction Direction)> Bidi { get; } = Bidi;
    internal float? ShapedWidth { get; } = ShapedWidth;
    // The font the runs were found for, which a reshaped part falls back in again; none when the runs are given.
    internal Font? Font { get; init; }
    /*
     * Each committed run's positions and cluster marks as classification read them from its candidate's shape
     * (FontRun.CachedShape), so that measuring the text need not shape the run again. Empty for given runs.
     */
    internal ReadOnlySpan<long> ShapePositions { get; init; }
    internal ReadOnlySpan<ClusterMark> ShapeMarks { get; init; }
}
