using System;
using System.IO;
using System.Linq;
using BenchmarkDotNet.Attributes;
using Tests;
using Mht = MessageHeightTwitch.MessageHeightTwitch;

namespace MessageHeightBenchmarks;

[MemoryDiagnoser]
[MaxRelativeError(0.01)]
public class MessageHeightTestDataBenchmarks
{
    private PreparedMessage[] _messages = null!;

    [Params(false, true)]
    public bool AsciiOnly { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        /*
         * The library resolves its default Fonts directory from the working directory.
         * Project references copy the fonts and native libraries into the benchmark output.
         */
        Directory.SetCurrentDirectory(AppContext.BaseDirectory);
        var cases = MeasurementTestData.Read<TextMeasurementCase>("text-measurements.json")
            .Where(test => !AsciiOnly || test.Text.All(c => c <= 0x7f)).ToList();
        _messages = new PreparedMessage[cases.Count];

        for (var x = 0; x < cases.Count; x++)
        {
            var test = cases[x];
            var (measurer, twitch) = Fixture.ForCase(test);
            var parameters = test.Parameters(twitch);
            _messages[x] = new PreparedMessage(test.Text, test.DisplayName, measurer, parameters);

            // Exercise every input and initialize lazy font/shaper caches before timing.
            var height = measurer.CalculateMessageHeightEx(test.Text, "tetyys", test.DisplayName, parameters);
            if (test.Height is { } expected && height != expected)
                throw new InvalidDataException(
                    $"{test.Name}: expected height {expected}, measured {height}.");
        }

        Console.WriteLine($"Loaded and validated {_messages.Length} messages from text-measurements.json. One operation measures the entire corpus.");
    }

    [Benchmark]
    public double Measure()
    {
        double checksum = 0;
        foreach (var message in _messages)
            checksum += message.Measurer.CalculateMessageHeightEx(message.Text, "tetyys", message.DisplayName, message.Parameters);
        return checksum;
    }

    private readonly record struct PreparedMessage(
        string Text, string DisplayName, Mht Measurer, Mht.CalculateMessageHeightParams Parameters);
}
