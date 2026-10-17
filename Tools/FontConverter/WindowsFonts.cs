using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using SkiaSharp;

internal static class WindowsFonts
{
    /*
     * Regular and bold source files with their collection indexes. Collections are
     * unpacked so runtime font references are ordinary filenames.
     */
    private static readonly (string Family, string Regular, int RegularIndex, string? Bold, int BoldIndex)[] Sources =
    [
        ("Arial", "arial.ttf", 0, "arialbd.ttf", 0),
        ("Calibri", "calibri.ttf", 0, "calibrib.ttf", 0),
        ("Cambria Math", "cambria.ttc", 1, null, 0),
        ("Tahoma", "tahoma.ttf", 0, "tahomabd.ttf", 0),
        ("Arial Unicode MS", "arialuni.ttf", 0, null, 0),
        ("Lucida Sans Unicode", "l_10646.ttf", 0, null, 0),
        ("Microsoft Sans Serif", "micross.ttf", 0, null, 0),
        ("Palatino Linotype", "pala.ttf", 0, "palab.ttf", 0),
        ("Segoe UI", "segoeui.ttf", 0, "segoeuib.ttf", 0),
        ("Segoe UI Emoji", "seguiemj.ttf", 0, null, 0),
        ("Segoe UI Symbol", "seguisym.ttf", 0, null, 0),
        ("Segoe UI Historic", "seguihis.ttf", 0, null, 0),
        ("Sans Serif Collection", "SansSerifCollection.ttf", 0, null, 0),
        ("Nirmala UI", "Nirmala.ttc", 0, "Nirmala.ttc", 1),
        ("Sylfaen", "sylfaen.ttf", 0, null, 0),
        ("Leelawadee UI", "leelawui.ttf", 0, "leelauib.ttf", 0),
        ("Gadugi", "gadugi.ttf", 0, "gadugib.ttf", 0),
        ("Times New Roman", "times.ttf", 0, "timesbd.ttf", 0),
        ("Malgun Gothic", "malgun.ttf", 0, "malgunbd.ttf", 0),
        ("Javanese Text", "javatext.ttf", 0, null, 0),
        ("Yu Gothic", "YuGothR.ttc", 0, "YuGothB.ttc", 0),
        ("Yu Gothic UI", "YuGothR.ttc", 1, "YuGothB.ttc", 1),
        ("MS PGothic", "msgothic.ttc", 2, null, 0),
        ("Microsoft YaHei", "msyh.ttc", 0, "msyhbd.ttc", 0),
        ("Microsoft YaHei UI", "msyh.ttc", 1, "msyhbd.ttc", 1),
        ("Mongolian Baiti", "monbaiti.ttf", 0, null, 0),
        ("Myanmar Text", "mmrtext.ttf", 0, "mmrtextb.ttf", 0),
        ("Microsoft New Tai Lue", "ntailu.ttf", 0, "ntailub.ttf", 0),
        ("Ebrima", "ebrima.ttf", 0, "ebrimabd.ttf", 0),
        ("Microsoft PhagsPa", "phagspa.ttf", 0, "phagspab.ttf", 0),
        ("Microsoft Tai Le", "taile.ttf", 0, "taileb.ttf", 0),
        ("MV Boli", "mvboli.ttf", 0, null, 0),
        ("Microsoft Himalaya", "himalaya.ttf", 0, null, 0),
        ("Microsoft JhengHei", "msjh.ttc", 0, "msjhbd.ttc", 0),
        ("Microsoft JhengHei UI", "msjh.ttc", 1, "msjhbd.ttc", 1),
        ("simsun", "simsun.ttc", 0, null, 0),
        ("SimSun-ExtB", "simsunb.ttf", 0, null, 0),
        ("SimSun-ExtG", "SimsunExtG.ttf", 0, null, 0),
        ("MingLiU-ExtB", "mingliub.ttc", 0, null, 0),
        ("MingLiU_MSCS-ExtB", "mingliub.ttc", 3, null, 0),
        ("Microsoft Yi Baiti", "msyi.ttf", 0, null, 0),
    ];

    private static readonly (string Family, string Italic, string BoldItalic)[] ItalicSources =
    [
        ("Arial", "ariali.ttf", "arialbi.ttf"),
        ("Calibri", "calibrii.ttf", "calibriz.ttf"),
        ("Palatino Linotype", "palai.ttf", "palabi.ttf"),
        ("Segoe UI", "segoeuii.ttf", "segoeuiz.ttf"),
        ("Times New Roman", "timesi.ttf", "timesbi.ttf")
    ];

    internal static List<ManifestEntry> Copy(string Source, string Output)
    {
        if (!Directory.Exists(Source))
            throw new DirectoryNotFoundException($"Font directory {Source} does not exist.");
        Directory.CreateDirectory(Output);
        var entries = new List<ManifestEntry>();
        void copy(string family, string weight, string? name, int index)
        {
            if (name == null)
                return;
            var path = Path.Combine(Source, name);
            if (!File.Exists(path)) {
                Console.WriteLine($"Skipping {family} {weight}: {path} is missing");
                return;
            }
            var file = string.Concat(family.Split(' ')) + "-" + weight + ".ttf";
            var font = Sfnt.ExtractFace(File.ReadAllBytes(path), index);
            File.WriteAllBytes(Path.Combine(Output, file), font);
            entries.Add(new ManifestEntry(file, family, weight, name, index, null, FontFile.Sha256(font)));
        }
        foreach (var (family, regular, regularIndex, bold, boldIndex) in Sources) {
            copy(family, "Regular", regular, regularIndex);
            copy(family, "Bold", bold, boldIndex);
        }
        foreach (var (family, italic, boldItalic) in ItalicSources) {
            copy(family, "Italic", italic, 0);
            copy(family, "BoldItalic", boldItalic, 0);
        }
        WriteSystemFallbacks(Output, entries);
        WritePlatformMetrics(Output, entries);
        return entries;
    }

    /*
     * A DirectWrite simulated face keeps the regular font file but has different
     * glyph advances. Export the selected face's strike metrics as Blink's
     * HarfBuzz callbacks see them, including its embedded-bitmap policy.
     */
    internal static void WritePlatformMetrics(string Output, List<ManifestEntry> Entries)
    {
        const float size = 14;
        const string binary = "WindowsFontMetrics.bin", metadata = "WindowsFontMetrics.json";
        var profiles = new List<(ManifestEntry File, int Weight, bool Italic, int[] Advances)>();
        // The Windows and Twitch steps update their own faces in the same profile.
        var previous = Path.Combine(Output, binary);
        if (File.Exists(previous)) {
            using var reader = new BinaryReader(File.OpenRead(previous));
            if (reader.ReadUInt32() != 0x314D4657u || reader.ReadSingle() != size)
                throw new InvalidDataException("Unsupported platform metrics.");
            var count = reader.ReadInt32();
            for (var index = 0; index < count; index++) {
                var file = reader.ReadString();
                var hash = System.Convert.ToHexStringLower(reader.ReadBytes(32));
                var weight = reader.ReadInt32();
                var italic = reader.ReadBoolean();
                var advances = new int[reader.ReadInt32()];
                for (var glyph = 0; glyph < advances.Length; glyph++) advances[glyph] = reader.ReadInt32();
                if (!Entries.Any(entry => entry.File == file))
                    profiles.Add((new(file, "", "", "", null, null, hash), weight, italic, advances));
            }
        }
        foreach (var entry in Entries.Where(entry => entry.File.EndsWith(".ttf", StringComparison.Ordinal))) {
            var bytes = File.ReadAllBytes(Path.Combine(Output, entry.File));
            var variable = Sfnt.HasTable(bytes, "fvar");
            // Chromium builds Skia with SK_WIN_FONTMGR_NO_SIMULATIONS.
            // FirstMatchingFontWithoutSimulations permits DirectWrite bold
            // simulation only for faces with an EBDT table. Other families
            // retain their regular advances under Skia's synthetic bold.
            if (!variable && (entry.Weight != "Regular" || !Sfnt.HasTable(bytes, "EBDT") ||
                File.Exists(Path.Combine(Output, entry.File.Replace("-Regular.ttf", "-Bold.ttf", StringComparison.Ordinal)))))
                continue;
            var requestedWeight = variable && entry.Weight == "Regular" ? 400 : 700;
            using var sourceFace = variable ? SKTypeface.FromFile(Path.Combine(Output, entry.File)) : null;
            using var face = variable
                ? sourceFace!.Clone([new SKFontVariationPositionCoordinate {Axis = new SKFourByteTag('w', 'g', 'h', 't'), Value = requestedWeight}])
                : SKFontManager.Default.MatchFamily(entry.Family, SKFontStyle.Bold);
            if (face == null) throw new InvalidDataException($"No platform face for {entry.Family}.");
            using var stream = face.OpenStream(out var index);
            using var data = SKData.Create(stream);
            var file = Sfnt.ExtractFace(data.ToArray(), index);
            if (FontFile.Sha256(file) != entry.Sha256)
                throw new InvalidDataException($"Platform glyphs for {entry.Family} do not use the exported font file.");
            using var font = new SKFont(face, size) {
                Subpixel = true, Edging = SKFontEdging.SubpixelAntialias, EmbeddedBitmaps = true
            };
            var glyphs = Enumerable.Range(0, face.GlyphCount).Select(glyph => checked((ushort)glyph)).ToArray();
            var advances = font.GetGlyphWidths(glyphs).Select(width => (int)(width * 65536f)).ToArray();
            profiles.Add((entry, variable ? requestedWeight : face.FontWeight, face.FontSlant != SKFontStyleSlant.Upright, advances));
        }
        using (var writer = new BinaryWriter(File.Create(Path.Combine(Output, binary)))) {
            writer.Write(0x314D4657u); // WFM1
            writer.Write(size);
            writer.Write(profiles.Count);
            foreach (var profile in profiles) {
                writer.Write(profile.File.File);
                writer.Write(System.Convert.FromHexString(profile.File.Sha256));
                writer.Write(profile.Weight);
                writer.Write(profile.Italic);
                writer.Write(profile.Advances.Length);
                foreach (var advance in profile.Advances)
                    writer.Write(advance);
            }
        }
        File.WriteAllText(Path.Combine(Output, metadata), JsonSerializer.Serialize(new {
            schema = "WFM1", os = Environment.OSVersion.VersionString, skia = SkiaSharpVersion.Native,
            size, weights = new[] {400, 700}, subpixel = true, embeddedBitmaps = true, edging = "SubpixelAntialias",
            simulations = "SK_WIN_FONTMGR_NO_SIMULATIONS with the EBDT exception",
            fonts = profiles.Select(profile => new { file = profile.File.File, sha256 = profile.File.Sha256,
                weight = profile.Weight, italic = profile.Italic, glyphs = profile.Advances.Length })
        }, ManifestEntry.Json) + "\n");
        foreach (var file in new[] { binary, metadata })
            Entries.Add(new ManifestEntry(file, "Windows platform metrics", "400/700", "Skia/DirectWrite selected-face glyph advances at 14px",
                null, null, FontFile.Sha256(File.ReadAllBytes(Path.Combine(Output, file)))));
        Console.WriteLine($"Captured platform advances for {profiles.Count} selected faces.");
    }

    // Capture Blink's final GetDWriteFallbackFamily query, including no-font results.
    private static void WriteSystemFallbacks(string Output, List<ManifestEntry> Entries)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Windows font fallback must be captured on Windows.");
        var timer = Stopwatch.StartNew();
        var fonts = new List<SystemFont> { new("", 0, false) };
        var indexes = new Dictionary<SystemFont, ushort>();
        var ranges = new List<(int Start, int End, Selection Fonts)>();
        using var regularStyle = new SKFontStyle(SKFontStyleWeight.Normal, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright);
        using var boldStyle = new SKFontStyle(SKFontStyleWeight.Bold, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright);
        using var italicStyle = new SKFontStyle(SKFontStyleWeight.Normal, SKFontStyleWidth.Normal, SKFontStyleSlant.Italic);
        using var boldItalicStyle = new SKFontStyle(SKFontStyleWeight.Bold, SKFontStyleWidth.Normal, SKFontStyleSlant.Italic);
        // LayoutLocale::LocaleForSkFontMgr removes the English region subtag.
        string[] locales = ["en"];
        // FallbackLocaleForCharacter disambiguates Han before the DWrite call.
        // At the pinned Blink revision kChineseSimplified actually contains
        // "zh-Hant"; preserve the implementation rather than its comment.
        string[] hanLocales = ["zh-Hant"];
        string[] monoEmojiLocales = ["und-Zsym"], colorEmojiLocales = ["und-Zsye"];

        ushort capture(int Character, SKFontStyle Style, string[] Locales)
        {
            using var face = SKFontManager.Default.MatchCharacter("Inter", Style, Locales, Character);
            if (face == null)
                return 0;
            using var font = new SKFont(face);
            if (font.GetGlyph(Character) == 0)
                return 0;
            var key = new SystemFont(face.FamilyName, face.FontWeight, face.FontSlant != SKFontStyleSlant.Upright);
            if (!indexes.TryGetValue(key, out var index)) {
                index = checked((ushort)fonts.Count);
                indexes.Add(key, index);
                fonts.Add(key);
            }
            return index;
        }

        for (var character = 0; character <= 0x10FFFF; character++) {
            Selection selection = default;
            if (Rune.IsValid(character) && Rune.GetUnicodeCategory(new Rune(character)) != UnicodeCategory.PrivateUse &&
                character is not (>= 0xFDD0 and <= 0xFDEF) && (character & 0xFFFE) != 0xFFFE) {
                var characterLocales = HarfBuzzSharp.UnicodeFunctions.Default.GetScript(character) == HarfBuzzSharp.Script.Han
                    ? hanLocales : locales;
                selection = new(capture(character, regularStyle, characterLocales), capture(character, boldStyle, characterLocales),
                    capture(character, regularStyle, monoEmojiLocales), capture(character, boldStyle, monoEmojiLocales),
                    capture(character, regularStyle, colorEmojiLocales), capture(character, boldStyle, colorEmojiLocales),
                    capture(character, italicStyle, characterLocales), capture(character, boldItalicStyle, characterLocales),
                    capture(character, italicStyle, monoEmojiLocales), capture(character, boldItalicStyle, monoEmojiLocales),
                    capture(character, italicStyle, colorEmojiLocales), capture(character, boldItalicStyle, colorEmojiLocales));
            }
            if (ranges.Count > 0 && ranges[^1].Fonts == selection) {
                var previous = ranges[^1];
                ranges[^1] = (previous.Start, character, selection);
            } else {
                ranges.Add((character, character, selection));
            }
            if ((character & 0xFFFF) == 0xFFFF)
                Console.WriteLine($"System fallback U+{character:X}: {timer.Elapsed.TotalSeconds:F1}s, {ranges.Count} ranges");
        }

        const string binary = "WindowsSystemFallbacks.bin", metadata = "WindowsSystemFallbacks.json";
        using (var writer = new BinaryWriter(File.Create(Path.Combine(Output, binary)))) {
            writer.Write(0x33465357u); // WSF3: both weights and styles at all presentation priorities.
            writer.Write(fonts.Count);
            foreach (var font in fonts) {
                writer.Write(font.Family);
                writer.Write(font.Weight);
                writer.Write(font.Italic);
            }
            writer.Write(ranges.Count);
            foreach (var range in ranges) {
                writer.Write(range.Start);
                writer.Write(range.End);
                writer.Write(range.Fonts.Regular);
                writer.Write(range.Fonts.Bold);
                writer.Write(range.Fonts.MonoRegular);
                writer.Write(range.Fonts.MonoBold);
                writer.Write(range.Fonts.EmojiRegular);
                writer.Write(range.Fonts.EmojiBold);
                writer.Write(range.Fonts.Italic);
                writer.Write(range.Fonts.BoldItalic);
                writer.Write(range.Fonts.MonoItalic);
                writer.Write(range.Fonts.MonoBoldItalic);
                writer.Write(range.Fonts.EmojiItalic);
                writer.Write(range.Fonts.EmojiBoldItalic);
            }
        }
        File.WriteAllText(Path.Combine(Output, metadata), JsonSerializer.Serialize(new {
            schema = "WSF3", os = Environment.OSVersion.VersionString, skia = SkiaSharpVersion.Native,
            family = "Inter", contentLocale = "en-US", locale = "en", hanLocale = "zh-Hant",
            monoEmojiLocale = "und-Zsym", colorEmojiLocale = "und-Zsye", weights = new[] { 400, 700 },
            styles = new[] { "normal", "italic" },
            fonts, ranges = ranges.Select(r => new[] { r.Start, r.End, r.Fonts.Regular, r.Fonts.Bold,
                r.Fonts.MonoRegular, r.Fonts.MonoBold, r.Fonts.EmojiRegular, r.Fonts.EmojiBold,
                r.Fonts.Italic, r.Fonts.BoldItalic, r.Fonts.MonoItalic, r.Fonts.MonoBoldItalic, r.Fonts.EmojiItalic, r.Fonts.EmojiBoldItalic })
        }, ManifestEntry.Json) + "\n");
        foreach (var file in new[] { binary, metadata })
            Entries.Add(new ManifestEntry(file, "Windows system fallback", "400/700", "Skia/DirectWrite en; Han zh-Hant; emoji und-Zsym/und-Zsye", null, null,
                FontFile.Sha256(File.ReadAllBytes(Path.Combine(Output, file)))));
        Console.WriteLine($"Captured {fonts.Count - 1} system faces in {ranges.Count} ranges ({timer.Elapsed.TotalSeconds:F1}s).");
    }

    private sealed record SystemFont(string Family, int Weight, bool Italic);
    private readonly record struct Selection(ushort Regular, ushort Bold, ushort MonoRegular, ushort MonoBold,
        ushort EmojiRegular, ushort EmojiBold, ushort Italic, ushort BoldItalic,
        ushort MonoItalic, ushort MonoBoldItalic, ushort EmojiItalic, ushort EmojiBoldItalic);
}
