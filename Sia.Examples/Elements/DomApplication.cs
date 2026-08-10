using Sia;
using Sia.Reactive;
using Sia_Examples.Dom;
using Sia_Examples.Notebook;

namespace Sia_Examples;

internal static class DomApplication
{
    public static async Task RunAsync(
        NotebookLibrary library,
        IUiThread uiThread,
        IDomBackend backend,
        AssemblyLoader frameworkAssemblies,
        PackageReferenceLoader packages,
        INotebookStorage storage)
    {
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(uiThread);
        ArgumentNullException.ThrowIfNull(backend);
        ArgumentNullException.ThrowIfNull(frameworkAssemblies);
        ArgumentNullException.ThrowIfNull(packages);
        ArgumentNullException.ThrowIfNull(storage);

        DomRuntime.Initialize(backend);
        try {
            using var world = new World();
            using var view = new BrowserApplicationView();
            var app = world.Mount(ExampleApp.Definition, new(library, view));
            world.FlushReactive();
            view.SetFrameworkAssemblyNames(frameworkAssemblies.KnownAssemblyNames);
            DomRuntime.Flush();

            NotebookWorkspace? workspace = null;
            try {
                while (true) {
                    var payload = await DomRuntime.WaitForEventAsync();
                    var (eventName, argument) = Split(payload);
                    switch (eventName) {
                        case "quit":
                            return;

                        case "select":
                            if (!int.TryParse(argument, out var index)
                                || index < 0
                                || index >= library.Notebooks.Count) {
                                break;
                            }
                            if (workspace is not null) {
                                await workspace.DisposeAsync();
                            }
                            var info = library.Notebooks[index];
                            var (document, version) = await library.LoadAsync(info);
                            var references = new MetadataReferenceProvider(
                                frameworkAssemblies,
                                packages);
                            workspace = new(
                                world,
                                uiThread,
                                document,
                                references,
                                info,
                                version,
                                storage,
                                library);
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

                        case "save-notebook" when workspace is not null:
                            workspace.StartSave();
                            break;

                        case "add-package" when workspace is not null:
                            await workspace.AddPackageAsync(argument);
                            break;

                        default:
                            workspace?.RouteEditorEvent(payload);
                            break;
                    }
                    DomRuntime.Flush();
                }
            }
            finally {
                if (workspace is not null) {
                    await workspace.DisposeAsync();
                }
                if (app.IsMounted) {
                    app.Unmount();
                }
            }
        }
        finally {
            DomRuntime.Dispose();
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
