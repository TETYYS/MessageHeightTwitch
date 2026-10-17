using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using System.Threading;
using MessageHeightTwitch.Emotes;
using Xunit;
using Mht = MessageHeightTwitch.MessageHeightTwitch;

namespace Tests;

// A measurer that loads its channel's emotes, here from catalogs served from memory.
[Collection("Emote loading")]
public class ChannelMeasurerTests
{
    private static FakeHttp Catalogs() => new(new Dictionary<string, Func<HttpResponseMessage>> {
        ["api.frankerfacez.com/v1/set/global"] = () => FakeHttp.Json("""
            {"default_sets":[3],"sets":{"3":{"emoticons":[
              {"id":1,"name":"FfzWide","width":200,"height":28},
              {"id":2,"name":"ffzHide","width":150,"height":28,"modifier":true,"modifier_flags":1}]}}}
            """),
        ["cdn.frankerfacez.com/static/emoji/v2-.json"] = () => FakeHttp.Json("""{"e":[]}"""),
        ["api.betterttv.net/3/cached/emotes/global"] = () => FakeHttp.Json("""
            [{"id":"wide","code":"BttvWide"},{"id":"over","code":"Over!","modifier":true}]
            """),
        ["cdn.betterttv.net/emote/wide/1x"] = () => FakeHttp.Png(200, 28),
        ["cdn.betterttv.net/emote/over/1x"] = () => FakeHttp.Png(150, 28),
        ["7tv.io/v3/emote-sets/global"] = () => FakeHttp.Json("""
            {"emotes":[{"name":"SevenTall","data":{"host":{"files":[{"name":"1x.webp","width":40,"height":80}]}}}]}
            """),
    });

    private static float Height(Mht Measurer, string Text) =>
        Measurer.CalculateMessageHeightEx(Text, "tetyys", "tetyys", Mht.CalculateMessageHeightParams.Default);

    /// <summary>
    /// A hidden FFZ modifier and a BetterTTV overlay both merge into the emote before them, so the pair takes one
    /// 200px box instead of wrapping. 7TV emotes count only when enabled.
    /// </summary>
    private static bool LoadedEverything(Mht Measurer, bool SevenTv) =>
        Height(Measurer, "FfzWide ffzHide") == Height(Measurer, "FfzWide") &&
        Height(Measurer, "BttvWide Over!") == Height(Measurer, "BttvWide") &&
        Height(Measurer, "FfzWide FfzWide") > Height(Measurer, "FfzWide") &&
        (Height(Measurer, "SevenTall") > Height(Measurer, "SevenTalk")) == SevenTv;

    [Fact]
    public async Task ExplicitGlobalInitializationIsReusedAcrossChannelReplacement()
    {
        using var http = Catalogs();
        using var client = new HttpClient(http);
        var globals = new GlobalEmotes();
        await globals.Initialize(false, CancellationToken.None, client).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.False(globals.IsInitialized(true));
        Assert.Throws<InvalidOperationException>(() => { _ = globals.GetInitialization(true); });
        await globals.Initialize(true, CancellationToken.None, client).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(globals.IsInitialized(true));

        for (var i = 0; i < 2; i++) {
            using var channel = new Mht("channel", "id", CancellationToken.None, true, Client: client, Globals: globals);
            Assert.True(LoadedEverything(channel, true));
        }

        Assert.Equal(1, http.Requests["api.frankerfacez.com/v1/set/global"]);
        Assert.Equal(1, http.Requests["cdn.frankerfacez.com/static/emoji/v2-.json"]);
        Assert.Equal(1, http.Requests["api.betterttv.net/3/cached/emotes/global"]);
        Assert.Equal(1, http.Requests["7tv.io/v3/emote-sets/global"]);
        Assert.Equal(2, http.Requests["api.frankerfacez.com/v1/room/channel"]);
        Assert.Equal(2, http.Requests["api.betterttv.net/3/cached/users/twitch/id"]);
        Assert.Equal(2, http.Requests["7tv.io/v3/users/twitch/id"]);
    }

    [Fact]
    public async Task InitializationRejectsAnotherFontDirectory()
    {
        _ = MessageHeightTwitch.FontFallbackList.Shared;
        var other = Path.Combine(Path.GetTempPath(), "MessageHeightTwitch-missing-" + Guid.NewGuid());

        await Assert.ThrowsAsync<ArgumentException>(() => Mht.Init(other));
        Assert.ThrowsAny<OperationCanceledException>(() => new Mht("channel", "id", false, new CancellationToken(true)));

    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ManagedChannelWaitsForCatalogs(bool SevenTv)
    {
        using var http = Catalogs();
        using var client = new HttpClient(http);
        using var measurer = new Mht("channel", "id", CancellationToken.None, SevenTv,
            Client: client, Globals: new());
        Assert.True(LoadedEverything(measurer, SevenTv));
    }

    [Fact]
    public void ManagedChannelThrowsLoadingFailures()
    {
        using var http = new FakeHttp(_ => new(System.Net.HttpStatusCode.ServiceUnavailable));
        using var client = new HttpClient(http);
        Assert.Throws<HttpRequestException>(() => new Mht("channel", "id", CancellationToken.None, true,
            Client: client, Globals: new()));
    }

    [Fact]
    public async Task InternalConstructorCancelsInFlightRequestsWithItsToken()
    {
        using var cancellation = new CancellationTokenSource();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var client = new HttpClient(new CancelableHttp(entered, stopped));
        var construction = Task.Run(() => new Mht("channel", "id", cancellation.Token, true,
            Client: client, Globals: new()));
        try {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => construction.WaitAsync(TimeSpan.FromSeconds(5)));
            await stopped.Task.WaitAsync(TimeSpan.FromSeconds(5));
        } finally {
            cancellation.Cancel();
        }
    }

    private sealed class CancelableHttp(TaskCompletionSource Entered, TaskCompletionSource Stopped) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage Request, CancellationToken Token)
        {
            Entered.TrySetResult();
            try { await Task.Delay(Timeout.Infinite, Token); }
            finally { Stopped.TrySetResult(); }
            return new(System.Net.HttpStatusCode.OK);
        }
    }

    [Fact]
    public async Task ManagedImageLoadingThrowsAndMeasurementOnlyUsesCachedSizes()
    {
        var attempts = 0;
        var url = "https://example.test/managed-" + Guid.NewGuid() + ".png";
        using var http = new FakeHttp(path => path.StartsWith("example.test/")
            ? ++attempts == 1 ? new(System.Net.HttpStatusCode.ServiceUnavailable) : FakeHttp.Png(40, 80)
            : new(System.Net.HttpStatusCode.NotFound));
        using var client = new HttpClient(http);
        using var measurer = new Mht("channel", "id", CancellationToken.None, false, client, GlobalEmotes.Shared);
        var parameters = Mht.CalculateMessageHeightParams.Default;
        parameters.TwitchEmotes = new() { ["Emote"] = url };
        var placeholder = measurer.CalculateMessageHeightEx("Emote", "user", "user", parameters);
        Assert.Equal(0, attempts);
        await Assert.ThrowsAsync<HttpRequestException>(() => measurer.LoadTwitchEmotes([url]));
        Assert.Equal(1, attempts);
        await measurer.LoadTwitchEmotes([url]);
        Assert.True(measurer.CalculateMessageHeightEx("Emote", "user", "user", parameters) > placeholder);
        Assert.Equal(2, attempts);
    }

    [Fact]
    public void ChannelsMeasureConcurrentlyWithSharedFonts()
    {
        var first = Fixture.CreateTextMeasurer(null);
        var second = Fixture.CreateTextMeasurer(null);
        string[] messages = ["hello world", "漢字 (مرحبا) ไทย 😀", "Ελληνικά кириллица tiếng Việt", new string('w', 300)];
        var expected = Array.ConvertAll(messages, message => Height(first, message));

        Parallel.For(0, 4000, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount * 2 }, i => {
            var measurer = i % 2 == 0 ? first : second;
            Assert.Equal(expected[i % messages.Length], Height(measurer, messages[i % messages.Length]));
        });
    }
}
