#if BROWSER
using Sia_Examples.Editor;

namespace Sia_Examples.Notebook;

public static class NotebookRenderer
{
    public static string SourceElementId(string cellId) => "cell-source-" + cellId;
    public static string EditorContainerId(string cellId) => "editor-" + cellId;

    public static BrowserElement Render(NotebookDocument document, NotebookSession session)
    {
        var root = BrowserElement.Create("div");

        var title = BrowserElement.Create("h2").Class("title");
        title.Text(document.Title);
        root.Append(title);

        var number = 0;
        Dictionary<string, int> cellNumbers = [];
        foreach (var cell in session.Cells) {
            cellNumbers[cell.Id] = ++number;
        }

        foreach (var section in document.Sections) {
            var sectionEl = BrowserElement.Create("section").Class("section");
            var heading = BrowserElement.Create("h3").Class("section-title");
            heading.Text(section.Title);
            sectionEl.Append(heading);

            foreach (var block in section.Blocks) {
                switch (block) {
                    case ParagraphBlock p: {
                        var para = BrowserElement.Create("p").Class("paragraph");
                        AppendInlines(para, p.Inlines);
                        sectionEl.Append(para);
                        break;
                    }
                    case ListBlock l: {
                        var list = BrowserElement.Create("ul").Class("list");
                        foreach (var item in l.Items) {
                            var li = BrowserElement.Create("li");
                            AppendInlines(li, item);
                            list.Append(li);
                        }
                        sectionEl.Append(list);
                        break;
                    }
                    case CodeCellBlock cell:
                        sectionEl.Append(RenderCodeCell(
                            cellNumbers[cell.Id], cell, session.GetState(cell.Id), session.References));
                        break;
                }
            }

            root.Append(sectionEl);
        }

        return root;
    }

    private static void AppendInlines(BrowserElement parent, IReadOnlyList<Inline> inlines)
    {
        foreach (var inline in inlines) {
            switch (inline) {
                case TextInline t:
                    parent.Append(BrowserElement.CreateText(t.Text));
                    break;
                case CodeInline c:
                    parent.Append(BrowserElement.Create("code").Text(c.Text));
                    break;
            }
        }
    }

    private static BrowserElement RenderCodeCell(
        int number, CodeCellBlock cell, CellState state, IMetadataReferenceProvider references)
    {
        var wrapper = BrowserElement.Create("div").Class("cell");

        var header = BrowserElement.Create("div").Class("cell-header");
        var label = BrowserElement.Create("span").Class("cell-label");
        label.Text($"[{number}] {cell.Id}");
        var phase = BrowserElement.Create("span").Class("phase").Class(PhaseClass(state.Phase));
        phase.Text(PhaseLabel(state.Phase));
        header.Append(label).Append(phase);

        var controls = BrowserElement.Create("div").Class("cell-controls");
        controls.Append(BrowserElement.Create("button").Class("btn").On("click", "compile:" + cell.Id).Text("Compile"));
        controls.Append(BrowserElement.Create("button").Class("btn").On("click", "run:" + cell.Id).Text("Run"));
        controls.Append(BrowserElement.Create("button").Class("btn").On("click", "stop:" + cell.Id).Text("Stop"));
        if (cell.Editable) {
            controls.Append(BrowserElement.Create("button").Class("btn").On("click", "save:" + cell.Id).Text("Save"));
        }
        header.Append(controls);
        wrapper.Append(header);

        if (cell.Editable) {
            var editorContainer = BrowserElement.Create("div").Class("code").Class("code-edit");
            editorContainer.Id(EditorContainerId(cell.Id));
            wrapper.Append(editorContainer);
            BrowserEditorHost.GetOrCreate(editorContainer, cell.Id, state.Source, references);
        }
        else {
            var pre = BrowserElement.Create("pre").Class("code");
            AppendHighlighted(pre, state.Source, state.Highlights);
            wrapper.Append(pre);
        }

        if (state.Diagnostics.Count > 0) {
            var diagnostics = BrowserElement.Create("div").Class("diagnostics");
            foreach (var d in state.Diagnostics) {
                var line = BrowserElement.Create("div")
                    .Class(d.Severity == NotebookDiagnosticSeverity.Error ? "diagnostic-error" : "diagnostic-warning");
                line.Text($"({d.Line},{d.Column}) {d.Id}: {d.Message}");
                diagnostics.Append(line);
            }
            wrapper.Append(diagnostics);
        }

        if (state.StdOut.Length > 0 || state.StdErr.Length > 0) {
            var outputEl = BrowserElement.Create("pre").Class("output");
            outputEl.Text(state.StdOut + state.StdErr);
            if (state.StdErr.Length > 0) {
                outputEl.ToggleClass("output-error", true);
            }
            wrapper.Append(outputEl);
        }

        return wrapper;
    }

    private static void AppendHighlighted(BrowserElement pre, string source, IReadOnlyList<HighlightRun> runs)
    {
        var cursor = 0;
        foreach (var run in runs) {
            if (run.Start > cursor) {
                pre.Append(BrowserElement.CreateText(source[cursor..run.Start]));
            }
            var span = BrowserElement.Create("span").Class(CSharpHighlighter.CssClass(run.Classification));
            span.Text(source.Substring(run.Start, run.Length));
            pre.Append(span);
            cursor = run.Start + run.Length;
        }
        if (cursor < source.Length) {
            pre.Append(BrowserElement.CreateText(source[cursor..]));
        }
    }

    private static string PhaseClass(CellPhase phase) => "phase-" + phase.ToString().ToLowerInvariant();

    private static string PhaseLabel(CellPhase phase) => phase switch {
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
#endif
