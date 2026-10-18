using Icu;

using MessageHeightTwitch;
using MessageHeightTwitch.Measurement.Shared;
using MessageHeightTwitch.Measurement.Shared.Shaping;

using System;
using System.Globalization;
using System.Linq;

using Tests.Utils;

using Xunit;

using Buffer = HarfBuzzSharp.Buffer;
using Mht = MessageHeightTwitch.MessageHeightTwitch;

namespace Tests;

public class Algo
{
    static Algo()
    {
        System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(typeof(Mht).TypeHandle);
    }

    [Theory]
    [InlineData("と、")]
    [InlineData("ボス、")]
    [InlineData("、と")]
    [InlineData("、ボス")]
    public void KanaPunctuationStaysInScriptRun(string Text)
    {
        var segment = Assert.Single(TextSetup.GetScriptSegments(Text, new UScript.ScriptContext()));
        Assert.Equal(0..Text.Length, segment.Range);
        Assert.Equal(UScriptCode.USCRIPT_HIRAGANA, segment.Script);
    }

    [Fact]
    public void KanaPunctuationUsesPrecedingInlineContext()
    {
        var context = new UScript.ScriptContext();
        context.Advance("と");
        var fonts = FontFallbackList.Shared;
        const string text = "、abc";
        using var buffer = new Buffer();
        var runs = fonts.ClassifyText(text, false, out _, ScriptContext: context);

        Assert.Equal(0..1, runs[0].Range);

        /*
         * Blink picks the fallback family from the hint's script, so the ideographic comma
         * follows the en-US Han default even in a kana run
         */
        Assert.Equal("Microsoft YaHei", runs[0].TypeFace.FamilyName);
        Assert.Equal(14f, BlinkLineModel.Width(text, ..1, runs));
        Assert.Equal(UScriptCode.USCRIPT_HIRAGANA, context.Script);
    }

    [Theory]
    [InlineData("ja-JP")]
    [InlineData("zh-TW")]
    public void GenericHanFallbackUsesFixedChromiumDefaultLocale(string AmbientLocale)
    {
        var previousCulture = CultureInfo.CurrentCulture;
        var previousUiCulture = CultureInfo.CurrentUICulture;
        try {
            var culture = CultureInfo.GetCultureInfo(AmbientLocale);
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = culture;

            const string text = "，：";
            var runs = TextSetup.ClassifyText(text, false);
            Assert.Equal("Microsoft YaHei", Assert.Single(runs).TypeFace.FamilyName);
            Assert.Equal(28f, BlinkLineModel.Width(text, .., runs));
        } finally {
            CultureInfo.CurrentCulture = previousCulture;
            CultureInfo.CurrentUICulture = previousUiCulture;
        }
    }

    [Theory]
    [InlineData("officé", true)]
    // Printable ASCII is classified without shaping: Inter's latin subset has every glyph.
    [InlineData("office", false)]
    [InlineData("با", true)]
    [InlineData("aبا", false)]
    [InlineData("٩و٩", false)]
    public void PrimaryFontProbeWidthMatchesIndependentMeasurement(string Text, bool HasProbeWidth)
    {
        using var buffer = new HarfBuzzSharp.Buffer();
        var fonts = FontFallbackList.Shared;
        var runs = fonts.ClassifyText(Text, false, out var probeWidth);
        Assert.Equal(HasProbeWidth, probeWidth.HasValue);
        if (probeWidth.HasValue)
            Assert.Equal(BlinkLineModel.Width(Text, .., runs), probeWidth.Value);
    }

    [Theory]
    [InlineData("6666666666666666666666666666666666")]
    [InlineData("office affinity AVATAR")]
    [InlineData("بابابابابابابابابا")]
    public void ShapedWidthKeepsPerGlyphRounding(string Text)
    {
        var typeface = Assert.Single(TextSetup.ClassifyText(Text, false)).TypeFace;
        using var face = new HarfBuzzSharp.Face(typeface.Blob, 0);

        /*
         * Blink's HarfBuzzFace: a 16.16 font at the CSS size whose nominal advances are Skia's
         * DirectWrite float advance (size * advanceWidth / unitsPerEm) truncated to 16.16;
         * ShapeResult sums the 16.16 advances exactly (TextRunLayoutUnit).
         */
        using var unscaled = new HarfBuzzSharp.Font(face);
        using var font = new HarfBuzzSharp.Font(unscaled);

        var size = (int)(Fonts.TWITCH_FONT_SIZE * HarfBuzz.FIXED_ONE);
        font.SetScale(size, size);

        using var functions = new HarfBuzzSharp.FontFunctions();
        functions.SetHorizontalGlyphAdvanceDelegate((_, _, glyph) => (int)(Fonts.TWITCH_FONT_SIZE * unscaled.GetHorizontalGlyphAdvance(glyph) / face.UnitsPerEm * HarfBuzz.FIXED_ONE), null);
        font.SetFontFunctions(functions);

        using var buffer = new HarfBuzzSharp.Buffer();
        buffer.AddUtf16(Text.AsSpan());
        buffer.GuessSegmentProperties();
        font.Shape(buffer);

        long total = 0;
        foreach (var position in buffer.GetGlyphPositionSpan())
            total += position.XAdvance;
        var expected = HarfBuzz.FixedToFloat(total);

        using var shaper = new HarfBuzz(typeface, Fonts.TWITCH_FONT_SIZE);
        Assert.Equal(expected, shaper.MeasureWidth(Text));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RepeatedClustersUseTheirSegmentsFallbackCandidate(bool Bold)
    {
        var text = string.Concat(Enumerable.Repeat("ᜑ⃦⃕⃢", 30));
        var expected = FontFallbackList.Shared.FullUnicodeFontPriority[0].GetTypeFace(Bold);

        var run = Assert.Single(TextSetup.ClassifyText(text, Bold));
        Assert.Equal(0..text.Length, run.Range);
        Assert.Same(expected, run.TypeFace);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RepeatedClustersKeepScriptAndWeightBoundaries(bool Bold)
    {
        const string text = "aباaبا";
        var fonts = TextSetup.ClassifyText(text, Bold);

        var latin = Assert.Single(TextSetup.ClassifyText("a", Bold)).TypeFace;
        var arabic = Assert.Single(TextSetup.ClassifyText("با", Bold)).TypeFace;
        Assert.Equal([0..1, 1..3, 3..4, 4..6], fonts.Select(run => run.Range));
        Assert.Same(latin, fonts[0].TypeFace);
        Assert.Same(arabic, fonts[1].TypeFace);
        Assert.Same(latin, fonts[2].TypeFace);
        Assert.Same(arabic, fonts[3].TypeFace);
    }

    [Theory]
    [InlineData("aبا ")]
    [InlineData("ဗီူ6ူ66 ")]
    [InlineData("ᜑ⃦⃕⃢ a ")]
    [InlineData("٩(^ᗜ^ )وabc")]
    [InlineData("٩و٩")]
    [InlineData("あ「【Steamセール】」abc")]
    [InlineData("a(「あ」)abc")]
    [InlineData("Dｰ12これabc")]
    [InlineData("Aｰウサギabc")]
    public void CachedPrefixWidthsMatchIndependentMeasurement(string Text)
    {
        using var buffer = new Buffer();
        var fonts = FontFallbackList.Shared;
        var runs = fonts.ClassifyText(Text, false, out var shapedWidth);
        Assert.Null(shapedWidth);

        using var workspace = new MeasurementWorkspace();
        var widths = new TextWidths(TextSetup.Classified(Text, runs, workspace, shapedWidth), workspace.Shaping, workspace.LineBreaking.Widths);
        foreach (var end in new[] { Text.Length, Text.Length - 1, 1, Text.Length - 1, Text.Length })
            Assert.Equal(BlinkLineModel.Width(Text, ..end, runs), widths.Measure(end));
    }

    /// <summary>
    /// Inter kerns a right single quote before a period, comma, ellipsis or low quote by more than its advance, so the
    /// position after the quote is negative: it rounds up to the next 1/64 px, not a whole unit further toward zero.
    /// </summary>
    [Theory]
    [InlineData("\u2019.")]
    [InlineData("\u2019,")]
    [InlineData("\u2019\u2026")]
    [InlineData("\u2019\u201a")]
    public void NegativePrefixWidthRoundsUp(string Text)
    {
        var fonts = FontFallbackList.Shared;
        var runs = fonts.ClassifyText(Text, false, out var shapedWidth);
        using var workspace = new MeasurementWorkspace();
        var widths = new TextWidths(TextSetup.Classified(Text, runs, workspace, shapedWidth), workspace.Shaping, workspace.LineBreaking.Widths);

        Assert.True(widths.ExactPrefix(1) < 0);
        Assert.Equal(BlinkLineModel.Width(Text, ..1, runs), widths.Measure(1));
    }

    [Fact]
    public void CachedPrimaryProbeWidthMatchesIndependentMeasurement()
    {
        const string text = "officé ";
        using var buffer = new Buffer();
        var fonts = FontFallbackList.Shared;
        var runs = fonts.ClassifyText(text, false, out var shapedWidth);
        Assert.NotNull(shapedWidth);

        using var workspace = new MeasurementWorkspace();
        var widths = new TextWidths(TextSetup.Classified(text, runs, workspace, shapedWidth), workspace.Shaping, workspace.LineBreaking.Widths);
        Assert.Equal(BlinkLineModel.Width(text, .., runs), widths.Measure(text.Length));
        Assert.Equal(BlinkLineModel.Width(text, ..^1, runs), widths.Measure(text.Length - 1));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MyanmarCombiningDigitKeepsItsClusterWhilePlainDigitsUseInter(bool Bold)
    {
        var fonts = TextSetup.ClassifyText("ဗီ66ူ66", Bold);
        var inter = TextSetup.ClassifyText("66", Bold).Single().TypeFace;

        Assert.Equal([(0, 2), (2, 3), (3, 5), (5, 7)], fonts.Select(font => (font.Range.Start.Value, font.Range.End.Value)).ToArray());
        Assert.Same(inter, fonts[1].TypeFace);
        Assert.Same(inter, fonts[3].TypeFace);
        Assert.NotSame(inter, fonts[2].TypeFace); // The digit plus mark falls back together.
        Assert.Equal(fonts[0].TypeFace.FamilyName, fonts[2].TypeFace.FamilyName);
        Assert.Equal(0..2, TextSetup.ClassifyText("6ူ", Bold).Single().Range);
    }

    [Theory]
    [InlineData("office")]
    [InlineData("ဗီူဗီူ")]
    public void AdjacentClustersWithTheSameFontRemainOneShapingRun(string Text)
    {
        var run = Assert.Single(TextSetup.ClassifyText(Text, false));
        Assert.Equal(0..Text.Length, run.Range);
    }

    [Theory]
    [InlineData("𐎧⃦⃕⃢")]
    [InlineData("1ဗီူဗီူ 2ဗီူဗီူ")]
    [InlineData("ဗီ66ူ66")]
    [InlineData("")]
    public void FontRangesDontOverlap(string Text)
    {
        var end = 0;
        foreach (var font in TextSetup.ClassifyText(Text, false)) {
            Assert.Equal(end, font.Range.Start.Value);
            Assert.InRange(font.Range.End.Value, end + 1, Text.Length);
            end = font.Range.End.Value;
        }
        Assert.Equal(Text.Length, end);
    }

    [Theory]
    [InlineData("ᜑ⃦⃕⃢", false)]
    [InlineData("စ⃦⃕⃢", false)]
    [InlineData("𐎧⃦⃕⃢", false)]
    [InlineData("a⃦⃕⃢", false)]
    [InlineData("ᜑ⃦⃕⃢", true)]
    [InlineData("စ⃦⃕⃢", true)]
    [InlineData("𐎧⃦⃕⃢", true)]
    [InlineData("a⃦⃕⃢", true)]
    public void UnsupportedCombiningClustersUseFirstCssCandidate(string Text, bool Bold)
    {
        var run = Assert.Single(TextSetup.ClassifyText(Text, Bold));
        var expectedFont = Text.StartsWith("a", StringComparison.Ordinal) ? TextSetup.ClassifyText("a", Bold).Single().TypeFace : FontFallbackList.Shared.FullUnicodeFontPriority[0].GetTypeFace(Bold);
        using var shaper = new HarfBuzz(run.TypeFace, Fonts.TWITCH_FONT_SIZE);

        Assert.Equal(0..Text.Length, run.Range);
        Assert.Same(expectedFont, run.TypeFace);
        Assert.False(shaper.SupportedByFont(Text));
    }

    [Theory]
    [InlineData(77, 76)]
    [InlineData(79, 78)]
    [InlineData(340, 78)]
    public void ArabicWrapKeepsJoiningContext(int Length, int ChromeBreak)
    {
        var text = string.Concat(Enumerable.Repeat("با", 170))[..Length];
        var fonts = TextSetup.ClassifyText(text, false);
        var boundary = TextSetup.FindBreak(text, true, fonts, 293f);

        Assert.True(boundary.HasValue);
        Assert.Equal(ChromeBreak, boundary.Value.Start.Value);
    }

    [Theory]
    [InlineData("あア", UScriptCode.USCRIPT_HIRAGANA)]
    [InlineData("ア･ﾟ", UScriptCode.USCRIPT_HIRAGANA)]
    [InlineData("･ﾟア", UScriptCode.USCRIPT_HIRAGANA)]
    [InlineData("アー", UScriptCode.USCRIPT_HIRAGANA)]
    [InlineData("অ।", UScriptCode.USCRIPT_BENGALI)]
    [InlineData("।অ", UScriptCode.USCRIPT_BENGALI)]
    public void SharedCharactersIntersectScriptExtensions(string Text, UScriptCode Script)
    {
        var run = Assert.Single(TextSetup.GetScriptSegments(Text, new UScript.ScriptContext()));

        Assert.Equal(0..Text.Length, run.Range);
        Assert.Equal(Script, run.Script);
    }

    [Fact]
    public void InheritedScriptExtensionConstrainsCommonBase()
    {
        var runs = TextSetup.GetScriptSegments("A6͂", new UScript.ScriptContext()).ToArray();
        Assert.Equal([(0..1, UScriptCode.USCRIPT_LATIN), (1..3, UScriptCode.USCRIPT_GREEK)], runs);
    }

    [Theory]
    [InlineData("(", ")")]
    [InlineData("[", "]")]
    [InlineData("⟨", "⟩")]
    [InlineData("⟪", "⟫")]
    public void UnicodeBracketPairsPreserveContextAcrossChunks(string Opening, string Closing)
    {
        var context = new UScript.ScriptContext();
        context.Advance("ဗီူ");
        context.Advance(Opening + "abc");
        Assert.Equal(UScriptCode.USCRIPT_LATIN, context.Script);
        context.Advance(Closing);
        Assert.Equal(UScriptCode.USCRIPT_MYANMAR, context.Script);
    }

    [Theory]
    [InlineData("（", "）")]
    [InlineData("〈", "〉")]
    [InlineData("｢", "｣")]
    public void EastAsianBracketsStayWithKana(string Opening, string Closing)
    {
        var text = "a" + Opening + "あ" + Closing + "b";
        var runs = TextSetup.GetScriptSegments(text, new UScript.ScriptContext()).ToArray();
        Assert.Equal([ (0..1, UScriptCode.USCRIPT_LATIN), (1..4, UScriptCode.USCRIPT_HIRAGANA), (4..5, UScriptCode.USCRIPT_LATIN) ], runs);
    }

    [Fact]
    public void ClassificationDoesNotAdvanceBracketContext()
    {
        var context = new UScript.ScriptContext(UScriptCode.USCRIPT_MYANMAR);
        _ = TextSetup.GetScriptSegments("⟨abc", context).ToArray();
        context.Advance("⟩");
        Assert.Equal(UScriptCode.USCRIPT_MYANMAR, context.Script);
    }

    [Fact]
    public void LeadingBracketStaysPendingAcrossNeutralChunks()
    {
        var context = new UScript.ScriptContext();
        context.Advance("⟨");
        context.Advance(" ");
        context.Advance("αabc");
        Assert.Equal(UScriptCode.USCRIPT_LATIN, context.Script);
        context.Advance("⟩");
        Assert.Equal(UScriptCode.USCRIPT_GREEK, context.Script);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AuthorSeparatorBodyAndMentionKeepTheirOwnStyles(bool IsAction)
    {
        using var workspace = new MeasurementWorkspace();
        var parameters = Mht.CalculateMessageHeightParams.Default with {
            IsAction = IsAction
        };

        var measurer = Fixture.CreateTextMeasurer(null);
        const string message = "hello @user_հայերեն ©";
        measurer.ClassifyMessageText(message, "tetyys", "TETYYS", parameters, workspace, out _, out _);
        var paragraph = workspace.Paragraph;

        Assert.Equal("TETYYS" + (IsAction ? " " : ": ") + message, paragraph.Text.ToString());
        Assert.All(paragraph.Items.Where(item => item.Region != PARAGRAPH_REGION.BODY), item => Assert.False(item.Italic));
        Assert.All(paragraph.Items.Where(item => item.Region == PARAGRAPH_REGION.BODY), item => Assert.Equal(IsAction, item.Italic));

        var mention = Assert.Single(paragraph.Items, item => item.Region == PARAGRAPH_REGION.BODY && item.Bold);
        Assert.Equal("@user_հայերեն", paragraph.Text[mention.Start..mention.End].ToString());
        Assert.True(paragraph.Items[0].Bold);

        // The Armenian part falls through the web fonts to Arial
        var armenian = Assert.Single(paragraph.Runs, run => run.TypeFace.FamilyName == "Arial");
        Assert.Equal(IsAction, armenian.TypeFace.IsItalic);
        Assert.Equal(700, armenian.TypeFace.FontWeight);
    }

    [Fact]
    public void SyntheticObliqueKeepsTheInterAdvancesAndASeparateStyleBoundary()
    {
        using var workspace = new MeasurementWorkspace();
        TextSetup.ClassifyMessage("office ©", workspace, out _, out _, IsAction: true);

        var paragraph = workspace.Paragraph;
        var body = Assert.Single(paragraph.Groups, group => group.Start == paragraph.BodyStart);

        Assert.True(body.Font.Italic);
        Assert.False(body.Font.Bold);
        Assert.All(paragraph.Runs.Where(run => run.Range.Start.Value >= paragraph.BodyStart), run => Assert.Same(FontFallbackList.Shared.AsciiRegular.Typeface, run.TypeFace));
        Assert.DoesNotContain(paragraph.Groups, group => group.Start < paragraph.BodyStart && group.End > paragraph.BodyStart);
    }
}
