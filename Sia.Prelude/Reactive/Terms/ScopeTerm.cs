namespace Sia.Reactive;

public sealed class ContextScope(Type contextType, Entity providerSlot, ContextScope? parent)
{
    public readonly Type ContextType = contextType;
    public readonly Entity ProviderSlot = providerSlot;
    public readonly ContextScope? Parent = parent;
}

public struct ContextNode<TCtx>(TCtx value)
{
    public TCtx Value = value;
    public List<Entity> Consumers = [];
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
        var slot = ctx.Reconciler.CreateNode(new ContextNode<TCtx>(self.Value));
        var scope = new ContextScope(typeof(TCtx), slot, ctx.Scope);
        slot.Get<ContextNode<TCtx>>().Scope = scope;
        ctx.SetSlot(slot);

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
                var consumers = node.Consumers;
                for (var i = consumers.Count - 1; i >= 0; i--) {
                    var consumer = consumers[i];
                    if (consumer.IsValid) {
                        ctx.Reconciler.EnqueueDirty(consumer);
                    }
                    else {
                        consumers.RemoveAt(i);
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
