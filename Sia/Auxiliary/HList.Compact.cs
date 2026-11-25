using System.Runtime.CompilerServices;

namespace Sia;

/// <summary>A final list cell with storage for its component and no stored empty tail.</summary>
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
        // The tail is a logical terminator. All component references come from Head.
        var tail = EmptyHList.Default;
        handler.Handle(ref tail);
    }

    public readonly void Filter<TPredicate, THandler>(in TPredicate predicate, in THandler handler)
        where TPredicate : IGenericPredicate
        where THandler : IGenericStructHandler<IHList>
    {
        if (predicate.Predicate(Head)) {
            handler.Handle(this);
        }
        else {
            handler.Handle(EmptyHList.Default);
        }
    }

    public readonly void Concat<THList, THandler>(in THList list, in THandler handler)
        where THList : struct, IHList
        where THandler : IGenericStructHandler<IHList>
    {
        if (typeof(THList) == typeof(EmptyHList)) {
            handler.Handle(this);
        }
        else {
            handler.Handle(HList.Cons(Head, list));
        }
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
        if (typeof(THead) != typeof(TValue) || !Unsafe.As<THead, TValue>(ref Head).Equals(value)) return false;
        handler.Handle(EmptyHList.Default);
        return true;
    }

    public readonly override string ToString()
        => "Cons(" + (Head?.ToString() ?? (typeof(THead) + ":null")) + ", Empty)";

    public static void HandleTypes<THandler>(in THandler handler)
        where THandler : IGenericTypeHandler
        => handler.Handle<THead>();
}

public static partial class HList
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static HList<THead> Compact<THead>(in THead head) => new(head);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static HList<T1, HList<T2>> Compact<T1, T2>(in T1 item1, in T2 item2)
        => Cons(item1, Compact(item2));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static HList<T1, HList<T2, HList<T3>>> Compact<T1, T2, T3>(in T1 item1, in T2 item2, in T3 item3)
        => Cons(item1, Cons(item2, Compact(item3)));
}
