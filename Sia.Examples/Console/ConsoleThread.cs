#if !BROWSER
namespace Sia_Examples.Console;

internal sealed class ConsoleThread : IUiThread
{
    public static ConsoleThread Shared { get; } = new();

    public void VerifyAccess()
    {
    }
}
#endif
