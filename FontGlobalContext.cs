using System;
using System.IO;
using System.Threading;

namespace MessageHeightTwitch;

/// <summary>
/// Publishes the process font cache and fixed chat fallback list together after successful initialization.
/// </summary>
internal sealed class FontGlobalContext
{
    internal readonly FontCache Cache;
    internal readonly FontFallbackList FallbackList;
    internal string Directory => Cache.Directory;

    private FontGlobalContext(string Directory)
    {
        Cache = new FontCache(Directory);
        FallbackList = new FontFallbackList(Cache);
    }

    private static readonly Lock ContextLock = new();
    private static FontGlobalContext? SharedContext;

    /// <summary>
    /// The process font context, loaded from the default directory if not explicitly initialized yet.
    /// </summary>
    internal static FontGlobalContext Shared => Volatile.Read(ref SharedContext) ?? Load(null);

    /// <summary>
    /// Loads the process font context from <paramref name="FontDir"/> on first use.
    /// </summary>
    internal static FontGlobalContext Load(string? FontDir)
    {
        if (string.IsNullOrEmpty(FontDir) && Volatile.Read(ref SharedContext) is { } loaded)
            return loaded;

        var directory = ResolveDirectory(FontDir);
        if (Volatile.Read(ref SharedContext) is not { } fonts) {
            lock (ContextLock) {
                fonts = SharedContext;
                if (fonts == null) {
                    fonts = new FontGlobalContext(directory);
                    fonts.Cache.InitializeShapers();
                    Volatile.Write(ref SharedContext, fonts);
                }
            }
        }

        if (!string.Equals(fonts.Directory, directory, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException($"Fonts are already loaded from '{fonts.Directory}'; a process uses one font directory.", nameof(FontDir));
        return fonts;
    }

    internal static string ResolveDirectory(string? FontDir)
    {
        var directory = string.IsNullOrEmpty(FontDir) ?
            Environment.GetEnvironmentVariable("MESSAGEHEIGHTTWITCH_FONTDIR") :
            FontDir;
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(string.IsNullOrEmpty(directory) ? Path.Combine(Environment.CurrentDirectory, "Fonts") : directory));
    }
}
