using System;
using System.Linq;
using System.Drawing;
using System.Threading.Tasks;
using MessageHeightTwitch;
using Xunit;
using Mht = MessageHeightTwitch.MessageHeightTwitch;
using MessageHeightTwitch.Measurement.Shared;
using MessageHeightTwitch.Measurement.Shared.Shaping;
using Tests.Utils;

namespace Tests;

[Collection("Allocation measurements")]
public class MeasurementTests
{
    [Theory]
    [InlineData("abc אבג 123 مرحبا 漢字「（）」。e\u0301 👩‍💻 𐒰𐓘")]
    [InlineData("漢a漢b漢c「漢」「字」。あaあbあc")]
    [InlineData("مرحبا (abc אבג) 123 ภาษาไทย বাংলা हिन्दी")]
    public void WidthProbesMatchShapingInAnyOrder(string Text)
    {
        var fonts = FontFallbackList.Shared;
        fonts.Cache.InitializeShapers();
        using var workspace = new MeasurementWorkspace();
        using var buffer = new HarfBuzzSharp.Buffer();
        var classification = fonts.ClassifyText(Text, false, out _);
        var expected = Enumerable.Range(0, Text.Length + 1).Select(end => BlinkLineModel.Width(Text, ..end, classification)).ToArray();
        var random = new Random(317);
        for (var pass = 0; pass < 3; pass++) {
            var widths = new TextWidths(fonts.ClassifyAlone(Text, workspace), workspace.Shaping, workspace.LineBreaking.Widths);
            var ends = Enumerable.Range(0, Text.Length + 1).ToArray();

            if (pass == 0)
                Array.Reverse(ends);
            else
                random.Shuffle(ends);

            foreach (var end in ends)
                Assert.Equal(expected[end], widths.Measure(end));
        }
    }

    [Theory]
    [InlineData("office بابا ဗီူ6ူ66 ᜑ⃦⃕⃢ xyz")]
    [InlineData("a⃦⃕⃢ 🇱🇹 بابا AVATAR")]
    [InlineData("a(「あ」)abc あ「【Steamセール】」abc")]
    [InlineData("Sｰ誰 Dｰこれいる？ みたいなabc")]
    public void TextSliceWidthsMatchFreshShaping(string Text)
    {
        var runs = TextSetup.ClassifyText(Text, false);
        using var workspace = new MeasurementWorkspace();
        var classified = TextSetup.Classified(Text, runs, workspace);
        for (var start = 0; start <= Text.Length; start++) {
            var widths = new TextWidths(classified, workspace.Shaping, workspace.LineBreaking.Widths, start);
            foreach (var end in new[] { Math.Min(start + 2, Text.Length), Text.Length })
                Assert.Equal(BlinkLineModel.Width(Text, start..end, runs), widths.Measure(end));
        }
    }

    /// <summary>
    /// Chrome 154: a message ending in a ZWJ keeps the joining form it causes.
    /// UAX #9 L1 gives a trailing ZWJ the paragraph level and its own bidi run, but runs
    /// are shaped with the whole text as context, so the preceding letter still joins.
    /// </summary>
    [Theory]
    [InlineData("\u0646\u200D", 3.78125f)]
    [InlineData("\u0628\u0628\u200D", 8.578125f)]
    [InlineData("\u0644\u0627\u200D", 8.15625f)]
    public void TrailingJoinerKeepsJoiningForm(string Text, float Expected)
    {
        var fonts = FontFallbackList.Shared;
        var workspace = MeasurementWorkspace.Pool.Get();
        try {
            Assert.Equal(Expected, new TextWidths(fonts.ClassifyAlone(Text, workspace), workspace.Shaping, workspace.LineBreaking.Widths).Measure(Text.Length));
        } finally { MeasurementWorkspace.Pool.Return(workspace); }
    }

    [Fact]
    public void TextWidthsMatchAllMeasuredTextsAndPrefixes()
    {
        var fonts = FontFallbackList.Shared;
        fonts.Cache.InitializeShapers();
        using var workspace = new MeasurementWorkspace();
        using var buffer = new HarfBuzzSharp.Buffer();
        foreach (var test in MeasurementTestData.Read<TextMeasurementCase>("text-measurements.json").Where(test => !test.IsAction && (test.Prefixes != null || test.CompareAllPrefixes))) {
            var classification = fonts.ClassifyText(test.Text, false, out _);
            var widths = new TextWidths(fonts.ClassifyAlone(test.Text, workspace), workspace.Shaping, workspace.LineBreaking.Widths);
            for (var end = test.CompareAllPrefixes ? 0 : test.Text.Length; end <= test.Text.Length; end++) {
                var expected = BlinkLineModel.Width(test.Text, ..end, classification);
                var actual = widths.Measure(end);
                Assert.True(expected == actual, $"{test.Name}: expected {expected}, measured {actual} for prefix [0, {end}) of {test.Text}");
            }
        }
    }

    [Theory]
    [InlineData("😀NaM")]
    [InlineData("abc 🙂NaM")]
    [InlineData("NaM 😀NaM")]
    public void BttvEmoteEndingWordAfterFfzEmojiIsMeasured(string Text)
    {
        // A removed BTTV-after-FFZ spacing check read past the end of the word.
        var measurer = Fixture.CreateMeasurer(true);
        var parameters = Mht.CalculateMessageHeightParams.Default;
        parameters.TwitchEmotes = Fixture.TwitchEmotes;
        Assert.True(measurer.CalculateMessageHeightEx(Text, "user", "USER", parameters) > 0);
    }

    [Fact]
    public void PreviouslyUnseenAsciiMessagesAllocateNoManagedMemory()
    {
        var measurer = Fixture.CreateMeasurer(true);
        var parameters = Mht.CalculateMessageHeightParams.Default;
        parameters.TwitchEmotes = Fixture.TwitchEmotes;
        var messages = Enumerable.Range(0, 256).Select(i => $"new message {i:X8} hello office AVATAR NaM <--> xxx abcdefghijklmnopqrstuvwxyz").ToArray();
        for (var x = 0; x < 8; x++)
            measurer.CalculateMessageHeightEx("warm message 12345678 hello office AVATAR NaM <--> xxx abcdefghijklmnopqrstuvwxyz", "user", "USER", parameters);
        var start = GC.GetAllocatedBytesForCurrentThread();
        double checksum = 0;
        foreach (var message in messages)
            checksum += measurer.CalculateMessageHeightEx(message.AsSpan(), "user", "USER", parameters);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - start;
        Assert.True(checksum > 0);
        Assert.Equal(0, allocated);
    }

    [Fact]
    public void WorkspacesHandleReentrancyExceptionsAndConcurrentCalls()
    {
        Mht measurer = null!;
        var inside = false;
        var fail = false;
        var parameters = Mht.CalculateMessageHeightParams.Default;
        measurer = new(
            (ReadOnlySpan<char> name, out SizeF size) => { size = default; return false; },
            (ReadOnlySpan<char> name, out SizeF size) => {
                size = default;
                if (!inside) {
                    inside = true;
                    try { measurer.CalculateMessageHeightEx("nested مرحبا".AsSpan(), "inner", "INNER", parameters); }
                    finally { inside = false; }
                }
                if (fail)
                    throw new InvalidOperationException("callback failed");
                return false;
            },
            (ReadOnlySpan<char> name, out SizeF size) => { size = default; return false; },
            (_, _) => default, _ => false, _ => false);
        const string text = "outer 漢字 (مرحبا) hello";
        var expected = measurer.CalculateMessageHeightEx(text.AsSpan(), "user", "USER", parameters);
        fail = true;
        Assert.Throws<InvalidOperationException>(() => measurer.CalculateMessageHeightEx(text.AsSpan(), "user", "USER", parameters));
        fail = false;
        Assert.Equal(expected, measurer.CalculateMessageHeightEx(text.AsSpan(), "user", "USER", parameters));
        var parallel = Fixture.CreateMeasurer(false);
        expected = parallel.CalculateMessageHeightEx(text.AsSpan(), "user", "USER", parameters);
        Parallel.For(0, 100, _ => Assert.Equal(expected, parallel.CalculateMessageHeightEx(text.AsSpan(), "user", "USER", parameters)));
    }

    [Fact]
    public void PreviouslyUnseenUnicodeMessagesAllocateNoManagedMemory()
    {
        var measurer = Fixture.CreateMeasurer(true);
        var parameters = Mht.CalculateMessageHeightParams.Default;
        parameters.TwitchEmotes = Fixture.TwitchEmotes;
        var messages = Enumerable.Range(0, 256).Select(i => $"unique{i:X8} (مرحبا {i} 漢字。ひらがな ᎣᏏᏲ বাংলা हिन्दी ไทย) NaM 😀 𐒰𐓘 e\u0301 👩‍💻 {(char)(0x1401 + i)}").ToArray();

        /*
         * Warm allocator capacities and native engines without measuring any of
         * the inputs above. Their strings and analysis never enter a cache.
         */
        for (var length = 1; length <= 256; length *= 2) {
            var warm = new string('漢', length) + " (مرحبا) 😀";
            measurer.CalculateMessageHeightEx(warm, "warmuser", "表示名", parameters);
        }

        measurer.CalculateMessageHeightEx("warm (مرحبا) 漢字。ひらがな Ꭳ বাংলা हिन्दी ไทย NaM 😀 𐒰𐓘 e\u0301 👩‍💻 ᐀", "warmuser", "表示名", parameters);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        var start = GC.GetAllocatedBytesForCurrentThread();
        double checksum = 0;
        foreach (var message in messages)
            checksum += measurer.CalculateMessageHeightEx(message.AsSpan(), "uniqueuser", "名前", parameters);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - start;
        Assert.True(checksum > 0);
        Assert.Equal(0, allocated);
    }

    [Fact]
    public void PreviouslyUnseenModifierMessagesAllocateNoManagedMemory()
    {
        var measurer = new Mht(
            (ReadOnlySpan<char> name, out SizeF size) => { size = default; return false; },
            (ReadOnlySpan<char> name, out SizeF size) => { size = new(38, 40); return name is "NaM" or "w!" or "z!"; },
            (ReadOnlySpan<char> name, out SizeF size) => { size = new(28, 28); return name is "ffzW" or "ffzX"; },
            (_, _) => default, _ => false, _ => true);

        var parameters = Mht.CalculateMessageHeightParams.Default;
        var messages = Enumerable.Range(0, 128).Select(i => $"msg{i:X8} w! NaM ffzW 😀 ffzX 漢字{i:X8}").ToArray();
        measurer.CalculateMessageHeightEx("warmmessage w! NaM ffzW 😀 ffzX 漢字12345678", "username", "表示名", parameters);
        var start = GC.GetAllocatedBytesForCurrentThread();
        double checksum = 0;
        foreach (var message in messages)
            checksum += measurer.CalculateMessageHeightEx(message.AsSpan(), "username", "表示名", parameters);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - start;
        Assert.True(checksum > 0);
        Assert.Equal(0, allocated);
    }
}
