using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using SkiaSharp;

namespace Tests;

/// <summary>Serves emote catalogs and images from memory, keyed by host and path, so tests never use the network.</summary>
internal sealed class FakeHttp : HttpMessageHandler
{
    private readonly Func<string, HttpResponseMessage> Reply;
    internal readonly ConcurrentDictionary<string, int> Requests = new();

    internal FakeHttp(Func<string, HttpResponseMessage> Reply) => this.Reply = Reply;

    /// <summary>Answers each "host/path" from <paramref name="Routes"/>, and anything else with 404.</summary>
    internal FakeHttp(IReadOnlyDictionary<string, Func<HttpResponseMessage>> Routes)
        : this(key => Routes.TryGetValue(key, out var route) ? route() : new(HttpStatusCode.NotFound)) { }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage Request, CancellationToken Token)
    {
        var key = Request.RequestUri!.Host + Request.RequestUri.AbsolutePath;
        Requests.AddOrUpdate(key, 1, (_, count) => count + 1);
        return Task.FromResult(Reply(key));
    }

    internal static HttpResponseMessage Json(string Text) => new(HttpStatusCode.OK) { Content = new StringContent(Text) };

    internal static HttpResponseMessage Png(int Width, int Height) => new(HttpStatusCode.OK) { Content = new ByteArrayContent(PngBytes(Width, Height)) };

    internal static byte[] PngBytes(int Width, int Height)
    {
        using var bitmap = new SKBitmap(Width, Height);
        using var data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }
}
