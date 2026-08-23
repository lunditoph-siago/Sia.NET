#if !BROWSER
using Sia_Examples.Editor;
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

        var storage = new FileSystemWorkspaceStorage("./notebooks");
        System.Console.WriteLine($"Notebooks stored at: {storage.RootPath}");
        var library = new NotebookLibrary(storage);
        await library.RefreshAsync();

        var workspaceStorage = new FileSystemWorkspaceStorage("./editor-workspace");
        System.Console.WriteLine($"Editor workspace stored at: {workspaceStorage.RootPath}");

        await DomApplication.RunAsync(
            library,
            uiThread,
            new ConsoleDomBackend(new SystemConsoleTerminal()),
            frameworkAssemblies,
            packages,
            storage,
            workspaceStorage);
    }
}
#endif
