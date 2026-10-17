using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MessageHeightTwitch.Emotes;

/// <summary>Measures cached Twitch images; Load fetches dimensions before measurement.</summary>
internal sealed class TwitchEmotes
{
    private readonly EmoteLoading Loading;

    internal TwitchEmotes(EmoteLoading? Loading = null) => this.Loading = Loading ?? new EmoteLoading();

    internal bool IsLoaded(string Url) => Loading.TryGetImageSize(Url, out _);

    internal Task Load(IEnumerable<string> Urls, CancellationToken Token) =>
        Task.WhenAll(Urls.Distinct(StringComparer.Ordinal).Select(url => Loading.ImageSize(url, Token)));

    public SizeF GetEmote(ReadOnlySpan<char> Name, string Url) =>
        Loading.TryGetImageSize(Url, out var size) ? size : new SizeF(28, 28);
}
