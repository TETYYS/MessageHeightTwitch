using System;
using System.Collections.Generic;
using MessageHeightTwitch;
using MessageHeightTwitch.Measurement.InlineCollection;
using MessageHeightTwitch.Measurement.ParagraphShaping;
using MessageHeightTwitch.Measurement.Shared;
using MessageHeightTwitch.Measurement.Shared.Shaping;

using Xunit;
using Mht = MessageHeightTwitch.MessageHeightTwitch;

namespace Tests;

public class LoginMeasurementTests
{
    [Theory]
    [InlineData("hello")]
    [InlineData("مرحبا")]
    [InlineData("漢字")]
    [InlineData("😀")]
    public void KnownLoginKeepsItsFastShapeWithUnicodeBody(string Message)
    {
        using var workspace = new MeasurementWorkspace();
        using var measurer = Fixture.CreateTextMeasurer(null);
        var parameters = Mht.CalculateMessageHeightParams.Default;

        var message = workspace.Collection.Modifiers.Rewrite(Message, parameters, measurer.Emotes);
        var builder = new ChatParagraphBuilder(message, "TETYYS", PARAGRAPH_REGION.DISPLAY_NAME, "", workspace.Collection, workspace.Paragraph);
        builder.Build();
        var preparation = new ParagraphShaper(FontFallbackList.Shared, workspace.Paragraph, workspace.ParagraphShaping, workspace.Shaping, 0..6);
        preparation.Prepare();

        var group = Assert.Single(workspace.Paragraph.Groups, group => group.Start == 0);
        Assert.False(group.Marked);
        Assert.Equal("TETYYS", workspace.Paragraph.Text[group.Start..group.End].ToString());
        using var shaper = new HarfBuzz(FontFallbackList.Shared.AsciiBold.Typeface, Fonts.TWITCH_FONT_SIZE);
        using var buffer = new HarfBuzzSharp.Buffer();

        Assert.Equal(shaper.MeasureFixed("TETYYS", .., buffer), workspace.Paragraph.GroupPositions[group.PositionStart + group.End - group.Start]);
    }

    [Theory]
    [InlineData("tetyys")]
    [InlineData("TETYYS_123")]
    [InlineData("AVATAR_To_Yo")]
    [InlineData("office_affinity_ffi")]
    [InlineData("___")]
    [InlineData("123_456")]
    [InlineData("xray")]
    [InlineData("1x2")]
    [InlineData("WWWWWWWWWWWWWWWWWWWWWWWWW")]
    public void NameModesWithIdenticalInputsKeepHeightsAndLineStarts(string Name)
    {
        var measurer = new Fixture().Mht;
        string[] messages = [
            "", "hello world", " hello", "hello ", " hello ",
            "office affinity AVATAR To Yo", "1x2 -> <=>", "@tetyys hello @someone",
            new('a', 200),
            "NaM", "NaM ", "hello NaM Kappa", "\ud83d\ude00", "\u65e5\u672c\u8a9e hello",
            "\u0645\u0631\u062d\u0628\u0627 @123_456 hello", "\u0301hello", "\u200d\ud83d\ude00 test",
            "\u202eabc\u202c", "\u2067hello\u2069 123"
        ];
        var expectedStarts = new List<int>();
        var actualStarts = new List<int>();
        foreach (var message in messages) {
            foreach (var badges in new[] { 0, 1, 3, 12, 16 }) {
                for (var x = 0; x < 8; x++) {
                    var parameters = Mht.CalculateMessageHeightParams.Default;
                    parameters.NumberOfBadges = badges;
                    parameters.TwitchEmotes = Fixture.TwitchEmotes;
                    parameters.LineMargin = (x & 1) != 0;
                    parameters.EmoteMargin = (x & 2) != 0;
                    parameters.WholeMargin = (x & 4) != 0;
                    parameters.LocalizedNameMode = Mht.LOCALIZED_NAME_MODE.DISPLAY_NAME_ONLY;
                    // Each name mode should preserve the same visible author text, weight and line starts.
                    var expected = measurer.CalculateMessageHeightEx(message, Name, Name, parameters, expectedStarts);
                    foreach (var mode in Enum.GetValues<Mht.LOCALIZED_NAME_MODE>()) {
                        parameters.LocalizedNameMode = mode;
                        var actual = measurer.CalculateMessageHeightEx(message, Name, Name, parameters, actualStarts);
                        Assert.True(expected == actual, $"'{Name}', '{message}', {mode}, badges={badges}, margins={x}: {expected} != {actual}");
                        Assert.Equal(expectedStarts, actualStarts);
                    }
                }
            }
        }
    }
}
