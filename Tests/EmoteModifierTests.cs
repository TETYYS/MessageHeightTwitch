using System;
using System.Drawing;
using MessageHeightTwitch;
using MessageHeightTwitch.Measurement.EmoteRewriting;

using Xunit;
using Mht = MessageHeightTwitch.MessageHeightTwitch;

namespace Tests;

public class EmoteModifierTests
{
    /// <summary>Emoji left as text cannot divide a word into an addon emote; replacement tokens can.</summary>
    [Theory]
    [InlineData(Mht.EMOTE_PROVIDER.NONE, false, 0)]
    [InlineData(Mht.EMOTE_PROVIDER.NONE, true, 0)]
    [InlineData(Mht.EMOTE_PROVIDER.FFZ, false, 0)]
    [InlineData(Mht.EMOTE_PROVIDER.FFZ, true, 2)]
    [InlineData(Mht.EMOTE_PROVIDER.BTTV, false, 0)]
    [InlineData(Mht.EMOTE_PROVIDER.BTTV, true, 2)]
    public void EmojiTokenBoundariesFollowTheReplacementMode(Mht.EMOTE_PROVIDER Mode, bool Supported, int Objects)
    {
        var measurer = new Mht(
            (ReadOnlySpan<char> name, out SizeF size) => { size = default; return false; },
            (ReadOnlySpan<char> name, out SizeF size) => { size = new(38, 40); return name.SequenceEqual("NaM"); },
            (ReadOnlySpan<char> _, out SizeF size) => { size = default; return false; },
            (_, _) => default, _ => Supported, _ => Supported);
        var parameters = Mht.CalculateMessageHeightParams.Default;
        parameters.EmojiReplaceMode = Mode;
        var emoji = char.ConvertFromUtf32(0x1F600);
        using var rewriter = new ModifierRewriter();
        foreach (var text in new[] { emoji + "NaM", "NaM" + emoji }) {
            var prepared = rewriter.Rewrite(text, parameters, measurer.Emotes);
            Assert.Equal(text, prepared.Text.ToString());
            Assert.Equal(Objects, prepared.Emotes.Count);
        }
    }

}
