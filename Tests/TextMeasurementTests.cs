using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MessageHeightTwitch;
using MessageHeightTwitch.Measurement.EmoteRewriting;
using MessageHeightTwitch.Measurement.LineFitting;
using MessageHeightTwitch.Measurement.Shared;
using MessageHeightTwitch.Measurement.Shared.Shaping;

using Tests.Utils;

using Xunit;

namespace Tests;

/*
 * Features of message texts measured together in production Chrome (text-measurements.json).
 * Each text is replayed as a message of tetyys (display name TETYYS) with the case's badges,
 * and the browser reports its DOM Range widths on one line, its height, and where its lines
 * end in the chat. Cases without plain-text range data can carry a height measured with the
 * emotes, emoji images, display name or margins they list, or isolated modifier-layout,
 * shaping or cached break-search expectations.
 */
public class TextMeasurementTests
{
    private static readonly Lazy<Dictionary<string, TextMeasurementCase>> Cases = new(() =>
        MeasurementTestData.Read<TextMeasurementCase>("text-measurements.json").ToDictionary(test => test.Name));

    private static TheoryData<string> Names(Func<TextMeasurementCase, bool> Measured)
    {
        var data = new TheoryData<string>();
        foreach (var test in Cases.Value.Values.Where(Measured))
            data.Add(test.Name);
        return data;
    }

    public static TheoryData<string> AllCases() => Names(test => test.Height != null);
    public static TheoryData<string> LineCases() => Names(test => test.Lines != null);
    public static TheoryData<string> SeparatorCases() => Names(test => test.SeparatorOffset != 0);
    public static TheoryData<string> FontCases() => Names(test => test.ExpectedFontFamily != null);
    public static TheoryData<string> ModifierCases() => Names(test => test.ModifierLayouts != null);
    public static TheoryData<string> ShapedWidthCases() => Names(test => test.ShapedWidths != null);
    public static TheoryData<string> BreakSearchCases() => Names(test => test.BreakSearchWidths != null);

    [Theory]
    [MemberData(nameof(ShapedWidthCases), DisableDiscoveryEnumeration = true)]
    public void ShapedWidthsMatchExpected(string Name)
    {
        var test = Cases.Value[Name];
        var context = new UScript.ScriptContext();
        foreach (var text in test.ScriptContext ?? [])
            context.Advance(text);
        var runs = FontFallbackList.Shared.ClassifyText(test.Text, false, out _, ScriptContext: context);
        foreach (var width in test.ShapedWidths!)
            Assert.Equal(width[1], BlinkLineModel.Width(test.Text, ..(int)width[0], runs));
    }

    [Theory]
    [MemberData(nameof(BreakSearchCases), DisableDiscoveryEnumeration = true)]
    public void CachedBreakSearchMatchesFreshMeasurement(string Name)
    {
        var test = Cases.Value[Name];
        var runs = TextSetup.ClassifyText(test.Text, false);
        using var workspace = new MeasurementWorkspace();
        var widths = new TextWidths(TextSetup.Classified(test.Text, runs, workspace), workspace.Shaping, workspace.LineBreaking.Widths);
        widths.Measure(test.Text.Length);
        widths.Measure(test.Text.Length - 1);
        foreach (var character in new[] { false, true }) {
            foreach (var maximum in test.BreakSearchWidths!) {
                var expected = BlinkLineModel.Break(test.Text, character, runs, maximum);
                var actual = widths.FindBreak(workspace.LineBreaking, character, maximum, 0);
                Assert.Equal(expected, actual);
                if (!character && actual.HasValue)
                    Assert.Equal(BlinkLineModel.Width(test.Text, ..actual.Value.End, runs), widths.Measure(actual.Value.End.Value));
            }
        }
    }

    /// <summary>
    /// The DOM Range width of every prefix that ends at a grapheme boundary, and of the case's other ranges. The whole
    /// text's width is production's width of the message; a prefix ending inside a glyph sequence (a ligature, or a
    /// cluster of several graphemes) takes Blink's XPositionForOffset, which line layout does not use.
    /// </summary>
    [Fact]
    public void RangeWidthsMatchBrowser()
    {
        Parallel.ForEach(Cases.Value.Values.Where(test => !test.IsAction && test.Prefixes != null), AssertRangeWidthsMatchBrowser);
    }

    [Fact]
    public void ActionRangeWidthsMatchBrowser() =>
        Parallel.ForEach(Cases.Value.Values.Where(test => test.IsAction && test.Prefixes != null), AssertRangeWidthsMatchBrowser);

    private static void AssertRangeWidthsMatchBrowser(TextMeasurementCase test)
    {
        var ranges = test.Prefixes!
            .Select(prefix => (Start: 0, End: (int)prefix[0], Width: prefix[1]))
            .Concat((test.Ranges ?? [])
                .Select(range => (Start: (int)range[0], End: (int)range[1], Width: range[2]))
            );

        // A text wider than the long line wraps there; its prefixes are on the first line, which ends at the last.
        var lastEnd = (int)test.Prefixes!.Select(prefix => prefix[0]).DefaultIfEmpty(test.Text.Length).Max();
        var model = new DomRangeModel(test.Text, lastEnd < test.Text.TrimEnd(' ').Length && test.Prefixes!.Length > 1 ? lastEnd : null, test.TextNodeEnds, test.IsAction);

        var mismatches = ranges
            .Select(range => (range, Actual: model.Measure(range.Start..range.End)))
            .Where(result => result.Actual != result.range.Width)
            .Select(result => $"[{result.range.Start}, {result.range.End}): expected {result.range.Width}, measured {result.Actual}").ToList();
        Assert.True(mismatches.Count == 0, $"{test.Name}: {string.Join("; ", mismatches)}");
    }

    [Theory]
    [MemberData(nameof(AllCases), DisableDiscoveryEnumeration = true)]
    public void HeightMatchesBrowser(string Name)
    {
        var test = Cases.Value[Name];
        if (test.Height is not { } expected) return;
        var (measurer, twitch) = Fixture.ForCase(test);
        var height = measurer.CalculateMessageHeightEx(test.Text, "tetyys", test.DisplayName, test.Parameters(twitch));
        Assert.True(height == expected, $"{Name}: expected height {expected}, measured {height}.");
    }

    [Theory]
    [MemberData(nameof(ModifierCases), DisableDiscoveryEnumeration = true)]
    public void ModifierLayoutMatchesBrowser(string Name)
    {
        var test = Cases.Value[Name];
        var (measurer, twitch) = Fixture.ForCase(test);
        var parameters = test.Parameters(twitch);
        using var rewriter = new ModifierRewriter();
        var prepared = rewriter.Rewrite(test.Text, parameters, measurer.Emotes);

        Assert.True(prepared.Text.SequenceEqual(test.RenderedText));
        var actual = prepared.Emotes.OrderBy(pair => pair.Key).ToArray();
        Assert.Equal(test.ModifierLayouts!.Length, actual.Length);
        for (var i = 0; i < actual.Length; i++) {
            var (offset, emote) = actual[i];
            var expected = test.ModifierLayouts[i];
            Assert.Equal(expected.Offset, offset);
            Assert.True(prepared.Text.Slice(offset, emote.Length).SequenceEqual(expected.Text));
            Assert.InRange(emote.Size.Width, expected.Width - 0.02f, expected.Width + 0.02f);
            Assert.InRange(emote.Size.Height, expected.Height - 0.02f, expected.Height + 0.02f);
        }
    }

    /// <summary>Where production's layout starts each line of the message text, including a first line below the name.</summary>
    [Theory]
    [MemberData(nameof(LineCases), DisableDiscoveryEnumeration = true)]
    public void LineBreaksMatchBrowser(string Name)
    {
        var test = Cases.Value[Name];
        // A collapsed space between DOM text nodes can have no Range rectangle. Lines records the last
        // boxed character's end; the next line begins after any such discarded spaces.
        var expected = (test.FreshLine ? [0] : Array.Empty<int>()).Concat(test.Lines!.SkipLast(1).Select(line => {
            var start = (int)line[0];
            while (start < test.Text.Length && test.Text[start] == ' ') start++;
            return start;
        }));
        var starts = new List<int>();
        var (measurer, twitch) = Fixture.ForCase(test);
        measurer.CalculateMessageHeightEx(test.Text, "tetyys", test.DisplayName, test.Parameters(twitch), starts);
        Assert.Equal(expected, starts);
    }

    public static TheoryData<string> WordLineCases()
    {
        return Names(test => WordLines(test).Any() && OneBidiItem(test.Text));
    }

    /// <summary>
    /// The Range width of each line that holds part of a single word, the width browser gives the line of a word it
    /// breaks: the line's fragment, which Blink's ShapingLineBreaker builds from the original shape between
    /// safe-to-break offsets and reshapes where the line starts or ends at an unsafe one (inside a ligature, a kerned
    /// pair, or Arabic joining). A space that hangs at the line's end is not in its Range. The line-break assertions
    /// separately check how the controller fits shaped items into lines. In a message of several bidi items, a line's
    /// Range is 1/64 px narrower than this width for some lines, and not always the difference of rounded positions
    /// from the item's start either, so only messages of one item are checked.
    /// </summary>
    [Theory]
    [MemberData(nameof(WordLineCases), DisableDiscoveryEnumeration = true)]
    public void WordLineWidthsMatchBrowser(string Name)
    {
        var test = Cases.Value[Name];
        var fonts = FontFallbackList.Shared;
        var workspace = MeasurementWorkspace.Pool.Get();
        try {
            var classified = test.IsAction ? TextSetup.ClassifyMessage(test.Text, workspace, out _, out _, IsAction: true) : fonts.ClassifyAfter("TETYYS: ", test.Text, workspace);
            var widths = new TextWidths(classified, workspace.Shaping, workspace.LineBreaking.Widths, 0, Origin: 0);
            var breaker = new ShapingLineBreaker(widths, workspace.LineBreaking);
            foreach (var (line, expected) in WordLines(test)) {
                var start = line.Start.Value;
                var end = line.End.Value;
                float actual = 0;
                // Each DOM text node becomes a separately snapped fragment, including punctuation beside a mention.
                foreach (var boundary in (test.TextNodeEnds ?? []).Where(boundary => boundary > start && boundary < end).Append(end)) {
                    actual += breaker.ShapeLineAt(start..boundary);
                    start = boundary;
                }
                for (var boundary = 0; boundary < 2; boundary++) {
                    var offset = boundary == 0 ? line.Start.Value - 1 : end - 1;
                    if (offset < 0 || boundary == 1 && end == test.Text.Length || test.Text[offset] != '\u00ad')
                        continue;
                    // A Range includes its ending hyphen and, at a soft-hyphen boundary, the previous line's
                    // inserted hyphen too. ShapeLineAt measures only the source text on this line.
                    var font = test.IsAction ? workspace.Paragraph.LineItems[workspace.Paragraph.LineItemAt(workspace.Paragraph.BodyStart + offset)].Font!.Value : new MessageHeightTwitch.Font(fonts, false);
                    var primary = fonts.FontPriority[0].GetTypeFace(font.Bold, font.Italic);
                    using var buffer = new HarfBuzzSharp.Buffer();
                    ReadOnlySpan<char> hyphenText = primary.HasGlyph(0x2010) ? "\u2010" : "-";
                    var hyphen = FontCache.GetShaper(primary).MeasureFixed(hyphenText, .., buffer, HarfBuzzSharp.Direction.LeftToRight, Script: UScriptCode.USCRIPT_COMMON);
                    actual += HarfBuzz.FixedToFloat(TextWidths.SnapFloat(hyphen));
                }
                Assert.True(actual == expected, $"{Name}: line [{line}) expected {expected}, measured {actual}.");
            }
        } finally {
            MeasurementWorkspace.Pool.Return(workspace);
        }
    }

    private static bool OneBidiItem(string Text)
    {
        var fonts = FontFallbackList.Shared;
        var workspace = MeasurementWorkspace.Pool.Get();
        try {
            return fonts.ClassifyAfter("TETYYS: ", Text, workspace).Bidi.Length == 1;
        } finally {
            MeasurementWorkspace.Pool.Return(workspace);
        }
    }

    // The lines of a single word (or part of one), without the spaces that hang at their ends, and their widths.
    private static IEnumerable<(Range Line, float Width)> WordLines(TextMeasurementCase Test)
    {
        var start = 0;
        foreach (var line in Test.Lines ?? []) {
            var end = (int)line[0];
            var content = Test.Text[start..end].TrimEnd(' ');
            if (content.Length > 0 && !content.Contains(' '))
                yield return (start..(start + content.Length), line[1]);
            start = end;
        }
    }

    /// <summary>
    /// How far the message moves right when it starts with marks that join the separator: Chrome's box for the
    /// message, against the message "a". The separator segment grows by as much.
    /// </summary>
    [Theory]
    [MemberData(nameof(SeparatorCases), DisableDiscoveryEnumeration = true)]
    public void SeparatorWidthMatchesBrowser(string Name)
    {
        var test = Cases.Value[Name];
        var workspace = MeasurementWorkspace.Pool.Get();
        try {
            TextSetup.ClassifyMessage(test.Text, workspace, out _, out var joined, test.IsAction);
            TextSetup.ClassifyMessage("a", workspace, out _, out var plain, test.IsAction);
            Assert.Equal(test.SeparatorOffset, joined - plain);
        } finally {
            MeasurementWorkspace.Pool.Return(workspace);
        }
    }

    [Theory]
    [MemberData(nameof(FontCases), DisableDiscoveryEnumeration = true)]
    public void SelectExpectedFont(string Name)
    {
        var test = Cases.Value[Name];
        var family = Assert.Single(TextSetup.ClassifyText(test.Text, false)).TypeFace.FamilyName;
        Assert.Equal(test.ExpectedFontFamily, family);
    }

    /// <summary>
    /// Inside a glyph sequence, a DOM Range shares the sequence's advance among its graphemes and line layout takes the
    /// sequence's start: production keeps the line layout position (Inter's "->" ligature, a Javanese cluster).
    /// </summary>
    [Theory]
    [InlineData("[de ->", 5)]
    [InlineData("◌꦳꧀ꦠ", 2)]
    public void PrefixInsideGlyphSequenceKeepsLineLayoutPosition(string Text, int End)
    {
        Assert.NotEqual(new DomRangeModel(Text).Measure(..End), MeasurePrefix(Text, End));
    }

    /// <summary>
    /// The ASCII fast path sums pair advances; each prefix's last character takes its kern with the next
    /// character, so it must agree with shaping for every prefix, as the browser's positions do.
    /// </summary>
    [Theory]
    [InlineData("AVATAR")]
    [InlineData("Wave To Yo")]
    [InlineData("Tu Ty Te Vo Wa")]
    [InlineData("Yo Yo Yo 'Ta' Te? To.")]
    public void AsciiPrefixesMatchShapingIncludingFollowingKerning(string Text)
    {
        var fonts = FontFallbackList.Shared;
        var ascii = Assert.IsType<AsciiFastPath>(fonts.AsciiRegular);
        Assert.True(ascii.TryMeasure(Text, out _), $"not eligible for the fast path: {Text}");
        var workspace = MeasurementWorkspace.Pool.Get();
        try {
            var context = new UScript.ScriptContext();
            context.Advance("TETYYS: ");
            var shaped = new TextWidths(fonts.Classify(Text, false, context, workspace.Shaping), workspace.Shaping, workspace.LineBreaking.Widths);
            var fast = new TextWidths(Text, ascii);
            for (var end = 1; end <= Text.Length; end++)
                Assert.Equal(shaped.Measure(end), fast.Measure(end));
        } finally {
            MeasurementWorkspace.Pool.Return(workspace);
        }
    }

    /// <summary>
    /// Chrome's line breaks count the kern after a letter that does not join forward toward the next letter
    /// (HarfBuzz.CharacterPositions), but its glyph advances keep the kern on the letter: the DOM Range of each prefix of
    /// an Arabic text follows the kerned advances (rtl-letter-ranges-chrome.json, rounding within 1/32 px). Moving the
    /// kern after the alef at offset 1 would widen the prefix that ends there by 0.56 px.
    /// </summary>
    [Fact]
    public void RightToLeftRangesKeepTheKernOnTheLetter()
    {
        var text = "لاهدكنًسعصلغژعكب";
        float[] chrome = [3.09375f, 7.609375f, 15.640625f, 22.53125f, 28.296875f, 33.109375f, 33.109375f, 45f, 52.296875f,
            65.578125f, 69.78125f, 77.078125f, 82.265625f, 89.3125f, 96.421875f, 111.71875f];
        var fonts = FontFallbackList.Shared;
        var workspace = MeasurementWorkspace.Pool.Get();
        try {
            var context = new UScript.ScriptContext();
            context.Advance("TETYYS: ");
            var run = Assert.Single(fonts.Classify(text, false, context, workspace.Shaping).Runs.ToArray());
            var shaper = FontCache.GetShaper(run.TypeFace);
            shaper.MeasureFixed(text, .., workspace.Shaping.Buffer, Direction: HarfBuzzSharp.Direction.RightToLeft, Script: run.Script);
            var infos = workspace.Shaping.Buffer.GetGlyphInfoSpan();
            var glyphs = workspace.Shaping.Buffer.GetGlyphPositionSpan();
            for (var end = 1; end <= text.Length; end++) {
                long prefix = 0, kerned = 0;
                for (var g = 0; g < infos.Length; g++) {
                    if (infos[g].Cluster >= end)
                        continue;
                    prefix += shaper.FixedAdvance(glyphs[g].XAdvance);
                    // The same prefix with a placement kern of its last letter moved out of it.
                    kerned += shaper.FixedAdvance(glyphs[g].XAdvance) - (g > 0 && infos[g - 1].Cluster >= end ? Math.Min(0, glyphs[g].XOffset) : 0);
                }
                Assert.InRange(chrome[end - 1] - HarfBuzz.FixedToFloat(prefix), -1 / 32f, 1 / 32f);
                if (end == 2)
                    Assert.True(HarfBuzz.FixedToFloat(kerned) - chrome[end - 1] > 0.5f);
            }
        } finally {
            MeasurementWorkspace.Pool.Return(workspace);
        }
    }

    /// <summary>
    /// Chrome line layout width of text[..End) in a message: characters that join the cluster of the separator's
    /// space are measured with ": ", and a DOM Range credits that cluster to the space, so the prefix starts after them.
    /// Kerning and cluster positions count toward the boundary from the following character, as Blink's
    /// CachedPositionForOffset does.
    /// </summary>
    internal static float MeasurePrefix(string Text, int End)
    {
        var workspace = MeasurementWorkspace.Pool.Get();
        try {
            var classified = TextSetup.ClassifyMessage(Text, workspace, out var lead, out _);
            return End <= lead ? 0 : new TextWidths(classified, workspace.Shaping, workspace.LineBreaking.Widths).Measure(End - lead);
        } finally {
            MeasurementWorkspace.Pool.Return(workspace);
        }
    }
}
