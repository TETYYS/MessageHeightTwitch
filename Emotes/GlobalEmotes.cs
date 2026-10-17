using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;

namespace MessageHeightTwitch.Emotes;

/// <summary>
/// The global emote catalogs, initialized once independently of channel lifetimes.
/// Every channel in the process shares the resulting catalogs and image sizes.
/// </summary>
/// <remarks>
/// Cache usage: by default one process-wide instance holds three global EmoteSets, FFZ emoji names and
/// user-set mappings, in-flight catalog tasks by URL and successful image-size tasks by URL. Catalog tasks
/// are removed on completion; image sizes and merged global emotes have no eviction or fixed limit. Storage
/// grows with names, user sets and successful image URLs; image pixels are not cached.
/// </remarks>
internal sealed class GlobalEmotes
{
    internal static readonly GlobalEmotes Shared = new();

    private readonly Lock InitLock = new();
    private Task? BttvInitialization;
    private Task? FfzInitialization;
    private Task? SevenTvInitialization;
    internal bool IsInitialized(bool Enable7Tv) => Volatile.Read(ref BttvInitialization) != null &&
        Volatile.Read(ref FfzInitialization) != null && (!Enable7Tv || Volatile.Read(ref SevenTvInitialization) != null);

    internal Task GetInitialization(bool Enable7Tv)
    {
        lock (InitLock) {
            if (!IsInitialized(Enable7Tv))
                throw new InvalidOperationException("Call Init with the requested emote providers enabled before constructing a channel.");
            return Task.WhenAll(BttvInitialization!, FfzInitialization!, Enable7Tv ? SevenTvInitialization! : Task.CompletedTask);
        }
    }

    internal Task Initialize(bool Enable7Tv, CancellationToken Token, System.Net.Http.HttpClient? Client = null)
    {
        Token.ThrowIfCancellationRequested();
        lock (InitLock) {
            var loading = new EmoteLoading(Client, this);
            Start(ref BttvInitialization, () => new BTTVEmoteProvider(loading).InitializeGlobal(Token));
            Start(ref FfzInitialization, () => new FFZEmoteProvider(loading).InitializeGlobal(Token));
            if (Enable7Tv)
                Start(ref SevenTvInitialization, () => new SevenTVEmoteProvider(loading).InitializeGlobal(Token));
            return GetInitialization(Enable7Tv).WaitAsync(Token);
        }
    }

    private static void Start(ref Task? Initialization, Func<Task> Load)
    {
        if (Initialization == null || Initialization.IsFaulted || Initialization.IsCanceled)
            Initialization = Load();
    }

    internal readonly EmoteSet Bttv = new();
    internal readonly EmoteSet Ffz = new();
    internal readonly EmoteSet SevenTv = new();

    // Requests every channel joins, by URL: parsed catalogs while in flight, and image sizes for good once loaded.
    internal readonly ConcurrentDictionary<string, Task<object>> Catalogs = new(StringComparer.Ordinal);
    internal readonly ConcurrentDictionary<string, Task<SizeF>> ImageSizes = new(StringComparer.Ordinal);

    private HashSet<string> FFZEmojis = new(StringComparer.Ordinal);

    // FFZ sets granted to listed users (such as Subwoofer Emote Effects), by login. Replaced whole on each load.
    private Dictionary<string, EmoteSet[]> FFZUserSets = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The emoji FFZ replaces with its images; empty until its catalog loads
    /// </summary>
    internal HashSet<string> FfzEmojis
    {
        get => Volatile.Read(ref FFZEmojis);
        set => Volatile.Write(ref FFZEmojis, value);
    }

    /// <summary>
    /// The FFZ sets granted to the sender with this login
    /// </summary>
    internal EmoteSet[]? FfzUserSetsFor(ReadOnlySpan<char> Login)
    {
        if (!Volatile.Read(ref FFZUserSets).TryGetAlternateLookup<ReadOnlySpan<char>>(out var lookup))
            throw new InvalidOperationException("FFZ user sets do not support span lookup.");
        return lookup.TryGetValue(Login, out var sets) ? sets : null;
    }

    internal void SetFfzUserSets(Dictionary<string, EmoteSet[]> ByLogin) => Volatile.Write(ref FFZUserSets, ByLogin);
}
