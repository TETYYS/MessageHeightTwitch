using System;
using System.Collections.Generic;
using System.IO;
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
    public async Task ChannelMeasuresNothingUntilItsMeasurerIsPublished()
    {
        var channel = Channel();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Native.QueueChannel(channel, "id", 1000, false, null, () => {
            entered.TrySetResult();
            release.Task.Wait();
            return Measurer();
        });
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(0, Measure(channel, "hi", "user", "User"));
        release.TrySetResult();
        Assert.NotEqual(0, await MeasureOncePublished(channel, "hi"));
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
    public async Task MeasurerFinishedAfterItsChannelWasReplacedIsDisposed()
    {
        var channel = Channel();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var late = Measurer();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var created = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Native.QueueChannel(channel, "id", 1000, false, null, () => {
            entered.TrySetResult();
            release.Task.Wait();
            created.TrySetResult();
            return late;
        });
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Native.QueueChannel(channel, "id", 1000, false, null, Measurer);
        release.TrySetResult();
        await created.Task.WaitAsync(TimeSpan.FromSeconds(5));

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!Disposed(late)) await Task.Delay(10, timeout.Token);
    }

    [Fact]
    public async Task ReplacedChannelReportsNoErrors()
    {
        var channel = Channel();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var failed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var errors = 0;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Native.QueueChannel(channel, "id", 1000, false, (_, _) => Interlocked.Increment(ref errors), () => {
            entered.TrySetResult();
            release.Task.Wait();
            failed.TrySetResult();
            throw new InvalidOperationException("setup failed");
        });
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        // A replacement without an error callback, whose setup fails as well.
        var second = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Native.QueueChannel(channel, "id", 1000, false, null, () => {
            second.TrySetResult();
            throw new InvalidOperationException("setup failed");
        });
        release.TrySetResult();
        await Task.WhenAll(failed.Task, second.Task).WaitAsync(TimeSpan.FromSeconds(5));
        await Task.Delay(200);

        Assert.Equal(0, Volatile.Read(ref errors));
    }

    /// <summary>
    /// An exception from the host's error callback must not escape into the background setup.
    /// </summary>
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
    public async Task NetworkFailureIsReportedAndRetriedByTheShim()
    {
        var channel = Channel();
        var attempts = 0;
        var reported = new TaskCompletionSource<EmoteErrorCode>(TaskCreationOptions.RunContinuationsAsynchronously);
        Native.QueueChannel(channel, "id", 5000, false, (code, _) => reported.TrySetResult(code), () => {
            if (Interlocked.Increment(ref attempts) == 1)
                throw new System.Net.Http.HttpRequestException("offline");
            return Measurer();
        });
        Assert.Equal(EmoteErrorCode.ChannelInitializationFailed, await reported.Task.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.NotEqual(0, await MeasureOncePublished(channel, "hi"));
        Assert.Equal(2, attempts);
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
