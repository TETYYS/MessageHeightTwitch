using System;
using System.Linq;
using Xunit;
using Mht = MessageHeightTwitch.MessageHeightTwitch;

namespace Tests;

[Collection("Allocation measurements")]
public class MeasurementLimitTests
{
    [Fact]
    public void LimitsCountRunesAndAcceptTheBoundary()
    {
        var measurer = Fixture.CreateMeasurer(false);
        var parameters = Mht.CalculateMessageHeightParams.Default;
        var input = string.Concat(Enumerable.Repeat("𐒰", 500));
        var username = new string('a', 25);
        var displayName = string.Concat(Enumerable.Repeat("𠮷", 25));
        Assert.True(measurer.CalculateMessageHeightEx(input, username, displayName, parameters) > 0);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void OversizedArgumentsThrowBeforeCallbacks(int Argument)
    {
        var callbacks = 0;
        var measurer = new Mht(
            (ReadOnlySpan<char> _, out System.Drawing.SizeF size) => { callbacks++; size = default; return false; },
            (ReadOnlySpan<char> _, out System.Drawing.SizeF size) => { callbacks++; size = default; return false; },
            (ReadOnlySpan<char> _, out System.Drawing.SizeF size) => { callbacks++; size = default; return false; },
            (_, _) => default, _ => false, _ => false);
        var parameters = Mht.CalculateMessageHeightParams.Default;
        foreach (var rune in Argument == 1 ? new[] { "a" } : new[] { "a", "𐒰" }) {
            var args = new[] { "hello", "user", "USER" };
            args[Argument] = string.Concat(Enumerable.Repeat(rune, Argument == 0 ? 501 : 26));
            var error = Assert.Throws<ArgumentException>(() => measurer.CalculateMessageHeightEx(args[0], args[1], args[2], parameters));
            Assert.Equal(new[] { "Input", "Username", "DisplayName" }[Argument], error.ParamName);

        }
        Assert.Equal(0, callbacks);
    }

    [Fact]
    public void WarmWorkspaceNeedsNoGrowthForUnseenMaximumInputs()
    {
        var measurer = new Mht(
            (ReadOnlySpan<char> _, out System.Drawing.SizeF size) => { size = default; return false; },
            (ReadOnlySpan<char> name, out System.Drawing.SizeF size) => { size = new(28, 28); return name is "NaM" or "w!"; },
            (ReadOnlySpan<char> _, out System.Drawing.SizeF size) => { size = default; return false; },
            (_, _) => default, _ => false, _ => false);
        var parameters = Mht.CalculateMessageHeightParams.Default;

        /*
         * Warm engines with small inputs; large sizes and modifier structures
         * below have never been measured by this instance.
         */
        measurer.CalculateMessageHeightEx("warm 漢字 (مرحبا) NaM", "user", "USER", parameters);

        /*
         * Typefaces and emote names get their per-process caches on first use, so the warm-up
         * also covers the scripts and emotes that the sizes below use. It has no character break,
         * so the first one below still checks that positioned breaks need no new workspace state.
         */
        measurer.CalculateMessageHeightEx("𐒰 𐓘 w! NaM 漢", "user", "𠮷", parameters);
        var messages = new[] {
            new string('漢', 500), string.Concat(Enumerable.Repeat("𐒰", 500)),
            new string('(', 200) + new string(')', 200) + "漢字",
            "NaM " + string.Concat(Enumerable.Repeat("w! ", 160)),
            string.Concat(Enumerable.Repeat("w! NaM ", 70)),
            string.Concat(Enumerable.Repeat("a漢", 250))
        };
        var username = new string('a', 25);
        var displayName = string.Concat(Enumerable.Repeat("𠮷", 25));

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var start = GC.GetAllocatedBytesForCurrentThread();
        double total = 0;
        foreach (var message in messages)
            total += measurer.CalculateMessageHeightEx(message.AsSpan(), username, displayName, parameters);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - start;
        Assert.True(total > 0);
        Assert.Equal(0, allocated);
    }

    [Fact]
    public void MaximumImageParagraphNeedsNoGrowthAfterSmallInputs()
    {
        using var measurer = Fixture.CreateCapturedEmojiMeasurer(["☺", "☹"]);
        var parameters = Mht.CalculateMessageHeightParams.Default;
        parameters.EmojiReplaceMode = Mht.EMOTE_PROVIDER.FFZ;
        measurer.CalculateMessageHeightEx("☺☹", "a", "名", parameters);
        var message = new string('☺', 500);
        var username = new string('a', 25);
        var displayName = new string('名', 25);

        var start = GC.GetAllocatedBytesForCurrentThread();
        var height = measurer.CalculateMessageHeightEx(message, username, displayName, parameters);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - start;

        Assert.True(height > 0);
        Assert.Equal(0, allocated);
    }
}
