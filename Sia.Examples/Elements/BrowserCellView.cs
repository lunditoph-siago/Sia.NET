using Sia_Examples.Editor;

namespace Sia_Examples.Notebook;

public sealed class BrowserCellView : IDisposable
{
    private readonly CodeCellBlock _cell;
    private readonly BrowserEditorRegistry _editors;
    private readonly BrowserElement _phase;
    private readonly BrowserElement _code;
    private readonly BrowserElement _diagnostics;
    private readonly BrowserElement _output;
    private BrowserEditorHost? _editor;
    private CellState? _current;

    public BrowserCellView(
        int number,
        CodeCellBlock cell,
        BrowserEditorRegistry editors)
    {
        _cell = cell;
        _editors = editors;
        Root = BrowserElement.Create("div").Class("cell");

        using var header = BrowserElement.Create("div").Class("cell-header");
        using var label = BrowserElement.Create("span")
            .Class("cell-label")
            .Text($"[{number}] {cell.Id}");
        _phase = BrowserElement.Create("span").Class("phase");
        header.Append(label).Append(_phase);

        using var controls = BrowserElement.Create("div").Class("cell-controls");
        AppendButton(controls, "Compile", $"compile:{cell.Id}");
        AppendButton(controls, "Run", $"run:{cell.Id}");
        AppendButton(controls, "Stop", $"stop:{cell.Id}");
        if (cell.Editable) {
            AppendButton(controls, "Save", $"save:{cell.Id}");
        }
        header.Append(controls);
        Root.Append(header);

        _code = BrowserElement.Create(cell.Editable ? "div" : "pre").Class("code");
        if (cell.Editable) {
            _code.Class("code-edit").Id(NotebookElementIds.Editor(cell.Id));
        }
        Root.Append(_code);

        _diagnostics = BrowserElement.Create("div").Class("diagnostics").Class("hidden");
        _output = BrowserElement.Create("pre").Class("output").Class("hidden");
        Root.Append(_diagnostics).Append(_output);
    }

    public BrowserElement Root { get; }

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
        Root.Remove();
        _output.Dispose();
        _diagnostics.Dispose();
        _code.Dispose();
        _phase.Dispose();
        Root.Dispose();
    }

    private void RenderDiagnostics(IReadOnlyList<NotebookDiagnostic> diagnostics)
    {
        _diagnostics.Text(string.Empty).ToggleClass("hidden", diagnostics.Count == 0);
        foreach (var diagnostic in diagnostics) {
            using var line = BrowserElement.Create("div")
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
        BrowserElement element,
        string source,
        IReadOnlyList<HighlightRun> highlights)
    {
        element.Text(string.Empty);
        var position = 0;
        foreach (var highlight in highlights) {
            if (highlight.Start > position) {
                using var text = BrowserElement.CreateText(source[position..highlight.Start]);
                element.Append(text);
            }
            using var span = BrowserElement.Create("span")
                .Class(CSharpHighlighter.CssClass(highlight.Classification))
                .Text(source.Substring(highlight.Start, highlight.Length));
            element.Append(span);
            position = highlight.Start + highlight.Length;
        }
        if (position < source.Length) {
            using var text = BrowserElement.CreateText(source[position..]);
            element.Append(text);
        }
    }

    private static void AppendButton(BrowserElement controls, string text, string payload)
    {
        using var button = BrowserElement.Create("button")
            .Class("btn")
            .On("click", payload)
            .Text(text);
        controls.Append(button);
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
