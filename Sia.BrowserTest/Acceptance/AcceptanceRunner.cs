using System.Diagnostics;

namespace Sia_BrowserTest.Acceptance;

public sealed class AcceptanceRunner
{
    private readonly AcceptanceContext _context = new();
    private readonly Stopwatch _stopwatch = Stopwatch.StartNew();

    public async Task RunAsync(IAcceptanceStage stage)
    {
        Console.WriteLine();
        Console.WriteLine($"=== {stage.Name} ===");
        await stage.RunAsync(_context);
    }

    public int Report()
    {
        _stopwatch.Stop();
        Console.WriteLine();
        Console.WriteLine("============================================================");
        Console.WriteLine(
            $"{_context.Passed + _context.Failed} checks: "
            + $"{_context.Passed} passed, {_context.Failed} failed, "
            + $"{_stopwatch.Elapsed.TotalSeconds:F2}s");
        Console.WriteLine("============================================================");
        return _context.Failed == 0 ? 0 : 1;
    }
}
