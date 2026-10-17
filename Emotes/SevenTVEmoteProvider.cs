using System;
using System.Collections.Generic;
using System.Drawing;
using System.Net;
using System.Net.Http;
using System.IO;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace MessageHeightTwitch.Emotes;

/// <summary>
/// 7TV v3 catalog sizes at 1x. Negative sizes identify suffix overlays.
/// </summary>
public sealed partial class SevenTVEmoteProvider : IEmoteProvider
{
    private readonly EmoteLoading Loading;
    internal readonly EmoteSet Channel = new();
    internal EmoteSet Global => Loading.Globals.SevenTv;

    private sealed class SevenTVEmoteSet
    {
        public List<SevenTVEmote>? Emotes { get; set; } = [];
    }

    private sealed class SevenTVChannelResponse
    {
        public SevenTVEmoteSet? EmoteSet { get; set; }
    }

    private sealed class SevenTVEmote
    {
        public string? Name { get; set; }
        public int Flags { get; set; }
        public SevenTVEmoteData? Data { get; set; }
    }

    private sealed class SevenTVEmoteData
    {
        public SevenTVHost? Host { get; set; }
    }

    private sealed class SevenTVHost
    {
        public List<SevenTVFile>? Files { get; set; }
    }

    private sealed class SevenTVFile
    {
        public string? Name { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
    }

    [JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
    [JsonSerializable(typeof(SevenTVEmoteSet))]
    [JsonSerializable(typeof(SevenTVChannelResponse))]
    private partial class SevenTVJsonContext : JsonSerializerContext { }

    internal SevenTVEmoteProvider(EmoteLoading? Loading = null) => this.Loading = Loading ?? new EmoteLoading();

    public bool TryGetEmote(ReadOnlySpan<char> Name, out SizeF Size)
    {
        var found = Channel.TryGet(Name, out var emote) || Global.TryGet(Name, out emote);
        Size = emote.Size;
        return found;
    }

    public bool IsEmojiSupported(ReadOnlySpan<char> Emoji) => false;

    public Task Initialize(string ChannelId, CancellationToken Token)
        => Task.WhenAll(InitializeGlobal(Token), InitializeChannel(ChannelId, Token));

    internal async Task InitializeGlobal(CancellationToken Token)
    {
        var globalUrl = "https://7tv.io/v3/emote-sets/global";
        Apply(await Loading.GlobalCatalog(globalUrl, async shared => (await Fetch(globalUrl, false, shared).ConfigureAwait(false))!, Token).ConfigureAwait(false), Global);
    }

    internal async Task InitializeChannel(string ChannelId, CancellationToken Token)
    {
        var channelUrl = "https://7tv.io/v3/users/twitch/" + Uri.EscapeDataString(ChannelId);
        Apply(await Fetch(channelUrl, true, Token).ConfigureAwait(false), Channel);
    }

    /// <summary>
    /// Null only for a channel without a 7TV set.
    /// </summary>
    private async Task<SevenTVEmoteSet?> Fetch(string Url, bool FromChannel, CancellationToken Token)
    {
        SevenTVEmoteSet? set;
        try {
            set = FromChannel ?
                (await Loading.Client.GetFromJsonAsync(Url, SevenTVJsonContext.Default.SevenTVChannelResponse, Token).ConfigureAwait(false))?.EmoteSet :
                await Loading.Client.GetFromJsonAsync(Url, SevenTVJsonContext.Default.SevenTVEmoteSet, Token).ConfigureAwait(false);
        } catch (HttpRequestException ex) when (FromChannel && ex.StatusCode == HttpStatusCode.NotFound) {
            return null;
        }

        if (set is null)
            return FromChannel ? null : throw new InvalidDataException("Missing 7TV global emote set");
        _ = set.Emotes ?? throw new InvalidDataException("Missing 7TV emote list");
        return set;
    }

    private static void Apply(SevenTVEmoteSet? Set, EmoteSet Target)
    {
        foreach (var emote in Set?.Emotes ?? []) {
            if (emote.Name is not { } name)
                continue;
            foreach (var file in emote.Data?.Host?.Files ?? []) {
                if (file.Name is not ("1x.webp" or "1x.avif" or "1x.png" or "1x.gif") || file.Width <= 0 || file.Height <= 0)
                    continue;
                var overlay = (emote.Flags & 1) != 0;
                Target.Set(name, new(overlay ? new(-file.Width, -file.Height) : new(file.Width, file.Height)));
                break;
            }
        }
    }
}
