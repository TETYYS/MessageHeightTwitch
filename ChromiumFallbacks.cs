using MessageHeightTwitch.Measurement.Shared.Shaping;

using System.Collections.Generic;

namespace MessageHeightTwitch;

/*
 * Derived from Chromium source, in its family order, limited to the bundled fonts
 */
/// <summary>
/// Chromium's fallback font ordering, limited to the bundled fonts.
/// </summary>
internal static class ChromiumFallbacks
{
    internal static readonly Dictionary<UScriptCode, string> ScriptFonts = new() {
        { UScriptCode.USCRIPT_ARABIC, "Tahoma-Regular.ttf" },
        { UScriptCode.USCRIPT_ARMENIAN, "SegoeUI-Regular.ttf" },
        { UScriptCode.USCRIPT_BENGALI, "NirmalaUI-Regular.ttf" },
        { UScriptCode.USCRIPT_BRAHMI, "SegoeUIHistoric-Regular.ttf" },
        { UScriptCode.USCRIPT_BRAILLE, "SegoeUISymbol-Regular.ttf" },
        { UScriptCode.USCRIPT_BUGINESE, "LeelawadeeUI-Regular.ttf" },
        { UScriptCode.USCRIPT_CANADIAN_ABORIGINAL, "Gadugi-Regular.ttf" },
        { UScriptCode.USCRIPT_CARIAN, "SegoeUIHistoric-Regular.ttf" },
        { UScriptCode.USCRIPT_CHEROKEE, "Gadugi-Regular.ttf" },
        { UScriptCode.USCRIPT_COPTIC, "SegoeUISymbol-Regular.ttf" },
        { UScriptCode.USCRIPT_CUNEIFORM, "SegoeUIHistoric-Regular.ttf" },
        { UScriptCode.USCRIPT_CYPRIOT, "SegoeUIHistoric-Regular.ttf" },
        { UScriptCode.USCRIPT_CYRILLIC, "TimesNewRoman-Regular.ttf" },
        { UScriptCode.USCRIPT_DESERET, "SegoeUISymbol-Regular.ttf" },
        { UScriptCode.USCRIPT_DEVANAGARI, "NirmalaUI-Regular.ttf" },
        { UScriptCode.USCRIPT_EGYPTIAN_HIEROGLYPHS, "SegoeUIHistoric-Regular.ttf" },
        { UScriptCode.USCRIPT_ETHIOPIC, "Ebrima-Regular.ttf" },
        { UScriptCode.USCRIPT_GEORGIAN, "Sylfaen-Regular.ttf" },
        { UScriptCode.USCRIPT_GLAGOLITIC, "SegoeUIHistoric-Regular.ttf" },
        { UScriptCode.USCRIPT_GOTHIC, "SegoeUIHistoric-Regular.ttf" },
        { UScriptCode.USCRIPT_GREEK, "TimesNewRoman-Regular.ttf" },
        { UScriptCode.USCRIPT_GUJARATI, "NirmalaUI-Regular.ttf" },
        { UScriptCode.USCRIPT_GURMUKHI, "NirmalaUI-Regular.ttf" },
        { UScriptCode.USCRIPT_HANGUL, "MalgunGothic-Regular.ttf" },
        { UScriptCode.USCRIPT_HEBREW, "SegoeUI-Regular.ttf" },
        { UScriptCode.USCRIPT_HIRAGANA, "YuGothic-Regular.ttf" },
        { UScriptCode.USCRIPT_IMPERIAL_ARAMAIC, "SegoeUIHistoric-Regular.ttf" },
        { UScriptCode.USCRIPT_INSCRIPTIONAL_PAHLAVI, "SegoeUIHistoric-Regular.ttf" },
        { UScriptCode.USCRIPT_INSCRIPTIONAL_PARTHIAN, "SegoeUIHistoric-Regular.ttf" },
        { UScriptCode.USCRIPT_JAVANESE, "JavaneseText-Regular.ttf" },
        { UScriptCode.USCRIPT_KANNADA, "NirmalaUI-Regular.ttf" },
        { UScriptCode.USCRIPT_KATAKANA, "YuGothic-Regular.ttf" },
        { UScriptCode.USCRIPT_KATAKANA_OR_HIRAGANA, "YuGothic-Regular.ttf" },
        { UScriptCode.USCRIPT_KHAROSHTHI, "SegoeUIHistoric-Regular.ttf" },
        { UScriptCode.USCRIPT_KHMER, "LeelawadeeUI-Regular.ttf" },
        { UScriptCode.USCRIPT_LAO, "LeelawadeeUI-Regular.ttf" },
        { UScriptCode.USCRIPT_LATIN, "TimesNewRoman-Regular.ttf" },
        { UScriptCode.USCRIPT_LISU, "SegoeUI-Regular.ttf" },
        { UScriptCode.USCRIPT_LYCIAN, "SegoeUIHistoric-Regular.ttf" },
        { UScriptCode.USCRIPT_LYDIAN, "SegoeUIHistoric-Regular.ttf" },
        { UScriptCode.USCRIPT_MALAYALAM, "NirmalaUI-Regular.ttf" },
        { UScriptCode.USCRIPT_MEITEI_MAYEK, "NirmalaUI-Regular.ttf" },
        { UScriptCode.USCRIPT_MEROITIC_CURSIVE, "SegoeUIHistoric-Regular.ttf" },
        { UScriptCode.USCRIPT_MONGOLIAN, "MongolianBaiti-Regular.ttf" },
        { UScriptCode.USCRIPT_MYANMAR, "MyanmarText-Regular.ttf" },
        { UScriptCode.USCRIPT_NEW_TAI_LUE, "MicrosoftNewTaiLue-Regular.ttf" },
        { UScriptCode.USCRIPT_NKO, "Ebrima-Regular.ttf" },
        { UScriptCode.USCRIPT_OGHAM, "SegoeUIHistoric-Regular.ttf" },
        { UScriptCode.USCRIPT_OL_CHIKI, "NirmalaUI-Regular.ttf" },
        { UScriptCode.USCRIPT_OLD_ITALIC, "SegoeUIHistoric-Regular.ttf" },
        { UScriptCode.USCRIPT_OLD_PERSIAN, "SegoeUIHistoric-Regular.ttf" },
        { UScriptCode.USCRIPT_OLD_SOUTH_ARABIAN, "SegoeUIHistoric-Regular.ttf" },
        { UScriptCode.USCRIPT_ORIYA, "NirmalaUI-Regular.ttf" },
        { UScriptCode.USCRIPT_ORKHON, "SegoeUIHistoric-Regular.ttf" },
        { UScriptCode.USCRIPT_OSMANYA, "Ebrima-Regular.ttf" },
        { UScriptCode.USCRIPT_PHAGS_PA, "MicrosoftPhagsPa-Regular.ttf" },
        { UScriptCode.USCRIPT_RUNIC, "SegoeUIHistoric-Regular.ttf" },
        { UScriptCode.USCRIPT_SHAVIAN, "SegoeUIHistoric-Regular.ttf" },
        { UScriptCode.USCRIPT_SIMPLIFIED_HAN, "MicrosoftYaHei-Regular.ttf" },
        { UScriptCode.USCRIPT_SINHALA, "NirmalaUI-Regular.ttf" },
        { UScriptCode.USCRIPT_SORA_SOMPENG, "NirmalaUI-Regular.ttf" },
        { UScriptCode.USCRIPT_SYMBOLS, "SegoeUISymbol-Regular.ttf" },
        { UScriptCode.USCRIPT_TAI_LE, "MicrosoftTaiLe-Regular.ttf" },
        { UScriptCode.USCRIPT_TAMIL, "NirmalaUI-Regular.ttf" },
        { UScriptCode.USCRIPT_TELUGU, "NirmalaUI-Regular.ttf" },
        { UScriptCode.USCRIPT_THAANA, "MVBoli-Regular.ttf" },
        { UScriptCode.USCRIPT_THAI, "Tahoma-Regular.ttf" },
        { UScriptCode.USCRIPT_TIBETAN, "MicrosoftHimalaya-Regular.ttf" },
        { UScriptCode.USCRIPT_TIFINAGH, "Ebrima-Regular.ttf" },
        { UScriptCode.USCRIPT_TRADITIONAL_HAN, "MicrosoftJhengHei-Regular.ttf" },
        { UScriptCode.USCRIPT_BOPOMOFO, "MicrosoftJhengHei-Regular.ttf" },
        { UScriptCode.USCRIPT_VAI, "Ebrima-Regular.ttf" },
        { UScriptCode.USCRIPT_YI, "MicrosoftYiBaiti-Regular.ttf" },
    };

    internal static readonly string[] EmojiFonts = ["SegoeUIEmoji-Regular.ttf", "SegoeUISymbol-Regular.ttf"];
    internal static readonly string[] MonoEmojiFonts = ["SegoeUISymbol-Regular.ttf", "SegoeUIEmoji-Regular.ttf"];
    internal const string MathFont = "CambriaMath-Regular.ttf";
    internal static readonly string[] CommonPanUnicodeFonts = ["Tahoma-Regular.ttf", "LucidaSansUnicode-Regular.ttf", "MicrosoftSansSerif-Regular.ttf", "PalatinoLinotype-Regular.ttf", "MSPGothic-Regular.ttf", "simsun-Regular.ttf"];
    internal static readonly string[] CjkPanUnicodeFonts = ["MSPGothic-Regular.ttf", "simsun-Regular.ttf"];
}
