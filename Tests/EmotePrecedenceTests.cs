using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MessageHeightTwitch.Emotes;
using Xunit;
using Mht = MessageHeightTwitch.MessageHeightTwitch;

namespace Tests;

/*
 * Which emote a name measures as when several catalogs hold it. FFZ renders the first emote in its set order: the
 * room's sets, then the global (default) sets. Every channel emote, from any provider, wins over every global one.
 * Heights are Chrome 155 on tetyys (Tools/BrowserProbe/validation/2026-10-11/channel-precedence).
 */
[Collection("Emote loading")]
public class EmotePrecedenceTests
{
    private static readonly Lazy<Dictionary<string, TextMeasurementCase>> BrowserCases = new(() =>
        MeasurementTestData.Read<TextMeasurementCase>("text-measurements.json").ToDictionary(test => test.Name));

    private const string BttvGlobal = "api.betterttv.net/3/cached/emotes/global";
    private const string BttvChannel = "api.betterttv.net/3/cached/users/twitch/36175310";
    private const string FfzGlobal = "api.frankerfacez.com/v1/set/global";
    private const string FfzRoom = "api.frankerfacez.com/v1/room/tetyys";
    private const string FfzEmoji = "cdn.frankerfacez.com/static/emoji/v2-.json";
    private const string SevenTvGlobal = "7tv.io/v3/emote-sets/global";
    private const string SevenTvChannel = "7tv.io/v3/users/twitch/36175310";

    private static string BttvImage(string Id) => $"cdn.betterttv.net/emote/{Id}/1x";

    private static string Ffz(params (string Name, int Width, int Height)[] Emotes) =>
        """{"default_sets":[1],"sets":{"1":{"emoticons":[""" + string.Join(",", Emotes.Select((e, i) =>
            $$"""{"id":{{i + 1}},"name":"{{e.Name}}","width":{{e.Width}},"height":{{e.Height}}""" + "}")) + "]}}}";

    private static string SevenTvSet(params (string Name, int Width, int Height)[] Emotes) =>
        """{"emotes":[""" + string.Join(",", Emotes.Select(e =>
            $$"""{"name":"{{e.Name}}","data":{"host":{"files":[{"name":"1x.webp","width":{{e.Width}},"height":{{e.Height}}""" + "}]}}}")) + "]}";

    /// <summary>tetyys's catalogs, reduced to the names both a channel and a global catalog hold.</summary>
    private static Dictionary<string, Func<HttpResponseMessage>> Tetyys() => new() {
        [BttvGlobal] = () => FakeHttp.Json("""[{"id":"global-feelsgoodman","code":"FeelsGoodMan"},{"id":"global-taxibro","code":"TaxiBro"}]"""),
        [BttvChannel] = () => FakeHttp.Json("""{"channelEmotes":[],"sharedEmotes":[{"id":"shared-feelsgoodman","code":"FeelsGoodMan"}]}"""),
        [BttvImage("global-feelsgoodman")] = () => FakeHttp.Png(30, 30),
        [BttvImage("global-taxibro")] = () => FakeHttp.Png(87, 30),
        [BttvImage("shared-feelsgoodman")] = () => FakeHttp.Png(28, 28),
        [FfzGlobal] = () => FakeHttp.Json(Ffz(("LilZ", 32, 23))),
        [FfzRoom] = () => FakeHttp.Json(Ffz(("LilZ", 22, 25), ("TaxiBro", 32, 11))),
        [FfzEmoji] = () => FakeHttp.Json("""{"e":[]}"""),
        [SevenTvGlobal] = () => FakeHttp.Json(SevenTvSet(("AlienDance", 32, 32))),
        [SevenTvChannel] = () => FakeHttp.Json("""{"emote_set":""" + SevenTvSet(("AlienDance", 32, 32)) + "}"),
    };

    private static Mht Channel(Dictionary<string, Func<HttpResponseMessage>> Routes, GlobalEmotes? Globals = null)
    {
        Routes.TryAdd(BttvGlobal, () => FakeHttp.Json("[]"));
        Routes.TryAdd(FfzGlobal, () => FakeHttp.Json("{\"default_sets\":[],\"sets\":{}}"));
        Routes.TryAdd(FfzEmoji, () => FakeHttp.Json("{\"e\":[]}"));
        Routes.TryAdd(SevenTvGlobal, () => FakeHttp.Json("{\"emotes\":[]}"));
        var client = new HttpClient(new FakeHttp(Routes));
        return new Mht("tetyys", "36175310", System.Threading.CancellationToken.None, true, Client: client, Globals: Globals ?? new());
    }

    [Theory]
    [InlineData("ffzW")]
    [InlineData("ffzRainbow")]
    public void AnOrdinaryCatalogEmoteKeepsItsMetadataWhenItsNameMatchesAnEffect(string Name)
    {
        var routes = Tetyys();
        routes[FfzRoom] = () => FakeHttp.Json(Ffz(("Base", 150, 28), (Name, 150, 28), ("Ordinary", 150, 28)));
        using var measurer = Channel(routes);
        var parameters = Mht.CalculateMessageHeightParams.Default;
        Assert.Equal(measurer.CalculateMessageHeightEx("Base Ordinary", "a", "a", parameters),
            measurer.CalculateMessageHeightEx("Base " + Name, "a", "a", parameters));
    }

    public static TheoryData<string> ChannelEmoteCases()
    {
        var data = new TheoryData<string>();
        foreach (var test in BrowserCases.Value.Values.Where(test => test.Name.StartsWith("ChannelEmotePrecedence-", StringComparison.Ordinal)))
            data.Add(test.Name);
        return data;
    }

    /// <summary>Browser-captured heights exercise the channel-over-global catalogs with the standard message corpus.</summary>
    [Theory]
    [MemberData(nameof(ChannelEmoteCases), DisableDiscoveryEnumeration = true)]
    public void ChannelEmotesWinOverGlobalOnesAsInChrome(string Name)
    {
        var test = BrowserCases.Value[Name];
        using var measurer = Channel(Tetyys());
        Assert.Equal(test.Height!.Value, measurer.CalculateMessageHeightEx(test.Text, "tetyys", test.DisplayName, test.Parameters(new())));
    }

    /// <summary>
    /// One name in two catalogs, as provider and width. Measured in Chrome: BetterTTV over FFZ in the room (LOLE),
    /// FFZ over 7TV in the room (VaN, Sadeg) and globally (BibleThump), a BetterTTV channel emote over a 7TV global one
    /// (EZ). A 7TV channel emote over a BetterTTV global one follows FFZ's set order. BetterTTV over FFZ among global
    /// emotes is not measured.
    /// </summary>
    [Theory]
    [InlineData("RoomBttvFfz", Mht.EMOTE_PROVIDER.BTTV, 40)]
    [InlineData("RoomFfzSevenTv", Mht.EMOTE_PROVIDER.FFZ, 60)]
    [InlineData("GlobalBttvFfz", Mht.EMOTE_PROVIDER.BTTV, 80)]
    [InlineData("GlobalFfzSevenTv", Mht.EMOTE_PROVIDER.FFZ, 100)]
    [InlineData("RoomBttvGlobalSevenTv", Mht.EMOTE_PROVIDER.BTTV, 120)]
    [InlineData("RoomSevenTvGlobalBttv", Mht.EMOTE_PROVIDER.SEVENTV, 140)]
    [InlineData("RoomFfzGlobalBttv", Mht.EMOTE_PROVIDER.FFZ, 160)]
    public void ProvidersKeepTheirOrderWithinATier(string Name, Mht.EMOTE_PROVIDER Provider, float Width)
    {
        using var measurer = Channel(new() {
            [BttvGlobal] = () => FakeHttp.Json("""
                [{"id":"g80","code":"GlobalBttvFfz"},{"id":"g130","code":"RoomSevenTvGlobalBttv"},{"id":"g170","code":"RoomFfzGlobalBttv"}]
                """),
            [BttvChannel] = () => FakeHttp.Json("""
                {"channelEmotes":[{"id":"c40","code":"RoomBttvFfz"},{"id":"c120","code":"RoomBttvGlobalSevenTv"}],"sharedEmotes":[]}
                """),
            [BttvImage("c40")] = () => FakeHttp.Png(40, 28),
            [BttvImage("c120")] = () => FakeHttp.Png(120, 28),
            [BttvImage("g80")] = () => FakeHttp.Png(80, 28),
            [BttvImage("g130")] = () => FakeHttp.Png(130, 28),
            [BttvImage("g170")] = () => FakeHttp.Png(170, 28),
            [FfzRoom] = () => FakeHttp.Json(Ffz(("RoomBttvFfz", 50, 28), ("RoomFfzSevenTv", 60, 28), ("RoomFfzGlobalBttv", 160, 28))),
            [FfzGlobal] = () => FakeHttp.Json(Ffz(("GlobalBttvFfz", 90, 28), ("GlobalFfzSevenTv", 100, 28))),
            [SevenTvChannel] = () => FakeHttp.Json("""{"emote_set":""" + SevenTvSet(("RoomFfzSevenTv", 70, 28), ("RoomSevenTvGlobalBttv", 140, 28)) + "}"),
            [SevenTvGlobal] = () => FakeHttp.Json(SevenTvSet(("GlobalFfzSevenTv", 110, 28), ("RoomBttvGlobalSevenTv", 150, 28))),
        });

        Assert.True(measurer.Emotes.TryResolveEmote(Name, Mht.CalculateMessageHeightParams.Default, out var entry, out var found));
        Assert.Equal(Provider, found);
        Assert.Equal(Width, entry.Size.Width);
    }

    /// <summary>
    /// FFZ adds a set it grants to listed logins (Subwoofer Emote Effects) to those users' own sets, which come before
    /// the room's. Other senders don't have it at all.
    /// </summary>
    [Fact]
    public void FfzSetsGrantedToTheSenderComeFirstAndOnlyForThem()
    {
        using var measurer = Channel(new() {
            [FfzGlobal] = () => FakeHttp.Json("""
                {"default_sets":[3],
                 "sets":{"3":{"emoticons":[]},
                         "1532818":{"emoticons":[{"id":1,"name":"Wide","width":200,"height":28},
                           {"id":720510,"name":"ffzRainbow","width":28,"height":28,"modifier":true,"modifier_flags":2049}]}},
                 "users":{"1532818":["subwoofer_fan"]}}
                """),
            [FfzRoom] = () => FakeHttp.Json(Ffz(("Wide", 20, 28), ("LilZ", 22, 25))),
            [FfzEmoji] = () => FakeHttp.Json("""{"e":[]}"""),
        });
        float Height(string Sender, string Text) =>
            measurer.CalculateMessageHeightEx(Text, Sender, Sender, Mht.CalculateMessageHeightParams.Default);

        // The granted 200px Wide wins over the room's 20px one, so two of them wrap.
        Assert.True(Height("subwoofer_fan", "Wide Wide") > Height("subwoofer_fan", "Wide"));
        Assert.Equal(Height("SubWoofer_Fan", "Wide Wide"), Height("subwoofer_fan", "Wide Wide"));
        Assert.Equal(Height("tetyys", "Wide"), Height("tetyys", "Wide Wide"));

        // A granted hidden modifier merges into the emote before it; for anyone else its name is text.
        var modified = string.Join(' ', Enumerable.Repeat("LilZ ffzRainbow", 8));
        var plain = string.Join(' ', Enumerable.Repeat("LilZ", 8));
        Assert.Equal(Height("subwoofer_fan", plain), Height("subwoofer_fan", modified));
        Assert.True(Height("tetyys", modified) > Height("tetyys", plain));
    }

    /// <summary>Twitch renders its own emotes before FFZ's tokenizer runs, so a native emote in the message beats them all.</summary>
    [Fact]
    public void TwitchNativeEmoteWinsOverEveryCatalog()
    {
        using var measurer = Channel(Tetyys());
        var parameters = Mht.CalculateMessageHeightParams.Default;
        parameters.TwitchEmotes = new() { ["TaxiBro"] = "https://example.test/taxibro.png" };

        Assert.True(measurer.Emotes.TryResolveEmote("TaxiBro", parameters, out _, out var provider));
        Assert.Equal(Mht.EMOTE_PROVIDER.NONE, provider);
    }

    /// <summary>
    /// The old merged catalog kept whichever image finished downloading first. Chrome never consults the images: FFZ's
    /// tokenizer picks the emote from the room's sets before the global ones (getSetIDs, getEmotes), and the token it
    /// emits already names the channel set and its API size (28x28) before the image element exists, which has no size
    /// attributes. On tetyys the shared 28x28 FeelsGoodMan renders over the 30x30 global one, 5 in one line and 6 in two
    /// (Tools/BrowserProbe/validation/2026-10-15/bttv-channel-over-global).
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task BttvChannelEmoteWinsWhicheverImageLoadsFirst(bool ChannelFirst)
    {
        var globals = new GlobalEmotes();
        BTTVEmoteProvider provider = null!;
        async Task<HttpResponseMessage> After(Func<EmoteSet> Set, int Side)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            while (!Set().TryGet("FeelsGoodMan", out _)) await Task.Delay(5, timeout.Token);
            return FakeHttp.Png(Side, Side);
        }
        using var handler = new AsyncHttp(key => key switch {
            BttvGlobal => Task.FromResult(FakeHttp.Json("""[{"id":"global","code":"FeelsGoodMan"}]""")),
            BttvChannel => Task.FromResult(FakeHttp.Json("""{"channelEmotes":[],"sharedEmotes":[{"id":"shared","code":"FeelsGoodMan"}]}""")),
            _ when key == BttvImage("global") => ChannelFirst ? After(() => provider.Channel, 30) : Task.FromResult(FakeHttp.Png(30, 30)),
            _ => ChannelFirst ? Task.FromResult(FakeHttp.Png(28, 28)) : After(() => provider.Global, 28),
        });
        using var client = new HttpClient(handler);
        provider = new BTTVEmoteProvider(new EmoteLoading(Client: client, Globals: globals));

        await provider.Initialize("36175310", CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));

        Assert.True(provider.TryGetEmote("FeelsGoodMan", out var size));
        Assert.Equal(new SizeF(28, 28), size);
        Assert.Equal(new SizeF(30, 30), provider.Global["FeelsGoodMan"].Size);
    }

    [Fact]
    public async Task SevenTvChannelEmoteWinsOverTheGlobalOne()
    {
        using var http = new FakeHttp(new Dictionary<string, Func<HttpResponseMessage>> {
            [SevenTvGlobal] = () => FakeHttp.Json(SevenTvSet(("AlienDance", 32, 32))),
            [SevenTvChannel] = () => FakeHttp.Json("""{"emote_set":""" + SevenTvSet(("AlienDance", 48, 24)) + "}"),
        });
        using var client = new HttpClient(http);
        var provider = new SevenTVEmoteProvider(new EmoteLoading(Client: client, Globals: new()));

        await provider.Initialize("36175310", CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(provider.TryGetEmote("AlienDance", out var size));
        Assert.Equal(new SizeF(48, 24), size);
    }

    [Fact]
    public async Task FfzRoomEmoteWinsOverTheGlobalOne()
    {
        using var http = new FakeHttp(new Dictionary<string, Func<HttpResponseMessage>> {
            [FfzGlobal] = () => FakeHttp.Json(Ffz(("LilZ", 32, 23))),
            [FfzRoom] = () => FakeHttp.Json(Ffz(("LilZ", 22, 25))),
            [FfzEmoji] = () => FakeHttp.Json("""{"e":[]}"""),
        });
        using var client = new HttpClient(http);
        var provider = new FFZEmoteProvider(new EmoteLoading(Client: client, Globals: new()));

        await provider.Initialize("tetyys", CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(provider.TryGetEmote("LilZ", out var size));
        Assert.Equal(new SizeF(22, 25), size);
    }

    /// <summary>Channels share the global catalogs but keep their own channel catalogs.</summary>
    [Fact]
    public void ChannelsShareGlobalEmotesButNotTheirOwn()
    {
        var globals = new GlobalEmotes();
        using var tetyys = Channel(Tetyys(), globals);
        using var other = Channel(new() { [FfzEmoji] = () => FakeHttp.Json("""{"e":[]}""") }, globals);
        var parameters = Mht.CalculateMessageHeightParams.Default;

        Assert.True(other.Emotes.TryResolveEmote("FeelsGoodMan", parameters, out var global, out _));
        Assert.Equal(new SizeF(30, 30), global.Size);
        Assert.True(tetyys.Emotes.TryResolveEmote("FeelsGoodMan", parameters, out var own, out _));
        Assert.Equal(new SizeF(28, 28), own.Size);
        // tetyys's FFZ room emote stays in tetyys; the other channel has only the BetterTTV global one.
        Assert.True(other.Emotes.TryResolveEmote("TaxiBro", parameters, out var taxi, out var provider));
        Assert.Equal((Mht.EMOTE_PROVIDER.BTTV, new SizeF(87, 30)), (provider, taxi.Size));
    }

    private sealed class AsyncHttp(Func<string, Task<HttpResponseMessage>> Reply) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage Request, CancellationToken Token) =>
            Reply(Request.RequestUri!.Host + Request.RequestUri.AbsolutePath);
    }
}
