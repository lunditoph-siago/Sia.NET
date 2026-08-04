#if !BROWSER
using System.Text;

namespace Sia_Examples.Notebook;

public static class NotebookRenderer
{
    private const string Reset = "\e[0m";
    private const string Bold = "\e[1m";
    private const string Dim = "\e[2m";
    private const string Underline = "\e[4m";
    private const string Red = "\e[31m";
    private const string Green = "\e[32m";
    private const string Yellow = "\e[33m";
    private const string InlineCodeColor = "\e[38;5;222m";

    public static IReadOnlyList<string> RenderLines(
        NotebookDocument document, NotebookSession session, string? editingCellId = null, IReadOnlyList<string>? editingBuffer = null)
    {
        var sb = new StringBuilder();
        sb.Append(Bold).Append(document.Title).Append(Reset).Append("\n\n");

        var number = 0;
        var cellNumbers = new Dictionary<string, int>();
        foreach (var cell in session.Cells) {
            cellNumbers[cell.Id] = ++number;
        }

        foreach (var section in document.Sections) {
            sb.Append(Bold).Append(Underline)
              .Append("== ").Append(section.Title).Append(" ==")
              .Append(Reset).Append("\n\n");

            foreach (var block in section.Blocks) {
                switch (block) {
                    case ParagraphBlock p:
                        AppendInlines(sb, p.Inlines);
                        sb.Append("\n\n");
                        break;

                    case ListBlock l:
                        foreach (var item in l.Items) {
                            sb.Append("  - ");
                            AppendInlines(sb, item);
                            sb.Append('\n');
                        }
                        sb.Append('\n');
                        break;

                    case CodeCellBlock cell when cell.Id == editingCellId:
                        AppendEditingCell(sb, cellNumbers[cell.Id], cell, editingBuffer ?? []);
                        break;

                    case CodeCellBlock cell:
                        AppendCodeCell(sb, cellNumbers[cell.Id], cell, session.GetState(cell.Id));
                        break;
                }
            }
        }

        return sb.ToString().Split('\n');
    }

    private static void AppendInlines(StringBuilder sb, IReadOnlyList<Inline> inlines)
    {
        foreach (var inline in inlines) {
            switch (inline) {
                case TextInline t:
                    sb.Append(t.Text);
                    break;
                case CodeInline c:
                    sb.Append(InlineCodeColor).Append(c.Text).Append(Reset);
                    break;
            }
        }
    }

    private static void AppendCodeCell(StringBuilder sb, int number, CodeCellBlock cell, CellState state)
    {
        var (label, color) = PhaseBadge(state.Phase);
        sb.Append(Bold).Append('[').Append(number).Append("] ").Append(cell.Id).Append(Reset)
          .Append("  ").Append(color).Append(label).Append(Reset).Append('\n');

        sb.Append(CSharpHighlighter.ToAnsi(state.Source, state.Highlights)).Append('\n');

        if (state.Diagnostics.Count > 0) {
            foreach (var d in state.Diagnostics) {
                var dColor = d.Severity == NotebookDiagnosticSeverity.Error ? Red : Yellow;
                sb.Append(dColor).Append($"  ({d.Line},{d.Column}) {d.Id}: {d.Message}").Append(Reset).Append('\n');
            }
        }

        if (state.StdOut.Length > 0 || state.StdErr.Length > 0) {
            sb.Append(Dim).Append("→ output").Append(Reset).Append('\n');
            sb.Append(state.StdOut);
            if (state.StdErr.Length > 0) {
                sb.Append(Red).Append(state.StdErr).Append(Reset);
            }
        }

        sb.Append('\n');
    }

    private static void AppendEditingCell(StringBuilder sb, int number, CodeCellBlock cell, IReadOnlyList<string> buffer)
    {
        sb.Append(Bold).Append('[').Append(number).Append("] ").Append(cell.Id).Append(Reset)
          .Append("  ").Append(Yellow).Append("editing…").Append(Reset).Append('\n');
        foreach (var line in buffer) {
            sb.Append(line).Append('\n');
        }
        sb.Append(Dim).Append("(blank line + :end to save, :cancel to discard)").Append(Reset).Append('\n');
        sb.Append('\n');
    }

    private static (string Label, string Color) PhaseBadge(CellPhase phase) => phase switch {
        CellPhase.Idle => ("idle", Dim),
        CellPhase.Compiling => ("compiling…", Yellow),
        CellPhase.CompileError => ("compile error", Red),
        CellPhase.Compiled => ("compiled", Green),
        CellPhase.Running => ("running…", Yellow),
        CellPhase.RanSuccess => ("done", Green),
        CellPhase.RanError => ("error", Red),
        CellPhase.Interrupted => ("interrupted", Red),
        _ => (phase.ToString(), Dim),
    };
}
#endif
