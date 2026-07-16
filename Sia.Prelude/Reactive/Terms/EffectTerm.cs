namespace Sia.Reactive;

public interface IEffect<TSelf>
    where TSelf : struct, IEffect<TSelf>
{
    static abstract void Mount(in TSelf self);
    static abstract void Reconcile(in TSelf previous, in TSelf next);
    static abstract void Unmount(in TSelf self);
}

public interface IEffectCleanup
{
    void Unmount();
}

public readonly record struct EffectNode(IEffectCleanup Cleanup);

public sealed class EffectState<TEffect>(TEffect effect) : IEffectCleanup
    where TEffect : struct, IEffect<TEffect>
{
    private TEffect _current = effect;
    private bool _mounted;

    public void Mount()
    {
        TEffect.Mount(_current);
        _mounted = true;
    }

    public void Reconcile(in TEffect next)
    {
        if (!_mounted) {
            throw new InvalidOperationException("Cannot reconcile an unmounted effect.");
        }
        TEffect.Reconcile(_current, next);
        _current = next;
    }

    public void Unmount()
    {
        if (!_mounted) {
            return;
        }
        _mounted = false;
        TEffect.Unmount(_current);
    }
}

public readonly record struct EffectTerm<TEffect>(TEffect Effect)
    : ITerm<EffectTerm<TEffect>>
    where TEffect : struct, IEffect<TEffect>
{
    public static int SlotCount => 1;

    public static void Mount(in EffectTerm<TEffect> self, ref GraphContext context)
    {
        var state = new EffectState<TEffect>(self.Effect);
        var reconciler = context.Reconciler;
        var node = reconciler.CreateNode(new EffectNode(state));
        try {
            state.Mount();
            context.SetSlot(node);
        }
        catch (Exception error) {
            Outcome<Exception>.Failure(error)
                .Attempt(() => reconciler.DestroySlot(node))
                .ThrowFailure();
        }
    }

    public static void Reconcile(
        in EffectTerm<TEffect> previous,
        in EffectTerm<TEffect> next,
        ref GraphContext context)
    {
        var node = context.PeekSlot();
        if (node is not { IsValid: true }) {
            Mount(next, ref context);
            return;
        }
        var state = (EffectState<TEffect>)node.Get<EffectNode>().Cleanup;
        state.Reconcile(next.Effect);
        context.Advance();
    }
}
