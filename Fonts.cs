namespace MessageHeightTwitch;

/// <summary>
/// Public font-size constant for the fixed Twitch chat style.
/// </summary>
public static class Fonts
{
    public const float TWITCH_FONT_SIZE = 14.0f;
}

/// <summary>
/// The CSS font selection for an inline item: fallback families, weight and style.
/// </summary>
internal readonly record struct Font(FontFallbackList FallbackList, bool Bold, bool Italic = false);
