using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

internal static unsafe partial class HarfBuzzInstancer
{
    internal const int RegularWeight = 400;
    internal const int BoldWeight = 700;
    private const string Library = "libHarfBuzzSharp";
    private const int DuplicateMemory = 0;
    private static readonly uint WeightAxis = Sfnt.Tag("wght");

    internal static byte[] Instance(byte[] Font, int Weight)
    {
        nint blob, face = 0, input = 0, instance = 0, result = 0;
        fixed (byte* data = Font)
            blob = hb_blob_create(data, checked((uint)Font.Length), DuplicateMemory, 0, 0);
        try
        {
            face = hb_face_create(blob, 0);
            input = hb_subset_input_create_or_fail();
            if (input == 0)
                throw new InvalidOperationException("hb_subset_input_create_or_fail failed.");
            // Keep every glyph, table and glyph ID; only the variations are applied.
            hb_subset_input_keep_everything(input);
            if (hb_subset_input_pin_all_axes_to_default(input, face) == 0 || hb_subset_input_pin_axis_location(input, face, WeightAxis, Weight) == 0)
                throw new InvalidDataException("Variable font has no usable wght axis.");
            instance = hb_subset_or_fail(face, input);
            if (instance == 0)
                throw new InvalidDataException($"HarfBuzz could not instance the font at wght {Weight}.");
            result = hb_face_reference_blob(instance);
            uint length;
            var bytes = hb_blob_get_data(result, &length);
            return new ReadOnlySpan<byte>(bytes, checked((int)length)).ToArray();
        }
        finally
        {
            hb_blob_destroy(result);
            hb_face_destroy(instance);
            hb_subset_input_destroy(input);
            hb_face_destroy(face);
            hb_blob_destroy(blob);
        }
    }

    [LibraryImport(Library), UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    private static partial nint hb_blob_create(byte* Data, uint Length, int Mode, nint UserData, nint Destroy);

    [LibraryImport(Library), UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    private static partial byte* hb_blob_get_data(nint Blob, uint* Length);

    [LibraryImport(Library), UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    private static partial void hb_blob_destroy(nint Blob);

    [LibraryImport(Library), UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    private static partial nint hb_face_create(nint Blob, uint Index);

    [LibraryImport(Library), UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    private static partial nint hb_face_reference_blob(nint Face);

    [LibraryImport(Library), UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    private static partial void hb_face_destroy(nint Face);

    [LibraryImport(Library), UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    private static partial nint hb_subset_input_create_or_fail();

    [LibraryImport(Library), UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    private static partial void hb_subset_input_keep_everything(nint Input);

    [LibraryImport(Library), UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    private static partial int hb_subset_input_pin_all_axes_to_default(nint Input, nint Face);

    [LibraryImport(Library), UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    private static partial int hb_subset_input_pin_axis_location(nint Input, nint Face, uint Axis, float Value);

    [LibraryImport(Library), UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    private static partial nint hb_subset_or_fail(nint Face, nint Input);

    [LibraryImport(Library), UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    private static partial void hb_subset_input_destroy(nint Input);
}
