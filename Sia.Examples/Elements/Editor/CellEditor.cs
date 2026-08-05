using Sia;
using Sia.Reactive;
using Sia_Examples.Notebook;

namespace Sia_Examples.Editor;

[ReactiveComponent]
public static partial class CellEditor
{
    public static ReactiveNode Render(in CellEditorProps props, ref Hooks hooks)
    {
        var cellId = props.CellId;
        var view = props.View;
        var doc = hooks.UseState(new EditorDoc(props.InitialSource));
        var cursor = hooks.UseState(CursorState.Default);
        var highlights = hooks.UseState(new HighlightCache([]));

        hooks.UseEffect(
            new TokenizeDeps(doc.Value, highlights),
            setup: static (in TokenizeDeps d) =>
            {
                d.Highlights.Set(new HighlightCache(EditorTokenizer.Tokenize(d.Doc.Value.FullText)));
                return default(Unit);
            },
            cleanup: static (in Unit _) => { });

        hooks.UseEffect(
            new ViewBinding(view, doc.Value, cursor.Value, highlights.Value.Runs),
            setup: static (in ViewBinding b) =>
            {
                var (docComp, cur, v, runs) = (b.Doc, b.Cursor, b.View, b.Highlights);
                v.BeginRender();
                var scrollLine = Math.Clamp(cur.ScrollLine, 0, Math.Max(docComp.LineCount - v.VisibleLines, 0));
                if (cur.Line < scrollLine) scrollLine = cur.Line;
                if (cur.Line >= scrollLine + v.VisibleLines) scrollLine = cur.Line - v.VisibleLines + 1;
                scrollLine = Math.Clamp(scrollLine, 0, Math.Max(docComp.LineCount - 1, 0));
                var endLine = Math.Min(scrollLine + v.VisibleLines, docComp.LineCount);
                var offset = 0;
                for (var i = 0; i < scrollLine; i++) offset += docComp.Value[i].Length + 1;
                for (var i = scrollLine; i < endLine; i++)
                {
                    var screenRow = i - scrollLine;
                    v.RenderGutter(screenRow, i, i == cur.Line);
                    v.RenderLine(screenRow, docComp.Value[i], cur, i, runs, offset);
                    offset += docComp.Value[i].Length + 1;
                }
                v.RenderCursor(cur.Line - scrollLine, cur.Column);
                v.RenderStatus(
                    $"{docComp.LineCount}L",
                    $"{(cur.Line + 1),3},{cur.Column + 1,-3}  {ModeLabel(cur.Mode)}");
                v.EndRender();
                return v;
            },
            cleanup: static (in IEditorView v) => v.Dispose());

        return Reactive.Entity(HList.From(
            new EditorEntityTag(cellId)));
    }

    private static string ModeLabel(EditorMode mode) => mode switch
    {
        EditorMode.Insert => "INSERT",
        EditorMode.Normal => "NORMAL",
        EditorMode.Visual => "VISUAL",
        EditorMode.VisualLine => "V-LINE",
        _ => "??",
    };
}

public readonly record struct EditorEntityTag(string CellId);

public readonly record struct HighlightCache(List<HighlightRun> Runs);

public readonly record struct TokenizeDeps(EditorDoc Doc, State<HighlightCache> Highlights);

public readonly record struct ViewBinding(
    IEditorView View,
    EditorDoc Doc,
    CursorState Cursor,
    List<HighlightRun> Highlights);
