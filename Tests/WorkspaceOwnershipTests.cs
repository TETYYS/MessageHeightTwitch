using System;
using System.Drawing;
using System.Linq;
using MessageHeightTwitch;
using MessageHeightTwitch.Measurement.InlineCollection;
using MessageHeightTwitch.Measurement.LineFitting;
using MessageHeightTwitch.Measurement.ParagraphShaping;
using MessageHeightTwitch.Measurement.Shared;
using MessageHeightTwitch.Measurement.Shared.Shaping;

using Xunit;
using Mht = MessageHeightTwitch.MessageHeightTwitch;

namespace Tests;

public class WorkspaceOwnershipTests
{
    [Fact]
    public void PreparedParagraphSurvivesCollectionAndShapingScratchReuse()
    {
        using var collection = new CollectionWorkspace();
        using var shaping = new ShapingWorkspace();
        using var preparation = new ParagraphShapingWorkspace();
        using var breaking = new LineBreakingWorkspace();
        using var paragraph = new InlineItemsData();
        using var measurer = Fixture.CreateTextMeasurer(null);
        var parameters = Mht.CalculateMessageHeightParams.Default;
        var message = collection.Modifiers.Rewrite("office affinity مرحبا بالعالم ’. office affinity مرحبا بالعالم", parameters, measurer.Emotes);
        var builder = new ChatParagraphBuilder(message, "TETYYS", PARAGRAPH_REGION.LOGIN, "", collection, paragraph);
        builder.Build();
        var shaper = new ParagraphShaper(FontFallbackList.Shared, paragraph, preparation, shaping);
        shaper.Prepare();
        var breaker = new ParagraphLineBreaker(paragraph, shaper, shaping, breaking, 293 + 1 / 64f, parameters.EmoteMargin);
        var initial = new SizeF(0, Fonts.TWITCH_FONT_SIZE);
        var expected = breaker.NextLine(0, paragraph.Length, initial, true);
        var text = paragraph.Text.ToString();
        var runs = paragraph.Runs.ToArray();
        var bidi = paragraph.Bidi.ToArray();
        var positions = paragraph.Positions.ToArray();
        var marks = paragraph.Marks.AsSpan(0, paragraph.Length).ToArray();

        // A prepared paragraph owns its results even after the producing stages reuse their temporary storage.
        collection.Modifiers.Rewrite("different source", parameters, measurer.Emotes);
        const string unrelated = "unrelated 漢字 بب";
        var classified = FontFallbackList.Shared.Classify(unrelated, false, new UScript.ScriptContext(), shaping);
        var widths = new TextWidths(classified, shaping, preparation.Widths);
        Assert.True(widths.Measure(unrelated.Length) > 0);
        breaking.BeginParagraph();

        Assert.Equal(expected, breaker.NextLine(0, paragraph.Length, initial, true));
        Assert.Equal(text, paragraph.Text.ToString());
        Assert.Equal(runs, paragraph.Runs);
        Assert.Equal(bidi, paragraph.Bidi);
        Assert.Equal(positions, paragraph.Positions);
        Assert.Equal(marks, paragraph.Marks.AsSpan(0, paragraph.Length).ToArray());
    }
}
