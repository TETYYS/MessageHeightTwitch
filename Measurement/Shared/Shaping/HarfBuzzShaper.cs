using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

using Icu;

namespace MessageHeightTwitch.Measurement.Shared.Shaping;

/// <summary>
/// Font-shaping helper for stage 3 (paragraph shaping), reused by stage 4 (line fitting) for unsafe line edges.
/// Segments and shapes text through CSS and platform fallback, using explicit font inputs and scoped scratch.
/// </summary>
internal readonly ref struct HarfBuzzShaper(ReadOnlySpan<char> Text)
{
    private readonly ReadOnlySpan<char> Text = Text;

    /// <summary>
    /// Shapes script, presentation and direction segments through Blink's candidate/reshape queue. A hint reads
    /// only the cluster ranges still unresolved by earlier fonts. Script runs continue from
    /// <paramref name="Context"/>, which stays as it is. Missing glyphs use the segment's first candidate.
    /// Supplied <paramref name="Bidi"/> runs are projected from the paragraph; standalone probes resolve
    /// their own text.
    /// </summary>
    internal ClassifiedText Shape(Font Font, UScript.ScriptContext Context, ShapingWorkspace Workspace, ReadOnlySpan<(int End, HarfBuzzSharp.Direction Direction)> Bidi = default)
    {
        Workspace.ProbeScript.CopyFrom(Context);
        EmojiPresentation.FillRuns(Workspace.ProbeScript.Segment(Text, Workspace.Scripts, Workspace.Scratch), Workspace.Presentations);
        if (Bidi.IsEmpty)
            Workspace.BidiResolver.GetBidi(Text, Workspace.Bidi);
        else {
            Workspace.Bidi.Clear();
            foreach (var run in Bidi)
                Workspace.Bidi.Add(run);
        }
        return ClassifyRuns(Font, Workspace);
    }

    /// <summary>
    /// Falls back over one segment of a text item, as Blink's HarfBuzzShaper does for a range that a line edge
    /// reshapes (LineBreaker::ShapeText): a fallback iterator of its own, hinted by this text only, in the item's
    /// <paramref name="Script"/> and <paramref name="Direction"/>.
    /// </summary>
    internal ClassifiedText ShapeSegment(Font Font, UScriptCode Script, HarfBuzzSharp.Direction Direction, ShapingWorkspace Workspace)
    {
        Workspace.Scratch.Ensure(Text.Length);
        var runes = 0;
        foreach (var rune in Text.EnumerateRunes())
            Workspace.Scratch.Runes[runes++] = rune;
        EmojiPresentation.FillRuns(Workspace.Scratch.Runes.AsSpan(0, runes), Workspace.Presentations);
        Workspace.Scripts.Clear();
        Workspace.Scripts.Add((0..Text.Length, Script));
        Workspace.Bidi.Clear();
        Workspace.Bidi.Add((Text.Length, Direction));
        return ClassifyRuns(Font, Workspace);
    }

    /// <summary>The font runs of a text whose script, presentation and bidi runs are in the workspace.</summary>
    private ClassifiedText ClassifyRuns(Font Font, ShapingWorkspace Workspace)
    {
        var Bold = Font.Bold;
        var Fonts = Font.FallbackList;
        Workspace.Runs.Clear();
        if (Text.IsEmpty) {
            return new(Text, CollectionsMarshal.AsSpan(Workspace.Runs), CollectionsMarshal.AsSpan(Workspace.Bidi)) {
                Font = Font
            };
        }

        // Inter covers printable ASCII. This shortcut retains the fixed-font contract of AsciiFastPath.
        var ascii = Workspace.Scripts.Count == 1 && Workspace.Presentations.Count == 1;
        foreach (var ch in Text)
            ascii &= ch is >= ' ' and <= '~';

        if (ascii) {
            Workspace.Runs.Add(new(0..Text.Length, (Bold ? Fonts.AsciiBold : Fonts.AsciiRegular).Typeface, Workspace.Scripts[0].Script));
        } else {
            for (int start = 0, script = 0, presentation = 0, bidi = 0; start < Text.Length;) {
                var end = Math.Min(Workspace.Scripts[script].Range.End.Value, Math.Min(Workspace.Presentations[presentation].Range.End.Value, Workspace.Bidi[bidi].End));
                ShapeSegment(Text, start..end, Workspace.Scripts[script].Script, Workspace.Presentations[presentation].Priority, Workspace.Bidi[bidi].Direction, Bold, Font.Italic, Workspace, Fonts);

                if (end == Workspace.Scripts[script].Range.End.Value)
                    script++;
                if (end == Workspace.Presentations[presentation].Range.End.Value)
                    presentation++;
                if (end == Workspace.Bidi[bidi].End)
                    bidi++;

                start = end;
            }
            Workspace.Runs.Sort(static (a, b) => a.Range.Start.Value.CompareTo(b.Range.Start.Value));
        }
        float? shapedWidth = null;

        if (!ascii && Workspace.Runs.Count == 1 && Workspace.Scripts.Count == 1 && Workspace.Presentations.Count == 1 && Workspace.Bidi.Count == 1 &&
            FontCache.GetShaper(Workspace.Runs[0].TypeFace).IsUnicodeRangeSubset && Workspace.Runs[0].ShapingRange is { } source && Workspace.Runs[0].Range.Equals(source) &&
            Workspace.LastShapedFont == Workspace.Runs[0].TypeFace && Workspace.LastShapedRange.Equals(source))
            shapedWidth = HarfBuzz.FixedToFloat(TextWidths.SnapFixed(FontCache.GetShaper(Workspace.Runs[0].TypeFace).ShapedFixedWidth(Workspace.Buffer)));

        return new(Text, CollectionsMarshal.AsSpan(Workspace.Runs), CollectionsMarshal.AsSpan(Workspace.Bidi), shapedWidth) { Font = Font };
    }

    /// <summary>
    /// Blink's ShapeSegment/ExtractShapeResults: shape each unresolved range with one candidate, commit its
    /// successful HarfBuzz clusters, and send only missing cluster ranges to the next font. Successful runs
    /// retain the candidate shaping range so their original advances can be replayed without guessing context.
    /// </summary>
    private void ShapeSegment(ReadOnlySpan<char> Text, Range Segment, UScriptCode Script, EmojiPresentation.PRIORITY Presentation, HarfBuzzSharp.Direction Direction, bool Bold, bool Italic, ShapingWorkspace Workspace, FontFallbackList Fonts)
    {
        var scratch = Workspace;
        scratch.Pending.Clear();
        scratch.Pending.Add(Segment);
        scratch.Tried.Clear();

        foreach (var candidate in Fonts.FontPriority) {
            var contributes = false;
            foreach (var range in scratch.Pending)
                contributes |= candidate.RangeMatch(Text[range]);
            if (contributes)
                TryCandidate(Text, candidate.GetTypeFace(Bold, Italic), Script, Direction, Workspace, CheckVariationSelectors: true);
            if (scratch.Pending.Count == 0)
                return;
        }
        foreach (var candidate in Fonts.FullUnicodeFontPriority) {
            TryCandidate(Text, candidate.GetTypeFace(Bold, Italic), Script, Direction, Workspace, CheckVariationSelectors: true);
            if (scratch.Pending.Count == 0)
                return;
        }

        while (scratch.Pending.Count != 0) {
            var hint = FallbackHint(Text, scratch.Pending);
            var candidate = Fonts.Cache.FallbackFontForCharacter(hint, Bold, Presentation, Italic);
#if TEST
            if (scratch.FallbackAttempts is { } attempts) {
                var pending = new System.Text.StringBuilder();
                foreach (var range in scratch.Pending)
                    pending.Append(Text[range]);
                attempts.Add(new(hint.Value, candidate?.FamilyName, candidate != null && scratch.Tried.Contains(candidate),
                    Segment.Start.Value, Segment.End.Value, pending.ToString()));
            }
#endif
            if (candidate == null || scratch.Tried.Contains(candidate))
                break;
            TryCandidate(Text, candidate, Script, Direction, Workspace, CheckVariationSelectors: false);
        }

        // FontFallbackIterator returns its first candidate once more at kNotDef. Its lifetime is this
        // script/presentation/direction segment, including when a line edge starts a fresh fallback queue.
        TryCandidate(Text, scratch.Tried[0], Script, Direction, Workspace, CheckVariationSelectors: false, IsLastFont: true);
    }

    private static void TryCandidate(ReadOnlySpan<char> Text, Typeface Candidate, UScriptCode Script, HarfBuzzSharp.Direction Direction, ShapingWorkspace Workspace, bool CheckVariationSelectors, bool IsLastFont = false)
    {
        var scratch = Workspace;
        scratch.Tried.Add(Candidate);
        scratch.Next.Clear();
        var shaper = FontCache.GetShaper(Candidate);

        foreach (var range in scratch.Pending) {
            var nextStart = scratch.Next.Count;
            shaper.ShapeRange(Text, range, Workspace.Buffer, Direction, default, Script);

            scratch.LastShapedFont = Candidate;
            scratch.LastShapedRange = range;

            var infos = Workspace.Buffer.GetGlyphInfoSpan();
            var rtl = Direction == HarfBuzzSharp.Direction.RightToLeft;
            var step = rtl ? -1 : 1;
            var glyph = rtl ? infos.Length - 1 : 0;
            var sliceStart = range.Start.Value;
            bool? sliceSupported = null;

            static void commitSlice(Range Slice, bool Supported, Range ShapingRange, Typeface Candidate, UScriptCode Script, ShapingWorkspace Workspace)
            {
                if (Supported)
                    Workspace.Runs.Add(new(Slice, Candidate, Script) {
                        ShapingRange = ShapingRange
                    });
                else
                    Workspace.Next.Add(Slice);
            }

            while (glyph >= 0 && glyph < infos.Length) {
                var start = (int)infos[glyph].Cluster;
                var supported = true;

                do {
                    supported &= IsLastFont || infos[glyph].Codepoint != 0 && !shaper.IsSyntheticIdeographicSpace(Text[start], infos[glyph].Codepoint);
                    glyph += step;
                } while (glyph >= 0 && glyph < infos.Length && infos[glyph].Cluster == start);

                var end = glyph >= 0 && glyph < infos.Length ? (int)infos[glyph].Cluster : range.End.Value;

                if (CheckVariationSelectors)
                    supported &= shaper.SupportsVariationSelectors(Text[start..end]);

                if (sliceSupported != null && sliceSupported != supported) {
                    commitSlice(sliceStart..start, sliceSupported.Value, range, Candidate, Script, Workspace);
                    sliceStart = start;
                }

                sliceSupported = supported;
            }

            if (sliceSupported != null)
                commitSlice(sliceStart..range.End.Value, sliceSupported.Value, range, Candidate, Script, Workspace);
            else
                Workspace.Runs.Add(new(range, Candidate, Script) { ShapingRange = range });

            // Blink extracts in HarfBuzz's visual glyph order. Keep the unresolved RTL slices in that
            // order too, because the next system fallback hint comes from the front of this queue.
            if (rtl)
                scratch.Next.Reverse(nextStart, scratch.Next.Count - nextStart);
        }

        scratch.Pending.Clear();
        scratch.Pending.AddRange(scratch.Next);
    }

    /// <summary>
    /// CollectFallbackHintChars and ChooseHintIndex, over the queue's actual unresolved ranges.
    /// </summary>
    private static Rune FallbackHint(ReadOnlySpan<char> Text, List<Range> Pending)
    {
        Rune? first = null, preferred = null;

        foreach (var range in Pending) {
            foreach (var rune in Text[range].EnumerateRunes()) {
                if (first == null)
                    first = rune;
                else if (preferred == null && HasLikelyScript(rune))
                    preferred = rune;

                var primary = (UScriptCode)Character.GetIntPropertyValue(rune.Value, Character.UProperty.SCRIPT);
                if (primary is not (UScriptCode.USCRIPT_COMMON or UScriptCode.USCRIPT_INHERITED))
                    return preferred ?? first.Value;
            }
        }

        return preferred ?? first ?? default;
    }

    /// <summary>
    /// Blink's Character::HasLikelyScript. Unlike UScript.GetScript, full-width ASCII is not Han here,
    /// and Unknown (private use, unassigned) counts as a likely script.
    /// </summary>
    private static bool HasLikelyScript(Rune Rune)
    {
        var script = (UScriptCode)Character.GetIntPropertyValue(Rune.Value, Character.UProperty.SCRIPT);

        // UScript.GetScript applies Blink's GetScriptBasedOnUnicodeBlock, plus a full-width override.
        if (script is (UScriptCode.USCRIPT_COMMON or UScriptCode.USCRIPT_INHERITED) && Rune.Value is not (> 0xFF00 and < 0xFF5F))
            script = UScript.GetScript(Rune);

        return script is not (UScriptCode.USCRIPT_COMMON or UScriptCode.USCRIPT_INHERITED);
    }

}
