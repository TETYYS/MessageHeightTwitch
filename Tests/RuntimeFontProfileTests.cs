using System;
using System.IO;
using System.Security.Cryptography;
using MessageHeightTwitch;
using Xunit;

namespace Tests;

public class RuntimeFontProfileTests
{
    // Minimal independent bundles make sure profile data belongs to the selected
    // font directory, including when a previous directory failed to load.
    private sealed class Bundle : IDisposable
    {
        internal readonly string Directory = Path.Combine(Path.GetTempPath(), "mht-fonts-" + Guid.NewGuid());
        internal const string Font = "Arial-Regular.ttf";

        internal Bundle(int Advance, bool ValidHash = true, int? GlyphCount = null)
        {
            System.IO.Directory.CreateDirectory(Directory);
            File.Copy(Path.Combine(FontGlobalContext.Shared.Directory, Font), Path.Combine(Directory, Font));
            using (var writer = new BinaryWriter(File.Create(Path.Combine(Directory, "WindowsSystemFallbacks.bin")))) {
                writer.Write(0x32465357u);
                writer.Write(1);
                writer.Write(""); writer.Write(0); writer.Write(false);
                writer.Write(0);
            }
            using var blob = HarfBuzzSharp.Blob.FromFile(Path.Combine(Directory, Font));
            using var face = new HarfBuzzSharp.Face(blob, 0);
            using var metrics = new BinaryWriter(File.Create(Path.Combine(Directory, "WindowsFontMetrics.bin")));
            metrics.Write(0x314D4657u);
            metrics.Write(Fonts.TWITCH_FONT_SIZE);
            metrics.Write(1);
            metrics.Write(Font);
            metrics.Write(ValidHash ? SHA256.HashData(File.ReadAllBytes(Path.Combine(Directory, Font))) : new byte[32]);
            metrics.Write(400); metrics.Write(false);
            metrics.Write(GlyphCount ?? face.GlyphCount);
            for (var i = 0; i < (GlyphCount ?? face.GlyphCount); i++)
                metrics.Write(Advance);
        }

        public void Dispose() => System.IO.Directory.Delete(Directory, true);
    }

    [Fact]
    public void DifferentBundlesSupplyTheirOwnMetricsWithoutRecompiling()
    {
        using var first = new Bundle(123);
        using var second = new Bundle(456);
        var a = new FontCache(first.Directory).GetTypeface(Bundle.Font);
        var b = new FontCache(second.Directory).GetTypeface(Bundle.Font);
        Assert.All(a.PlatformAdvances!, value => Assert.Equal(123, value));
        Assert.All(b.PlatformAdvances!, value => Assert.Equal(456, value));
        Assert.DoesNotContain("MessageHeightTwitch.WindowsFontMetrics.bin", typeof(Fonts).Assembly.GetManifestResourceNames());
        Assert.DoesNotContain("MessageHeightTwitch.WindowsSystemFallbacks.bin", typeof(Fonts).Assembly.GetManifestResourceNames());
    }

    [Fact]
    public void FailedBundleDoesNotPoisonLaterInitialization()
    {
        using var invalid = new Bundle(123, ValidHash: false);
        Assert.Throws<InvalidDataException>(() => new FontCache(invalid.Directory));
        using var valid = new Bundle(456);
        Assert.NotNull(new FontCache(valid.Directory).GetTypeface(Bundle.Font));
    }

    [Fact]
    public void ProfileWithDifferentGlyphCountIsRejectedBeforeShaping()
    {
        using var bundle = new Bundle(123, GlyphCount: 1);
        Assert.Throws<InvalidDataException>(() => new FontCache(bundle.Directory));
    }

    [Theory]
    [InlineData("WindowsFontMetrics.bin")]
    [InlineData("WindowsSystemFallbacks.bin")]
    public void MissingProfileIsReportedFromTheSelectedDirectory(string FileName)
    {
        using var bundle = new Bundle(123);
        var path = Path.Combine(bundle.Directory, FileName);
        File.Delete(path);
        Assert.Equal(path, Assert.Throws<FileNotFoundException>(() => new FontCache(bundle.Directory)).FileName);
    }
}
