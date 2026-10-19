#if BROWSER
namespace Sia.Examples;

public static class ExampleViewer
{
    public static Task RunAsync()
        => BrowserApplication.RunAsync();
}
#endif
