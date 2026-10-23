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
    internal ClassifiedText Shape(Font Font, UScript.ScriptContext Context, ShapingWorkspace Ws, ReadOnlySpan<(int End, HarfBuzzSharp.Direction Direction)> Bidi = default)
    {
        Ws.ProbeScript.CopyFrom(Context);
        EmojiPresentation.FillRuns(Ws.ProbeScript.Segment(Text, Ws.Scripts, Ws.Scratch), Ws.Presentations);
        if (Bidi.IsEmpty)
            Ws.BidiResolver.GetBidi(Text, Ws.Bidi);
        else {
            Ws.Bidi.Clear();
            foreach (var run in Bidi)
                Ws.Bidi.Add(run);
        }
        return ClassifyRuns(Font, Ws);
    }

    /// <summary>
    /// Falls back over one segment of a text item, as Blink's HarfBuzzShaper does for a range that a line edge
    /// reshapes (LineBreaker::ShapeText): a fallback iterator of its own, hinted by this text only, in the item's
    /// <paramref name="Script"/> and <paramref name="Direction"/>.
    /// </summary>
    internal ClassifiedText ShapeSegment(Font Font, UScriptCode Script, HarfBuzzSharp.Direction Direction, ShapingWorkspace Ws)
    {
        Ws.Scratch.Ensure(Text.Length);
        var runes = 0;
        foreach (var rune in Text.EnumerateRunes())
            Ws.Scratch.Runes[runes++] = rune;
        EmojiPresentation.FillRuns(Ws.Scratch.Runes.AsSpan(0, runes), Ws.Presentations);
        Ws.Scripts.Clear();
        Ws.Scripts.Add((0..Text.Length, Script));
        Ws.Bidi.Clear();
        Ws.Bidi.Add((Text.Length, Direction));
        return ClassifyRuns(Font, Ws);
    }

    /// <summary>The font runs of a text whose script, presentation and bidi runs are in the workspace.</summary>
    private ClassifiedText ClassifyRuns(Font Font, ShapingWorkspace Ws)
    {
        var Bold = Font.Bold;
        var Fonts = Font.FallbackList;
        Ws.Runs.Clear();
        Ws.ShapeLength = 0;
        PooledBuffers.Ensure(ref Ws.ShapePositions, 2 * Text.Length + 1);
        PooledBuffers.Ensure(ref Ws.ShapeMarks, 2 * Text.Length + 1);
        if (Text.IsEmpty) {
            return new(Text, CollectionsMarshal.AsSpan(Ws.Runs), CollectionsMarshal.AsSpan(Ws.Bidi)) {
                Font = Font
            };
        }

        // Inter covers printable ASCII. This shortcut retains the fixed-font contract of AsciiFastPath.
        var ascii = Ws.Scripts.Count == 1 && Ws.Presentations.Count == 1;
        foreach (var ch in Text)
            ascii &= ch is >= ' ' and <= '~';

        if (ascii) {
            Ws.Runs.Add(new(0..Text.Length, (Bold ? Fonts.AsciiBold : Fonts.AsciiRegular).Typeface, Ws.Scripts[0].Script));
        } else {
            for (int start = 0, script = 0, presentation = 0, bidi = 0; start < Text.Length;) {
                var end = Math.Min(Ws.Scripts[script].Range.End.Value, Math.Min(Ws.Presentations[presentation].Range.End.Value, Ws.Bidi[bidi].End));
                ShapeSegment(Text, start..end, Ws.Scripts[script].Script, Ws.Presentations[presentation].Priority, Ws.Bidi[bidi].Direction, Bold, Font.Italic, Ws, Fonts);

                if (end == Ws.Scripts[script].Range.End.Value)
                    script++;
                if (end == Ws.Presentations[presentation].Range.End.Value)
                    presentation++;
                if (end == Ws.Bidi[bidi].End)
                    bidi++;

                start = end;
            }
            Ws.Runs.Sort(static (a, b) => a.Range.Start.Value.CompareTo(b.Range.Start.Value));
        }
        float? shapedWidth = null;

        if (!ascii && Ws.Runs.Count == 1 && Ws.Scripts.Count == 1 && Ws.Presentations.Count == 1 && Ws.Bidi.Count == 1 &&
            FontCache.GetShaper(Ws.Runs[0].TypeFace).IsUnicodeRangeSubset && Ws.Runs[0].ShapingRange is { } source && Ws.Runs[0].Range.Equals(source) &&
            Ws.LastShapedFont == Ws.Runs[0].TypeFace && Ws.LastShapedRange.Equals(source))
            shapedWidth = HarfBuzz.FixedToFloat(TextWidths.SnapFixed(FontCache.GetShaper(Ws.Runs[0].TypeFace).ShapedFixedWidth(Ws.Buffer)));

        return new(Text, CollectionsMarshal.AsSpan(Ws.Runs), CollectionsMarshal.AsSpan(Ws.Bidi), shapedWidth) {
            Font = Font,
            ShapePositions = Ws.ShapePositions.AsSpan(0, Ws.ShapeLength),
            ShapeMarks = Ws.ShapeMarks.AsSpan(0, Ws.ShapeLength)
        };
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

    private static void TryCandidate(ReadOnlySpan<char> Text, Typeface Candidate, UScriptCode Script, HarfBuzzSharp.Direction Direction, ShapingWorkspace Ws, bool CheckVariationSelectors, bool IsLastFont = false)
    {
        var scratch = Ws;
        scratch.Tried.Add(Candidate);
        scratch.Next.Clear();
        var shaper = FontCache.GetShaper(Candidate);

        foreach (var range in scratch.Pending) {
            var nextStart = scratch.Next.Count;
            shaper.ShapeRange(Text, range, Ws.Buffer, Direction, default, Script);

            scratch.LastShapedFont = Candidate;
            scratch.LastShapedRange = range;

            var infos = Ws.Buffer.GetGlyphInfoSpan();
            var rtl = Direction == HarfBuzzSharp.Direction.RightToLeft;
            var step = rtl ? -1 : 1;
            var glyph = rtl ? infos.Length - 1 : 0;
            var sliceStart = range.Start.Value;
            bool? sliceSupported = null;

            // A committed slice keeps its positions from this shape, which the buffer holds until the next range.
            static void commitSlice(ReadOnlySpan<char> Text, Range Slice, bool Supported, Range ShapingRange, Typeface Candidate, HarfBuzz Shaper, UScriptCode Script, HarfBuzzSharp.Direction Direction, ShapingWorkspace Ws)
            {
                if (!Supported) {
                    Ws.Next.Add(Slice);
                    return;
                }
                var length = Slice.End.Value - Slice.Start.Value;
                var cached = Ws.ShapeLength;
                Shaper.ReadPositions(Text, ShapingRange, Ws.Buffer, Direction, Ws.ShapePositions.AsSpan(cached, length + 1), Ws.ShapeMarks.AsSpan(cached, length), Slice: Slice);
                Ws.ShapeLength += length + 1;
                Ws.Runs.Add(new(Slice, Candidate, Script) {
                    ShapingRange = ShapingRange,
                    CachedShape = cached
                });
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
                    commitSlice(Text, sliceStart..start, sliceSupported.Value, range, Candidate, shaper, Script, Direction, Ws);
                    sliceStart = start;
                }

                sliceSupported = supported;
            }

            // A range without glyphs is committed whole.
            commitSlice(Text, sliceStart..range.End.Value, sliceSupported ?? true, range, Candidate, shaper, Script, Direction, Ws);

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
