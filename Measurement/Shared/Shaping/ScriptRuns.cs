using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;
using Icu;

namespace MessageHeightTwitch.Measurement.Shared.Shaping;

public static partial class UScript
{
    /// <summary>
    /// OpenType script resolution, which uses ICU Script_Extensions. Font fallback's block-based
    /// GetScript is deliberately separate from this.
    /// </summary>
    private static UScriptCode NormalizeScript(UScriptCode Script) => Script is UScriptCode.USCRIPT_KATAKANA or UScriptCode.USCRIPT_KATAKANA_OR_HIRAGANA ? UScriptCode.USCRIPT_HIRAGANA : Script;

    [InlineArray(256)]
    internal struct ScriptValues
    {
        private UScriptCode First;
    }

    internal struct ScriptSetValue
    {
        internal ScriptValues Values;
        internal int Count;
        internal readonly bool Contains(UScriptCode Value)
        {
            for (var x = 0; x < Count; x++) if (Values[x] == Value) return true;
            return false;
        }
    }

    /// <summary>
    /// Rented rune and Script_Extensions scratch for script segmentation.
    /// </summary>
    /// <remarks>
    /// Cache usage: 4 bytes per rune plus 1,028 bytes per script set (256 script codes and a count), excluding
    /// array headers. The initial 1,024-entry arrays use about 1 MiB; ShapingWorkspace reserves 1,080 entries,
    /// usually rounded to 2,048 by the shared pools (about 2 MiB). Dispose returns both arrays to those pools.
    /// </remarks>
    internal sealed class ScriptScratch : IDisposable
    {
        internal Rune[] Runes = ArrayPool<Rune>.Shared.Rent(1024);
        internal ScriptSetValue[] Sets = ArrayPool<ScriptSetValue>.Shared.Rent(1024);
        internal void Ensure(int Length)
        {
            PooledBuffers.Ensure(ref Runes, Length);
            PooledBuffers.Ensure(ref Sets, Length);
        }
        public void Dispose()
        {
            ArrayPool<Rune>.Shared.Return(Runes);
            ArrayPool<ScriptSetValue>.Shared.Return(Sets);
        }
    }

    /*
     * FillScriptSet's result for each BMP character, once computed: the count in the low 2 bits, then up to two
     * 10-bit script codes. 0 until computed, and for sets that do not fit. Every thread writes the same value.
     */
    private static readonly int[] ScriptSetCache = new int[0x10000];
    private const int CachedScriptBits = 10;
    private const int CachedScriptMask = (1 << CachedScriptBits) - 1;

    internal static void FillScriptSet(Rune Rune, ref ScriptSetValue Set)
    {
        if (!Rune.IsBmp) {
            ComputeScriptSet(Rune, ref Set);
            return;
        }
        var cached = ScriptSetCache[Rune.Value];
        if (cached != 0) {
            Set.Count = cached & 3;
            Set.Values[0] = (UScriptCode)((cached >> 2) & CachedScriptMask);
            Set.Values[1] = (UScriptCode)((cached >> (2 + CachedScriptBits)) & CachedScriptMask);
            return;
        }

        ComputeScriptSet(Rune, ref Set);
        if (Set.Count is 1 or 2 && (uint)Set.Values[0] <= CachedScriptMask && (Set.Count == 1 || (uint)Set.Values[1] <= CachedScriptMask))
            ScriptSetCache[Rune.Value] = Set.Count | (int)Set.Values[0] << 2 | (Set.Count == 2 ? (int)Set.Values[1] << (2 + CachedScriptBits) : 0);
    }

    internal static void ComputeScriptSet(Rune Rune, ref ScriptSetValue Set)
    {
        var primary = NormalizeScript((UScriptCode)Character.GetIntPropertyValue(Rune.Value, Character.UProperty.SCRIPT));
        if (primary == UScriptCode.USCRIPT_HIRAGANA) {
            Set.Count = 1;
            Set.Values[0] = primary;
            return;
        }

        Set.Count = ICUExtensions.Get(Rune.Value, Set.Values);
        if (Set.Values[0] == primary)
            return;

        if (primary > UScriptCode.USCRIPT_INHERITED) {
            // A character's own script is one of its Script_Extensions, second when not first (Unicode 17).
            (Set.Values[0], Set.Values[1]) = (Set.Values[1], Set.Values[0]);
            return;
        }

        if (primary == UScriptCode.USCRIPT_COMMON && Set.Count == 1) {
            Set.Values[1] = Set.Values[0];
            Set.Values[0] = primary;
            Set.Count = 2;
            return;
        }

        var preferred = 0;
        if (primary == UScriptCode.USCRIPT_INHERITED) {
            Set.Values[Set.Count++] = Set.Values[0];
            Set.Values[0] = primary;
            preferred = 1;
        }

        for (var x = preferred + 1; x < Set.Count; x++)
            if (Set.Values[preferred] == UScriptCode.USCRIPT_LATIN || Set.Values[x] < Set.Values[preferred])
                (Set.Values[preferred], Set.Values[x]) = (Set.Values[x], Set.Values[preferred]);
    }

    // Port of Blink's ScriptRunIterator. The UTF-16 bitmap shortcut handles bracket-free single-script text
    /// <summary>
    /// Script-run context, including paired brackets and a shared shortcut for single-script text.
    /// </summary>
    /// <remarks>
    /// Cache usage: the process shares one lazily built 6,912-byte bitmap per encountered script, without
    /// eviction. Each context keeps at most 32 bracket entries (256 bytes of payload when fully reserved),
    /// excluding list/object overhead; resetting the context retains that capacity.
    /// </remarks>
    internal sealed class ScriptContext(UScriptCode Script = UScriptCode.USCRIPT_COMMON)
    {
        private const int FirstSurrogate = 0xD800;
        // Built once per script on first use; 6,912 bytes of bitmap storage per script.
        private static readonly ConcurrentDictionary<UScriptCode, Lazy<ulong[]>> SingleScriptBits = new();

        private static ulong[] BuildSingleScriptBits(UScriptCode Script)
        {
            var bits = new ulong[FirstSurrogate / 64];
            ScriptSetValue set = default;
            for (var c = 0; c < FirstSurrogate; c++) {
                FillScriptSet(new Rune(c), ref set);
                // An inherited character with extensions can constrain its preceding Common base.
                // Leave it to the general path even when this script is one of those extensions.
                if (set.Values[0] == UScriptCode.USCRIPT_INHERITED && set.Count > 1)
                    continue;
                if (set.Values[0] > UScriptCode.USCRIPT_INHERITED && !set.Contains(Script))
                    continue;
                if (Character.GetIntPropertyValue(c, Character.UProperty.BIDI_PAIRED_BRACKET_TYPE) != 0)
                    continue;
                bits[c >> 6] |= 1UL << (c & 63);
            }
            return bits;
        }

        [SkipLocalsInit]
        private bool TrySegmentSingleScript(ReadOnlySpan<char> Text, Rune[] Characters)
        {
            var script = Script;
            if (script <= UScriptCode.USCRIPT_INHERITED) {
                if (Text[0] >= FirstSurrogate)
                    return false;
                Unsafe.SkipInit(out ScriptSetValue first);
                FillScriptSet(new Rune(Text[0]), ref first);
                // A Common prefix may update CommonPreferred before the first real script arrives.
                if (first.Count != 1 || first.Values[0] <= UScriptCode.USCRIPT_INHERITED)
                    return false;
                script = first.Values[0];
            }
            var bits = SingleScriptBits.GetOrAdd(script, static value => new(() => BuildSingleScriptBits(value))).Value;
            for (var x = 0; x < Text.Length; x++) {
                var c = Text[x];
                if (c >= FirstSurrogate || (bits[c >> 6] & (1UL << (c & 63))) == 0)
                    return false;
                Characters[x] = new Rune(c);
            }
            Script = script;
            ResolvePendingBrackets(script, false);
            return true;
        }

        private const int MaxBrackets = 32;
        internal UScriptCode Script { get; private set; } = NormalizeScript(Script);
        /*
         * Unclosed opening brackets, each with the script of its run; the closing bracket takes that script. Runs
         * resolve their brackets only to real scripts, so the brackets still Common are the newest ones, whose run
         * has not resolved yet.
         */
        private List<(int Opening, UScriptCode Script)> Brackets = [];
        private UScriptCode CommonPreferred;

        internal void ReserveBrackets() => Brackets.EnsureCapacity(MaxBrackets);

        internal void Reset()
        {
            Script = UScriptCode.USCRIPT_COMMON;
            Brackets.Clear();
            CommonPreferred = UScriptCode.USCRIPT_COMMON;
        }

        internal void CopyFrom(ScriptContext Other)
        {
            Script = Other.Script;
            Brackets.Clear();
            Brackets.AddRange(Other.Brackets);
            CommonPreferred = Other.CommonPreferred;
        }

        /// <summary>
        /// Advances over text of a chat message known to contain printable ASCII from end to end.
        /// Brackets cannot inherit a non-Latin script in such a message.
        /// </summary>
        internal void AdvanceAsciiMessage(ReadOnlySpan<char> Text)
        {
            foreach (var c in Text) {
                if (c is >= 'A' and <= 'Z' or >= 'a' and <= 'z') {
                    Script = UScriptCode.USCRIPT_LATIN;
                    return;
                }
            }
        }

        /// <summary>
        /// Whether Segment would return one run for printable ASCII text in this context. ASCII has
        /// only Latin and Common characters. A closing bracket can take a paired bracket's script, and
        /// a Common prefix inherits a non-Latin context until the first letter starts a Latin run.
        /// </summary>
        internal bool SegmentsAsciiAsOneRun(ReadOnlySpan<char> Text)
        {
            // Generic span searches box char before tiered JIT promotion.
            bool closing = false, letter = false;
            foreach (var c in Text) {
                closing |= c is ')' or ']' or '}';
                letter |= char.IsAsciiLetter(c);
            }

            var latin = Script is <= UScriptCode.USCRIPT_INHERITED or UScriptCode.USCRIPT_LATIN;
            if (closing) {
                if (!latin)
                    return false;
                foreach (var bracket in Brackets)
                    if (bracket.Script is not (UScriptCode.USCRIPT_COMMON or UScriptCode.USCRIPT_LATIN))
                        return false;
            }
            return latin || !letter || char.IsAsciiLetter(Text[0]);
        }

        private static bool IsHan(UScriptCode Script) => Script is UScriptCode.USCRIPT_HAN or UScriptCode.USCRIPT_HIRAGANA or UScriptCode.USCRIPT_BOPOMOFO;

        /// <summary>
        /// Gives the pending brackets, the newest ones still Common, the script their run resolved to (Chromium's
        /// FixupStack). When the opening bracket just pushed is what started the next run, it belongs to that run and
        /// stays pending.
        /// </summary>
        private void ResolvePendingBrackets(UScriptCode Resolved, bool LastStartsNextRun)
        {
            var end = Brackets.Count - (LastStartsNextRun ? 1 : 0);
            for (var x = end - 1; x >= 0 && Brackets[x].Script == UScriptCode.USCRIPT_COMMON; x--)
                Brackets[x] = (Brackets[x].Opening, Resolved);
        }

        /// <summary>
        /// Pushes an opening bracket, pending until its run resolves; a full stack drops its oldest bracket. A Common
        /// East Asian bracket takes the scripts of U+300C, so that it can stay in a Han run.
        /// </summary>
        private void OpenBracket(Rune Rune, ref ScriptSetValue Set)
        {
            if (Brackets.Count == MaxBrackets)
                Brackets.RemoveAt(0);

            if (Set.Count == 1 && Set.Values[0] == UScriptCode.USCRIPT_COMMON) {
                var width = (Character.UEastAsianWidth)Character.GetIntPropertyValue(Rune.Value, Character.UProperty.EAST_ASIAN_WIDTH);
                if (width is Character.UEastAsianWidth.HALFWIDTH or Character.UEastAsianWidth.FULLWIDTH or Character.UEastAsianWidth.WIDE)
                    Set.Count = ICUExtensions.Get(0x300C, Set.Values);
            }

            Brackets.Add((Rune.Value, UScriptCode.USCRIPT_COMMON));
        }

        /// <summary>
        /// Gives a closing bracket the resolved script of the newest matching opening bracket, or the current run's
        /// first Han script in place of a Han one, and pops the brackets opened after the match. The match itself
        /// stays, as in Chromium.
        /// </summary>
        private void CloseBracket(Rune Rune, ref ScriptSetValue Set, in ScriptSetValue Current)
        {
            var target = ICUExtensions.GetPairedBracket(Rune.Value);
            for (var b = Brackets.Count - 1; b >= 0; b--) {
                if (Brackets[b].Opening != target)
                    continue;

                var pairedScript = Brackets[b].Script;
                if (IsHan(pairedScript)) {
                    for (var n = 0; n < Current.Count; n++)
                        if (IsHan(Current.Values[n])) {
                            pairedScript = Current.Values[n];
                            break;
                        }
                }
                if (pairedScript != UScriptCode.USCRIPT_COMMON) {
                    Set.Count = 1;
                    Set.Values[0] = pairedScript;
                }

                Brackets.RemoveRange(b + 1, Brackets.Count - b - 1);
                return;
            }
        }

        /// <summary>Sets are read only up to their counts, so their storage need not be zeroed.</summary>
        [SkipLocalsInit]
        private bool Merge(ref ScriptSetValue Current, in ScriptSetValue Next)
        {
            if (Next.Values[0] <= UScriptCode.USCRIPT_INHERITED) {
                if (Next.Count == 2 && Current.Values[0] <= UScriptCode.USCRIPT_INHERITED && CommonPreferred == UScriptCode.USCRIPT_COMMON)
                    CommonPreferred = Next.Values[1];
                return true;
            }

            if (Current.Values[0] <= UScriptCode.USCRIPT_INHERITED) {
                Current = Next;
                return true;
            }

            // Most characters have a single script: the intersection is that script or nothing.
            if (Current.Count == 1 && Next.Count == 1)
                return Current.Values[0] == Next.Values[0];

            Span<UScriptCode> intersection = stackalloc UScriptCode[256];
            var count = 0;
            for (var x = 0; x < Current.Count; x++)
                if (Next.Contains(Current.Values[x]))
                    intersection[count++] = Current.Values[x];

            if (count == 0)
                return false;

            var preferred = intersection[0];
            if (intersection[..count].Contains(Current.Values[0]))
                preferred = Current.Values[0];
            else if (intersection[..count].Contains(Next.Values[0]))
                preferred = Next.Values[0];

            Current.Count = 1;
            Current.Values[0] = preferred;

            for (var x = 0; x < count; x++)
                if (intersection[x] != preferred)
                    Current.Values[Current.Count++] = intersection[x];

            return true;
        }

        private UScriptCode Resolve(in ScriptSetValue Current) => Current.Values[0] == UScriptCode.USCRIPT_COMMON ? CommonPreferred : Current.Values[0];

        /// <summary>
        /// Segment for a caller that needs only the runs, with scratch of its own.
        /// </summary>
        internal void Segment(ReadOnlySpan<char> Text, List<(Range Range, UScriptCode Script)> Result)
        {
            using var scratch = new ScriptScratch();
            Segment(Text, Result, scratch);
        }

        /// <summary>
        /// Fills <paramref name="Result"/> with the script runs of a text that continues this context, and advances
        /// the context past it. Returns the text's runes, decoded into <paramref name="Scratch"/>.
        /// </summary>
        [SkipLocalsInit]
        internal ReadOnlySpan<Rune> Segment(ReadOnlySpan<char> Text, List<(Range Range, UScriptCode Script)> Result, ScriptScratch Scratch)
        {
            Result.Clear();
            if (Text.IsEmpty)
                return [];

            Scratch.Ensure(Text.Length);
            var characters = Scratch.Runes;
            var sets = Scratch.Sets;
            var length = 0;

            if (TrySegmentSingleScript(Text, characters)) {
                Result.Add((0..Text.Length, Script));
                return characters.AsSpan(0, Text.Length);
            }

            foreach (var rune in Text.EnumerateRunes()) {
                characters[length] = rune;
                FillScriptSet(rune, ref sets[length++]);
            }

            for (var x = 1; x < length; x++) {
                if (sets[x].Values[0] != UScriptCode.USCRIPT_INHERITED || sets[x].Count == 1)
                    continue;
                if (sets[x - 1].Values[0] == UScriptCode.USCRIPT_COMMON) {
                    sets[x - 1].Count = sets[x].Count - 1;
                    for (var n = 0; n < sets[x - 1].Count; n++)
                        sets[x - 1].Values[n] = sets[x].Values[n + 1];
                }
                sets[x].Count = 1;
                sets[x].Values[0] = UScriptCode.USCRIPT_INHERITED;
            }

            Unsafe.SkipInit(out ScriptSetValue current);
            current.Count = 1;
            current.Values[0] = Script;

            var start = 0;
            var offset = 0;
            for (var x = 0; x < length; x++) {
                var rune = characters[x];
                ref var next = ref sets[x];

                var type = (Character.UBidiPairedBracketType)Character.GetIntPropertyValue(rune.Value, Character.UProperty.BIDI_PAIRED_BRACKET_TYPE);
                if (type == Character.UBidiPairedBracketType.OPEN)
                    OpenBracket(rune, ref next);
                else if (type == Character.UBidiPairedBracketType.CLOSE)
                    CloseBracket(rune, ref next, current);

                if (!Merge(ref current, next)) {
                    var resolved = Resolve(current);
                    if (offset > start)
                        Result.Add((start..offset, resolved));
                    ResolvePendingBrackets(resolved, type == Character.UBidiPairedBracketType.OPEN);
                    current = next;
                    start = offset;
                }
                offset += rune.Utf16SequenceLength;
            }
            Script = Resolve(current);
            if (Script > UScriptCode.USCRIPT_INHERITED)
                ResolvePendingBrackets(Script, false);
            Result.Add((start..offset, Script));
            return characters.AsSpan(0, length);
        }
    }
}
