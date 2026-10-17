using MessageHeightTwitch.Emotes;
using System;
using System.Linq;
using System.Collections.Generic;
using System.Drawing;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MessageHeightTwitch;
using Xunit;
using Mht = MessageHeightTwitch.MessageHeightTwitch;
using MessageHeightTwitch.Measurement.EmoteRewriting;

namespace Tests;

public class SevenTVTests
{
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> Reply) : HttpMessageHandler
    {
        internal readonly List<string> Requests = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage Request, CancellationToken CancellationToken)
        {
            lock (Requests) Requests.Add(Request.RequestUri!.AbsolutePath);
            return Task.FromResult(Reply(Request, CancellationToken));
        }
    }

    private static HttpResponseMessage Json(string Text) => new(HttpStatusCode.OK) { Content = new StringContent(Text) };
    private const string Global = """
        {"emotes":[{"name":"Shared","flags":0,"data":{"host":{"files":[{"name":"4x.webp","width":128,"height":128},{"name":"1x.webp","width":32,"height":32}]}}}]}
        """;
    private const string Channel = """
        {"emote_set":{"emotes":[
          {"name":"Shared","flags":0,"data":{"name":"original","host":{"files":[{"name":"1x.webp","width":96,"height":20}]}}},
          {"name":"OverlayAlias","flags":1,"data":{"flags":0,"host":{"files":[{"name":"1x.webp","width":40,"height":32}]}}},
          {"name":"NotOverlay","flags":0,"data":{"flags":256,"host":{"files":[{"name":"1x.webp","width":28,"height":28}]}}},
          {"name":"Deleted","data":null}
        ]}}
        """;

    /// <summary>
    /// The size the provider measures for a name, after its precedence between sets.
    /// </summary>
    private static SizeF Found(SevenTVEmoteProvider Provider, string Name)
    {
        Assert.True(Provider.TryGetEmote(Name, out var size), Name);
        return size;
    }

    [Fact]
    public async Task LoadsV3AliasesChannelOverridesAndOverlayFlagsWithoutDownloadingImages()
    {
        using var handler = new Handler((request, _) => Json(request.RequestUri!.AbsolutePath.EndsWith("global") ? Global : Channel));
        using var client = new HttpClient(handler);
        var provider = new SevenTVEmoteProvider(new(Client: client, Globals: new()));
        await provider.Initialize("36175310", CancellationToken.None);
        Assert.Equal(["/v3/emote-sets/global", "/v3/users/twitch/36175310"], handler.Requests.Order());
        Assert.Equal(new SizeF(96,20), Found(provider, "Shared"));
        Assert.Equal(new SizeF(-40,-32), Found(provider, "OverlayAlias"));
        Assert.Equal(new SizeF(28,28), Found(provider, "NotOverlay"));
        Assert.False(provider.TryGetEmote("original", out _));
        Assert.False(provider.TryGetEmote("Deleted", out _));
        Assert.False(provider.IsEmojiSupported("😀"));
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.OK)]
    public async Task ChannelWithoutASetRetainsGlobals(HttpStatusCode ChannelStatus)
    {
        using var handler = new Handler((request, _) => request.RequestUri!.AbsolutePath.EndsWith("global") ? Json(Global) : new(ChannelStatus) { Content = new StringContent("{\"emote_set\":null}") });
        using var client = new HttpClient(handler);
        var provider = new SevenTVEmoteProvider(new(Client: client, Globals: new()));
        await provider.Initialize("missing", CancellationToken.None);
        Assert.Equal(new SizeF(32,32), Found(provider, "Shared"));
    }

    [Fact]
    public async Task CancellationReachesTheRequest()
    {
        using var handler = new Handler((_, token) => {
            token.ThrowIfCancellationRequested();
            return Json(Global);
        });
        using var client = new HttpClient(handler);
        var provider = new SevenTVEmoteProvider(new(Client: client, Globals: new()));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => provider.Initialize("id", cancellation.Token));
    }

    [Fact]
    public async Task FailedChannelRequestDoesNotDisableSuccessfullyLoadedGlobals()
    {
        using var handler = new Handler((request, _) => request.RequestUri!.AbsolutePath.EndsWith("global") ? Json(Global) : new(HttpStatusCode.ServiceUnavailable));
        using var client = new HttpClient(handler);
        var provider = new SevenTVEmoteProvider(new(Client: client, Globals: new()));
        await Assert.ThrowsAsync<HttpRequestException>(() => provider.Initialize("id", CancellationToken.None));
        Assert.True(provider.TryGetEmote("Shared", out _));
    }

    [Fact]
    public void LegacyConstructorDisables7TvAndEnabledConstructorUsesIt()
    {
        var enabled = new Mht(Lookup, Empty, Empty, (_, _) => default, _ => false, _ => false);
        var disabled = new Mht(Empty, Empty, Empty, (_, _) => default, _ => false, _ => false);
        var text = "Wide Wide Wide";
        Assert.Equal(74, enabled.CalculateMessageHeight(text, "tetyys", "TETYYS", 0, null!));
        Assert.Equal(30, disabled.CalculateMessageHeight(text, "tetyys", "TETYYS", 0, null!));
        static bool Lookup(ReadOnlySpan<char> Name, out SizeF Size) { Size = new(200, 28); return Name.SequenceEqual("Wide"); }
    }

    [Fact]
    public void OverlayReservesLargestBoxBeforeWrappingAndCanRenderAlone()
    {
        var measurer = new Mht(Lookup, Empty, Empty, (_, _) => default, _ => false, _ => false);
        using var rewriter = new ModifierRewriter();
        var prepared = rewriter.Rewrite("Base Overlay Overlay", Mht.CalculateMessageHeightParams.Default, measurer.Emotes);
        Assert.Equal("Base", prepared.Text.ToString());
        Assert.Equal(new SizeF(80,40), prepared.Emotes[0].Size);
        prepared = rewriter.Rewrite("Overlay", Mht.CalculateMessageHeightParams.Default, measurer.Emotes);
        Assert.Equal(new SizeF(80,40), prepared.Emotes[0].Size);
        static bool Lookup(ReadOnlySpan<char> Name, out SizeF Size) {
            Size = Name.SequenceEqual("Overlay") ? new(-80,-40) : new(28,28);
            return Name is "Base" or "Overlay";
        }
    }
    private static bool Empty(ReadOnlySpan<char> Name, out SizeF Size) { Size = default; return false; }
}
