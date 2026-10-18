using NativeFont = HarfBuzzSharp.Font;

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using HarfBuzzSharp;

namespace MessageHeightTwitch;

/// <summary>
/// Font's OpenType tables
/// </summary>
internal sealed partial class OpenType : IDisposable
{
    [LibraryImport("libHarfBuzzSharp", EntryPoint = "hb_ot_layout_table_get_feature_tags"), UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    private static unsafe partial uint GetFeatureTags(nint Face, uint TableTag, uint StartOffset, uint* FeatureCount, uint* FeatureTags);

    [LibraryImport("libHarfBuzzSharp", EntryPoint = "hb_ot_layout_table_find_feature_variations"), UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    private static unsafe partial int FindFeatureVariations(nint Face, uint TableTag, int* Coords, uint CoordCount, uint* VariationsIndex);

    [LibraryImport("libHarfBuzzSharp", EntryPoint = "hb_ot_layout_table_select_script"), UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    private static unsafe partial int SelectScript(nint Face, uint TableTag, uint ScriptCount, uint* ScriptTags, uint* ScriptIndex, uint* ChosenScript);

    [LibraryImport("libHarfBuzzSharp", EntryPoint = "hb_ot_layout_script_select_language"), UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    private static unsafe partial int SelectLanguage(nint Face, uint TableTag, uint ScriptIndex, uint LanguageCount, uint* LanguageTags, uint* LanguageIndex);

    [LibraryImport("libHarfBuzzSharp", EntryPoint = "hb_ot_layout_language_find_feature"), UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    private static unsafe partial int FindFeature(nint Face, uint TableTag, uint ScriptIndex, uint LanguageIndex, uint FeatureTag, uint* FeatureIndex);

    [LibraryImport("libHarfBuzzSharp", EntryPoint = "hb_ot_layout_feature_with_variations_get_lookups"), UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    private static unsafe partial uint GetFeatureLookups(nint Face, uint TableTag, uint FeatureIndex, uint VariationsIndex, uint StartOffset, uint* LookupCount, uint* LookupIndexes);

    [LibraryImport("libHarfBuzzSharp", EntryPoint = "hb_ot_name_get_utf16"), UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    private static unsafe partial uint GetName(nint Face, uint NameId, nint Language, uint* TextSize, char* Text);

    [LibraryImport("libHarfBuzzSharp", EntryPoint = "hb_style_get_value"), UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    private static partial float GetStyleValue(nint Font, uint StyleTag);

    // Tables HarfBuzz doesn't expose are read by read-fonts through Native/opentype
    private const string TablesLibrary = "mht_opentype";

    [LibraryImport(TablesLibrary, EntryPoint = "mht_gsub_rules"), UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    [return: MarshalAs(UnmanagedType.U1)]
    private static unsafe partial bool GetSubstitutionRules(ReadOnlySpan<byte> Gsub, nuint GsubLength, ReadOnlySpan<ushort> Lookups, nuint LookupCount,
        ReadOnlySpan<uint> Glyphs, nuint GlyphCount, nint Context, delegate* unmanaged[Cdecl]<nint, UInt128*, nuint, void> Rule);

    [LibraryImport(TablesLibrary, EntryPoint = "mht_gasp_range"), UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    [return: MarshalAs(UnmanagedType.U1)]
    private static partial bool GetGaspRange(ReadOnlySpan<byte> Gasp, nuint Length, int Ppem, out GaspRange Range);

    [LibraryImport(TablesLibrary, EntryPoint = "mht_bitmap_strikes"), UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    private static partial nuint GetBitmapStrikes(ReadOnlySpan<byte> Eblc, nuint Length, Span<BitmapStrike> Strikes, nuint Capacity);

    // hb-ot-layout.h
    private const uint NoScriptIndex = 0xFFFF;
    private const uint DefaultLanguageIndex = 0xFFFF;

    internal static readonly Tag GSUB = new('G', 'S', 'U', 'B');
    internal static readonly Tag GPOS = new('G', 'P', 'O', 'S');
    internal static readonly Tag GASP = new('g', 'a', 's', 'p');
    internal static readonly Tag EBLC = new('E', 'B', 'L', 'C');

    // https://learn.microsoft.com/typography/opentype/spec/gasp
    internal const int GaspGridFit = 0x0001;

    // Layouts match the structs of Native/opentype
    internal readonly record struct GaspRange(int Lower, int Upper, int Behavior);
    internal readonly record struct BitmapStrike(int PpemX, int PpemY, int StartGlyph, int EndGlyph);

    internal readonly Face Face;

    internal OpenType(Typeface Typeface) => Face = new Face(Typeface.Blob, 0);

    /// <summary>Returns the raw table, or an empty blob if the font lacks it</summary>
    internal Blob Table(Tag Tag) => Face.ReferenceTable(Tag);

    /// <summary>
    /// The name table string for <paramref name="Id"/> in <paramref name="Language"/>, or null if the font
    /// has none. HarfBuzz falls back only to a name whose language is a prefix of the requested one.
    /// </summary>
    internal static unsafe string? Name(Face Face, OpenTypeNameId Id, Language Language)
    {
        var length = GetName(Face.Handle, (uint)Id, Language.Handle, null, null);
        if (length == 0)
            return null;
        // The size counts the terminating null HarfBuzz writes
        var size = length + 1;
        var text = stackalloc char[(int)size];
        GetName(Face.Handle, (uint)Id, Language.Handle, &size, text);
        return new string(text, 0, (int)size);
    }

    /// <summary>A style attribute such as weight or italic, from the OS/2 and head tables (hb-style.h).</summary>
    internal static float Style(NativeFont Font, Tag Attribute) => GetStyleValue(Font.Handle, Attribute);

    /// <summary>Whether the GPOS or GSUB FeatureList contains the feature tag</summary>
    internal bool HasFeature(Tag Feature) => HasFeature(GPOS, Feature) || HasFeature(GSUB, Feature);

    private unsafe bool HasFeature(Tag Table, Tag Feature)
    {
        const int PageSize = 64;
        var tags = stackalloc uint[PageSize];
        uint total, start = 0;
        do {
            var count = (uint)PageSize;
            total = GetFeatureTags(Face.Handle, Table, start, &count, tags);
            for (var x = 0;x < count;x++) {
                if (tags[x] == Feature)
                    return true;
            }
            start += count;
            if (count == 0)
                break;
        } while (start < total);
        return false;
    }

    /// <summary>
    /// Every lookup HarfBuzz could apply from <paramref name="Features"/>, for runs in any of <paramref name="Scripts"/> with <paramref name="Language"/>.
    /// Script selection falls back to DFLT like HarfBuzz; both the default and the selected
    /// language system are included. Feature variations are resolved at default coordinates.
    /// The bundled fonts have no required features.
    /// </summary>
    internal unsafe HashSet<int> CollectLookups(Tag Table, ReadOnlySpan<Tag> Scripts, Tag Language, ReadOnlySpan<Tag> Features)
    {
        var lookups = new HashSet<int>();
        uint variations;
        FindFeatureVariations(Face.Handle, Table, null, 0, &variations);
        foreach (var requested in Scripts) {
            uint scriptTag = requested, script, chosen;
            SelectScript(Face.Handle, Table, 1, &scriptTag, &script, &chosen);
            if (script == NoScriptIndex)
                continue;

            uint languageTag = Language, language;
            SelectLanguage(Face.Handle, Table, script, 1, &languageTag, &language);
            foreach (var languageIndex in (ReadOnlySpan<uint>)[DefaultLanguageIndex, language]) {
                uint feature;
                foreach (var tag in Features) {
                    if (FindFeature(Face.Handle, Table, script, languageIndex, tag, &feature) != 0)
                        AddFeatureLookups(Table, feature, variations, lookups);
                }
            }
        }
        return lookups;
    }

    private unsafe void AddFeatureLookups(Tag Table, uint Feature, uint Variations, HashSet<int> Lookups)
    {
        const int PageSize = 64;
        var indexes = stackalloc uint[PageSize];
        uint total, start = 0;
        do {
            var count = (uint)PageSize;
            total = GetFeatureLookups(Face.Handle, Table, Feature, Variations, start, &count, indexes);
            for (var x = 0;x < count;x++)
                Lookups.Add((int)indexes[x]);
            start += count;
            if (count == 0)
                break;
        } while (start < total);
    }

    /// <summary>
    /// The substitutions of the GSUB <paramref name="Lookups"/>, each as one mask per glyph position: bit i is
    /// set when <paramref name="Glyphs"/>[i] can occupy the position. Handles the subtables Inter's default
    /// features use: ligatures, and chained contexts of format 3. Changing the font or enabled features requires
    /// checking this coverage before using the ASCII fast path; unsupported lookup forms throw.
    /// </summary>
    internal unsafe List<UInt128[]> SubstitutionRules(IEnumerable<int> Lookups, ReadOnlySpan<uint> Glyphs)
    {
        var rules = new List<UInt128[]>();
        var lookups = new List<ushort>();
        foreach (var index in Lookups)
            lookups.Add((ushort)index);
        using var gsub = Table(GSUB);
        using var handle = new GCHandle<List<UInt128[]>>(rules);
        if (!GetSubstitutionRules(gsub.AsSpan(), (nuint)gsub.Length, CollectionsMarshal.AsSpan(lookups), (nuint)lookups.Count,
                Glyphs, (nuint)Glyphs.Length, GCHandle<List<UInt128[]>>.ToIntPtr(handle), &AddRule))
            throw new InvalidDataException("GSUB lookups must be ligatures or chained contexts of format 3");
        return rules;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static unsafe void AddRule(nint Rules, UInt128* Masks, nuint Count) => GCHandle<List<UInt128[]>>.FromIntPtr(Rules).Target.Add(new ReadOnlySpan<UInt128>(Masks, (int)Count).ToArray());

    /// <summary>
    /// Finds the gasp range covering <paramref name="Ppem"/>, if the font has a gasp table. The last range
    /// of every bundled gasp table ends at 0xFFFF.
    /// </summary>
    internal bool TryGetGaspRange(int Ppem, out GaspRange Range)
    {
        using var blob = Table(GASP);
        return GetGaspRange(blob.AsSpan(), (nuint)blob.Length, Ppem, out Range);
    }

    /// <summary>Returns the EBLC bitmap strikes, or none if the font has no EBLC table</summary>
    internal BitmapStrike[] BitmapStrikes()
    {
        using var blob = Table(EBLC);
        var strikes = new BitmapStrike[GetBitmapStrikes(blob.AsSpan(), (nuint)blob.Length, [], 0)];
        GetBitmapStrikes(blob.AsSpan(), (nuint)blob.Length, strikes, (nuint)strikes.Length);
        return strikes;
    }

    public void Dispose() => Face.Dispose();
}
