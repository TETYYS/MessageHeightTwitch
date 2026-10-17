using MessageHeightTwitch.Measurement.EmoteRewriting;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;

namespace MessageHeightTwitch.Emotes;

/// <summary>An emote's size and, when it is a modifier, how it changes the emote it attaches to.</summary>
/*
 * An emote's drawn size, its modifier metadata, and the size of its FFZ token when that differs from the drawn size:
 * FFZ draws an unmodified emote at its image's size, but a modified one at its token's (a Twitch emote's
 * WEIRD_EMOTE_SIZES entry, or 28x28).
 */
internal readonly record struct CatalogEmote(SizeF Size, EmoteModifier? Modifier = null, SizeF? TokenSize = null);

/// <summary>The emotes of one catalog by name. Measurements read it without locking while loads write to it.</summary>
/// <remarks>
/// Cache usage: one concurrent dictionary entry per distinct name, retaining its string and CatalogEmote
/// value plus dictionary overhead. No size limit or eviction; Set replaces a value and TryAdd preserves it.
/// Global sets live for the process; channel and user sets live with their owners.
/// </remarks>
internal sealed class EmoteSet
{
    private readonly ConcurrentDictionary<string, CatalogEmote> Emotes = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, CatalogEmote>.AlternateLookup<ReadOnlySpan<char>> Lookup;

    internal EmoteSet() => Lookup = Emotes.GetAlternateLookup<ReadOnlySpan<char>>();

    internal int Count => Emotes.Count;

    internal CatalogEmote this[string Name] => Emotes[Name];

    internal IEnumerable<string> Names => Emotes.Keys;

    internal bool TryGet(ReadOnlySpan<char> Name, out CatalogEmote Emote) => Lookup.TryGetValue(Name, out Emote);

    internal bool TryGetSize(ReadOnlySpan<char> Name, out SizeF Size)
    {
        var found = Lookup.TryGetValue(Name, out var emote);
        Size = emote.Size;
        return found;
    }

    internal void Set(string Name, CatalogEmote Emote) => Emotes[Name] = Emote;

    internal void TryAdd(string Name, CatalogEmote Emote) => Emotes.TryAdd(Name, Emote);
}
