using Sia;
using Sia.Reactive;

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

        hooks.UseEffect(
            new ViewBinding(view, doc.Value, cursor.Value),
            setup: static (in ViewBinding b) =>
            {
                var (docComp, cur, v) = (b.Doc, b.Cursor, b.View);
                v.BeginRender();
                var scrollLine = cur.ScrollLine;
                var endLine = Math.Min(scrollLine + v.VisibleLines, docComp.LineCount);
                for (var i = scrollLine; i < endLine; i++)
                {
                    var screenRow = i - scrollLine;
                    v.RenderGutter(screenRow, i, i == cur.Line);
                    v.RenderLine(screenRow, docComp.Value[i], cur, i);
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

public readonly record struct ViewBinding(
    IEditorView View,
    EditorDoc Doc,
    CursorState Cursor);
