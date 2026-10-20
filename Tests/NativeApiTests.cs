using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MessageHeightTwitch.Emotes;
using Xunit;
using EmoteErrorCode = MessageHeightTwitchStatic.EmoteErrorCode;
using Mht = MessageHeightTwitch.MessageHeightTwitch;
using Native = MessageHeightTwitchStatic;

namespace Tests;

/*
 * The C ABI, called through function pointers as native code calls it. Channels are registered with measurers
 * that need no network.
 */
[Collection("Emote loading")]
public class NativeApiTests
{
    private static string Channel() => "native-" + Guid.NewGuid();

    private static Mht Measurer() => Fixture.CreateMeasurer(false);

    private static bool Disposed(Mht Measurer) => ((CancellationTokenSource)typeof(Mht).GetField("EmoteLifetime", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(Measurer)!).IsCancellationRequested;

    private static byte[] Utf8(string Text) => Encoding.UTF8.GetBytes(Text + "\0");

    private static unsafe float Measure(string? Channel, string? Message, string? User, string? Display, (string? Name, string? Url)[]? Emotes = null, int? Count = null)
    {
        delegate* unmanaged[Cdecl]<byte*, byte*, byte*, byte*, int, Native.TwitchEmote*, int, float> measure = &Native.CalculateMessageHeight;
        var handles = new List<GCHandle>();
        byte* Pin(string? Text)
        {
            if (Text == null) return null;
            var handle = GCHandle.Alloc(Utf8(Text), GCHandleType.Pinned);
            handles.Add(handle);
            return (byte*)handle.AddrOfPinnedObject();
        }
        try {
            var records = Emotes == null ? null : new Native.TwitchEmote[Emotes.Length];
            for (var x = 0; x < (Emotes?.Length ?? 0); x++) {
                records![x] = new() {
                    Name = Pin(Emotes![x].Name),
                    Url = Pin(Emotes[x].Url)
                };
            }
            fixed (Native.TwitchEmote* table = records)
                return measure(Pin(Channel), Pin(Message), Pin(User), Pin(Display), 1, table, Count ?? records?.Length ?? 0);
        } finally {
            foreach (var handle in handles) handle.Free();
        }
    }

    private static async Task<float> MeasureOncePublished(string Channel, string Message)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        float height;
        while ((height = Measure(Channel, Message, "tetyys", "TETYYS")) == 0)
            await Task.Delay(10, timeout.Token);
        return height;
    }

    private sealed class PendingCatalogHandler : HttpMessageHandler
    {
        internal readonly TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage Request, CancellationToken Token)
        {
            Started.TrySetResult();
            await Task.Delay(Timeout.Infinite, Token);
            return new(HttpStatusCode.NotFound);
        }
    }

    [Fact]
    public async Task MeasurementExceptionsAreReportedThroughTheChannelCallback()
    {
        var channel = Channel();
        var errors = new List<(EmoteErrorCode Code, string Message)>();
        Native.QueueChannel(channel, "id", 1000, false, (code, message) => errors.Add((code, message)), Measurer);
        await MeasureOncePublished(channel, "hi");
        Assert.Equal(0, Measure(channel, new string('a', 501), "user", "user"));
        var error = Assert.Single(errors);
        Assert.Equal(EmoteErrorCode.MeasurementFailed, error.Code);
        Assert.Contains("ArgumentException", error.Message);
        Assert.Contains("500", error.Message);
    }

    [Fact]
    public async Task FailedCatalogFetchDoesNotPreventNativeMeasurement()
    {
        var channel = Channel();
        var reported = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var http = new FakeHttp(_ => new(System.Net.HttpStatusCode.ServiceUnavailable));
        using var client = new System.Net.Http.HttpClient(http);
        Native.QueueChannel(channel, "id", 5000, false, (_, _) => reported.TrySetResult(), Client: client, Globals: new());
        try {
            await reported.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(Measure(channel, "hello", "user", "user") > 0);
            Assert.True(Measure(channel, "Emote", "user", "user", [("Emote", "https://example.test/bad")]) > 0);
        } finally {
            Native.QueueChannel(channel, "id", 5000, false, null, Measurer);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InvalidBttvImageUsesPlaceholderAndRecoversWithoutReplacingChannel(bool GlobalImage)
    {
        var channel = Channel();
        var reported = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var recover = false;
        using var http = new FakeHttp(path => path switch {
            "api.betterttv.net/3/cached/emotes/global" => FakeHttp.Json(GlobalImage ? "[{\"id\":\"bad\",\"code\":\"Broken\"}]" : "[]"),
            "api.frankerfacez.com/v1/set/global" => FakeHttp.Json("{\"default_sets\":[],\"sets\":{}}"),
            "cdn.frankerfacez.com/static/emoji/v2-.json" => FakeHttp.Json("{\"e\":[]}"),
            "api.betterttv.net/3/cached/users/twitch/id" => FakeHttp.Json(GlobalImage ? "{}" : "{\"channelEmotes\":[{\"id\":\"bad\",\"code\":\"Broken\"}],\"sharedEmotes\":[]}"),
            "cdn.betterttv.net/emote/bad/1x" => Volatile.Read(ref recover) ? FakeHttp.Png(40, 80) : FakeHttp.Json("not an image"),
            _ => new(System.Net.HttpStatusCode.NotFound)
        });
        using var client = new System.Net.Http.HttpClient(http);
        Native.QueueChannel(channel, "id", 5000, false, (_, message) => reported.TrySetResult(message), Client: client, Globals: new());
        try {
            Assert.Contains("https://cdn.betterttv.net/emote/bad/1x", await reported.Task.WaitAsync(TimeSpan.FromSeconds(5)));
            var placeholder = Measure(channel, "Broken", "user", "user");
            using var expected = new Mht(false);
            Assert.Equal(expected.CalculateMessageHeight("Broken", "user", "user", 1, new() { ["Broken"] = "https://example.test/missing" }), placeholder);
            Assert.True(placeholder > 0);
            Volatile.Write(ref recover, true);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            while (Measure(channel, "Broken", "user", "user") <= placeholder)
                await Task.Delay(10, timeout.Token);
        } finally {
            Native.QueueChannel(channel, "id", 5000, false, null, Measurer);
        }
    }

    [Fact]
    public async Task PublishedChannelMeasuresLikeTheManagedApi()
    {
        var channel = Channel();
        var measurer = Measurer();
        Assert.Equal(1, Native.QueueChannel(channel, "id", 1000, false, null, () => measurer));
        await MeasureOncePublished(channel, "hi");

        var emotes = new Dictionary<string, string> {
            ["Kappa"] = "https://example.test/kappa"
        };
        var expected = measurer.CalculateMessageHeight("Kappa hello", "tetyys", "TETYYS", 1, emotes);
        Assert.Equal(expected, Measure(channel, "Kappa hello", "tetyys", "TETYYS", [("Kappa", "https://example.test/kappa")]));
    }

    [Fact]
    public async Task InvalidCallsAndFailingMeasurementsReturnZero()
    {
        var channel = Channel();
        Assert.Equal(1, Native.QueueChannel(channel, "id", 1000, false, null, Measurer));
        await MeasureOncePublished(channel, "hi");

        Assert.Equal(0, Measure(null, "hi", "user", "User"));
        Assert.Equal(0, Measure(channel, null, "user", "User"));
        Assert.Equal(0, Measure(channel, "hi", null, "User"));
        Assert.Equal(0, Measure(channel, "hi", "user", null));
        Assert.Equal(0, Measure(channel, "hi", "user", "User", Count: -1));
        Assert.Equal(0, Measure(channel, "hi", "user", "User", Count: 1));
        Assert.Equal(0, Measure(Channel(), "hi", "user", "User"));
        Assert.Equal(0, Measure(channel, "hi", "user", "User", [(null, "https://example.test/x")]));
        Assert.Equal(0, Measure(channel, "hi", "user", "User", [("Kappa", null)]));
        // Measurement throws for a message over 500 runes, and for an emote the test measurer does not know.
        Assert.Equal(0, Measure(channel, new string('a', 501), "user", "User"));
        Assert.Equal(0, Measure(channel, "Unknown", "user", "User", [("Unknown", "https://example.test/x")]));
    }

    [Fact]
    public async Task ChannelMeasuresImmediatelyWhileEveryCatalogRequestIsPending()
    {
        var channel = Channel();
        using var http = new PendingCatalogHandler();
        using var client = new HttpClient(http);
        try {
            Assert.Equal(1, Native.QueueChannel(channel, "id", -1, true, null, Client: client, Globals: new()));
            Assert.True(Measure(channel, "hello", "user", "user") > 0);
            await http.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(Measure(channel, "hello", "user", "user") > 0);
        } finally {
            Native.QueueChannel(channel, "id", 1000, false, null, Measurer);
        }
    }

    [Fact]
    public async Task ReplacingAChannelDisposesItsPublishedMeasurer()
    {
        var channel = Channel();
        var first = Measurer();
        Native.QueueChannel(channel, "id", 1000, false, null, () => first);
        await MeasureOncePublished(channel, "hi");

        Native.QueueChannel(channel, "id", 1000, false, null, Measurer);

        Assert.True(Disposed(first));
    }

    [Fact]
    public async Task PreviousChannelRemainsAvailableDuringReplacementSetup()
    {
        var channel = Channel();
        var first = Measurer();
        Native.QueueChannel(channel, "id", 1000, false, null, () => first);
        await MeasureOncePublished(channel, "hi");
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var replacement = Task.Run(() => Native.QueueChannel(channel, "id", 1000, false, null, () => {
            entered.TrySetResult();
            release.Task.Wait();
            return Measurer();
        }));
        try {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(replacement.IsCompleted);
            Assert.False(Disposed(first));
            Assert.True(Measure(channel, "hi", "user", "user") > 0);
        } finally {
            release.TrySetResult();
            await replacement.WaitAsync(TimeSpan.FromSeconds(5));
        }
        Assert.True(Disposed(first));
        Assert.True(Measure(channel, "hi", "user", "user") > 0);
    }

    [Fact]
    public async Task FailedReplacementPreservesTheWorkingChannel()
    {
        var channel = Channel();
        var first = Measurer();
        Native.QueueChannel(channel, "id", 1000, false, null, () => first);
        await MeasureOncePublished(channel, "hi");
        var errors = new List<(EmoteErrorCode Code, string Message)>();
        Assert.Equal(0, Native.QueueChannel(channel, "id", 1000, false,
            (code, message) => errors.Add((code, message)), () => throw new InvalidOperationException("setup failed")));
        var error = Assert.Single(errors);
        Assert.Equal(EmoteErrorCode.ChannelInitializationFailed, error.Code);
        Assert.Contains("setup failed", error.Message);
        Assert.False(Disposed(first));
        Assert.True(Measure(channel, "hi", "user", "user") > 0);
    }

    [Fact]
    public async Task ThrowingErrorCallbackIsContained()
    {
        var called = new TaskCompletionSource<EmoteErrorCode>(TaskCreationOptions.RunContinuationsAsynchronously);
        Native.QueueChannel(Channel(), "id", 1000, false, (code, _) => {
            called.TrySetResult(code);
            throw new InvalidOperationException("host failure");
        }, () => throw new InvalidOperationException("setup failed"));

        Assert.Equal(EmoteErrorCode.ChannelInitializationFailed, await called.Task.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task CatalogFailureRetriesWhileMeasurementRemainsAvailable()
    {
        var channel = Channel();
        var reported = new TaskCompletionSource<EmoteErrorCode>(TaskCreationOptions.RunContinuationsAsynchronously);
        var attempts = 0;
        using var http = new FakeHttp(path => path switch {
            "api.betterttv.net/3/cached/emotes/global" => FakeHttp.Json("[]"),
            "api.frankerfacez.com/v1/set/global" => FakeHttp.Json("{\"default_sets\":[],\"sets\":{}}"),
            "cdn.frankerfacez.com/static/emoji/v2-.json" => FakeHttp.Json("{\"e\":[]}"),
            "api.betterttv.net/3/cached/users/twitch/id" => Interlocked.Increment(ref attempts) == 1
                ? new(HttpStatusCode.ServiceUnavailable) : new(HttpStatusCode.NotFound),
            _ => new(HttpStatusCode.NotFound)
        });
        using var client = new HttpClient(http);
        try {
            Assert.Equal(1, Native.QueueChannel(channel, "id", 5000, false,
                (code, _) => reported.TrySetResult(code), Client: client, Globals: new()));
            Assert.True(Measure(channel, "hi", "user", "user") > 0);
            Assert.Equal(EmoteErrorCode.ChannelInitializationFailed, await reported.Task.WaitAsync(TimeSpan.FromSeconds(5)));
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            while (Volatile.Read(ref attempts) < 2) {
                Assert.True(Measure(channel, "hi", "user", "user") > 0);
                await Task.Delay(10, timeout.Token);
            }
            Assert.Equal(2, Volatile.Read(ref attempts));
        } finally {
            Native.QueueChannel(channel, "id", 1000, false, null, Measurer);
        }
    }

    [Fact]
    public async Task NativeShimLoadsAndRetriesTwitchImagesInTheBackground()
    {
        var channel = Channel();
        var url = "https://example.test/native-" + Guid.NewGuid() + ".png";
        var attempts = 0;
        var reported = new TaskCompletionSource<(EmoteErrorCode Code, string Message)>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var http = new FakeHttp(path => path.StartsWith("example.test/") ? Interlocked.Increment(ref attempts) == 1 ? new(System.Net.HttpStatusCode.ServiceUnavailable) : FakeHttp.Png(40, 80) : new(System.Net.HttpStatusCode.NotFound));
        using var client = new System.Net.Http.HttpClient(http);
        using var measurer = new Mht("channel", "id", CancellationToken.None, false, client, GlobalEmotes.Shared);
        Native.QueueChannel(channel, "id", 5000, false, (code, message) => reported.TrySetResult((code, message)), () => measurer);
        await MeasureOncePublished(channel, "hi");
        var placeholder = Measure(channel, "Emote", "user", "user", [("Emote", url)]);
        Assert.True(placeholder > 0);

        var error = await reported.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(EmoteErrorCode.TwitchFetchFailed, error.Code);
        Assert.Contains(url, error.Message);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (Measure(channel, "Emote", "user", "user", [("Emote", url)]) <= placeholder)
            await Task.Delay(10, timeout.Token);
        Assert.Equal(2, Volatile.Read(ref attempts));
    }

    [Fact]
    public void QueueRejectsATimeoutBelowInfinite()
    {
        Assert.Equal(0, Native.QueueChannel(Channel(), "id", -2, false, null, Measurer));
    }

    [Fact]
    public unsafe void InitChannelValidatesItsArguments()
    {
        delegate* unmanaged[Cdecl]<byte*, byte*, int, byte, delegate* unmanaged[Cdecl]<void*, byte*, int, byte*, void>, void*, int> init = &Native.InitChannel;
        var name = Utf8(Channel());
        fixed (byte* channel = name) {
            Assert.Equal(0, init(null, channel, 1000, 0, null, null));
            Assert.Equal(0, init(channel, null, 1000, 0, null, null));
            Assert.Equal(0, init(channel, channel, -2, 0, null, null));
        }
    }

    [Fact]
    public unsafe void GlobalInitializationRejectsInvalidTimeoutAndDirectory()
    {
        delegate* unmanaged[Cdecl]<byte*, int, byte, delegate* unmanaged[Cdecl]<void*, byte*, int, byte*, void>, void*, int> init = &Native.InitMessageHeightTwitch;
        var path = Utf8(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()));
        fixed (byte* directory = path) {
            Assert.Equal(0, init(null, -2, 0, null, null));
            Assert.Equal(0, init(directory, 1000, 0, null, null));
        }
    }
}
