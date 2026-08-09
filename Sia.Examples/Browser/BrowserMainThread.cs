#if BROWSER
namespace Sia_Examples.Browser;

public sealed class BrowserMainThread : IUiThread
{
    private readonly int _managedThreadId;

    private BrowserMainThread()
    {
        _managedThreadId = Environment.CurrentManagedThreadId;
    }

    public static BrowserMainThread Capture() => new();

    public void VerifyAccess()
    {
        if (Environment.CurrentManagedThreadId != _managedThreadId) {
            throw new InvalidOperationException(
                "Browser I/O and DOM access must remain on the browser main thread.");
        }
    }
}
#endif
