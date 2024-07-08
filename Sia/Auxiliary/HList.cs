namespace Sia;

using System.Runtime.CompilerServices;

public struct TypeProxy<T>
{
    public static TypeProxy<T> _ => default;
}

public interface IHListHandler
{
    void Handle<THead, TTail>(in THead head, in TTail tail)
        where TTail : IHList;
}

public interface IHList
{
    void Handle<THandler>(in THandler handler)
        where THandler : IHListHandler;

    void HandleHead<THandler>(in THandler handler)
        where THandler : IGenericHandler;

    void HandleHeadRef<THandler>(ref THandler handler)
        where THandler : IRefGenericHandler;

    void HandleTail<THandler>(in THandler handler)
        where THandler : IGenericHandler<IHList>;

    void HandleTailRef<THandler>(ref THandler handler)
        where THandler : IRefGenericHandler<IHList>;
    
    void Filter<TPredicate, THandler>(in TPredicate predicate, in THandler handler)
        where TPredicate : IGenericPredicate
        where THandler : IGenericHandler<IHList>;
    
    void Concat<THList, THandler>(in THList list, in THandler handler)
        where THList : IHList
        where THandler : IGenericHandler<IHList>;

    bool Remove<TValue, THandler>(TypeProxy<TValue> proxy, in THandler handler)
        where THandler : IGenericHandler<IHList>;

    bool Remove<TValue, THandler>(in TValue value, in THandler handler)
        where TValue : IEquatable<TValue>
        where THandler : IGenericHandler<IHList>;
    
    virtual static void HandleTypes<THandler>(in THandler handler)
        where THandler : IGenericTypeHandler
        => throw new NotImplementedException();
}

public struct EmptyHList : IHList
{
    public static EmptyHList Default => default;

    public readonly void Handle<THandler>(in THandler handler)
        where THandler : IHListHandler {}

    public readonly void HandleHead<THandler>(in THandler handler)
        where THandler : IGenericHandler {}

    public readonly void HandleHeadRef<THandler>(ref THandler handler)
        where THandler : IRefGenericHandler {}

    public readonly void HandleTail<THandler>(in THandler handler)
        where THandler : IGenericHandler<IHList> {}

    public readonly void HandleTailRef<THandler>(ref THandler handler)
        where THandler : IRefGenericHandler<IHList> {}

    public readonly void Filter<TPredicate, THandler>(in TPredicate predicate, in THandler handler)
        where TPredicate : IGenericPredicate
        where THandler : IGenericHandler<IHList>
        => handler.Handle(this);

    public readonly void Concat<THList, THandler>(in THList list, in THandler handler)
        where THList : IHList
        where THandler : IGenericHandler<IHList>
        => handler.Handle(list);

    public readonly bool Remove<TValue, THandler>(
        TypeProxy<TValue> proxy, in THandler handler)
        where THandler : IGenericHandler<IHList>
        => false;

    public readonly bool Remove<TValue, THandler>(in TValue value, in THandler handler)
        where TValue : IEquatable<TValue>
        where THandler : IGenericHandler<IHList>
        => false;

    public override readonly string ToString() => "Empty";

    public static void HandleTypes<THandler>(in THandler handler)
        where THandler : IGenericTypeHandler
    {}
}

public struct HList<THead, TTail>(in THead head, in TTail tail) : IHList
    where TTail : IHList
{
    public THead Head = head;
    public TTail Tail = tail;

    public readonly void Handle<THandler>(in THandler handler)
        where THandler : IHListHandler
        => handler.Handle(Head, Tail);

    public readonly void HandleHead<THandler>(in THandler handler)
        where THandler : IGenericHandler
        => handler.Handle(Head);

    public void HandleHeadRef<THandler>(ref THandler handler)
        where THandler : IRefGenericHandler
        => handler.Handle(ref Head);

    public readonly void HandleTail<THandler>(in THandler handler)
        where THandler : IGenericHandler<IHList>
        => handler.Handle(Tail);

    public void HandleTailRef<THandler>(ref THandler handler)
        where THandler : IRefGenericHandler<IHList>
        => handler.Handle(ref Tail);

    private struct TailConcater<THandler>(in THead head, in THandler handler)
        : IGenericHandler<IHList>
        where THandler : IGenericHandler<IHList>
    {
        public THead Head = head;
        public THandler Handler = handler;

        public void Handle<T>(in T value) where T : IHList
            => Handler.Handle(new HList<THead, T>(Head, value));
    }

    public readonly void Filter<TPredicate, THandler>(in TPredicate predicate, in THandler handler)
        where TPredicate : IGenericPredicate
        where THandler : IGenericHandler<IHList>
    {
        if (predicate.Predicate(Head)) {
            Tail.Filter(predicate, new TailConcater<THandler>(Head, handler));
        }
        else {
            Tail.Filter(predicate, handler);
        }
    }

    public readonly void Concat<THList, THandler>(in THList list, in THandler handler)
        where THList : IHList
        where THandler : IGenericHandler<IHList>
        => Tail.Concat(list, new TailConcater<THandler>(Head, handler));

    public readonly bool Remove<TValue, THandler>(TypeProxy<TValue> proxy, in THandler handler)
        where THandler : IGenericHandler<IHList>
    {
        if (typeof(THead) == typeof(TValue)) {
            handler.Handle(Tail);
            return true;
        }
        return Tail.Remove(proxy, new TailConcater<THandler>(Head, handler));
    }

    public bool Remove<TValue, THandler>(in TValue value, in THandler handler)
        where TValue : IEquatable<TValue>
        where THandler : IGenericHandler<IHList>
    {
        if (typeof(THead) == typeof(TValue)) {
            ref TValue casted = ref Unsafe.As<THead, TValue>(ref Head);
            if (casted.Equals(value)) {
                handler.Handle(Tail);
                return true;
            }
        }
        return Tail.Remove(value, new TailConcater<THandler>(Head, handler));
    }

    public readonly override string ToString()
        => "Cons(" + (Head?.ToString() ?? (typeof(THead) + ":null")) + ", " + Tail + ')';

    public static void HandleTypes<THandler>(in THandler handler)
        where THandler : IGenericTypeHandler
    {
        handler.Handle<THead>();
        TTail.HandleTypes(handler);
    }
}

public static class HList
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static HList<THead, EmptyHList> Create<THead>(in THead head) =>
        new(head, EmptyHList.Default);
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static HList<THead, TTail> Cons<THead, TTail>(in THead head, in TTail tail)
        where TTail : IHList
        => new(head, tail);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static HList<T1, HList<T2, EmptyHList>>
        From<T1, T2>(in T1 item1, in T2 item2)
        => Cons(item1, Create(item2));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static HList<T1, HList<T2, HList<T3, EmptyHList>>>
        Create<T1, T2, T3>(in T1 item1, in T2 item2, in T3 item3)
        => Cons(item1, Cons(item2, Create(item3)));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static HList<T1, HList<T2, HList<T3, HList<T4, EmptyHList>>>>
        Create<T1, T2, T3, T4>(in T1 item1, in T2 item2, in T3 item3, in T4 item4)
        => Cons(item1, Cons(item2, Cons(item3, Create(item4))));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static HList<T1, HList<T2, HList<T3, HList<T4, HList<T5, EmptyHList>>>>>
        Create<T1, T2, T3, T4, T5>(in T1 item1, in T2 item2, in T3 item3, in T4 item4, in T5 item5)
        => Cons(item1, Cons(item2, Cons(item3, Cons(item4, Create(item5)))));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static HList<T1, HList<T2, HList<T3, HList<T4, HList<T5, HList<T6, EmptyHList>>>>>>
        Create<T1, T2, T3, T4, T5, T6>(in T1 item1, in T2 item2, in T3 item3, in T4 item4, in T5 item5, in T6 item6)
        => Cons(item1, Cons(item2, Cons(item3, Cons(item4, Cons(item5, Create(item6))))));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static HList<T1, HList<T2, HList<T3, HList<T4, HList<T5, HList<T6, HList<T7, EmptyHList>>>>>>>
        Create<T1, T2, T3, T4, T5, T6, T7>(in T1 item1, in T2 item2, in T3 item3, in T4 item4, in T5 item5, in T6 item6, in T7 item7)
        => Cons(item1, Cons(item2, Cons(item3, Cons(item4, Cons(item5, Cons(item6, Create(item7)))))));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static HList<T1, HList<T2, HList<T3, HList<T4, HList<T5, HList<T6, HList<T7, HList<T8, EmptyHList>>>>>>>>
        Create<T1, T2, T3, T4, T5, T6, T7, T8>(in T1 item1, in T2 item2, in T3 item3, in T4 item4, in T5 item5, in T6 item6, in T7 item7, in T8 item8)
        => Cons(item1, Cons(item2, Cons(item3, Cons(item4, Cons(item5, Cons(item6, Cons(item7, Create(item8))))))));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static HList<T1, HList<T2, HList<T3, HList<T4, HList<T5, HList<T6, HList<T7, HList<T8, HList<T9, EmptyHList>>>>>>>>>
        Create<T1, T2, T3, T4, T5, T6, T7, T8, T9>(in T1 item1, in T2 item2, in T3 item3, in T4 item4, in T5 item5, in T6 item6, in T7 item7, in T8 item8, in T9 item9)
        => Cons(item1, Cons(item2, Cons(item3, Cons(item4, Cons(item5, Cons(item6, Cons(item7, Cons(item8, Create(item9)))))))));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static HList<T1, HList<T2, HList<T3, HList<T4, HList<T5, HList<T6, HList<T7, HList<T8, HList<T9, HList<T10, EmptyHList>>>>>>>>>>
        Create<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>(in T1 item1, in T2 item2, in T3 item3, in T4 item4, in T5 item5, in T6 item6, in T7 item7, in T8 item8, in T9 item9, in T10 item10)
        => Cons(item1, Cons(item2, Cons(item3, Cons(item4, Cons(item5, Cons(item6, Cons(item7, Cons(item8, Cons(item9, Create(item10))))))))));
}