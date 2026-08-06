using Sia;
using Sia.Reactive;

namespace Sia_Examples.Editor;

public interface IEditorRenderer
{
    public int VisibleLines { get; }

    public ViewportState AdvanceViewport(int cursorLine, int totalLines);

    public void PrepareChanges(ChangeDesc changes, Text oldDoc, Text newDoc) { }

    public void BeginRender();
    public void RenderGutter(int screenRow, int lineIndex, bool isCursorLine);

    public void RenderLine(int screenRow, int lineIndex, string text, IReadOnlyList<StyledRun>? runs, string? lineClass);

    public void RenderSelection(int anchorLineIndex, int anchorCol, int headLineIndex, int headCol, bool empty);

    public void RenderStatus(string left, string right);
    public void EndRender();
    public void Dispose();
}

public readonly record struct EditorViewTag;

public readonly record struct EditorViewProps(
    IEditorRenderer Renderer,
    EditorState State,
    KeyBinding[]? Keymap,
    bool ReadOnly,
    int TabSize
) : IEquatable<EditorViewProps>;

public readonly record struct ViewRenderDeps(
    EditorState State,
    IEditorRenderer Renderer
) : IEquatable<ViewRenderDeps>;

[ReactiveComponent]
public static partial class EditorViewComponent
{
    public static ReactiveNode Render(in EditorViewProps props, ref Hooks hooks)
    {
        var state = hooks.UseState(props.State);
        var renderer = props.Renderer;

        hooks.UseEffect(
            new ViewRenderDeps(state.Value, renderer),
            setup: static (in ViewRenderDeps d) =>
            {
                var s = d.State; var r = d.Renderer;
                var sel = s.Selection.Main;
                var anchorLine = s.Doc.LineAt(sel.Anchor);
                var headLine = s.Doc.LineAt(sel.Head);

                var vp = r.AdvanceViewport(headLine.Number, s.Doc.Lines);
                var decorations = s.Field(EditorHighlights.Field);

                r.BeginRender();
                for (var ln = vp.From; ln <= vp.To; ln++) {
                    var sr = ln - vp.From;
                    var line = s.Doc.Line(ln);
                    r.RenderGutter(sr, ln - 1, ln == headLine.Number);

                    var marks = decorations.Between(line.From, line.To).Where(m => m.Value.Kind == DecorationKind.Mark).ToList();
                    var runs = marks.Count == 0 ? null : LineDecorator.Segment(line.Text, line.From, marks);
                    var lineClass = decorations.LineClass(line.From, line.To);
                    r.RenderLine(sr, ln - 1, line.Text, runs, lineClass);
                }

                r.RenderSelection(anchorLine.Number - 1, sel.Anchor - anchorLine.From,
                                  headLine.Number - 1, sel.Head - headLine.From, sel.Empty);
                r.RenderStatus($"{s.Doc.Lines}L  {s.Doc.Length}B", $"{headLine.Number,3}:{sel.Head - headLine.From + 1,-3}");
                r.EndRender();
                return default(Unit);
            },
            cleanup: static (in Unit _) => { });

        return Reactive.Entity(HList.From(new EditorViewTag(), state.Value));
    }
}
