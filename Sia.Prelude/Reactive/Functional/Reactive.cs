namespace Sia.Reactive;

using System.Runtime.CompilerServices;

public readonly record struct ReactiveKeyed<TKey, TValue>(
    TKey Key,
    TValue Value)
    where TKey : notnull;

public delegate ReactiveNode<TTerm> ReactiveItemRenderer<TItem, TTerm>(
    scoped in TItem item)
    where TTerm : struct, ITerm<TTerm>;

public readonly record struct ReactiveItemSpec<TItem, TTerm>(
    TItem Item,
    ReactiveItemRenderer<TItem, TTerm> Render)
    : ISpec<ReactiveItemSpec<TItem, TTerm>, Unit, TTerm>
    where TTerm : struct, ITerm<TTerm>
{
    public static TTerm Expand(
        in ReactiveItemSpec<TItem, TTerm> props,
        in Unit state,
        in ExpandContext context)
        => props.Render(props.Item).Term;
}

public delegate TMessage ReactiveEventHandler<TEvent, TMessage>(
    scoped in TEvent @event)
    where TEvent : IEvent;

public delegate TResource ReactiveEffectSetup<TDependencies, TResource>(
    scoped in TDependencies dependencies)
    where TDependencies : struct, IEquatable<TDependencies>;

public delegate void ReactiveEffectCleanup<TResource>(
    scoped in TResource resource);

public delegate ReactiveNode<TTerm> ReactiveContextRenderer<TContext, TTerm>(
    scoped in TContext value)
    where TContext : struct
    where TTerm : struct, ITerm<TTerm>;

public static partial class Reactive
{
    public static ReactiveNode<UnitTerm> None => new(default);

    public static ReactiveComponent<TProps, TState, TMessage> Define<
        TProps, TState, TMessage>(
        ReactiveInitial<TProps, TState> initialState,
        ReactiveReducer<TState, TMessage> reduce,
        ReactiveRenderer<TProps, TState> render)
        where TProps : struct
        where TState : struct
        => new(initialState, reduce, render);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ReactiveNode<FunctionalComponentTerm<
        TProps, TState, TMessage>> Func<TProps, TState, TMessage>(
        ReactiveInitial<TProps, TState> initial,
        ReactiveReducer<TState, TMessage> reduce,
        ReactiveRenderer<TProps, TState> render,
        scoped in TProps props)
        where TProps : struct
        where TState : struct
    {
        ArgumentNullException.ThrowIfNull(initial);
        ArgumentNullException.ThrowIfNull(reduce);
        ArgumentNullException.ThrowIfNull(render);
        return new(new FunctionalComponentTerm<TProps, TState, TMessage>(
            FuncComponentCache<TProps, TState, TMessage>.GetOrAdd(initial, reduce, render),
            props));
    }

    private static class FuncComponentCache<TProps, TState, TMessage>
        where TProps : struct
        where TState : struct
    {
        private static readonly ConditionalWeakTable<
            ReactiveRenderer<TProps, TState>,
            ReactiveComponent<TProps, TState, TMessage>> Cache = [];

        public static ReactiveComponent<TProps, TState, TMessage> GetOrAdd(
            ReactiveInitial<TProps, TState> initial,
            ReactiveReducer<TState, TMessage> reduce,
            ReactiveRenderer<TProps, TState> render)
        {
            if (Cache.TryGetValue(render, out var existing)) {
                return existing;
            }
            var component = new ReactiveComponent<TProps, TState, TMessage>(initial, reduce, render);
            return Cache.GetValue(render, _ => component);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ReactiveNode<EntityTerm<TList, UnitTerm>> Entity<TList>(
        scoped in TList components)
        where TList : struct, IHList
        => new(Term.Entity(components));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ReactiveNode<EntityTerm<TList, TChildren>> Entity<
        TList, TChildren>(
        scoped in TList components,
        scoped in ReactiveNode<TChildren> children)
        where TList : struct, IHList
        where TChildren : struct, ITerm<TChildren>
        => new(Term.Entity(components, children.Term));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ReactiveNode<CondTerm<TTerm>> When<TTerm>(
        bool condition,
        scoped in ReactiveNode<TTerm> node)
        where TTerm : struct, ITerm<TTerm>
        => new(Term.Cond(condition, node.Term));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ReactiveNode<EitherTerm<TFirst, TSecond>> Either<
        TFirst, TSecond>(
        bool first,
        scoped in ReactiveNode<TFirst> whenTrue,
        scoped in ReactiveNode<TSecond> whenFalse)
        where TFirst : struct, ITerm<TFirst>
        where TSecond : struct, ITerm<TSecond>
        => new(Term.Either(first, whenTrue.Term, whenFalse.Term));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ReactiveNode<ScopeTerm<TContext, TChildren>> Provide<
        TContext, TChildren>(
        scoped in TContext value,
        scoped in ReactiveNode<TChildren> children)
        where TContext : struct
        where TChildren : struct, ITerm<TChildren>
        => new(Term.Scope(value, children.Term));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ReactiveNode<LiftTerm<ContextRenderSpec<TContext, TTerm>>>
        Use<TContext, TTerm>(
            ReactiveContextRenderer<TContext, TTerm> render)
        where TContext : struct
        where TTerm : struct, ITerm<TTerm>
        => new(Term.Lift(new ContextRenderSpec<TContext, TTerm>(
            render ?? throw new ArgumentNullException(nameof(render)))));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ReactiveKeyed<TKey, TValue> Keyed<TKey, TValue>(
        TKey key,
        scoped in TValue value)
        where TKey : notnull
        => new(key, value);

    public static ReactiveNode<ForEachTerm<TKey, ReactiveNodeSpec<TTerm>>>
        ForEach<TKey, TTerm>(
            ReadOnlyMemory<ReactiveKeyed<TKey, ReactiveNode<TTerm>>> items)
        where TKey : notnull
        where TTerm : struct, ITerm<TTerm>
    {
        var source = items.Span;
        var keyed = new Keyed<TKey, ReactiveNodeSpec<TTerm>>[source.Length];
        for (var index = 0; index < source.Length; index++) {
            keyed[index] = Term.Keyed(
                source[index].Key,
                new ReactiveNodeSpec<TTerm>(source[index].Value.Term));
        }
        return new(Term.ForEach<TKey, ReactiveNodeSpec<TTerm>>(keyed));
    }

    public static ReactiveNode<ForEachTerm<TKey, ReactiveNodeSpec<TTerm>>>
        ForEach<TKey, TTerm>(
            params ReactiveKeyed<TKey, ReactiveNode<TTerm>>[] items)
        where TKey : notnull
        where TTerm : struct, ITerm<TTerm>
    {
        ArgumentNullException.ThrowIfNull(items);
        return ForEach<TKey, TTerm>(items.AsMemory());
    }

    public static ReactiveNode<ForEachTerm<
        TKey, ReactiveItemSpec<TItem, TTerm>>> ForEach<
        TKey, TItem, TTerm>(
        ReadOnlyMemory<ReactiveKeyed<TKey, TItem>> items,
        ReactiveItemRenderer<TItem, TTerm> render)
        where TKey : notnull
        where TTerm : struct, ITerm<TTerm>
    {
        ArgumentNullException.ThrowIfNull(render);
        var source = items.Span;
        var keyed = new Keyed<
            TKey, ReactiveItemSpec<TItem, TTerm>>[source.Length];
        for (var index = 0; index < source.Length; index++) {
            keyed[index] = Term.Keyed(
                source[index].Key,
                new ReactiveItemSpec<TItem, TTerm>(
                    source[index].Value,
                    render));
        }
        return new(Term.ForEach<
            TKey, ReactiveItemSpec<TItem, TTerm>>(keyed));
    }

    public static ReactiveNode<ForEachTerm<
        TKey, ReactiveItemSpec<TItem, TTerm>>> ForEach<
        TKey, TItem, TTerm>(
        ReactiveItemRenderer<TItem, TTerm> render,
        params ReactiveKeyed<TKey, TItem>[] items)
        where TKey : notnull
        where TTerm : struct, ITerm<TTerm>
    {
        ArgumentNullException.ThrowIfNull(items);
        return ForEach<TKey, TItem, TTerm>(items.AsMemory(), render);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ReactiveNode<FunctionalComponentTerm<
        TProps, TState, TMessage>> Component<TProps, TState, TMessage>(
        ReactiveComponent<TProps, TState, TMessage> component,
        scoped in TProps props)
        where TProps : struct
        where TState : struct
    {
        ArgumentNullException.ThrowIfNull(component);
        return new(new(component, props));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ReactiveNode<ScheduleTerm<TLabel, TChildren>> Schedule<
        TLabel, TChildren>(
        TLabel _,
        scoped in ReactiveNode<TChildren> children)
        where TLabel : struct
        where TChildren : struct, ITerm<TChildren>
        => new(Term.Schedule(default(TLabel), children.Term));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ReactiveNode<SystemTerm<TSystem>> System<TSystem>()
        where TSystem : ISystem, new()
        => new(Term.System<TSystem>());

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ReactiveNode<EventBindingTerm<TEvent, TMessage>> On<
        TEvent, TMessage>(
        ReactiveEventHandler<TEvent, TMessage> handler)
        where TEvent : IEvent
        => new(new(handler
            ?? throw new ArgumentNullException(nameof(handler))));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ReactiveNode<DeferredEffectTerm<
        TDependencies, TResource>> Effect<TDependencies, TResource>(
        scoped in TDependencies dependencies,
        ReactiveEffectSetup<TDependencies, TResource> setup,
        ReactiveEffectCleanup<TResource> cleanup)
        where TDependencies : struct, IEquatable<TDependencies>
        => new(new(
            dependencies,
            setup ?? throw new ArgumentNullException(nameof(setup)),
            cleanup ?? throw new ArgumentNullException(nameof(cleanup))));
}
