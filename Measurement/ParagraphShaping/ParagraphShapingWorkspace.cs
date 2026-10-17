using System;
using System.Collections.Generic;

using HarfBuzzSharp;

using MessageHeightTwitch.Measurement.Shared.Shaping;

namespace MessageHeightTwitch.Measurement.ParagraphShaping;

/// <summary>
/// Scratch for stage 3 (paragraph shaping), separate from stage 4's line-edge reshaping.
/// </summary>
/// <remarks>
/// Cache usage: per measurement workspace, one WidthBuffers, a 1,024-entry bidi list (8 KiB of payload), and
/// a reserved 32-entry script bracket stack (256 bytes of payload). These retain capacity across measurements;
/// Dispose returns the width arrays to their shared pools. Object headers and list overhead are additional.
/// </remarks>
internal sealed class ParagraphShapingWorkspace : IDisposable
{
    internal readonly UScript.ScriptContext Script = new();
    internal readonly List<(int End, Direction Direction)> Bidi = new(1024);
    internal readonly TextWidths.WidthBuffers Widths = new();

    internal ParagraphShapingWorkspace() => Script.ReserveBrackets();

    public void Dispose() => Widths.Return();
}
