using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

using Icu;

using MessageHeightTwitch.Measurement.Shared.Shaping;

namespace MessageHeightTwitch;

/// <summary>
/// Loaded typefaces, native font data and platform fallback selection, separate from CSS family ordering.
/// </summary>
internal sealed class FontCache
{
    private readonly Dictionary<string, Typeface> LocalTypeFaces = new(StringComparer.Ordinal);
    private readonly Typeface?[] SystemFallbacks;
    private readonly record struct SystemFont(string Family, int Weight, bool Italic);
    private readonly record struct SystemRange(int Start, int End, ushort Regular, ushort Bold, ushort MonoRegular,
        ushort MonoBold, ushort EmojiRegular, ushort EmojiBold,
        ushort Italic, ushort BoldItalic, ushort MonoItalic, ushort MonoBoldItalic, ushort EmojiItalic, ushort EmojiBoldItalic);
    private readonly (SystemFont[] Fonts, SystemRange[] Ranges) SystemFallbackData;
    private readonly Dictionary<string, PlatformMetrics> PlatformMetricData;

    internal readonly string Directory;

    private readonly Dictionary<string, Typeface> BoldFaces = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Typeface> ItalicFaces = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Typeface> BoldItalicFaces = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<Typeface, Lazy<HarfBuzz>> Shapers = new();

    internal static HarfBuzz GetShaper(Typeface Face) =>
        Shapers.GetOrAdd(Face, static face => new Lazy<HarfBuzz>(() => new HarfBuzz(face, Fonts.TWITCH_FONT_SIZE))).Value;

    internal FontCache(string FontDir)
    {
        Directory = FontDir;
        SystemFallbackData = ReadSystemFallbacks();
        PlatformMetricData = ReadPlatformMetrics();
        foreach (var path in System.IO.Directory.EnumerateFiles(FontDir, "*.ttf", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(FontDir, path).Replace('\\', '/')).Order(StringComparer.Ordinal))
        {
            var face = Typeface.FromFile(Path.Combine(FontDir, path));
            LocalTypeFaces.Add(path, FaceForStyle(path, face, face.FontWeight));
        }

        foreach (var (file, face) in LocalTypeFaces) {
            var boldFile = file.EndsWith("-Regular.ttf", StringComparison.Ordinal) ? file[..^12] + "-Bold.ttf"
                : file.EndsWith("-Italic.ttf", StringComparison.Ordinal) ? file[..^11] + "-BoldItalic.ttf" : file;
            BoldFaces.Add(file, LocalTypeFaces.TryGetValue(boldFile, out var bold)
                ? FaceForStyle(boldFile, bold, 700) : FaceForStyle(file, face, 700));
        }

        foreach (var (file, face) in LocalTypeFaces) {
            var italicFile = file.EndsWith("-Regular.ttf", StringComparison.Ordinal) ? file[..^12] + "-Italic.ttf"
                : file.EndsWith("-Bold.ttf", StringComparison.Ordinal) ? file[..^9] + "-BoldItalic.ttf" : file;
            // Synthetic oblique changes painted bounds, not advances. Reuse the upright shaper when
            // the family has no real italic face; the paragraph still retains its italic style boundary.
            ItalicFaces.Add(file, LocalTypeFaces.GetValueOrDefault(italicFile, face));
            BoldItalicFaces.Add(file, BoldFaces.GetValueOrDefault(italicFile, BoldFaces[file]));
        }

        var systemFonts = SystemFallbackData.Fonts;
        SystemFallbacks = new Typeface?[systemFonts.Length];
        for (var index = 1; index < systemFonts.Length; index++) {
            var font = systemFonts[index];
            var selected = LocalTypeFaces
                .Where(pair => StringComparer.OrdinalIgnoreCase.Equals(pair.Value.FamilyName, font.Family))
                .OrderBy(pair => pair.Value.IsItalic == font.Italic ? 0 : 1)
                .ThenBy(pair => Math.Abs(pair.Value.FontWeight - font.Weight))
                .FirstOrDefault();
            if (selected.Value == null)
                throw new FileNotFoundException($"Required Windows system fallback family '{font.Family}' was not found.", FontDir);
            SystemFallbacks[index] = GetTypeface(selected.Key, Bold: font.Weight >= 600, Italic: font.Italic);
        }
    }

    private Typeface FaceForStyle(string File, Typeface Face, int Weight)
    {
        if (PlatformMetricData.TryGetValue(File, out var profile) && profile.Weight == Weight) {
            using var stream = System.IO.File.OpenRead(Path.Combine(Directory, File));
            if (!SHA256.HashData(stream).AsSpan().SequenceEqual(profile.Sha256))
                throw new InvalidDataException($"Font file '{File}' differs from its Windows platform metrics profile.");
            using var nativeFace = new HarfBuzzSharp.Face(Face.Blob, 0);
            if (profile.Advances.Length != nativeFace.GlyphCount)
                throw new InvalidDataException($"Windows platform metrics for '{File}' have a different glyph count.");
            return new(Face, Weight, profile.Italic, profile.Advances);
        }
        return Face.HasWeightAxis && Face.FontWeight != Weight ? new(Face, Weight, Face.IsItalic, null) : Face;
    }

    internal Typeface GetTypeface(string File, bool Bold = false, bool Italic = false) =>
        (Italic ? Bold ? BoldItalicFaces : ItalicFaces : Bold ? BoldFaces : LocalTypeFaces).TryGetValue(File, out var face) ? face :
        throw new FileNotFoundException("Required font file was not found.", Path.Combine(Directory, File));

    internal void InitializeShapers()
    {
        foreach (var face in LocalTypeFaces.Values.Concat(BoldFaces.Values).Distinct())
            GetShaper(face);
    }

    private static string? GetForcedUnicodeBlockFont(Character.UBlockCode In) => In switch {
        Character.UBlockCode.EMOTICONS or
        Character.UBlockCode.ENCLOSED_ALPHANUMERIC_SUPPLEMENT => ChromiumFallbacks.MonoEmojiFonts[0],

        Character.UBlockCode.PLAYING_CARDS or
        Character.UBlockCode.MISCELLANEOUS_SYMBOLS or
        Character.UBlockCode.MISCELLANEOUS_SYMBOLS_AND_ARROWS or
        Character.UBlockCode.MISCELLANEOUS_SYMBOLS_AND_PICTOGRAPHS or
        Character.UBlockCode.TRANSPORT_AND_MAP_SYMBOLS or
        Character.UBlockCode.ALCHEMICAL_SYMBOLS or
        Character.UBlockCode.DINGBATS or
        Character.UBlockCode.GOTHIC => "SegoeUISymbol-Regular.ttf",

        Character.UBlockCode.ARROWS or
        Character.UBlockCode.MATHEMATICAL_OPERATORS or
        Character.UBlockCode.MISCELLANEOUS_TECHNICAL or
        Character.UBlockCode.GEOMETRIC_SHAPES or
        Character.UBlockCode.MISCELLANEOUS_MATHEMATICAL_SYMBOLS_A or
        Character.UBlockCode.SUPPLEMENTAL_ARROWS_A or
        Character.UBlockCode.SUPPLEMENTAL_ARROWS_B or
        Character.UBlockCode.MISCELLANEOUS_MATHEMATICAL_SYMBOLS_B or
        Character.UBlockCode.SUPPLEMENTAL_MATHEMATICAL_OPERATORS or
        Character.UBlockCode.MATHEMATICAL_ALPHANUMERIC_SYMBOLS or
        Character.UBlockCode.ARABIC_MATHEMATICAL_ALPHABETIC_SYMBOLS or
        Character.UBlockCode.GEOMETRIC_SHAPES_EXTENDED => ChromiumFallbacks.MathFont,

        _ => null
    };

    /*
     * Pin Chromium's unresolved Han fallback to en-US so measurements do not vary
     * with the machine's UI or system locale
     */
    private const UScriptCode HAN_FALLBACK_SCRIPT = UScriptCode.USCRIPT_SIMPLIFIED_HAN;

    /// <summary>
    /// Selects a system fallback candidate for a character hint. Coverage of the unresolved ranges is
    /// decided by HarfBuzzShaper, independently of this platform selection policy.
    /// </summary>
    internal Typeface? FallbackFontForCharacter(Rune Hint, bool Bold, EmojiPresentation.PRIORITY Presentation, bool Italic = false)
    {
        if (Character.GetCharType(Hint.Value) == Character.UCharCategory.PRIVATE_USE_CHAR ||
            Hint.Value is >= 0xFDD0 and <= 0xFDEF || (Hint.Value & 0xFFFE) == 0xFFFE)
            return null;

        var emoji = Presentation is EmojiPresentation.PRIORITY.EMOJI or EmojiPresentation.PRIORITY.EMOJI_WITH_VS;
        if (emoji || Presentation == EmojiPresentation.PRIORITY.TEXT_WITH_VS || EmojiPresentation.IsEmoji(Hint)) {
            foreach (var file in emoji ? ChromiumFallbacks.EmojiFonts : ChromiumFallbacks.MonoEmojiFonts) {
                var face = GetTypeface(file, Bold, Italic);
                if (face.HasGlyph(Hint.Value))
                    return face;
            }
        }

        var blockFile = GetForcedUnicodeBlockFont(UScript.GetBlock(Hint));
        var blockFace = blockFile == null ? null : GetTypeface(blockFile, Bold, Italic);
        if (blockFace != null && blockFace.HasGlyph(Hint.Value))
            return blockFace;

        var hintScript = UScript.GetScript(Hint);
        var familyScript = hintScript switch {
            UScriptCode.USCRIPT_HIRAGANA or UScriptCode.USCRIPT_KATAKANA => UScriptCode.USCRIPT_KATAKANA_OR_HIRAGANA,
            UScriptCode.USCRIPT_HAN => HAN_FALLBACK_SCRIPT,
            _ => hintScript
        };
        var plane = Hint.Value >> 16;
        var family = plane switch {
            0 when ChromiumFallbacks.ScriptFonts.TryGetValue(familyScript, out var scriptFont) => scriptFont,
            // Chromium's GetFallbackFamily: Extension I and plane 3 use ExtG;
            // other plane-2 characters use ExtB for our fixed en-US baseline.
            2 when Hint.Value is >= 0x2EBF0 and <= 0x2EE5F => "SimSun-ExtG-Regular.ttf",
            2 => "SimSun-ExtB-Regular.ttf",
            3 => "SimSun-ExtG-Regular.ttf",
            1 => null,
            _ => "LucidaSansUnicode-Regular.ttf"
        };
        var panUnicode = hintScript == UScriptCode.USCRIPT_HAN ? ChromiumFallbacks.CjkPanUnicodeFonts : ChromiumFallbacks.CommonPanUnicodeFonts;
        for (var x = family == null ? 0 : -1; x < panUnicode.Length; x++) {
            var file = x < 0 ? family! : panUnicode[x];
            var face = GetTypeface(file, Bold, Italic);
            if (face.HasGlyph(Hint.Value))
                return face;
        }

        // Blink asks DirectWrite here. Coverage alone does not make a font a
        // system fallback; retain the captured no-font outcome as well.
        var priority = emoji ? 2 : Presentation == EmojiPresentation.PRIORITY.TEXT_WITH_VS || EmojiPresentation.IsEmoji(Hint) ? 1 : 0;
        var fallback = SystemFallbacks[SystemFontForCharacter(Hint.Value, Bold, priority, Italic)];
        return fallback != null && fallback.HasGlyph(Hint.Value) ? fallback : null;
    }

    private int SystemFontForCharacter(int Character, bool Bold, int Priority, bool Italic)
    {
        var ranges = SystemFallbackData.Ranges;
        int left = 0, right = ranges.Length - 1;
        while (left <= right) {
            var middle = left + (right - left) / 2;
            var range = ranges[middle];
            if (Character < range.Start)
                right = middle - 1;
            else if (Character > range.End)
                left = middle + 1;
            else
                return (Priority, Italic) switch {
                    (1, true) => Bold ? range.MonoBoldItalic : range.MonoItalic,
                    (2, true) => Bold ? range.EmojiBoldItalic : range.EmojiItalic,
                    (_, true) => Bold ? range.BoldItalic : range.Italic,
                    (1, false) => Bold ? range.MonoBold : range.MonoRegular,
                    (2, false) => Bold ? range.EmojiBold : range.EmojiRegular,
                    _ => Bold ? range.Bold : range.Regular
                };
        }
        return 0;
    }

    // Profiles travel with the supplied fonts, independently of the library build.
    private readonly record struct PlatformMetrics(byte[] Sha256, int Weight, bool Italic, int[] Advances);

    private Dictionary<string, PlatformMetrics> ReadPlatformMetrics()
    {
        using var stream = System.IO.File.OpenRead(Path.Combine(Directory, "WindowsFontMetrics.bin"));
        using var reader = new BinaryReader(stream);
        if (reader.ReadUInt32() != 0x314D4657u || reader.ReadSingle() != Fonts.TWITCH_FONT_SIZE)
            throw new InvalidDataException("Unsupported Windows font metrics.");
        var metrics = new Dictionary<string, PlatformMetrics>(StringComparer.Ordinal);
        var count = reader.ReadInt32();
        for (var index = 0; index < count; index++) {
            var file = reader.ReadString();
            var hash = reader.ReadBytes(32);
            var weight = reader.ReadInt32();
            var italic = reader.ReadBoolean();
            var advances = new int[reader.ReadInt32()];
            for (var glyph = 0; glyph < advances.Length; glyph++)
                advances[glyph] = reader.ReadInt32();
            metrics.Add(file, new(hash, weight, italic, advances));
        }
        return metrics;
    }

    private (SystemFont[], SystemRange[]) ReadSystemFallbacks()
    {
        using var stream = System.IO.File.OpenRead(Path.Combine(Directory, "WindowsSystemFallbacks.bin"));
        using var reader = new BinaryReader(stream);
        var version = reader.ReadUInt32();
        if (version is not (0x32465357u or 0x33465357u))
            throw new InvalidDataException("Unsupported Windows system fallback data.");
        var fonts = new SystemFont[reader.ReadInt32()];
        for (var index = 0; index < fonts.Length; index++)
            fonts[index] = new(reader.ReadString(), reader.ReadInt32(), reader.ReadBoolean());
        var ranges = new SystemRange[reader.ReadInt32()];
        for (var index = 0; index < ranges.Length; index++) {
            var start = reader.ReadInt32(); var end = reader.ReadInt32();
            var regular = reader.ReadUInt16(); var bold = reader.ReadUInt16();
            var monoRegular = reader.ReadUInt16(); var monoBold = reader.ReadUInt16();
            var emojiRegular = reader.ReadUInt16(); var emojiBold = reader.ReadUInt16();
            ranges[index] = version == 0x33465357u
                ? new(start, end, regular, bold, monoRegular, monoBold, emojiRegular, emojiBold,
                    reader.ReadUInt16(), reader.ReadUInt16(), reader.ReadUInt16(), reader.ReadUInt16(), reader.ReadUInt16(), reader.ReadUInt16())
                : new(start, end, regular, bold, monoRegular, monoBold, emojiRegular, emojiBold,
                    regular, bold, monoRegular, monoBold, emojiRegular, emojiBold);
        }
        return (fonts, ranges);
    }
}
