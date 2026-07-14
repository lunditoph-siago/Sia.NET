namespace Sia.Reactive;

using System.Runtime.CompilerServices;

public readonly record struct EntityTerm<TList, TChildren>(TList Components, TChildren Children)
    : ITerm<EntityTerm<TList, TChildren>>
    where TList : struct, IHList
    where TChildren : struct, ITerm<TChildren>
{
    public static int SlotCount => 1 + TChildren.SlotCount;

    public static void Mount(in EntityTerm<TList, TChildren> self, ref GraphContext ctx)
    {
        ctx.SetSlot(ctx.Reconciler.CreateOutput(self.Components));
        TChildren.Mount(self.Children, ref ctx);
    }

    public static void Reconcile(
        in EntityTerm<TList, TChildren> prev, in EntityTerm<TList, TChildren> next,
        ref GraphContext ctx)
    {
        var entity = ctx.PeekSlot();
        if (entity is { IsValid: true }) {
            TList.HandleTypes(new DiffHandler(prev.Components, next.Components, entity));
            ctx.Advance();
        }
        else {
            ctx.SetSlot(ctx.Reconciler.CreateOutput(next.Components));
        }
        TChildren.Reconcile(prev.Children, next.Children, ref ctx);
    }

    private readonly struct DiffHandler(in TList prev, in TList next, Entity target)
        : IGenericTypeHandler
    {
        private readonly TList _prev = prev;
        private readonly TList _next = next;
        private readonly Entity _target = target;

        public void Handle<T>()
        {
            var offset = EntityIndexer<TList, T>.Offset;
            ref var prevComp = ref Unsafe.As<byte, T>(ref Unsafe.AddByteOffset(
                ref Unsafe.As<TList, byte>(ref Unsafe.AsRef(in _prev)), offset));
            ref var nextComp = ref Unsafe.As<byte, T>(ref Unsafe.AddByteOffset(
                ref Unsafe.As<TList, byte>(ref Unsafe.AsRef(in _next)), offset));
            if (!EqualityComparer<T>.Default.Equals(prevComp, nextComp)) {
                _target.Set(nextComp);
            }
        }
    }
}
