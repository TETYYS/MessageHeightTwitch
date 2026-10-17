using System;
using System.Collections.Generic;
using System.Drawing;
using MessageHeightTwitch;
using Xunit;
using Mht = MessageHeightTwitch.MessageHeightTwitch;

namespace Tests;

public class SingleEmoteFastPathTests
{
    [Theory]
    [InlineData("NaM")]
    [InlineData("Kappa")]
    [InlineData("w! NaM ffzW")]
    [InlineData("NaM ")]
    [InlineData("NaM NaM")]
    [InlineData("😀")]
    public void TrailingWhitespaceDoesNotChangeHeightsOrLineStarts(string Message)
    {
        var size = new SizeF(38, 40);
        var measurer = new Mht(
            (ReadOnlySpan<char> name, out SizeF result) => { result = default; return false; },
            (ReadOnlySpan<char> name, out SizeF result) => { result = size; return name is "NaM" or "w!"; },
            (ReadOnlySpan<char> name, out SizeF result) => { result = new(28, 28); return name is "ffzW"; },
            (_, _) => size, _ => true, _ => true);
        (string Login, string Display)[] names = [
            ("tetyys", "TETYYS"), ("displayname", "DisplayName"),
            (new('W', 18), new('W', 18)), (new('W', 25), new('W', 25)),
            ("a", "名前"), ("tetyys", "表示名"), ("user_1x2", "user_1x2")
        ];
        var general = new List<int>();
        var optimized = new List<int>();

        // A trailing CSS space preserves the rendered content and uses normal dispatch.
        var padded = Message + " ";
        foreach (var (login, display) in names) {
            foreach (var mode in Enum.GetValues<Mht.LOCALIZED_NAME_MODE>()) {
                foreach (var badges in new[] { 0, 1, 3, 12 }) {
                    foreach (var isAction in new[] { false, true }) {
                        for (var margins = 0; margins < 8; margins++) {
                            foreach (var box in new[] { new SizeF(25, 28), new SizeF(38, 40), new SizeF(128, 128), new SizeF(400, 20) }) {
                                size = box;
                                var parameters = Mht.CalculateMessageHeightParams.Default;
                                parameters.TwitchEmotes = Fixture.TwitchEmotes;
                                parameters.LocalizedNameMode = mode;
                                parameters.NumberOfBadges = badges;
                                parameters.IsAction = isAction;
                                parameters.EmoteMargin = (margins & 1) != 0;
                                parameters.LineMargin = (margins & 2) != 0;
                                parameters.WholeMargin = (margins & 4) != 0;
                                var expected = measurer.CalculateMessageHeightEx(padded, login, display, parameters, general);
                                var actual = measurer.CalculateMessageHeightEx(Message, login, display, parameters, optimized);
                                Assert.True(expected == actual,
                                    $"'{Message}', '{login}', '{display}', {mode}, badges={badges}, margins={margins}, {box}: {expected} != {actual}");
                                Assert.Equal(general, optimized);
                            }
                        }
                    }
                }
            }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EmoteWrappingMatchesAtTheLayoutUnitBoundary(bool IsAction)
    {
        var size = new SizeF(1, 40);
        var measurer = new Mht(
            (ReadOnlySpan<char> _, out SizeF result) => { result = default; return false; },
            (ReadOnlySpan<char> name, out SizeF result) => { result = size; return name is "NaM"; },
            (ReadOnlySpan<char> _, out SizeF result) => { result = default; return false; },
            (_, _) => default, _ => false, _ => false);
        var parameters = Mht.CalculateMessageHeightParams.Default;
        parameters.IsAction = IsAction;
        var general = new List<int>();
        var optimized = new List<int>();
        // Sweep the remaining width in 1/128px steps, including exact equality with WIDTH_TO_FIT.
        for (var units = 0; units <= 293 * 128; units++) {
            size.Width = units / 128f;
            var expected = measurer.CalculateMessageHeightEx("NaM ", "tetyys", "tetyys", parameters, general);
            Assert.Equal(expected, measurer.CalculateMessageHeightEx("NaM", "tetyys", "tetyys", parameters, optimized));
            Assert.Equal(general, optimized);
        }
    }
}
