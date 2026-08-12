#if BROWSER
namespace Sia_Examples;

public static class ExampleViewer
{
    public static Task RunAsync()
        => BrowserApplication.RunAsync();
}
#endif
