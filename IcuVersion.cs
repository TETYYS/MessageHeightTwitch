using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Icu;

namespace MessageHeightTwitch;

/// <summary>
/// Pins the ICU version used by the library.
/// </summary>
internal static class IcuVersion
{
    private const int Major = 78;
    private static readonly Lazy<nint> LinuxLibrary = new(LoadLinuxLibrary);

    internal static nint LinuxCommonLibrary => LinuxLibrary.Value;

#pragma warning disable CA2255
    [ModuleInitializer]
    internal static void Confine()
    {
        Wrapper.ConfineIcuVersions(Major, Major);
        if (OperatingSystem.IsLinux())
            _ = LinuxLibrary.Value;
    }
#pragma warning restore CA2255

    /// <summary>
    /// Loads the bundled libicuuc with custom data
    /// </summary>
    private static nint LoadLinuxLibrary()
    {
        var directory = Path.Combine(LibraryDirectory(), "icu78");
        // Do not free the handle while ICU iterators or icu.net can still use it.
        var library = NativeLibrary.Load(Path.Combine(directory, $"libicuuc.so.{Major}"));

        /*
         * icu.net opens a library by path only from a directory it was given, and then prepends that directory to
         * LD_LIBRARY_PATH for child processes, where it would shadow their system ICU. Its loader runs once, in Init,
         * so the variable is restored right after.
         */
        var searchPath = Environment.GetEnvironmentVariable("LD_LIBRARY_PATH");
        try {
            Wrapper.SetPreferredIcu4cDirectory(directory);
            Wrapper.Init();
        } finally {
            Environment.SetEnvironmentVariable("LD_LIBRARY_PATH", searchPath);
        }
        return library;
    }

    [UnconditionalSuppressMessage("SingleFile", "IL3000", Justification = "Empty Assembly.Location is handled for single-file and Native AOT deployments.")]
    private static unsafe string LibraryDirectory()
    {
        var location = typeof(IcuVersion).Assembly.Location;
        if (!string.IsNullOrEmpty(location))
            return Path.GetDirectoryName(location)!;
        if (RuntimeFeature.IsDynamicCodeSupported)
            return AppContext.BaseDirectory;

        // In a Native AOT shared library, BaseDirectory belongs to the native host
        // (e.g. Python or Go). Locate our own ELF module using an address inside it.
        if (GetLibraryInfo((nint)(delegate* unmanaged<void>)&LibraryAnchor, out var info) == 0 || info.FileName == 0)
            throw new InvalidOperationException("Cannot locate the native MessageHeightTwitch library for bundled ICU loading.");
        return Path.GetDirectoryName(Path.GetFullPath(Marshal.PtrToStringUTF8(info.FileName)!))!;
    }

    [UnmanagedCallersOnly]
    private static void LibraryAnchor() { }

    [StructLayout(LayoutKind.Sequential)]
    private struct LibraryInfo
    {
        internal nint FileName;
        internal nint BaseAddress;
        internal nint SymbolName;
        internal nint SymbolAddress;
    }

    [DllImport("__Internal", EntryPoint = "dladdr")]
    private static extern int GetLibraryInfo(nint Address, out LibraryInfo Info);
}
