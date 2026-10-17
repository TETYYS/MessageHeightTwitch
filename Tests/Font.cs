using System;
using System.IO;
using System.Threading.Tasks;
using MessageHeightTwitch;
using Xunit;
using Mht = MessageHeightTwitch.MessageHeightTwitch;

namespace Tests
{
    // These tests temporarily change process-wide environment and working-directory settings.
    [CollectionDefinition("Font directory settings", DisableParallelization = true)]
    public class FontDirectorySettingsCollection { }

    // The bundled fonts are fixed; only the directory holding them can be chosen.
    [Collection("Font directory settings")]
    public class Font : IDisposable
    {
        private const string Variable = "MESSAGEHEIGHTTWITCH_FONTDIR";
        private readonly string? OriginalFontDir = Environment.GetEnvironmentVariable(Variable);
        private readonly string OriginalWorkingDirectory = Environment.CurrentDirectory;

        [Fact]
        public void ExplicitDirectoryWinsOverTheEnvironment()
        {
            Environment.SetEnvironmentVariable(Variable, Path.Combine(Path.GetTempPath(), "from-environment"));
            var explicitDirectory = Path.Combine(Path.GetTempPath(), "explicit") + Path.DirectorySeparatorChar;

            Assert.Equal(Path.Combine(Path.GetTempPath(), "explicit"), FontGlobalContext.ResolveDirectory(explicitDirectory));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public void MissingDirectoryComesFromTheEnvironment(string? FontDir)
        {
            Environment.SetEnvironmentVariable(Variable, Path.Combine(Path.GetTempPath(), "from-environment"));

            Assert.Equal(Path.Combine(Path.GetTempPath(), "from-environment"), FontGlobalContext.ResolveDirectory(FontDir));
        }

        [Fact]
        public void WithoutEitherTheFontsFolderOfTheWorkingDirectoryIsUsed()
        {
            Environment.SetEnvironmentVariable(Variable, null);
            Environment.CurrentDirectory = AppContext.BaseDirectory;

            Assert.Equal(Path.Combine(Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory), "Fonts"), FontGlobalContext.ResolveDirectory(null));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public async Task RepeatedInitializationWithoutADirectoryReusesTheLoadedFontSet(string? FontDir)
        {
            var loaded = FontGlobalContext.Shared;
            Environment.SetEnvironmentVariable(Variable, Path.Combine(Path.GetTempPath(), "from-environment"));

            await Mht.Init(FontDir);

            Assert.Same(loaded, FontGlobalContext.Shared);
        }

        [Fact]
        public void DirectoriesDifferingOnlyInCaseShareOneFontSet()
        {
            var loaded = FontGlobalContext.Shared;

            Assert.Same(loaded, FontGlobalContext.Load(loaded.Directory.ToUpperInvariant() + Path.DirectorySeparatorChar));
        }

        [Fact]
        public void AnotherDirectoryIsRejectedOnceFontsAreLoaded()
        {
            _ = FontGlobalContext.Shared;

            Assert.Throws<ArgumentException>(() => FontGlobalContext.Load(Path.Combine(Path.GetTempPath(), "other-fonts")));
        }

        public void Dispose()
        {
            Environment.SetEnvironmentVariable(Variable, OriginalFontDir);
            Environment.CurrentDirectory = OriginalWorkingDirectory;
        }
    }
}
