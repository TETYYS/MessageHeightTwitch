using System;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.CompilerServices;
using MessageHeightTwitch.Emotes;
using Xunit;
using Mht = MessageHeightTwitch.MessageHeightTwitch;

namespace Tests;

internal static class TestInitialization
{
    // Keep shared catalog loading offline for the lifetime of the test process.
    private static readonly HttpClient Client = new(new FakeHttp(path => FakeHttp.Json(path switch {
        "api.betterttv.net/3/cached/emotes/global" => "[]",
        "api.frankerfacez.com/v1/set/global" => "{\"default_sets\":[],\"sets\":{}}",
        "cdn.frankerfacez.com/static/emoji/v2-.json" => "{\"e\":[]}",
        _ => "{\"emotes\":[]}"
    })));

    [ModuleInitializer]
    internal static void Initialize()
    {
        _ = GlobalEmotes.Shared.Initialize(true, CancellationToken.None, Client);
        // Module initialization cannot wait for callbacks implemented in this test assembly.
        Mht.InitFonts();
    }
}

[Collection("Font directory settings")]
public class InitializationTests
{
    [Fact]
    public async Task ManagedGlobalFailuresPropagateAndCanBeRetried()
    {
        var fail = true;
        using var client = new HttpClient(new FakeHttp(path => fail
            ? new(System.Net.HttpStatusCode.ServiceUnavailable)
            : FakeHttp.Json(path switch {
                "api.betterttv.net/3/cached/emotes/global" => "[]",
                "api.frankerfacez.com/v1/set/global" => "{\"default_sets\":[],\"sets\":{}}",
                _ => "{\"e\":[]}"
            })));
        var globals = new GlobalEmotes();
        await Assert.ThrowsAsync<HttpRequestException>(() => globals.Initialize(false, CancellationToken.None, client));
        fail = false;
        await globals.Initialize(false, CancellationToken.None, client);
        await globals.GetInitialization(false);
    }

    [Fact]
    public async Task ManagedGlobalCancellationStopsRequests()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var client = new HttpClient(new CancellationHandler(entered, stopped));
        using var cancellation = new CancellationTokenSource();
        var globals = new GlobalEmotes();
        var loading = globals.Initialize(false, cancellation.Token, client);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => loading);
        await stopped.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    private sealed class CancellationHandler(TaskCompletionSource Entered, TaskCompletionSource Stopped) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage Request, CancellationToken Token)
        {
            Entered.TrySetResult();
            try {
                await Task.Delay(Timeout.Infinite, Token);
            }
            finally {
                Stopped.TrySetResult();
            }
            return new(System.Net.HttpStatusCode.OK);
        }
    }
}
