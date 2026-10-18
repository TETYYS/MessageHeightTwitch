using System;
using System.Collections.Generic;
using System.Drawing;
using MessageHeightTwitch;
using MessageHeightTwitch.Measurement.Shared;
using MessageHeightTwitch.Measurement.Shared.Shaping;

using Xunit;
using Mht = MessageHeightTwitch.MessageHeightTwitch;

namespace Tests;

public class PreparedParagraphTests
{
    [Theory]
    [InlineData("TETYYS", Mht.LOCALIZED_NAME_MODE.USERNAME_ONLY, "tetyys", true, false)]
    [InlineData("TETYYS", Mht.LOCALIZED_NAME_MODE.DISPLAY_NAME_ONLY, "TETYYS", false, false)]
    [InlineData("TETYYS", Mht.LOCALIZED_NAME_MODE.USERNAME_DISPLAY_NAME, "TETYYS", false, false)]
    [InlineData("表示名", Mht.LOCALIZED_NAME_MODE.USERNAME_ONLY, "tetyys", true, false)]
    [InlineData("表示名", Mht.LOCALIZED_NAME_MODE.DISPLAY_NAME_ONLY, "表示名", false, false)]
    [InlineData("表示名", Mht.LOCALIZED_NAME_MODE.USERNAME_DISPLAY_NAME, "表示名", false, true)]
    public void NameModesPreserveTheTextStyleAndSourceIdentity(string DisplayName, Mht.LOCALIZED_NAME_MODE Mode, string AuthorName, bool AuthorIsLogin, bool HasLoginSuffix)
    {
        using var workspace = new MeasurementWorkspace();
        using var measurer = Fixture.CreateTextMeasurer(null);
        var parameters = Mht.CalculateMessageHeightParams.Default;
        parameters.LocalizedNameMode = Mode;

        measurer.ClassifyMessageText("hi", "tetyys", DisplayName, parameters, workspace, out _, out _);

        var paragraph = workspace.Paragraph;
        var suffix = HasLoginSuffix ? " (tetyys)" : "";
        Assert.Equal(AuthorName + suffix + ": hi", paragraph.Text.ToString());
        var names = new List<ParagraphItem>();
        foreach (var item in paragraph.Items)
            if (item.Region is PARAGRAPH_REGION.LOGIN or PARAGRAPH_REGION.DISPLAY_NAME)
                names.Add(item);
        Assert.Equal(HasLoginSuffix ? 2 : 1, names.Count);
        Assert.Equal(AuthorIsLogin ? PARAGRAPH_REGION.LOGIN : PARAGRAPH_REGION.DISPLAY_NAME, names[0].Region);
        Assert.Equal(AuthorName, paragraph.Text[names[0].Start..names[0].End].ToString());
        Assert.True(names[0].Bold);
        Assert.True(names[0].BreakAll);
        if (HasLoginSuffix) {
            Assert.Equal(PARAGRAPH_REGION.LOGIN, names[1].Region);
            Assert.Equal(suffix, paragraph.Text[names[1].Start..names[1].End].ToString());
            Assert.False(names[1].Bold);
            Assert.True(names[1].BreakAll);
        }
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(7, false)]
    public void NameBreakAllDoesNotChangeTheFollowingBodysWholeWordPolicy(int Badges, bool BodyStartsFresh)
    {
        var measurer = Fixture.CreateTextMeasurer(null);
        var parameters = Mht.CalculateMessageHeightParams.Default;
        parameters.LocalizedNameMode = Mht.LOCALIZED_NAME_MODE.DISPLAY_NAME_ONLY;
        parameters.NumberOfBadges = Badges;
        var starts = new List<int>();

        var name = new string('a', 25);
        var height = measurer.CalculateMessageHeightEx("mmmmmmmmmmmm", name, name, parameters, starts);

        // With badges, the name breaks across the first two lines and the body fits after its remaining letters.
        // Without badges, the name fits on the first line and the body's whole word moves to the second one.
        Assert.Equal(Badges > 0 ? 52.1875f : 52, height);
        Assert.Equal(BodyStartsFresh ? [0] : Array.Empty<int>(), starts);
    }

    [Theory]
    [InlineData("NaM")]
    [InlineData("GiantWideEmote")]
    public void CanonicalImageRetainsTheRewrittenMessagesTraceOffsets(string Name)
    {
        using var measurer = new Mht(
            (ReadOnlySpan<char> _, out SizeF size) => { size = default; return false; },
            (ReadOnlySpan<char> name, out SizeF size) => { size = new(400, 20); return name is "NaM" or "GiantWideEmote"; },
            (ReadOnlySpan<char> _, out SizeF size) => { size = default; return false; },
            (_, _) => default, _ => false, _ => false);
        var text = " " + Name + " x";
        var starts = new List<int>();

        var height = measurer.CalculateMessageHeightEx(text, "a", "a", Mht.CalculateMessageHeightParams.Default, starts);

        // The image occupies its own overflowing line, followed by x on the third line. Its one-character
        // paragraph representation must not change offsets into the rewritten message.
        Assert.Equal(74, height);
        Assert.Equal(new[] { 1, Name.Length + 2 }, starts);
    }

    [Theory]
    [InlineData("\u0301x", 1)]
    [InlineData("\u0301\u0302x", 2)]
    public void AClusterCrossingAnInlineItemBoundaryKeepsOneOwnerAndConservesItsAdvance(string Body, int Continuation)
    {
        using var workspace = new MeasurementWorkspace();
        var measurer = Fixture.CreateTextMeasurer(null);
        measurer.ClassifyMessageText(Body, "tetyys", "TETYYS", Mht.CalculateMessageHeightParams.Default, workspace, out var lead, out _);
        var separator = Assert.Single(workspace.Paragraph.Items, item => item.Region == PARAGRAPH_REGION.SEPARATOR);
        var body = Assert.Single(workspace.Paragraph.Items, item => item.Region == PARAGRAPH_REGION.BODY);
        var separatorLine = Assert.Single(workspace.Paragraph.LineItems, item => item.Start == separator.Start && item.End == separator.End);
        var bodyLine = Assert.Single(workspace.Paragraph.LineItems, item => item.Start == body.Start && item.End == body.End);
        var group = workspace.Paragraph.Groups[separator.Group];

        Assert.Equal(Continuation, lead);
        Assert.Equal(separator.Group, body.Group);
        Assert.Equal(ClusterMark.None, workspace.Paragraph.Marks[body.Start]);
        Assert.Equal(0, workspace.Paragraph.Positions[bodyLine.PositionStart + Continuation] - workspace.Paragraph.Positions[bodyLine.PositionStart]);
        var separatorWidth = workspace.Paragraph.Positions[separatorLine.PositionStart + separatorLine.End - separatorLine.Start];
        var clusterEnd = workspace.Paragraph.GroupPositions[group.PositionStart + body.Start + Continuation - group.Start];
        Assert.Equal(clusterEnd - workspace.Paragraph.GroupPositions[group.PositionStart], separatorWidth);

        long copiedAdvance = 0;
        foreach (var item in workspace.Paragraph.LineItems)
            if (item.Group == separator.Group)
                copiedAdvance += workspace.Paragraph.Positions[item.PositionStart + item.End - item.Start] - workspace.Paragraph.Positions[item.PositionStart];
        var groupAdvance = workspace.Paragraph.GroupPositions[group.PositionStart + group.End - group.Start] - workspace.Paragraph.GroupPositions[group.PositionStart];
        Assert.Equal(groupAdvance, copiedAdvance);
    }

    [Fact]
    public void ProjectedItemsPreserveNegativeGlyphAdvances()
    {
        using var workspace = new MeasurementWorkspace();
        var measurer = Fixture.CreateTextMeasurer(null);
        measurer.ClassifyMessageText("\u2019.", "tetyys", "TETYYS", Mht.CalculateMessageHeightParams.Default,
            workspace, out _, out _);
        var body = Assert.Single(workspace.Paragraph.Items, item => item.Region == PARAGRAPH_REGION.BODY);
        var item = Assert.Single(workspace.Paragraph.LineItems, item => item.Start == body.Start && item.End == body.End);
        var group = workspace.Paragraph.Groups[item.Group];
        var prefix = workspace.Paragraph.GroupPositions[group.PositionStart + body.Start + 1 - group.Start] -
            workspace.Paragraph.GroupPositions[group.PositionStart + body.Start - group.Start];

        Assert.True(prefix < 0);
        Assert.Equal(prefix, workspace.Paragraph.Positions[item.PositionStart + 1] - workspace.Paragraph.Positions[item.PositionStart]);
    }
}
