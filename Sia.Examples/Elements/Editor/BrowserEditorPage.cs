using System.Globalization;
using Sia;
using Sia_Examples.Dom;
using Sia_Examples.Notebook;

namespace Sia_Examples.Editor;

internal sealed partial class BrowserEditorPage : IAsyncDisposable
{
    private const string RegionId = "region-editor-page";
    private const string ProjectId = "editor-page";
    private const string ConsoleTabId = "tab-console-output";
    private const string RenderTabId = "tab-render-render";
    private const string ScriptSuffix = "script";
    private const double DefaultEditorShare = 0.72;
    private const int HighlightLimit = 200_000;

    private readonly EditorWorkspace _workspace;
    private readonly BrowserEditorRegistry _editors;
    private readonly EditorProjectCompiler _compiler;
    private readonly DomElement _container;
    private readonly DomElement _root;
    private readonly DomElement _workbench;
    private readonly DomElement _activeFileName;
    private readonly DomElement _workspaceStatus;
    private readonly DomElement _buildStatus;
    private readonly DomElement _diagnostics;
    private readonly DomElement _consoleOutput;
    private readonly DomElement _renderOutput;
    private readonly DomElement _consolePanel;
    private readonly DomElement _renderPanel;
    private readonly DomElement _layoutRevision;
    private readonly List<DomElement> _ownedElements = [];
    private readonly CancellationTokenSource _lifetime = new();
    private readonly HashSet<Task> _operations = [];

    private readonly Dictionary<Entity, string> _entryKeys = [];
    private readonly Dictionary<string, Entity> _entriesByKey = [];
    private int _entryKeySeed;

    private NotebookCellState _state;
    private Entity? _activeEntity;
    private bool _busy;
    private bool _disposed;
    private bool _initialized;

    public BrowserEditorPage(World world, ICompilationReferenceResolver references, IWorkspaceStorage storage)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(references);
        ArgumentNullException.ThrowIfNull(storage);

        _workspace = new(world, storage);
        _compiler = new(references);
        _container = DomElement.Find("notebook");
        _root = Own(DomElement.Create("section")
            .Class("editor-page")
            .Attr("aria-label", "Standalone Sia editor"));

        var titlebar = Own(DomElement.Create("header").Class("editor-page-titlebar"));
        var title = Own(DomElement.Create("div").Class("editor-page-title"));
        var brand = Own(DomElement.Create("span")
            .Class("editor-page-brand")
            .Text("Sia.NET · Editor Lab"));
        _buildStatus = Own(DomElement.Create("span")
            .Class("editor-page-build-status")
            .Attr("role", "status")
            .Text("Loading workspace…"));
        title.Append(brand).Append(_buildStatus);
        var actions = Own(DomElement.Create("div").Class("editor-page-actions"));
        actions
            .Append(CreateAction("Build", "editor-page-build", "Compile the workspace"))
            .Append(CreateAction("Run ▶", "editor-page-run", "Compile and run the workspace"))
            .Append(CreateAction("Save", "editor-page-save", "Save the active file (Ctrl+S)", secondary: true))
            .Append(CreateAction(
                "Save All", "editor-page-save-all", "Save every open file (Ctrl+Shift+S)", secondary: true))
            .Append(CreateAction("Save As…", "editor-page-save-as-begin", "Save the active file under a new path", secondary: true))
            .Append(CreateAction("Revert", "editor-page-revert", "Discard unsaved edits in the active file", secondary: true))
            .Append(CreateAction("New File", "editor-page-new-file", "Open a new untitled file", secondary: true))
            .Append(CreateAction(
                "Console", "editor-page-open:console", "Open the Console window", secondary: true))
            .Append(CreateAction(
                "Render", "editor-page-open:render", "Open the Render window", secondary: true))
            .Append(CreateAction(
                "Clear", "editor-page-clear", "Clear Console and Render output", secondary: true))
            .Append(CreateAction("Close", "editor-page-home", "Return to the examples home"));
        titlebar.Append(title).Append(actions);

        _workbench = Own(DomElement.Create("div")
            .Class("editor-page-workbench")
            .Attr("data-cell-region", RegionId)
            .Attr("data-cell-owner", ProjectId)
            .Attr("role", "region")
            .Attr("aria-label", "Editor layout"));

        var statusbar = Own(DomElement.Create("footer").Class("editor-page-statusbar"));
        _activeFileName = Own(DomElement.Create("span"));
        _workspaceStatus = Own(DomElement.Create("span"));
        var runtime = Own(DomElement.Create("span").Text("C# workspace · browser-wasm"));
        statusbar.Append(_activeFileName).Append(_workspaceStatus).Append(runtime);

        _layoutRevision = Own(DomElement.Create("div")
            .Class("floating-layer")
            .Class("editor-page-layout-revision")
            .Attr("data-cell-layout-revision", "0")
            .Attr("aria-hidden", "true"));

        _diagnostics = Own(DomElement.Create("div").Class("editor-page-diagnostics"));
        _consoleOutput = Own(DomElement.Create("pre")
            .Class("editor-page-console-output")
            .Text("Build or run the workspace to see output."));
        _consolePanel = Own(DomElement.Create("div")
            .Class("editor-page-console")
            .Attr("role", "tabpanel"));
        _consolePanel.Append(_diagnostics).Append(_consoleOutput);
        _renderOutput = Own(DomElement.Create("pre")
            .Class("editor-page-render-output")
            .Text("Render surface · waiting for Notebook.Render(…)"));
        _renderPanel = Own(DomElement.Create("div")
            .Class("editor-page-render")
            .Attr("role", "tabpanel"));
        _renderPanel.Append(_renderOutput);

        InitDialogs();

        _root
            .Append(titlebar)
            .Append(_confirmBanner)
            .Append(_workbench)
            .Append(statusbar)
            .Append(_layoutRevision);
        _container.Text(string.Empty).Append(_root);

        _editors = new(world, references);
        _state = CreateLayoutState();
        ApplyLayout();
        UpdateChrome();

        ActivateFilesSidebarTab();
        RenderExplorer();
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_initialized) {
            return;
        }
        _initialized = true;
        await _workspace.InitializeAsync(cancellationToken);
        if (_disposed) {
            return;
        }
        RenderExplorer();
        var initial = _workspace.RootEntries
            .SelectMany(EnumerateFilesUnder)
            .FirstOrDefault();
        if (initial != default) {
            await OpenEntryAsync(initial, pin: true);
        }
        _buildStatus.Text("Ready");
        UpdateChrome();
    }

    private IEnumerable<Entity> EnumerateFilesUnder(Entity entity)
        => EditorWorkspace.IsFolder(entity)
            ? _workspace.ChildrenOf(entity).SelectMany(EnumerateFilesUnder)
            : [entity];

    public bool Route(string payload)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var separator = payload.IndexOf(':');
        var eventName = separator < 0 ? payload : payload[..separator];
        var argument = separator < 0 ? string.Empty : payload[(separator + 1)..];

        switch (eventName) {
            case "editor-page-tab":
                ActivateTab(argument);
                return true;
            case "editor-page-tab-close":
                Track(CloseTabAsync(argument));
                return true;
            case "editor-page-close-active-tab":
                CloseActiveTab();
                return true;
            case "editor-page-open":
                OpenPanelWindow(argument);
                return true;
            case "editor-page-build":
                StartOperation(run: false);
                return true;
            case "editor-page-run":
                StartOperation(run: true);
                return true;
            case "editor-page-clear":
                ClearOutput();
                return true;
            case "editor-page-save":
                Track(SaveActiveAsync());
                return true;
            case "editor-page-save-all":
                Track(SaveAllAsync());
                return true;
            case "editor-page-save-as-begin":
                BeginSaveAs();
                return true;
            case "editor-page-save-as-commit":
                Track(CommitSaveAsAsync());
                return true;
            case "editor-page-save-as-cancel":
                CancelSaveAs();
                return true;
            case "editor-page-revert":
                RevertActiveFile();
                return true;
            case "editor-page-new-file":
                Track(CreateUntitledAsync());
                return true;
            case "editor-page-home":
                return true;
            case "editor-page-tree-open":
                Track(OpenEntryAsync(argument, pin: false));
                return true;
            case "editor-page-tree-pin":
                Track(OpenEntryAsync(argument, pin: true));
                return true;
            case "editor-page-tree-new-file":
                Track(CreateEntryAsync(argument, folder: false));
                return true;
            case "editor-page-tree-new-folder":
                Track(CreateEntryAsync(argument, folder: true));
                return true;
            case "editor-page-rename-entry":
                Track(RenameEntryAsync(argument));
                return true;
            case "editor-page-tree-delete":
                BeginDelete(argument);
                return true;
            case "editor-page-dialog-save":
                ResolveCloseDialog(CloseDecision.Save);
                return true;
            case "editor-page-dialog-dont-save":
                ResolveCloseDialog(CloseDecision.DontSave);
                return true;
            case "editor-page-dialog-cancel":
                CancelCloseDialog();
                return true;
            case "editor-page-dialog-delete-confirm":
                Track(ConfirmDeleteAsync());
                return true;
            case "editor-page-dialog-delete-cancel":
                CancelDeleteDialog();
                return true;
            case "cell":
                CellMove(argument);
                return true;
            case "cell-detach":
                CellDetach(argument);
                return true;
            case "cell-resize":
                CellResize(argument);
                return true;
            case "cell-normalize":
                CellNormalize(argument);
                return true;
            case "editor-focus":
                return SurfaceIds.Contains(argument);
            default: {
                    var handled = _editors.Route(payload);
                    if (handled) {
                        SyncDirtyIndicators();
                    }
                    return handled;
                }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) {
            return;
        }
        _disposed = true;
        _lifetime.Cancel();
        foreach (var operation in _operations.ToArray()) {
            await operation;
        }
        _editors.Dispose();
        DisposeSurfaces();
        DisposeLayoutViews();
        DisposeExplorer();
        RestoreExplorerSidebarTab();
        _workspace.Dispose();
        _root.Remove();
        for (var index = _ownedElements.Count - 1; index >= 0; index--) {
            _ownedElements[index].Dispose();
        }
        _container.Dispose();
        _lifetime.Dispose();
    }

    private string KeyFor(Entity entity)
    {
        if (_entryKeys.TryGetValue(entity, out var key)) {
            return key;
        }
        key = "e" + (++_entryKeySeed).ToString(CultureInfo.InvariantCulture);
        _entryKeys[entity] = key;
        _entriesByKey[key] = entity;
        return key;
    }

    private bool TryResolve(string key, out Entity entity) => _entriesByKey.TryGetValue(key, out entity);

    private void Forget(Entity entity)
    {
        if (_entryKeys.Remove(entity, out var key)) {
            _entriesByKey.Remove(key);
        }
    }

    private string WindowIdFor(Entity entity) => $"window-{KeyFor(entity)}-{ScriptSuffix}";

    private string TabIdFor(Entity entity) => $"tab-{KeyFor(entity)}-{ScriptSuffix}";

    private async Task OpenEntryAsync(string key, bool pin)
    {
        if (!TryResolve(key, out var entity)) {
            return;
        }
        await OpenEntryAsync(entity, pin);
    }

    private async Task OpenEntryAsync(Entity entity, bool pin)
    {
        if (!entity.IsValid || EditorWorkspace.IsFolder(entity)) {
            return;
        }
        await _workspace.OpenFileAsync(entity, pin, _lifetime.Token);
        if (_disposed) {
            return;
        }
        EnsureFileWindow(entity);
        _activeEntity = entity;
        UpdateState(state => NotebookCellLayout.OpenTabIntoHome(state, TabIdFor(entity)));
        RefreshExplorerSelection();
    }

    private void ActivateTab(string tabId)
    {
        if (!_state.Tabs.TryGetValue(tabId, out var tab)) {
            return;
        }
        var window = _state.Windows[tab.WindowId];
        if (window.Kind == CellWindowKind.Script && TryResolve(window.SourceId, out var entity)) {
            _activeEntity = entity;
        }
        UpdateState(state => NotebookCellLayout.Activate(state, tabId));
        RefreshExplorerSelection();
    }

    private void OpenPanelWindow(string windowKey)
        => UpdateState(state => NotebookCellLayout.OpenWindow(
            state,
            windowKey == "console" ? ConsoleWindowId : RenderWindowId));

    private void CloseActiveTab()
    {
        var active = FindActiveTabId();
        if (active is not null) {
            Track(CloseTabAsync(active));
        }
    }

    private string? FindActiveTabId()
        => NotebookCellLayout.EnumerateGroups(_state)
            .Select(static group => group.ActiveTabId)
            .FirstOrDefault(id => _state.Tabs.ContainsKey(id));

    private async Task CloseTabAsync(string tabId)
    {
        if (!_state.Tabs.TryGetValue(tabId, out var tab)
            || !_state.Windows.TryGetValue(tab.WindowId, out var window)) {
            return;
        }
        if (window.Kind != CellWindowKind.Script) {
            UpdateState(state => NotebookCellLayout.CloseTab(state, tabId));
            return;
        }
        if (!TryResolve(window.SourceId, out var entity) || !entity.IsValid) {
            return;
        }
        if (EditorWorkspace.IsDirty(entity)) {
            var choice = await RequestCloseDecisionAsync(entity);
            if (choice == CloseDecision.Cancel) {
                return;
            }
            if (choice == CloseDecision.Save) {
                if (!await SaveEntityAsync(entity)) {
                    return;
                }
            }
        }
        FinishCloseTab(entity, tabId, window.Id);
    }

    private void FinishCloseTab(Entity entity, string tabId, string windowId)
    {
        var destroyed = _workspace.CloseFile(entity);
        _state = NotebookCellLayout.CloseTab(_state, tabId);
        _state = _state with {
            Windows = _state.Windows.Remove(windowId),
            Tabs = _state.Tabs.Remove(tabId),
        };
        if (_activeEntity == entity) {
            _activeEntity = null;
        }
        if (destroyed) {
            Forget(entity);
            RenderExplorer();
        }
        ApplyLayout();
        UpdateChrome();
        RefreshExplorerSelection();
    }

    private void UpdateState(
        Func<NotebookCellState, NotebookCellState> update,
        long? expectedRevision = null,
        bool acknowledge = false)
    {
        if (_disposed) {
            return;
        }
        if (expectedRevision is { } expected && expected != _state.Revision) {
            return;
        }
        var next = update(_state);
        if (next == _state && !acknowledge) {
            return;
        }
        if (!NotebookCellLayout.IsValid(next)) {
            throw new InvalidOperationException("The editor layout operation produced an invalid state.");
        }
        _state = next with { Revision = _state.Revision + 1 };
        ApplyLayout();
        UpdateChrome();
    }

    private void CellMove(string arguments)
    {
        var parts = arguments.Split(':');
        if (parts.Length != 5
            || !long.TryParse(parts[0], out var revision)
            || !Enum.TryParse<CellDropPosition>(parts[3], ignoreCase: true, out var position)
            || !int.TryParse(parts[4], out var targetIndex)) {
            return;
        }
        UpdateState(
            state => NotebookCellLayout.Cell(state, parts[1], parts[2], position, targetIndex),
            revision,
            acknowledge: true);
    }

    private void CellDetach(string arguments)
    {
        var parts = arguments.Split(':');
        if (parts.Length != 6
            || !long.TryParse(parts[0], out var revision)
            || !int.TryParse(parts[2], out var pointerX)
            || !int.TryParse(parts[3], out var pointerY)
            || !int.TryParse(parts[4], out var viewportWidth)
            || !int.TryParse(parts[5], out var viewportHeight)) {
            return;
        }
        UpdateState(
            state => NotebookCellLayout.Detach(
                state, parts[1], pointerX, pointerY, viewportWidth, viewportHeight),
            revision,
            acknowledge: true);
    }

    private void CellResize(string arguments)
    {
        var parts = arguments.Split(':');
        if (parts.Length != 3
            || !long.TryParse(parts[0], out var revision)
            || !double.TryParse(parts[2], CultureInfo.InvariantCulture, out var ratio)) {
            return;
        }
        UpdateState(
            state => NotebookCellLayout.ResizeSplit(state, parts[1], ratio),
            revision,
            acknowledge: true);
    }

    private void CellNormalize(string arguments)
    {
        var parts = arguments.Split(':');
        if (parts.Length != 2
            || !int.TryParse(parts[0], out var viewportWidth)
            || !int.TryParse(parts[1], out var viewportHeight)) {
            return;
        }
        UpdateState(
            state => NotebookCellLayout.NormalizeFloatingHosts(state, viewportWidth, viewportHeight));
    }

    private void StartOperation(bool run)
    {
        if (_busy) {
            return;
        }
        SaveEditorSources();
        _busy = true;
        UpdateState(state => NotebookCellLayout.OpenWindow(state, ConsoleWindowId));
        _buildStatus.Text(run ? "Building to run…" : "Building…").ToggleClass("busy", true);
        _diagnostics.Text(string.Empty);
        _consoleOutput.Text(run ? "Building workspace before execution…" : "Building C# workspace…");
        DomRuntime.Flush();
        Track(BuildAsync(run));
    }

    private async Task BuildAsync(bool run)
    {
        try {
            await _workspace.EnsureAllLoadedAsync(_lifetime.Token);
            if (_disposed) {
                return;
            }
            var files = _workspace.AllFiles()
                .Select(entity => new EditorProjectCompiler.File(
                    KeyFor(entity), EditorWorkspace.NameOf(entity), entity.Get<WorkspaceFile>().Content))
                .ToArray();
            var result = await _compiler.CompileAsync(files, _lifetime.Token);
            if (_lifetime.IsCancellationRequested) {
                return;
            }
            RenderDiagnostics(result.Diagnostics);
            if (!result.Success) {
                _buildStatus.Text("Build failed").ToggleClass("busy", false);
                _consoleOutput.Text($"Build failed with {ErrorCount(result.Diagnostics)} error(s).\n");
                return;
            }
            if (!run) {
                _buildStatus.Text("Build succeeded").ToggleClass("busy", false);
                _consoleOutput.Text($"Build succeeded · {files.Length} C# files\n");
                return;
            }

            _buildStatus.Text("Running…").ToggleClass("busy", true);
            _consoleOutput.Text("Running workspace…\n");
            DomRuntime.Flush();
            var execution = await EditorProjectCompiler.ExecuteAsync(result.AssemblyImage!);
            if (_lifetime.IsCancellationRequested) {
                return;
            }
            _consoleOutput
                .Text(execution.StandardOutput + execution.StandardError)
                .ToggleClass("output-error", execution.StandardError.Length > 0);
            _renderOutput.Text(execution.RenderRequested
                ? execution.RenderOutput
                : "Render surface · run completed without Notebook.Render(…)");
            if (execution.RenderRequested) {
                UpdateState(state => NotebookCellLayout.OpenWindow(state, RenderWindowId));
            }
            _buildStatus
                .Text(execution.Success ? "Run succeeded" : "Run failed")
                .ToggleClass("busy", false);
        } finally {
            _busy = false;
            if (!_disposed) {
                _buildStatus.ToggleClass("busy", false);
                DomRuntime.Flush();
            }
        }
    }

    private void RenderDiagnostics(IReadOnlyList<EditorProjectCompiler.DiagnosticInfo> diagnostics)
    {
        _diagnostics.Text(string.Empty);
        foreach (var diagnostic in diagnostics) {
            var location = diagnostic.FileName is null
                ? string.Empty
                : $"{diagnostic.FileName}({diagnostic.Line},{diagnostic.Column}): ";
            var text = $"{location}{diagnostic.Severity.ToString().ToLowerInvariant()} "
                + $"{diagnostic.Id}: {diagnostic.Message}";
            using var line = DomElement.Create(diagnostic.FileId is null ? "div" : "button")
                .Class("editor-page-diagnostic")
                .Class(diagnostic.Severity == NotebookDiagnosticSeverity.Error
                    ? "diagnostic-error"
                    : "diagnostic-warning")
                .Text(text);
            if (diagnostic.FileId is { } fileId) {
                line.Attr("type", "button").On("click", $"editor-page-tree-open:{fileId}");
            }
            _diagnostics.Append(line);
        }
    }

    private void RevertActiveFile()
    {
        if (_activeEntity is not { } entity || !entity.IsValid || entity.Contains<UntitledFile>()) {
            return;
        }
        var saved = entity.Get<WorkspaceFile>().SavedContent;
        _workspace.SetContent(entity, saved);
        _workspace.Pin(entity);
        RefreshEditorForEntity(entity);
        UpdateChrome("reverted to the saved version");
    }

    private async Task CreateUntitledAsync()
    {
        var entity = _workspace.CreateUntitled();
        EnsureFileWindow(entity);
        _activeEntity = entity;
        UpdateState(state => NotebookCellLayout.OpenTabIntoHome(state, TabIdFor(entity)));
        await Task.CompletedTask;
    }

    private void ClearOutput()
    {
        _diagnostics.Text(string.Empty);
        _consoleOutput.Text(string.Empty).ToggleClass("output-error", false);
        _renderOutput.Text("Render surface · waiting for Notebook.Render(…)");
    }

    private void UpdateChrome(string? message = null)
    {
        if (_activeEntity is { } entity && entity.IsValid) {
            var dirty = EditorWorkspace.IsDirty(entity) ? " ●" : string.Empty;
            _activeFileName.Text($"{EditorWorkspace.NameOf(entity)}{dirty}");
        } else {
            _activeFileName.Text(string.Empty);
        }
        _workspaceStatus.Text(message ?? string.Empty);
    }

    private DomElement CreateAction(string label, string payload, string title, bool secondary = false)
    {
        var action = Own(DomElement.Create("button")
            .Class("btn")
            .Attr("type", "button")
            .Attr("title", title)
            .On("click", payload)
            .Text(label));
        return secondary ? action.Class("editor-page-secondary-action") : action;
    }

    private DomElement Own(DomElement element)
    {
        _ownedElements.Add(element);
        return element;
    }

    private void Track(Task operation)
    {
        var observed = ObserveAsync(operation);
        _operations.Add(observed);
        _ = RemoveWhenCompleteAsync(observed);
    }

    private async Task ObserveAsync(Task operation)
    {
        try {
            await operation;
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) {
        }
        catch (Exception error) {
            if (!_disposed) {
                _busy = false;
                _buildStatus.Text("Operation failed").ToggleClass("busy", false);
                _consoleOutput.Text(error.ToString()).ToggleClass("output-error", true);
                DomRuntime.ReportError(error.ToString());
                DomRuntime.Flush();
            }
        }
    }

    private async Task RemoveWhenCompleteAsync(Task operation)
    {
        await operation;
        _operations.Remove(operation);
    }

    private static int ErrorCount(IReadOnlyList<EditorProjectCompiler.DiagnosticInfo> diagnostics)
        => diagnostics.Count(static diagnostic => diagnostic.Severity == NotebookDiagnosticSeverity.Error);

    private static IReadOnlyList<HighlightRun> HighlightsFor(string source)
        => source.Length <= HighlightLimit ? CSharpHighlighter.Classify(source) : [];
}
