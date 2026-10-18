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
    private readonly DomElement _container;
    private readonly DomElement _root;
    private readonly DomElement _workbench;
    private readonly DomElement _activeFileName;
    private readonly DomElement _workspaceStatus;
    private readonly DomElement _buildStatus;
    private readonly DomElement _compileAction;
    private readonly DomElement _diagnostics;
    private readonly DomElement _consoleOutput;
    private readonly DomElement _programOutput;
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
    private long _activationGeneration;
    private bool _busy;
    private bool _disposed;
    private bool _initialized;

    public BrowserEditorPage(World world, MetadataReferenceProvider references, IWorkspaceStorage storage)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(references);
        ArgumentNullException.ThrowIfNull(storage);

        _workspace = new(world, storage);
        _shaderCompiler = new(references);
        _container = DomElement.Find("notebook");
        _root = Own(DomElement.Create("section")
            .Class("editor-page")
            .Attr("aria-label", "Sia C# Editor Lab"));

        var titlebar = Own(DomElement.Create("header").Class("editor-page-titlebar"));
        _buildStatus = Own(DomElement.Create("span")
            .Class("editor-page-build-status")
            .Attr("role", "status")
            .Text("Loading workspace…"));
        var menus = Own(DomElement.Create("nav").Class("editor-page-menus").Attr("aria-label", "Editor commands"));
        CreateMenu(menus, "File", [
            ("New File", "editor-page-new-file", "Open a new untitled file"),
            ("Save", "editor-page-save", "Save the active file (Ctrl+S)"),
            ("Save All", "editor-page-save-all", "Save every open file (Ctrl+Shift+S)"),
            ("Save As…", "editor-page-save-as-begin", "Save the active file under a new path"),
            ("Revert", "editor-page-revert", "Discard unsaved edits in the active file"),
        ]);
        CreateMenu(menus, "View", [
            ("Console", "editor-page-open:console", "Open Console and diagnostics"),
            ("Render", "editor-page-open:render", "Open the Render window"),
            ("Clear Output", "editor-page-clear", "Clear Console and Render output"),
        ]);
        var actions = Own(DomElement.Create("div").Class("editor-page-actions"));
        _compileAction = CreateAction("Compile", "editor-compile-action", "Compile C# and inspect shader artifacts when present")
            .Attr("aria-disabled", "false");
        actions
            .Append(CreateAction("Run", "editor-shader-run", "Run the C# program or programmable RenderGraph from the active folder"))
            .Append(_compileAction)
            .Append(CreateAction("Close", "editor-page-home", "Return to the examples home"));
        titlebar.Append(menus).Append(actions);

        _workbench = Own(DomElement.Create("div")
            .Class("editor-page-workbench")
            .Attr("data-cell-region", RegionId)
            .Attr("data-cell-owner", ProjectId)
            .Attr("role", "region")
            .Attr("aria-label", "Editor layout"));

        var statusbar = Own(DomElement.Create("footer").Class("editor-page-statusbar"));
        _activeFileName = Own(DomElement.Create("span"));
        _workspaceStatus = Own(DomElement.Create("span"));
        statusbar.Append(_activeFileName).Append(_workspaceStatus).Append(_buildStatus);

        _layoutRevision = Own(DomElement.Create("div")
            .Class("floating-layer")
            .Class("editor-page-layout-revision")
            .Attr("data-cell-layout-revision", "0")
            .Attr("aria-hidden", "true"));

        _diagnostics = Own(DomElement.Create("div").Class("editor-page-diagnostics"));
        _consoleOutput = Own(DomElement.Create("pre")
            .Class("editor-page-console-output")
            .Text("Compile the active folder to see diagnostics and timings."));
        _consolePanel = Own(DomElement.Create("div")
            .Class("editor-page-console")
            .Attr("role", "tabpanel"));
        _programOutput = Own(DomElement.Create("pre").Class("editor-page-console-output").Class("editor-program-output")
            .Attr("aria-label", "Program Console"));
        _consolePanel.Append(_diagnostics).Append(_consoleOutput).Append(_programOutput);
        _renderOutput = Own(DomElement.Create("pre")
            .Class("editor-page-render-output")
            .Text("Compile C# to inspect WGSL and SPIR-V, then render the result"));
        _renderPanel = Own(DomElement.Create("div")
            .Class("editor-page-render")
            .Attr("role", "tabpanel"));
        InitShaderPanel();

        InitDialogs();

        _root
            .Append(titlebar)
            .Append(_confirmBanner)
            .Append(_workbench)
            .Append(statusbar)
            .Append(_layoutRevision);
        _container.Text(string.Empty).Append(_root);
        _uniformInspector = new();

        _editors = new(world, references);
        _state = CreateLayoutState();
        ApplyLayout();
        UpdateChrome();

        ActivateFilesSidebarTab();
        RenderExplorer();
        Track(PumpProgramConsoleAsync());
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_initialized) {
            return;
        }
        _initialized = true;
        var generation = _activationGeneration;
        await _workspace.InitializeAsync(cancellationToken);
        if (_disposed) {
            return;
        }
        RenderExplorer();
        var initial = _workspace.RootEntries
            .SelectMany(EnumerateFilesUnder)
            .FirstOrDefault();
        if (initial != default && generation == _activationGeneration) {
            await OpenEntryAsync(initial, pin: true, showConsole: true);
        }
        if (_disposed) {
            return;
        }
        _buildStatus.Text("Ready");
        OpenPanelWindow("console");
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
        if (RouteShader(eventName)) return true;

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
                Track(CommitSaveAsAsync(argument));
                return true;
            case "editor-page-save-as-cancel":
                CancelDialog(argument);
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
                ResolveCloseDialog(CloseDecision.Save, argument);
                return true;
            case "editor-page-dialog-dont-save":
                ResolveCloseDialog(CloseDecision.DontSave, argument);
                return true;
            case "editor-page-dialog-cancel":
                CancelDialog(argument);
                return true;
            case "editor-page-dialog-delete-confirm":
                Track(ConfirmDeleteAsync(argument));
                return true;
            case "editor-page-dialog-delete-cancel":
                CancelDialog(argument);
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
                if (!_editors.TryResolveCellId(argument, out var groupId)
                    || !_editorEntities.TryGetValue(groupId, out var focused)
                    || !focused.IsValid || !focused.Contains<OpenEditorFile>()) {
                    return false;
                }
                if (_activeEntity != focused) {
                    ++_activationGeneration;
                    _activeEntity = focused;
                    UpdateChrome();
                    RefreshExplorerSelection();
                }
                return true;
            default: {
                    var handled = _editors.Route(payload);
                    if (handled && eventName is "input" or "text" or "mut" or "key" or "complete") {
                        SyncDirtyIndicators();
                        CheckShaderSources();
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
        List<Exception>? failures = null;
        void Release(Action action)
        {
            try { action(); }
            catch (Exception error) { (failures ??= []).Add(error); }
        }
        Release(_lifetime.Cancel);
        Release(CancelCloseDialog);
        Release(StopRendering);
        Release(() => _uniformInspector?.Dispose());
        _uniformInspector = null;
        Release(() => _programConsole?.Dispose());
        foreach (var operation in _operations.ToArray()) {
            try { await operation; }
            catch (Exception error) { (failures ??= []).Add(error); }
        }
        Release(_editors.Dispose);
        Release(DisposeSurfaces);
        Release(DisposeLayoutViews);
        Release(DisposeExplorer);
        Release(RestoreExplorerSidebarTab);
        Release(_workspace.Dispose);
        Release(() => _root.Remove());
        for (var index = _ownedElements.Count - 1; index >= 0; index--) {
            Release(_ownedElements[index].Dispose);
        }
        Release(_container.Dispose);
        Release(_lifetime.Dispose);
        if (failures is { Count: 1 }) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failures[0]).Throw();
        if (failures is not null) throw new AggregateException(failures);
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
        _mementos.Remove(entity);
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

    private async Task OpenEntryAsync(Entity entity, bool pin, bool showConsole = false)
    {
        if (!entity.IsValid || EditorWorkspace.IsFolder(entity)) {
            return;
        }
        var generation = ++_activationGeneration;
        if (!await _workspace.EnsureContentLoadedAsync(entity, _lifetime.Token)
            || _disposed || generation != _activationGeneration) {
            return;
        }
        await _workspace.OpenFileAsync(entity, pin, _lifetime.Token);
        EnsureFileWindow(entity);
        _activeEntity = entity;
        UpdateState(state => {
            var opened = NotebookCellLayout.OpenTabIntoHome(state, TabIdFor(entity));
            return showConsole ? NotebookCellLayout.OpenWindow(opened, ConsoleWindowId) : opened;
        });
        RefreshExplorerSelection();
    }

    private void ActivateTab(string tabId)
    {
        if (!_state.Tabs.TryGetValue(tabId, out var tab)) {
            return;
        }
        ++_activationGeneration;
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
    {
        var active = NotebookCellLayout.EnumerateGroups(_state)
            .Select(static group => group.ActiveTabId).ToArray();
        if (_activeEntity is { } entity && entity.IsValid) {
            var tabId = TabIdFor(entity);
            if (active.Contains(tabId)) {
                return tabId;
            }
        }
        return active.FirstOrDefault(id => _state.Tabs.ContainsKey(id));
    }

    private async Task CloseTabAsync(string tabId)
    {
        if (!_state.Tabs.TryGetValue(tabId, out var tab)
            || !_state.Windows.TryGetValue(tab.WindowId, out var window)) {
            return;
        }
        if (window.Kind != CellWindowKind.Script) {
            if (window.Id == RenderWindowId) StopRendering();
            UpdateState(state => NotebookCellLayout.CloseTab(state, tabId));
            return;
        }
        if (!TryResolve(window.SourceId, out var entity) || !entity.IsValid) {
            return;
        }
        while (IsCurrentTab(entity, tab)) {
            SaveEditorSources(synchronizeNativeInput: true);
            if (!EditorWorkspace.IsDirty(entity)) {
                break;
            }
            var (choice, content) = await RequestCloseDecisionAsync(entity);
            if (!IsCurrentTab(entity, tab) || choice == CloseDecision.Cancel) {
                return;
            }
            SaveEditorSources(synchronizeNativeInput: true);
            if (choice == CloseDecision.DontSave) {
                if (entity.Get<WorkspaceFile>().Content == content) {
                    break;
                }
            } else {
                if (!await SaveEntityAsync(entity) || !IsCurrentTab(entity, tab)) {
                    return;
                }
            }
        }
        if (IsCurrentTab(entity, tab)) {
            FinishCloseTab(entity, tabId, window.Id);
        }
    }

    private bool IsCurrentTab(Entity entity, CellTab tab)
        => !_disposed && entity.IsValid
            && _state.Tabs.TryGetValue(tab.Id, out var current) && ReferenceEquals(current, tab);

    private void FinishCloseTab(Entity entity, string tabId, string windowId)
    {
        CancelDialog(entity);
        var destroyed = _workspace.CloseFile(entity);
        _state = NotebookCellLayout.CloseTab(_state, tabId);
        _state = _state with {
            Windows = _state.Windows.Remove(windowId),
            Tabs = _state.Tabs.Remove(tabId),
        };
        if (_activeEntity == entity) {
            ++_activationGeneration;
            _activeEntity = null;
            foreach (var group in NotebookCellLayout.EnumerateGroups(_state)) {
                if (_state.Tabs.TryGetValue(group.ActiveTabId, out var active)
                    && _state.Windows.TryGetValue(active.WindowId, out var next)
                    && next.Kind == CellWindowKind.Script
                    && TryResolve(next.SourceId, out var remaining)
                    && remaining.IsValid && remaining.Contains<OpenEditorFile>()) {
                    _activeEntity = remaining;
                    break;
                }
            }
        }
        if (destroyed) {
            Forget(entity);
            RenderExplorer();
        }
        ApplyLayout();
        if (_mementos.TryGetValue(entity, out var position)) {
            _mementos[entity] = position with { History = null };
        }
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

    private void RenderDiagnostics(IReadOnlyList<CSharpCompilationDiagnostic> diagnostics)
    {
        _diagnostics.Text(string.Empty);
        foreach (var diagnostic in diagnostics) {
            var location = diagnostic.SourcePath is null
                ? string.Empty
                : $"{diagnostic.SourcePath}({diagnostic.Line},{diagnostic.Column}): ";
            var text = $"{location}{diagnostic.Severity.ToString().ToLowerInvariant()} "
                + $"{diagnostic.Id}: {diagnostic.Message}";
            using var line = DomElement.Create(diagnostic.SourceId is null ? "div" : "button")
                .Class("editor-page-diagnostic")
                .Class(diagnostic.Severity == NotebookDiagnosticSeverity.Error
                    ? "diagnostic-error"
                    : "diagnostic-warning")
                .Text(text);
            if (diagnostic.SourceId is { } fileId) {
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
        CheckShaderSources();
        UpdateChrome("reverted to the saved version");
    }

    private async Task CreateUntitledAsync()
    {
        ++_activationGeneration;
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
        _programConsole?.Clear();
        _programOutput.Text(string.Empty).ToggleClass("output-error", false);
        _renderOutput.Text("Compile C# to inspect WGSL and SPIR-V, then render the result");
        StopRendering();
    }

    private void UpdateChrome(string? message = null)
    {
        if (_activeEntity is { } entity && entity.IsValid) {
            var dirty = EditorWorkspace.IsDirty(entity) ? " ●" : string.Empty;
            _activeFileName.Text($"{EditorWorkspace.NameOf(entity)}{dirty}");
        }
        else {
            _activeFileName.Text(string.Empty);
        }
        _workspaceStatus.Text(message ?? string.Empty);
    }

    private void CreateMenu(DomElement parent, string label, (string Label, string Payload, string Title)[] items)
    {
        var host = Own(DomElement.Create("details").Class("menu-toggle"));
        var summary = Own(DomElement.Create("summary").Class("btn").Attr("aria-label", label).Text(label));
        var menu = Own(DomElement.Create("div").Class("menu-popover").Class("editor-page-menu"));
        foreach (var (text, payload, title) in items) {
            menu.Append(CreateAction(text, payload, title).Class("menu-item"));
        }
        parent.Append(host.Append(summary).Append(menu));
    }

    private DomElement CreateAction(string label, string payload, string title)
        => Own(DomElement.Create("button")
            .Class("btn")
            .Attr("type", "button")
            .Attr("title", title)
            .On("click", payload)
            .Text(label));

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

    private static IReadOnlyList<HighlightRun> HighlightsFor(string source)
        => source.Length <= HighlightLimit ? CSharpHighlighter.Classify(source) : [];
}
