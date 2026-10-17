using System;
using System.Collections.Generic;
using BenchmarkDotNet.Attributes;
using MessageHeightTwitch;
using MessageHeightTwitch.Measurement.Shared.Shaping;

namespace MessageHeightBenchmarks;

[MemoryDiagnoser]
public class ScriptRunsBenchmarks
{
    [Params("Latin", "Cyrillic", "Japanese", "Mixed")]
    public string Input { get; set; } = "Latin";

    private string _text = null!;
    private readonly UScript.ScriptContext _context = new();
    private readonly UScript.ScriptScratch _scratch = new();
    private readonly List<(Range Range, UScriptCode Script)> _runs = [];

    [GlobalSetup]
    public void Setup()
    {
        _text = Input switch {
            "Latin" => "Café déjà vu, voilà une très belle journée !",
            "Cyrillic" => "Привет всем, сегодня прекрасный день!",
            "Japanese" => "こんにちは、今日はとてもいい天気ですね。",
            _ => "hello (Привет) こんにちは A6\u0342 😀"
        };
        Segment();
    }

    [Benchmark]
    public int Segment()
    {
        _context.Reset();
        _context.Segment(_text, _runs, _scratch);
        return _runs.Count;
    }

    [GlobalCleanup]
    public void Cleanup() => _scratch.Dispose();
}
