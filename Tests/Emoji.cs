using MessageHeightTwitch.Emotes;
using MessageHeightTwitch;


using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;

using Xunit;
using MessageHeightTwitch.Measurement.Shared.Shaping;
using Tests.Utils;

namespace Tests;

public class Emoji
{
    private static readonly Lazy<Dictionary<string, TextMeasurementCase>> MeasurementCases = new(() =>
        MeasurementTestData.Read<TextMeasurementCase>("text-measurements.json").ToDictionary(test => test.Name));

    static Emoji()
    {
        System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(typeof(MessageHeightTwitch.MessageHeightTwitch).TypeHandle);
    }

    public static TheoryData<string> NativeWidthCases()
    {
        var data = new TheoryData<string>();
        foreach (var test in MeasurementCases.Value.Values.Where(test => test.Name.StartsWith("NativeWidth-", StringComparison.Ordinal)))
            data.Add(test.Name);
        return data;
    }

    [Theory]
    [MemberData(nameof(NativeWidthCases))]
    public void NativeWidthsMatchBrowser(string Name)
    {
        var test = MeasurementCases.Value[Name];
        var fonts = FontFallbackList.Shared;
        var context = new UScript.ScriptContext();
        context.Advance("TETYYS: ");
        using var buffer = new HarfBuzzSharp.Buffer();
        var runs = fonts.ClassifyText(test.Text, false, out _, ScriptContext: context);
        var width = BlinkLineModel.Width(test.Text, .., runs);

        var expected = Assert.Single(test.Prefixes!, prefix => prefix[0] == test.Text.Length)[1];
        Assert.Equal(expected, width);
    }

    [Theory]
    [InlineData("©", "Inter")]
    [InlineData("©\ufe0e", "Inter")]
    [InlineData("©\ufe0f", "Segoe UI Emoji")]
    [InlineData("1", "Inter")]
    [InlineData("1\u20e3", "Segoe UI Symbol")]
    [InlineData("1\ufe0e\u20e3", "Segoe UI Symbol")]
    [InlineData("1\ufe0f\u20e3", "Segoe UI Emoji")]
    [InlineData("☝\ufe0e", "Segoe UI Symbol")]
    [InlineData("☝\ufe0f", "Segoe UI Emoji")]
    public void NativePresentationRespectsCssAndVariationSelectors(string Text, string Family)
    {
        var fonts = FontFallbackList.Shared;
        using var buffer = new HarfBuzzSharp.Buffer();
        var runs = fonts.ClassifyText(Text, false, out _, UScriptCode.USCRIPT_LATIN);
        Assert.Equal(Family, Assert.Single(runs).TypeFace.FamilyName);
    }

    public static TheoryData<string> RepeatedNativeSymbolCases()
    {
        var data = new TheoryData<string>();
        foreach (var test in MeasurementCases.Value.Values.Where(test => test.Name.StartsWith("RepeatedNativeSymbol-", StringComparison.Ordinal)))
            data.Add(test.Name);
        return data;
    }

    [Theory]
    [MemberData(nameof(RepeatedNativeSymbolCases))]
    public void RepeatedNativeSymbolsWrapLikeProduction(string Name)
    {
        var test = MeasurementCases.Value[Name];
        var measurer = new MessageHeightTwitch.MessageHeightTwitch(
            (ReadOnlySpan<char> name, out SizeF size) => { size = default; return false; },
            (ReadOnlySpan<char> name, out SizeF size) => { size = default; return false; },
            (ReadOnlySpan<char> name, out SizeF size) => { size = default; return false; },
            (name, url) => default, name => false, name => false);
        var height = measurer.CalculateMessageHeight(test.Text, "tetyys", test.DisplayName, test.BadgeCount, null!);

        Assert.Equal(test.Height!.Value, height);
    }

    [Theory]
    [InlineData("☺\u200D🙃", false)]
    [InlineData("☺\u200D🙃", true)]
    [InlineData("↔\u200D🙃", false)]
    [InlineData("↔\u200D🙃", true)]
    [InlineData("🙃\u200D☺", false)]
    [InlineData("🙃\u200D☺", true)]
    public void EmojiZwjPresentationPrecedesUnicodeBlockFallback(string Text, bool Bold)
    {
        Assert.Single(StringInfo.ParseCombiningCharacters(Text));
        var emojiFont = Assert.Single(TextSetup.ClassifyText("🙃", false)).TypeFace;
        Assert.Equal("Segoe UI Emoji", emojiFont.FamilyName);
        using var shaper = new HarfBuzz(emojiFont, Fonts.TWITCH_FONT_SIZE);
        Assert.True(shaper.SupportedByFont(Text));

        /*
         * Chromium's grammar recognizes well-formed ZWJ sequences even
         * when the sequence has no single ligature or RGI/addon entry.
         */
        var (range, font, _) = Assert.Single(TextSetup.ClassifyText(Text, Bold));
        Assert.Equal(0..Text.Length, range);
        Assert.Same(emojiFont, font);
    }

    [Theory]
    [InlineData("🥹")]
    [InlineData("🫠")]
    [InlineData("🫡")]
    [InlineData("✌️")]
    public void SupportsEmojiFromCurrentBTTVCatalog(string Emoji)
    {
        var provider = new BTTVEmoteProvider();
        Assert.True(provider.IsEmojiSupported(Emoji));
    }

    [Theory]
    [InlineData("©")]
    [InlineData("🖕")]
    [InlineData("✌")]
    public void DoesNotReplaceEmojiExcludedByBTTV(string Emoji)
    {
        var provider = new BTTVEmoteProvider();
        Assert.False(provider.IsEmojiSupported(Emoji));
    }

    [Theory]
    [InlineData("👩🏽‍🚀")]
    [InlineData("👩‍⚕")]
    [InlineData("🏳‍🌈")]
    [InlineData("❤‍🔥")]
    [InlineData("🇱🇹")]
    [InlineData("1️⃣")]
    [InlineData("©")]
    [InlineData("\U0001FAEA")] // distorted face, Emoji 17.0
    [InlineData("\U0001FACD")] // orca, Emoji 17.0
    public void RecognizesCompleteEmojiWithinText(string Emoji)
    {
        var input = "before" + Emoji + "after";

        var candidates = new List<string>();
        var measurer = new MessageHeightTwitch.MessageHeightTwitch(
            (ReadOnlySpan<char> name, out SizeF size) => { size = default; return false; },
            (ReadOnlySpan<char> name, out SizeF size) => { size = default; return false; },
            (ReadOnlySpan<char> name, out SizeF size) => { size = default; return false; },
            (name, url) => default,
            name => false,
            name => { candidates.Add(name.ToString()); return name.SequenceEqual(Emoji); });

        measurer.CalculateMessageHeight(input, "tester", "TESTER", 0, []);

        Assert.Contains(Emoji, candidates);
    }
}
