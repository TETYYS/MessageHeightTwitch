using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MessageHeightTwitch.Emotes;
using Xunit;
using EmoteErrorCode = MessageHeightTwitchStatic.EmoteErrorCode;
using Mht = MessageHeightTwitch.MessageHeightTwitch;

namespace Tests;

[CollectionDefinition("Emote loading", DisableParallelization = true)]
public class EmoteLoadingCollection { }

[Collection("Emote loading")]
public class EmoteLoadingTests
{
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> Reply) : HttpMessageHandler
    {
        internal readonly ConcurrentDictionary<string, int> Requests = new();
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage Request, CancellationToken Token)
        {
            Requests.AddOrUpdate(Request.RequestUri!.AbsolutePath, 1, (_, count) => count + 1);
            return Reply(Request, Token);
        }
    }
    private static TaskCompletionSource NewTcs() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static HttpResponseMessage Json(string Text) => new(HttpStatusCode.OK) { Content = new StringContent(Text) };
    private const string Global = """
        {"emotes":[{"name":"Shared","data":{"host":{"files":[{"name":"1x.png","width":32,"height":32}]}}}]}
        """;
    private const string Channel = """
        {"emote_set":{"emotes":[{"name":"Shared","data":{"host":{"files":[{"name":"1x.png","width":96,"height":20}]}}}]}}
        """;
    private static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aE1sAAAAASUVORK5CYII=");

    [Fact]
    public async Task ManagedProviderThrowsWithoutRetryingAndCanBeCalledAgain()
    {
        var attempts = 0;
        using var handler = new Handler((request, _) => Task.FromResult(request.RequestUri!.AbsolutePath.EndsWith("global")
            ? Interlocked.Increment(ref attempts) == 1 ? new(HttpStatusCode.ServiceUnavailable) : Json(Global)
            : Json(Channel)));
        using var client = new HttpClient(handler);
        var provider = new SevenTVEmoteProvider(new(Client: client, Globals: new()));
        await Assert.ThrowsAsync<HttpRequestException>(() => provider.Initialize("id", CancellationToken.None));
        Assert.Equal(1, attempts);
        Assert.True(provider.TryGetEmote("Shared", out var channelSize));
        Assert.Equal(new SizeF(96, 20), channelSize);
        await provider.Initialize("id", CancellationToken.None);
        Assert.True(provider.TryGetEmote("Shared", out var size));
        Assert.Equal(channelSize, size);
    }

    [Fact]
    public async Task NativeRetriesUseFreshTimeoutTokensAndReportFailures()
    {
        var tokens = new List<CancellationToken>();
        var errors = new List<Exception>();
        await MessageHeightTwitchStatic.Retry(async token => {
            tokens.Add(token);
            if (tokens.Count == 1)
                await Task.Delay(Timeout.Infinite, token);
            else
                token.ThrowIfCancellationRequested();
        }, 20, errors.Add, CancellationToken.None, (_, _) => Task.CompletedTask).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.IsAssignableFrom<OperationCanceledException>(Assert.Single(errors));
        Assert.Equal(2, tokens.Count);
        Assert.NotEqual(tokens[0], tokens[1]);
    }

    [Fact]
    public async Task NativeBackoffIsCappedAndThrowingCallbacksDoNotStopRecovery()
    {
        var delays = new List<double>();
        var attempts = 0;
        var errors = 0;
        await MessageHeightTwitchStatic.Retry(_ => {
            if (++attempts <= 8) throw new HttpRequestException("offline");
            return Task.CompletedTask;
        }, 1000, _ => { errors++; throw new InvalidOperationException("consumer failure"); }, CancellationToken.None,
            (delay, _) => { delays.Add(delay.TotalSeconds); return Task.CompletedTask; });
        Assert.Equal(9, attempts);
        Assert.Equal(8, errors);
        Assert.Equal(new double[] { 1, 2, 4, 8, 16, 30, 30, 30 }, delays);
    }

    [Fact]
    public async Task NativeCancellationStopsBackoffWithoutReportingCancellation()
    {
        using var lifetime = new CancellationTokenSource();
        var paused = NewTcs();
        var errors = 0;
        var attempts = 0;
        var loading = MessageHeightTwitchStatic.Retry(_ => {
            attempts++;
            throw new HttpRequestException("offline");
        }, 1000, _ => errors++, lifetime.Token, (_, token) => {
            paused.TrySetResult();
            return Task.Delay(Timeout.Infinite, token);
        });
        await paused.Task.WaitAsync(TimeSpan.FromSeconds(5));
        lifetime.Cancel();
        await loading.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, attempts);
        Assert.Equal(1, errors);
    }

    [Fact]
    public async Task TwitchImagesAreLoadedExplicitlyAndFailuresAreThrown()
    {
        var attempts = 0;
        using var handler = new Handler((_, _) => Task.FromResult(Interlocked.Increment(ref attempts) == 1
            ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            : new(HttpStatusCode.OK) { Content = new ByteArrayContent(Png) }));
        using var client = new HttpClient(handler);
        var emotes = new TwitchEmotes(new(Client: client, Globals: new()));
        const string url = "https://example.test/image.png";
        Assert.Equal(new SizeF(28, 28), emotes.GetEmote("Emote", url));
        Assert.Equal(0, attempts);
        await Assert.ThrowsAsync<HttpRequestException>(() => emotes.Load([url], CancellationToken.None));
        await emotes.Load([url, url], CancellationToken.None);
        Assert.Equal(new SizeF(1, 1), emotes.GetEmote("Emote", url));
        Assert.Equal(2, attempts);
    }

    [Fact]
    public async Task ConcurrentRequestsForOneImageJoinOneDownloadAndLaterReuseItsSize()
    {
        var release = NewTcs();
        using var handler = new Handler(async (_, token) => {
            await release.Task.WaitAsync(token);
            return new HttpResponseMessage(HttpStatusCode.OK) {
                Content = new ByteArrayContent(Png)
            };
        });
        using var client = new HttpClient(handler);
        var globals = new GlobalEmotes();
        const string url = "https://example.test/joined.png";
        var first = new EmoteLoading(Client: client, Globals: globals).ImageSize(url, CancellationToken.None);
        var second = new EmoteLoading(Client: client, Globals: globals).ImageSize(url, CancellationToken.None);
        release.TrySetResult();

        Assert.Equal(new SizeF(1, 1), await first.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(new SizeF(1, 1), await second.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.True(new EmoteLoading(Client: client, Globals: globals).TryGetImageSize(url, out var known));
        Assert.Equal(new SizeF(1, 1), known);
        Assert.Equal(1, handler.Requests["/joined.png"]);
    }

    [Fact]
    public async Task CatalogRequestsJoinWhileInFlightAndRefreshAfterward()
    {
        var release = NewTcs();
        var loads = 0;
        var loading = new EmoteLoading(Globals: new());
        const string url = "https://example.test/catalog.json";
        Func<CancellationToken, Task<string>> load = async token => {
            var attempt = Interlocked.Increment(ref loads);
            await release.Task.WaitAsync(token);
            return attempt.ToString();
        };
        var first = loading.GlobalCatalog(url, load, CancellationToken.None);
        var second = loading.GlobalCatalog(url, load, CancellationToken.None);
        release.TrySetResult();

        Assert.Equal("1", await first.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal("1", await second.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal("2", await loading.GlobalCatalog(url, load, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task CancelledWaiterDoesNotAbortTheDownloadThatOthersStillNeed()
    {
        var release = NewTcs();
        var started = NewTcs();
        using var handler = new Handler(async (_, token) => {
            started.TrySetResult();
            await release.Task.WaitAsync(token);
            return new HttpResponseMessage(HttpStatusCode.OK) {
                Content = new ByteArrayContent(Png)
            };
        });
        using var client = new HttpClient(handler);
        var loading = new EmoteLoading(Client: client, Globals: new());
        const string url = "https://example.test/cancelled-waiter.png";
        using var leaving = new CancellationTokenSource();

        var staying = loading.ImageSize(url, CancellationToken.None);
        var cancelled = loading.ImageSize(url, leaving.Token);

        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        leaving.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled).WaitAsync(TimeSpan.FromSeconds(5));
        release.TrySetResult();

        Assert.Equal(new SizeF(1, 1), await staying.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(1, handler.Requests["/cancelled-waiter.png"]);
    }

    [Fact]
    public async Task CancelledOwnerStopsItsRequestAndRemainingWaiterStartsAnother()
    {
        var restarted = NewTcs();
        using var owner = new CancellationTokenSource();
        var attempts = 0;
        using var handler = new Handler(async (_, token) => {
            if (Interlocked.Increment(ref attempts) == 1)
                await Task.Delay(Timeout.Infinite, token);
            restarted.TrySetResult();
            return new(HttpStatusCode.OK) { Content = new ByteArrayContent(Png) };
        });
        using var client = new HttpClient(handler);
        var loading = new EmoteLoading(client, new());
        const string url = "https://example.test/owner.png";
        var cancelled = loading.ImageSize(url, owner.Token);
        var staying = loading.ImageSize(url, CancellationToken.None);
        owner.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled);
        await restarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(new SizeF(1, 1), await staying.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(2, attempts);
    }

    [Fact]
    public async Task CancellingDownloadStopsTheRequestAndAllowsAnotherAttempt()
    {
        var attempts = 0;
        using var handler = new Handler(async (_, token) => {
            if (Interlocked.Increment(ref attempts) == 1)
                await Task.Delay(Timeout.Infinite, token);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Png) };
        });
        using var client = new HttpClient(handler);
        var loading = new EmoteLoading(Client: client, Globals: new());
        using var cancellation = new CancellationTokenSource(50);
        const string url = "https://example.test/stopped.png";

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => loading.ImageSize(url, cancellation.Token)).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(new SizeF(1, 1), await loading.ImageSize(url, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(2, attempts);
    }

    [Fact]
    public async Task TwitchInstancesJoinOneDownloadOfTheSameEmote()
    {
        var release = NewTcs();
        using var handler = new Handler(async (request, token) => {
            await release.Task.WaitAsync(token);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Png) };
        });
        using var client = new HttpClient(handler);
        var globals = new GlobalEmotes();
        const string url = "https://example.test/twitch.png";
        var first = new TwitchEmotes(new EmoteLoading(Client: client, Globals: globals));
        var second = new TwitchEmotes(new EmoteLoading(Client: client, Globals: globals));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var firstLoad = first.Load([url], timeout.Token);
        while (!handler.Requests.ContainsKey("/twitch.png"))
            await Task.Delay(10, timeout.Token);
        var secondLoad = second.Load([url], timeout.Token);
        release.TrySetResult();

        await Task.WhenAll(firstLoad, secondLoad).WaitAsync(timeout.Token);
        Assert.Equal(1, handler.Requests["/twitch.png"]);
    }

    [Fact]
    public async Task QueuedInitializationReturnsBeforeFactoryCompletes()
    {
        var entered = NewTcs();
        var release = NewTcs();
        var error = new TaskCompletionSource<EmoteErrorCode>(TaskCreationOptions.RunContinuationsAsynchronously);
        try {
            var queued = Task.Run(() => MessageHeightTwitchStatic.QueueChannel(Guid.NewGuid().ToString(), "id", 1000,
                false, (code, _) => error.TrySetResult(code), () => {
                    entered.TrySetResult();
                    release.Task.GetAwaiter().GetResult();
                    throw new InvalidOperationException("local setup failed");
                }));
            Assert.Equal(1, await queued.WaitAsync(TimeSpan.FromSeconds(5)));
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(error.Task.IsCompleted);
        } finally { release.TrySetResult(); }
        Assert.Equal(EmoteErrorCode.ChannelInitializationFailed, await error.Task.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task MeasurementsCanReadTheCacheWhileItGrowsInTheBackground()
    {
        var release = NewTcs();
        var readerStarted = NewTcs();
        var entries = Enumerable.Range(0, 5000).Select(index =>
            "{\"name\":\"Emote" + index + "\",\"data\":{\"host\":{\"files\":[{\"name\":\"1x.png\",\"width\":28,\"height\":28}]}}}");
        var catalog = "{\"emotes\":[" + string.Join(",", entries) + "]}";
        using var handler = new Handler(async (request, token) => {
            if (!request.RequestUri!.AbsolutePath.EndsWith("global")) return new(HttpStatusCode.NotFound);
            await release.Task.WaitAsync(token);
            return Json(catalog);
        });
        using var client = new HttpClient(handler);
        var provider = new SevenTVEmoteProvider(new(Client: client, Globals: new()));
        var loading = provider.Initialize("id", CancellationToken.None);
        var reading = Task.Run(() => {
            readerStarted.TrySetResult();
            do {
                provider.TryGetEmote("Emote4999", out _);
                provider.TryGetEmote("missing", out _);
            } while (!loading.IsCompleted);
        });
        try { await readerStarted.Task.WaitAsync(TimeSpan.FromSeconds(5)); }
        finally { release.TrySetResult(); }
        await Task.WhenAll(loading, reading).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(provider.TryGetEmote("Emote4999", out var size));
        Assert.Equal(new SizeF(28, 28), size);
    }

    [Fact]
    public async Task NativeCallbackReceivesContextChannelCodeAndMessageWithNoConsoleOutput()
    {
        var result = new TaskCompletionSource<(string Channel, int Code, string Message)>(TaskCreationOptions.RunContinuationsAsynchronously);
        var handle = GCHandle.Alloc(result);
        var oldOut = Console.Out;
        var oldError = Console.Error;
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();
        try {
            Console.SetOut(stdout);
            Console.SetError(stderr);
            Assert.Equal(0, StartNative(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()), GCHandle.ToIntPtr(handle)));
            var error = await result.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal("", error.Channel);
            Assert.Equal((int)EmoteErrorCode.GlobalInitializationFailed, error.Code);
            Assert.NotEmpty(error.Message);
            Assert.Equal(0, InvalidNativeCalls(GCHandle.ToIntPtr(handle)));
            Assert.Equal("", stdout.ToString());
            Assert.Equal("", stderr.ToString());
        } finally {
            Console.SetOut(oldOut);
            Console.SetError(oldError);
            handle.Free();
        }
    }

    private static unsafe int StartNative(string FontDir, nint Context)
    {
        var fonts = Encoding.UTF8.GetBytes(FontDir + "\0");
        delegate* unmanaged[Cdecl]<byte*, int, byte, delegate* unmanaged[Cdecl]<void*, byte*, int, byte*, void>, void*, int> init = &MessageHeightTwitchStatic.InitMessageHeightTwitch;
        fixed (byte* f = fonts)
            return init(f, 1000, 0, &NativeError, (void*)Context);
    }
    private static unsafe int InvalidNativeCalls(nint Context)
    {
        delegate* unmanaged[Cdecl]<byte*, int, byte, delegate* unmanaged[Cdecl]<void*, byte*, int, byte*, void>, void*, int> init = &MessageHeightTwitchStatic.InitMessageHeightTwitch;
        delegate* unmanaged[Cdecl]<byte*, byte*, byte*, byte*, int, MessageHeightTwitchStatic.TwitchEmote*, int, float> measure = &MessageHeightTwitchStatic.CalculateMessageHeight;
        return init(null, -2, 0, &NativeError, (void*)Context) + (int)measure(null, null, null, null, 0, null, 0);
    }
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static unsafe void NativeError(void* Context, byte* Channel, int Code, byte* Message)
    {
        var completion = (TaskCompletionSource<(string, int, string)>)GCHandle.FromIntPtr((nint)Context).Target!;
        completion.TrySetResult((Marshal.PtrToStringUTF8((nint)Channel)!, Code, Marshal.PtrToStringUTF8((nint)Message)!));
    }
}
