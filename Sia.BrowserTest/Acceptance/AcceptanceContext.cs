using System.Diagnostics;

namespace Sia_BrowserTest.Acceptance;

public sealed class AcceptanceContext
{
    public int Passed { get; private set; }

    public int Failed { get; private set; }

    public async Task CaseAsync(
        string name,
        Func<CancellationToken, Task> test,
        TimeSpan? timeout = null)
    {
        Console.WriteLine($"  {name,-58} RUN");
        var stopwatch = Stopwatch.StartNew();
        using var cancellation = new CancellationTokenSource(
            timeout ?? TimeSpan.FromSeconds(30));
        try {
            await test(cancellation.Token);
            Passed++;
            Console.WriteLine($"  {name,-58} PASS  {stopwatch.Elapsed.TotalSeconds,6:F2}s");
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) {
            Failed++;
            Console.WriteLine($"  {name,-58} TIME  {stopwatch.Elapsed.TotalSeconds,6:F2}s");
        }
        catch (Exception error) {
            Failed++;
            Console.WriteLine($"  {name,-58} FAIL  {stopwatch.Elapsed.TotalSeconds,6:F2}s");
            Console.Error.WriteLine($"    {error.GetType().Name}: {error.Message}");
        }
    }
}
