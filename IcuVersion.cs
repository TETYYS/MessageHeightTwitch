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
    private static readonly Lazy<(nint Data, nint Common)> LinuxLibraries = new(LoadLinuxLibraries);

    internal static nint LinuxCommonLibrary => LinuxLibraries.Value.Common;

#pragma warning disable CA2255
    [ModuleInitializer]
    internal static void Confine()
    {
        if (OperatingSystem.IsLinux())
            _ = LinuxLibraries.Value;
        Wrapper.ConfineIcuVersions(Major, Major);
    }
#pragma warning restore CA2255

    private static (nint Data, nint Common) LoadLinuxLibraries()
    {
        var directory = Path.Combine(LibraryDirectory(), "icu78");

        // Load the dependency first: libicuuc's DT_NEEDED refers to this exact SONAME.
        // icu.net subsequently opens the already-loaded libicuuc.so.78 by SONAME. Keeping
        // these files outside its discovery directories avoids its LD_LIBRARY_PATH mutation.
        // Do not free either handle while ICU iterators or icu.net can still use them.
        var data = NativeLibrary.Load(Path.Combine(directory, $"libicudata.so.{Major}"));
        var common = NativeLibrary.Load(Path.Combine(directory, $"libicuuc.so.{Major}"));
        return (data, common);
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
