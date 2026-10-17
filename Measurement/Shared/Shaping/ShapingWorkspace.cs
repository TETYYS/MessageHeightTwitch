using System;
using System.Collections.Generic;

using HarfBuzzSharp;

namespace MessageHeightTwitch.Measurement.Shared.Shaping;

/// <summary>
/// Font-shaping scratch shared by stage 3 (paragraph shaping) and stage 4 (line fitting).
/// </summary>
/// <remarks>
/// Cache usage: per measurement workspace, six lists initially sized for 1,024 entries, a 128-entry tried-face
/// list, about 2 MiB of rented script scratch, and a 32-entry script bracket stack. The reusable HarfBuzz buffer
/// and ICU bidi resolver add native storage that varies with input.
/// </remarks>
internal sealed class ShapingWorkspace : IDisposable
{
    internal readonly HarfBuzzSharp.Buffer Buffer = new();
    internal readonly BidiParagraph BidiResolver = new();
    internal readonly UScript.ScriptContext ProbeScript = new();
    internal readonly UScript.ScriptScratch Scratch = new();
    internal readonly List<(Range Range, UScriptCode Script)> Scripts = new(1024);
    internal readonly List<(Range Range, EmojiPresentation.PRIORITY Priority)> Presentations = new(1024);
    internal readonly List<FontRun> Runs = new(1024);
    internal readonly List<(int End, Direction Direction)> Bidi = new(1024);
    internal readonly List<Range> Pending = new(1024);
    internal readonly List<Range> Next = new(1024);
    internal readonly List<Typeface> Tried = new(128);
    internal Typeface? LastShapedFont;
    internal Range LastShapedRange;
#if TEST
    internal readonly record struct FallbackAttempt(int Hint, string? Family, bool AlreadyTried, int Start, int End, string Pending);
    internal List<FallbackAttempt>? FallbackAttempts;
#endif

    internal ShapingWorkspace()
    {
        Scratch.Ensure(MessageHeightTwitch.MAX_PARAGRAPH_UTF16);
        ProbeScript.ReserveBrackets();
    }

    public void Dispose()
    {
        Buffer.Dispose();
        BidiResolver.Dispose();
        Scratch.Dispose();
    }
}
