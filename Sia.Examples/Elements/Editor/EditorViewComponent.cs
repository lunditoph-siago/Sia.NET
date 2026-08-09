using Sia.Reactive;

namespace Sia_Examples.Editor;

[ReactiveComponent]
public static partial class EditorViewComponent
{
    public static ReactiveNode Render(in EditorViewProps props, ref Hooks hooks)
    {
        var state = hooks.UseState(props.InitialState);
        var selection = state.Value.Selection.Main;
        var anchorLine = state.Value.Doc.LineAt(selection.Anchor);
        var headLine = state.Value.Doc.LineAt(selection.Head);

        return Reactive.Group(
            Reactive.Component<EditorLinesProps>(
                EditorLinesComponent.Render,
                new EditorLinesProps(
                    props.View,
                    state.Value.Doc,
                    state.Value.Decorations,
                    state.Value.LineIdentities,
                    state.Value.LineUpdate)),
            RenderActiveLine(new(
                props.View,
                new(state.Value.LineIdentities.Values[headLine.Number - 1]))),
            RenderSelection(new(
                props.View,
                new(
                    anchorLine.Number - 1,
                    selection.Anchor - anchorLine.From,
                    headLine.Number - 1,
                    selection.Head - headLine.From))),
            RenderStatus(new(
                props.View,
                new(
                    $"{state.Value.Doc.Lines}L  {state.Value.Doc.Length}B",
                    $"{headLine.Number,3}:{selection.Head - headLine.From + 1,-3}"))));
    }

    private static ReactiveNode<EffectTerm<RenderEffect<EditorActiveLineView>>>
        RenderActiveLine(scoped in EditorActiveLineItem item)
        => new(Term.Effect(new RenderEffect<EditorActiveLineView>(item.View, item.Value)));

    private static ReactiveNode<EffectTerm<RenderEffect<EditorSelectionView>>> RenderSelection(
        scoped in EditorSelectionItem item)
        => new(Term.Effect(new RenderEffect<EditorSelectionView>(item.View, item.Value)));

    private static ReactiveNode<EffectTerm<RenderEffect<EditorStatusView>>> RenderStatus(
        scoped in EditorStatusItem item)
        => new(Term.Effect(new RenderEffect<EditorStatusView>(item.View, item.Value)));

    private readonly record struct EditorActiveLineItem(
        IEditorView View,
        EditorActiveLineView Value);

    private readonly record struct EditorSelectionItem(IEditorView View, EditorSelectionView Value);

    private readonly record struct EditorStatusItem(IEditorView View, EditorStatusView Value);
}
