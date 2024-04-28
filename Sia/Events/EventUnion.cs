namespace Sia;

public class EventUnion<T1>
    : TypeUnion<T1>, IEventUnion
    where T1 : IEvent, new()
{
    public IEnumerable<Type> EventTypesWithPureEvents {
        get {
            foreach (var type in Types) {
                yield return type;
            }
            if (!typeof(T1).IsAssignableTo(typeof(SingletonEvent<T1>))) yield return typeof(PureEvent<T1>);
        }
    }
}

public class EventUnion<T1, T2>
    : TypeUnion<T1, T2>, IEventUnion
    where T1 : IEvent, new()
    where T2 : IEvent, new()
{
    public IEnumerable<Type> EventTypesWithPureEvents {
        get {
            foreach (var type in Types) {
                yield return type;
            }
            if (!typeof(T1).IsAssignableTo(typeof(SingletonEvent<T1>))) yield return typeof(PureEvent<T1>);
            if (!typeof(T2).IsAssignableTo(typeof(SingletonEvent<T2>))) yield return typeof(PureEvent<T2>);
        }
    }
}

public class EventUnion<T1, T2, T3>
    : TypeUnion<T1, T2, T3>, IEventUnion
    where T1 : IEvent, new()
    where T2 : IEvent, new()
    where T3 : IEvent, new()
{
    public IEnumerable<Type> EventTypesWithPureEvents {
        get {
            foreach (var type in Types) {
                yield return type;
            }
            if (!typeof(T1).IsAssignableTo(typeof(SingletonEvent<T1>))) yield return typeof(PureEvent<T1>);
            if (!typeof(T2).IsAssignableTo(typeof(SingletonEvent<T2>))) yield return typeof(PureEvent<T2>);
            if (!typeof(T3).IsAssignableTo(typeof(SingletonEvent<T3>))) yield return typeof(PureEvent<T3>);
        }
    }
}

public class EventUnion<T1, T2, T3, T4>
    : TypeUnion<T1, T2, T3, T4>, IEventUnion
    where T1 : IEvent, new()
    where T2 : IEvent, new()
    where T3 : IEvent, new()
    where T4 : IEvent, new()
{
    public IEnumerable<Type> EventTypesWithPureEvents {
        get {
            foreach (var type in Types) {
                yield return type;
            }
            if (!typeof(T1).IsAssignableTo(typeof(SingletonEvent<T1>))) yield return typeof(PureEvent<T1>);
            if (!typeof(T2).IsAssignableTo(typeof(SingletonEvent<T2>))) yield return typeof(PureEvent<T2>);
            if (!typeof(T3).IsAssignableTo(typeof(SingletonEvent<T3>))) yield return typeof(PureEvent<T3>);
            if (!typeof(T4).IsAssignableTo(typeof(SingletonEvent<T4>))) yield return typeof(PureEvent<T4>);
        }
    }
}

public class EventUnion<T1, T2, T3, T4, T5>
    : TypeUnion<T1, T2, T3, T4, T5>, IEventUnion
    where T1 : IEvent, new()
    where T2 : IEvent, new()
    where T3 : IEvent, new()
    where T4 : IEvent, new()
    where T5 : IEvent, new()
{
    public IEnumerable<Type> EventTypesWithPureEvents {
        get {
            foreach (var type in Types) {
                yield return type;
            }
            if (!typeof(T1).IsAssignableTo(typeof(SingletonEvent<T1>))) yield return typeof(PureEvent<T1>);
            if (!typeof(T2).IsAssignableTo(typeof(SingletonEvent<T2>))) yield return typeof(PureEvent<T2>);
            if (!typeof(T3).IsAssignableTo(typeof(SingletonEvent<T3>))) yield return typeof(PureEvent<T3>);
            if (!typeof(T4).IsAssignableTo(typeof(SingletonEvent<T4>))) yield return typeof(PureEvent<T4>);
            if (!typeof(T5).IsAssignableTo(typeof(SingletonEvent<T5>))) yield return typeof(PureEvent<T5>);
        }
    }
}

public class EventUnion<T1, T2, T3, T4, T5, T6>
    : TypeUnion<T1, T2, T3, T4, T5, T6>, IEventUnion
    where T1 : IEvent, new()
    where T2 : IEvent, new()
    where T3 : IEvent, new()
    where T4 : IEvent, new()
    where T5 : IEvent, new()
    where T6 : IEvent, new()
{
    public IEnumerable<Type> EventTypesWithPureEvents {
        get {
            foreach (var type in Types) {
                yield return type;
            }
            if (!typeof(T1).IsAssignableTo(typeof(SingletonEvent<T1>))) yield return typeof(PureEvent<T1>);
            if (!typeof(T2).IsAssignableTo(typeof(SingletonEvent<T2>))) yield return typeof(PureEvent<T2>);
            if (!typeof(T3).IsAssignableTo(typeof(SingletonEvent<T3>))) yield return typeof(PureEvent<T3>);
            if (!typeof(T4).IsAssignableTo(typeof(SingletonEvent<T4>))) yield return typeof(PureEvent<T4>);
            if (!typeof(T5).IsAssignableTo(typeof(SingletonEvent<T5>))) yield return typeof(PureEvent<T5>);
            if (!typeof(T6).IsAssignableTo(typeof(SingletonEvent<T6>))) yield return typeof(PureEvent<T6>);
        }
    }
}

public class EventUnion<T1, T2, T3, T4, T5, T6, T7>
    : TypeUnion<T1, T2, T3, T4, T5, T6, T7>, IEventUnion
    where T1 : IEvent, new()
    where T2 : IEvent, new()
    where T3 : IEvent, new()
    where T4 : IEvent, new()
    where T5 : IEvent, new()
    where T6 : IEvent, new()
    where T7 : IEvent, new()
{
    public IEnumerable<Type> EventTypesWithPureEvents {
        get {
            foreach (var type in Types) {
                yield return type;
            }
            if (!typeof(T1).IsAssignableTo(typeof(SingletonEvent<T1>))) yield return typeof(PureEvent<T1>);
            if (!typeof(T2).IsAssignableTo(typeof(SingletonEvent<T2>))) yield return typeof(PureEvent<T2>);
            if (!typeof(T3).IsAssignableTo(typeof(SingletonEvent<T3>))) yield return typeof(PureEvent<T3>);
            if (!typeof(T4).IsAssignableTo(typeof(SingletonEvent<T4>))) yield return typeof(PureEvent<T4>);
            if (!typeof(T5).IsAssignableTo(typeof(SingletonEvent<T5>))) yield return typeof(PureEvent<T5>);
            if (!typeof(T6).IsAssignableTo(typeof(SingletonEvent<T6>))) yield return typeof(PureEvent<T6>);
            if (!typeof(T7).IsAssignableTo(typeof(SingletonEvent<T7>))) yield return typeof(PureEvent<T7>);
        }
    }
}

public class EventUnion<T1, T2, T3, T4, T5, T6, T7, T8>
    : TypeUnion<T1, T2, T3, T4, T5, T6, T7, T8>, IEventUnion
    where T1 : IEvent, new()
    where T2 : IEvent, new()
    where T3 : IEvent, new()
    where T4 : IEvent, new()
    where T5 : IEvent, new()
    where T6 : IEvent, new()
    where T7 : IEvent, new()
    where T8 : IEvent, new()
{
    public IEnumerable<Type> EventTypesWithPureEvents {
        get {
            foreach (var type in Types) {
                yield return type;
            }
            if (!typeof(T1).IsAssignableTo(typeof(SingletonEvent<T1>))) yield return typeof(PureEvent<T1>);
            if (!typeof(T2).IsAssignableTo(typeof(SingletonEvent<T2>))) yield return typeof(PureEvent<T2>);
            if (!typeof(T3).IsAssignableTo(typeof(SingletonEvent<T3>))) yield return typeof(PureEvent<T3>);
            if (!typeof(T4).IsAssignableTo(typeof(SingletonEvent<T4>))) yield return typeof(PureEvent<T4>);
            if (!typeof(T5).IsAssignableTo(typeof(SingletonEvent<T5>))) yield return typeof(PureEvent<T5>);
            if (!typeof(T6).IsAssignableTo(typeof(SingletonEvent<T6>))) yield return typeof(PureEvent<T6>);
            if (!typeof(T7).IsAssignableTo(typeof(SingletonEvent<T7>))) yield return typeof(PureEvent<T7>);
            if (!typeof(T8).IsAssignableTo(typeof(SingletonEvent<T8>))) yield return typeof(PureEvent<T8>);
        }
    }
}

public class EventUnion<T1, T2, T3, T4, T5, T6, T7, T8, T9>
    : TypeUnion<T1, T2, T3, T4, T5, T6, T7, T8, T9>, IEventUnion
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
    public IEnumerable<Type> EventTypesWithPureEvents {
        get {
            foreach (var type in Types) {
                yield return type;
            }
            if (!typeof(T1).IsAssignableTo(typeof(SingletonEvent<T1>))) yield return typeof(PureEvent<T1>);
            if (!typeof(T2).IsAssignableTo(typeof(SingletonEvent<T2>))) yield return typeof(PureEvent<T2>);
            if (!typeof(T3).IsAssignableTo(typeof(SingletonEvent<T3>))) yield return typeof(PureEvent<T3>);
            if (!typeof(T4).IsAssignableTo(typeof(SingletonEvent<T4>))) yield return typeof(PureEvent<T4>);
            if (!typeof(T5).IsAssignableTo(typeof(SingletonEvent<T5>))) yield return typeof(PureEvent<T5>);
            if (!typeof(T6).IsAssignableTo(typeof(SingletonEvent<T6>))) yield return typeof(PureEvent<T6>);
            if (!typeof(T7).IsAssignableTo(typeof(SingletonEvent<T7>))) yield return typeof(PureEvent<T7>);
            if (!typeof(T8).IsAssignableTo(typeof(SingletonEvent<T8>))) yield return typeof(PureEvent<T8>);
            if (!typeof(T9).IsAssignableTo(typeof(SingletonEvent<T9>))) yield return typeof(PureEvent<T9>);
        }
    }
}

public class EventUnion<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>
    : TypeUnion<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>, IEventUnion
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
    public IEnumerable<Type> EventTypesWithPureEvents {
        get {
            foreach (var type in Types) {
                yield return type;
            }
            if (!typeof(T1).IsAssignableTo(typeof(SingletonEvent<T1>))) yield return typeof(PureEvent<T1>);
            if (!typeof(T2).IsAssignableTo(typeof(SingletonEvent<T2>))) yield return typeof(PureEvent<T2>);
            if (!typeof(T3).IsAssignableTo(typeof(SingletonEvent<T3>))) yield return typeof(PureEvent<T3>);
            if (!typeof(T4).IsAssignableTo(typeof(SingletonEvent<T4>))) yield return typeof(PureEvent<T4>);
            if (!typeof(T5).IsAssignableTo(typeof(SingletonEvent<T5>))) yield return typeof(PureEvent<T5>);
            if (!typeof(T6).IsAssignableTo(typeof(SingletonEvent<T6>))) yield return typeof(PureEvent<T6>);
            if (!typeof(T7).IsAssignableTo(typeof(SingletonEvent<T7>))) yield return typeof(PureEvent<T7>);
            if (!typeof(T8).IsAssignableTo(typeof(SingletonEvent<T8>))) yield return typeof(PureEvent<T8>);
            if (!typeof(T9).IsAssignableTo(typeof(SingletonEvent<T9>))) yield return typeof(PureEvent<T9>);
            if (!typeof(T10).IsAssignableTo(typeof(SingletonEvent<T10>))) yield return typeof(PureEvent<T10>);
        }
    }
}

public class EventUnion<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11>
    : TypeUnion<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11>, IEventUnion
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
    public IEnumerable<Type> EventTypesWithPureEvents {
        get {
            foreach (var type in Types) {
                yield return type;
            }
            if (!typeof(T1).IsAssignableTo(typeof(SingletonEvent<T1>))) yield return typeof(PureEvent<T1>);
            if (!typeof(T2).IsAssignableTo(typeof(SingletonEvent<T2>))) yield return typeof(PureEvent<T2>);
            if (!typeof(T3).IsAssignableTo(typeof(SingletonEvent<T3>))) yield return typeof(PureEvent<T3>);
            if (!typeof(T4).IsAssignableTo(typeof(SingletonEvent<T4>))) yield return typeof(PureEvent<T4>);
            if (!typeof(T5).IsAssignableTo(typeof(SingletonEvent<T5>))) yield return typeof(PureEvent<T5>);
            if (!typeof(T6).IsAssignableTo(typeof(SingletonEvent<T6>))) yield return typeof(PureEvent<T6>);
            if (!typeof(T7).IsAssignableTo(typeof(SingletonEvent<T7>))) yield return typeof(PureEvent<T7>);
            if (!typeof(T8).IsAssignableTo(typeof(SingletonEvent<T8>))) yield return typeof(PureEvent<T8>);
            if (!typeof(T9).IsAssignableTo(typeof(SingletonEvent<T9>))) yield return typeof(PureEvent<T9>);
            if (!typeof(T10).IsAssignableTo(typeof(SingletonEvent<T10>))) yield return typeof(PureEvent<T10>);
            if (!typeof(T11).IsAssignableTo(typeof(SingletonEvent<T11>))) yield return typeof(PureEvent<T11>);
        }
    }
}

public class EventUnion<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>
    : TypeUnion<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>, IEventUnion
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
    public IEnumerable<Type> EventTypesWithPureEvents {
        get {
            foreach (var type in Types) {
                yield return type;
            }
            if (!typeof(T1).IsAssignableTo(typeof(SingletonEvent<T1>))) yield return typeof(PureEvent<T1>);
            if (!typeof(T2).IsAssignableTo(typeof(SingletonEvent<T2>))) yield return typeof(PureEvent<T2>);
            if (!typeof(T3).IsAssignableTo(typeof(SingletonEvent<T3>))) yield return typeof(PureEvent<T3>);
            if (!typeof(T4).IsAssignableTo(typeof(SingletonEvent<T4>))) yield return typeof(PureEvent<T4>);
            if (!typeof(T5).IsAssignableTo(typeof(SingletonEvent<T5>))) yield return typeof(PureEvent<T5>);
            if (!typeof(T6).IsAssignableTo(typeof(SingletonEvent<T6>))) yield return typeof(PureEvent<T6>);
            if (!typeof(T7).IsAssignableTo(typeof(SingletonEvent<T7>))) yield return typeof(PureEvent<T7>);
            if (!typeof(T8).IsAssignableTo(typeof(SingletonEvent<T8>))) yield return typeof(PureEvent<T8>);
            if (!typeof(T9).IsAssignableTo(typeof(SingletonEvent<T9>))) yield return typeof(PureEvent<T9>);
            if (!typeof(T10).IsAssignableTo(typeof(SingletonEvent<T10>))) yield return typeof(PureEvent<T10>);
            if (!typeof(T11).IsAssignableTo(typeof(SingletonEvent<T11>))) yield return typeof(PureEvent<T11>);
            if (!typeof(T12).IsAssignableTo(typeof(SingletonEvent<T12>))) yield return typeof(PureEvent<T12>);
        }
    }
}

public class EventUnion<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13>
    : TypeUnion<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13>, IEventUnion
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
    public IEnumerable<Type> EventTypesWithPureEvents {
        get {
            foreach (var type in Types) {
                yield return type;
            }
            if (!typeof(T1).IsAssignableTo(typeof(SingletonEvent<T1>))) yield return typeof(PureEvent<T1>);
            if (!typeof(T2).IsAssignableTo(typeof(SingletonEvent<T2>))) yield return typeof(PureEvent<T2>);
            if (!typeof(T3).IsAssignableTo(typeof(SingletonEvent<T3>))) yield return typeof(PureEvent<T3>);
            if (!typeof(T4).IsAssignableTo(typeof(SingletonEvent<T4>))) yield return typeof(PureEvent<T4>);
            if (!typeof(T5).IsAssignableTo(typeof(SingletonEvent<T5>))) yield return typeof(PureEvent<T5>);
            if (!typeof(T6).IsAssignableTo(typeof(SingletonEvent<T6>))) yield return typeof(PureEvent<T6>);
            if (!typeof(T7).IsAssignableTo(typeof(SingletonEvent<T7>))) yield return typeof(PureEvent<T7>);
            if (!typeof(T8).IsAssignableTo(typeof(SingletonEvent<T8>))) yield return typeof(PureEvent<T8>);
            if (!typeof(T9).IsAssignableTo(typeof(SingletonEvent<T9>))) yield return typeof(PureEvent<T9>);
            if (!typeof(T10).IsAssignableTo(typeof(SingletonEvent<T10>))) yield return typeof(PureEvent<T10>);
            if (!typeof(T11).IsAssignableTo(typeof(SingletonEvent<T11>))) yield return typeof(PureEvent<T11>);
            if (!typeof(T12).IsAssignableTo(typeof(SingletonEvent<T12>))) yield return typeof(PureEvent<T12>);
            if (!typeof(T13).IsAssignableTo(typeof(SingletonEvent<T13>))) yield return typeof(PureEvent<T13>);
        }
    }
}

public class EventUnion<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14>
    : TypeUnion<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14>, IEventUnion
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
    public IEnumerable<Type> EventTypesWithPureEvents {
        get {
            foreach (var type in Types) {
                yield return type;
            }
            if (!typeof(T1).IsAssignableTo(typeof(SingletonEvent<T1>))) yield return typeof(PureEvent<T1>);
            if (!typeof(T2).IsAssignableTo(typeof(SingletonEvent<T2>))) yield return typeof(PureEvent<T2>);
            if (!typeof(T3).IsAssignableTo(typeof(SingletonEvent<T3>))) yield return typeof(PureEvent<T3>);
            if (!typeof(T4).IsAssignableTo(typeof(SingletonEvent<T4>))) yield return typeof(PureEvent<T4>);
            if (!typeof(T5).IsAssignableTo(typeof(SingletonEvent<T5>))) yield return typeof(PureEvent<T5>);
            if (!typeof(T6).IsAssignableTo(typeof(SingletonEvent<T6>))) yield return typeof(PureEvent<T6>);
            if (!typeof(T7).IsAssignableTo(typeof(SingletonEvent<T7>))) yield return typeof(PureEvent<T7>);
            if (!typeof(T8).IsAssignableTo(typeof(SingletonEvent<T8>))) yield return typeof(PureEvent<T8>);
            if (!typeof(T9).IsAssignableTo(typeof(SingletonEvent<T9>))) yield return typeof(PureEvent<T9>);
            if (!typeof(T10).IsAssignableTo(typeof(SingletonEvent<T10>))) yield return typeof(PureEvent<T10>);
            if (!typeof(T11).IsAssignableTo(typeof(SingletonEvent<T11>))) yield return typeof(PureEvent<T11>);
            if (!typeof(T12).IsAssignableTo(typeof(SingletonEvent<T12>))) yield return typeof(PureEvent<T12>);
            if (!typeof(T13).IsAssignableTo(typeof(SingletonEvent<T13>))) yield return typeof(PureEvent<T13>);
            if (!typeof(T14).IsAssignableTo(typeof(SingletonEvent<T14>))) yield return typeof(PureEvent<T14>);
        }
    }
}

public class EventUnion<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15>
    : TypeUnion<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15>, IEventUnion
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
    public IEnumerable<Type> EventTypesWithPureEvents {
        get {
            foreach (var type in Types) {
                yield return type;
            }
            if (!typeof(T1).IsAssignableTo(typeof(SingletonEvent<T1>))) yield return typeof(PureEvent<T1>);
            if (!typeof(T2).IsAssignableTo(typeof(SingletonEvent<T2>))) yield return typeof(PureEvent<T2>);
            if (!typeof(T3).IsAssignableTo(typeof(SingletonEvent<T3>))) yield return typeof(PureEvent<T3>);
            if (!typeof(T4).IsAssignableTo(typeof(SingletonEvent<T4>))) yield return typeof(PureEvent<T4>);
            if (!typeof(T5).IsAssignableTo(typeof(SingletonEvent<T5>))) yield return typeof(PureEvent<T5>);
            if (!typeof(T6).IsAssignableTo(typeof(SingletonEvent<T6>))) yield return typeof(PureEvent<T6>);
            if (!typeof(T7).IsAssignableTo(typeof(SingletonEvent<T7>))) yield return typeof(PureEvent<T7>);
            if (!typeof(T8).IsAssignableTo(typeof(SingletonEvent<T8>))) yield return typeof(PureEvent<T8>);
            if (!typeof(T9).IsAssignableTo(typeof(SingletonEvent<T9>))) yield return typeof(PureEvent<T9>);
            if (!typeof(T10).IsAssignableTo(typeof(SingletonEvent<T10>))) yield return typeof(PureEvent<T10>);
            if (!typeof(T11).IsAssignableTo(typeof(SingletonEvent<T11>))) yield return typeof(PureEvent<T11>);
            if (!typeof(T12).IsAssignableTo(typeof(SingletonEvent<T12>))) yield return typeof(PureEvent<T12>);
            if (!typeof(T13).IsAssignableTo(typeof(SingletonEvent<T13>))) yield return typeof(PureEvent<T13>);
            if (!typeof(T14).IsAssignableTo(typeof(SingletonEvent<T14>))) yield return typeof(PureEvent<T14>);
            if (!typeof(T15).IsAssignableTo(typeof(SingletonEvent<T15>))) yield return typeof(PureEvent<T15>);
        }
    }
}

public class EventUnion<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16>
    : TypeUnion<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16>, IEventUnion
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
    public IEnumerable<Type> EventTypesWithPureEvents {
        get {
            foreach (var type in Types) {
                yield return type;
            }
            if (!typeof(T1).IsAssignableTo(typeof(SingletonEvent<T1>))) yield return typeof(PureEvent<T1>);
            if (!typeof(T2).IsAssignableTo(typeof(SingletonEvent<T2>))) yield return typeof(PureEvent<T2>);
            if (!typeof(T3).IsAssignableTo(typeof(SingletonEvent<T3>))) yield return typeof(PureEvent<T3>);
            if (!typeof(T4).IsAssignableTo(typeof(SingletonEvent<T4>))) yield return typeof(PureEvent<T4>);
            if (!typeof(T5).IsAssignableTo(typeof(SingletonEvent<T5>))) yield return typeof(PureEvent<T5>);
            if (!typeof(T6).IsAssignableTo(typeof(SingletonEvent<T6>))) yield return typeof(PureEvent<T6>);
            if (!typeof(T7).IsAssignableTo(typeof(SingletonEvent<T7>))) yield return typeof(PureEvent<T7>);
            if (!typeof(T8).IsAssignableTo(typeof(SingletonEvent<T8>))) yield return typeof(PureEvent<T8>);
            if (!typeof(T9).IsAssignableTo(typeof(SingletonEvent<T9>))) yield return typeof(PureEvent<T9>);
            if (!typeof(T10).IsAssignableTo(typeof(SingletonEvent<T10>))) yield return typeof(PureEvent<T10>);
            if (!typeof(T11).IsAssignableTo(typeof(SingletonEvent<T11>))) yield return typeof(PureEvent<T11>);
            if (!typeof(T12).IsAssignableTo(typeof(SingletonEvent<T12>))) yield return typeof(PureEvent<T12>);
            if (!typeof(T13).IsAssignableTo(typeof(SingletonEvent<T13>))) yield return typeof(PureEvent<T13>);
            if (!typeof(T14).IsAssignableTo(typeof(SingletonEvent<T14>))) yield return typeof(PureEvent<T14>);
            if (!typeof(T15).IsAssignableTo(typeof(SingletonEvent<T15>))) yield return typeof(PureEvent<T15>);
            if (!typeof(T16).IsAssignableTo(typeof(SingletonEvent<T16>))) yield return typeof(PureEvent<T16>);
        }
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