namespace Sia.Reactive;

using System.Runtime.CompilerServices;

public static class Term
{
    public static UnitTerm None => default;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static EntityTerm<TList, UnitTerm> Entity<TList>(in TList components)
        where TList : struct, IHList
        => new(components, default);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static EntityTerm<TList, TChildren> Entity<TList, TChildren>(
        in TList components, in TChildren children)
        where TList : struct, IHList
        where TChildren : struct, ITerm<TChildren>
        => new(components, children);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static LiftTerm<TSpec> Lift<TSpec>(in TSpec props)
        where TSpec : struct, ISpec, IEquatable<TSpec>
        => new(props);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static SystemTerm<TSystem> System<TSystem>()
        where TSystem : ISystem, new()
        => default;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ScheduleTerm<TLabel, TChildren> Schedule<TLabel, TChildren>(
        TLabel _, in TChildren children)
        where TLabel : struct
        where TChildren : struct, ITerm<TChildren>
        => new(children);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ScopeTerm<TCtx, TChildren> Scope<TCtx, TChildren>(
        in TCtx value, in TChildren children)
        where TCtx : struct
        where TChildren : struct, ITerm<TChildren>
        => new(value, children);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ForEachTerm<TKey, TSpec> ForEach<TKey, TSpec>(
        ReadOnlyMemory<Keyed<TKey, TSpec>> items)
        where TKey : notnull
        where TSpec : struct, ISpec, IEquatable<TSpec>
        => new(items);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Keyed<TKey, TSpec> Keyed<TKey, TSpec>(TKey key, in TSpec props)
        where TKey : notnull
        where TSpec : struct, ISpec, IEquatable<TSpec>
        => new(key, props);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static CondTerm<TTerm> Cond<TTerm>(bool condition, in TTerm term)
        where TTerm : struct, ITerm<TTerm>
        => new(condition, term);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static EitherTerm<TFirst, TSecond> Either<TFirst, TSecond>(
        bool isFirst, in TFirst first, in TSecond second)
        where TFirst : struct, ITerm<TFirst>
        where TSecond : struct, ITerm<TSecond>
        => new(isFirst, first, second);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Group<T1, T2> Group<T1, T2>(in T1 item1, in T2 item2)
        where T1 : struct, ITerm<T1>
        where T2 : struct, ITerm<T2>
        => new(item1, item2);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Group<T1, T2, T3> Group<T1, T2, T3>(
        in T1 item1, in T2 item2, in T3 item3)
        where T1 : struct, ITerm<T1>
        where T2 : struct, ITerm<T2>
        where T3 : struct, ITerm<T3>
        => new(item1, item2, item3);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Group<T1, T2, T3, T4> Group<T1, T2, T3, T4>(
        in T1 item1, in T2 item2, in T3 item3, in T4 item4)
        where T1 : struct, ITerm<T1>
        where T2 : struct, ITerm<T2>
        where T3 : struct, ITerm<T3>
        where T4 : struct, ITerm<T4>
        => new(item1, item2, item3, item4);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Group<T1, T2, T3, T4, T5> Group<T1, T2, T3, T4, T5>(
        in T1 item1, in T2 item2, in T3 item3, in T4 item4, in T5 item5)
        where T1 : struct, ITerm<T1>
        where T2 : struct, ITerm<T2>
        where T3 : struct, ITerm<T3>
        where T4 : struct, ITerm<T4>
        where T5 : struct, ITerm<T5>
        => new(item1, item2, item3, item4, item5);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Group<T1, T2, T3, T4, T5, T6> Group<T1, T2, T3, T4, T5, T6>(
        in T1 item1, in T2 item2, in T3 item3, in T4 item4, in T5 item5, in T6 item6)
        where T1 : struct, ITerm<T1>
        where T2 : struct, ITerm<T2>
        where T3 : struct, ITerm<T3>
        where T4 : struct, ITerm<T4>
        where T5 : struct, ITerm<T5>
        where T6 : struct, ITerm<T6>
        => new(item1, item2, item3, item4, item5, item6);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Group<T1, T2, T3, T4, T5, T6, T7> Group<T1, T2, T3, T4, T5, T6, T7>(
        in T1 item1, in T2 item2, in T3 item3, in T4 item4, in T5 item5, in T6 item6,
        in T7 item7)
        where T1 : struct, ITerm<T1>
        where T2 : struct, ITerm<T2>
        where T3 : struct, ITerm<T3>
        where T4 : struct, ITerm<T4>
        where T5 : struct, ITerm<T5>
        where T6 : struct, ITerm<T6>
        where T7 : struct, ITerm<T7>
        => new(item1, item2, item3, item4, item5, item6, item7);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Group<T1, T2, T3, T4, T5, T6, T7, T8> Group<T1, T2, T3, T4, T5, T6, T7, T8>(
        in T1 item1, in T2 item2, in T3 item3, in T4 item4, in T5 item5, in T6 item6,
        in T7 item7, in T8 item8)
        where T1 : struct, ITerm<T1>
        where T2 : struct, ITerm<T2>
        where T3 : struct, ITerm<T3>
        where T4 : struct, ITerm<T4>
        where T5 : struct, ITerm<T5>
        where T6 : struct, ITerm<T6>
        where T7 : struct, ITerm<T7>
        where T8 : struct, ITerm<T8>
        => new(item1, item2, item3, item4, item5, item6, item7, item8);
}
