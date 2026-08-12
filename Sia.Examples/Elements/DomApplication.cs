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
            DomRuntime.Flush();

            NotebookWorkspace? workspace = null;

            void RefreshSidebarSelection()
            {
                var selected = workspace is null
                    ? -1
                    : IndexOf(library.Notebooks, workspace.Info);
                app.GetState<ExampleAppState>().Set(new(selected));
                world.FlushReactive();
            }

            async Task OpenAsync(NotebookInfo info)
            {
                if (workspace is not null) {
                    await workspace.DisposeAsync();
                    workspace = null;
                }
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
                RefreshSidebarSelection();
                view.ShowNotebookBar(info, version);
                await workspace.InitializeAsync();
            }

            try {
                while (true) {
                    var payload = await DomRuntime.WaitForEventAsync();
                    var (eventName, argument) = Split(payload);
                    switch (eventName) {
                        case "quit":
                            return;

                        case "select":
                            if (int.TryParse(argument, out var index)
                                && index >= 0
                                && index < library.Notebooks.Count) {
                                await OpenAsync(library.Notebooks[index]);
                            }
                            break;

                        case "new-notebook": {
                                if (workspace is not null) {
                                    await workspace.DisposeAsync();
                                    workspace = null;
                                }
                                var info = new NotebookInfo(
                                    "Untitled",
                                    "",
                                    "",
                                    NotebookOrigin.User);
                                var references = new MetadataReferenceProvider(
                                    frameworkAssemblies,
                                    packages);
                                workspace = new(
                                    world,
                                    uiThread,
                                    new NotebookDocument("Untitled", [], []),
                                    references,
                                    info,
                                    null,
                                    storage,
                                    library);
                                workspace.InsertSection(null, "Notes");
                                RefreshSidebarSelection();
                                view.ShowNotebookBar(info, null);
                                await workspace.InitializeAsync();
                                break;
                            }

                        case "compile" when workspace is not null:
                            workspace.StartCompile(argument);
                            break;

                        case "run" when workspace is not null:
                            workspace.StartRun(argument);
                            break;

                        case "toggle-run" when workspace is not null:
                            workspace.ToggleRun(argument);
                            break;

                        case "stop" when workspace is not null:
                            workspace.Stop();
                            break;

                        case "save" when workspace is not null:
                            workspace.Save(argument);
                            break;

                        case "discard" when workspace is not null:
                            workspace.Discard(argument);
                            break;

                        case "set-scope" when workspace is not null:
                            workspace.SetCellScope(argument);
                            break;

                        case "editor-focus" when workspace is not null:
                            workspace.BeginEditorEdit(argument);
                            break;

                        case "save-notebook" when workspace is not null: {
                                var (version, conflict) = await workspace.SaveToStorageAsync();
                                if (conflict is not null) {
                                    view.ShowConflict(conflict);
                                } else {
                                    view.ShowNotebookBar(workspace.Info, version);
                                    RefreshSidebarSelection();
                                }
                                break;
                            }

                        case "delete-notebook" when workspace is not null:
                            try {
                                if (await workspace.DeleteFromStorageAsync()) {
                                    await workspace.DisposeAsync();
                                    workspace = null;
                                    RefreshSidebarSelection();
                                    view.HideNotebookBar();
                                }
                            }
                            catch (NotebookConflictException error) {
                                view.ShowConflict(error);
                            }
                            break;

                        case "resolve-conflict-overwrite" when workspace is not null: {
                                var (version, conflict) = await workspace.SaveToStorageAsync(force: true);
                                if (conflict is not null) {
                                    view.ShowConflict(conflict);
                                } else {
                                    view.ShowNotebookBar(workspace.Info, version);
                                    RefreshSidebarSelection();
                                }
                                break;
                            }

                        case "resolve-conflict-reload" when workspace is not null:
                            await OpenAsync(workspace.Info);
                            break;

                        case "insert-cell" when workspace is not null:
                            workspace.InsertCell(argument);
                            break;

                        case "insert-paragraph" when workspace is not null:
                            workspace.InsertParagraph(argument);
                            break;

                        case "save-paragraph" when workspace is not null:
                            workspace.SaveParagraph(argument);
                            break;

                        case "remove-cell" when workspace is not null:
                            workspace.RemoveCell(argument);
                            break;

                        case "move-cell-up" when workspace is not null:
                            workspace.MoveCell(argument, -1);
                            break;

                        case "move-cell-down" when workspace is not null:
                            workspace.MoveCell(argument, 1);
                            break;

                        case "insert-section" when workspace is not null:
                            workspace.InsertSection(
                                null,
                                "New Section",
                                argument == "text" ? NotebookBlockKind.Text : NotebookBlockKind.Code);
                            break;

                        case "remove-section" when workspace is not null:
                            workspace.RemoveSection(argument);
                            break;

                        case "rename-section" when workspace is not null:
                            workspace.RenameSection(argument);
                            break;

                        case "rename-title" when workspace is not null:
                            workspace.RenameTitle();
                            break;

                        case "add-package" when workspace is not null:
                            await workspace.AddPackageAsync();
                            break;

                        case "activate-tab" when workspace is not null:
                            workspace.ActivateTab(argument);
                            break;

                        case "open-window" when workspace is not null:
                            workspace.OpenWindow(argument);
                            break;

                        case "close-window" when workspace is not null:
                            workspace.CloseWindow(argument);
                            break;

                        case "cell" when workspace is not null:
                            workspace.Cell(argument);
                            break;

                        case "cell-detach" when workspace is not null:
                            workspace.Detach(argument);
                            break;

                        default:
                            workspace?.RouteEditorEvent(payload);
                            break;
                    }
                    DomRuntime.Flush();
                }
            } finally {
                if (workspace is not null) {
                    await workspace.DisposeAsync();
                }
                if (app.IsMounted) {
                    app.Unmount();
                }
            }
        } finally {
            DomRuntime.Dispose();
        }
    }

    private static int IndexOf(
        IReadOnlyList<NotebookInfo> notebooks,
        NotebookInfo info)
    {
        for (var index = 0; index < notebooks.Count; index++) {
            if (notebooks[index].Key == info.Key
                && notebooks[index].Origin == info.Origin) {
                return index;
            }
        }
        return -1;
    }

    private static (string EventName, string Argument) Split(string payload)
    {
        var separator = payload.IndexOf(':');
        return separator < 0
            ? (payload, string.Empty)
            : (payload[..separator], payload[(separator + 1)..]);
    }
}
