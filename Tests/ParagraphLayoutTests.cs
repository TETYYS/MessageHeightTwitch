using System;
using System.Collections.Generic;
using Xunit;
using Mht = MessageHeightTwitch.MessageHeightTwitch;

namespace Tests;

public class ParagraphLayoutTests
{
    /// <summary>Chrome 155.0.8059.40: the real Arabic name supplies bidi context to the neutral @.</summary>
    [Theory]
    [InlineData(12, 30, false, -1)]
    [InlineData(30, 52, true, -1)]
    [InlineData(55, 74, true, 32)]
    public void NamesAndTextShareTheParagraphDirection(int Length, float Height, bool FreshLine, int Break)
    {
        var name = new string([(char)0x645, (char)0x631, (char)0x62D, (char)0x628, (char)0x627]);
        var digits = string.Concat(System.Linq.Enumerable.Repeat("1234567890", 6));
        var text = "@" + digits[..Length];
        var starts = new List<int>();
        var expected = new List<int>();
        if (FreshLine)
            expected.Add(0);
        if (Break >= 0)
            expected.Add(Break);
        var parameters = Mht.CalculateMessageHeightParams.Default;
        parameters.LocalizedNameMode = Mht.LOCALIZED_NAME_MODE.DISPLAY_NAME_ONLY;
        var measured = Fixture.CreateTextMeasurer(null).CalculateMessageHeightEx(text, "user", name,
            parameters, starts);
        Assert.Equal(Height, measured);
        Assert.Equal(expected, starts);
    }

    /// <summary>Chrome 155.0.8059.40: collapsing a leading space keeps its following combining mark.</summary>
    [Theory]
    [InlineData(false, 52, new int[] { 30 })]
    [InlineData(true, 96, new int[] { 30, 32, 70 })]
    public void CollapsingALineLeadingSpaceKeepsFollowingMarks(bool MoreText, float Height, int[] LineStarts)
    {
        var text = new string('a', 28) + "t " + (char)0x301;
        if (MoreText)
            text += " " + new string('x', 70);
        var starts = new List<int>();
        var measured = Fixture.CreateTextMeasurer(null).CalculateMessageHeightEx(text, "tetyys", "TETYYS",
            Mht.CalculateMessageHeightParams.Default, starts);
        Assert.Equal(Height, measured);
        Assert.Equal(LineStarts, starts);
    }

}
