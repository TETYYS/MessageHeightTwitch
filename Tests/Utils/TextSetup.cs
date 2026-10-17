using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using MessageHeightTwitch;
using MessageHeightTwitch.Measurement.LineFitting;
using MessageHeightTwitch.Measurement.Shared;
using MessageHeightTwitch.Measurement.Shared.Shaping;

using Tests.Utils;

using Buffer = HarfBuzzSharp.Buffer;
using Mht = MessageHeightTwitch.MessageHeightTwitch;

namespace Tests;

/// <summary>Texts classified, segmented and measured the ways the tests set them up.</summary>
internal static class TextSetup
{
    // Lays out the name and separator before the message text that ClassifyMessage classifies.
    private static readonly Mht Measurer = Fixture.CreateTextMeasurer(null);

    internal static ClassifiedText Classify(this FontFallbackList Fonts, ReadOnlySpan<char> Text, bool Bold, UScript.ScriptContext Context, ShapingWorkspace Workspace, ReadOnlySpan<(int End, HarfBuzzSharp.Direction Direction)> Bidi = default)
    {
        return new HarfBuzzShaper(Text).Shape(new(Fonts, Bold), Context, Workspace, Bidi);
    }

    internal static List<FontRun> ClassifyText(string Text, bool Bold)
    {
        return FontFallbackList.Shared.ClassifyText(Text, Bold, out _);
    }

    internal static List<FontRun> ClassifyText(this FontFallbackList FallbackList, ReadOnlySpan<char> Text, bool Bold, out float? ShapedWidth, UScriptCode PrecedingScript = UScriptCode.USCRIPT_COMMON, UScript.ScriptContext? ScriptContext = null)
    {
        var workspace = MeasurementWorkspace.Pool.Get();
        try {
            var context = ScriptContext ?? new UScript.ScriptContext(PrecedingScript);
            var classified = FallbackList.Classify(Text, Bold, context, workspace.Shaping);
            ShapedWidth = classified.ShapedWidth;
            return [.. classified.Runs];
        } finally {
            MeasurementWorkspace.Pool.Return(workspace);
        }
    }

    /// <summary>
    /// A text classified with its own shaping segments and fallback candidates.
    /// </summary>
    internal static ClassifiedText ClassifyAlone(this FontFallbackList FallbackList, ReadOnlySpan<char> Text, MeasurementWorkspace Workspace)
    {
        return FallbackList.Classify(Text, false, new UScript.ScriptContext(), Workspace.Shaping);
    }

    /// <summary>
    /// A message's text classified after the names and separator before it, as in a paragraph.
    /// </summary>
    internal static ClassifiedText ClassifyAfter(this FontFallbackList FallbackList, string Before, ReadOnlySpan<char> Text, MeasurementWorkspace Workspace)
    {
        var context = new UScript.ScriptContext();
        context.Advance(Before);
        Workspace.Shaping.BidiResolver.GetBidi(Before + Text.ToString(), Workspace.Paragraph.Bidi);
        Workspace.LineBreaking.PieceBidi.Clear();
        foreach (var (end, direction) in Workspace.Paragraph.Bidi) {
            if (end > Before.Length)
                Workspace.LineBreaking.PieceBidi.Add((end - Before.Length, direction));
        }
        return FallbackList.Classify(Text, false, context, Workspace.Shaping, CollectionsMarshal.AsSpan(Workspace.LineBreaking.PieceBidi));
    }

    /// <summary>
    /// The message text as the layout classifies it after the name TETYYS and the separator, from the characters the
    /// separator's cluster took (<paramref name="Lead"/>) on, and the width of the line before it.
    /// </summary>
    internal static ClassifiedText ClassifyMessage(string Text, MeasurementWorkspace Workspace, out int Lead, out float Indent, bool IsAction = false)
    {
        var parameters = Mht.CalculateMessageHeightParams.Default;
        parameters.IsAction = IsAction;
        return Measurer.ClassifyMessageText(Text, "tetyys", "TETYYS", parameters, Workspace, out Lead, out Indent);
    }

    /// <summary>
    /// A text classified into the given runs, with its bidi items found in the workspace.
    /// </summary>
    internal static ClassifiedText Classified(ReadOnlySpan<char> Text, List<FontRun> Runs, MeasurementWorkspace Workspace, float? ShapedWidth = null)
    {
        Workspace.Shaping.BidiResolver.GetBidi(Text, Workspace.Shaping.Bidi);
        return new(Text, CollectionsMarshal.AsSpan(Runs), CollectionsMarshal.AsSpan(Workspace.Shaping.Bidi), ShapedWidth);
    }

    internal static List<(Range Range, UScriptCode Script)> GetScriptSegments(ReadOnlySpan<char> Text, UScript.ScriptContext PrecedingContext)
    {
        var result = new List<(Range Range, UScriptCode Script)>();
        var probe = new UScript.ScriptContext();
        probe.CopyFrom(PrecedingContext);
        probe.Segment(Text, result);
        return result;
    }

    internal static void Advance(this UScript.ScriptContext Context, ReadOnlySpan<char> Text)
    {
        Context.Segment(Text, new List<(Range Range, UScriptCode Script)>());
    }

    internal static Range? FindBreak(ReadOnlySpan<char> Text, bool Character, List<FontRun> FontRuns, float Maximum)
    {
        using var workspace = new MeasurementWorkspace();
        var widths = new TextWidths(Classified(Text, FontRuns, workspace), workspace.Shaping, workspace.LineBreaking.Widths);
        return widths.FindBreak(workspace.LineBreaking, Character, Maximum, 0);
    }

    internal static Range? FindBreak(this ref TextWidths Widths, LineBreakingWorkspace Workspace, bool Character, float Maximum, float TrailingWidth)
    {
        var text = Widths.Text;
        var withObject = TrailingWidth > 0 && !Character;
        var segments = Workspace.Boundaries;
        Workspace.Iterators.GetBreaks(withObject ? LineBreakIterator.WithFollowingObject(text, Workspace) : text,
            Character, segments);
        for (var i = 0; i < segments.Count; i++) {
            var end = Math.Min(segments[i].End, text.Length);
            var includesObject = Character ? end == text.Length : segments[i].End > text.Length;
            if (Widths.Measure(end) + (includesObject ? TrailingWidth : 0) > Maximum)
                return Math.Min(segments[i].Start, text.Length)..Math.Min(segments[i].End, text.Length);
        }
        return null;
    }

    internal static bool SupportedByFont(this HarfBuzz Shaper, string Text)
    {
        using var buffer = new Buffer();
        return Shaper.SupportedByFont(Text.AsSpan(), buffer);
    }

    internal static float MeasureWidth(this HarfBuzz Shaper, string Text)
    {
        using var buffer = new Buffer();
        var types = Text.Select(c => Shaper.Kerning.GetCharType(c)).ToArray();
        return HarfBuzz.FixedToFloat(BlinkLineModel.Shape(Shaper, Text, .., types, buffer));
    }
}
