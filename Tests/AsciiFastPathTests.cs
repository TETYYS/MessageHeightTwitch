using System;
using System.Collections.Generic;
using System.Linq;
using MessageHeightTwitch;
using MessageHeightTwitch.Measurement.Shared.Shaping;

using Xunit;

namespace Tests;

public class AsciiFastPathTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LoginWidthsMatchShapingAndKeepContextualSubstitutions(bool Bold)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789_";
        var fonts = FontFallbackList.Shared;
        var table = Bold ? fonts.AsciiBold : fonts.AsciiRegular;
        using var shaper = new HarfBuzz(table.Typeface, Fonts.TWITCH_FONT_SIZE);
        using var buffer = new HarfBuzzSharp.Buffer();

        void check(string Text)
        {
            var accepted = table.TryMeasureLogin(Text, out var width);
            Assert.Equal(table.Accepts(Text), accepted);

            Span<long> prefixes = stackalloc long[Text.Length + 1];
            Assert.Equal(accepted, table.TryFillLoginPrefixes(Text, prefixes));

            if (accepted) {
                Assert.Equal(shaper.MeasureFixed(Text, .., buffer), width);

                var positions = buffer.GetGlyphPositionSpan();
                Assert.Equal(Text.Length, positions.Length);

                long prefix = 0;
                Assert.Equal(0, prefixes[0]);

                for (var end = 1; end <= Text.Length; end++) {
                    prefix += shaper.FixedAdvance(positions[end - 1].XAdvance);
                    Assert.Equal(prefix, prefixes[end]);
                }
            }
        }

        foreach (var a in alphabet) {
            check(a.ToString());
            foreach (var b in alphabet)
                check(string.Concat(a, b));
        }

        foreach (var text in new[] { "fi", "ffi", "office_affinity", "AVATAR_To_Yo", "x", "xray", "1x2", "99x99", "123_", "___" })
            check(text);

        // Inter has no alphabet-only ligatures, but calt changes x between digits to multiplication.
        Assert.False(table.TryMeasureLogin("1x2", out _));
        Assert.NotEqual(shaper.MeasureFixed("1x2", .., buffer), shaper.MeasureFixed("1x2", .., buffer, Features: [new HarfBuzzSharp.Feature(new HarfBuzzSharp.Tag('c', 'a', 'l', 't'), 0, 0, uint.MaxValue)]));
        Assert.True(table.TryMeasureLogin("office_affinity_ffi", out _));

        var random = new Random(98112);
        for (var i = 0; i < 20000; i++) {
            check(string.Create(random.Next(1, 26), random, (span, r) => {
                for (var c = 0; c < span.Length; c++)
                    span[c] = alphabet[r.Next(alphabet.Length)];
            }));
        }

    }

    /// <summary>
    /// TextWidths uses the fast path for a text only once it measured the whole text, and then measures prefixes with
    /// it. Every prefix of an accepted text must be accepted too, including texts with ligature trigger characters.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EveryPrefixOfAnAcceptedTextIsAccepted(bool Bold)
    {
        var fonts = FontFallbackList.Shared;
        var lookup = Assert.IsType<AsciiFastPath>(Bold ? fonts.AsciiBold : fonts.AsciiRegular);

        var random = new Random(611);
        var accepted = 0;
        for (var x = 0; x < 20000; x++) {
            var length = random.Next(1, 40);
            var text = string.Create(length, random, (span, r) => {
                for (var k = 0; k < span.Length; k++)
                    span[k] = r.Next(4) == 0 ? "-=<>!.:;?/(){}[]"[r.Next(16)] : (char)r.Next(' ', '~' + 1);
            });

            if (!lookup.TryMeasure(text, out _))
                continue;

            accepted++;

            for (var end = 0; end <= text.Length; end++)
                Assert.True(lookup.TryMeasurePrefixFixed(text, end, out _), $"prefix {end} of '{text}'");
        }

        Assert.True(accepted > 1000, $"only {accepted} random texts were eligible");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LookupMatchesShapingForEveryEligiblePairAndRandomRun(bool Bold)
    {
        var fonts = FontFallbackList.Shared;
        var lookup = Assert.IsType<AsciiFastPath>(Bold ? fonts.AsciiBold : fonts.AsciiRegular);
        using var shaper = new HarfBuzz(lookup.Typeface, Fonts.TWITCH_FONT_SIZE);
        using var buffer = new HarfBuzzSharp.Buffer();

        var eligible = new List<char>();
        for (var a = ' '; a <= '~'; a++) {
            if (lookup.TryMeasure(a.ToString(), out _))
                eligible.Add(a);
            for (var b = ' '; b <= '~'; b++) {
                var text = string.Concat(a, b);
                if (lookup.TryMeasure(text, out var width))
                    Assert.Equal(shaper.MeasureWidth(text, .., buffer), width);
            }
        }

        /*
         * Ensure that the optimization covers ordinary text, rather than only
         * passing comparisons by rejecting every potentially interesting input.
         */
        Assert.True(lookup.TryMeasure("hello office affinity AVATAR 1234567890", out _), $"Eligible characters: {new string([.. eligible])}");

        var random = new Random(4801);
        for (var sample = 0; sample < 20000; sample++) {
            var text = new string([.. Enumerable.Range(0, random.Next(1, 300)).Select(_ => eligible[random.Next(eligible.Count)])]);
            if (lookup.TryMeasure(text, out var width))
                Assert.Equal(shaper.MeasureWidth(text, .., buffer), width);
        }
    }
}
