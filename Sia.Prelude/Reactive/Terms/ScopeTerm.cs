namespace Sia.Reactive;

public sealed class ContextScope(Type contextType, Entity providerSlot, ContextScope? parent)
{
    public readonly Type ContextType = contextType;
    public readonly Entity ProviderSlot = providerSlot;
    public readonly ContextScope? Parent = parent;
    internal readonly Dictionary<long, CellSlot> Consumers = [];
}

public struct ContextNode<TCtx>(TCtx value)
{
    public TCtx Value = value;
    public ContextScope Scope = null!;
}

public readonly record struct ScopeTerm<TCtx, TChildren>(TCtx Value, TChildren Children)
    : ITerm<ScopeTerm<TCtx, TChildren>>
    where TCtx : struct
    where TChildren : struct, ITerm<TChildren>
{
    public static int SlotCount => 1 + TChildren.SlotCount;

    public static void Mount(in ScopeTerm<TCtx, TChildren> self, ref GraphContext ctx)
    {
        var reconciler = ctx.Reconciler;
        var slot = reconciler.CreateNode(new ContextNode<TCtx>(self.Value));
        ContextScope scope;
        try {
            scope = new ContextScope(typeof(TCtx), slot, ctx.Scope);
            slot.Get<ContextNode<TCtx>>().Scope = scope;
            ctx.SetSlot(slot);
        }
        catch (Exception error) {
            scope = Outcome<Exception>.Failure(error)
                .Attempt(() => reconciler.DestroySlot(slot))
                .ThrowFailure<ContextScope>();
        }

        var saved = ctx.Scope;
        ctx.Scope = scope;
        TChildren.Mount(self.Children, ref ctx);
        ctx.Scope = saved;
    }

    public static void Reconcile(
        in ScopeTerm<TCtx, TChildren> prev, in ScopeTerm<TCtx, TChildren> next,
        ref GraphContext ctx)
    {
        var slot = ctx.PeekSlot();
        if (slot is not { IsValid: true }) {
            var start = ctx.NextSlotIndex;
            ctx.DestroyRange(SlotCount);
            ctx.RewindTo(start);
            Mount(next, ref ctx);
            return;
        }
        ctx.Advance();

        ContextScope scope;
        {
            ref var node = ref slot.Get<ContextNode<TCtx>>();
            scope = node.Scope;
            if (!EqualityComparer<TCtx>.Default.Equals(node.Value, next.Value)) {
                node.Value = next.Value;
                foreach (var (identity, consumer) in scope.Consumers.ToArray()) {
                    if (ctx.Reconciler.Validate(consumer) is { } cell) {
                        ctx.Reconciler.EnqueueDirty(cell);
                    }
                    else {
                        scope.Consumers.Remove(identity);
                    }
                }
            }
        }

        var saved = ctx.Scope;
        ctx.Scope = scope;
        TChildren.Reconcile(prev.Children, next.Children, ref ctx);
        ctx.Scope = saved;
    }
}
