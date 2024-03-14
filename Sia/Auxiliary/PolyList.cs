using System.Runtime.CompilerServices;

namespace Sia;

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

public interface IPolyList
{
    public void HandleHead<THandler>(in THandler handler)
        where THandler : IGenericHandler;
    public void HandleTail<THandler>(in THandler handler)
        where THandler : IGenericHandler<IPolyList>;
    
    public void Concat<TPolyList, TResultHandler>(in TPolyList list, in TResultHandler handler)
        where TPolyList : IPolyList
        where TResultHandler : IGenericHandler<IPolyList>;

    public bool Remove<TValue, TResultHandler>(TypeProxy<TValue> proxy, in TResultHandler handler)
        where TResultHandler : IGenericHandler<IPolyList>;

    public bool Remove<TValue, TResultHandler>(in TValue value, in TResultHandler handler)
        where TValue : IEquatable<TValue>
        where TResultHandler : IGenericHandler<IPolyList>;
}

public struct EmptyPolyList : IPolyList
{
    public static EmptyPolyList Default => default;

    public readonly void HandleHead<THandler>(in THandler handler)
        where THandler : IGenericHandler {}

    public readonly void HandleTail<THandler>(in THandler handler)
        where THandler : IGenericHandler<IPolyList> {}

    public readonly void Concat<TPolyList, TResultHandler>(in TPolyList list, in TResultHandler handler)
        where TPolyList : IPolyList
        where TResultHandler : IGenericHandler<IPolyList>
        => handler.Handle(list);

    public readonly bool Remove<TValue, TResultHandler>(
        TypeProxy<TValue> proxy, in TResultHandler handler)
        where TResultHandler : IGenericHandler<IPolyList>
        => false;

    public readonly bool Remove<TValue, TResultHandler>(in TValue value, in TResultHandler handler)
        where TValue : IEquatable<TValue>
        where TResultHandler : IGenericHandler<IPolyList>
        => false;

    public override readonly string ToString() => "Empty";
}

public struct PolyList<THead, TTail>(in THead head, in TTail tail) : IPolyList
    where TTail : IPolyList
{
    public THead Head = head;
    public TTail Tail = tail;

    public readonly void HandleHead<THandler>(in THandler handler)
        where THandler : IGenericHandler
        => handler.Handle(Head);

    public readonly void HandleTail<THandler>(in THandler handler)
        where THandler : IGenericHandler<IPolyList>
        => handler.Handle(Tail);

    private struct TailConcater<TResultHandler>(in THead head, in TResultHandler handler)
        : IGenericHandler<IPolyList>
        where TResultHandler : IGenericHandler<IPolyList>
    {
        public THead Head = head;
        public TResultHandler Handler = handler;

        public void Handle<T>(in T value) where T : IPolyList
            => Handler.Handle(new PolyList<THead, T>(Head, value));
    }

    public readonly void Concat<TPolyList, TResultHandler>(in TPolyList list, in TResultHandler handler)
        where TPolyList : IPolyList
        where TResultHandler : IGenericHandler<IPolyList>
        => Tail.Concat(list, new TailConcater<TResultHandler>(Head, handler));

    public readonly bool Remove<TValue, TResultHandler>(TypeProxy<TValue> proxy, in TResultHandler handler)
        where TResultHandler : IGenericHandler<IPolyList>
    {
        if (typeof(THead) == typeof(TValue)) {
            handler.Handle(Tail);
            return true;
        }
        return Tail.Remove(proxy, new TailConcater<TResultHandler>(Head, handler));
    }

    public bool Remove<TValue, TResultHandler>(in TValue value, in TResultHandler handler)
        where TValue : IEquatable<TValue>
        where TResultHandler : IGenericHandler<IPolyList>
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

public static class PolyList
{
    public static PolyList<THead, EmptyPolyList> Create<THead>(in THead head)
        => new(head, EmptyPolyList.Default);
    
    public static PolyList<THead, TTail> Cons<THead, TTail>(in THead head, in TTail tail)
        where TTail : IPolyList
        => new(head, tail);
}