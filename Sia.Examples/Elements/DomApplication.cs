using Sia;
using Sia.Reactive;
using Sia_Examples.Dom;
using Sia_Examples.Editor;
using Sia_Examples.Notebook;
using Destination = Sia_Examples.ApplicationDestination;

namespace Sia_Examples;

internal static class DomApplication
{
    public static async Task RunAsync(
        NotebookLibrary library,
        IUiThread uiThread,
        IDomBackend backend,
        AssemblyLoader frameworkAssemblies,
        PackageReferenceLoader packages,
        IWorkspaceStorage storage,
        IWorkspaceStorage workspaceStorage,
        IApplicationNavigation? navigation = null)
    {
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(uiThread);
        ArgumentNullException.ThrowIfNull(backend);
        ArgumentNullException.ThrowIfNull(frameworkAssemblies);
        ArgumentNullException.ThrowIfNull(packages);
        ArgumentNullException.ThrowIfNull(storage);
        ArgumentNullException.ThrowIfNull(workspaceStorage);

        DomRuntime.Initialize(backend);
        try {
            using var world = new World();
            using var view = new BrowserApplicationView();
            var app = world.Mount(ExampleApp.Definition, new(library, view));
            world.FlushReactive();
            DomRuntime.Flush();

            NotebookWorkspace? workspace = null;
            BrowserEditorPage? editorPage = null;
            NavigationRequest? pendingNavigation = null;
            object? navigationOwner = null;
            long navigationGeneration = 0;
            var quitting = false;

            object? CurrentOwner() => (object?)workspace ?? editorPage;

            (string Content, bool Dirty) CaptureNavigationState()
                => workspace?.CaptureNavigationState()
                    ?? editorPage?.CaptureNavigationState()
                    ?? (string.Empty, false);

            void ClearNavigation()
            {
                pendingNavigation = null;
                navigationOwner = null;
                view.HideConflict();
            }

            async Task PromptNavigationAsync(NavigationRequest request, object? owner, string? error = null)
            {
                if (navigation is not null) await navigation.HoldAsync(request.HistoryTicket);
                if (navigation?.IsCurrent(request.HistoryTicket) == false) return;
                pendingNavigation = request;
                navigationOwner = owner;
                var generation = ++navigationGeneration;
                view.ShowNotice(
                    error ?? "There are unsaved changes. Save before leaving?",
                    ("Save", $"navigation-save:{generation}"),
                    ("Don't Save", $"navigation-discard:{generation}"),
                    ("Cancel", $"navigation-cancel:{generation}"));
            }

            async Task CloseWorkspaceAsync()
            {
                if (workspace is null) {
                    return;
                }
                await workspace.DisposeAsync();
                workspace = null;
            }

            async ValueTask CloseEditorPageAsync()
            {
                if (editorPage is null) {
                    return;
                }
                var closing = editorPage;
                editorPage = null;
                await closing.DisposeAsync();
            }

            void RefreshSidebarSelection()
            {
                var selected = workspace is null
                    ? -1
                    : IndexOf(library.Notebooks, workspace.Info);
                app.GetState<ExampleAppState>().Set(new(selected));
                world.FlushReactive();
            }

            Task CommitCurrentRouteAsync(bool replace = false, int ticket = 0)
            {
                var route = editorPage is not null ? new ApplicationRoute(Destination.Editor)
                    : workspace is not null ? workspace.Info.Key.Length == 0
                        ? new ApplicationRoute(Destination.NewNotebook)
                        : new ApplicationRoute(Destination.Notebook, workspace.Info)
                    : new ApplicationRoute(Destination.Home);
                return navigation?.CommitAsync(route.Path, ticket, replace) ?? Task.CompletedTask;
            }

            async Task NavigatePathAsync(string path, int ticket = 0, bool replace = false)
            {
                if (navigation?.IsCurrent(ticket) == false) return;
                if (ApplicationRoute.Resolve(path, library.Notebooks) is { } route) {
                    await NavigateAsync(new(route.Target, route.Info, HistoryTicket: ticket, Replace: replace));
                } else {
                    if (navigation is not null) await navigation.HoldAsync(ticket);
                    if (navigation?.IsCurrent(ticket) == false) return;
                    ClearNavigation();
                    view.ShowNotice("This page or notebook could not be found. User notebooks are stored in this browser.",
                        ("Home", "application-home"));
                }
            }

            async Task NavigateAsync(NavigationRequest request, bool discard = false)
            {
                if (navigation?.IsCurrent(request.HistoryTicket) == false) return;
                if (!request.Reload && (request.Target == Destination.Notebook
                    && workspace is not null && request.Info is { } current
                    && workspace.Info.Key == current.Key && workspace.Info.Origin == current.Origin
                    || request.Target == Destination.Editor && editorPage is not null)) {
                    ClearNavigation();
                    await CommitCurrentRouteAsync(request.Replace, request.HistoryTicket);
                    return;
                }

                var owner = CurrentOwner();
                var before = CaptureNavigationState();
                if (before.Dirty && !discard) {
                    await PromptNavigationAsync(request, owner);
                    return;
                }

                pendingNavigation = request;
                navigationOwner = owner;
                var generation = ++navigationGeneration;
                NotebookDocument? document = null;
                string? version = null;
                try {
                    if (request.Target == Destination.Notebook) {
                        (document, version) = await library.LoadAsync(request.Info!);
                    }
                }
                catch (Exception error) {
                    if (navigation is not null) await navigation.HoldAsync(request.HistoryTicket);
                    if (navigation?.IsCurrent(request.HistoryTicket) == false) return;
                    view.ShowNotice($"Could not open notebook: {error.Message}",
                        ("Retry", $"navigation-retry:{generation}"),
                        ("Cancel", $"navigation-cancel:{generation}"));
                    return;
                }

                if (!ReferenceEquals(owner, CurrentOwner())
                    || navigation?.IsCurrent(request.HistoryTicket) == false) {
                    return;
                }
                var after = CaptureNavigationState();
                if (after.Dirty && (!discard || after.Content != before.Content)) {
                    await PromptNavigationAsync(request, owner);
                    return;
                }

                ClearNavigation();
                view.HideNotebookBar();
                await CloseEditorPageAsync();
                await CloseWorkspaceAsync();
                if (request.Target == Destination.Quit) {
                    quitting = true;
                    return;
                }
                if (request.Target == Destination.Home) {
                    RefreshSidebarSelection();
                    await CommitCurrentRouteAsync(request.Replace, request.HistoryTicket);
                    return;
                }
                var references = new MetadataReferenceProvider(
                    frameworkAssemblies,
                    packages);
                if (request.Target == Destination.Editor) {
                    editorPage = new BrowserEditorPage(world, references, workspaceStorage);
                    RefreshSidebarSelection();
                    await editorPage.InitializeAsync();
                    await CommitCurrentRouteAsync(request.Replace, request.HistoryTicket);
                    return;
                }
                var info = request.Info ?? new NotebookInfo("Untitled", "", "", NotebookOrigin.User);
                workspace = new(
                    world,
                    uiThread,
                    document ?? new NotebookDocument("Untitled", [], []),
                    references,
                    info,
                    version,
                    storage,
                    library);
                if (request.Target == Destination.NewNotebook) {
                    workspace.InsertSection(null, "Notes");
                }
                RefreshSidebarSelection();
                view.ShowNotebookBar(info, version);
                await workspace.InitializeAsync();
                await CommitCurrentRouteAsync(request.Replace, request.HistoryTicket);
            }

            try {
                if (navigation is not null) await NavigatePathAsync(navigation.InitialPath, replace: true);
                DomRuntime.Flush();
                DomRuntime.NotifyAppReady();
                while (!quitting) {
                    var payload = await DomRuntime.WaitForEventAsync();
                    var (eventName, argument) = Split(payload);
                    try {
                        switch (eventName) {
                            case "navigate": {
                                    var (ticketText, path) = Split(argument);
                                    if (int.TryParse(ticketText, out var ticket)) await NavigatePathAsync(path, ticket);
                                    break;
                                }

                            case "application-home":
                                await NavigateAsync(new(Destination.Home));
                                break;

                            case "quit":
                                await NavigateAsync(new(Destination.Quit));
                                break;

                            case "select":
                                if (int.TryParse(argument, out var index)
                                    && index >= 0
                                    && index < library.Notebooks.Count) {
                                    await NavigateAsync(new(Destination.Notebook, library.Notebooks[index]));
                                }
                                break;

                            case "new-notebook":
                                await NavigateAsync(new(Destination.NewNotebook));
                                break;

                            case "open-editor":
                                await NavigateAsync(new(Destination.Editor));
                                break;

                            case "editor-page-home" when editorPage is not null:
                                await NavigateAsync(new(Destination.Home));
                                break;

                            case "navigation-save":
                            case "navigation-discard":
                            case "navigation-cancel":
                            case "navigation-retry": {
                                    if (pendingNavigation is not { } request
                                        || !long.TryParse(argument, out var generation)
                                        || generation != navigationGeneration
                                        || !ReferenceEquals(navigationOwner, CurrentOwner())
                                        || navigation?.IsCurrent(request.HistoryTicket) == false) {
                                        break;
                                    }
                                    if (eventName == "navigation-cancel") {
                                        ClearNavigation();
                                        break;
                                    }
                                    if (eventName == "navigation-save") {
                                        try {
                                            if (workspace is not null) {
                                                var (version, conflict) = await workspace.SaveToStorageAsync();
                                                if (conflict is not null) {
                                                    ClearNavigation();
                                                    view.ShowConflict(conflict);
                                                    break;
                                                }
                                                view.ShowNotebookBar(workspace.Info, version);
                                                RefreshSidebarSelection();
                                                await CommitCurrentRouteAsync(replace: true);
                                            } else if (editorPage is not null
                                                && !await editorPage.SaveForNavigationAsync()) {
                                                ClearNavigation();
                                                break;
                                            }
                                        }
                                        catch (Exception error) {
                                            await PromptNavigationAsync(request, CurrentOwner(),
                                                $"Could not save changes: {error.Message}");
                                            break;
                                        }
                                    }
                                    await NavigateAsync(request, discard: eventName == "navigation-discard");
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
                                        await CommitCurrentRouteAsync(replace: true);
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
                                        await CommitCurrentRouteAsync(replace: true);
                                    }
                                }
                                catch (WorkspaceConflictException error) {
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
                                        await CommitCurrentRouteAsync(replace: true);
                                    }
                                    break;
                                }

                            case "resolve-conflict-reload" when workspace is not null:
                                await NavigateAsync(new(Destination.Notebook, workspace.Info, Reload: true), discard: true);
                                break;

                            case "insert-cell" when workspace is not null:
                                workspace.InsertCell(argument);
                                break;

                            case "add-script" when workspace is not null:
                                workspace.AddScript(argument);
                                break;

                            case "remove-script" when workspace is not null:
                                workspace.RemoveScript(argument);
                                break;

                            case "rename-script" when workspace is not null:
                                workspace.RenameScript(argument);
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

                            case "cell-normalize" when workspace is not null:
                                workspace.NormalizeCells(argument);
                                break;

                            case "cell-resize" when workspace is not null:
                                workspace.ResizeCellSplit(argument);
                                break;

                            default:
                                if (editorPage?.Route(payload) != true) {
                                    workspace?.RouteEditorEvent(payload);
                                }
                                break;
                        }
                    }
                    catch (Exception error) {
                        DomRuntime.ReportError(error.ToString());
                    }
                    DomRuntime.Flush();
                }
            } finally {
                await CloseEditorPageAsync();
                await CloseWorkspaceAsync();
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

    private sealed record NavigationRequest(
        Destination Target, NotebookInfo? Info = null, bool Reload = false,
        int HistoryTicket = 0, bool Replace = false);

    private static (string EventName, string Argument) Split(string payload)
    {
        var separator = payload.IndexOf(':');
        return separator < 0
            ? (payload, string.Empty)
            : (payload[..separator], payload[(separator + 1)..]);
    }
}
