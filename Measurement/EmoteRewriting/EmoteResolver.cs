using MessageHeightTwitch.Emotes;

using System;
using System.Collections.Generic;
using System.Drawing;

using static MessageHeightTwitch.MessageHeightTwitch;

namespace MessageHeightTwitch.Measurement.EmoteRewriting;

/// <summary>
/// Finds what a piece of message text shows as: a Twitch, BTTV, FFZ or 7TV emote, an emote modifier, or an
    /// emoji image. <see cref="ModifierRewriter"/> passes the resolved images on to layout.
/// </summary>
/// <remarks>
/// Cache usage: one fixed process-wide dictionary of legacy Twitch emote sizes; no per-message result cache.
/// Instances retain provider callbacks and catalog references, whose storage belongs to the providers.
/// </remarks>
internal sealed class EmoteResolver
{
    private readonly EmoteLookup BTTV;
    private readonly EmoteLookup FFZ;
    private readonly EmoteLookup SevenTv;
    private readonly TwitchEmoteLookup Twitch;
    private readonly EmojiSupport BTTVEmoji;
    internal readonly EmojiSupport FFZEmoji;

    // Global emotes, which only a measurer that loads a channel's catalogs separates from the channel's own.
    internal EmoteLookup? BTTVGlobal { get; init; }
    internal EmoteLookup? FFZGlobal { get; init; }
    internal EmoteLookup? SevenTvGlobal { get; init; }
    // The loaded catalogs, which also know their modifiers and FFZ's sets granted to senders.
    internal BTTVEmoteProvider? BTTVProvider { get; init; }
    internal FFZEmoteProvider? FFZProvider { get; init; }

    // Sizes of the images BTTV and FFZ substitute for Unicode emoji.
    internal static readonly SizeF BttvEmojiSize = new(23.66f, 20);
    internal static readonly SizeF FfzEmojiSize = new(21, 21);

    // The size of FFZ's token for a Twitch emote it has no WEIRD_EMOTE_SIZES entry for.
    private const float FFZ_TWITCH_TOKEN_SIZE = 28;

    // FFZ's WEIRD_EMOTE_SIZES (src/utilities/constants.ts): the token sizes of legacy Twitch emotes, by emote id.
    private static readonly Dictionary<string, SizeF>.AlternateLookup<ReadOnlySpan<char>> WeirdEmoteSizes = new Dictionary<string, SizeF> {
        ["15"] = new(21, 27), ["16"] = new(22, 27), ["17"] = new(20, 27), ["18"] = new(20, 27), ["22"] = new(19, 27),
        ["25"] = new(25, 28), ["26"] = new(20, 27), ["28"] = new(39, 27), ["30"] = new(29, 27), ["32"] = new(21, 27),
        ["33"] = new(25, 32), ["34"] = new(21, 28), ["36"] = new(36, 30), ["40"] = new(21, 27), ["41"] = new(19, 27),
        ["46"] = new(24, 24), ["47"] = new(24, 24), ["50"] = new(18, 27), ["52"] = new(32, 32), ["65"] = new(40, 30),
        ["66"] = new(20, 27), ["69"] = new(41, 28), ["73"] = new(21, 30), ["74"] = new(24, 30), ["86"] = new(36, 30),
        ["87"] = new(24, 30), ["92"] = new(23, 30), ["244"] = new(24, 30), ["354"] = new(20, 30), ["357"] = new(28, 30),
        ["360"] = new(22, 30), ["483"] = new(20, 18), ["484"] = new(20, 22), ["485"] = new(27, 18), ["486"] = new(21, 32),
        ["487"] = new(15, 32), ["488"] = new(29, 24), ["489"] = new(20, 18), ["490"] = new(20, 18), ["491"] = new(20, 18),
        ["492"] = new(20, 18), ["493"] = new(20, 18), ["494"] = new(20, 18), ["495"] = new(20, 18), ["496"] = new(20, 18),
        ["497"] = new(20, 18), ["498"] = new(20, 18), ["499"] = new(20, 18), ["500"] = new(20, 18), ["501"] = new(20, 18),
        ["1896"] = new(20, 30), ["1898"] = new(26, 28), ["1899"] = new(22, 30), ["1900"] = new(33, 30), ["1901"] = new(24, 28),
        ["1902"] = new(27, 29), ["1904"] = new(24, 30), ["1906"] = new(24, 30)
    }.GetAlternateLookup<ReadOnlySpan<char>>();

    /// <summary>
    /// The size of FFZ's token for the Twitch emote at <paramref name="Url"/> (its CDN image, .../emoticons/v2/{id}/...),
    /// which FFZ draws the emote at when modifiers or effects apply: its WEIRD_EMOTE_SIZES entry, or else 28x28.
    /// </summary>
    internal static SizeF FfzTwitchTokenSize(ReadOnlySpan<char> Url)
    {
        const string EMOTICONS = "/emoticons/";
        var path = Url.IndexOf(EMOTICONS, StringComparison.Ordinal);
        if (path >= 0) {
            // After the API version: the id, up to the next path segment.
            var rest = Url[(path + EMOTICONS.Length)..];
            var version = rest.IndexOf('/');
            if (version >= 0) {
                rest = rest[(version + 1)..];
                var end = rest.IndexOf('/');
                if (WeirdEmoteSizes.TryGetValue(end < 0 ? rest : rest[..end], out var size))
                    return size;
            }
        }
        return new(FFZ_TWITCH_TOKEN_SIZE, FFZ_TWITCH_TOKEN_SIZE);
    }

    internal EmoteResolver(
        EmoteLookup SevenTvGetEmote,
        EmoteLookup BTTVGetEmote,
        EmoteLookup FFZGetEmote,
        TwitchEmoteLookup TwitchGetEmote,
        EmojiSupport BTTVIsEmojiSupported,
        EmojiSupport FFZIsEmojiSupported)
    {
        ArgumentNullException.ThrowIfNull(BTTVGetEmote);
        ArgumentNullException.ThrowIfNull(FFZGetEmote);
        ArgumentNullException.ThrowIfNull(TwitchGetEmote);
        ArgumentNullException.ThrowIfNull(BTTVIsEmojiSupported);
        ArgumentNullException.ThrowIfNull(FFZIsEmojiSupported);
        BTTV = BTTVGetEmote;
        FFZ = FFZGetEmote;
        SevenTv = SevenTvGetEmote;
        Twitch = TwitchGetEmote;
        BTTVEmoji = BTTVIsEmojiSupported;
        FFZEmoji = FFZIsEmojiSupported;
    }

    /// <summary>
    /// Resolves one catalog entry, keeping its size and modifier behavior together. A catalog's null modifier means
    /// an ordinary emote; name-based defaults are used only by callbacks that provide no catalog metadata.
    /// </summary>
    internal bool TryResolveEmote(ReadOnlySpan<char> Name, CalculateMessageHeightParams Parameters, out CatalogEmote Emote, out EMOTE_PROVIDER Provider)
    {
        /*
         * Twitch native emote beats BTTV, FFZ and 7TV emotes with the same name
         */
        Provider = EMOTE_PROVIDER.NONE;
        // No emote has an empty name, such as the one after an emote at the end of a word.
        if (Name.IsEmpty) {
            Emote = default;
            return false;
        }
        if (Parameters.TwitchEmotes != null) {
            if (!Parameters.TwitchEmotes.TryGetAlternateLookup<ReadOnlySpan<char>>(out var lookup))
                throw new ArgumentException("Measurement requires a TwitchEmotes comparer supporting alternate span lookup.", nameof(Parameters));
            if (lookup.TryGetValue(Name, out var url)) {
                Emote = new(Twitch(Name, url), TokenSize: FfzTwitchTokenSize(url));
                return true;
            }
        }
        /*
         * FFZ takes the first emote in its set order: the sender's own sets (sets FFZ grants to listed logins), the
         * room's sets, then the global (default) sets, so any channel emote beats any global one.
         * Within each tier, BetterTTV comes before FFZ and FFZ before 7TV
         */
        Provider = EMOTE_PROVIDER.FFZ;
        foreach (var set in Parameters.SenderEmotes ?? [])
            if (set.TryGet(Name, out Emote))
                return true;
        Provider = EMOTE_PROVIDER.BTTV;
        if (TryResolve(Name, BTTVProvider?.Channel, BTTV, Provider, out Emote))
            return true;
        Provider = EMOTE_PROVIDER.FFZ;
        if (TryResolve(Name, FFZProvider?.Room, FFZ, Provider, out Emote))
            return true;
        Provider = EMOTE_PROVIDER.SEVENTV;
        if (TryResolve(Name, null, SevenTv, Provider, out Emote))
            return true;
        Provider = EMOTE_PROVIDER.BTTV;
        if (TryResolve(Name, BTTVProvider?.Global, BTTVGlobal, Provider, out Emote))
            return true;
        Provider = EMOTE_PROVIDER.FFZ;
        if (TryResolve(Name, FFZProvider?.Global, FFZGlobal, Provider, out Emote))
            return true;
        Provider = EMOTE_PROVIDER.SEVENTV;
        return TryResolve(Name, null, SevenTvGlobal, Provider, out Emote);
    }

    private static bool TryResolve(ReadOnlySpan<char> Name, EmoteSet? Catalog, EmoteLookup? Lookup, EMOTE_PROVIDER Provider, out CatalogEmote Emote)
    {
        if (Catalog != null)
            return Catalog.TryGet(Name, out Emote);
        Emote = default;
        if (Lookup == null || !Lookup(Name, out var size))
            return false;
        Emote = new(size, EmoteModifier.Known(Name, Provider));
        return true;
    }

    /// <summary>The FFZ sets granted to the sender's login, such as Subwoofer Emote Effects.</summary>
    internal EmoteSet[]? SenderEmotes(ReadOnlySpan<char> Login) => FFZProvider?.UserSetsFor(Login);

    /// <summary>
    /// Picks the image provider for an emoji. FFZ falls back to BTTV's image for emoji it lacks.
    /// Returns <see cref="EMOTE_PROVIDER.NONE"/> when neither provider has it, leaving the emoji as text.
    /// </summary>
    internal EMOTE_PROVIDER ResolveEmojiProvider(ReadOnlySpan<char> Emoji, EMOTE_PROVIDER ReplaceMode)
    {
        if (ReplaceMode == EMOTE_PROVIDER.FFZ && !FFZEmoji(Emoji))
            ReplaceMode = EMOTE_PROVIDER.BTTV;
        if (ReplaceMode == EMOTE_PROVIDER.BTTV && !BTTVEmoji(Emoji))
            ReplaceMode = EMOTE_PROVIDER.NONE;
        return ReplaceMode;
    }
}
