#if BROWSER
using Sia.Examples.Browser;
using Sia.Examples.Notebook;

namespace Sia.Examples;

public static class BrowserApplication
{
    public static async Task RunAsync()
    {
        var mainThread = BrowserMainThread.Capture();
        var resources = new BrowserResourceLoader(mainThread);
        var frameworkAssemblies = new AssemblyLoader(mainThread, resources);
        var packages = new PackageReferenceLoader(resources);

        var storage = new IndexedDbNotebookStorage(mainThread);
        var library = new NotebookLibrary(storage);
        await library.RefreshAsync();

        var workspaceStorage = new IndexedDbWorkspaceStorage(mainThread);
        using var navigation = new BrowserNavigation();
        if (ApplicationRoute.Resolve(navigation.InitialPath, library.Notebooks) is null) {
            BrowserNavigation.ShowNotFound();
            return;
        }

        await DomApplication.RunAsync(
            library,
            mainThread,
            new BrowserDomBackend(mainThread),
            frameworkAssemblies,
            packages,
            storage,
            workspaceStorage,
            navigation);
    }
}
#endif
