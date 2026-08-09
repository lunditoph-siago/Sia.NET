#if !BROWSER
using Sia_Examples.Console.Layout;

namespace Sia_BrowserTest.Acceptance;

public sealed class ConsoleLayoutAcceptance : IAcceptanceStage
{
    public string Name => "7. Console layout engine";

    public async Task RunAsync(AcceptanceContext context)
    {
        await context.CaseAsync(
            "fixed lengths split exactly",
            TestFixedLengthsAsync);
        await context.CaseAsync(
            "fill consumes remaining space",
            TestFillConsumesRemainderAsync);
        await context.CaseAsync(
            "multiple fills distribute by weight",
            TestWeightedFillAsync);
        await context.CaseAsync(
            "percentage resolves against the total",
            TestPercentageAsync);
        await context.CaseAsync(
            "an over-sized length clamps instead of overflowing",
            TestOversizedLengthClampsAsync);
        await context.CaseAsync(
            "vertical direction splits height instead of width",
            TestVerticalDirectionAsync);
        await context.CaseAsync(
            "a zero-size area never throws and splits to zero",
            TestZeroAreaAsync);
        await context.CaseAsync(
            "resolved sizes always sum to the original total",
            TestSizesSumToTotalAsync);
    }

    private static Task TestFixedLengthsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var area = new Rect(0, 0, 10, 1);
        var parts = LayoutEngine.Split(area, Direction.Horizontal, Constraint.Length(3), Constraint.Length(7));

        AcceptanceAssert.Equal(new Rect(0, 0, 3, 1), parts[0]);
        AcceptanceAssert.Equal(new Rect(3, 0, 7, 1), parts[1]);
        return Task.CompletedTask;
    }

    private static Task TestFillConsumesRemainderAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var area = new Rect(0, 0, 10, 1);
        var parts = LayoutEngine.Split(area, Direction.Horizontal, Constraint.Length(3), Constraint.Fill());

        AcceptanceAssert.Equal(new Rect(0, 0, 3, 1), parts[0]);
        AcceptanceAssert.Equal(new Rect(3, 0, 7, 1), parts[1]);
        return Task.CompletedTask;
    }

    private static Task TestWeightedFillAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var area = new Rect(0, 0, 9, 1);
        var parts = LayoutEngine.Split(area, Direction.Horizontal, Constraint.Fill(1), Constraint.Fill(2));

        AcceptanceAssert.Equal(3, parts[0].Width);
        AcceptanceAssert.Equal(6, parts[1].Width);
        return Task.CompletedTask;
    }

    private static Task TestPercentageAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var area = new Rect(0, 0, 100, 1);
        var parts = LayoutEngine.Split(area, Direction.Horizontal, Constraint.Percentage(30), Constraint.Fill());

        AcceptanceAssert.Equal(30, parts[0].Width);
        AcceptanceAssert.Equal(70, parts[1].Width);
        return Task.CompletedTask;
    }

    private static Task TestOversizedLengthClampsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var area = new Rect(0, 0, 5, 1);
        var parts = LayoutEngine.Split(area, Direction.Horizontal, Constraint.Length(10));

        AcceptanceAssert.Equal(5, parts[0].Width);
        return Task.CompletedTask;
    }

    private static Task TestVerticalDirectionAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var area = new Rect(0, 0, 20, 10);
        var parts = LayoutEngine.Split(area, Direction.Vertical, Constraint.Length(2), Constraint.Fill());

        AcceptanceAssert.Equal(new Rect(0, 0, 20, 2), parts[0]);
        AcceptanceAssert.Equal(new Rect(0, 2, 20, 8), parts[1]);
        return Task.CompletedTask;
    }

    private static Task TestZeroAreaAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var area = new Rect(0, 0, 0, 0);
        var parts = LayoutEngine.Split(area, Direction.Horizontal, Constraint.Length(5), Constraint.Fill());

        AcceptanceAssert.Equal(0, parts[0].Width);
        AcceptanceAssert.Equal(0, parts[1].Width);
        return Task.CompletedTask;
    }

    private static Task TestSizesSumToTotalAsync(CancellationToken cancellationToken)
    {
        Constraint[][] cases = [
            [Constraint.Length(1), Constraint.Fill(), Constraint.Length(1)],
            [Constraint.Fill(1), Constraint.Fill(2), Constraint.Fill(3)],
            [Constraint.Percentage(50), Constraint.Percentage(50)],
            [Constraint.Length(100), Constraint.Fill()],
        ];
        foreach (var totalWidth in new[] { 0, 1, 2, 3, 7, 13, 80 }) {
            foreach (var constraints in cases) {
                cancellationToken.ThrowIfCancellationRequested();
                var area = new Rect(0, 0, totalWidth, 1);
                var parts = LayoutEngine.Split(area, Direction.Horizontal, constraints);
                var sum = parts.Sum(static part => part.Width);
                AcceptanceAssert.Equal(
                    totalWidth,
                    sum,
                    $"width {totalWidth} with {constraints.Length} constraints summed to {sum}");
            }
        }
        return Task.CompletedTask;
    }
}
#endif
