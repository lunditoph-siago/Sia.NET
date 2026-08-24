using Sia.Reactive;

namespace Sia_Examples.Editor;

[ReactiveComponent]
public static partial class EditorLinesComponent
{
    public static ReactiveNode Render(in EditorLinesProps props, ref Hooks hooks)
    {
        var cache = hooks.UseRef(static () => new EditorLinesCache()).Value;
        var patch = cache.Update(props);
        return Reactive.PatchForEach(RenderLine, patch.Upserts, patch.Removals);
    }

    private static ReactiveNode<EffectTerm<RenderEffect<EditorLineView>>> RenderLine(
        scoped in EditorLineItem item)
        => new(Term.Effect(new RenderEffect<EditorLineView>(item.View, item.Value)));
}
