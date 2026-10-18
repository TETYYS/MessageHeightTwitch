using System;
using System.IO;
using BenchmarkDotNet.Attributes;
using Tests;
using Mht = MessageHeightTwitch.MessageHeightTwitch;

namespace MessageHeightBenchmarks;

[MemoryDiagnoser]
[MaxRelativeError(0.01)]
public class SingleEmoteBenchmarks
{
    private Mht _measurer = null!;
    private Mht.CalculateMessageHeightParams _parameters;
    private string _username = null!;
    private string _displayName = null!;

    [Params("ShortName", "LocalizedName", "EmoteWrap", "NameWrap")]
    public string Scenario { get; set; } = null!;

    [GlobalSetup]
    public void Setup()
    {
        Directory.SetCurrentDirectory(AppContext.BaseDirectory);
        _measurer = new Fixture().Mht;
        _parameters = Mht.CalculateMessageHeightParams.Default;
        (_username, _displayName) = Scenario switch {
            "ShortName" => ("tetyys", "tetyys"),
            "LocalizedName" => ("tetyys", "表示名"),
            "EmoteWrap" => (new string('W', 18), new string('W', 18)),
            "NameWrap" => (new string('W', 25), new string('W', 25)),
            _ => throw new ArgumentException(Scenario)
        };
        var height = _measurer.CalculateMessageHeightEx("NaM", _username, _displayName, _parameters);
        var expected = Scenario is "ShortName" or "LocalizedName" ? 38 : 60;
        if (height != expected)
            throw new InvalidDataException($"{Scenario}: expected height {expected}, measured {height}.");
        Console.WriteLine($"{Scenario}: height {height}");
    }

    [Benchmark(OperationsPerInvoke = 256)]
    public double MeasureSingleEmote()
    {
        double checksum = 0;
        for (var i = 0; i < 256; i++)
            checksum += _measurer.CalculateMessageHeightEx("NaM", _username, _displayName, _parameters);
        return checksum;
    }
}
