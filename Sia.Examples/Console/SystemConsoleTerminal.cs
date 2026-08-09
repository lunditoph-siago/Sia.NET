#if !BROWSER
using System.Text;

namespace Sia_Examples.Console;

internal sealed class SystemConsoleTerminal : IConsoleTerminal
{
    private readonly bool _previousTreatControlCAsInput;
    private readonly Encoding _previousOutputEncoding;
    private string[] _drawn = [];
    private bool _disposed;

    public SystemConsoleTerminal()
    {
        if (System.Console.IsInputRedirected || System.Console.IsOutputRedirected) {
            throw new InvalidOperationException(
                "The Console application requires an interactive terminal.");
        }

        _previousTreatControlCAsInput = System.Console.TreatControlCAsInput;
        _previousOutputEncoding = System.Console.OutputEncoding;
        System.Console.TreatControlCAsInput = true;
        System.Console.OutputEncoding = Encoding.UTF8;
        System.Console.Write("\e[?1049h\e[?25l\e[2J");
    }

    public int Width => Math.Max(System.Console.WindowWidth, 40);

    public int Height => Math.Max(System.Console.WindowHeight, 12);

    public async ValueTask<ConsoleKeyInfo> ReadKeyAsync(CancellationToken cancellationToken)
    {
        while (!System.Console.KeyAvailable) {
            await Task.Delay(25, cancellationToken);
        }
        return System.Console.ReadKey(intercept: true);
    }

    public void Draw(IReadOnlyList<string> rows)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_drawn.Length != rows.Count) {
            _drawn = new string[rows.Count];
            System.Console.Write("\e[2J");
        }

        var output = new StringBuilder();
        for (var row = 0; row < rows.Count; row++) {
            if (_drawn[row] == rows[row]) {
                continue;
            }
            _drawn[row] = rows[row];
            output.Append("\e[").Append(row + 1).Append(";1H").Append(rows[row]);
        }
        if (output.Length > 0) {
            System.Console.Write(output);
        }
    }

    public void Dispose()
    {
        if (_disposed) {
            return;
        }
        _disposed = true;
        System.Console.Write("\e[0m\e[?25h\e[?1049l");
        System.Console.TreatControlCAsInput = _previousTreatControlCAsInput;
        System.Console.OutputEncoding = _previousOutputEncoding;
    }
}
#endif
