using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using MessageHeightTwitch.Emotes;
using MessageHeightTwitch.Measurement.EmoteRewriting;

using Xunit;

using Mht = MessageHeightTwitch.MessageHeightTwitch;

namespace Tests;

// How each provider reads its catalogs, served from memory.
[Collection("Emote loading")]
public class EmoteCatalogTests
{
    private const string BttvGlobal = "api.betterttv.net/3/cached/emotes/global";
    private const string BttvChannel = "api.betterttv.net/3/cached/users/twitch/id";
    private const string FfzGlobal = "api.frankerfacez.com/v1/set/global";
    private const string FfzRoom = "api.frankerfacez.com/v1/room/id";
    private const string FfzEmoji = "cdn.frankerfacez.com/static/emoji/v2-.json";
    private const string SevenTvGlobal = "7tv.io/v3/emote-sets/global";
    private const string SevenTvChannel = "7tv.io/v3/users/twitch/id";

    private static EmoteLoading Once(HttpClient Client) => new(Client: Client, Globals: new());

    [Fact]
    public async Task BttvReadsChannelAndSharedEmotesAndTheirModifiers()
    {
        using var http = new FakeHttp(new Dictionary<string, Func<HttpResponseMessage>> {
            [BttvGlobal] = () => FakeHttp.Json("""[{"id":"g","code":"Global","imageType":"png"}]"""),
            [BttvChannel] = () => FakeHttp.Json("""
                {"id":"user","bots":["bot"],
                 "channelEmotes":[{"id":"c","code":"Channel","imageType":"png"},{"id":"m","code":"Overlay","modifier":true},
                                  {"code":"NoId"},{"id":"nocode"}],
                 "sharedEmotes":[{"id":"w","code":"w!"}]}
                """),
            ["cdn.betterttv.net/emote/g/1x"] = () => FakeHttp.Png(28, 28),
            ["cdn.betterttv.net/emote/c/1x"] = () => FakeHttp.Png(56, 28),
            ["cdn.betterttv.net/emote/m/1x"] = () => FakeHttp.Png(20, 20),
            ["cdn.betterttv.net/emote/w/1x"] = () => FakeHttp.Png(10, 10),
        });
        using var client = new HttpClient(http);
        var provider = new BTTVEmoteProvider(Once(client));

        await provider.Initialize("id", CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(provider.TryGetEmote("Global", out var global));
        Assert.Equal(new SizeF(28, 28), global);

        Assert.True(provider.TryGetEmote("Channel", out var channel));
        Assert.Equal(new SizeF(56, 28), channel);

        Assert.False(provider.TryGetEmote("NoId", out _));
        Assert.Equal(["Global"], provider.Global.Names);
        Assert.Equal(["Channel", "Overlay", "w!"], provider.Channel.Names.Order());

        Assert.True(provider.TryGetModifier("Overlay", out var overlay));
        Assert.Equal(new EmoteModifier(), overlay);

        Assert.True(provider.TryGetModifier("w!", out var wide));
        Assert.Equal(EmoteModifier.Known("w!", Mht.EMOTE_PROVIDER.BTTV), wide);

        Assert.False(provider.TryGetModifier("Channel", out _));
    }

    [Theory]
    [InlineData("null", "[]")]
    [InlineData("[]", "null")]
    [InlineData("[]", """{"channelEmotes":null}""")]
    [InlineData("[]", """{"channelEmotes":[],"sharedEmotes":null}""")]
    public async Task BttvRejectsMissingEmoteLists(string Global, string Channel)
    {
        using var http = new FakeHttp(new Dictionary<string, Func<HttpResponseMessage>> {
            [BttvGlobal] = () => FakeHttp.Json(Global),
            [BttvChannel] = () => FakeHttp.Json(Channel),
        });
        using var client = new HttpClient(http);
        var provider = new BTTVEmoteProvider(Once(client));

        await Assert.ThrowsAsync<InvalidDataException>(() => provider.Initialize("id", CancellationToken.None));
    }

    [Fact]
    public void BttvEmojiCatalogMustBePresentAndAList()
    {
        Assert.Throws<InvalidDataException>(() => BTTVEmoteProvider.ReadSupportedEmojis(null));
        Assert.Throws<InvalidDataException>(() => BTTVEmoteProvider.ReadSupportedEmojis(new MemoryStream("null"u8.ToArray())));
        Assert.Equal(["a"], BTTVEmoteProvider.ReadSupportedEmojis(new MemoryStream("""["a"]"""u8.ToArray())));
    }

    [Fact]
    public async Task FfzRoomWinsOverTheGlobalSetAndRecordsModifiers()
    {
        using var http = new FakeHttp(new Dictionary<string, Func<HttpResponseMessage>> {
            [FfzGlobal] = () => FakeHttp.Json("""
                {"default_sets":[3],"sets":{"3":{"_type":1,"css":null,"description":"Global","emoticons":[
                  {"id":1,"name":"Shared","width":20,"height":20,"css":"x","hidden":false,"margins":"1px","offset":"0"},
                  {"id":2,"name":"Mod","width":10,"height":10,"modifier":true,"modifier_flags":3,"modifier_prefix":true},
                  {"id":59847,"name":"Known","width":10,"height":10},
                  {"id":3,"name":null,"width":5,"height":5}]}}}
                """),
            [FfzRoom] = () => FakeHttp.Json("""
                {"sets":{"9":{"emoticons":[
                  {"id":4,"name":"Shared","width":99,"height":99},
                  {"id":5,"name":"Mod","width":1,"height":1,"modifier":true},
                  {"id":6,"name":"Own","width":30,"height":28,"modifier":true,"modifier_flags":1}]}}}
                """),
            [FfzEmoji] = () => FakeHttp.Json("""
                {"v":2,"c":["Smileys"],"e":[
                  [0,0,0,0,"1f44d",0,0,[["1f44d-1f3fb"],[],[null]]],
                  [0,0,0,0,"1f600",0,0,5],
                  [1,2]]}
                """),
        });
        using var client = new HttpClient(http);
        var provider = new FFZEmoteProvider(Once(client));

        await provider.Initialize("id", CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));

        // The room's emote and its modifier behavior win over the global one with the same name.
        Assert.True(provider.TryGetEmote("Shared", out var shared));
        Assert.Equal(new SizeF(99, 99), shared);

        Assert.True(provider.TryGetEmote("Own", out var own));
        Assert.Equal(new SizeF(30, 28), own);

        Assert.Equal(["Known", "Mod", "Shared"], provider.Global.Names.Order());
        Assert.Equal(["Mod", "Own", "Shared"], provider.Room.Names.Order());

        Assert.True(provider.TryGetModifier("Mod", null, out var mod));
        Assert.Equal(new EmoteModifier(), mod);
        Assert.Equal(new EmoteModifier(EmoteModifierFlags.Hidden | EmoteModifierFlags.FlipX, true), provider.Global["Mod"].Modifier);

        Assert.True(provider.TryGetModifier("Known", null, out var known));
        Assert.Equal(new EmoteModifier(), known);

        Assert.True(provider.TryGetModifier("Own", null, out var ownModifier));
        Assert.Equal(new EmoteModifier(EmoteModifierFlags.Hidden), ownModifier);

        Assert.False(provider.TryGetModifier("Shared", null, out _));
        Assert.True(provider.IsEmojiSupported("\U0001F44D"));
        Assert.True(provider.IsEmojiSupported("\U0001F44D\U0001F3FB"));
        Assert.True(provider.IsEmojiSupported("\U0001F600"));
        Assert.False(provider.IsEmojiSupported("\U0001F601"));
    }

    /// <summary>
    /// Like Subwoofer Emote Effects: a set FFZ grants to listed logins applies to their messages only, and a set that
    /// is neither default nor granted only appears in FFZ's emote menu.
    /// </summary>
    [Fact]
    public async Task FfzGrantedSetsApplyOnlyToListedLogins()
    {
        using var http = new FakeHttp(new Dictionary<string, Func<HttpResponseMessage>> {
            [FfzGlobal] = () => FakeHttp.Json("""
                {"default_sets":[3],
                 "sets":{"3":{"emoticons":[{"id":1,"name":"Everyone","width":20,"height":20}]},
                         "7":{"emoticons":[{"id":2,"name":"ffzRainbow","width":28,"height":28,"modifier":true,"modifier_flags":2049}]},
                         "9":{"emoticons":[{"id":3,"name":"MenuOnly","width":20,"height":20}]}},
                 "users":{"7":["subwoofer_fan","second_fan"]}}
                """),
            [FfzEmoji] = () => FakeHttp.Json("""{"e":[]}"""),
        });
        using var client = new HttpClient(http);
        var provider = new FFZEmoteProvider(Once(client));

        await provider.Initialize("id", CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(["Everyone"], provider.Global.Names);
        Assert.False(provider.TryGetEmote("ffzRainbow", out _));
        Assert.False(provider.TryGetEmote("MenuOnly", out _));
        Assert.Null(provider.UserSetsFor("tetyys"));
        foreach (var login in new[] { "subwoofer_fan", "SubWoofer_Fan", "second_fan" }) {
            var granted = Assert.Single(provider.UserSetsFor(login)!);
            Assert.Equal(["ffzRainbow"], granted.Names);
            Assert.True(provider.TryGetModifier("ffzRainbow", provider.UserSetsFor(login), out var rainbow));
            Assert.Equal(new EmoteModifier(EmoteModifierFlags.Hidden | EmoteModifierFlags.Rainbow), rainbow);
        }
        Assert.False(provider.TryGetModifier("ffzRainbow", provider.UserSetsFor("tetyys"), out _));
    }

    [Fact]
    public async Task FfzRejectsAGlobalResponseWithoutDefaultSets()
    {
        using var http = new FakeHttp(new Dictionary<string, Func<HttpResponseMessage>> {
            [FfzGlobal] = () => FakeHttp.Json("""{"sets":{"3":{"emoticons":[]}}}"""),
            [FfzEmoji] = () => FakeHttp.Json("""{"e":[]}"""),
        });
        using var client = new HttpClient(http);
        var provider = new FFZEmoteProvider(Once(client));

        await Assert.ThrowsAsync<InvalidDataException>(() => provider.Initialize("id", CancellationToken.None)).WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void ProvidersDefaultToTheSharedClientAndOneAttemptPerResource()
    {
        Assert.False(new FFZEmoteProvider().TryGetEmote("x", out _));
        Assert.False(new SevenTVEmoteProvider().IsEmojiSupported("\U0001F600"));
    }

    [Theory]
    [InlineData("null")]
    [InlineData("""{"emotes":null}""")]
    public async Task SevenTvRejectsAMissingGlobalSet(string Global)
    {
        using var http = new FakeHttp(new Dictionary<string, Func<HttpResponseMessage>> { [SevenTvGlobal] = () => FakeHttp.Json(Global) });
        using var client = new HttpClient(http);

        await Assert.ThrowsAsync<InvalidDataException>(() => new SevenTVEmoteProvider(Once(client)).Initialize("id", CancellationToken.None));
    }

    [Theory]
    [InlineData("null")]
    [InlineData("""{"emote_set":null}""")]
    public async Task SevenTvAcceptsAChannelWithoutASet(string Channel)
    {
        using var http = new FakeHttp(new Dictionary<string, Func<HttpResponseMessage>> {
            [SevenTvGlobal] = () => FakeHttp.Json("""{"emotes":[]}"""),
            [SevenTvChannel] = () => FakeHttp.Json(Channel),
        });
        using var client = new HttpClient(http);
        var provider = new SevenTVEmoteProvider(Once(client));

        await provider.Initialize("id", CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(0, provider.Channel.Count + provider.Global.Count);
    }

    [Fact]
    public async Task SevenTvSkipsEmotesWithoutAUsableOneTimesFile()
    {
        using var http = new FakeHttp(new Dictionary<string, Func<HttpResponseMessage>> {
            [SevenTvGlobal] = () => FakeHttp.Json("""
                {"emotes":[
                  {"name":"NoData"},
                  {"name":"NoHost","data":{}},
                  {"name":"NoFiles","data":{"host":{}}},
                  {"name":"Large","data":{"host":{"files":[{"name":"2x.webp","width":64,"height":64}]}}},
                  {"name":"Empty","data":{"host":{"files":[{"name":"1x.webp","width":0,"height":28}]}}},
                  {"name":"Flat","data":{"host":{"files":[{"name":"1x.webp","width":28,"height":0}]}}},
                  {"data":{"host":{"files":[{"name":"1x.png","width":28,"height":28}]}}},
                  {"name":"Overlay","flags":1,"data":{"host":{"files":[{"name":"1x.avif","width":32,"height":30}]}}},
                  {"name":"Gif","data":{"host":{"files":[{"name":"1x.gif","width":40,"height":30}]}}}]}
                """),
        });
        using var client = new HttpClient(http);
        var provider = new SevenTVEmoteProvider(Once(client));

        await provider.Initialize("id", CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(["Gif", "Overlay"], provider.Global.Names.Order());
        Assert.Equal(new SizeF(-32, -30), provider.Global["Overlay"].Size);
        Assert.Equal(new SizeF(40, 30), provider.Global["Gif"].Size);
    }

    [Fact]
    public async Task ImagesSkiaCannotDecodeAreRejected()
    {
        using var http = new FakeHttp(_ => new(HttpStatusCode.OK) { Content = new ByteArrayContent(Encoding.ASCII.GetBytes("not an image")) });
        using var client = new HttpClient(http);

        await Assert.ThrowsAsync<InvalidDataException>(() => Once(client).ImageSize("https://example.test/x.png", CancellationToken.None));
    }

    /// <summary>A size another channel already downloaded is measured at once, without a placeholder or another request.</summary>
    [Fact]
    public async Task DefaultTwitchEmotesShareTheProcessImageCache()
    {
        var url = "https://example.test/shared-" + Guid.NewGuid() + ".png";
        GlobalEmotes.Shared.ImageSizes[url] = Task.FromResult(new SizeF(5, 6));
        var emotes = new TwitchEmotes();

        Assert.Equal(new SizeF(5, 6), emotes.GetEmote("Emote", url));
    }
}
