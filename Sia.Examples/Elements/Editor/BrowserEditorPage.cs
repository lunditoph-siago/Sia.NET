using Sia.Examples.Dom;
using Sia.Examples.Notebook;
using Sia;

namespace Sia.Examples.Editor;

internal sealed partial class BrowserEditorPage : IAsyncDisposable
{
    internal void ConfigureKeyboard(EditorKeymap keymap) => _editors.ConfigureKeyboard(keymap, CommandContext.EditorLab);
    internal void FlushCommands() => _editors.FlushCommands();
    internal event Action<EditorCommand, string> CommandInvoked {
        add => _editors.CommandInvoked += value;
        remove => _editors.CommandInvoked -= value;
    }

    private const string k_RegionId = "region-editor-page";
    private const string k_ProjectId = "editor-page";
    private const string k_ScriptSuffix = "script";
    private const double k_DefaultEditorShare = 0.72;
    private const int k_HighlightLimit = 200_000;

    private readonly EditorWorkspace _workspace;
    private readonly BrowserEditorRegistry _editors;
    private readonly DomElement _container;
    private readonly DomElement _root;
    private readonly DomElement _workbench;
    private readonly DomElement _runAction;
    private readonly DomElement _compileAction;
    private readonly DomElement _compactInputAction;
    private readonly DomElement _inputToolsAction;
    private readonly DomElement _operationError;
    private readonly DomElement _compilationOutput;
    private readonly DomElement _programOutput;
    private readonly DomElement _compilationPanel;
    private readonly DomElement _consolePanel;
    private readonly DomElement _renderPanel;
    private readonly DomElement _layoutRevision;
    private readonly List<DomElement> _ownedElements = [];
    private readonly CancellationTokenSource _lifetime = new();
    private readonly HashSet<Task> _operations = [];

    private NotebookCellState _state;
    private bool _busy;
    private bool _disposed;
    private bool _initialized;
    private bool _compactInput;
    private bool _inputToolsOpen;
    private int _inputResetGeneration;

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
            .Id("editor-page-status")
            .Class("editor-page-build-status")
            .Attr("role", "status")
            .Text("Loading workspace…"));
        var menus = Own(DomElement.Create("nav").Class("editor-page-menus").Attr("aria-label", "Editor commands"));
        var filesToggle = Own(DomElement.Create("button").Class("icon-btn").Class("editor-files-toggle")
            .Attr("type", "button").Attr("data-sidebar-toggle", "")
            .Attr("aria-label", "Files").Attr("title", "Show workspace files")
            .Attr("aria-controls", "sidebar").Attr("aria-expanded", "false"));
        var filesIcon = Own(DomElement.Create("svg").Attr("viewBox", "0 0 24 24")
            .Attr("width", "20").Attr("height", "20").Attr("fill", "none")
            .Attr("stroke", "currentColor").Attr("stroke-width", "1.4")
            .Attr("stroke-linejoin", "round").Attr("aria-hidden", "true"));
        filesIcon.Append(Own(DomElement.Create("path").Attr("d", "M3 7V5h7l2 2h9v13H3Z")));
        menus.Append(filesToggle.Append(filesIcon));
        CreateMenu(menus, "File", [
            ("New File", "editor-page-new-file", "Open a new untitled file"),
            ("Quick Open…", "workbench-command:QuickOpen", "Search workspace files"),
            ("Save", "editor-page-save", "Save the active file"),
            ("Save All", "editor-page-save-all", "Save every open file"),
            ("Save As…", "editor-page-save-as-begin", "Save the active file under a new path"),
            ("Revert", "editor-page-revert", "Discard unsaved edits in the active file"),
        ]);
        var view = CreateMenu(menus, "View", [
            ("Find…", "workbench-command:Find", "Find text in the active file"),
            ("Replace…", "workbench-command:Replace", "Find and replace text in the active file"),
            ("Symbol in File…", "workbench-command:DocumentSymbols", "Go to a C# declaration in the active file"),
            ("Symbol in Workspace…", "workbench-command:WorkspaceSymbols", "Go to a C# declaration across workspace files"),
            ("Compilation", "editor-page-open:compilation", "Open diagnostics and compiler outputs"),
            ("Console", "editor-page-open:console", "Open program logs"),
            ("Render", "editor-page-open:render", "Open the Render window"),
            ("Clear Output", "editor-page-clear", "Clear compiler output, program logs and rendering"),
        ]);
        _outputPaneAction = CreateAction("Output Pane", "editor-page-toggle-output", "Collapse the output pane to its tabs, or restore its previous size")
            .Class("menu-item").Attr("role", "menuitemcheckbox").Attr("aria-checked", "true");
        view.Append(_outputPaneAction);
        var tools = CreateMenu(menus, "Tools", [
            ("Command Palette…", "workbench-command:Commands", "Search and run commands"),
            ("Keyboard Shortcuts…", "workbench-command:Keybindings", "Configure keyboard shortcuts"),
        ]);
        _inputToolsAction = CreateAction("Editor Toolbar", "editor-page-input-tools", "Show or hide editing controls")
            .Class("menu-item").Class("editor-input-action").Attr("role", "menuitemcheckbox").Attr("aria-checked", "false");
        _compactInputAction = CreateAction("Compact Input Layout", "editor-page-compact-input", "Reserve space for manual keyboard input")
            .Class("menu-item").Class("editor-input-action").Attr("role", "menuitemcheckbox").Attr("aria-checked", "false");
        tools.Append(_inputToolsAction).Append(_compactInputAction)
            .Append(CreateAction("Reset Keyboard Reference", "editor-page-input-reset", "Close the keyboard first, then use the current viewport as its reference")
                .Class("menu-item").Class("editor-input-reset"));
        var actions = Own(DomElement.Create("div").Class("editor-page-actions"));
        _runAction = CreateIconAction("Run", "editor-run-action");
        _compileAction = CreateIconAction("Compile", "editor-compile-action");
        actions
            .Append(_runAction)
            .Append(_compileAction)
            .Append(CreateAction("Close", "editor-page-home", "Return to the examples home"));
        titlebar.Append(menus).Append(actions);

        _workbench = Own(DomElement.Create("div")
            .Class("editor-page-workbench")
            .Attr("data-cell-region", k_RegionId)
            .Attr("data-cell-owner", k_ProjectId)
            .Attr("role", "region")
            .Attr("aria-label", "Editor layout"));

        var statusbar = Own(DomElement.Create("footer").Class("editor-page-statusbar"));
        var touchbar = Own(DomElement.Create("div").Class("editor-page-touchbar")
            .Attr("role", "toolbar").Attr("aria-label", "Editor actions"));
        _activeFileName = Own(DomElement.Create("span").Class("editor-status-file"));
        _workspaceStatus = Own(DomElement.Create("span").Class("editor-status-workspace"));
        _cursorStatus = Own(DomElement.Create("span").Class("editor-status-cursor"));
        _inputModeStatus = Own(DomElement.Create("span").Class("editor-status-mode").Class("hidden")
            .Attr("aria-live", "polite").Attr("aria-label", "Vim mode"));
        _diagnosticStatus = CreateAction("Not checked", "editor-page-open:compilation", "Open compiler diagnostics")
            .Class("editor-status-item").Class("is-empty");
        _lastActionStatus = CreateAction(string.Empty, "editor-status-details", "Open last action details")
            .Class("editor-status-item").Class("editor-status-duration");
        statusbar.Append(_inputModeStatus).Append(_activeFileName).Append(_workspaceStatus).Append(_diagnosticStatus)
            .Append(_buildStatus).Append(_lastActionStatus).Append(_cursorStatus);

        _layoutRevision = Own(DomElement.Create("div")
            .Class("floating-layer")
            .Class("editor-page-layout-revision")
            .Attr("data-cell-layout-revision", "0")
            .Attr("aria-hidden", "true"));

        _diagnostics = Own(DomElement.Create("div").Class("editor-page-diagnostics"));
        _operationError = Own(DomElement.Create("div").Class("editor-operation-error").Attr("role", "alert"));
        _compilationOutput = Own(DomElement.Create("pre")
            .Class("editor-page-console-output")
            .Text("Compile the active folder to see diagnostics and timings."));
        _consolePanel = Own(DomElement.Create("div")
            .Class("editor-page-console")
            .Attr("role", "tabpanel"));
        _programOutput = Own(DomElement.Create("pre").Class("editor-page-console-output").Class("editor-program-output")
            .Attr("aria-label", "Program Console"));
        _consolePanel.Append(_programOutput);
        _compilationPanel = Own(DomElement.Create("div").Class("editor-page-console").Class("editor-page-compilation")
            .Attr("role", "tabpanel"));
        _compilationPanel.Append(_operationError).Append(_diagnostics);
        _renderPanel = Own(DomElement.Create("div")
            .Class("editor-page-render")
            .Attr("role", "tabpanel"));
        InitShaderPanel();

        InitDialogs();

        _root
            .Append(titlebar)
            .Append(_confirmBanner)
            .Append(_workbench)
            .Append(touchbar)
            .Append(statusbar)
            .Append(_layoutRevision);
        _container.Text(string.Empty).Append(_root);
        _layoutRevision.Append(_renderTools);
        _uniformInspector = new();

        _editors = new(world, references);
        _editors.InputModeChanged += UpdateInputMode;
        _editors.LanguageContext = CaptureLanguageSnapshotAsync;
        InitializeLanguageTools();
        _state = CreateLayoutState();
        ApplyLayout();
        UpdateChrome();
        UpdateActions();

        ActivateWorkspaceSidebar();
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
        var initial = _workspace.TryGetEntity(WorkspaceSeed.ProgramPath, out var program) ? program : _workspace.RootEntries
            .SelectMany(EnumerateFilesUnder)
            .FirstOrDefault();
        if (initial != default && generation == _activationGeneration) {
            await OpenEntryAsync(initial, pin: true, showConsole: true);
        }
        if (_disposed) {
            return;
        }
        SetBuildStatus("Ready");
        OpenPanelWindow("compilation");
        UpdateChrome();
    }

    private IEnumerable<Entity> EnumerateFilesUnder(Entity entity)
        => EditorWorkspace.IsFolder(entity)
            ? _workspace.ChildrenOf(entity).SelectMany(EnumerateFilesUnder)
            : [entity];

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
        Release(() => _liveDiagnosticsCancellation?.Cancel());
        Release(ClearLanguageResults);
        Release(() => _referenceResults.Dispose());
        Release(_renamePanel.Dispose);
        Release(_navigationPicker.Dispose);
        Release(_workspaceEdits.Dispose);
        Release(CancelCloseDialog);
        Release(StopRendering);
        Release(() => _uniformInspector?.Dispose());
        _uniformInspector = null;
        Release(() => _programConsole?.Dispose());
        foreach (var operation in _operations.ToArray()) {
            try { await operation; }
            catch (Exception error) { (failures ??= []).Add(error); }
        }
        try { await _editors.DisposeAsync(); }
        catch (Exception error) { (failures ??= []).Add(error); }
        Release(DisposeSurfaces);
        Release(DisposeLayoutViews);
        Release(DisposeExplorer);
        Release(RestoreExampleSidebar);
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
                _buildStatus.ToggleClass("busy", false);
                ShowOperationError($"Operation failed: {error.Message}", error);
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
        => source.Length <= k_HighlightLimit ? CSharpHighlighter.Classify(source) : [];
}
