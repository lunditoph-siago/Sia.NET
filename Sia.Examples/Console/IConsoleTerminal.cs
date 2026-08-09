#if !BROWSER
namespace Sia_Examples.Console;

internal interface IConsoleTerminal : IDisposable
{
    public int Width { get; }

    public int Height { get; }

    public ValueTask<ConsoleKeyInfo> ReadKeyAsync(CancellationToken cancellationToken);

    public void Draw(IReadOnlyList<string> rows);
}
#endif
