using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

using Icu;

namespace MessageHeightTwitch.Measurement.LineFitting;

/// <summary>
/// Reusable line and grapheme iterators owned by line breaking.
/// </summary>
internal sealed unsafe class TextBreakIterators : IDisposable
{
    private static readonly char* EmptyText = (char*)NativeMemory.AllocZeroed((nuint)sizeof(char));
    private static readonly delegate* unmanaged[Cdecl]<int, byte*, char*, int, int*, nint> BreakOpen = (delegate* unmanaged[Cdecl]<int, byte*, char*, int, int*, nint>)ICUExtensions.Bind("ubrk_open");
    private static readonly delegate* unmanaged[Cdecl]<nint, void> BreakClose = (delegate* unmanaged[Cdecl]<nint, void>)ICUExtensions.Bind("ubrk_close");
    private static readonly delegate* unmanaged[Cdecl]<nint, char*, int, int*, void> BreakSet = (delegate* unmanaged[Cdecl]<nint, char*, int, int*, void>)ICUExtensions.Bind("ubrk_setText");
    private static readonly delegate* unmanaged[Cdecl]<nint, int> BreakFirst = (delegate* unmanaged[Cdecl]<nint, int>)ICUExtensions.Bind("ubrk_first");
    private static readonly delegate* unmanaged[Cdecl]<nint, int> BreakNext = (delegate* unmanaged[Cdecl]<nint, int>)ICUExtensions.Bind("ubrk_next");
    private nint hLine;
    private nint hCharacter;

    internal TextBreakIterators()
    {
        try {
            var status = 0;
            /*
             * Chromium's ICU makes CSS line-break: normal its default line rules (patches/line_normal.patch): a
             * conditional Japanese starter (small kana, ー) breaks like an ideograph, not like a non-starter.
             */
            fixed (byte* line = "en-US@lb=normal\0"u8)
                hLine = BreakOpen((int)BreakIterator.UBreakIteratorType.LINE, line, null, 0, &status);
            Check(status);
            fixed (byte* locale = "en-US\0"u8)
                hCharacter = BreakOpen((int)BreakIterator.UBreakIteratorType.CHARACTER, locale, null, 0, &status);
            Check(status);
        } catch {
            Dispose();
            throw;
        }
    }

    private static void Check(int Status) => ICUExtensions.Check(Status);

    internal void GetBreaks(ReadOnlySpan<char> Text, bool Characters, List<(int Start, int End)> Result, int StartOffset = 0)
    {
        Result.Clear();
        var iterator = Characters ? hCharacter : hLine;
        fixed (char* data = Text[StartOffset..]) {
            var status = 0;
            try {
                BreakSet(iterator, data, Text.Length - StartOffset, &status);
                Check(status);
                var start = BreakFirst(iterator);
                for (var end = BreakNext(iterator);end != -1;end = BreakNext(iterator)) {
                    Result.Add((StartOffset + start, StartOffset + end));
                    start = end;
                }
            } finally {
                status = 0;
                BreakSet(iterator, EmptyText, 0, &status);
            }
        }
    }

    public void Dispose()
    {
        if (hLine != 0) {
            BreakClose(hLine);
            hLine = 0;
        }
        if (hCharacter != 0) {
            BreakClose(hCharacter);
            hCharacter = 0;
        }
        GC.SuppressFinalize(this);
    }

    ~TextBreakIterators() => Dispose();
}
