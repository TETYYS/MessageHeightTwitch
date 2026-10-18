using System;
using System.Linq;
using MessageHeightTwitch;
using HarfBuzzSharp;
using Icu;
using Xunit;
using MessageHeightTwitch.Measurement.Shared.Shaping;
using MessageHeightTwitch.Measurement.Shared;

namespace Tests;

public class FontFallbackShapingTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SystemFallbackCanFailEvenWhenABundledFontHasTheGlyph(bool Bold)
    {
        var cache = FontFallbackList.Shared.Cache;
        Assert.True(cache.GetTypeface("MyanmarText-Regular.ttf").HasGlyph(0x116D5));
        Assert.Null(cache.FallbackFontForCharacter(new System.Text.Rune(0x116D5), Bold, EmojiPresentation.PRIORITY.TEXT));
    }

    [Fact]
    public void SeparatorContinuationEndsBeforeFollowingVisibleCharacter()
    {
        using var workspace = new MeasurementWorkspace();
        TextSetup.ClassifyMessage("\u200C\u06DD", workspace, out var lead, out _);
        Assert.Equal(0, lead);
    }

    [Fact]
    public void IdeographicSpaceReachesAFontWithAnActualGlyph()
    {
        var fonts = FontFallbackList.Shared;
        var css = FontCache.GetShaper(fonts.FullUnicodeFontPriority[0].TypeFace);
        Assert.False(css.HasGlyph(0x3000));

        using var buffer = new HarfBuzzSharp.Buffer();
        css.Shape("\u3000", buffer);
        Assert.True(css.IsSyntheticIdeographicSpace('\u3000', Assert.Single(buffer.GetGlyphInfoSpan().ToArray()).Codepoint));

        using var workspace = new MeasurementWorkspace();
        var run = Assert.Single(fonts.ClassifyAlone("\u3000", workspace).Runs.ToArray());
        Assert.True(run.TypeFace.HasGlyph(0x3000));
    }
    /// <summary>
    /// A successful cluster retains its candidate's glyph advances even when another cluster falls back. Check
    /// both sides of the failed cluster against glyphs of the original candidate shape, independently of replay.
    /// </summary>
    [Theory]
    [InlineData(false, "Yó\u0302AV", false)]
    [InlineData(true, "Yó\u0302AV", false)]
    [InlineData(false, "بب\u0302بب", true)]
    [InlineData(true, "بب\u0302بب", true)]
    public void SuccessfulClustersKeepTheirOriginalCandidateShape(bool Bold, string Text, bool Rtl)
    {
        var fonts = FontFallbackList.Shared;
        using var workspace = new MeasurementWorkspace();
        var classified = fonts.Classify(Text, Bold, new UScript.ScriptContext(), workspace.Shaping);
        var candidate = Rtl ? fonts.FontPriority.Single(font => font.Font == "NotoSansArabic-Regular.ttf").GetTypeFace(Bold) : (Bold ? fonts.AsciiBold : fonts.AsciiRegular).Typeface;
        var committed = classified.Runs.ToArray().Where(run => run.TypeFace == candidate).ToArray();

        Assert.Equal([0..1, 3..5], committed.Select(run => run.Range));
        Assert.All(committed, run => Assert.Equal(0..Text.Length, run.ShapingRange));

        using var buffer = new HarfBuzzSharp.Buffer();
        var shaper = FontCache.GetShaper(candidate);
        shaper.ShapeRange(Text, .., buffer, Rtl ? Direction.RightToLeft : Direction.LeftToRight, default, Rtl ? UScriptCode.USCRIPT_ARABIC : UScriptCode.USCRIPT_LATIN);
        var infos = buffer.GetGlyphInfoSpan();
        var glyphs = buffer.GetGlyphPositionSpan();
        var widths = new TextWidths(classified, workspace.Shaping, workspace.LineBreaking.Widths);
        var positions = new long[Text.Length + 1];
        var marks = new ClusterMark[Text.Length];
        widths.FillPositions(positions, marks);
        foreach (var run in committed) {
            long expected = 0;
            for (var x = 0; x < infos.Length; x++) {
                if (infos[x].Cluster >= run.Range.Start.Value && infos[x].Cluster < run.Range.End.Value) {
                    expected += shaper.FixedAdvance(glyphs[x].XAdvance);
                }
            }
            Assert.Equal(expected, positions[run.Range.End.Value] - positions[run.Range.Start.Value]);
        }
    }

    [Theory]
    [InlineData("\u06DDب")]
    [InlineData("a\u00AD\u0302b")]
    [InlineData("ᜑ⃦⃕⃢ a")]
    public void FallbackRunsFollowHarfBuzzClusterBoundaries(string Text)
    {
        var fonts = FontFallbackList.Shared;
        using var workspace = new MeasurementWorkspace();
        var classified = fonts.ClassifyAlone(Text, workspace);
        using var buffer = new HarfBuzzSharp.Buffer();
        foreach (var run in classified.Runs) {
            var source = run.ShapingRange ?? run.Range;
            var direction = classified.Bidi.FirstDirectionAt(run.Range.Start.Value);
            FontCache.GetShaper(run.TypeFace).ShapeRange(Text, source, buffer, direction, default, run.Script);
            Assert.Contains(buffer.GetGlyphInfoSpan().ToArray(), glyph => glyph.Cluster == run.Range.Start.Value);
            if (run.Range.End.Value != source.End.Value)
                Assert.Contains(buffer.GetGlyphInfoSpan().ToArray(), glyph => glyph.Cluster == run.Range.End.Value);
        }
    }

    [Fact]
    public void MissingGlyphFallbackDoesNotReuseAnotherCandidatesWidth()
    {
        const string text = "ᜑ⃦⃕⃢";
        var fonts = FontFallbackList.Shared;
        using var workspace = new MeasurementWorkspace();
        var classified = fonts.ClassifyAlone(text, workspace);
        Assert.Null(classified.ShapedWidth);
        var shaper = FontCache.GetShaper(Assert.Single(classified.Runs.ToArray()).TypeFace);
        using var buffer = new HarfBuzzSharp.Buffer();
        var expected = TextWidths.SnapFixed(shaper.MeasureFixed(text, .., buffer, classified.Bidi[0].Direction, Script: classified.Runs[0].Script));
        Assert.Equal(HarfBuzz.FixedToFloat(expected), new TextWidths(classified, workspace.Shaping, workspace.LineBreaking.Widths).Measure(text.Length));
    }
}

internal static class FontFallbackTestDirections
{
    internal static Direction FirstDirectionAt(this ReadOnlySpan<(int End, Direction Direction)> Bidi, int Offset)
    {
        foreach (var run in Bidi)
            if (run.End > Offset)
                return run.Direction;
        throw new ArgumentOutOfRangeException(nameof(Offset));
    }
}
