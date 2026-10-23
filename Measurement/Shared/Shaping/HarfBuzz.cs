using NativeFont = HarfBuzzSharp.Font;

using System;
using System.Buffers;
using System.Text;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using HarfBuzzSharp;

using Buffer = HarfBuzzSharp.Buffer;

namespace MessageHeightTwitch.Measurement.Shared.Shaping;

/// <summary>
/// What <see cref="HarfBuzz.CharacterPositions"/> found at a character of a run.
/// </summary>
internal enum ClusterMark : byte
{
    // No cluster starts at the character.
    None,
    // A cluster starts that is not safe to break before.
    Unsafe,
    // A cluster starts that is safe to break before.
    Safe
}

/// <summary>
/// A font shaper with precomputed nominal glyph advances for one typeface and font size.
/// </summary>
/// <remarks>
/// Cache usage: 4 native bytes per glyph for the advance table, plus native face/font state and an optional
/// unicode-range sub-font per instance. The process also shares a 1,024-entry script table (4 KiB of payload)
/// and native font callbacks.
/// </remarks>
public partial class HarfBuzz : IDisposable
{
    [LibraryImport("libHarfBuzzSharp", EntryPoint = "hb_shape"), UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    private static unsafe partial void ShapeNative(nint Font, nint Buffer, Feature* Features, uint Count);

    private unsafe void Shape(Buffer Buffer, ReadOnlySpan<Feature> Features)
    {
        fixed (Feature* data = Features)
            ShapeNative(RangeFont?.Handle ?? Font.Handle, Buffer.Handle, data, (uint)Features.Length);
    }

    /*
     * Blink's HarfBuzzGetGlyph finds no glyph for a code point outside a unicode-range
     * subset's range, so outside characters, compositions such as U+00C0 and an inserted
     * dotted circle all depend on the CSS range rather than the file's cmap. A sub-font
     * applies the limit, including glyph lookups for characters produced by normalization.
     */
    private Range[]? UnicodeRanges;
    private NativeFont? RangeFont;

    // The sub-font's native data (RangeFunctions): the parent font, the range count, then each range's bounds.
    private unsafe nint* RangeData;

    internal unsafe void LimitToUnicodeRanges(Range[] Ranges)
    {
        RangeData = (nint*)NativeMemory.Alloc((nuint)(2 + 2 * Ranges.Length), (nuint)sizeof(nint));
        RangeData[0] = Font.Handle;
        RangeData[1] = Ranges.Length;
        for (var x = 0; x < Ranges.Length; x++) {
            RangeData[2 + 2 * x] = Ranges[x].Start.Value;
            RangeData[3 + 2 * x] = Ranges[x].End.Value;
        }
        var limited = new NativeFont(Font);
        FontSetFuncs(limited.Handle, RangeFunctions, (nint)RangeData, 0);
        RangeFont = limited;
        UnicodeRanges = Ranges;
    }

    // Glyph lookups limited to the ranges
    private static readonly unsafe nint RangeFunctions = CreateRangeFunctions();

    private static unsafe nint CreateRangeFunctions()
    {
        var functions = FontFuncsCreate();
        SetNominalGlyphFunc(functions, &RangeNominalGlyph, 0, 0);
        SetNominalGlyphsFunc(functions, &RangeNominalGlyphs, 0, 0);
        SetVariationGlyphFunc(functions, &RangeVariationGlyph, 0, 0);
        FontFuncsMakeImmutable(functions);
        return functions;
    }

    private static unsafe bool InRangeData(nint* Data, uint Codepoint)
    {
        for (nint x = 0; x < Data[1]; x++)
            if (Data[2 + 2 * x] <= Codepoint && Codepoint <= Data[3 + 2 * x])
                return true;
        return false;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static unsafe int RangeNominalGlyph(nint Font, nint FontData, uint Unicode, uint* Glyph, nint UserData)
    {
        *Glyph = 0;
        var data = (nint*)FontData;
        return InRangeData(data, Unicode) ? GetNominalGlyph(data[0], Unicode, Glyph) : 0;
    }

    /// <summary>
    /// HarfBuzz's batch lookup: the number of characters mapped before the first one without a glyph.
    /// </summary>
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static unsafe uint RangeNominalGlyphs(nint Font, nint FontData, uint Count, byte* FirstUnicode, uint UnicodeStride, byte* FirstGlyph, uint GlyphStride, nint UserData)
    {
        var data = (nint*)FontData;
        for (var x = 0u; x < Count; x++) {
            var unicode = *(uint*)(FirstUnicode + x * UnicodeStride);
            if (!InRangeData(data, unicode) || GetNominalGlyph(data[0], unicode, (uint*)(FirstGlyph + x * GlyphStride)) == 0)
                return x;
        }
        return Count;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static unsafe int RangeVariationGlyph(nint Font, nint FontData, uint Unicode, uint Selector, uint* Glyph, nint UserData)
    {
        *Glyph = 0;
        var data = (nint*)FontData;
        return InRangeData(data, Unicode) ? GetVariationGlyph(data[0], Unicode, Selector, Glyph) : 0;
    }

    // The parent's lookups read its cmap natively, without calling back into managed code.
    [LibraryImport("libHarfBuzzSharp", EntryPoint = "hb_font_get_nominal_glyph"), UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)]), SuppressGCTransition]
    private static unsafe partial int GetNominalGlyph(nint Font, uint Unicode, uint* Glyph);

    [LibraryImport("libHarfBuzzSharp", EntryPoint = "hb_font_get_variation_glyph"), UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)]), SuppressGCTransition]
    private static unsafe partial int GetVariationGlyph(nint Font, uint Unicode, uint Selector, uint* Glyph);

    [LibraryImport("libHarfBuzzSharp", EntryPoint = "hb_font_funcs_set_nominal_glyph_func"), UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    private static unsafe partial void SetNominalGlyphFunc(nint Functions, delegate* unmanaged[Cdecl]<nint, nint, uint, uint*, nint, int> Func, nint UserData, nint Destroy);

    [LibraryImport("libHarfBuzzSharp", EntryPoint = "hb_font_funcs_set_nominal_glyphs_func"), UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    private static unsafe partial void SetNominalGlyphsFunc(nint Functions, delegate* unmanaged[Cdecl]<nint, nint, uint, byte*, uint, byte*, uint, nint, uint> Func, nint UserData, nint Destroy);

    [LibraryImport("libHarfBuzzSharp", EntryPoint = "hb_font_funcs_set_variation_glyph_func"), UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    private static unsafe partial void SetVariationGlyphFunc(nint Functions, delegate* unmanaged[Cdecl]<nint, nint, uint, uint, uint*, nint, int> Func, nint UserData, nint Destroy);

    private bool InUnicodeRange(int Codepoint) => FontDesc.InRanges(UnicodeRanges!, Codepoint);

    // Whether the font is a CSS unicode-range subset, one of the faces Blink tries first for a segment.
    internal bool IsUnicodeRangeSubset => UnicodeRanges != null;

    /// <summary>Whether the font has a glyph for <paramref name="Codepoint"/> within its unicode-range.</summary>
    internal bool CoversCodepoint(int Codepoint) => (UnicodeRanges == null || InUnicodeRange(Codepoint)) && HasGlyph(Codepoint);

    // GuessSegmentProperties otherwise takes the language from the host locale.
    private static readonly Language ShapingLanguage = new("en-US");

    /*
     * Blink's ICUScriptToHBScript: hb_script_from_string(uscript_getShortName(script)).
     * Filled once so that shaping never allocates.
     */
    private static readonly Script[] HarfBuzzScripts = CreateHarfBuzzScripts();

    private static unsafe Script[] CreateHarfBuzzScripts()
    {
        /*
         * The bundled ICU can know scripts newer than icu.net's UScriptCode enum, so the
         * table covers every code ICU names; uscript_getShortName is null past the last one.
         */
        var shortName = (delegate* unmanaged[Cdecl]<int, byte*>)ICUExtensions.Bind("uscript_getShortName");
        var scripts = new Script[1024];
        for (var code = 0; code < scripts.Length; code++) {
            var name = shortName(code);
            scripts[code] = name == null ? Script.Invalid : Script.Parse(Marshal.PtrToStringAnsi((nint)name)!);
        }
        return scripts;
    }
    internal readonly OpenType Tables;
    private readonly NativeFont Font;
    private readonly bool HasColor;
    private readonly bool RoundAdvances;
    internal readonly HanKerning Kerning;

    public unsafe HarfBuzz(Typeface Typeface, float FontSize)
    {
        Tables = new OpenType(Typeface);

        /*
         * Blink's ColorTableLookup::TypefaceHasAnySupportedColorTable checks the table directory. Among the
         * bundled fonts only Segoe UI Emoji has color glyphs, in COLR with its CPAL; none has sbix or CBDT.
         */
        HasColor = Tables.Face.HasColorLayers;
        // Captured strike advances already apply DirectWrite's bitmap policy.
        // Blink keeps subpixel positioning enabled for these platform faces.
        var platformAdvances = FontSize == Fonts.TWITCH_FONT_SIZE ? Typeface.PlatformAdvances : null;
        RoundAdvances = platformAdvances == null && UsesBitmapMetrics(Tables, FontSize);

        /*
         * Blink's HarfBuzzFace: positions are 16.16 fixed point at the font size, HarfBuzz
         * scales GPOS itself, and nominal advances come from Skia (CreateAdvanceTable).
         */
        Unscaled = new NativeFont(Tables.Face);
        if (Typeface.HasWeightAxis)
            Unscaled.SetVariations([new Variation { Tag = new Tag('w', 'g', 'h', 't'), Value = Typeface.FontWeight }]);
        AdvanceTable = CreateAdvanceTable(Unscaled, Tables.Face.GlyphCount, Tables.Face.UnitsPerEm, FontSize, RoundAdvances,
            platformAdvances);
        Font = new NativeFont(Unscaled);
        var size = (int)(FontSize * FIXED_ONE);
        Font.SetScale(size, size);
        FontSetFuncs(Font.Handle, AdvanceFunctions, (nint)AdvanceTable, 0);
        Kerning = new HanKerning(Tables);
    }

    private readonly NativeFont Unscaled;
    // Native advances by glyph id, so HarfBuzz reads advances without entering managed code.
    private readonly unsafe int* AdvanceTable;

    /// <summary>
    /// Blink's SkFontGetGlyphWidthForHarfBuzz on Skia's DirectWrite backend: SkScalerContext_DW
    /// computes <c>fTextSizeMeasure * advanceWidth / designUnitsPerEm</c> in float, rounds it
    /// for a non-subpixel font, and SkiaScalarToHarfBuzzPosition truncates it to 16.16. Summing
    /// these instead of exact advances decides which 1/64 px a run's width rounds up to.
    /// </summary>
    private static unsafe int* CreateAdvanceTable(NativeFont Unscaled, int GlyphCount, int UnitsPerEm, float FontSize, bool Round, int[]? PlatformAdvances)
    {
        var table = (int*)NativeMemory.Alloc((nuint)GlyphCount, sizeof(int));
        if (PlatformAdvances != null) {
            PlatformAdvances.AsSpan().CopyTo(new Span<int>(table, GlyphCount));
            return table;
        }
        var glyphs = new uint[GlyphCount];

        for (var g = 0; g < GlyphCount; g++)
            glyphs[g] = (uint)g;

        var units = Unscaled.GetHorizontalGlyphAdvances(glyphs);

        for (var g = 0; g < GlyphCount; g++) {
            var width = FontSize * units[g] / UnitsPerEm;

            if (Round)
                width = MathF.Round(width, MidpointRounding.AwayFromZero);

            table[g] = (int)(width * FIXED_ONE);
        }
        return table;
    }

    private static readonly unsafe nint AdvanceFunctions = CreateAdvanceFunctions();

    private static unsafe nint CreateAdvanceFunctions()
    {
        var functions = FontFuncsCreate();
        SetAdvanceFunc(functions, &TableAdvance, 0, 0);

        // HarfBuzzSharp's managed batch delegate ignores these strides, so it is set natively.
        SetAdvancesFunc(functions, &TableAdvances, 0, 0);
        FontFuncsMakeImmutable(functions);
        return functions;
    }

    /// <summary>Shaping the bundled fonts only produces glyph ids within the font.</summary>
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static unsafe int TableAdvance(nint Font, nint FontData, uint Glyph, nint UserData) => ((int*)FontData)[Glyph];

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static unsafe void TableAdvances(nint Font, nint FontData, uint Count, byte* FirstGlyph, uint GlyphStride,byte* FirstAdvance, uint AdvanceStride, nint UserData)
    {
        var table = (int*)FontData;
        for (var x = 0u; x < Count; x++) {
            *(int*)(FirstAdvance + x * AdvanceStride) = table[*(uint*)(FirstGlyph + x * GlyphStride)];
        }
    }

    [LibraryImport("libHarfBuzzSharp", EntryPoint = "hb_font_funcs_create"), UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    private static partial nint FontFuncsCreate();

    [LibraryImport("libHarfBuzzSharp", EntryPoint = "hb_font_funcs_set_glyph_h_advance_func"), UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    private static unsafe partial void SetAdvanceFunc(nint Functions, delegate* unmanaged[Cdecl]<nint, nint, uint, nint, int> Func, nint UserData, nint Destroy);

    [LibraryImport("libHarfBuzzSharp", EntryPoint = "hb_font_funcs_set_glyph_h_advances_func"), UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    private static unsafe partial void SetAdvancesFunc(nint Functions, delegate* unmanaged[Cdecl]<nint, nint, uint, byte*, uint, byte*, uint, nint, void> Func, nint UserData, nint Destroy);

    [LibraryImport("libHarfBuzzSharp", EntryPoint = "hb_font_funcs_make_immutable"), UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    private static partial void FontFuncsMakeImmutable(nint Functions);

    [LibraryImport("libHarfBuzzSharp", EntryPoint = "hb_font_set_funcs"), UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    private static partial void FontSetFuncs(nint Font, nint Functions, nint FontData, nint Destroy);

    /*
     * An EBLC strike must span at least this many glyph ids to count as
     * covering the font, rather than being a stray glyph or two.
     */
    private const int MIN_STRIKE_GLYPHS = 4;

    private static bool UsesBitmapMetrics(OpenType Tables, float FontSize)
    {
        /*
         * Skia's DirectWrite backend uses GDI-compatible, integer advances
         * for monochrome bitmap strikes, including the gridfit-only gasp range.
         * No bundled font has EBSC bitmap scales.
         */
        var ppem = (int)FontSize;
        var (min, max) = Tables.TryGetGaspRange(ppem, out var range) && range.Behavior == OpenType.GaspGridFit
            ? (range.Lower, range.Upper) : (ppem, ppem);
        return Array.Exists(Tables.BitmapStrikes(), strike => strike.PpemX == strike.PpemY && strike.PpemY >= min &&
            strike.PpemY <= max && strike.EndGlyph - strike.StartGlyph + 1 >= MIN_STRIKE_GLYPHS);
    }

    private const int NOTDEF = 0;
    private const int TEXT_PRESENTATION_SELECTOR = 0xFE0E;  // VS15
    private const int EMOJI_PRESENTATION_SELECTOR = 0xFE0F; // VS16

    // One pixel in HarfBuzz's 16.16 fixed point, Blink's kHbPosition1.
    internal const int FIXED_ONE = 1 << 16;

    /// <summary>
    /// A glyph advance in 16.16 fixed point, rounded to whole pixels for a
    /// non-subpixel font like Blink's RoundHarfBuzzBufferPositions.
    /// </summary>
    internal int FixedAdvance(int Advance) => RoundAdvances ? (int)MathF.Round(Advance / (float)FIXED_ONE, MidpointRounding.AwayFromZero) * FIXED_ONE : Advance;

    /// <summary>
    /// Blink keeps glyph advances as 16.16 TextRunLayoutUnit and sums
    /// </summary>
    internal static float FixedToFloat(long Advance) => (float)(Advance / (double)FIXED_ONE);

    /// <summary>A pixel width in 16.16 fixed point, rounded to the nearest unit.</summary>
    internal static long FloatToFixed(double Width) => (long)Math.Round(Width * FIXED_ONE);

    /// <summary>
    /// The glyph the font's cmap maps <paramref name="Character"/> to, without a variation
    /// selector and ignoring any unicode-range limit; false with <c>.notdef</c> when unmapped.
    /// </summary>
    internal bool TryGetNominalGlyph(char Character, out uint Glyph) => Font.TryGetGlyph((int)Character, out Glyph);

    internal bool HasGlyph(int Codepoint) => Font.TryGetGlyph(Codepoint, out _);

    // HarfBuzz can synthesize U+3000 from the ordinary space glyph. Blink requeues that cluster until the
    // last fallback stage so a real ideographic-space glyph determines its font and run metrics.
    internal bool IsSyntheticIdeographicSpace(char Character, uint Glyph) =>
        Character == '\u3000' && Font.TryGetGlyph(' ', out var space) && Glyph == space;

    /// <summary>
    /// Like Blink's HarfBuzzGetGlyph: an explicit emoji variation selector
    /// must be honored by CSS fonts. Windows system fallback then selects
    /// the requested presentation and may use the base glyph without cmap14.
    /// </summary>
    internal bool SupportsVariationSelectors(ReadOnlySpan<char> Text)
    {
        Rune previous = default;
        foreach (var rune in Text.EnumerateRunes()) {
            if (rune.Value is TEXT_PRESENTATION_SELECTOR or EMOJI_PRESENTATION_SELECTOR && EmojiPresentation.IsEmoji(previous) && !Font.TryGetVariationGlyph(previous.Value, (uint)rune.Value, out _) && HasColor != (rune.Value == EMOJI_PRESENTATION_SELECTOR))
                return false;
            previous = rune;
        }
        return true;
    }

    /// <summary>
    /// Shapes <paramref name="Text"/> on its own into <paramref name="Buffer"/>,
    /// with the default features and guessed script and direction.
    /// </summary>
    internal void Shape(ReadOnlySpan<char> Text, Buffer Buffer, UScriptCode Script = UScriptCode.USCRIPT_INVALID_CODE) => ShapeRange(Text, .., Buffer, null, default, Script);

    /// <summary>
    /// Whether shaping <paramref name="Text"/> produces no .notdef glyph.
    /// <paramref name="Buffer"/> is scratch space and is left holding the shaped text.
    /// </summary>
    internal bool SupportedByFont(ReadOnlySpan<char> Text, Buffer Buffer, UScriptCode Script = UScriptCode.USCRIPT_INVALID_CODE)
    {
        Shape(Text, Buffer, Script);

        foreach (var info in Buffer.GetGlyphInfoSpan())
            if (info.Codepoint == NOTDEF)
                return false;

        return true;
    }

    /// <summary>
    /// Shapes <c>Text[Run]</c> into <paramref name="Buffer"/>, with the whole
    /// <paramref name="Text"/> available as context so joining decisions match the surrounding text,
    /// and returns its width like <see cref="ShapedFixedWidth(Buffer)"/>.
    /// </summary>
    /// <param name="Script">The run's script, as Blink sets it on the buffer. A script decides the
    /// shaper even for text without letters of its own, such as a space and a Devanagari mark,
    /// which the Indic shaper gives a dotted circle. Without one, HarfBuzz guesses.</param>
    internal float MeasureWidth(ReadOnlySpan<char> Text, Range Run, Buffer Buffer, Direction? Direction = null, ReadOnlySpan<Feature> Features = default, UScriptCode Script = UScriptCode.USCRIPT_INVALID_CODE) =>
        FixedToFloat(MeasureFixed(Text, Run, Buffer, Direction, Features, Script));

    /// <summary>The exact 16.16 advance of <see cref="MeasureWidth"/>, before conversion to float.</summary>
    internal long MeasureFixed(ReadOnlySpan<char> Text, Range Run, Buffer Buffer, Direction? Direction = null, ReadOnlySpan<Feature> Features = default, UScriptCode Script = UScriptCode.USCRIPT_INVALID_CODE,
        ReadOnlySpan<char> Before = default)
    {
        ShapeRange(Text, Run, Buffer, Direction, Features, Script, Before);
        return ShapedFixedWidth(Buffer);
    }

    // HarfBuzz reads at most this many UTF-16 units of context before an item (HB_BUFFER_CONTEXT_LENGTH).
    private const int CONTEXT_LENGTH = 5;

    /// <summary>
    /// How many units of <paramref name="Before"/> a run at the start of its text is shaped after: the end of the
    /// text before it, without a low surrogate cut from its pair. Features for such a run take clusters that many
    /// units later (HanKerning.GetFeatures with a ClusterOffset of minus this).
    /// </summary>
    internal static int ContextLength(ReadOnlySpan<char> Before)
    {
        var length = Math.Min(Before.Length, CONTEXT_LENGTH);
        return length > 0 && char.IsLowSurrogate(Before[^length]) ? length - 1 : length;
    }

    /// <summary>
    /// Reset direction and script as well as glyphs before reusing across font runs. A run at the start of
    /// <paramref name="Text"/> is shaped after <paramref name="Before"/>, the text before it, as Blink shapes a
    /// range of the whole paragraph: a ZWJ before an Arabic letter joins it, and a mark is not at the text's start.
    /// </summary>
    internal void ShapeRange(ReadOnlySpan<char> Text, Range Run, Buffer Buffer, Direction? Direction, ReadOnlySpan<Feature> Features, UScriptCode Script, ReadOnlySpan<char> Before = default)
    {
        var (start, length) = Run.GetOffsetAndLength(Text.Length);
        Buffer.ClearContents();
        var context = start == 0 ? ContextLength(Before) : 0;
        if (context == 0) {
            Buffer.AddUtf16(Text, start, length);
        } else {
            // Clusters then count from the context's start.
            var size = context + Text.Length;
            char[]? rented = null;

            // All validated chat paragraphs fit in a bounded stack buffer, including their shaping context.
            // Standalone internal callers may supply longer text and retain the pooled fallback.
            const int stackLimit = MessageHeightTwitch.MAX_PARAGRAPH_UTF16 + CONTEXT_LENGTH;
            Span<char> joined = size <= stackLimit ? stackalloc char[size] : (rented = ArrayPool<char>.Shared.Rent(size)).AsSpan(0, size);

            Before[^context..].CopyTo(joined);
            Text.CopyTo(joined[context..]);
            Buffer.AddUtf16(joined, context, length);

            if (rented != null)
                ArrayPool<char>.Shared.Return(rented);
        }
        Buffer.Language = ShapingLanguage;

        if (Direction.HasValue)
            Buffer.Direction = Direction.Value;

        if (Script >= 0)
            Buffer.Script = HarfBuzzScripts[(int)Script];

        Buffer.GuessSegmentProperties();

        Shape(Buffer, Features);
    }

    /// <summary>
    /// Blink's CachedPositionForOffset for a run in either direction, in exact 16.16 units, from one shaping
    /// of the whole run (ShapeResult::ComputePositionData). <paramref name="Positions"/>[k] is the logical
    /// advance before the cluster containing character k: the advances of every glyph in an earlier cluster, so
    /// the kern between a character and the next one (applied to the earlier glyph) counts toward the boundary
    /// after it, except a right-to-left kern applied as a placement, which counts toward the next character.
    /// <paramref name="Positions"/>[length] is the run's total advance. <paramref name="Marks"/>[k] is whether a
    /// cluster starts at k and whether it is safe to break before (Blink's SafeToBreakBefore: for the cluster's
    /// first glyph left to right, any glyph right to left).
    /// When <paramref name="Slice"/> is supplied, shape the original candidate <paramref name="Run"/> but retain
    /// only that successful cluster range, as Blink's CommitGlyphs does before another font shapes missing clusters.
    /// </summary>
    internal void CharacterPositions(ReadOnlySpan<char> Text, Range Run, Buffer Buffer, ReadOnlySpan<Feature> Features, UScriptCode Script, Direction Direction, Span<long> Positions, Span<ClusterMark> Marks, ReadOnlySpan<char> Before = default, Range? Slice = null)
    {
        ShapeRange(Text, Run, Buffer, Direction, Features, Script, Before);
        ReadPositions(Text, Run, Buffer, Direction, Positions, Marks, Before, Slice);
    }

    /// <summary>
    /// The positions and marks of <see cref="CharacterPositions"/> from <paramref name="Buffer"/>, which this font has
    /// just shaped with the same arguments.
    /// </summary>
    internal void ReadPositions(ReadOnlySpan<char> Text, Range Run, Buffer Buffer, Direction Direction, Span<long> Positions, Span<ClusterMark> Marks, ReadOnlySpan<char> Before = default, Range? Slice = null)
    {
        var infos = Buffer.GetGlyphInfoSpan();
        var glyphs = Buffer.GetGlyphPositionSpan();
        var (shapeStart, _) = Run.GetOffsetAndLength(Text.Length);
        var (start, length) = (Slice ?? Run).GetOffsetAndLength(Text.Length);

        // A run shaped after its context starts that many units into the buffer's text.
        if (shapeStart == 0)
            start += ContextLength(Before);

        Positions[..(length + 1)].Clear();
        Marks[..length].Clear();

        var firstGlyph = 0;
        while (firstGlyph < infos.Length && (infos[firstGlyph].Cluster < start || infos[firstGlyph].Cluster >= start + length))
            firstGlyph++;
        var glyphEnd = infos.Length;
        while (glyphEnd > firstGlyph && (infos[glyphEnd - 1].Cluster < start || infos[glyphEnd - 1].Cluster >= start + length))
            glyphEnd--;

        var rtl = Direction == Direction.RightToLeft;
        for (var x = firstGlyph; x < glyphEnd; x++) {
            // Clusters are offsets into the whole text, not into the buffer that starts at the run.
            var cluster = (int)infos[x].Cluster - start;
            Positions[cluster] += FixedAdvance(glyphs[x].XAdvance);

            if (rtl) {
                /*
                 * A right-to-left kern moves a glyph toward the next character with a placement and an advance
                 * of the same size (Noto Sans Arabic after alef, dal, reh and the other letters that do not join
                 * forward). Chrome's breaks inside Arabic words count that kern toward the next character, as
                 * if the text were cut after the letter: move it from this cluster to the logically next one,
                 * the closest glyph to the left in buffer order with another cluster.
                 */
                if (glyphs[x].XOffset < 0 && glyphs[x].XAdvance != 0) {
                    var left = x - 1;
                    while (left >= firstGlyph && infos[left].Cluster == infos[x].Cluster) left--;
                    if (left >= firstGlyph) {
                        var kern = FixedAdvance(glyphs[x].XOffset);
                        Positions[cluster] -= kern;
                        Positions[(int)infos[left].Cluster - start] += kern;
                    }
                }

                /*
                 * Blink's right-to-left safe offsets search every glyph (ShapeResultRun::NextSafeToBreakOffset):
                 * a cluster is safe when any of its glyphs is. The run's logically first glyph is safe; a glyph
                 * whose cluster continues in the next glyph is not at a boundary; otherwise HarfBuzz decides.
                 */
                var safe = x == glyphEnd - 1 || infos[x + 1].Cluster != infos[x].Cluster && (infos[x].GlyphFlags & GlyphFlags.UnsafeToBreak) == 0;
                if (safe)
                    Marks[cluster] = ClusterMark.Safe;
                else if (Marks[cluster] == ClusterMark.None)
                    Marks[cluster] = ClusterMark.Unsafe;
                continue;
            }
            if (x > firstGlyph && infos[x - 1].Cluster == infos[x].Cluster)
                continue;
            /*
             * Left to right, the cached positions keep the cluster's first glyph (ComputePositionData): the
             * run's first glyph is safe to break before, and HarfBuzz decides for the others.
             */
            Marks[cluster] = x == firstGlyph || (infos[x].GlyphFlags & GlyphFlags.UnsafeToBreak) == 0 ? ClusterMark.Safe : ClusterMark.Unsafe;
        }

        // A character without its own cluster keeps the position of the cluster before it.
        long running = 0, clusterStart = 0;
        for (var x = 0; x < length; x++) {
            var advance = Positions[x];
            if (Marks[x] != ClusterMark.None)
                clusterStart = running;
            Positions[x] = clusterStart;
            running += advance;
        }
        Positions[length] = running;
    }

    /// <summary>
    /// Returns the 16.16 advance of the glyphs already in <paramref name="Buffer"/>, scaled to the
    /// font size and, when <see cref="RoundAdvances"/> is set, rounded per glyph to whole pixels.
    /// Only valid immediately after this font has shaped that buffer.
    /// </summary>
    internal long ShapedFixedWidth(Buffer Buffer)
    {
        long total = 0;
        foreach (var pos in Buffer.GetGlyphPositionSpan())
            total += FixedAdvance(pos.XAdvance);
        return total;
    }

    public unsafe void Dispose()
    {
        RangeFont?.Dispose();
        NativeMemory.Free(RangeData);
        Font.Dispose();
        Unscaled.Dispose();
        NativeMemory.Free(AdvanceTable);
        Tables.Dispose();
    }

}
