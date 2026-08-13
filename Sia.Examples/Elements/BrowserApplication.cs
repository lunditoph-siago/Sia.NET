#if BROWSER
using Sia_Examples.Browser;
using Sia_Examples.Notebook;

namespace Sia_Examples;

public static class BrowserApplication
{
    public static async Task RunAsync()
    {
        // Notebooks live on WASMFS's default in-memory backend, not OPFS:
        // wasmfs_create_opfs_backend() can deadlock waiting for its proxy
        // worker to spawn (Emscripten's own docs warn about this), and it
        // did hang the whole app boot in production. No mount call means no
        // deadlock risk; the tradeoff is notebooks don't survive a reload.
        const string NotebooksPath = "/notebooks";

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
