using System.Runtime.CompilerServices;

namespace Sia.Reactive;

public readonly record struct EntityTerm<TList, TChildren>(TList Components, TChildren Children)
    : ITerm<EntityTerm<TList, TChildren>>
    where TList : struct, IHList
    where TChildren : struct, ITerm<TChildren>
{
    public bool Equals(EntityTerm<TList, TChildren> other)
        => ComponentEquality<TList>.Equal(Components, other.Components)
            && EqualityComparer<TChildren>.Default.Equals(Children, other.Children);

    public override int GetHashCode()
        => unchecked(EqualityComparer<TList>.Default.GetHashCode(Components) * -1521134295
            + EqualityComparer<TChildren>.Default.GetHashCode(Children));

    private static bool FieldEquals<T>(in T left, in T right)
        => left is null ? right is null : left.Equals(right);

    private static class ComponentEquality<T> where T : IHList
    {
        private static readonly bool IsList = typeof(T).IsGenericType
            && typeof(T).GetGenericTypeDefinition() == typeof(HList<,>);

        public static unsafe bool Equal(in T left, in T right, bool nested = false)
        {
            if (typeof(T) == typeof(EmptyHList)) {
                return true;
            }
            if (!IsList) {
                return nested ? FieldEquals(left, right) : EqualityComparer<T>.Default.Equals(left, right);
            }
            var equal = false;
            left.Handle(new LeftHandler(right, &equal));
            return equal;
        }

        private readonly unsafe struct LeftHandler(in T right, bool* equal) : IHListHandler
        {
            private readonly T _right = right;
            private readonly bool* _equal = equal;

            public void Handle<THead, TTail>(in THead head, in TTail tail) where TTail : IHList
                => _right.Handle(new RightHandler<THead, TTail>(head, tail, _equal));
        }

        private readonly unsafe struct RightHandler<THead, TTail>(in THead head, in TTail tail, bool* equal)
            : IHListHandler where TTail : IHList
        {
            private readonly THead _head = head;
            private readonly TTail _tail = tail;
            private readonly bool* _equal = equal;

            public void Handle<TOtherHead, TOtherTail>(in TOtherHead head, in TOtherTail tail)
                where TOtherTail : IHList
                => *_equal = FieldEquals(_head, Unsafe.As<TOtherHead, THead>(ref Unsafe.AsRef(in head)))
                    && ComponentEquality<TTail>.Equal(_tail,
                        Unsafe.As<TOtherTail, TTail>(ref Unsafe.AsRef(in tail)), nested: true);
        }
    }

    public static int SlotCount => 1 + TChildren.SlotCount;

    public static void Mount(in EntityTerm<TList, TChildren> self, ref GraphContext ctx)
    {
        var output = ctx.Reconciler.CreateOutput(self.Components);
        ctx.SetSlot(output);
        var previousOutput = ctx.Output;
        ctx.Output = output;
        try {
            TChildren.Mount(self.Children, ref ctx);
        }
        finally {
            ctx.Output = previousOutput;
        }
    }

    public static void Reconcile(
        in EntityTerm<TList, TChildren> prev, in EntityTerm<TList, TChildren> next,
        ref GraphContext ctx)
    {
        var entity = ctx.PeekSlot();
        Entity output;
        if (entity is { IsValid: true } target) {
            TList.HandleTypes(new DiffHandler(prev.Components, next.Components, target));
            ctx.Advance();
            output = target;
        }
        else {
            output = ctx.Reconciler.CreateOutput(next.Components);
            ctx.SetSlot(output);
        }
        var previousOutput = ctx.Output;
        ctx.Output = output;
        try {
            TChildren.Reconcile(prev.Children, next.Children, ref ctx);
        }
        finally {
            ctx.Output = previousOutput;
        }
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
