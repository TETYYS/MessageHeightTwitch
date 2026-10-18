using System;
using MessageHeightTwitch;
using MessageHeightTwitch.Measurement.LineFitting;
using MessageHeightTwitch.Measurement.Shared;
using MessageHeightTwitch.Measurement.Shared.Shaping;

using Xunit;

namespace Tests;

public class ParagraphBreakIteratorTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NormalAndBreakAllItemsShareTheParagraphWithoutSharingItsPolicy(bool BreakAllFirst)
    {
        const string paragraph = "namebody";
        const string item = "body";
        using var workspace = new MeasurementWorkspace();

        bool canBreak(bool breakAll)
        {
            var breaker = new ShapingLineBreaker(new TextWidths(FontFallbackList.Shared.ClassifyAlone(item, workspace), workspace.Shaping, workspace.LineBreaking.Widths), workspace.LineBreaking) {
                ParagraphText = paragraph,
                ParagraphOffset = 4,
                BreakAll = breakAll
            };
            return breaker.CanBreakAfter(1);
        }

        Assert.Equal(BreakAllFirst, canBreak(BreakAllFirst));
        Assert.Equal(!BreakAllFirst, canBreak(!BreakAllFirst));
        Assert.False(canBreak(false));
        Assert.True(canBreak(true));
        Assert.Single(workspace.LineBreaking.NormalBreaks);
        Assert.Contains(workspace.LineBreaking.BreakAllBreaks, opportunity => opportunity.End == 5);
    }

    [Fact]
    public void BreakAllKeepsTheBaseCharacterBeforeAnItemLeadingCombiningMark()
    {
        const string paragraph = "a\u0301bc";
        using var workspace = new MeasurementWorkspace();
        var breaker = new ShapingLineBreaker(new TextWidths(FontFallbackList.Shared.ClassifyAlone(paragraph.AsSpan(1), workspace), workspace.Shaping, workspace.LineBreaking.Widths), workspace.LineBreaking) {
            ParagraphText = paragraph,
            ParagraphOffset = 1,
            BreakAll = true
        };

        // The preceding alphabetic character supplies the mark's line-break class across the item boundary.
        Assert.True(breaker.CanBreakAfter(1));
        Assert.Contains(workspace.LineBreaking.BreakAllBreaks, opportunity => opportunity.End == 2);
    }

    [Fact]
    public void GraphemeFallbackKeepsRegionalIndicatorPairsAcrossAnItemBoundary()
    {
        const string paragraph = "\U0001F1FA\U0001F1F8\U0001F1E8\U0001F1E6X";
        const int offset = 2;
        using var workspace = new MeasurementWorkspace();
        var breaker = new ShapingLineBreaker(new TextWidths(FontFallbackList.Shared.ClassifyAlone(paragraph.AsSpan(offset), workspace), workspace.Shaping, workspace.LineBreaking.Widths), workspace.LineBreaking) {
            ParagraphText = paragraph,
            ParagraphOffset = offset
        };

        var result = breaker.ShapeLine(.., 0, Anywhere: true);

        // US and Canada each remain one grapheme. Re-segmenting the suffix would pair US's second indicator with
        // Canada's first one and choose a different break.
        Assert.Equal(2, result.BreakOffset);
        Assert.Equal(new[] { (0, 4), (4, 8), (8, 9) }, workspace.LineBreaking.ParagraphGraphemes);
    }

    [Fact]
    public void AnItemEndingInsideAGraphemeKeepsTheParagraphsLaterOpportunity()
    {
        const string paragraph = "\U0001F1FA\U0001F1F8X";
        using var workspace = new MeasurementWorkspace();
        var breaker = new ShapingLineBreaker(new TextWidths(FontFallbackList.Shared.ClassifyAlone(paragraph.AsSpan(0, 2), workspace), workspace.Shaping, workspace.LineBreaking.Widths), workspace.LineBreaking) {
            ParagraphText = paragraph
        };

        var result = breaker.ShapeLine(.., 0, Anywhere: true);

        // ShapeLine clamps the result to the item's shape, while its iterator retains the real paragraph boundary.
        Assert.Equal(2, result.BreakOffset);
        Assert.Equal(4, workspace.LineBreaking.ItemGraphemes[0].End);
        Assert.Equal(4, workspace.LineBreaking.ParagraphGraphemes[0].End);
    }
    [Fact]
    public void ResumingALineInvalidatesItsBreakContextAndParagraphResetRestoresIt()
    {
        const string paragraph = "a-1";
        using var workspace = new MeasurementWorkspace();

        bool canBreak()
        {
            var breaker = new ShapingLineBreaker(new TextWidths(FontFallbackList.Shared.ClassifyAlone("-1", workspace),
                workspace.Shaping, workspace.LineBreaking.Widths), workspace.LineBreaking) {
                ParagraphText = paragraph,
                ParagraphOffset = 1
            };
            return breaker.CanBreakAfter(1);
        }

        Assert.True(canBreak()); // The hyphen follows an alphanumeric character.
        workspace.LineBreaking.SetStartOffset(1);
        Assert.Empty(workspace.LineBreaking.NormalBreaks);
        Assert.False(canBreak()); // That preceding character is outside the resumed iterator.
        workspace.LineBreaking.BeginParagraph();
        Assert.True(canBreak());
    }

    [Fact]
    public void ResumingALineRestartsTheGraphemeIteratorAsWell()
    {
        const string paragraph = "\U0001F1FA\U0001F1F8\U0001F1E8\U0001F1E6X";
        using var workspace = new MeasurementWorkspace();
        workspace.LineBreaking.SetStartOffset(2);
        var breaker = new ShapingLineBreaker(new TextWidths(FontFallbackList.Shared.ClassifyAlone(paragraph.AsSpan(2), workspace),
            workspace.Shaping, workspace.LineBreaking.Widths), workspace.LineBreaking) {
            ParagraphText = paragraph,
            ParagraphOffset = 2
        };

        var result = breaker.ShapeLine(.., 0, Anywhere: true);

        // The first two indicators in the resumed string now form a pair, with paragraph offsets retained.
        Assert.Equal(4, result.BreakOffset);
        Assert.Equal(new[] { (2, 6), (6, 8), (8, 9) }, workspace.LineBreaking.ParagraphGraphemes);
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(1, false)]
    public void BreakAllDoesNotInheritAClassFromBeforeTheResumedLine(int StartOffset, bool Expected)
    {
        const string paragraph = "a\u0301b";
        using var workspace = new MeasurementWorkspace();
        workspace.LineBreaking.SetStartOffset(StartOffset);
        var breaker = new ShapingLineBreaker(new TextWidths(FontFallbackList.Shared.ClassifyAlone(paragraph.AsSpan(1), workspace),
            workspace.Shaping, workspace.LineBreaking.Widths), workspace.LineBreaking) {
            ParagraphText = paragraph,
            ParagraphOffset = 1,
            BreakAll = true
        };

        Assert.Equal(Expected, breaker.CanBreakAfter(1));
    }

}
