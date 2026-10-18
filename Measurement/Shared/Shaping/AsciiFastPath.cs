using System;
using System.Collections.Generic;

using HarfBuzzSharp;


using Buffer = HarfBuzzSharp.Buffer;

namespace MessageHeightTwitch.Measurement.Shared.Shaping;

/// <summary>
/// Fast path for measuring printable ASCII with the bundled Inter fonts without shaping it. For each font, it
/// precomputes HarfBuzz's advance of every character followed by each neighbour, which keeps kerning, and
/// every character's advance at the end of a run
/// </summary>
/// <remarks>
/// Cache usage: about 64.6 KiB of advance and login-trigger array payload per font, or 129 KiB for the
/// regular/bold pair shared by the process
///
/// Relies on the bundled Inter Latin subsets: each printable ASCII character shapes alone to its nominal glyph,
/// active substitutions use the GSUB forms handled by OpenType.SubstitutionRules, and GPOS only adjusts the
/// first glyph of a pair. Replacing or updating Inter, or changing shaping features, requires revalidating
/// these shortcuts or disabling them and their callers.
/// </remarks>
internal sealed class AsciiFastPath
{
    // Tables are indexed by character code
    private const int TABLE_SIZE = 128;
    private const char FIRST = ' ';
    private const char LAST = '~';

    // 16.16 fixed-point advances
    private const int UNKNOWN = int.MinValue;
    private readonly int[] Singles = new int[TABLE_SIZE];
    private readonly int[] Pairs = new int[TABLE_SIZE * TABLE_SIZE];

    private static int PairIndex(char A, char B) => A * TABLE_SIZE + B;

    /*
     * Contextual rules as character classes. A rule can only fire when every
     * class has a member in the text; triggers holds each rule's rarest class.
     * 
     * For example, login "123x123" has a different "x" glyph because Inter
     * substitutes x between digits.
     */
    private readonly UInt128[][] Rules;
    private readonly UInt128 Triggers;
    internal Typeface Typeface { get; }

    // Derive login triggers from the loaded Inter's rules; other fonts still need the assumptions above revalidated.
    private static readonly UInt128 LoginCharacters =
        ((UInt128.One << 26) - 1) << 'A' | ((UInt128.One << 26) - 1) << 'a' |
        ((UInt128.One << 10) - 1) << '0' | UInt128.One << '_';

    private readonly bool[] LoginContextualTriggers = new bool[TABLE_SIZE];

    // The most characters a contextual rule matches on either side of the one it substitutes.
    internal int ContextReach { get; }

    private AsciiFastPath(Typeface Typeface, HarfBuzz Shaper, uint[] Glyphs, Buffer Buffer, List<UInt128[]> Rules)
    {
        this.Typeface = Typeface;
        this.Rules = [.. Rules];
        foreach (var rule in this.Rules)
            ContextReach = Math.Max(ContextReach, rule.Length - 1);

        Triggers = FindRuleTriggers(this.Rules, UInt128.MaxValue);
        var loginTriggers = FindRuleTriggers(this.Rules, LoginCharacters);
        for (var c = FIRST;c <= LAST;c++)
            if ((loginTriggers & UInt128.One << c) != 0)
                LoginContextualTriggers[c] = true;

        Array.Fill(Pairs, UNKNOWN);

        // Every printable ASCII character of the bundled Inter shapes alone to its nominal glyph.
        Span<char> text = stackalloc char[2];
        for (var c = FIRST;c <= LAST;c++) {
            text[0] = c;
            Shaper.Shape(text[..1], Buffer);
            Singles[c] = Shaper.FixedAdvance(Buffer.GetGlyphPositionSpan()[0].XAdvance);
        }

        // Every pair of printable ASCII characters with the kern between them applied to the first glyph's advance.
        for (var a = FIRST;a <= LAST;a++) {
            for (var b = FIRST;b <= LAST;b++) {
                text[0] = a;
                text[1] = b;
                Shaper.Shape(text, Buffer);

                var info = Buffer.GetGlyphInfoSpan();
                var positions = Buffer.GetGlyphPositionSpan();

                /*
                 * A second-glyph advance adjustment cannot be composed using this table. Texts with
                 * a pair left Unknown here are rejected by the contextual rules before the table is read.
                 */
                if (info.Length == 2 && info[0].Codepoint == Glyphs[a] && info[1].Codepoint == Glyphs[b] && Shaper.FixedAdvance(positions[1].XAdvance) == Singles[b])
                    Pairs[PairIndex(a, b)] = Shaper.FixedAdvance(positions[0].XAdvance);
            }
        }
    }

    /// <summary>
    /// Selects one trigger class per rule, restricted to <paramref name="Alphabet"/>. If any class has no members
    /// in that alphabet, the rule cannot apply. A text containing none of the selected characters rules out every
    /// rule, letting Accepts skip the full checks with a single AND.
    /// </summary>
    private static UInt128 FindRuleTriggers(ReadOnlySpan<UInt128[]> Rules, UInt128 Alphabet)
    {
        UInt128 triggers = 0;
        foreach (var rule in Rules) {
            UInt128 rarest = 0;
            var leastWeight = int.MaxValue;
            foreach (var members in rule) {
                var allowed = members & Alphabet;
                if (allowed == 0) {
                    rarest = 0;
                    break;
                }

                // Fewer trigger characters let more texts skip the checks. Most words end with a space, so
                // prefer any class without one: its weight is below that of every class containing a space.
                var weight = (int)UInt128.PopCount(allowed) + ((allowed & UInt128.One << ' ') != 0 ? TABLE_SIZE : 0);
                if (weight < leastWeight) {
                    rarest = allowed;
                    leastWeight = weight;
                }
            }
            triggers |= rarest;
        }
        return triggers;
    }

    internal bool TryMeasure(ReadOnlySpan<char> Text, out float Width)
    {
        var result = TryMeasurePrefixFixed(Text, Text.Length, out var units);
        Width = HarfBuzz.FixedToFloat(units);
        return result;
    }

    /// <summary>
    /// Measures a Twitch login in one pass, keeping kerning without building a character set. Only a trigger of a
    /// rule that can match the login alphabet needs the full checks (Inter substitutes x between digits).
    /// The caller supplies a nonempty login containing only ASCII letters, digits and underscores.
    /// </summary>
    internal bool TryMeasureLogin(ReadOnlySpan<char> Text, out long Width) => TryMeasureLogin(Text, default, out Width);

    /// <summary>
    /// Original positions for a known login, using the same pair lookup and substitution checks
    /// </summary>
    internal bool TryFillLoginPrefixes(ReadOnlySpan<char> Text, Span<long> Prefixes) => TryMeasureLogin(Text, Prefixes, out _);

    private bool TryMeasureLogin(ReadOnlySpan<char> Text, Span<long> Prefixes, out long Width)
    {
        System.Diagnostics.Debug.Assert(!Text.IsEmpty);
        System.Diagnostics.Debug.Assert(Prefixes.IsEmpty || Prefixes.Length >= Text.Length + 1);
        Width = 0;

        var previous = Text[0];

        System.Diagnostics.Debug.Assert(char.IsAsciiLetterOrDigit(previous) || previous == '_');

        var needsContextCheck = LoginContextualTriggers[previous];
        if (!Prefixes.IsEmpty)
            Prefixes[0] = 0;
        var end = 1;
        foreach (var c in Text[1..]) {

            System.Diagnostics.Debug.Assert(char.IsAsciiLetterOrDigit(c) || c == '_');

            needsContextCheck |= LoginContextualTriggers[c];
            Width += Pairs[PairIndex(previous, c)];
            if (!Prefixes.IsEmpty)
                Prefixes[end] = Width;
            end++;
            previous = c;
        }
        if (needsContextCheck && !Accepts(Text))
            return false;
        Width += Singles[previous];
        if (!Prefixes.IsEmpty)
            Prefixes[Text.Length] = Width;
        return true;
    }

    /// <summary>
    /// The exact 16.16 advance of <paramref name="Text"/>[0..end): the glyphs of its characters,
    /// with the kern between the last one and <paramref name="Text"/>[end] applied to it as Blink's
    /// positions do. The following character is checked for the same rules as the prefix.
    /// </summary>
    internal bool TryMeasurePrefixFixed(ReadOnlySpan<char> Text, int End, out long Width)
    {
        Width = 0;
        if (End == 0)
            return true;

        if (!Accepts(Text[..Math.Min(End + 1, Text.Length)]))
            return false;

        Width = PrefixFixed(Text, End);
        return true;
    }

    /// <summary>
    /// Whether <paramref name="Text"/> is printable ASCII to which no contextual rule can apply.
    /// </summary>
    internal bool Accepts(ReadOnlySpan<char> Text)
    {
        /*
         * The generic range-search helper boxes char in its unoptimized path, so short messages would allocate
         * before tiered JIT promotion.
         */
        UInt128 present = 0;
        foreach (var c in Text) {
            if (c is < FIRST or > LAST)
                return false;
            present |= UInt128.One << c;
        }
        return (present & Triggers) == 0 || !MayApplyRule(present);
    }

    /// <summary>
    /// Whether a contextual rule may apply to <paramref name="Text"/>, judged by its printable ASCII characters: the
    /// rules this table knows match only those.
    /// </summary>
    internal bool MayApplyRule(ReadOnlySpan<char> Text)
    {
        UInt128 present = 0;
        foreach (var c in Text)
            if (c is >= FIRST and <= LAST)
                present |= UInt128.One << c;
        return (present & Triggers) != 0 && MayApplyRule(present);
    }

    private bool MayApplyRule(UInt128 Present)
    {
        foreach (var rule in Rules) {
            var possible = true;
            foreach (var members in rule)
                possible &= (Present & members) != 0;
            if (possible)
                return true;
        }
        return false;
    }

    /// <summary>
    /// <see cref="TryMeasurePrefixFixed"/> without the checks, for an <paramref name="End"/> of at least 1 in a
    /// text the caller found <see cref="Accepts"/> whole, which every prefix then passes.
    /// </summary>
    internal long PrefixFixed(ReadOnlySpan<char> Text, int End)
    {
        long total = 0;
        for (var x = 0; x < End - 1; x++)
            total += Pairs[PairIndex(Text[x], Text[x + 1])];

        return total + (End < Text.Length ? Pairs[PairIndex(Text[End - 1], Text[End])] : Singles[Text[End - 1]]);
    }

    /// <summary>
    /// Every <see cref="TryMeasurePrefixFixed"/> of <paramref name="Text"/> at once: <paramref name="Prefixes"/>[end]
    /// for each end from 0 to its length. The whole text passes the checks only if every prefix does.
    /// </summary>
    internal bool TryFillPrefixes(ReadOnlySpan<char> Text, Span<long> Prefixes)
    {
        if (!Accepts(Text))
            return false;
        long total = 0;
        Prefixes[0] = 0;
        for (var end = 1; end < Text.Length; end++) {
            total += Pairs[PairIndex(Text[end - 1], Text[end])];
            Prefixes[end] = total;
        }
        if (Text.Length > 0)
            Prefixes[Text.Length] = total + Singles[Text[^1]];
        return true;
    }

    // Scripts HarfBuzz uses for ASCII: Latin letters, and Common digits and punctuation.
    private static readonly Tag[] Scripts = [new('l', 'a', 't', 'n'), new('D', 'F', 'L', 'T')];
    private static readonly Tag English = new('E', 'N', 'G', ' ');

    /*
     * Features HarfBuzz enables for horizontal LTR text without user features
     * (hb-ot-shape.cc), except frac, numr and dnom, which only apply around U+2044.
     */
    private static readonly Tag[] DefaultFeatures = [
        new('r', 'v', 'r', 'n'), new('l', 't', 'r', 'a'), new('l', 't', 'r', 'm'), new('r', 'a', 'n', 'd'),
        new('t', 'r', 'a', 'k'), new('H', 'a', 'r', 'f'), new('H', 'A', 'R', 'F'), new('B', 'u', 'z', 'z'),
        new('B', 'U', 'Z', 'Z'), new('a', 'b', 'v', 'm'), new('b', 'l', 'w', 'm'), new('c', 'c', 'm', 'p'),
        new('l', 'o', 'c', 'l'), new('m', 'a', 'r', 'k'), new('m', 'k', 'm', 'k'), new('r', 'l', 'i', 'g'),
        new('c', 'a', 'l', 't'), new('c', 'l', 'i', 'g'), new('c', 'u', 'r', 's'), new('d', 'i', 's', 't'),
        new('k', 'e', 'r', 'n'), new('l', 'i', 'g', 'a'), new('r', 'c', 'l', 't'),
    ];

    /// <summary>
    /// The fast path for the bundled Inter, which has no AAT, variations or legacy kern table.
    /// These font-specific restrictions are assumed here; changing the font requires revalidation.
    /// </summary>
    internal static AsciiFastPath Create(Typeface Typeface, HarfBuzz Shaper)
    {
        var tables = Shaper.Tables;
        using var buffer = new Buffer();
        var glyphs = new uint[TABLE_SIZE];
        for (var c = FIRST;c <= LAST;c++)
            Shaper.TryGetNominalGlyph(c, out glyphs[c]);
        var rules = new List<UInt128[]>();
        foreach (var rule in tables.SubstitutionRules(tables.CollectLookups(OpenType.GSUB, Scripts, English, DefaultFeatures), glyphs.AsSpan(FIRST..(LAST + 1)))) {
            // A rule with a class that no printable ASCII character belongs to never fires
            if (Array.IndexOf(rule, UInt128.Zero) >= 0)
                continue;

            // Masks index the glyphs from First; classes are indexed by character code
            for (var x = 0;x < rule.Length;x++)
                rule[x] <<= FIRST;

            rules.Add(rule);
        }

        // Inter's only GPOS lookup is first-glyph pair adjustment, which the pair table composes.
        return new AsciiFastPath(Typeface, Shaper, glyphs, buffer, rules);
    }
}
