namespace Sia.Reactive;

using System.Runtime.CompilerServices;

public static partial class Term
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
    public static ScopeTerm<TContext, TChildren> Scope<TContext, TChildren>(
        in TContext value, in TChildren children)
        where TContext : struct
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
    public static EffectTerm<TEffect> Effect<TEffect>(in TEffect effect)
        where TEffect : struct, IEffect<TEffect>
        => new(effect);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static EitherTerm<TFirst, TSecond> Either<TFirst, TSecond>(
        bool isFirst, in TFirst first, in TSecond second)
        where TFirst : struct, ITerm<TFirst>
        where TSecond : struct, ITerm<TSecond>
        => new(isFirst, first, second);
}
