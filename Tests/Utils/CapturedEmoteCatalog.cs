using System;
using System.Collections.Generic;
using System.Drawing;
using MessageHeightTwitch;
using Mht = MessageHeightTwitch.MessageHeightTwitch;

namespace Tests;

public sealed class CapturedEmote
{
    public string Name { get; init; } = "";
    public string Provider { get; init; } = "";
    // A Twitch emote's id, which its CDN URL carries (FFZ sizes a modified Twitch emote by it).
    public string? Id { get; init; }
    public float Width { get; init; }
    public float Height { get; init; }
    public bool Modifier { get; init; }
    public bool Prefix { get; init; }
    public int Flags { get; init; }
}

// Dimensions and metadata only: constructing or using this catalog never performs I/O.
internal sealed class CapturedEmoteCatalog
{
    private readonly Dictionary<string, CapturedEmote> SevenTv = new(StringComparer.Ordinal);
    private readonly Dictionary<string, CapturedEmote> Bttv = new(StringComparer.Ordinal);
    private readonly Dictionary<string, CapturedEmote> Ffz = new(StringComparer.Ordinal);
    private readonly Dictionary<string, CapturedEmote> Twitch = new(StringComparer.Ordinal);
    internal Dictionary<string, string> TwitchEmotes { get; } = new(StringComparer.Ordinal);

    internal CapturedEmoteCatalog(IEnumerable<CapturedEmote> Entries)
    {
        foreach (var entry in Entries) {
            var map = entry.Provider switch { "7tv" => SevenTv, "bttv" => Bttv, "twitch" => Twitch, _ => Ffz };
            map.Add(entry.Name, entry);
            if (entry.Provider == "twitch")
                TwitchEmotes.Add(entry.Name, entry.Id is { } id ? $"https://static-cdn.jtvnw.net/emoticons/v2/{id}/default/dark/1.0" : "captured");
        }
    }

    private static bool Lookup(Dictionary<string, CapturedEmote> Map, ReadOnlySpan<char> Name, out SizeF Size)
    {
        if (!Map.GetAlternateLookup<ReadOnlySpan<char>>().TryGetValue(Name, out var emote)) {
            Size = default;
            return false;
        }
        Size = emote.Provider == "7tv" && emote.Modifier ? new(-emote.Width, -emote.Height) : new(emote.Width, emote.Height);
        return true;
    }

    internal Mht CreateMeasurer(IEnumerable<string>? Emojis = null)
    {
        var supported = new HashSet<string>(Emojis ?? [], StringComparer.Ordinal).GetAlternateLookup<ReadOnlySpan<char>>();
        return new Mht(
            (ReadOnlySpan<char> name, out SizeF size) => Lookup(SevenTv, name, out size),
            (ReadOnlySpan<char> name, out SizeF size) => Lookup(Bttv, name, out size),
            (ReadOnlySpan<char> name, out SizeF size) => Lookup(Ffz, name, out size),
            (name, _) => { Lookup(Twitch, name, out var size); return size; },
            _ => false, supported.Contains);
    }
}
