using SkiaSharp;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization.Metadata;
using System.Threading;
using System.Threading.Tasks;

namespace MessageHeightTwitch.Emotes;

/// <summary>
/// Fetches one channel's emote resources
/// </summary>
internal sealed class EmoteLoading
{
    private static readonly HttpClient SharedClient = new() { Timeout = Timeout.InfiniteTimeSpan };

    internal readonly HttpClient Client;
    internal readonly GlobalEmotes Globals;
    internal EmoteLoading(HttpClient? Client = null, GlobalEmotes? Globals = null)
    {
        this.Client = Client ?? SharedClient;
        this.Globals = Globals ?? GlobalEmotes.Shared;
    }

    /// <summary>
    /// Downloads and parses a JSON document. A 404 returns <paramref name="NotFound"/> when given; JSON null
    /// throws with <paramref name="Missing"/>.
    /// </summary>
    internal async Task<T> FetchJson<T>(string Url, JsonTypeInfo<T> Type, string Missing, CancellationToken Token, T? NotFound = null) where T : class
    {
        try {
            return await Client.GetFromJsonAsync(Url, Type, Token).ConfigureAwait(false) ?? throw new InvalidDataException(Missing);
        } catch (HttpRequestException ex) when (NotFound != null && ex.StatusCode == HttpStatusCode.NotFound) {
            return NotFound;
        }
    }

    /// <summary>
    /// A global catalog read by <paramref name="Fetch"/>. Channels initializing together join one request, and a
    /// later request fetches a fresh copy.
    /// </summary>
    internal async Task<T> GlobalCatalog<T>(string Url, Func<CancellationToken, Task<T>> Fetch, CancellationToken Token) where T : class
    {
        return (T)await Join(Globals.Catalogs, Url, async token => (object)await Fetch(token).ConfigureAwait(false), false, Token).ConfigureAwait(false);
    }

    internal bool TryGetImageSize(string Url, out SizeF Size)
    {
        Size = default;
        if (!Globals.ImageSizes.TryGetValue(Url, out var load) || !load.IsCompletedSuccessfully)
            return false;
        Size = load.Result;
        return true;
    }

    internal Task<SizeF> ImageSize(string Url, CancellationToken Token)
    {
        if (Token.IsCancellationRequested)
            return Task.FromCanceled<SizeF>(Token);
        if (Globals.ImageSizes.TryGetValue(Url, out var load) && load.IsCompletedSuccessfully)
            return load;
        return LoadImageSize(Url, Token);
    }

    private Task<SizeF> LoadImageSize(string Url, CancellationToken Token)
    {
        return Join(Globals.ImageSizes, Url, async token => {
            using var stream = await Client.GetStreamAsync(Url, token).ConfigureAwait(false);
            using var codec = SKCodec.Create(stream) ?? throw new InvalidDataException("Unsupported emote image format");
            return new SizeF(codec.Info.Width, codec.Info.Height);
        }, true, Token);
    }

    private async Task<T> Join<T>(ConcurrentDictionary<string, Task<T>> Loads, string Url, Func<CancellationToken, Task<T>> Fetch, bool Keep, CancellationToken Token)
    {
        while (true) {
            Token.ThrowIfCancellationRequested();
            if (!Loads.TryGetValue(Url, out var load)) {
                var started = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
                load = Loads.GetOrAdd(Url, started.Task);
                if (load == started.Task)
                    _ = Run(started);
            }
            try {
                return await load.WaitAsync(Token).ConfigureAwait(false);
            } catch (OperationCanceledException) when (load.IsCanceled && !Token.IsCancellationRequested) { }
        }

        async Task Run(TaskCompletionSource<T> Started)
        {
            try {
                var value = await Fetch(Token).ConfigureAwait(false);
                if (!Keep)
                    Loads.TryRemove(KeyValuePair.Create(Url, Started.Task));
                Started.SetResult(value);
            } catch (OperationCanceledException) when (Token.IsCancellationRequested) {
                Loads.TryRemove(KeyValuePair.Create(Url, Started.Task));
                Started.SetCanceled(Token);
            } catch (Exception ex) {
                Loads.TryRemove(KeyValuePair.Create(Url, Started.Task));
                Started.SetException(ex);
            }
        }
    }
}
