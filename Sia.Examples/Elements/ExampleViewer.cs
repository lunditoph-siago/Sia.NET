using Sia_Examples.Notebook;

namespace Sia_Examples;

public static class ExampleViewer
{
    public static Task RunAsync()
        => BrowserApplication.RunAsync(new NotebookLibrary());
}
