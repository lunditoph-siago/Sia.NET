using Sia.Reactive;

namespace Sia_Examples.Editor;

[ReactiveComponent]
public static partial class EditorLinesComponent
{
    public static ReactiveNode Render(in EditorLinesProps props, ref Hooks hooks)
        => Reactive.ForEach(RenderLine, BuildLines(props));

    private static (int Key, EditorLineItem Value)[] BuildLines(
        scoped in EditorLinesProps props)
    {
        var items = new (int Key, EditorLineItem Value)[props.Document.Lines];
        for (var lineNumber = 1; lineNumber <= props.Document.Lines; lineNumber++) {
            var line = props.Document.Line(lineNumber);
            var marks = props.Decorations
                .Between(line.From, line.To)
                .Where(static range => range.Value.Kind == DecorationKind.Mark)
                .ToArray();
            var styledRuns = marks.Length == 0
                ? null
                : LineDecorator.Segment(line.Text, line.From, marks);
            var lineView = new EditorLineView(
                props.Identities.Values[lineNumber - 1],
                lineNumber - 1,
                line.Text,
                styledRuns,
                DecorationSet.LineClass(props.Decorations, line.From, line.To));
            items[lineNumber - 1] = (
                lineView.Identity,
                new(props.View, lineView));
        }
        return items;
    }

    private static ReactiveNode<EffectTerm<RenderEffect<EditorLineView>>> RenderLine(
        scoped in EditorLineItem item)
        => new(Term.Effect(new RenderEffect<EditorLineView>(item.View, item.Value)));

    private readonly record struct EditorLineItem(
        IEditorView View,
        EditorLineView Value);
}
