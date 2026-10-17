using MessageHeightTwitch;
using MessageHeightTwitch.Measurement.Shared;
using MessageHeightTwitch.Measurement.Shared.Shaping;

using System;
using System.Linq;

using Xunit;

namespace Tests
{
    public class FontPriority
    {
        static FontPriority()
        {
            System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(typeof(MessageHeightTwitch.MessageHeightTwitch).TypeHandle);
        }

        public static TheoryData<string, string, bool, bool> OverlappingRanges()
        {
            var data = new TheoryData<string, string, bool, bool>();
            // Expected filenames are fixed independently of the priority list's order.
            var cases = new[] {
                ("\u0131", "Inter-400-latin.ttf"),       // Latin and Latin extended
                ("\u0153", "Inter-400-latin.ttf"),
                ("\u0102", "Inter-400-latin-ext.ttf"),   // Latin extended and Vietnamese
                ("\u20AB", "Inter-400-latin-ext.ttf"),
                ("\u0490", "Inter-400-cyrillic.ttf"),    // Cyrillic and Cyrillic extended
                ("\u04B0", "Inter-400-cyrillic.ttf")
            };
            foreach (var (character, expectedFile) in cases) {
                foreach (var bold in new[] { false, true }) {
                    foreach (var mixedScripts in new[] { false, true })
                        data.Add(character, expectedFile, bold, mixedScripts);
                }
            }
            return data;
        }

        [Theory]
        [MemberData(nameof(OverlappingRanges))]
        public void OverlappingRangesSelectExpectedSubset(string Character, string ExpectedFile, bool Bold, bool MixedScripts)
        {
            var fonts = FontFallbackList.Shared;
            Assert.True(fonts.FontPriority.Count(font => font.RangeMatch(Character)) > 1);

            var expected = fonts.FontPriority.Single(font => font.Font == ExpectedFile).GetTypeFace(Bold);
            
            var text = MixedScripts ? Character + "با" : Character;
            var runs = TextSetup.ClassifyText(text, Bold);

            Assert.Equal(0..Character.Length, runs[0].Range);
            Assert.Same(expected, runs[0].TypeFace);

            if (MixedScripts) {
                Assert.Equal(2, runs.Count);
                Assert.Equal(Character.Length..text.Length, runs[1].Range);
                var arabic = fonts.FontPriority.Single(font => font.Font == "NotoSansArabic-Regular.ttf");
                Assert.Same(arabic.GetTypeFace(Bold), runs[1].TypeFace);
            } else {
                Assert.Single(runs);
            }
        }

        [Theory]
        [InlineData(false, false)]
        [InlineData(false, true)]
        [InlineData(true, false)]
        [InlineData(true, true)]
        /*
         * FontFallbackIterator tries each unicode-range subset in CSS order, so a character that
         * the latin-ext subset's range claims but its file lacks reaches the Vietnamese subset.
         * Chrome 154 measures U+1EAF at 7.90625px alone and before Arabic.
         */
        public void UnsupportedPreferredSubsetFallsThroughToNextSubset(bool Bold, bool MixedScripts)
        {
            const string chr = "\u1EAF";

            var fonts = FontFallbackList.Shared;
            var preferred = fonts.FontPriority.Single(font => font.Font == "Inter-400-latin-ext.ttf");
            var lowerPriority = fonts.FontPriority.Single(font => font.Font == "Inter-400-vietnamese.ttf");

            Assert.True(preferred.RangeMatch(chr));
            Assert.True(lowerPriority.RangeMatch(chr));
            Assert.False(preferred.GetTypeFace(Bold).HasGlyph(chr[0]));
            Assert.True(lowerPriority.GetTypeFace(Bold).HasGlyph(chr[0]));

            var text = MixedScripts ? chr + "با" : chr;
            var runs = TextSetup.ClassifyText(text, Bold);

            Assert.Equal(0..chr.Length, runs[0].Range);
            Assert.Same(lowerPriority.GetTypeFace(Bold), runs[0].TypeFace);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void MissingGlyphsUseTheFirstCandidateOfTheirDirectionSegment(bool Bold)
        {
            var fonts = FontFallbackList.Shared;
            var expected = fonts.FontPriority.Single(font => font.Font == "NotoSansArabic-Regular.ttf").GetTypeFace(Bold);
            using var workspace = new MeasurementWorkspace();
            // The preceding Latin and separator do not supply the first candidate for this RTL segment.
            var context = new UScript.ScriptContext();
            context.Advance("TETYYS: ");
            var classified = fonts.Classify("\u064E\u0644\u3099", Bold, context, workspace.Shaping);
            Assert.All(classified.Runs.ToArray(), run => Assert.Same(expected, run.TypeFace));
            if (!Bold)
                Assert.Equal(18.140625f, new TextWidths(classified, workspace.Shaping, workspace.LineBreaking.Widths).Measure(classified.Text.Length));
        }

        [Fact]
        public void ReusingShapingScratchDoesNotCarryAnotherSegmentsFirstCandidate()
        {
            var fonts = FontFallbackList.Shared;
            using var workspace = new MeasurementWorkspace();
            var noto = fonts.FontPriority.Single(font => font.Font == "NotoSansArabic-Regular.ttf").TypeFace;
            var roobert = fonts.FullUnicodeFontPriority[0].TypeFace;
            foreach (var text in new[] { "\u0644\u3099", "\U000EFFFD", "\u0644\u3099" }) {
                var classified = fonts.ClassifyAlone(text, workspace);
                Assert.All(classified.Runs.ToArray(), run => Assert.Same(text.Length == 2 && char.IsHighSurrogate(text[0]) ? roobert : noto, run.TypeFace));
            }
        }

        [Theory]
        [InlineData(false, "")]
        [InlineData(true, "")]
        [InlineData(false, "abc")]
        [InlineData(true, "abc")]
        [InlineData(false, " more Latin text")]
        [InlineData(true, " more Latin text")]
        [InlineData(false, "با")]
        [InlineData(true, "با")]
        public void CombiningGraphemeJoinerKeepsBasesFont(bool Bold, string Suffix)
        {
            const string latin = "hahahaha \u034F";
            var text = latin + Suffix;
            var mixedScripts = Suffix == "با";
            var expected = FontFallbackList.Shared.FontPriority.Single(font => font.Font == "Inter-400-latin.ttf").GetTypeFace(Bold);
            var runs = TextSetup.ClassifyText(text, Bold);

            Assert.Equal(0..(mixedScripts ? latin.Length : text.Length), runs[0].Range);
            Assert.Same(expected, runs[0].TypeFace);
            Assert.Equal(mixedScripts ? 2 : 1, runs.Count);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void ArabicUsesDownloadedRegularAndBoldFonts(bool Bold)
        {
            var noto = FontFallbackList.Shared.FontPriority.Single(font => font.Font.StartsWith("NotoSansArabic-", StringComparison.Ordinal));
            Assert.Same(noto.GetTypeFace(Bold), Assert.Single(TextSetup.ClassifyText("با", Bold)).TypeFace);
        }
    }
}
