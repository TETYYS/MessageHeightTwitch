using System;
using System.Runtime.InteropServices;
using Icu;

using MessageHeightTwitch.Measurement.Shared.Shaping;

namespace MessageHeightTwitch;

/*
 * icu.net exposes Script but not uscript_getScriptExtensions. Bind to the same
 * initialized ICU version so script data and bracket properties stay in sync.
 */
/// <summary>Binds ICU script-extension and paired-bracket APIs from the initialized ICU library.</summary>
/// <remarks>
/// Cache usage: three lazy values retain one native library handle/version and two function addresses for
/// the process lifetime. Unicode data belongs to ICU; no per-character results are cached here.
/// </remarks>
internal static unsafe class ICUExtensions
{
    private static readonly Lazy<(nint Handle, string Major)> Library = new(() => {
        Wrapper.Init();
        var major = Wrapper.IcuVersion.Split('.')[0];
        if (OperatingSystem.IsLinux())
            return (IcuVersion.LinuxCommonLibrary, major);
        /*
         * Windows and macOS: assembly directory, then the OS loader. Linux uses the
         * absolute-path handle preloaded before icu.net initialization.
         */
        return (NativeLibrary.Load(LibraryName(major, OperatingSystem.IsWindows(), OperatingSystem.IsMacOS()),
            typeof(ICUExtensions).Assembly, null), major);
    });

    internal static string LibraryName(string Major, bool Windows, bool MacOS)
    {
        if (Windows) return $"icuuc{Major}.dll";
        return MacOS ? $"libicuuc.{Major}.dylib" : $"libicuuc.so.{Major}";
    }

    internal static nint Bind(string Name) => Bind(Library.Value.Handle, Name, Library.Value.Major);

    /// <summary>ICU builds that rename their symbols export them with the major version as a suffix.</summary>
    internal static nint Bind(nint Library, string Name, string Major) =>
        NativeLibrary.TryGetExport(Library, Name, out var address) ? address : NativeLibrary.GetExport(Library, $"{Name}_{Major}");

    /// <summary>Throws for an ICU error code (warnings are negative).</summary>
    internal static void Check(int Status)
    {
        if (Status > 0)
            throw new InvalidOperationException($"ICU operation failed: {Status}.");
    }

    private static readonly Lazy<nint> Function = new(() => Bind("uscript_getScriptExtensions"));
    private static readonly Lazy<nint> PairedBracket = new(() => Bind("u_getBidiPairedBracket"));
    internal static int Get(int Codepoint, Span<UScriptCode> Destination)
    {
        var get = (delegate* unmanaged[Cdecl]<int, int*, int, int*, int>)Function.Value;
        var status = 0;
        fixed (UScriptCode* data = Destination) {
            var count = get(Codepoint, (int*)data, Destination.Length, &status);
            Check(status);
            return count;
        }
    }

    internal static int GetPairedBracket(int Codepoint) => ((delegate* unmanaged[Cdecl]<int, int>)PairedBracket.Value)(Codepoint);

}
