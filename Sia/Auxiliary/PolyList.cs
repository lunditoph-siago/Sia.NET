namespace Sia;

using System.Runtime.CompilerServices;

public struct TypeProxy<T>
{
    public static TypeProxy<T> Default => default;
}

public interface IGenericHandler
{
    public void Handle<T>(in T value);
}

public interface IGenericHandler<TBase>
{
    public void Handle<T>(in T value) where T : TBase;
}

public interface IHList
{
    public void HandleHead<THandler>(in THandler handler)
        where THandler : IGenericHandler;

    public void HandleTail<THandler>(in THandler handler)
        where THandler : IGenericHandler<IHList>;
    
    public void Concat<THList, TResultHandler>(in THList list, in TResultHandler handler)
        where THList : IHList
        where TResultHandler : IGenericHandler<IHList>;

    public bool Remove<TValue, TResultHandler>(TypeProxy<TValue> proxy, in TResultHandler handler)
        where TResultHandler : IGenericHandler<IHList>;

    public bool Remove<TValue, TResultHandler>(in TValue value, in TResultHandler handler)
        where TValue : IEquatable<TValue>
        where TResultHandler : IGenericHandler<IHList>;
}

public struct EmptyHList : IHList
{
    public static EmptyHList Default => default;

    public readonly void HandleHead<THandler>(in THandler handler)
        where THandler : IGenericHandler {}

    public readonly void HandleTail<THandler>(in THandler handler)
        where THandler : IGenericHandler<IHList> {}

    public readonly void Concat<THList, TResultHandler>(in THList list, in TResultHandler handler)
        where THList : IHList
        where TResultHandler : IGenericHandler<IHList>
        => handler.Handle(list);

    public readonly bool Remove<TValue, TResultHandler>(
        TypeProxy<TValue> proxy, in TResultHandler handler)
        where TResultHandler : IGenericHandler<IHList>
        => false;

    public readonly bool Remove<TValue, TResultHandler>(in TValue value, in TResultHandler handler)
        where TValue : IEquatable<TValue>
        where TResultHandler : IGenericHandler<IHList>
        => false;

    public override readonly string ToString() => "Empty";
}

public struct HList<THead, TTail>(in THead head, in TTail tail) : IHList
    where TTail : IHList
{
    public THead Head = head;
    public TTail Tail = tail;

    public readonly void HandleHead<THandler>(in THandler handler)
        where THandler : IGenericHandler
        => handler.Handle(Head);

    public readonly void HandleTail<THandler>(in THandler handler)
        where THandler : IGenericHandler<IHList>
        => handler.Handle(Tail);

    private struct TailConcater<TResultHandler>(in THead head, in TResultHandler handler)
        : IGenericHandler<IHList>
        where TResultHandler : IGenericHandler<IHList>
    {
        public THead Head = head;
        public TResultHandler Handler = handler;

        public void Handle<T>(in T value) where T : IHList
            => Handler.Handle(new HList<THead, T>(Head, value));
    }

    public readonly void Concat<THList, TResultHandler>(in THList list, in TResultHandler handler)
        where THList : IHList
        where TResultHandler : IGenericHandler<IHList>
        => Tail.Concat(list, new TailConcater<TResultHandler>(Head, handler));

    public readonly bool Remove<TValue, TResultHandler>(TypeProxy<TValue> proxy, in TResultHandler handler)
        where TResultHandler : IGenericHandler<IHList>
    {
        if (typeof(THead) == typeof(TValue)) {
            handler.Handle(Tail);
            return true;
        }
        return Tail.Remove(proxy, new TailConcater<TResultHandler>(Head, handler));
    }

    public bool Remove<TValue, TResultHandler>(in TValue value, in TResultHandler handler)
        where TValue : IEquatable<TValue>
        where TResultHandler : IGenericHandler<IHList>
    {
        if (typeof(THead) == typeof(TValue)) {
            ref TValue casted = ref Unsafe.As<THead, TValue>(ref Head);
            if (casted.Equals(value)) {
                handler.Handle(Tail);
                return true;
            }
        }
        return Tail.Remove(value, new TailConcater<TResultHandler>(Head, handler));
    }

    public readonly override string ToString()
        => "Cons(" + (Head?.ToString() ?? (typeof(THead) + ":null")) + ", " + Tail + ')';
}

public static class HList
{
    public static HList<THead, EmptyHList> Create<THead>(in THead head)
        => new(head, EmptyHList.Default);
    
    public static HList<THead, TTail> Cons<THead, TTail>(in THead head, in TTail tail)
        where TTail : IHList
        => new(head, tail);

    public static HList<T1, HList<T2, EmptyHList>>
        Create<T1, T2>(in T1 item1, in T2 item2)
        => Cons(item1, Create(item2));

    public static HList<T1, HList<T2, HList<T3, EmptyHList>>>
        Create<T1, T2, T3>(in T1 item1, in T2 item2, in T3 item3)
        => Cons(item1, Cons(item2, Create(item3)));

    public static HList<T1, HList<T2, HList<T3, HList<T4, EmptyHList>>>>
        Create<T1, T2, T3, T4>(in T1 item1, in T2 item2, in T3 item3, in T4 item4)
        => Cons(item1, Cons(item2, Cons(item3, Create(item4))));

    public static HList<T1, HList<T2, HList<T3, HList<T4, HList<T5, EmptyHList>>>>>
        Create<T1, T2, T3, T4, T5>(in T1 item1, in T2 item2, in T3 item3, in T4 item4, in T5 item5)
        => Cons(item1, Cons(item2, Cons(item3, Cons(item4, Create(item5)))));

    public static HList<T1, HList<T2, HList<T3, HList<T4, HList<T5, HList<T6, EmptyHList>>>>>>
        Create<T1, T2, T3, T4, T5, T6>(in T1 item1, in T2 item2, in T3 item3, in T4 item4, in T5 item5, in T6 item6)
        => Cons(item1, Cons(item2, Cons(item3, Cons(item4, Cons(item5, Create(item6))))));

    public static HList<T1, HList<T2, HList<T3, HList<T4, HList<T5, HList<T6, HList<T7, EmptyHList>>>>>>>
        Create<T1, T2, T3, T4, T5, T6, T7>(in T1 item1, in T2 item2, in T3 item3, in T4 item4, in T5 item5, in T6 item6, in T7 item7)
        => Cons(item1, Cons(item2, Cons(item3, Cons(item4, Cons(item5, Cons(item6, Create(item7)))))));

    public static HList<T1, HList<T2, HList<T3, HList<T4, HList<T5, HList<T6, HList<T7, HList<T8, EmptyHList>>>>>>>>
        Create<T1, T2, T3, T4, T5, T6, T7, T8>(in T1 item1, in T2 item2, in T3 item3, in T4 item4, in T5 item5, in T6 item6, in T7 item7, in T8 item8)
        => Cons(item1, Cons(item2, Cons(item3, Cons(item4, Cons(item5, Cons(item6, Cons(item7, Create(item8))))))));

    public static HList<T1, HList<T2, HList<T3, HList<T4, HList<T5, HList<T6, HList<T7, HList<T8, HList<T9, EmptyHList>>>>>>>>>
        Create<T1, T2, T3, T4, T5, T6, T7, T8, T9>(in T1 item1, in T2 item2, in T3 item3, in T4 item4, in T5 item5, in T6 item6, in T7 item7, in T8 item8, in T9 item9)
        => Cons(item1, Cons(item2, Cons(item3, Cons(item4, Cons(item5, Cons(item6, Cons(item7, Cons(item8, Create(item9)))))))));

    public static HList<T1, HList<T2, HList<T3, HList<T4, HList<T5, HList<T6, HList<T7, HList<T8, HList<T9, HList<T10, EmptyHList>>>>>>>>>>
        Create<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>(in T1 item1, in T2 item2, in T3 item3, in T4 item4, in T5 item5, in T6 item6, in T7 item7, in T8 item8, in T9 item9, in T10 item10)
        => Cons(item1, Cons(item2, Cons(item3, Cons(item4, Cons(item5, Cons(item6, Cons(item7, Cons(item8, Cons(item9, Create(item10))))))))));
}