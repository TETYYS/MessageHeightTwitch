using MessageHeightTwitch.Measurement.EmoteRewriting;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;

namespace MessageHeightTwitch.Emotes;

/// <summary>
/// An emote's size and, when it is a modifier, how it changes the emote it attaches to.
/// </summary>
internal readonly record struct CatalogEmote(SizeF Size, EmoteModifier? Modifier = null, SizeF? TokenSize = null);

/// <summary>
/// The emotes of one catalog by name. Measurements read it without locking while loads write to it.
/// </summary>
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
