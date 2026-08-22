using System.Globalization;
using Sia;
using Sia_Examples.Dom;
using Sia_Examples.Notebook;

namespace Sia_Examples.Editor;

internal sealed class BrowserEditorPage : IAsyncDisposable
{
    private const string EditorId = "standalone-editor";
    private const string SplitId = "editor-page-main-split";
    private const int HighlightLimit = 200_000;

    private readonly BrowserEditorRegistry _editors;
    private readonly BrowserEditorHost _editor;
    private readonly EditorProjectCompiler _compiler;
    private readonly DomElement _container;
    private readonly DomElement _root;
    private readonly DomElement _editorSurface;
    private readonly DomElement _activeFileName;
    private readonly DomElement _workspaceStatus;
    private readonly DomElement _buildStatus;
    private readonly DomElement _diagnostics;
    private readonly DomElement _consoleOutput;
    private readonly DomElement _renderOutput;
    private readonly DomElement _consolePanel;
    private readonly DomElement _renderPanel;
    private readonly DomElement _consoleTab;
    private readonly DomElement _renderTab;
    private readonly DomElement _layoutRevision;
    private readonly DomElement _splitFirst;
    private readonly DomElement _splitSecond;
    private readonly DomElement _splitSeparator;
    private readonly List<EditorFile> _fileOrder;
    private readonly Dictionary<string, EditorFile> _files;
    private readonly Dictionary<string, FileNode> _fileNodes = [];
    private readonly List<DomElement> _ownedElements = [];
    private readonly CancellationTokenSource _lifetime = new();
    private readonly HashSet<Task> _operations = [];

    private EditorFile _activeFile;
    private long _splitRevision;
    private bool _busy;
    private bool _disposed;

    public BrowserEditorPage(World world, ICompilationReferenceResolver references)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(references);

        _fileOrder = CreateFiles().ToList();
        _files = _fileOrder.ToDictionary(static file => file.Id, StringComparer.Ordinal);
        _activeFile = _files["program"];
        _compiler = new(references);
        _container = DomElement.Find("notebook");
        _root = Own(DomElement.Create("section")
            .Class("editor-page")
            .Attr("aria-label", "Standalone Sia editor")
            .Attr("data-cell-region", "editor-project"));

        var explorer = Own(DomElement.Create("aside")
            .Class("editor-page-explorer")
            .Attr("aria-label", "Editor files"));
        var explorerHeader = Own(DomElement.Create("div")
            .Class("editor-page-explorer-header")
            .Text("EXPLORER"));
        var workspaceLabel = Own(DomElement.Create("div")
            .Class("editor-page-workspace-label")
            .Text("▾ SIA.EDITORLAB"));
        var sourceLabel = Own(DomElement.Create("div")
            .Class("editor-page-folder-label")
            .Text("▾ src"));
        var fileList = Own(DomElement.Create("div")
            .Class("editor-page-files")
            .Attr("role", "tree"));
        explorer.Append(explorerHeader).Append(workspaceLabel).Append(sourceLabel).Append(fileList);

        var titlebar = Own(DomElement.Create("header").Class("editor-page-titlebar"));
        var title = Own(DomElement.Create("div").Class("editor-page-title"));
        var brand = Own(DomElement.Create("span")
            .Class("editor-page-brand")
            .Text("Sia.NET · Editor Lab"));
        _buildStatus = Own(DomElement.Create("span")
            .Class("editor-page-build-status")
            .Attr("role", "status")
            .Text("Ready"));
        title.Append(brand).Append(_buildStatus);
        var actions = Own(DomElement.Create("div").Class("editor-page-actions"));
        actions
            .Append(CreateAction("Build", "editor-page-build", "Compile the C# project"))
            .Append(CreateAction("Run ▶", "editor-page-run", "Compile and run the C# project"))
            .Append(CreateAction("Reset", "editor-page-reset", "Reset the active file", secondary: true))
            .Append(CreateAction("Close", "editor-page-home", "Return to the examples home"));
        titlebar.Append(title).Append(actions);

        var workbench = Own(DomElement.Create("div").Class("editor-page-workbench"));
        var tabs = Own(DomElement.Create("div")
            .Class("editor-page-tabs")
            .Attr("role", "tablist")
            .Attr("aria-label", "Open files"));
        var split = Own(DomElement.Create("div")
            .Class("editor-page-split")
            .Class("cell-split")
            .Class("vertical")
            .Attr("data-cell-split", SplitId));
        _splitFirst = Own(DomElement.Create("div")
            .Class("split-child")
            .Class("editor-page-editor-pane"));
        _editorSurface = Own(DomElement.Create("div").Class("editor-page-editor"));
        _splitFirst.Append(_editorSurface);
        _splitSeparator = Own(DomElement.Create("div")
            .Class("separator")
            .Attr("role", "separator")
            .Attr("tabindex", "0")
            .Attr("aria-label", "Resize editor and output panels")
            .Attr("aria-valuemin", "15")
            .Attr("aria-valuemax", "85")
            .Attr("aria-orientation", "horizontal")
            .Attr("aria-disabled", "false"));
        _splitSecond = Own(DomElement.Create("div")
            .Class("split-child")
            .Class("editor-page-panel-pane"));

        var panel = Own(DomElement.Create("section")
            .Class("editor-page-panel")
            .Attr("aria-label", "Project output"));
        var panelHeader = Own(DomElement.Create("div").Class("editor-page-panel-header"));
        var panelTabs = Own(DomElement.Create("div")
            .Class("editor-page-panel-tabs")
            .Attr("role", "tablist")
            .Attr("aria-label", "Output views"));
        _consoleTab = CreatePanelTab("Console", PanelKind.Console);
        _renderTab = CreatePanelTab("Render", PanelKind.Render);
        panelTabs.Append(_consoleTab).Append(_renderTab);
        panelHeader
            .Append(panelTabs)
            .Append(CreateAction("Clear", "editor-page-clear", "Clear Console and Render", secondary: true));
        var panelContent = Own(DomElement.Create("div").Class("editor-page-panel-content"));
        _consolePanel = Own(DomElement.Create("div")
            .Class("editor-page-console")
            .Attr("role", "tabpanel"));
        _diagnostics = Own(DomElement.Create("div").Class("editor-page-diagnostics"));
        _consoleOutput = Own(DomElement.Create("pre")
            .Class("editor-page-console-output")
            .Text("Build or run the project to see output."));
        _consolePanel.Append(_diagnostics).Append(_consoleOutput);
        _renderPanel = Own(DomElement.Create("div")
            .Class("editor-page-render")
            .Attr("role", "tabpanel"));
        _renderOutput = Own(DomElement.Create("pre")
            .Class("editor-page-render-output")
            .Text("Render surface · waiting for Notebook.Render(…)"));
        _renderPanel.Append(_renderOutput);
        panelContent.Append(_consolePanel).Append(_renderPanel);
        panel.Append(panelHeader).Append(panelContent);
        _splitSecond.Append(panel);
        split.Append(_splitFirst).Append(_splitSeparator).Append(_splitSecond);
        workbench.Append(tabs).Append(split);

        var statusbar = Own(DomElement.Create("footer").Class("editor-page-statusbar"));
        _activeFileName = Own(DomElement.Create("span"));
        _workspaceStatus = Own(DomElement.Create("span"));
        var runtime = Own(DomElement.Create("span").Text("C# project · browser-wasm"));
        statusbar.Append(_activeFileName).Append(_workspaceStatus).Append(runtime);

        _layoutRevision = Own(DomElement.Create("div")
            .Class("floating-layer")
            .Class("editor-page-layout-revision")
            .Attr("data-cell-layout-revision", "0")
            .Attr("aria-hidden", "true"));

        foreach (var file in _fileOrder) {
            var node = CreateFileNode(file);
            _fileNodes.Add(file.Id, node);
            fileList.Append(node.ExplorerButton);
            tabs.Append(node.TabButton);
        }

        _root
            .Append(explorer)
            .Append(titlebar)
            .Append(workbench)
            .Append(statusbar)
            .Append(_layoutRevision);
        _container.Text(string.Empty).Append(_root);

        SetSplitRatio(0.72);
        SetPanel(PanelKind.Console);
        _editors = new(world, references);
        _editor = _editors.Add(
            _editorSurface,
            EditorId,
            _activeFile.Source,
            HighlightsFor(_activeFile.Source));
        UpdateChrome();
    }

    public bool Route(string payload)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var separator = payload.IndexOf(':');
        var eventName = separator < 0 ? payload : payload[..separator];
        var argument = separator < 0 ? string.Empty : payload[(separator + 1)..];

        switch (eventName) {
            case "editor-page-file":
                Activate(argument);
                return true;
            case "editor-page-build":
                StartOperation(run: false);
                return true;
            case "editor-page-run":
                StartOperation(run: true);
                return true;
            case "editor-page-panel":
                if (Enum.TryParse<PanelKind>(argument, ignoreCase: true, out var panel)) {
                    SetPanel(panel);
                }
                return true;
            case "editor-page-clear":
                ClearOutput();
                return true;
            case "editor-page-reset":
                ResetActiveFile();
                return true;
            case "cell-resize":
                ResizeSplit(argument);
                return true;
            case "editor-focus":
                return argument == EditorId;
            default:
                return _editors.Route(payload);
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
        _root.Remove();
        for (var index = _ownedElements.Count - 1; index >= 0; index--) {
            _ownedElements[index].Dispose();
        }
        _container.Dispose();
        _lifetime.Dispose();
    }

    private void Activate(string id)
    {
        if (!_files.TryGetValue(id, out var file) || ReferenceEquals(file, _activeFile)) {
            return;
        }
        SaveActiveSource();
        _activeFile = file;
        _editor.Update(file.Source, HighlightsFor(file.Source));
        UpdateChrome();
    }

    private void StartOperation(bool run)
    {
        if (_busy) {
            return;
        }
        SaveActiveSource();
        _busy = true;
        SetPanel(PanelKind.Console);
        _buildStatus.Text(run ? "Building to run…" : "Building…").ToggleClass("busy", true);
        _diagnostics.Text(string.Empty);
        _consoleOutput.Text(run ? "Building project before execution…" : "Building C# project…");
        DomRuntime.Flush();
        Track(BuildAsync(run));
    }

    private async Task BuildAsync(bool run)
    {
        try {
            var files = _fileOrder.Select(static file =>
                new EditorProjectCompiler.File(file.Id, file.Name, file.Source)).ToArray();
            var result = await _compiler.CompileAsync(files, _lifetime.Token);
            if (_disposed) {
                return;
            }
            RenderDiagnostics(result.Diagnostics);
            if (!result.Success) {
                _buildStatus.Text("Build failed").ToggleClass("busy", false);
                _consoleOutput.Text($"Build failed with {ErrorCount(result.Diagnostics)} error(s).\n");
                SetPanel(PanelKind.Console);
                return;
            }
            if (!run) {
                _buildStatus.Text("Build succeeded").ToggleClass("busy", false);
                _consoleOutput.Text($"Build succeeded · {_fileOrder.Count} C# files\n");
                return;
            }

            _buildStatus.Text("Running…").ToggleClass("busy", true);
            _consoleOutput.Text("Running EditorProject…\n");
            DomRuntime.Flush();
            var execution = await EditorProjectCompiler.ExecuteAsync(result.AssemblyImage!);
            if (_disposed) {
                return;
            }
            _consoleOutput
                .Text(execution.StandardOutput + execution.StandardError)
                .ToggleClass("output-error", execution.StandardError.Length > 0);
            _renderOutput.Text(execution.RenderRequested
                ? execution.RenderOutput
                : "Render surface · run completed without Notebook.Render(…)");
            SetPanel(execution.RenderRequested ? PanelKind.Render : PanelKind.Console);
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
                line.Attr("type", "button").On("click", $"editor-page-file:{fileId}");
            }
            _diagnostics.Append(line);
        }
    }

    private void ResetActiveFile()
    {
        _activeFile.Source = _activeFile.InitialSource;
        _editor.Update(_activeFile.Source, HighlightsFor(_activeFile.Source));
        UpdateChrome("reset to bundled sample");
    }

    private void ResizeSplit(string arguments)
    {
        var parts = arguments.Split(':');
        if (parts.Length != 3
            || !long.TryParse(parts[0], out var revision)
            || revision != _splitRevision
            || parts[1] != SplitId
            || !double.TryParse(parts[2], CultureInfo.InvariantCulture, out var ratio)) {
            return;
        }
        SetSplitRatio(ratio);
        _splitRevision++;
        _layoutRevision.Attr("data-cell-layout-revision", _splitRevision.ToString(CultureInfo.InvariantCulture));
    }

    private void SetSplitRatio(double ratio)
    {
        var normalized = Math.Clamp(ratio, 0.15, 0.85);
        _splitFirst.Attr("style", $"--cell-split-share:{normalized.ToString("0.####", CultureInfo.InvariantCulture)}");
        _splitSecond.Attr("style", $"--cell-split-share:{(1 - normalized).ToString("0.####", CultureInfo.InvariantCulture)}");
        _splitSeparator.Attr("aria-valuenow", Math.Round(normalized * 100).ToString(CultureInfo.InvariantCulture));
    }

    private void SetPanel(PanelKind panel)
    {
        var console = panel == PanelKind.Console;
        _consoleTab.ToggleClass("active", console).Attr("aria-selected", console ? "true" : "false");
        _renderTab.ToggleClass("active", !console).Attr("aria-selected", console ? "false" : "true");
        _consolePanel.ToggleClass("hidden", !console);
        _renderPanel.ToggleClass("hidden", console);
    }

    private void ClearOutput()
    {
        _diagnostics.Text(string.Empty);
        _consoleOutput.Text(string.Empty).ToggleClass("output-error", false);
        _renderOutput.Text("Render surface · waiting for Notebook.Render(…)");
    }

    private void SaveActiveSource() => _activeFile.Source = _editor.Source;

    private void UpdateChrome(string? message = null)
    {
        foreach (var (id, node) in _fileNodes) {
            node.SetActive(id == _activeFile.Id);
        }
        _activeFileName.Text($"{_activeFile.Name} · in memory");
        _workspaceStatus.Text(message ?? string.Empty);
    }

    private FileNode CreateFileNode(EditorFile file)
    {
        var explorerButton = Own(DomElement.Create("button")
            .Class("editor-page-file")
            .Attr("type", "button")
            .Attr("role", "treeitem")
            .On("click", $"editor-page-file:{file.Id}"));
        var fileIcon = Own(DomElement.Create("span")
            .Class("editor-page-file-icon")
            .Attr("aria-hidden", "true")
            .Text("C#"));
        var fileName = Own(DomElement.Create("span").Text(file.Name));
        explorerButton.Append(fileIcon).Append(fileName);

        var tabButton = Own(DomElement.Create("button")
            .Class("editor-page-tab")
            .Attr("type", "button")
            .Attr("role", "tab")
            .On("click", $"editor-page-file:{file.Id}"));
        var tabIcon = Own(DomElement.Create("span").Attr("aria-hidden", "true").Text("C#"));
        var tabName = Own(DomElement.Create("span").Text(file.Name));
        tabButton.Append(tabIcon).Append(tabName);
        return new(explorerButton, tabButton);
    }

    private DomElement CreatePanelTab(string label, PanelKind panel)
        => Own(DomElement.Create("button")
            .Class("editor-page-panel-tab")
            .Attr("type", "button")
            .Attr("role", "tab")
            .On("click", $"editor-page-panel:{panel.ToString().ToLowerInvariant()}")
            .Text(label));

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
                SetPanel(PanelKind.Console);
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

    private static IReadOnlyList<EditorFile> CreateFiles()
    {
        const string program = """
            using Sia;

            using var world = new World();
            var entity = world.Create(HList.From(
                new Position(10, 20),
                new Velocity(2, -1)));

            Console.WriteLine($"Created {entity}");
            Notebook.Render($"Render frame · {entity}");
            """;
        const string systems = """
            using Sia;

            public static class MovementSystem
            {
                public static void Update(World world, float deltaTime)
                {
                    _ = world;
                    _ = deltaTime;
                }
            }

            public readonly record struct Position(float X, float Y);
            public readonly record struct Velocity(float X, float Y);
            """;
        return [
            new("program", "Program.cs", program),
            new("systems", "MovementSystem.cs", systems),
        ];
    }

    private enum PanelKind
    {
        Console,
        Render,
    }

    private sealed class EditorFile(string id, string name, string source)
    {
        public string Id { get; } = id;
        public string Name { get; } = name;
        public string InitialSource { get; } = source;
        public string Source { get; set; } = source;
    }

    private sealed class FileNode(DomElement explorerButton, DomElement tabButton)
    {
        public DomElement ExplorerButton { get; } = explorerButton;
        public DomElement TabButton { get; } = tabButton;

        public void SetActive(bool active)
        {
            ExplorerButton.ToggleClass("active", active).Attr("aria-selected", active.ToString().ToLowerInvariant());
            TabButton
                .ToggleClass("active", active)
                .Attr("aria-selected", active.ToString().ToLowerInvariant())
                .Attr("tabindex", active ? "0" : "-1");
        }
    }
}
