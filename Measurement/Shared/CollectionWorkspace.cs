using MessageHeightTwitch.Measurement.EmoteRewriting;
using MessageHeightTwitch.Measurement.InlineCollection;
using MessageHeightTwitch.Measurement.Shared;

using System;
using System.Buffers;
using System.Collections.Generic;

namespace MessageHeightTwitch.Measurement.Shared;

/// <summary>
/// Scratch for stage 1 (emote rewriting) and stage 2 (inline collection), owned by <see cref="MeasurementWorkspace"/>.
/// Holds <see cref="ModifierRewriter"/> and the collection buffers used by <see cref="ChatParagraphBuilder"/>.
/// </summary>
/// <remarks>
/// Cache usage: per measurement workspace, two lists initially sized for 512 objects/items, a rented login
/// suffix buffer of at least 28 chars (56 bytes), and one ModifierRewriter. Lists retain their largest capacity;
/// Dispose returns the suffix and modifier text arrays to their shared pools.
/// </remarks>
internal sealed class CollectionWorkspace : IDisposable
{
    internal readonly ModifierRewriter Modifiers = new();
    internal char[] LoginSuffixText = ArrayPool<char>.Shared.Rent(" ()".Length + MessageHeightTwitch.MAX_NAME_RUNES);
    internal readonly List<(int Start, EmoteLayout Image)> Objects = new(512);
    internal readonly List<(int Start, int End, bool Bold)> Items = new(512);

    public void Dispose()
    {
        Modifiers.Dispose();
        ArrayPool<char>.Shared.Return(LoginSuffixText);
    }
}
