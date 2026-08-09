#if !BROWSER
using Sia_Examples.Notebook;

namespace Sia_Examples.Console;

public static class ConsoleApplication
{
    public static async Task RunAsync(NotebookLibrary library)
    {
        ArgumentNullException.ThrowIfNull(library);
        var uiThread = ConsoleThread.Shared;
        using var resources = new ConsoleResourceLoader();
        var frameworkAssemblies = new AssemblyLoader(uiThread);
        var packages = new PackageReferenceLoader(resources);
        await DomApplication.RunAsync(
            library,
            uiThread,
            new ConsoleDomBackend(new SystemConsoleTerminal()),
            frameworkAssemblies,
            packages);
    }
}
#endif
