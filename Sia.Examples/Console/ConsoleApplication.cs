#if !BROWSER
using Sia_Examples.Notebook;

namespace Sia_Examples.Console;

public static class ConsoleApplication
{
    public static async Task RunAsync()
    {
        var uiThread = ConsoleThread.Shared;
        using var resources = new ConsoleResourceLoader();
        var frameworkAssemblies = new AssemblyLoader(uiThread);
        var packages = new PackageReferenceLoader(resources);

        var storage = new FileSystemNotebookStorage("./notebooks");
        System.Console.WriteLine($"Notebooks stored at: {storage.RootPath}");
        var library = new NotebookLibrary(storage);
        await library.RefreshAsync();

        await DomApplication.RunAsync(
            library,
            uiThread,
            new ConsoleDomBackend(new SystemConsoleTerminal()),
            frameworkAssemblies,
            packages,
            storage);
    }
}
#endif
