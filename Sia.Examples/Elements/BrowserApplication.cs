#if BROWSER
using Sia_Examples.Browser;
using Sia_Examples.Notebook;

namespace Sia_Examples;

public static class BrowserApplication
{
    public static async Task RunAsync()
    {
        const string NotebooksPath = "/notebooks";

        await OpfsMount.MountAsync(NotebooksPath);

        var mainThread = BrowserMainThread.Capture();
        var resources = new BrowserResourceLoader(mainThread);
        var frameworkAssemblies = new AssemblyLoader(mainThread, resources);
        var packages = new PackageReferenceLoader(resources);

        var storage = new FileSystemNotebookStorage(NotebooksPath);
        var library = new NotebookLibrary(storage);
        await library.RefreshAsync();

        await DomApplication.RunAsync(
            library,
            mainThread,
            new BrowserDomBackend(mainThread),
            frameworkAssemblies,
            packages,
            storage);
    }
}
#endif
