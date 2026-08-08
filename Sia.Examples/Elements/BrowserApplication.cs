using Sia;
using Sia.Reactive;
using Sia_Examples.Browser;
using Sia_Examples.Notebook;

namespace Sia_Examples;

public static class BrowserApplication
{
    public static async Task RunAsync(NotebookLibrary library)
    {
        ArgumentNullException.ThrowIfNull(library);
        var mainThread = BrowserMainThread.Capture();
        BrowserDom.Initialize(mainThread);

        using var world = new World();
        using var view = new BrowserApplicationView();
        var app = world.Mount(ExampleApp.Definition, new(library, view));
        world.FlushReactive();
        var resources = new BrowserResourceLoader(mainThread);
        var frameworkAssemblies = new AssemblyLoader(mainThread, resources);
        var packages = new PackageReferenceLoader(resources);
        view.SetFrameworkAssemblyNames(frameworkAssemblies.KnownAssemblyNames);

        NotebookWorkspace? workspace = null;
        try {
            while (true) {
                var payload = await BrowserDom.WaitForEventAsync();
                var (eventName, argument) = Split(payload);
                switch (eventName) {
                    case "select":
                        if (!int.TryParse(argument, out var index)
                            || index < 0
                            || index >= library.Notebooks.Count) {
                            break;
                        }
                        if (workspace is not null) {
                            await workspace.DisposeAsync();
                        }
                        var document = library.Load(library.Notebooks[index]);
                        var references = new MetadataReferenceProvider(
                            frameworkAssemblies,
                            packages);
                        workspace = new(
                            world,
                            mainThread,
                            document,
                            references);
                        app.GetState<ExampleAppState>().Set(new(index));
                        world.FlushReactive();
                        await workspace.InitializeAsync();
                        break;

                    case "compile" when workspace is not null:
                        workspace.StartCompile(argument);
                        break;

                    case "run" when workspace is not null:
                        workspace.StartRun(argument);
                        break;

                    case "stop" when workspace is not null:
                        workspace.Stop();
                        break;

                    case "save" when workspace is not null:
                        workspace.Save(argument);
                        break;

                    case "add-package" when workspace is not null:
                        await workspace.AddPackageAsync(argument);
                        break;

                    default:
                        workspace?.RouteEditorEvent(payload);
                        break;
                }
            }
        } finally {
            if (workspace is not null) {
                await workspace.DisposeAsync();
            }
            if (app.IsMounted) {
                app.Unmount();
            }
        }
    }

    private static (string EventName, string Argument) Split(string payload)
    {
        var separator = payload.IndexOf(':');
        return separator < 0
            ? (payload, string.Empty)
            : (payload[..separator], payload[(separator + 1)..]);
    }
}
