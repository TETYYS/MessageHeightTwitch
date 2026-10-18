using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

using HarfBuzzSharp;

namespace MessageHeightTwitch.Measurement.Shared.Shaping;

/// <summary>
/// Reusable ICU paragraph bidi resolver. Native text is detached before caller spans are unpinned
/// </summary>
internal sealed unsafe class BidiParagraph : IDisposable
{
    private static readonly char* EmptyText = (char*)NativeMemory.AllocZeroed((nuint)sizeof(char));
    private static readonly delegate* unmanaged[Cdecl]<int, int, int*, nint> BidiOpen = (delegate* unmanaged[Cdecl]<int, int, int*, nint>)ICUExtensions.Bind("ubidi_openSized");
    private static readonly delegate* unmanaged[Cdecl]<nint, void> BidiClose = (delegate* unmanaged[Cdecl]<nint, void>)ICUExtensions.Bind("ubidi_close");
    private static readonly delegate* unmanaged[Cdecl]<nint, char*, int, byte, byte*, int*, void> BidiSet = (delegate* unmanaged[Cdecl]<nint, char*, int, byte, byte*, int*, void>)ICUExtensions.Bind("ubidi_setPara");
    private static readonly delegate* unmanaged[Cdecl]<nint, int, int*, byte*, void> BidiRun = (delegate* unmanaged[Cdecl]<nint, int, int*, byte*, void>)ICUExtensions.Bind("ubidi_getLogicalRun");
    private nint HBidi;

    internal BidiParagraph()
    {
        var status = 0;
        HBidi = BidiOpen(0, 0, &status);
        try { Check(status); } catch { Dispose(); throw; }
    }

    private static void Check(int Status) => ICUExtensions.Check(Status);

    internal void GetBidi(ReadOnlySpan<char> Text, List<(int End, Direction Direction)> Result)
    {
        Result.Clear();
        var latin = true;
        foreach (var c in Text) {
            // InlineNode::SegmentText also skips bidi for Latin-1 containing only object replacements above U+00FF.
            if (c >= '\u0100' && c != InlineItemsData.OBJECT_REPLACEMENT) {
                latin = false;
                break;
            }
        }

        if (latin) {
            Result.Add((Text.Length, Direction.LeftToRight));
            return;
        }

        // As in Blink's BidiParagraph::SetParagraph, resolve the actual paragraph, including its real neutral characters.
        fixed (char* data = Text) {
            var status = 0;
            try {
                BidiSet(HBidi, data, Text.Length, 0, null, &status);
                Check(status);

                for (var start = 0;start < Text.Length;) {
                    int end;
                    byte level;
                    BidiRun(HBidi, start, &end, &level);
                    Result.Add((end, (level & 1) == 0 ? Direction.LeftToRight : Direction.RightToLeft));
                    start = end;
                }
            } finally {
                status = 0;
                BidiSet(HBidi, EmptyText, 0, 0, null, &status);
            }
        }
    }

    public void Dispose()
    {
        if (HBidi != 0) { BidiClose(HBidi); HBidi = 0; }
        GC.SuppressFinalize(this);
    }

    ~BidiParagraph() => Dispose();
}
