using System.Runtime.CompilerServices;

namespace Sia;

public struct HList<THead>(in THead head) : IHList
{
    public THead Head = head;

    public readonly void Handle<THandler>(in THandler handler)
        where THandler : IHListHandler
        => handler.Handle(Head, EmptyHList.Default);

    public readonly void HandleHead<THandler>(in THandler handler)
        where THandler : IGenericHandler
        => handler.Handle(Head);

    public void HandleHeadRef<THandler>(ref THandler handler)
        where THandler : IRefGenericHandler
        => handler.Handle(ref Head);

    public readonly void HandleTail<THandler>(in THandler handler)
        where THandler : IGenericHandler<IHList>
        => handler.Handle(EmptyHList.Default);

    public readonly void HandleTailRef<THandler>(ref THandler handler)
        where THandler : IRefGenericHandler<IHList>
    {
        EmptyHList tail = default;
        handler.Handle(ref tail);
    }

    public readonly void Filter<TPredicate, THandler>(in TPredicate predicate, in THandler handler)
        where TPredicate : IGenericPredicate
        where THandler : IGenericStructHandler<IHList>
    {
        if (predicate.Predicate(Head)) {
            handler.Handle(this);
            return;
        }
        handler.Handle(EmptyHList.Default);
    }

    public readonly void Concat<THList, THandler>(in THList list, in THandler handler)
        where THList : struct, IHList
        where THandler : IGenericStructHandler<IHList>
    {
        if (typeof(THList) == typeof(EmptyHList)) {
            handler.Handle(this);
            return;
        }
        handler.Handle(HList.Cons(Head, list));
    }

    public readonly bool Remove<TValue, THandler>(TypeProxy<TValue> proxy, in THandler handler)
        where THandler : IGenericStructHandler<IHList>
    {
        if (typeof(THead) != typeof(TValue)) return false;
        handler.Handle(EmptyHList.Default);
        return true;
    }

    public bool Remove<TValue, THandler>(in TValue value, in THandler handler)
        where TValue : IEquatable<TValue>
        where THandler : IGenericStructHandler<IHList>
    {
        if (typeof(THead) != typeof(TValue)) return false;

        ref var head = ref Unsafe.As<THead, TValue>(ref Head);
        if (!head.Equals(value)) return false;

        handler.Handle(EmptyHList.Default);
        return true;
    }

    public readonly override string ToString()
        => "Cons(" + (Head?.ToString() ?? (typeof(THead) + ":null")) + ", Empty)";

    public static void HandleTypes<THandler>(in THandler handler)
        where THandler : IGenericTypeHandler
        => handler.Handle<THead>();
}
