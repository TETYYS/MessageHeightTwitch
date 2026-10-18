using System;
using System.IO;
using BenchmarkDotNet.Attributes;
using Tests;
using Mht = MessageHeightTwitch.MessageHeightTwitch;

namespace MessageHeightBenchmarks;

[MemoryDiagnoser]
[MaxRelativeError(0.01)]
public class LoginMessageBenchmarks
{
    private Mht _measurer = null!;
    private Mht.CalculateMessageHeightParams _parameters;
    private string _text = null!;
    private string _login = null!;

    [Params("ShortAscii", "WrappedAscii", "ContextualAscii", "Unicode", "NameWrap")]
    public string Scenario { get; set; } = null!;

    [GlobalSetup]
    public void Setup()
    {
        Directory.SetCurrentDirectory(AppContext.BaseDirectory);
        _measurer = Fixture.CreateTextMeasurer(null);
        _parameters = Mht.CalculateMessageHeightParams.Default;
        _login = Scenario == "NameWrap" ? new string('W', 25) : "TETYYS_123";
        _text = Scenario switch {
            "ShortAscii" or "NameWrap" => "hello office affinity AVATAR 1234567890",
            "WrappedAscii" => string.Concat(System.Linq.Enumerable.Repeat("hello AVATAR office 123 ", 8)),
            "ContextualAscii" => "hello -> <=> ==> office affinity",
            "Unicode" => "hello \u65e5\u672c\u8a9e \u0645\u0631\u062d\u0628\u0627 world",
            _ => throw new ArgumentException(Scenario)
        };
        Console.WriteLine($"{Scenario}: height {_measurer.CalculateMessageHeightEx(_text, _login, _login, _parameters)}");
    }

    [Benchmark(OperationsPerInvoke = 256)]
    public double MeasureLoginMessage()
    {
        double checksum = 0;
        for (var i = 0; i < 256; i++)
            checksum += _measurer.CalculateMessageHeightEx(_text, _login, _login, _parameters);
        return checksum;
    }
}
