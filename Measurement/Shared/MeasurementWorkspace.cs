using System;

using MessageHeightTwitch.Measurement.LineFitting;
using MessageHeightTwitch.Measurement.ParagraphShaping;
using MessageHeightTwitch.Measurement.Shared.Shaping;

using Microsoft.Extensions.ObjectPool;

namespace MessageHeightTwitch.Measurement.Shared;

/// <summary>Owns pooled resources for one measurement. Stages receive their own workspace and explicit inputs.</summary>
/// <remarks>
/// Cache usage: the process pool retains at most max(4, processor count) idle workspaces; concurrent calls can
/// create more. Each owns the collection, shaping, paragraph-shaping, line-breaking and inline-data buffers
/// documented by those classes, including about 2 MiB of script scratch. Buffer capacities survive pool return;
/// Dispose releases native resources and returns rented arrays, whose shared pools may retain them.
/// </remarks>
internal sealed class MeasurementWorkspace : IDisposable
{
    internal static readonly ObjectPool<MeasurementWorkspace> Pool = new DefaultObjectPoolProvider { MaximumRetained = Math.Max(4, Environment.ProcessorCount) }.Create<MeasurementWorkspace>();

    internal readonly CollectionWorkspace Collection = new();
    internal readonly ShapingWorkspace Shaping = new();
    internal readonly ParagraphShapingWorkspace ParagraphShaping = new();
    internal readonly LineBreakingWorkspace LineBreaking = new();
    internal readonly InlineItemsData Paragraph = new();

    public void Dispose()
    {
        Collection.Dispose();
        Shaping.Dispose();
        ParagraphShaping.Dispose();
        LineBreaking.Dispose();
        Paragraph.Dispose();
    }
}
