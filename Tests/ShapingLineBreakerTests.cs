using System;
using Xunit;
using MessageHeightTwitch;
using MessageHeightTwitch.Measurement.Shared;
using MessageHeightTwitch.Measurement.LineFitting;
using MessageHeightTwitch.Measurement.Shared.Shaping;

namespace Tests;

public class ShapingLineBreakerTests
{
    // Blink's ShapeLineZeroAvailableWidth, with the bundled font. The content offsets are independent of its metrics.
    [Theory]
    [InlineData(0, 7)]
    [InlineData(8, 16)]
    [InlineData(17, 21)]
    [InlineData(22, 28)]
    public void ZeroAvailableWidthReturnsTheFirstUnbreakableFragment(int Start, int ContentEnd)
    {
        const string text = "Testing overflow line break.";
        using var workspace = new MeasurementWorkspace();
        var breaker = new ShapingLineBreaker(new TextWidths(FontFallbackList.Shared.ClassifyAlone(text, workspace), workspace.Shaping, workspace.LineBreaking.Widths), workspace.LineBreaking) {
            CollapseSpaces = true
        };

        var result = breaker.ShapeLine(Start.., 0, WrappedStart: Start > 0);

        Assert.True(result.IsOverflow);
        Assert.Equal(ContentEnd, result.ContentEnd);
        Assert.InRange(result.BreakOffset, ContentEnd, Math.Min(ContentEnd + 1, text.Length));
        Assert.True(result.Width > 0);
    }

    [Theory]
    [InlineData("Mi", "Mid word", 4)]
    [InlineData("ب", "باب", 3)]
    public void AnItemEndsBeforeTheParagraphsNextBreak(string Item, string Paragraph, int NextBreak)
    {
        using var workspace = new MeasurementWorkspace();
        var breaker = new ShapingLineBreaker(new TextWidths(FontFallbackList.Shared.ClassifyAlone(Item, workspace), workspace.Shaping, workspace.LineBreaking.Widths), workspace.LineBreaking) {
            ParagraphText = Paragraph,
            CollapseSpaces = true
        };

        // Shared paragraph opportunities may be beyond this item's shape, as in Blink's ShapeLineRangeEndMidWord.
        var result = breaker.ShapeLine(.., 0);

        Assert.True(result.IsOverflow);
        Assert.Equal(Item.Length, result.BreakOffset);
        Assert.Equal(NextBreak, workspace.LineBreaking.NormalBreaks[0].End);
    }

    [Fact]
    public void AWholeFittingShapeDoesNotQueryBreakOpportunities()
    {
        const string text = "Mid word";
        using var workspace = new MeasurementWorkspace();
        var widths = new TextWidths(FontFallbackList.Shared.ClassifyAlone(text, workspace), workspace.Shaping, workspace.LineBreaking.Widths);
        var width = widths.Measure(text.Length);
        var breaker = new ShapingLineBreaker(widths, workspace.LineBreaking) { ParagraphText = text };

        var result = breaker.ShapeLine(.., HarfBuzz.FloatToFixed(width));

        Assert.False(result.IsOverflow);
        Assert.Equal(text.Length, result.BreakOffset);
        Assert.Equal(width, result.Width);
        Assert.Empty(workspace.LineBreaking.NormalBreaks);
    }

    [Theory]
    [InlineData("office affinity AVATAR")]
    [InlineData("سلامعليكمورحمةاللهوبركاته")]
    [InlineData("नमस्तेदुनियाक्षत्रिय")]
    public void GraphemeFallbackUsesTheSameOriginalShape(string Text)
    {
        using var workspace = new MeasurementWorkspace();
        var breaker = new ShapingLineBreaker(new TextWidths(FontFallbackList.Shared.ClassifyAlone(Text, workspace), workspace.Shaping, workspace.LineBreaking.Widths), workspace.LineBreaking);

        var normal = breaker.ShapeLine(.., 0);
        var anywhere = breaker.ShapeLine(.., 0, Anywhere: true);
        var repeated = breaker.ShapeLine(.., 0);

        Assert.True(normal.IsOverflow);
        Assert.InRange(anywhere.BreakOffset, 1, normal.BreakOffset);
        Assert.Equal(normal, repeated);
    }

    [Fact]
    public void AWholeFittingSuffixStillReshapesAnUnsafeWrappedStart()
    {
        const string text = "بابابا";
        const int start = 1;
        using var workspace = new MeasurementWorkspace();
        var classified = FontFallbackList.Shared.ClassifyAlone(text, workspace);
        var font = Assert.Single(classified.Runs.ToArray());

        // Blink reshapes a range of the paragraph, retaining HarfBuzz's context before that range.
        var exact = FontCache.GetShaper(font.TypeFace).MeasureFixed(text, start.., workspace.Shaping.Buffer, HarfBuzzSharp.Direction.RightToLeft, Script: font.Script);
        var expected = HarfBuzz.FixedToFloat(TextWidths.SnapFloat(exact));
        var original = new TextWidths(classified, workspace.Shaping, workspace.LineBreaking.Widths);
        var positions = new long[text.Length + 1];
        var marks = new ClusterMark[text.Length];
        original.FillPositions(positions, marks);
        Assert.NotEqual(ClusterMark.Safe, marks[start]);
        var widths = new TextWidths(classified, workspace.Shaping, workspace.LineBreaking.Widths, start, Origin: positions[start]) {
            ItemShape = positions,
            ItemMarks = marks
        };
        var breaker = new ShapingLineBreaker(widths, workspace.LineBreaking);

        var result = breaker.ShapeLine(start.., 1000 * HarfBuzz.FIXED_ONE, WrappedStart: true);

        Assert.False(result.IsOverflow);
        Assert.Equal(text.Length, result.BreakOffset);
        Assert.Equal(expected, result.Width);
    }
    [Fact]
    public void SoftHyphenMarksTheSelectedBreakAndStaysInvisibleWhenUnbroken()
    {
        const string text = "ab\u00ADcd";
        using var workspace = new MeasurementWorkspace();
        var widths = new TextWidths(FontFallbackList.Shared.ClassifyAlone(text, workspace),
            workspace.Shaping, workspace.LineBreaking.Widths);
        var breakWidth = widths.Measure(3);
        var fullWidth = widths.Measure(text.Length);
        var breaker = new ShapingLineBreaker(widths, workspace.LineBreaking);
        var broken = breaker.ShapeLine(.., HarfBuzz.FloatToFixed(breakWidth));
        var unbroken = breaker.ShapeLine(.., HarfBuzz.FloatToFixed(fullWidth));

        Assert.Equal(3, broken.BreakOffset);
        Assert.True(broken.IsHyphenated);
        Assert.False(unbroken.IsHyphenated);
        Assert.Equal(fullWidth, unbroken.Width);
    }

}
