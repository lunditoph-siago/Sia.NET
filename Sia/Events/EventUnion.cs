namespace Sia;

public class EventUnion<T1> : IEventUnion
    where T1 : IEvent, new()
{
    public static ITypeUnion StaticEventTypes { get; }
        = new TypeUnion<T1>();

    public ITypeUnion EventTypes => StaticEventTypes;

    public static void HandleEventTypes(IGenericTypeHandler<IEvent> handler)
    {
        handler.Handle<T1>();
    }
}

public class EventUnion<T1, T2> : IEventUnion
    where T1 : IEvent, new()
    where T2 : IEvent, new()
{
    public static ITypeUnion StaticEventTypes { get; }
        = new TypeUnion<T1, T2>();

    public ITypeUnion EventTypes => StaticEventTypes;

    public static void HandleEventTypes(IGenericTypeHandler<IEvent> handler)
    {
        handler.Handle<T1>();
        handler.Handle<T2>();
    }
}

public class EventUnion<T1, T2, T3> : IEventUnion
    where T1 : IEvent, new()
    where T2 : IEvent, new()
    where T3 : IEvent, new()
{
    public static ITypeUnion StaticEventTypes { get; }
        = new TypeUnion<T1, T2, T3>();

    public ITypeUnion EventTypes => StaticEventTypes;

    public static void HandleEventTypes(IGenericTypeHandler<IEvent> handler)
    {
        handler.Handle<T1>();
        handler.Handle<T2>();
        handler.Handle<T3>();
    }
}

public class EventUnion<T1, T2, T3, T4> : IEventUnion
    where T1 : IEvent, new()
    where T2 : IEvent, new()
    where T3 : IEvent, new()
    where T4 : IEvent, new()
{
    public static ITypeUnion StaticEventTypes { get; }
        = new TypeUnion<T1, T2, T3, T4>();

    public ITypeUnion EventTypes => StaticEventTypes;

    public static void HandleEventTypes(IGenericTypeHandler<IEvent> handler)
    {
        handler.Handle<T1>();
        handler.Handle<T2>();
        handler.Handle<T3>();
        handler.Handle<T4>();
    }
}

public class EventUnion<T1, T2, T3, T4, T5> : IEventUnion
    where T1 : IEvent, new()
    where T2 : IEvent, new()
    where T3 : IEvent, new()
    where T4 : IEvent, new()
    where T5 : IEvent, new()
{
    public static ITypeUnion StaticEventTypes { get; }
        = new TypeUnion<T1, T2, T3, T4, T5>();

    public ITypeUnion EventTypes => StaticEventTypes;

    public static void HandleEventTypes(IGenericTypeHandler<IEvent> handler)
    {
        handler.Handle<T1>();
        handler.Handle<T2>();
        handler.Handle<T3>();
        handler.Handle<T4>();
        handler.Handle<T5>();
    }
}

public class EventUnion<T1, T2, T3, T4, T5, T6> : IEventUnion
    where T1 : IEvent, new()
    where T2 : IEvent, new()
    where T3 : IEvent, new()
    where T4 : IEvent, new()
    where T5 : IEvent, new()
    where T6 : IEvent, new()
{
    public static ITypeUnion StaticEventTypes { get; }
        = new TypeUnion<T1, T2, T3, T4, T5, T6>();

    public ITypeUnion EventTypes => StaticEventTypes;

    public static void HandleEventTypes(IGenericTypeHandler<IEvent> handler)
    {
        handler.Handle<T1>();
        handler.Handle<T2>();
        handler.Handle<T3>();
        handler.Handle<T4>();
        handler.Handle<T5>();
        handler.Handle<T6>();
    }
}

public class EventUnion<T1, T2, T3, T4, T5, T6, T7> : IEventUnion
    where T1 : IEvent, new()
    where T2 : IEvent, new()
    where T3 : IEvent, new()
    where T4 : IEvent, new()
    where T5 : IEvent, new()
    where T6 : IEvent, new()
    where T7 : IEvent, new()
{
    public static ITypeUnion StaticEventTypes { get; }
        = new TypeUnion<T1, T2, T3, T4, T5, T6, T7>();

    public ITypeUnion EventTypes => StaticEventTypes;

    public static void HandleEventTypes(IGenericTypeHandler<IEvent> handler)
    {
        handler.Handle<T1>();
        handler.Handle<T2>();
        handler.Handle<T3>();
        handler.Handle<T4>();
        handler.Handle<T5>();
        handler.Handle<T6>();
        handler.Handle<T7>();
    }
}

public class EventUnion<T1, T2, T3, T4, T5, T6, T7, T8> : IEventUnion
    where T1 : IEvent, new()
    where T2 : IEvent, new()
    where T3 : IEvent, new()
    where T4 : IEvent, new()
    where T5 : IEvent, new()
    where T6 : IEvent, new()
    where T7 : IEvent, new()
    where T8 : IEvent, new()
{
    public static ITypeUnion StaticEventTypes { get; }
        = new TypeUnion<T1, T2, T3, T4, T5, T6, T7, T8>();

    public ITypeUnion EventTypes => StaticEventTypes;

    public static void HandleEventTypes(IGenericTypeHandler<IEvent> handler)
    {
        handler.Handle<T1>();
        handler.Handle<T2>();
        handler.Handle<T3>();
        handler.Handle<T4>();
        handler.Handle<T5>();
        handler.Handle<T6>();
        handler.Handle<T7>();
        handler.Handle<T8>();
    }
}

public class EventUnion<T1, T2, T3, T4, T5, T6, T7, T8, T9> : IEventUnion
    where T1 : IEvent, new()
    where T2 : IEvent, new()
    where T3 : IEvent, new()
    where T4 : IEvent, new()
    where T5 : IEvent, new()
    where T6 : IEvent, new()
    where T7 : IEvent, new()
    where T8 : IEvent, new()
    where T9 : IEvent, new()
{
    public static ITypeUnion StaticEventTypes { get; }
        = new TypeUnion<T1, T2, T3, T4, T5, T6, T7, T8, T9>();

    public ITypeUnion EventTypes => StaticEventTypes;

    public static void HandleEventTypes(IGenericTypeHandler<IEvent> handler)
    {
        handler.Handle<T1>();
        handler.Handle<T2>();
        handler.Handle<T3>();
        handler.Handle<T4>();
        handler.Handle<T5>();
        handler.Handle<T6>();
        handler.Handle<T7>();
        handler.Handle<T8>();
        handler.Handle<T9>();
    }
}

public class EventUnion<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10> : IEventUnion
    where T1 : IEvent, new()
    where T2 : IEvent, new()
    where T3 : IEvent, new()
    where T4 : IEvent, new()
    where T5 : IEvent, new()
    where T6 : IEvent, new()
    where T7 : IEvent, new()
    where T8 : IEvent, new()
    where T9 : IEvent, new()
    where T10 : IEvent, new()
{
    public static ITypeUnion StaticEventTypes { get; }
        = new TypeUnion<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>();

    public ITypeUnion EventTypes => StaticEventTypes;

    public static void HandleEventTypes(IGenericTypeHandler<IEvent> handler)
    {
        handler.Handle<T1>();
        handler.Handle<T2>();
        handler.Handle<T3>();
        handler.Handle<T4>();
        handler.Handle<T5>();
        handler.Handle<T6>();
        handler.Handle<T7>();
        handler.Handle<T8>();
        handler.Handle<T9>();
        handler.Handle<T10>();
    }
}

public class EventUnion<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11> : IEventUnion
    where T1 : IEvent, new()
    where T2 : IEvent, new()
    where T3 : IEvent, new()
    where T4 : IEvent, new()
    where T5 : IEvent, new()
    where T6 : IEvent, new()
    where T7 : IEvent, new()
    where T8 : IEvent, new()
    where T9 : IEvent, new()
    where T10 : IEvent, new()
    where T11 : IEvent, new()
{
    public static ITypeUnion StaticEventTypes { get; }
        = new TypeUnion<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11>();

    public ITypeUnion EventTypes => StaticEventTypes;

    public static void HandleEventTypes(IGenericTypeHandler<IEvent> handler)
    {
        handler.Handle<T1>();
        handler.Handle<T2>();
        handler.Handle<T3>();
        handler.Handle<T4>();
        handler.Handle<T5>();
        handler.Handle<T6>();
        handler.Handle<T7>();
        handler.Handle<T8>();
        handler.Handle<T9>();
        handler.Handle<T10>();
        handler.Handle<T11>();
    }
}

public class EventUnion<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12> : IEventUnion
    where T1 : IEvent, new()
    where T2 : IEvent, new()
    where T3 : IEvent, new()
    where T4 : IEvent, new()
    where T5 : IEvent, new()
    where T6 : IEvent, new()
    where T7 : IEvent, new()
    where T8 : IEvent, new()
    where T9 : IEvent, new()
    where T10 : IEvent, new()
    where T11 : IEvent, new()
    where T12 : IEvent, new()
{
    public static ITypeUnion StaticEventTypes { get; }
        = new TypeUnion<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>();

    public ITypeUnion EventTypes => StaticEventTypes;

    public static void HandleEventTypes(IGenericTypeHandler<IEvent> handler)
    {
        handler.Handle<T1>();
        handler.Handle<T2>();
        handler.Handle<T3>();
        handler.Handle<T4>();
        handler.Handle<T5>();
        handler.Handle<T6>();
        handler.Handle<T7>();
        handler.Handle<T8>();
        handler.Handle<T9>();
        handler.Handle<T10>();
        handler.Handle<T11>();
        handler.Handle<T12>();
    }
}

public class EventUnion<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13> : IEventUnion
    where T1 : IEvent, new()
    where T2 : IEvent, new()
    where T3 : IEvent, new()
    where T4 : IEvent, new()
    where T5 : IEvent, new()
    where T6 : IEvent, new()
    where T7 : IEvent, new()
    where T8 : IEvent, new()
    where T9 : IEvent, new()
    where T10 : IEvent, new()
    where T11 : IEvent, new()
    where T12 : IEvent, new()
    where T13 : IEvent, new()
{
    public static ITypeUnion StaticEventTypes { get; }
        = new TypeUnion<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13>();

    public ITypeUnion EventTypes => StaticEventTypes;

    public static void HandleEventTypes(IGenericTypeHandler<IEvent> handler)
    {
        handler.Handle<T1>();
        handler.Handle<T2>();
        handler.Handle<T3>();
        handler.Handle<T4>();
        handler.Handle<T5>();
        handler.Handle<T6>();
        handler.Handle<T7>();
        handler.Handle<T8>();
        handler.Handle<T9>();
        handler.Handle<T10>();
        handler.Handle<T11>();
        handler.Handle<T12>();
        handler.Handle<T13>();
    }
}

public class EventUnion<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14> : IEventUnion
    where T1 : IEvent, new()
    where T2 : IEvent, new()
    where T3 : IEvent, new()
    where T4 : IEvent, new()
    where T5 : IEvent, new()
    where T6 : IEvent, new()
    where T7 : IEvent, new()
    where T8 : IEvent, new()
    where T9 : IEvent, new()
    where T10 : IEvent, new()
    where T11 : IEvent, new()
    where T12 : IEvent, new()
    where T13 : IEvent, new()
    where T14 : IEvent, new()
{
    public static ITypeUnion StaticEventTypes { get; }
        = new TypeUnion<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14>();

    public ITypeUnion EventTypes => StaticEventTypes;

    public static void HandleEventTypes(IGenericTypeHandler<IEvent> handler)
    {
        handler.Handle<T1>();
        handler.Handle<T2>();
        handler.Handle<T3>();
        handler.Handle<T4>();
        handler.Handle<T5>();
        handler.Handle<T6>();
        handler.Handle<T7>();
        handler.Handle<T8>();
        handler.Handle<T9>();
        handler.Handle<T10>();
        handler.Handle<T11>();
        handler.Handle<T12>();
        handler.Handle<T13>();
        handler.Handle<T14>();
    }
}

public class EventUnion<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15> : IEventUnion
    where T1 : IEvent, new()
    where T2 : IEvent, new()
    where T3 : IEvent, new()
    where T4 : IEvent, new()
    where T5 : IEvent, new()
    where T6 : IEvent, new()
    where T7 : IEvent, new()
    where T8 : IEvent, new()
    where T9 : IEvent, new()
    where T10 : IEvent, new()
    where T11 : IEvent, new()
    where T12 : IEvent, new()
    where T13 : IEvent, new()
    where T14 : IEvent, new()
    where T15 : IEvent, new()
{
    public static ITypeUnion StaticEventTypes { get; }
        = new TypeUnion<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15>();

    public ITypeUnion EventTypes => StaticEventTypes;

    public static void HandleEventTypes(IGenericTypeHandler<IEvent> handler)
    {
        handler.Handle<T1>();
        handler.Handle<T2>();
        handler.Handle<T3>();
        handler.Handle<T4>();
        handler.Handle<T5>();
        handler.Handle<T6>();
        handler.Handle<T7>();
        handler.Handle<T8>();
        handler.Handle<T9>();
        handler.Handle<T10>();
        handler.Handle<T11>();
        handler.Handle<T12>();
        handler.Handle<T13>();
        handler.Handle<T14>();
        handler.Handle<T15>();
    }
}

public class EventUnion<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16> : IEventUnion
    where T1 : IEvent, new()
    where T2 : IEvent, new()
    where T3 : IEvent, new()
    where T4 : IEvent, new()
    where T5 : IEvent, new()
    where T6 : IEvent, new()
    where T7 : IEvent, new()
    where T8 : IEvent, new()
    where T9 : IEvent, new()
    where T10 : IEvent, new()
    where T11 : IEvent, new()
    where T12 : IEvent, new()
    where T13 : IEvent, new()
    where T14 : IEvent, new()
    where T15 : IEvent, new()
    where T16 : IEvent, new()
{
    public static ITypeUnion StaticEventTypes { get; }
        = new TypeUnion<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16>();

    public ITypeUnion EventTypes => StaticEventTypes;

    public static void HandleEventTypes(IGenericTypeHandler<IEvent> handler)
    {
        handler.Handle<T1>();
        handler.Handle<T2>();
        handler.Handle<T3>();
        handler.Handle<T4>();
        handler.Handle<T5>();
        handler.Handle<T6>();
        handler.Handle<T7>();
        handler.Handle<T8>();
        handler.Handle<T9>();
        handler.Handle<T10>();
        handler.Handle<T11>();
        handler.Handle<T12>();
        handler.Handle<T13>();
        handler.Handle<T14>();
        handler.Handle<T15>();
        handler.Handle<T16>();
    }
}

public static class EventUnion
{
    public static EventUnion<T1> Of<T1>()
        where T1 : IEvent, new()
        => new();

    public static EventUnion<T1, T2> Of<T1, T2>()
        where T1 : IEvent, new()
        where T2 : IEvent, new()
        => new();

    public static EventUnion<T1, T2, T3> Of<T1, T2, T3>()
        where T1 : IEvent, new()
        where T2 : IEvent, new()
        where T3 : IEvent, new()
        => new();

    public static EventUnion<T1, T2, T3, T4> Of<T1, T2, T3, T4>()
        where T1 : IEvent, new()
        where T2 : IEvent, new()
        where T3 : IEvent, new()
        where T4 : IEvent, new()
        => new();

    public static EventUnion<T1, T2, T3, T4, T5> Of<T1, T2, T3, T4, T5>()
        where T1 : IEvent, new()
        where T2 : IEvent, new()
        where T3 : IEvent, new()
        where T4 : IEvent, new()
        where T5 : IEvent, new()
        => new();

    public static EventUnion<T1, T2, T3, T4, T5, T6> Of<T1, T2, T3, T4, T5, T6>()
        where T1 : IEvent, new()
        where T2 : IEvent, new()
        where T3 : IEvent, new()
        where T4 : IEvent, new()
        where T5 : IEvent, new()
        where T6 : IEvent, new()
        => new();

    public static EventUnion<T1, T2, T3, T4, T5, T6, T7> Of<T1, T2, T3, T4, T5, T6, T7>()
        where T1 : IEvent, new()
        where T2 : IEvent, new()
        where T3 : IEvent, new()
        where T4 : IEvent, new()
        where T5 : IEvent, new()
        where T6 : IEvent, new()
        where T7 : IEvent, new()
        => new();

    public static EventUnion<T1, T2, T3, T4, T5, T6, T7, T8> Of<T1, T2, T3, T4, T5, T6, T7, T8>()
        where T1 : IEvent, new()
        where T2 : IEvent, new()
        where T3 : IEvent, new()
        where T4 : IEvent, new()
        where T5 : IEvent, new()
        where T6 : IEvent, new()
        where T7 : IEvent, new()
        where T8 : IEvent, new()
        => new();

    public static EventUnion<T1, T2, T3, T4, T5, T6, T7, T8, T9> Of<T1, T2, T3, T4, T5, T6, T7, T8, T9>()
        where T1 : IEvent, new()
        where T2 : IEvent, new()
        where T3 : IEvent, new()
        where T4 : IEvent, new()
        where T5 : IEvent, new()
        where T6 : IEvent, new()
        where T7 : IEvent, new()
        where T8 : IEvent, new()
        where T9 : IEvent, new()
        => new();

    public static EventUnion<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10> Of<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>()
        where T1 : IEvent, new()
        where T2 : IEvent, new()
        where T3 : IEvent, new()
        where T4 : IEvent, new()
        where T5 : IEvent, new()
        where T6 : IEvent, new()
        where T7 : IEvent, new()
        where T8 : IEvent, new()
        where T9 : IEvent, new()
        where T10 : IEvent, new()
        => new();

    public static EventUnion<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11> Of<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11>()
        where T1 : IEvent, new()
        where T2 : IEvent, new()
        where T3 : IEvent, new()
        where T4 : IEvent, new()
        where T5 : IEvent, new()
        where T6 : IEvent, new()
        where T7 : IEvent, new()
        where T8 : IEvent, new()
        where T9 : IEvent, new()
        where T10 : IEvent, new()
        where T11 : IEvent, new()
        => new();

    public static EventUnion<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12> Of<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>()
        where T1 : IEvent, new()
        where T2 : IEvent, new()
        where T3 : IEvent, new()
        where T4 : IEvent, new()
        where T5 : IEvent, new()
        where T6 : IEvent, new()
        where T7 : IEvent, new()
        where T8 : IEvent, new()
        where T9 : IEvent, new()
        where T10 : IEvent, new()
        where T11 : IEvent, new()
        where T12 : IEvent, new()
        => new();

    public static EventUnion<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13> Of<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13>()
        where T1 : IEvent, new()
        where T2 : IEvent, new()
        where T3 : IEvent, new()
        where T4 : IEvent, new()
        where T5 : IEvent, new()
        where T6 : IEvent, new()
        where T7 : IEvent, new()
        where T8 : IEvent, new()
        where T9 : IEvent, new()
        where T10 : IEvent, new()
        where T11 : IEvent, new()
        where T12 : IEvent, new()
        where T13 : IEvent, new()
        => new();

    public static EventUnion<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14> Of<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14>()
        where T1 : IEvent, new()
        where T2 : IEvent, new()
        where T3 : IEvent, new()
        where T4 : IEvent, new()
        where T5 : IEvent, new()
        where T6 : IEvent, new()
        where T7 : IEvent, new()
        where T8 : IEvent, new()
        where T9 : IEvent, new()
        where T10 : IEvent, new()
        where T11 : IEvent, new()
        where T12 : IEvent, new()
        where T13 : IEvent, new()
        where T14 : IEvent, new()
        => new();

    public static EventUnion<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15> Of<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15>()
        where T1 : IEvent, new()
        where T2 : IEvent, new()
        where T3 : IEvent, new()
        where T4 : IEvent, new()
        where T5 : IEvent, new()
        where T6 : IEvent, new()
        where T7 : IEvent, new()
        where T8 : IEvent, new()
        where T9 : IEvent, new()
        where T10 : IEvent, new()
        where T11 : IEvent, new()
        where T12 : IEvent, new()
        where T13 : IEvent, new()
        where T14 : IEvent, new()
        where T15 : IEvent, new()
        => new();

    public static EventUnion<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16> Of<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16>()
        where T1 : IEvent, new()
        where T2 : IEvent, new()
        where T3 : IEvent, new()
        where T4 : IEvent, new()
        where T5 : IEvent, new()
        where T6 : IEvent, new()
        where T7 : IEvent, new()
        where T8 : IEvent, new()
        where T9 : IEvent, new()
        where T10 : IEvent, new()
        where T11 : IEvent, new()
        where T12 : IEvent, new()
        where T13 : IEvent, new()
        where T14 : IEvent, new()
        where T15 : IEvent, new()
        where T16 : IEvent, new()
        => new();
}