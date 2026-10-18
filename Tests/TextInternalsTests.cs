using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using MessageHeightTwitch;
using MessageHeightTwitch.Measurement.LineFitting;
using MessageHeightTwitch.Measurement.Shared;
using MessageHeightTwitch.Measurement.Shared.Shaping;

using SkiaSharp;
using Xunit;
using CharType = MessageHeightTwitch.Measurement.Shared.Shaping.HanKerning.CHAR_TYPE;

namespace Tests;

public class TextInternalsTests
{
    private static readonly FontFallbackList Bundled = FontFallbackList.Shared;

    private static T WithWorkspace<T>(Func<MeasurementWorkspace, T> Use)
    {
        var workspace = MeasurementWorkspace.Pool.Get();
        try { return Use(workspace); } finally { MeasurementWorkspace.Pool.Return(workspace); }
    }

    /// <summary>
    /// LayoutUnit::FromFloatCeil rounds toward positive infinity, so a negative width rounds up too, not toward zero.
    /// </summary>
    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1024)]
    [InlineData(1024, 1024)]
    [InlineData(1025, 2048)]
    [InlineData(-1, 0)]
    [InlineData(-1023, 0)]
    [InlineData(-1024, -1024)]
    [InlineData(-1025, -1024)]
    [InlineData(-2048, -2048)]
    [InlineData(-2049, -2048)]
    public void SnapFixedRoundsUpToLayoutUnit(long Units, long Expected)
    {
        Assert.Equal(Expected, TextWidths.SnapFixed(Units));
    }

    [Fact]
    public void FastPathMeasuresOnlyPrintableAscii()
    {
        var ascii = Bundled.AsciiRegular;

        Assert.True(ascii.TryMeasure("", out var empty));
        Assert.Equal(0, empty);
        Assert.False(ascii.TryMeasure("a\tb", out _));
        Assert.False(ascii.TryMeasure("café", out _));
        Assert.False(ascii.TryMeasure("\u007F", out _));
        // A prefix also checks the character after it, whose kern it includes.
        Assert.False(ascii.TryMeasurePrefixFixed("aé", 1, out _));
        Assert.False(ascii.TryMeasurePrefixFixed("a\t", 1, out _));
    }

    /// <summary>
    /// Inter's arrow is a contextual substitution of "-&gt;": the rule's trigger alone, or with other text, stays fast.
    /// </summary>
    [Fact]
    public void FastPathRejectsTextsWhereAContextualRuleCanApply()
    {
        var ascii = Bundled.AsciiRegular;

        Assert.False(ascii.TryMeasure("->", out _));
        Assert.False(ascii.TryMeasurePrefixFixed("->", 1, out _));
        Assert.True(ascii.TryMeasure("-a", out _));
        Assert.True(ascii.TryMeasurePrefixFixed("-a", 1, out _));
    }

    /// <summary>
    /// The pair table leaves a pair unknown when the second glyph's advance changes; every such pair must be caught
    /// by the rules, since the fast path no longer checks the table. An accepted pair composes exactly.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EveryAcceptedPairPrefixComposesWithTheWholePair(bool Bold)
    {
        var ascii = Bold ? Bundled.AsciiBold : Bundled.AsciiRegular;
        for (var a = ' '; a <= '~'; a++) {
            for (var b = ' '; b <= '~'; b++) {
                var pair = string.Concat(a, b);
                if (!ascii.TryMeasurePrefixFixed(pair, 1, out var prefix)) continue;
                Assert.True(ascii.TryMeasurePrefixFixed(pair, 2, out var whole), pair);
                Assert.True(ascii.TryMeasurePrefixFixed(b.ToString(), 1, out var last), pair);
                Assert.Equal(whole - last, prefix);
            }
        }
    }

    [Fact]
    public void FastPathWidthsRejectAPrefixTheFastPathCannotMeasure()
    {
        Assert.Throws<System.Diagnostics.UnreachableException>(() => new TextWidths("é", Bundled.AsciiRegular).Measure(1));
    }

    [Fact]
    public void LineBreakPairsFollowBlinksAsciiRules()
    {
        WithWorkspace(workspace => {
            FastBreak Fast(char LastLast, char Last, char Ch) => AsciiLineBreakPairs.Fast(LastLast, Last, Ch, workspace.LineBreaking);
            // Controls below '!' never break in the fast table.
            Assert.Equal(FastBreak.NoBreak, Fast('\0', '\r', 'a'));
            Assert.Equal(FastBreak.NoBreak, Fast('\0', 'a', '\u0001'));
            Assert.Equal(FastBreak.Break, Fast('\0', '!', '('));
            Assert.Equal(FastBreak.NoBreak, Fast('\0', 'a', '('));
            Assert.Equal(FastBreak.Break, Fast('\0', '-', 'a'));
            Assert.Equal(FastBreak.NoBreak, Fast('\0', '-', '$'));
            Assert.Equal(FastBreak.Break, Fast('\0', '?', 'a'));
            Assert.Equal(FastBreak.NoBreak, Fast('\0', '?', '"'));
            Assert.Equal(FastBreak.NoBreak, Fast('\0', '?', '\''));
            Assert.Equal(FastBreak.NoBreak, Fast('\0', 'a', '!'));
            Assert.Equal(FastBreak.NoBreak, Fast('\0', '(', '('));
            // '-' before a digit breaks only after a letter or digit, and before non-ASCII ICU decides.
            Assert.Equal(FastBreak.Break, Fast('a', '-', '5'));
            Assert.Equal(FastBreak.NoBreak, Fast(' ', '-', '5'));
            Assert.Equal(FastBreak.Icu, Fast('\0', '-', 'é'));
            Assert.Equal(FastBreak.Icu, Fast('\0', 'a', '一'));
            return 0;
        });
    }

    [Fact]
    public void IcuBindingNamesLibrariesAndFindsExports()
    {
        Assert.Equal("icuuc78.dll", ICUExtensions.LibraryName("78", true, false));
        Assert.Equal("libicuuc.78.dylib", ICUExtensions.LibraryName("78", false, true));
        Assert.Equal("libicuuc.so.78", ICUExtensions.LibraryName("78", false, false));
        // A library exporting the plain name, unlike the bundled ICU's versioned names.
        var harfBuzz = System.Runtime.InteropServices.NativeLibrary.Load("libHarfBuzzSharp", typeof(HarfBuzzSharp.Buffer).Assembly, null);
        Assert.NotEqual(0, ICUExtensions.Bind(harfBuzz, "hb_shape", "78"));
        Assert.NotEqual(0, ICUExtensions.Bind("ubrk_open"));
    }

    [Fact]
    public void IcuErrorsThrowAndWarningsDoNot()
    {
        ICUExtensions.Check(0);
        ICUExtensions.Check(-128);
        Assert.Throws<InvalidOperationException>(() => ICUExtensions.Check(1));
        // U+3001 has six script extensions, which overflow a one-element buffer.
        Assert.Throws<InvalidOperationException>(() => ICUExtensions.Get(0x3001, new UScriptCode[1]));
    }

    private static List<(Range Range, UScriptCode Script)> Segment(string Text)
    {
        var result = new List<(Range Range, UScriptCode Script)>();
        new UScript.ScriptContext().Segment(Text, result);
        return result;
    }

    /// <summary>
    /// U+0375 is Greek with the extensions [Coptic, Greek]: its own script comes first.
    /// U+16EB is Common with the single extension Runic, which a Common run then prefers.
    /// </summary>
    [Theory]
    [InlineData("͵", UScriptCode.USCRIPT_GREEK)]
    [InlineData("᛫", UScriptCode.USCRIPT_RUNIC)]
    [InlineData("᛫a", UScriptCode.USCRIPT_LATIN)]
    public void ScriptRunsResolveScriptExtensions(string Text, UScriptCode Script)
    {
        Assert.Equal(Script, Segment(Text)[^1].Script);
    }

    [Theory]
    [InlineData("Café déjà vu", UScriptCode.USCRIPT_LATIN)]
    [InlineData("Привет всем!", UScriptCode.USCRIPT_CYRILLIC)]
    [InlineData("あア、ー", UScriptCode.USCRIPT_HIRAGANA)]
    public void SingleScriptTextKeepsItsDecodedRunes(string Text, UScriptCode Script)
    {
        using var scratch = new UScript.ScriptScratch();
        var context = new UScript.ScriptContext();
        var result = new List<(Range Range, UScriptCode Script)>();
        var runes = context.Segment(Text, result, scratch).ToArray();
        Assert.Equal((0..Text.Length, Script), Assert.Single(result));
        Assert.Equal(Text.EnumerateRunes().ToArray(), runes);
        Assert.Equal(Script, context.Script);
    }

    [Fact]
    public void SingleScriptTextResolvesPendingBracketsAcrossChunks()
    {
        using var scratch = new UScript.ScriptScratch();
        var context = new UScript.ScriptContext();
        var result = new List<(Range Range, UScriptCode Script)>();
        context.Segment("⟨", result, scratch);
        context.Segment("café", result, scratch);
        context.Segment("αβ", result, scratch);
        context.Segment("⟩", result, scratch);
        Assert.Equal((0..1, UScriptCode.USCRIPT_LATIN), Assert.Single(result));
    }

    [Theory]
    [InlineData("é6\u0342", 1, UScriptCode.USCRIPT_GREEK)]
    [InlineData("é\U0001F600α", 3, UScriptCode.USCRIPT_GREEK)]
    public void SingleScriptShortcutPreservesLookaheadAndUtf16Offsets(string Text, int LastStart, UScriptCode LastScript)
    {
        using var scratch = new UScript.ScriptScratch();
        var context = new UScript.ScriptContext();
        var result = new List<(Range Range, UScriptCode Script)>();
        var runes = context.Segment(Text, result, scratch).ToArray();
        Assert.Equal((LastStart..Text.Length, LastScript), result[^1]);
        Assert.Equal(Text.EnumerateRunes().ToArray(), runes);
    }

    [Fact]
    public void SingleScriptShortcutPreservesMalformedSurrogateOffsets()
    {
        // Construct at runtime: attribute strings cannot preserve an unpaired surrogate in metadata.
        var text = new string(['é', (char)0xD800, 'α']);
        using var scratch = new UScript.ScriptScratch();
        var context = new UScript.ScriptContext();
        var result = new List<(Range Range, UScriptCode Script)>();
        var runes = context.Segment(text, result, scratch).ToArray();
        Assert.Equal((2..3, UScriptCode.USCRIPT_GREEK), result[^1]);
        Assert.Equal([new Rune('é'), Rune.ReplacementChar, new Rune('α')], runes);
    }

    /// <summary>
    /// Script runs put a character's own script first among its extensions, assuming it is never later than second.
    /// </summary>
    [Fact]
    public void OwnScriptIsAmongTheFirstTwoScriptExtensions()
    {
        Span<UScriptCode> extensions = stackalloc UScriptCode[256];
        for (var codepoint = 0; codepoint <= 0x10FFFF; codepoint++) {
            if (codepoint is >= 0xD800 and <= 0xDFFF) continue;
            var script = (UScriptCode)Icu.Character.GetIntPropertyValue(codepoint, Icu.Character.UProperty.SCRIPT);
            if (script <= UScriptCode.USCRIPT_INHERITED) continue;
            var count = ICUExtensions.Get(codepoint, extensions);
            Assert.True(extensions[0] == script || count > 1 && extensions[1] == script, $"U+{codepoint:X4}");
        }
    }

    /// <summary>
    /// The per-character caches return what ICU does, both when they look a character up and once it is cached.
    /// </summary>
    [Fact]
    public void CachedScriptSetsMatchIcu()
    {
        UScript.ScriptSetValue expected = default, actual = default;
        for (var codepoint = 0; codepoint <= 0xFFFF; codepoint++) {
            if (codepoint is >= 0xD800 and <= 0xDFFF) continue;
            var rune = new Rune(codepoint);
            UScript.ComputeScriptSet(rune, ref expected);
            for (var pass = 0; pass < 2; pass++) {
                UScript.FillScriptSet(rune, ref actual);
                Assert.True(expected.Count == actual.Count, $"U+{codepoint:X4}");
                for (var x = 0; x < expected.Count; x++)
                    Assert.True(expected.Values[x] == actual.Values[x], $"U+{codepoint:X4}");
            }
        }
    }

    [Fact]
    public void ScriptScratchGrowsForLongTexts()
    {
        using var scratch = new UScript.ScriptScratch();
        scratch.Ensure(5000);
        Assert.True(scratch.Runes.Length >= 5000);
        Assert.True(scratch.Sets.Length >= 5000);
        var result = new List<(Range Range, UScriptCode Script)>();
        new UScript.ScriptContext().Segment(new string('a', 3000), result, scratch);
        Assert.Equal((0..3000, UScriptCode.USCRIPT_LATIN), Assert.Single(result));
    }

    /// <summary>Font fallback takes Common and Inherited characters' script from their block.</summary>
    [Theory]
    [InlineData(0x0E3F, UScriptCode.USCRIPT_THAI)]
    [InlineData(0x037E, UScriptCode.USCRIPT_GREEK)]
    [InlineData(0x10FB, UScriptCode.USCRIPT_GEORGIAN)]
    [InlineData(0x0964, UScriptCode.USCRIPT_DEVANAGARI)]
    [InlineData(0x060C, UScriptCode.USCRIPT_ARABIC)]
    [InlineData(0x3001, UScriptCode.USCRIPT_HAN)]
    [InlineData(0x30FC, UScriptCode.USCRIPT_KATAKANA_OR_HIRAGANA)]
    [InlineData(0x309B, UScriptCode.USCRIPT_KATAKANA_OR_HIRAGANA)]
    [InlineData(0xFF01, UScriptCode.USCRIPT_HAN)]
    [InlineData('!', UScriptCode.USCRIPT_COMMON)]
    [InlineData('a', UScriptCode.USCRIPT_LATIN)]
    public void FallbackScriptUsesTheBlockOfCommonCharacters(int Codepoint, UScriptCode Script)
    {
        Assert.Equal(Script, UScript.GetScript(new Rune(Codepoint)));
    }

    private static List<(Range Range, EmojiPresentation.PRIORITY Priority)> Presentations(params int[] Codepoints)
    {
        var result = new List<(Range Range, EmojiPresentation.PRIORITY Priority)>();
        EmojiPresentation.FillRuns(Codepoints.Select(codepoint => new Rune(codepoint)).ToArray(), result);
        return result;
    }

    [Fact]
    public void TagSequencesOfLettersAndDigitsFormOneEmoji()
    {
        // A subdivision flag: black flag, tag letters and a digit, cancel tag.
        Assert.Equal([(0..10, EmojiPresentation.PRIORITY.EMOJI), (10..11, EmojiPresentation.PRIORITY.TEXT)],
            Presentations(0x1F3F4, 0xE0067, 0xE0062, 0xE0031, 0xE007F, 'a'));
        // A tag space is not a tag character, so the flag stands alone.
        Assert.Equal([(0..2, EmojiPresentation.PRIORITY.EMOJI), (2..6, EmojiPresentation.PRIORITY.TEXT)],
            Presentations(0x1F3F4, 0xE0020, 0xE007F));
    }

    [Fact]
    public void WorkspaceBuffersGrowOnlyWhenTooSmall()
    {
        var buffer = System.Buffers.ArrayPool<int>.Shared.Rent(4);
        PooledBuffers.Ensure(ref buffer, 5000);
        Assert.True(buffer.Length >= 5000);
        var grown = buffer;
        PooledBuffers.Ensure(ref buffer, 10);
        Assert.Same(grown, buffer);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference Abandon(bool Bidi) => new(Bidi ? new BidiParagraph() : new TextBreakIterators());

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AbandonedIteratorsAreReleasedByTheirFinalizer(bool Bidi)
    {
        var iterators = Abandon(Bidi);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        Assert.False(iterators.IsAlive);
    }

    [Fact]
    public void AnEmptyTextHasNoFontRuns()
    {
        WithWorkspace(workspace => {
            Assert.Empty(Bundled.ClassifyAlone("", workspace).Runs.ToArray());
            return 0;
        });
    }

    /// <summary>
    /// Blink's Han kerning classifies a font's punctuation by its ink. Yu Gothic, the only bundled font with halt, has
    /// its dots' ink in the left half, a narrow centered colon and semicolon, and proportional quotes.
    /// </summary>
    [Theory]
    [InlineData("YuGothic-Regular.ttf")]
    [InlineData("YuGothic-Bold.ttf")]
    public void HanKerningTypesMatchYuGothicsGlyphs(string File)
    {
        var path = System.IO.Path.Combine(AppContext.BaseDirectory, "Fonts", File);
        using var shaper = new HarfBuzz(Typeface.FromFile(path), Fonts.TWITCH_FONT_SIZE);
        using var buffer = new HarfBuzzSharp.Buffer();

        const string sample = "、。，．：；“‘”’";
        shaper.Shape(sample, buffer, UScriptCode.USCRIPT_HAN);

        var infos = buffer.GetGlyphInfoSpan();
        var positions = buffer.GetGlyphPositionSpan();
        Assert.Equal(sample.Length, infos.Length);

        var glyphs = new ushort[infos.Length];
        for (var x = 0; x < infos.Length; x++)
            glyphs[x] = (ushort)infos[x].Codepoint;

        var bounds = new SKRect[glyphs.Length];
        using var face = SKTypeface.FromFile(path);
        using var font = new SKFont(face, Fonts.TWITCH_FONT_SIZE);
        font.GetGlyphWidths(glyphs, new float[glyphs.Length], bounds, null);

        for (var x = 0; x < glyphs.Length; x++) {
            var half = HarfBuzz.FixedToFloat(positions[x].XAdvance) / 2;
            var ink = bounds[x];
            ink.Offset(HarfBuzz.FixedToFloat(positions[x].XOffset), 0);

            if (x < 4)
                Assert.True(ink.Right <= half, $"dot {x}");
            else if (x < 6)
                Assert.True(ink.Width <= half && ink.Left >= half / 2 && ink.Right > half, $"colon {x}");
            else if (x < 8)
                Assert.True(ink.Left < half, $"open quote {x}");
        }

        var kerning = shaper.Kerning;
        Assert.All("、。，．", dot => Assert.Equal(CharType.CLOSE, kerning.GetCharType(dot)));
        Assert.Equal(CharType.MIDDLE, kerning.GetCharType(0xFF1A));
        Assert.Equal(CharType.MIDDLE, kerning.GetCharType(0xFF1B));
    }

    [Theory]
    [InlineData(0x2018, (byte)CharType.OPEN_NARROW)]
    [InlineData(0x201C, (byte)CharType.OPEN_NARROW)]
    [InlineData(0x2019, (byte)CharType.CLOSE_NARROW)]
    [InlineData(0x201D, (byte)CharType.CLOSE_NARROW)]
    [InlineData(0x3000, (byte)CharType.MIDDLE)]
    [InlineData(0x00B7, (byte)CharType.MIDDLE)]
    [InlineData(0x2027, (byte)CharType.MIDDLE)]
    [InlineData(0x30FB, (byte)CharType.MIDDLE)]
    [InlineData('(', (byte)CharType.OPEN_NARROW)]
    [InlineData(0xFF08, (byte)CharType.OPEN)]
    [InlineData(0x300C, (byte)CharType.OPEN)]
    [InlineData(')', (byte)CharType.CLOSE_NARROW)]
    [InlineData(0xFF09, (byte)CharType.CLOSE)]
    [InlineData(0x300D, (byte)CharType.CLOSE)]
    [InlineData('a', (byte)CharType.OTHER)]
    public void HanKerningClassifiesPunctuationByCategoryAndWidth(int Character, byte Type)
    {
        var face = Typeface.FromFile(System.IO.Path.Combine(AppContext.BaseDirectory, "Fonts", "YuGothic-Regular.ttf"));
        using var shaper = new HarfBuzz(face, Fonts.TWITCH_FONT_SIZE);
        Assert.Equal((CharType)Type, shaper.Kerning.GetCharType(Character));
    }

    /// <summary>A unicode-range subset finds no glyph outside its range, also for a variation sequence.</summary>
    [Fact]
    public void RangeLimitedFontsHideGlyphsOutsideTheirRange()
    {
        var shaper = FontCache.GetShaper(Bundled.FontPriority.Single(font => font.Font == "Inter-400-latin.ttf").TypeFace);
        using var buffer = new HarfBuzzSharp.Buffer();

        shaper.Shape("#️α", buffer);
        var inside = buffer.GetGlyphInfoSpan();
        Assert.NotEqual(0u, inside[0].Codepoint);
        Assert.Equal(0u, inside[^1].Codepoint);

        shaper.Shape("☺︎α", buffer);
        Assert.Equal(0u, buffer.GetGlyphInfoSpan()[0].Codepoint);
    }

    [Theory]
    [InlineData(32)]
    [InlineData(33)]
    [InlineData(100)]
    public void RangeCandidatesLeaveCompositionToTheShaper(int Count)
    {
        var font = Bundled.FontPriority.Single(font => font.Font == "Inter-400-latin.ttf");
        var text = string.Concat(Enumerable.Repeat("A\u0300", Count));
        using var workspace = new MeasurementWorkspace();
        Assert.True(font.RangeMatch(text));
        var classified = Bundled.Classify(text, false, new UScript.ScriptContext(), workspace.Shaping);
        Assert.Same(font.TypeFace, Assert.Single(classified.Runs.ToArray()).TypeFace);
        Assert.True(FontCache.GetShaper(font.TypeFace).SupportedByFont(text, workspace.Shaping.Buffer));
    }

    [Fact]
    public void RangeLimitsAlsoApplyToComposedCharacters()
    {
        var face = Typeface.FromFile(System.IO.Path.Combine(AppContext.BaseDirectory, "Fonts", "Inter-400-latin.ttf"));
        using var shaper = new HarfBuzz(face, Fonts.TWITCH_FONT_SIZE);
        shaper.LimitToUnicodeRanges([0x41..0x5A, 0x300..0x36F]);
        using var buffer = new HarfBuzzSharp.Buffer();
        shaper.Shape("A\u0300", buffer);
        Assert.True(shaper.TryGetNominalGlyph('A', out var baseGlyph));
        Assert.Equal(baseGlyph, buffer.GetGlyphInfoSpan()[0].Codepoint);
        Assert.True(shaper.TryGetNominalGlyph((char)0x00C0, out var composed));
        Assert.DoesNotContain(buffer.GetGlyphInfoSpan().ToArray(), glyph => glyph.Codepoint == composed);
    }

    [Fact]
    public void ARangeLimitedShaperCanBeDisposed()
    {
        var face = Typeface.FromFile(System.IO.Path.Combine(AppContext.BaseDirectory, "Fonts", "Inter-400-latin.ttf"));
        var shaper = new HarfBuzz(face, Fonts.TWITCH_FONT_SIZE);
        shaper.LimitToUnicodeRanges([0x41..0x5A]);
        using (var buffer = new HarfBuzzSharp.Buffer()) {
            Assert.True(shaper.SupportedByFont("AB", buffer));
            Assert.False(shaper.SupportedByFont("Ab", buffer));
        }
        shaper.Dispose();
    }

    /// <summary>
    /// Blink forces the monochrome emoji font for its blocks: U+1F100 is not an emoji, but in Enclosed Alphanumeric
    /// Supplement.
    /// </summary>
    [Fact]
    public void EnclosedAlphanumericSupplementUsesTheMonochromeEmojiFont()
    {
        var run = Assert.Single(TextSetup.ClassifyText("\U0001F100", false));
        Assert.Equal("Segoe UI Symbol", run.TypeFace.FamilyName);
    }
}
