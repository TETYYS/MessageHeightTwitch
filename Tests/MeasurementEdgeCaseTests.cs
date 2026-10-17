using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using MessageHeightTwitch;
using MessageHeightTwitch.Measurement.EmoteRewriting;
using MessageHeightTwitch.Measurement.LineFitting;
using MessageHeightTwitch.Measurement.Shared;
using MessageHeightTwitch.Measurement.Shared.Shaping;

using Xunit;
using Mht = MessageHeightTwitch.MessageHeightTwitch;

namespace Tests;

public class MeasurementEdgeCaseTests : IClassFixture<Fixture>
{
    private const string Grinning = "\U0001F600";
    private readonly Fixture Fixture;

    public MeasurementEdgeCaseTests(Fixture Fixture) => this.Fixture = Fixture;

    private static Mht.CalculateMessageHeightParams Parameters(Mht.EMOTE_PROVIDER Emoji = Mht.EMOTE_PROVIDER.FFZ, bool LineMargin = true,
        Dictionary<string, string>? Twitch = null)
    {
        var parameters = Mht.CalculateMessageHeightParams.Default;
        parameters.EmojiReplaceMode = Emoji;
        parameters.LineMargin = LineMargin;
        parameters.TwitchEmotes = Twitch;
        return parameters;
    }

    /// <summary>BetterTTV emotes by name (NaM, and the hidden modifier r!), with the grinning face supported by the given providers.</summary>
    private static Mht Measurer(bool FfzEmoji, bool BttvEmoji) => new(
        (ReadOnlySpan<char> name, out SizeF size) => { size = default; return false; },
        (ReadOnlySpan<char> name, out SizeF size) => {
            size = name.SequenceEqual("NaM") ? new(38, 40) : new(20, 20);
            return name.SequenceEqual("NaM") || name.SequenceEqual("r!");
        },
        (ReadOnlySpan<char> name, out SizeF size) => { size = default; return false; },
        (_, _) => new(28, 28), emoji => BttvEmoji && emoji.SequenceEqual(Grinning), emoji => FfzEmoji && emoji.SequenceEqual(Grinning));

    [Fact]
    public void TwitchEmotesNeedAComparerWithSpanLookup()
    {
        var parameters = Parameters(Twitch: new Dictionary<string, string>(new PlainComparer()) { ["Kappa"] = "x" });
        Assert.Throws<ArgumentException>(() => Fixture.TextOnlyMht.CalculateMessageHeightEx("Kappa", "a", "a", parameters));
    }

    private sealed class PlainComparer : IEqualityComparer<string>
    {
        public bool Equals(string? X, string? Y) => string.Equals(X, Y, StringComparison.Ordinal);
        public int GetHashCode(string Text) => Text.GetHashCode();
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(3)]
    public void UnknownNameModesAreRejected(int Mode)
    {
        var parameters = Parameters();
        parameters.LocalizedNameMode = (Mht.LOCALIZED_NAME_MODE)Mode;
        Assert.Throws<ArgumentException>(() => Fixture.TextOnlyMht.CalculateMessageHeightEx("hi", "a", "a", parameters));
    }

    /// <summary>A localized display name takes the paragraph off the ASCII path and shows the login in parentheses.</summary>
    [Fact]
    public void LocalizedNameWithNonAsciiDisplayNameStillFitsOneLine()
    {
        Assert.Equal(30, Fixture.TextOnlyMht.CalculateMessageHeightEx("hi", "name", "名前", Parameters()));
    }

    /// <summary>Twelve emoji fit beside a short name at FFZ's 21px, not at BetterTTV's 23.66px.</summary>
    [Fact]
    public void BetterTtvEmojiImagesAreWiderThanFfzOnes()
    {
        var message = string.Concat(Enumerable.Repeat(Grinning, 12));
        var ffz = Fixture.Mht.CalculateMessageHeightEx(message, "a", "a", Parameters(Mht.EMOTE_PROVIDER.FFZ));
        var bttv = Fixture.Mht.CalculateMessageHeightEx(message, "a", "a", Parameters(Mht.EMOTE_PROVIDER.BTTV));
        Assert.True(bttv > ffz, $"BetterTTV {bttv}, FFZ {ffz}");
    }

    /// <summary>
    /// A hidden modifier changes nothing about the layout, whether the emoji is FFZ's or BetterTTV's, joined to the
    /// modifier or to text that cannot break before it.
    /// </summary>
    [Theory]
    [InlineData(true, "r! NaM (😀", "NaM (😀")]
    [InlineData(true, "r!😀", "😀")]
    [InlineData(false, "r! NaM 😀", "NaM 😀")]
    [InlineData(false, "r!😀 r! NaM", "😀 NaM")]
    public void HiddenModifiersDoNotChangeTheLayout(bool FfzEmoji, string Modified, string Plain)
    {
        var measurer = Measurer(FfzEmoji, BttvEmoji: true);
        Assert.Equal(measurer.CalculateMessageHeightEx(Plain, "a", "a", Parameters()),
            measurer.CalculateMessageHeightEx(Modified, "a", "a", Parameters()));
    }

    /// <summary>
    /// Chat renders emotes only in the message: a login or display name that names an emote stays text, as tall as a
    /// measurer without emotes makes it, while the same word in the message is the emote's 40px image.
    /// </summary>
    [Theory]
    [InlineData("nam", "NaM", Mht.LOCALIZED_NAME_MODE.USERNAME_DISPLAY_NAME, "NaM")]
    [InlineData("NaM", "表示名", Mht.LOCALIZED_NAME_MODE.USERNAME_DISPLAY_NAME, "NaM")]
    [InlineData("NaM", "NaM", Mht.LOCALIZED_NAME_MODE.USERNAME_ONLY, "NaM")]
    [InlineData("nam", "NaM", Mht.LOCALIZED_NAME_MODE.DISPLAY_NAME_ONLY, "NaM")]
    [InlineData("kappa", "Kappa", Mht.LOCALIZED_NAME_MODE.USERNAME_DISPLAY_NAME, "Kappa")]
    public void NamesNeverShowEmotes(string Login, string DisplayName, Mht.LOCALIZED_NAME_MODE Mode, string Emote)
    {
        var parameters = Parameters(Twitch: new() { ["Kappa"] = "x" });
        parameters.LocalizedNameMode = Mode;
        var withoutEmotes = new Mht(
            (ReadOnlySpan<char> _, out SizeF size) => { size = default; return false; },
            (ReadOnlySpan<char> _, out SizeF size) => { size = default; return false; },
            (ReadOnlySpan<char> _, out SizeF size) => { size = default; return false; },
            (_, _) => default, _ => false, _ => false);

        var withEmotes = new Mht(
            (ReadOnlySpan<char> _, out SizeF size) => { size = default; return false; },
            (ReadOnlySpan<char> name, out SizeF size) => { size = new(38, 40); return name.SequenceEqual("NaM"); },
            (ReadOnlySpan<char> _, out SizeF size) => { size = default; return false; },
            (_, _) => new(40, 40), _ => false, _ => false);

        Assert.Equal(withoutEmotes.CalculateMessageHeightEx("hi", Login, DisplayName, parameters), withEmotes.CalculateMessageHeightEx("hi", Login, DisplayName, parameters));
        Assert.NotEqual(withoutEmotes.CalculateMessageHeightEx("hi " + Emote, Login, DisplayName, parameters), withEmotes.CalculateMessageHeightEx("hi " + Emote, Login, DisplayName, parameters));
    }

    /// <summary>Each line of plain text is 14px without line margins and 22px with them, so both count the same lines.</summary>
    [Fact]
    public void LineMarginsChangeOnlyTheHeightOfEachLine()
    {
        var random = new Random(29);
        for (var sample = 0; sample < 300; sample++) {
            var words = Enumerable.Range(0, random.Next(1, 40)).Select(_ => new string('x', random.Next(1, 30)).Replace('x', (char)random.Next('a', 'z' + 1)));
            var message = string.Join(" ", words);
            if (message.Length > 500)
                message = message[..500];
            var withMargins = Fixture.TextOnlyMht.CalculateMessageHeightEx(message, "tetyys", "tetyys", Parameters(LineMargin: true));
            var without = Fixture.TextOnlyMht.CalculateMessageHeightEx(message, "tetyys", "tetyys", Parameters(LineMargin: false));
            Assert.Equal((withMargins - 8) / 22, (without - 8) / 14);
        }
    }

    [Fact]
    public void ANameWiderThanTheChatBreaks()
    {
        // A localized display name followed by its username, each at the length limit.
        Assert.True(Fixture.TextOnlyMht.CalculateMessageHeightEx("hi", new string('W', 25), new string('漢', 25), Parameters()) >= 52);
    }

    /// <summary>
    /// A right-to-left word too long for a line, after an emote in the same right-to-left message, breaks with the
    /// positions of its item, which starts after the emote and is measured from its end.
    /// </summary>
    [Fact]
    public void LongRightToLeftWordAfterAnEmoteBreaks()
    {
        const string emote = "שלום";
        var measurer = Measurer(false, false);
        var message = emote + " " + string.Concat(Enumerable.Repeat("אבגדהוזחטי", 8));
        var height = measurer.CalculateMessageHeightEx(message, "a", "a", Parameters(Twitch: new() { [emote] = "x" }));
        Assert.True(height > 52, $"{height}");
    }

    [Fact]
    public void KnownModifiersHaveTheirProvidersFlags()
    {
        var bttv = new Dictionary<string, EmoteModifier> {
            ["w!"] = new(EmoteModifierFlags.Hidden | EmoteModifierFlags.GrowX, true),
            ["z!"] = new(EmoteModifierFlags.Hidden | EmoteModifierFlags.NoSpace, true),
            ["h!"] = new(EmoteModifierFlags.Hidden | EmoteModifierFlags.FlipX, true),
            ["v!"] = new(EmoteModifierFlags.Hidden | EmoteModifierFlags.FlipY, true),
            ["c!"] = new(EmoteModifierFlags.Hidden | EmoteModifierFlags.Cursed, true),
            ["l!"] = new(EmoteModifierFlags.Hidden | EmoteModifierFlags.Rotate90, true),
            ["r!"] = new(EmoteModifierFlags.Hidden, true),
        };
        foreach (var overlay in new[] { "p!", "s!", "cvMask", "cvHazmat", "SoSnowy", "IceCold", "TopHat", "SantaHat", "ReinDeer", "CandyCane" })
            bttv[overlay] = new();
        var ffz = new Dictionary<string, EmoteModifier> {
            ["ffzW"] = new(EmoteModifierFlags.Hidden | EmoteModifierFlags.GrowX),
            ["ffzX"] = new(EmoteModifierFlags.Hidden | EmoteModifierFlags.FlipX),
            ["ffzY"] = new(EmoteModifierFlags.Hidden | EmoteModifierFlags.FlipY),
            ["ffzCursed"] = new(EmoteModifierFlags.Hidden | EmoteModifierFlags.Cursed),
            ["ffzHyper"] = new(EmoteModifierFlags.Hidden | EmoteModifierFlags.HyperRed | EmoteModifierFlags.Shake),
            ["ffzRainbow"] = new(EmoteModifierFlags.Hidden | EmoteModifierFlags.Rainbow),
            ["ffzBounce"] = new(EmoteModifierFlags.Hidden | EmoteModifierFlags.Bounce),
            ["ffzJam"] = new(EmoteModifierFlags.Hidden | EmoteModifierFlags.Jam),
            ["ffzSlide"] = new(EmoteModifierFlags.Hidden | EmoteModifierFlags.Slide),
            ["ffzLeave"] = new(EmoteModifierFlags.Hidden | EmoteModifierFlags.Leave),
            ["ffzArrive"] = new(EmoteModifierFlags.Hidden | EmoteModifierFlags.Appear),
            ["ffzSpin"] = new(EmoteModifierFlags.Hidden | EmoteModifierFlags.Rotate),
        };
        // Every name of either provider, and near misses of each: other first, middle or last characters, and lengths.
        var names = bttv.Keys.Concat(ffz.Keys).SelectMany(name => new[] {
            name, "#" + name[1..], name[..^1] + "#", name.Length > 2 ? name[..(name.Length / 2)] + "#" + name[(name.Length / 2 + 1)..] : name,
            name + "#", name[..^1] }).Append("").Distinct();
        foreach (var name in names) {
            Assert.Equal(bttv.TryGetValue(name, out var b) ? b : null, EmoteModifier.Known(name, Mht.EMOTE_PROVIDER.BTTV));
            Assert.Equal(ffz.TryGetValue(name, out var f) ? f : null, EmoteModifier.Known(name, Mht.EMOTE_PROVIDER.FFZ));
            Assert.Null(EmoteModifier.Known(name, Mht.EMOTE_PROVIDER.SEVENTV));
        }
    }

    private delegate void ClassifiedUse(ClassifiedText Text, MeasurementWorkspace Workspace);

    /// <summary>Uses a message's text classified after the name and separator, as in a paragraph.</summary>
    private static void Classified(string Text, ClassifiedUse Use)
    {
        var fonts = FontFallbackList.Shared;
        var workspace = MeasurementWorkspace.Pool.Get();
        try {
            Use(fonts.ClassifyAfter("tetyys: ", Text, workspace), workspace);
        } finally {
            MeasurementWorkspace.Pool.Return(workspace);
        }
    }

    /// <summary>An inline emoji after the text counts toward the break that has to include it.</summary>
    [Fact]
    public void BreaksFindTheSegmentAnInlineObjectPushesOverTheWidth()
    {
        Classified("hello world", (text, workspace) => {
            var widths = new TextWidths(text, workspace.Shaping, workspace.LineBreaking.Widths);
            var full = widths.Measure(11);
            Assert.Null(widths.FindBreak(workspace.LineBreaking, false, full + 21, 21));
            Assert.NotNull(widths.FindBreak(workspace.LineBreaking, false, full + 20, 21));
        });
    }

    /// <summary>
    /// When the word fits but the object after it does not, a line can still end at a break opportunity right before
    /// the object: here after the hyphen.
    /// </summary>
    [Fact]
    public void LineEndsBeforeAnObjectAtABreakOpportunity()
    {
        Classified("ab-cd", (text, workspace) => {
            var widths = new TextWidths(text, workspace.Shaping, workspace.LineBreaking.Widths, 0, Origin: 0);
            var available = TextWidths.SnapFixed(widths.ExactPrefix(3));
            var breaker = new ShapingLineBreaker(widths, workspace.LineBreaking) { ObjectFollows = true };
            var result = breaker.ShapeLine(..3, available);
            Assert.Equal(3, result.BreakOffset);
            Assert.False(result.IsOverflow);
        });
    }

    /// <summary>
    /// Line breaking always makes progress, for every width from none to the whole text, at break opportunities or
    /// anywhere, at the paragraph's start or on a wrapped line, across ligatures, kerning, joining and marks.
    /// </summary>
    [Theory]
    [InlineData("office affinity AVAVAV Wolf ->> ffi")]
    [InlineData("سلامعليكمورحمةاللهوبركاته")]
    [InlineData("नमस्तेदुनियाक्षत्रिय")]
    [InlineData("WAVE—TAVAT—ToYó̂a")]
    [InlineData("abc-def-ghi/jkl(mno)pqr")]
    public void LineBreakingAlwaysMakesProgress(string Text)
    {
        Classified(Text, (classified, workspace) => {
            var widths = new TextWidths(classified, workspace.Shaping, workspace.LineBreaking.Widths, 0, Origin: 0);
            var full = TextWidths.SnapFixed(Math.Abs(widths.ExactPrefix(Text.Length))) + HarfBuzz.FIXED_ONE;
            var breaker = new ShapingLineBreaker(widths, workspace.LineBreaking);
            foreach (var start in new[] { 0, Text.Length / 3, Text.Length / 2 }) {
                for (var available = 0L; available <= full; available += HarfBuzz.FIXED_ONE / 4) {
                    foreach (var anywhere in new[] { false, true }) {
                        foreach (var wrapped in new[] { false, true }) {
                            var result = breaker.ShapeLine(start.., available, anywhere, wrapped);
                            Assert.InRange(result.BreakOffset, start + 1, Text.Length);
                            Assert.True(result.Width >= 0);
                        }
                    }
                }
            }
        });
    }
}
