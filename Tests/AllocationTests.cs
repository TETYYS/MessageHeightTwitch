using System;
using System.Linq;
using System.Collections.Generic;
using System.Drawing;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MessageHeightTwitch.Emotes;
using Xunit;
using Mht = MessageHeightTwitch.MessageHeightTwitch;

namespace Tests;

[CollectionDefinition("Allocation measurements", DisableParallelization = true)]
public class AllocationMeasurementCollection;

[Collection("Allocation measurements")]
public class AllocationTests
{
    [Fact]
    public void CachedImageSizeReusesItsTaskWithoutAllocating()
    {
        const string url = "https://example.test/cached.png";
        var globals = new GlobalEmotes();
        var cached = Task.FromResult(new SizeF(40, 80));
        globals.ImageSizes[url] = cached;
        var loading = new EmoteLoading(Globals: globals);
        using var cancellation = new CancellationTokenSource();
        for (var i = 0; i < 100; i++)
            _ = loading.ImageSize(url, cancellation.Token);

        var actual = cached;
        var start = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1000; i++)
            actual = loading.ImageSize(url, cancellation.Token);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - start;

        Assert.Equal(0, allocated);
        Assert.Same(cached, actual);
    }

    [Fact]
    public void NativeImageSchedulingAllocatesNothingForCachedEmotes()
    {
        using var http = new FakeHttp(_ => new(HttpStatusCode.NotFound));
        using var client = new HttpClient(http);
        using var measurer = new Mht("channel", "id", CancellationToken.None, false, client, GlobalEmotes.Shared);
        var emotes = new Dictionary<string, string>();
        for (var i = 0; i < 4; i++) {
            var url = "https://example.test/allocated-" + Guid.NewGuid() + ".png";
            emotes.Add("Emote" + i, url);
            GlobalEmotes.Shared.ImageSizes[url] = Task.FromResult(new SizeF(40, 80));
        }
        var registration = new MessageHeightTwitchStatic.ChannelRegistration(1000, null);
        var urls = emotes.Values;
        for (var i = 0; i < 100; i++)
            registration.LoadImages(measurer, urls);

        var start = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1000; i++)
            registration.LoadImages(measurer, urls);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - start;

        Assert.Equal(0, allocated);
    }

    [Fact]
    public void AsciiLookupAllocatesNoManagedMemory()
    {
        var lookup = MessageHeightTwitch.FontFallbackList.Shared.AsciiRegular!;
        lookup.TryMeasure("Just ", out _);

        var start = GC.GetAllocatedBytesForCurrentThread();
        lookup.TryMeasure("Just ", out _);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - start;

        Assert.Equal(0, allocated);
    }
    [Fact]
    public void WarmCorpusAllocatesNoManagedMemoryAndMatchesBrowserFixtures()
    {
        var messages = MeasurementTestData.Read<TextMeasurementCase>("text-measurements.json").Where(test => test.Height != null).Select(test => {
            var (measurer, twitch) = Fixture.ForCase(test);
            var parameters = test.Parameters(twitch);
            Assert.Equal(test.Height!.Value, measurer.CalculateMessageHeightEx(test.Text, "tetyys", test.DisplayName, parameters));
            return (test.Text, test.DisplayName, Measurer: measurer, Parameters: parameters);
        }).ToArray();

        double measure()
        {
            double sum = 0;
            foreach (var message in messages)
                sum += message.Measurer.CalculateMessageHeightEx(message.Text, "tetyys", message.DisplayName, message.Parameters);
            return sum;
        }

        var checksum1 = measure();

        var start = GC.GetAllocatedBytesForCurrentThread();
        var checksum2 = measure();
        var allocated = GC.GetAllocatedBytesForCurrentThread() - start;

        Assert.Equal(checksum1, checksum2);
        Assert.Equal(0, allocated);
    }

    [Theory]
    [InlineData("w! NaM ffzW")]
    [InlineData("w! NaM ffzX 😀 ffzW")]
    [InlineData("w! NaM z! NaM")]
    [InlineData("ordinary text!")]
    public void WarmModifiersAndLocalizedNamesAllocateNoManagedMemory(string Text)
    {
        var measurer = new Mht(
            (ReadOnlySpan<char> name, out SizeF size) => { size = default; return false; },
            (ReadOnlySpan<char> name, out SizeF size) => { size = new(38, 40); return name is "NaM" or "w!" or "z!"; },
            (ReadOnlySpan<char> name, out SizeF size) => { size = new(28, 28); return name is "ffzW" or "ffzX"; },
            (_, _) => default, _ => false, _ => true);
        var parameters = Mht.CalculateMessageHeightParams.Default;

        // A username at the length limit, in the localized name.
        var username = new string('a', 25);
        const string display = "名前";

        var expected = measurer.CalculateMessageHeightEx(Text, username, display, parameters);

        var start = GC.GetAllocatedBytesForCurrentThread();
        var actual = measurer.CalculateMessageHeightEx(Text, username, display, parameters);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - start;
        Assert.Equal(expected, actual);
        Assert.Equal(0, allocated);
    }

    [Fact]
    public void WarmWorkspaceObservesChangingEmoteSizesAndParameters()
    {
        var size = new SizeF(28, 28);
        var measurer = new Mht(
            (ReadOnlySpan<char> name, out SizeF size) => { size = default; return false; },
            (ReadOnlySpan<char> name, out SizeF found) => { found = size; return name.SequenceEqual("NaM"); },
            (ReadOnlySpan<char> name, out SizeF found) => { found = default; return false; },
            (_, _) => default, _ => false, _ => false);
        var parameters = Mht.CalculateMessageHeightParams.Default;

        const string text = "(hello مرحبا) NaM NaM NaM 漢字";
        var first = measurer.CalculateMessageHeightEx(text, "user", "表示名", parameters);
        size = new(120, 180);
        parameters.NumberOfBadges = 4;
        parameters.LineMargin = false;
        var changed = measurer.CalculateMessageHeightEx(text, "user", "表示名", parameters);
        Assert.NotEqual(first, changed);
    }

}
