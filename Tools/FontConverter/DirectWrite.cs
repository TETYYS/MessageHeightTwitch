using System.Runtime.InteropServices;

// Unpacks WOFF/WOFF2 with Windows' built-in DirectWrite decoder.
internal static unsafe partial class DirectWrite
{
    /*
     * Native ABI from Windows SDK dwrite_3.h: IDWriteFactory5 (47) and
     * dwrite.h: IDWriteFontFileStream (3/4/5). IUnknown occupies slots 0-2.
     */
    private const int AnalyzeContainerTypeSlot = 46;
    private const int UnpackFontFileSlot = 47;
    private const int ReadFileFragmentSlot = 3;
    private const int ReleaseFileFragmentSlot = 4;
    private const int GetFileSizeSlot = 5;
    private const int WoffContainer = 1;
    private const int Woff2Container = 2;
    private const int MinimumFontSize = 12;
    private const int MaximumFontSize = 128 * 1024 * 1024;

    [LibraryImport("dwrite.dll")]
    private static partial int DWriteCreateFactory(int FactoryType, in Guid Iid, out nint Factory);

    internal static byte[] Unpack(byte[] Compressed)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 15063))
            throw new PlatformNotSupportedException("Unpacking WOFF/WOFF2 requires Windows 10 build 15063 or newer (DirectWrite WOFF2 support).");
        var iid = new Guid("958DB99A-BE2A-4F09-AF7D-65189803D1D3");
        Marshal.ThrowExceptionForHR(DWriteCreateFactory(0, in iid, out var factory));
        try
        {
            fixed (byte* data = Compressed)
            {
                var methods = *(nint**)factory;
                var analyze = (delegate* unmanaged[Stdcall]<nint, void*, uint, int>)methods[AnalyzeContainerTypeSlot];
                var container = analyze(factory, data, checked((uint)Compressed.Length));
                if (container != WoffContainer && container != Woff2Container)
                    throw new InvalidDataException("Input must be a TrueType, OpenType, WOFF or WOFF2 font.");
                var unpack = (delegate* unmanaged[Stdcall]<nint, int, void*, uint, nint*, int>)methods[UnpackFontFileSlot];
                nint stream = 0;
                Marshal.ThrowExceptionForHR(unpack(factory, container, data, checked((uint)Compressed.Length), &stream));
                try
                {
                    var streamMethods = *(nint**)stream;
                    var getSize = (delegate* unmanaged[Stdcall]<nint, ulong*, int>)streamMethods[GetFileSizeSlot];
                    ulong size = 0;
                    Marshal.ThrowExceptionForHR(getSize(stream, &size));
                    if (size < MinimumFontSize || size > MaximumFontSize)
                        throw new InvalidDataException("Unexpected unpacked font size.");
                    var read = (delegate* unmanaged[Stdcall]<nint, void**, ulong, ulong, void**, int>)streamMethods[ReadFileFragmentSlot];
                    void* fragment = null;
                    void* context = null;
                    Marshal.ThrowExceptionForHR(read(stream, &fragment, 0, size, &context));
                    try
                    {
                        return new ReadOnlySpan<byte>(fragment, checked((int)size)).ToArray();
                    }
                    finally
                    {
                        var release = (delegate* unmanaged[Stdcall]<nint, void*, void>)streamMethods[ReleaseFileFragmentSlot];
                        release(stream, context);
                    }
                }
                finally { Marshal.Release(stream); }
            }
        }
        finally { Marshal.Release(factory); }
    }
}
