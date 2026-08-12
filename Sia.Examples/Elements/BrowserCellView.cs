using Sia_Examples.Dom;
using Sia_Examples.Editor;

namespace Sia_Examples.Notebook;

public sealed class BrowserCellView : IDisposable
{
    private readonly CodeCellBlock _cell;
    private readonly BrowserEditorRegistry _editors;
    private readonly DomElement _script;
    private readonly DomElement _phase;
    private readonly DomElement _runStop;
    private readonly DomElement _editActions;
    private readonly DomElement _code;
    private readonly DomElement _diagnostics;
    private readonly DomElement _output;
    private readonly DomElement _render;
    private BrowserEditorHost? _editor;
    private CellState? _current;

    public BrowserCellView(
        CodeCellBlock cell,
        BrowserEditorRegistry editors,
        CellWindow scriptWindow,
        CellWindow outputWindow,
        CellWindow renderWindow)
    {
        _cell = cell;
        _editors = editors;

        var toolbar = DomElement.Create("div").Class("toolbar");
        _phase = DomElement.Create("span").Class("phase");
        using (var controls = DomElement.Create("div").Class("cell-controls")) {
            _editActions = DomElement.Create("div")
                .Class("cell-edit-actions")
                .Class("hidden");
            using (var save = CreateIconButton("▣", "Save changes", $"save:{cell.Id}")) {
                _editActions.Append(save);
            }
            using (var discard = CreateIconButton(
                "↶",
                "Discard changes",
                $"discard:{cell.Id}")) {
                _editActions.Append(discard);
            }
            _runStop = CreateIconButton("▶", "Run cell", $"toggle-run:{cell.Id}");
            using var more = DomElement.Create("details").Class("menu-toggle");
            using var summary = DomElement.Create("summary")
                .Class("icon-btn")
                .Attr("aria-label", "More cell actions")
                .Attr("title", "More cell actions")
                .Text("⋯");
            using var menu = DomElement.Create("div").Class("menu-popover");
            AppendMenuButton(menu, "⌁", "Compile", $"compile:{cell.Id}");
            AppendMenuButton(menu, "›_", "Open console", $"open-window:{outputWindow.Id}");
            AppendMenuButton(menu, "◇", "Open render", $"open-window:{renderWindow.Id}");
            AppendMenuButton(menu, "⌁", "New code cell below", $"insert-cell:{cell.Id}");
            AppendMenuButton(menu, "¶", "New text below", $"insert-paragraph:{cell.Id}");
            AppendMenuButton(menu, "↑", "Move cell up", $"move-cell-up:{cell.Id}");
            AppendMenuButton(menu, "↓", "Move cell down", $"move-cell-down:{cell.Id}");
            AppendMenuButton(menu, "×", "Delete cell", $"remove-cell:{cell.Id}");
            more.Append(summary).Append(menu);
            controls.Append(_editActions);
            controls.Append(_runStop);
            controls.Append(more);
            toolbar.Append(_phase).Append(controls);
        }

        _script = DomElement.Create("div").Class("script-window");
        _code = DomElement.Create(cell.Editable ? "div" : "pre").Class("code");
        if (cell.Editable) {
            _code.Class("code-edit").Id(NotebookElementIds.Editor(cell.Id));
        }
        _script.Append(_code);
        Script = new(scriptWindow, _script, toolbar);

        var output = DomElement.Create("div").Class("output-window");
        _diagnostics = DomElement.Create("div").Class("diagnostics").Class("hidden");
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

    public BrowserCellWindowView Script { get; }

    public BrowserCellWindowView Output { get; }

    public BrowserCellWindowView Render { get; }

    public void BeginEditing()
    {
        if (!_cell.Editable) {
            return;
        }
        _script.ToggleClass("is-editing", true);
        _editActions.ToggleClass("hidden", false);
        UpdateDirtyState();
    }

    public void EndEditing()
    {
        _script.ToggleClass("is-editing", false).ToggleClass("is-dirty", false);
        _editActions.ToggleClass("hidden", true);
    }

    public void UpdateDirtyState()
    {
        if (!_cell.Editable || _editor is null || _current is null) {
            return;
        }
        _script.ToggleClass("is-dirty", _editor.Source != _current.Value.Source);
    }

    public void DiscardChanges()
    {
        if (_editor is not null && _current is { } current) {
            _editor.Update(current.Source, current.Highlights);
        }
        EndEditing();
    }

    public void Update(CellState state)
    {
        var previous = _current;
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
                .Attr("aria-label", active ? "Stop cell" : "Run cell");
        }
        if (previous is null
            || previous.Value.Phase != state.Phase
            || previous.Value.RenderRequested != state.RenderRequested
            || previous.Value.RenderOutput != state.RenderOutput) {
            _render
                .ToggleClass("render-active", state.RenderRequested)
                .Attr("aria-busy", state.Phase == CellPhase.Running ? "true" : "false")
                .Text(state.RenderOutput.Length > 0
                    ? state.RenderOutput
                    : GetRenderLabel(state.Phase, state.RenderRequested));
        }

        if (previous is null
            || previous.Value.Source != state.Source
            || previous.Value.Highlights != state.Highlights) {
            if (_cell.Editable) {
                _editor ??= _editors.Add(
                    _code,
                    _cell.Id,
                    state.Source,
                    state.Highlights);
                if (previous is not null) {
                    _editor.Update(state.Source, state.Highlights);
                }
            } else {
                RenderHighlightedCode(_code, state.Source, state.Highlights);
            }
        }

        if (previous is null || previous.Value.Diagnostics != state.Diagnostics) {
            RenderDiagnostics(state.Diagnostics);
        }
        if (previous is null
            || previous.Value.StandardOutput != state.StandardOutput
            || previous.Value.StandardError != state.StandardError) {
            RenderOutput(state.StandardOutput, state.StandardError);
        }
        _current = state;
    }

    public void Dispose()
    {
        _runStop.Dispose();
        _editActions.Dispose();
        _phase.Dispose();
        _code.Dispose();
        _diagnostics.Dispose();
        _output.Dispose();
        Render.Dispose();
        Output.Dispose();
        Script.Dispose();
    }

    private void RenderDiagnostics(IReadOnlyList<NotebookDiagnostic> diagnostics)
    {
        _diagnostics.Text(string.Empty).ToggleClass("hidden", diagnostics.Count == 0);
        foreach (var diagnostic in diagnostics) {
            using var line = DomElement.Create("div")
                .Class(diagnostic.Severity == NotebookDiagnosticSeverity.Error
                    ? "diagnostic-error"
                    : "diagnostic-warning")
                .Text($"({diagnostic.Line},{diagnostic.Column}) "
                    + $"{diagnostic.Id}: {diagnostic.Message}");
            _diagnostics.Append(line);
        }
    }

    private void RenderOutput(string standardOutput, string standardError)
    {
        var output = standardOutput + standardError;
        _output
            .Text(output)
            .ToggleClass("hidden", output.Length == 0)
            .ToggleClass("output-error", standardError.Length > 0);
    }

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

    private static string GetRenderLabel(CellPhase phase, bool requested)
        => requested
            ? "Render surface · active"
            : phase == CellPhase.Running
                ? "Render surface · waiting for Notebook.Render(…)"
                : "Render surface · ready";
}
