using System.Linq;
using BenchmarkDotNet.Running;

namespace MessageHeightBenchmarks;

internal static class Program
{
    // BenchmarkDotNet's generated executables also load this assembly.
    [System.Runtime.CompilerServices.ModuleInitializer]
    internal static void Initialize() => MessageHeightTwitch.MessageHeightTwitch.InitFonts();

    private static int Main(string[] Args)
    {
        var summaries = BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(Args);
        return summaries.Any(summary => summary.HasCriticalValidationErrors ||
            summary.Reports.Any(report => !report.Success)) ? 1 : 0;
    }
}
