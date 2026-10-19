using Sia.Reactive;

namespace Sia.Examples.Editor;

[ReactiveComponent]
public static partial class EditorLinesComponent
{
    public static ReactiveNode Render(in EditorLinesProps props, ref Hooks hooks)
    {
        var cache = hooks.UseRef(static () => new EditorLinesCache()).Value;
        return new ReactiveNode<EffectTerm<RenderEffect<EditorLinesView>>>(
            Term.Effect(new RenderEffect<EditorLinesView>(props.View, cache.Update(props))));
    }
}
