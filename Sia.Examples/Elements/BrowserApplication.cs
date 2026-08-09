#if BROWSER
using Sia_Examples.Browser;
using Sia_Examples.Notebook;

namespace Sia_Examples;

public static class BrowserApplication
{
    public static Task RunAsync(NotebookLibrary library)
    {
        ArgumentNullException.ThrowIfNull(library);
        var mainThread = BrowserMainThread.Capture();
        var resources = new BrowserResourceLoader(mainThread);
        var frameworkAssemblies = new AssemblyLoader(mainThread, resources);
        var packages = new PackageReferenceLoader(resources);
        return DomApplication.RunAsync(
            library,
            mainThread,
            new BrowserDomBackend(mainThread),
            frameworkAssemblies,
            packages);
    }
}
#endif
