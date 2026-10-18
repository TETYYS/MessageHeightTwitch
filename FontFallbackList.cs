using MessageHeightTwitch.Measurement.Shared.Shaping;

using System;
using System.Collections.Generic;
using System.Linq;

namespace MessageHeightTwitch;

/// <summary>
/// The fixed CSS family stack and its unicode-range candidates, backed by the process font cache.
/// </summary>
/// <remarks>
/// Cache usage: two process-shared AsciiFastPath tables (about 129 KiB of array payload), eight CSS subset
/// descriptors and three full-Unicode descriptors, plus ordering lists and range arrays.
/// </remarks>
internal sealed class FontFallbackList
{
    internal static FontFallbackList Shared => FontGlobalContext.Shared.FallbackList;
    internal readonly FontCache Cache;

    internal readonly AsciiFastPath AsciiRegular;
    internal readonly AsciiFastPath AsciiBold;

    // CSS unicode-range candidates in family order; shaping decides coverage.
    internal readonly List<FontDesc> FontPriority = new() {
        new FontDesc([0x0000..0x00ff, 0x0131..0x0131, 0x0152..0x0153, 0x02bb..0x02bc, 0x02c6..0x02c6, 0x02da..0x02da, 0x02dc..0x02dc, 0x2000..0x206f, 0x2074..0x2074, 0x20ac..0x20ac, 0x2122..0x2122, 0x2191..0x2191, 0x2193..0x2193, 0x2212..0x2212, 0x2215..0x2215, 0xfeff..0xfeff, 0xfffd..0xfffd], "Inter-400-latin.ttf", "Inter-700-latin.ttf"),
        new FontDesc([0x0100..0x024f, 0x0259..0x0259, 0x1e00..0x1eff, 0x2020..0x2020, 0x20a0..0x20ab, 0x20ad..0x20cf, 0x2113..0x2113, 0x2c60..0x2c7f, 0xa720..0xa7ff], "Inter-400-latin-ext.ttf", "Inter-700-latin-ext.ttf"),
        new FontDesc([0x0102..0x0103, 0x0110..0x0111, 0x0128..0x0129, 0x0168..0x0169, 0x01a0..0x01a1, 0x01af..0x01b0, 0x1ea0..0x1ef9, 0x20ab..0x20ab], "Inter-400-vietnamese.ttf", "Inter-700-vietnamese.ttf"),
        new FontDesc([0x0370..0x03ff], "Inter-400-greek.ttf", "Inter-700-greek.ttf"),
        new FontDesc([0x1f00..0x1fff], "Inter-400-greek-ext.ttf", "Inter-700-greek-ext.ttf"),
        new FontDesc([0x0400..0x045f, 0x0490..0x0491, 0x04b0..0x04b1, 0x2116..0x2116], "Inter-400-cyrillic.ttf", "Inter-700-cyrillic.ttf"),
        new FontDesc([0x0460..0x052f, 0x1c80..0x1c88, 0x20b4..0x20b4, 0x2de0..0x2dff, 0xa640..0xa69f, 0xfe2e..0xfe2f], "Inter-400-cyrillic-ext.ttf", "Inter-700-cyrillic-ext.ttf"),
        new FontDesc([0x0600..0x06ff, 0x0750..0x077f, 0x0870..0x089f, 0x08a0..0x08ff], "NotoSansArabic-Regular.ttf", "NotoSansArabic-Bold.ttf")
    };

    // Twitch's font stack after Roobert lists Helvetica Neue and Helvetica, which are not bundled.
    internal readonly List<FontDesc> FullUnicodeFontPriority = new() {
        new FontDesc([0x0000..Int32.MaxValue], "Roobert-Regular.ttf", "Roobert-Bold.ttf"),
        new FontDesc([0x0000..Int32.MaxValue], "Arial-Regular.ttf", "Arial-Bold.ttf"),
        /*
         * FontFallbackList::GetFontData appends the preferred standard
         * font after CSS families, before asking for system fallback.
         */
        new FontDesc([0x0000..Int32.MaxValue], "TimesNewRoman-Regular.ttf", "TimesNewRoman-Bold.ttf")
    };

    internal FontFallbackList(FontCache Cache)
    {
        this.Cache = Cache;
        foreach (var font in FontPriority.Concat(FullUnicodeFontPriority)) {
            font.TypeFace = Cache.GetTypeface(font.Font);
            font.BoldTypeFace = Cache.GetTypeface(font.BoldFont, Bold: true);
            font.ItalicTypeFace = Cache.GetTypeface(font.Font, Italic: true);
            font.BoldItalicTypeFace = Cache.GetTypeface(font.BoldFont, Bold: true, Italic: true);
        }

        // CSS fonts with a unicode-range are shaped with a cmap limited to it.
        foreach (var font in FontPriority) {
            FontCache.GetShaper(font.TypeFace).LimitToUnicodeRanges(font.Ranges);
            FontCache.GetShaper(font.BoldTypeFace).LimitToUnicodeRanges(font.Ranges);
        }

        /*
         * FontPriority[0] is the bundled Inter Latin subset in both weights. Reordering or replacing fonts
         * requires revisiting this selection and the Inter-specific assumptions in AsciiFastPath.
         */
        var latin = FontPriority[0];
        AsciiRegular = AsciiFastPath.Create(latin.TypeFace, FontCache.GetShaper(latin.TypeFace));
        AsciiBold = AsciiFastPath.Create(latin.BoldTypeFace, FontCache.GetShaper(latin.BoldTypeFace));
    }
}
