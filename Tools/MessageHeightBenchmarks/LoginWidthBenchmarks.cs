using System;
using System.IO;
using BenchmarkDotNet.Attributes;
using MessageHeightTwitch;
using MessageHeightTwitch.Measurement.Shared.Shaping;

namespace MessageHeightBenchmarks;

[MemoryDiagnoser]
[MaxRelativeError(0.01)]
public class LoginWidthBenchmarks
{
    private AsciiFastPath _table = null!;

    [Params("tetyys", "office_affinity_ffi", "1x2")]
    public string Login { get; set; } = null!;

    [GlobalSetup]
    public void Setup()
    {
        Directory.SetCurrentDirectory(AppContext.BaseDirectory);
        _table = FontFallbackList.Shared.AsciiBold;
        var accepted = _table.TryMeasurePrefixFixed(Login, Login.Length, out var expected);
        if (_table.TryMeasureLogin(Login, out var actual) != accepted || accepted && actual != expected)
            throw new InvalidDataException($"Login width mismatch: {Login}.");
    }

    [Benchmark(Baseline = true, OperationsPerInvoke = 256)]
    public long GeneralAsciiWidth()
    {
        long total = 0;
        for (var i = 0; i < 256; i++)
            if (_table.TryMeasurePrefixFixed(Login, Login.Length, out var width))
                total += width;
        return total;
    }

    [Benchmark(OperationsPerInvoke = 256)]
    public long LoginWidth()
    {
        long total = 0;
        for (var i = 0; i < 256; i++)
            if (_table.TryMeasureLogin(Login, out var width))
                total += width;
        return total;
    }
}
