using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Mht = MessageHeightTwitch.MessageHeightTwitch;

namespace Tests;

internal static class MeasurementTestData
{
    internal static List<T> Read<T>(string FileName)
    {
        using var stream = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", FileName));
        var cases = JsonSerializer.Deserialize<List<T>>(stream);
        if (cases == null || cases.Count == 0)
            throw new InvalidDataException($"No test cases found in {FileName}.");
        return cases;
    }
}

/*
 * A message text measured in production Chrome by Tools/BrowserProbe/measure-texts.cjs: the text is a message of
 * tetyys (display name TETYYS) with BadgeCount badges in the 293px chat. Offsets are UTF-16 units and widths are DOM
 * Range widths in CSS px. Cases without Prefixes may carry a height captured with emotes, emoji images, display name,
 * name format or margins, or isolated modifier-layout, shaping or cached break-search expectations.
 */
internal sealed class TextMeasurementCase
{
    public required string Name { get; init; }

    public required string Text { get; init; }

    // /me body without the command: a space separator and italic text, with bold italic mentions.
    public bool IsAction { get; init; }

    public int BadgeCount { get; init; }

    // The login is always tetyys; the FFZ name format the case was measured with shows the display name.
    public string DisplayName { get; init; } = "TETYYS";

    [JsonConverter(typeof(JsonStringEnumConverter<Mht.LOCALIZED_NAME_MODE>))]
    public Mht.LOCALIZED_NAME_MODE NameMode { get; init; } = Mht.LOCALIZED_NAME_MODE.USERNAME_DISPLAY_NAME;

    public bool LineMargin { get; init; } = true;

    public bool WholeMargin { get; init; } = true;

    // The FFZ emoji the browser replaced with images, and the emotes it rendered, with their sizes.
    public string[]? FfzEmojis { get; init; }

    public CapturedEmote[]? Emotes { get; init; }

    // The message's height, when the capture measured it.
    public float? Height { get; init; }

    // [End, Width] of text[..End) at every grapheme boundary, on one line wide enough for the whole message.
    public float[][]? Prefixes { get; init; }

    // Compare cached widths with fresh shaping at every UTF-16 offset, including inside clusters.
    public bool CompareAllPrefixes { get; init; }

    // [Start, End, Width] of other ranges on that line.
    public float[][]? Ranges { get; init; }

    // Line-layout widths [End, Width], after advancing each preceding inline script context.
    // Complete plain ASCII fragments can reuse their captured browser width; interior DOM prefixes cannot.
    public float[][]? ShapedWidths { get; init; }

    public string[]? ScriptContext { get; init; }

    // Compare cached and fresh break searches at these available widths, for words and graphemes.
    public float[]? BreakSearchWidths { get; init; }

    // UTF-16 ends of the browser's text nodes, including same-weight link boundaries.
    public int[]? TextNodeEnds { get; init; }

    // [End, Width] of each line of a message that wraps or starts below the name (FreshLine).
    public float[][]? Lines { get; init; }

    public bool FreshLine { get; init; }

    // How far the message moves right when it starts with characters in the cluster of the separator's space.
    public float SeparatorOffset { get; init; }

    // The font that classification picks for the whole text, checked without the browser.
    public string? ExpectedFontFamily { get; init; }

    // The text after FFZ consumes modifier tokens, and the measured boxes of the resulting emotes.
    public string? RenderedText { get; init; }

    public PreparedEmote[]? ModifierLayouts { get; init; }

    /// <summary>The library's parameters for the message the browser measured, with the Twitch emotes of its catalog.</summary>
    internal Mht.CalculateMessageHeightParams Parameters(Dictionary<string, string>? TwitchEmotes)
    {
        var parameters = Mht.CalculateMessageHeightParams.Default;
        parameters.NumberOfBadges = BadgeCount;
        parameters.TwitchEmotes = TwitchEmotes!;
        parameters.LineMargin = LineMargin;
        parameters.WholeMargin = WholeMargin;
        parameters.LocalizedNameMode = NameMode;
        parameters.IsAction = IsAction;
        return parameters;
    }
}

internal sealed class PreparedEmote
{
    public int Offset { get; init; }
    public string Text { get; init; } = "";
    public float Width { get; init; }
    public float Height { get; init; }
}
