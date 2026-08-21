using Sia_Examples.Dom;
using Sia_Examples.Editor;

namespace Sia_Examples.Notebook;

public sealed class BrowserCellView : IDisposable
{
    private readonly CodeCellBlock _cell;
    private readonly BrowserEditorRegistry _editors;
    private readonly List<ScriptPane> _scripts = [];
    private readonly Dictionary<string, ScriptPane> _scriptsById = [];
    private readonly DomElement _diagnostics;
    private readonly DomElement _output;
    private readonly DomElement _render;

    public BrowserCellView(
        CodeCellBlock cell,
        IReadOnlyList<(CellScript Script, CellWindow Window)> scripts,
        BrowserEditorRegistry editors,
        CellWindow outputWindow,
        CellWindow renderWindow)
    {
        _cell = cell;
        _editors = editors;

        foreach (var (script, window) in scripts) {
            AddScript(script, window);
        }

        var output = DomElement.Create("div")
            .Class("output-window")
            .Attr("role", "region")
            .Attr("aria-label", "Cell output");
        _diagnostics = DomElement.Create("div")
            .Class("diagnostics")
            .Class("hidden")
            .Attr("role", "alert");
        _output = DomElement.Create("pre").Class("output").Class("hidden");
        output.Append(_diagnostics).Append(_output);
        Output = new(outputWindow, output);

        _render = DomElement.Create("div")
            .Class("render-window")
            .Attr("data-render-surface", cell.Id)
            .Attr("aria-live", "polite")
            .Text("Render surface · ready");
        Render = new(renderWindow, _render);
    }

    public IReadOnlyList<BrowserCellWindowView> Scripts => [.. _scripts.Select(static pane => pane.Window)];

    public BrowserCellWindowView Output { get; }

    public BrowserCellWindowView Render { get; }

    public BrowserCellWindowView AddScript(CellScript script, CellWindow window)
    {
        var pane = new ScriptPane(_cell, script, window);
        _scripts.Add(pane);
        _scriptsById.Add(script.Id, pane);
        return pane.Window;
    }

    public BrowserCellWindowView? RemoveScript(string scriptId)
    {
        if (!_scriptsById.Remove(scriptId, out var pane)) {
            return null;
        }
        _scripts.Remove(pane);
        _editors.Remove(scriptId);
        var window = pane.Window;
        pane.Dispose();
        return window;
    }

    public void BeginEditing(string scriptId)
    {
        if (_scriptsById.TryGetValue(scriptId, out var pane)) {
            pane.BeginEditing();
        }
    }

    public void EndEditing(string scriptId)
    {
        if (_scriptsById.TryGetValue(scriptId, out var pane)) {
            pane.EndEditing();
        }
    }

    public void UpdateDirtyState(string scriptId)
    {
        if (_scriptsById.TryGetValue(scriptId, out var pane)) {
            pane.UpdateDirtyState();
        }
    }

    public void DiscardChanges(string scriptId)
    {
        if (_scriptsById.TryGetValue(scriptId, out var pane)) {
            pane.DiscardChanges();
        }
    }

    public void Update(string scriptId, CellState state)
    {
        if (!_scriptsById.TryGetValue(scriptId, out var pane)) {
            return;
        }
        pane.Update(state, _editors);
        RenderSharedOutput();
        RenderSharedSurface();
    }

    public void UpdateScope(string? scope)
    {
        foreach (var pane in _scripts) {
            pane.UpdateScope(scope);
        }
    }

    public void Dispose()
    {
        _diagnostics.Dispose();
        _output.Dispose();
        Render.Dispose();
        Output.Dispose();
        foreach (var pane in _scripts) {
            pane.Dispose();
        }
    }

    private void RenderSharedOutput()
    {
        var standardOutput = string.Concat(_scripts.Select(static pane => pane.Current?.StandardOutput ?? string.Empty));
        var standardError = string.Concat(_scripts.Select(static pane => pane.Current?.StandardError ?? string.Empty));
        _output
            .Text(standardOutput + standardError)
            .ToggleClass("hidden", standardOutput.Length == 0 && standardError.Length == 0)
            .ToggleClass("output-error", standardError.Length > 0);

        var labeled = _scripts.Count > 1;
        _diagnostics.Text(string.Empty);
        var any = false;
        foreach (var pane in _scripts) {
            foreach (var diagnostic in pane.Current?.Diagnostics ?? []) {
                any = true;
                var prefix = labeled ? $"{pane.Window.Window.Title} " : "";
                using var line = DomElement.Create("div")
                    .Class(diagnostic.Severity == NotebookDiagnosticSeverity.Error
                        ? "diagnostic-error"
                        : "diagnostic-warning")
                    .Text($"{prefix}({diagnostic.Line},{diagnostic.Column}) "
                        + $"{diagnostic.Id}: {diagnostic.Message}");
                _diagnostics.Append(line);
            }
        }
        _diagnostics.ToggleClass("hidden", !any);
    }

    private void RenderSharedSurface()
    {
        var requesting = _scripts.LastOrDefault(static pane => pane.Current?.RenderRequested == true);
        var renderOutput = requesting?.Current?.RenderOutput ?? string.Empty;
        var running = _scripts.Any(static pane => pane.Current?.Phase == CellPhase.Running);
        _render
            .ToggleClass("render-active", requesting is not null)
            .Attr("aria-busy", running ? "true" : "false")
            .Text(renderOutput.Length > 0
                ? renderOutput
                : GetRenderLabel(running, requesting is not null));
    }

    private static string GetRenderLabel(bool running, bool requested)
        => requested
            ? "Render surface · active"
            : running
                ? "Render surface · waiting for Notebook.Render(…)"
                : "Render surface · ready";

    private sealed class ScriptPane : IDisposable
    {
        private readonly DomElement _script;
        private readonly DomElement _phase;
        private readonly DomElement _runStop;
        private readonly DomElement _editActions;
        private readonly DomElement _scopeInput;
        private readonly DomElement _scopeSummary;
        private readonly DomElement _code;
        private BrowserEditorHost? _editor;

        public ScriptPane(CodeCellBlock cell, CellScript script, CellWindow window)
        {
            Cell = cell;
            ScriptId = script.Id;

            var toolbar = DomElement.Create("div").Class("toolbar");
            _phase = DomElement.Create("span")
                .Class("phase")
                .Attr("role", "status")
                .Attr("aria-live", "polite");

            var scopeInputId = NotebookElementIds.ScopeInput(cell.Id);
            var scopeSummaryId = NotebookElementIds.ScopeSummary(cell.Id);
            var scopeEditor = DomElement.Create("div").Class("scope-editor");
            _scopeSummary = DomElement.Create("button")
                .Class("scope-summary")
                .Id(scopeSummaryId)
                .Attr("type", "button")
                .Attr("data-inline-begin", scopeInputId)
                .Attr("aria-label", "Edit cell scope")
                .Attr("title", "Edit cell scope")
                .Text(ScopeLabel(cell.Scope));
            _scopeInput = DomElement.Create("input")
                .Class("scope-input")
                .Id(scopeInputId)
                .Attr("type", "text")
                .Attr("aria-label", "Cell scope")
                .Attr("title", "Cells sharing a scope run together, running one replays the scope in order. Empty means an isolated cell.")
                .Attr("value", cell.Scope ?? string.Empty)
                .Attr("placeholder", "scope")
                .Attr("data-inline-input", "true")
                .Attr("data-allow-empty", "true")
                .Attr("data-inline-trim", "true")
                .Attr("data-inline-summary", scopeSummaryId)
                .Attr("data-inline-prefix", "Scope: ")
                .Attr("data-inline-empty-label", "Isolated")
                .Attr("data-saved-value", cell.Scope ?? string.Empty);
            var scopeActions = DomElement.Create("div")
                .Class("inline-edit-actions")
                .Class("scope-actions");
            using (var saveScope = CreateIconButton(
                "▣",
                "Save scope",
                $"set-scope:{cell.Id}")) {
                saveScope.Attr("data-inline-save", scopeInputId);
                scopeActions.Append(saveScope);
            }
            using (var discardScope = DomElement.Create("button")
                .Class("icon-btn")
                .Attr("type", "button")
                .Attr("aria-label", "Discard scope changes")
                .Attr("title", "Discard scope changes")
                .Attr("data-inline-discard", scopeInputId)
                .Text("↶")) {
                scopeActions.Append(discardScope);
            }
            var scopeEditPanel = DomElement.Create("div")
                .Class("scope-edit-panel")
                .Append(_scopeInput);
            scopeEditor.Append(_scopeSummary).Append(scopeEditPanel).Append(scopeActions);

            using (var controls = DomElement.Create("div").Class("cell-controls")) {
                _editActions = DomElement.Create("div")
                    .Class("cell-edit-actions")
                    .Class("hidden");
                using (var save = CreateIconButton("▣", "Save changes", $"save:{script.Id}")
                    .Attr("title", "Save changes (Ctrl+S)")
                    .Attr("aria-keyshortcuts", "Control+S Meta+S")) {
                    _editActions.Append(save);
                }
                using (var discard = CreateIconButton(
                    "↶",
                    "Discard changes",
                    $"discard:{script.Id}")) {
                    _editActions.Append(discard);
                }
                _runStop = CreateIconButton("▶", "Run cell", $"toggle-run:{script.Id}")
                    .Attr("title", "Run cell (Ctrl+Enter)")
                    .Attr("aria-keyshortcuts", "Control+Enter Meta+Enter");
                using var more = DomElement.Create("details").Class("menu-toggle");
                using var summary = DomElement.Create("summary")
                    .Class("icon-btn")
                    .Attr("aria-label", "More cell actions")
                    .Attr("title", "More cell actions")
                    .Text("⋯");
                using var menu = DomElement.Create("div").Class("menu-popover");
                AppendClientMenuButton(
                    menu,
                    "◎",
                    "Set scope",
                    "data-inline-begin",
                    scopeInputId);
                AppendMenuButton(menu, "⌁", "Compile", $"compile:{script.Id}");
                AppendMenuButton(menu, "›_", "Open console",
                    $"open-window:{NotebookCellState.WindowId(cell.Id, CellWindowKind.Output)}");
                AppendMenuButton(menu, "◇", "Open render",
                    $"open-window:{NotebookCellState.WindowId(cell.Id, CellWindowKind.Render)}");
                AppendMenuButton(menu, "⌁", "New code cell below", $"insert-cell:{cell.Id}");
                AppendMenuButton(menu, "¶", "New text below", $"insert-paragraph:{cell.Id}");
                AppendMenuButton(menu, "↑", "Move cell up", $"move-cell-up:{cell.Id}");
                AppendMenuButton(menu, "↓", "Move cell down", $"move-cell-down:{cell.Id}");
                AppendMenuButton(menu, "×", "Delete cell", $"remove-cell:{cell.Id}");
                more.Append(summary).Append(menu);
                controls.Append(_editActions);
                controls.Append(_runStop);
                controls.Append(more);
                toolbar.Append(_phase).Append(scopeEditor).Append(controls);
            }

            _script = DomElement.Create("div")
                .Class("script-window")
                .Attr("data-script-id", script.Id)
                .Attr("data-script-editable", cell.Editable ? "true" : "false");
            _code = DomElement.Create(cell.Editable ? "div" : "pre").Class("code");
            if (cell.Editable) {
                _code.Class("code-edit").Id(NotebookElementIds.Editor(script.Id));
            }
            _script.Append(_code);
            Window = new(window, _script, toolbar);
        }

        public CodeCellBlock Cell { get; }

        public string ScriptId { get; }

        public BrowserCellWindowView Window { get; }

        public CellState? Current { get; private set; }

        public void BeginEditing()
        {
            if (!Cell.Editable) {
                return;
            }
            _script.ToggleClass("is-editing", true);
            UpdateDirtyState();
        }

        public void EndEditing()
        {
            _script.ToggleClass("is-editing", false).ToggleClass("is-dirty", false);
            _editActions.ToggleClass("hidden", true);
        }

        public void UpdateDirtyState()
        {
            if (!Cell.Editable || _editor is null || Current is null) {
                return;
            }
            var dirty = _editor.Source != Current.Value.Source;
            _script.ToggleClass("is-dirty", dirty);
            _editActions.ToggleClass("hidden", !dirty);
        }

        public void UpdateScope(string? scope)
        {
            var value = scope?.Trim() ?? string.Empty;
            _scopeInput
                .Attr("value", value)
                .Attr("data-saved-value", value);
            _scopeSummary.Text(ScopeLabel(value));
        }

        public void DiscardChanges()
        {
            if (_editor is not null && Current is { } current) {
                _editor.Update(current.Source, current.Highlights);
            }
            EndEditing();
        }

        public void Update(CellState state, BrowserEditorRegistry editors)
        {
            var previous = Current;
            if (previous is null || previous.Value.Phase != state.Phase) {
                if (previous is { } value) {
                    _phase.ToggleClass(GetPhaseClass(value.Phase), false);
                }
                _phase
                    .ToggleClass(GetPhaseClass(state.Phase), true)
                    .Text(GetPhaseLabel(state.Phase));
                var active = state.Phase is CellPhase.Compiling or CellPhase.Running;
                _runStop
                    .Text(active ? "■" : "▶")
                    .Attr("aria-label", active ? "Stop cell" : "Run cell")
                    .Attr("title", active ? "Stop cell (Ctrl+Enter)" : "Run cell (Ctrl+Enter)");
            }

            if (previous is null
                || previous.Value.Source != state.Source
                || previous.Value.Highlights != state.Highlights) {
                if (Cell.Editable) {
                    _editor ??= editors.Add(_code, ScriptId, state.Source, state.Highlights);
                    if (previous is not null) {
                        _editor.Update(state.Source, state.Highlights);
                    }
                } else {
                    RenderHighlightedCode(_code, state.Source, state.Highlights);
                }
            }
            Current = state;
        }

        public void Dispose()
        {
            _runStop.Dispose();
            _editActions.Dispose();
            _phase.Dispose();
            _code.Dispose();
            Window.Dispose();
        }

        private static DomElement CreateIconButton(
            string icon,
            string label,
            string payload)
            => DomElement.Create("button")
                .Class("icon-btn")
                .Attr("type", "button")
                .Attr("aria-label", label)
                .Attr("title", label)
                .On("click", payload)
                .Text(icon);

        private static void AppendMenuButton(
            DomElement menu,
            string icon,
            string label,
            string payload)
        {
            using var button = DomElement.Create("button")
                .Class("menu-item")
                .Attr("type", "button")
                .On("click", payload);
            using var glyph = DomElement.Create("span")
                .Class("menu-icon")
                .Attr("aria-hidden", "true")
                .Text(icon);
            using var text = DomElement.Create("span").Text(label);
            button.Append(glyph).Append(text);
            menu.Append(button);
        }

        private static void AppendClientMenuButton(
            DomElement menu,
            string icon,
            string label,
            string attribute,
            string value)
        {
            using var button = DomElement.Create("button")
                .Class("menu-item")
                .Attr("type", "button")
                .Attr(attribute, value);
            using var glyph = DomElement.Create("span")
                .Class("menu-icon")
                .Attr("aria-hidden", "true")
                .Text(icon);
            using var text = DomElement.Create("span").Text(label);
            button.Append(glyph).Append(text);
            menu.Append(button);
        }

        private static string ScopeLabel(string? scope)
            => string.IsNullOrWhiteSpace(scope) ? "Isolated" : $"Scope: {scope}";

        private static void RenderHighlightedCode(
            DomElement element,
            string source,
            IReadOnlyList<HighlightRun> highlights)
        {
            element.Text(string.Empty);
            var position = 0;
            foreach (var highlight in highlights) {
                if (highlight.Start > position) {
                    using var text = DomElement.CreateText(source[position..highlight.Start]);
                    element.Append(text);
                }
                using var span = DomElement.Create("span")
                    .Class(CSharpHighlighter.CssClass(highlight.Classification))
                    .Text(source.Substring(highlight.Start, highlight.Length));
                element.Append(span);
                position = highlight.Start + highlight.Length;
            }
            if (position < source.Length) {
                using var text = DomElement.CreateText(source[position..]);
                element.Append(text);
            }
        }

        private static string GetPhaseClass(CellPhase phase)
            => $"phase-{phase.ToString().ToLowerInvariant()}";

        private static string GetPhaseLabel(CellPhase phase)
            => phase switch {
                CellPhase.Idle => "idle",
                CellPhase.Compiling => "compiling…",
                CellPhase.CompileError => "compile error",
                CellPhase.Compiled => "compiled",
                CellPhase.Running => "running…",
                CellPhase.RanSuccess => "done",
                CellPhase.RanError => "error",
                CellPhase.Interrupted => "interrupted",
                _ => phase.ToString(),
            };
    }
}
