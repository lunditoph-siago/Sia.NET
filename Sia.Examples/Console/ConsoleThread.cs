#if !BROWSER
namespace Sia.Examples.Console;

internal sealed class ConsoleThread : IUiThread
{
    public static ConsoleThread Shared { get; } = new();

    public void VerifyAccess() {}
}
#endif
