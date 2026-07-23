namespace Sia.Reactive;

using System.Runtime.CompilerServices;

[AttributeUsage(AttributeTargets.Parameter, Inherited = false, AllowMultiple = false)]
internal sealed class NestedCallbackAttribute : Attribute;

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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ReactiveNode<ComponentTerm<TProps>> Component<
        TProps>(
        [NestedCallback] ReactiveComponent<TProps> render,
        scoped in TProps props)
        where TProps : struct
    {
        ArgumentNullException.ThrowIfNull(render);
        return new(new(render, props));
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
            [NestedCallback] ReactiveContextRenderer<TContext, TTerm> render)
        where TContext : struct
        where TTerm : struct, ITerm<TTerm>
        => new(Term.Lift(new ContextRenderSpec<TContext, TTerm>(
            render ?? throw new ArgumentNullException(nameof(render)))));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ReactiveNode<ForEachTerm<TKey, ReactiveNodeSpec<TTerm>>>
        ForEach<TKey, TTerm>(
            params ReadOnlySpan<(TKey Key, ReactiveNode<TTerm> Value)> items)
        where TKey : notnull
        where TTerm : struct, ITerm<TTerm>
    {
        var keyed = new Keyed<TKey, ReactiveNodeSpec<TTerm>>[items.Length];
        for (var index = 0; index < items.Length; index++) {
            keyed[index] = Term.Keyed(
                items[index].Key,
                new ReactiveNodeSpec<TTerm>(items[index].Value.Term));
        }
        return new(Term.ForEach<TKey, ReactiveNodeSpec<TTerm>>(keyed));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ReactiveNode<ForEachTerm<TKey, ReactiveItemSpec<TItem, TTerm>>>
        ForEach<TKey, TItem, TTerm>(
            ReactiveItemRenderer<TItem, TTerm> render,
            params ReadOnlySpan<(TKey Key, TItem Value)> items)
        where TKey : notnull
        where TTerm : struct, ITerm<TTerm>
    {
        ArgumentNullException.ThrowIfNull(render);
        var keyed = new Keyed<TKey, ReactiveItemSpec<TItem, TTerm>>[items.Length];
        for (var index = 0; index < items.Length; index++) {
            keyed[index] = Term.Keyed(
                items[index].Key,
                new ReactiveItemSpec<TItem, TTerm>(items[index].Value, render));
        }
        return new(Term.ForEach<TKey, ReactiveItemSpec<TItem, TTerm>>(keyed));
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
    public static ReactiveNode<EventActionTerm<TEvent, TCapture>> On<
        TEvent, TCapture>(
        scoped in TCapture capture,
        [NestedCallback] ReactiveEventAction<TEvent, TCapture> action)
        where TEvent : IEvent
        where TCapture : struct
        => new(new(
            capture,
            action ?? throw new ArgumentNullException(nameof(action))));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ReactiveNode<DeferredEffectTerm<
        TDependencies, TResource>> Effect<TDependencies, TResource>(
        scoped in TDependencies dependencies,
        [NestedCallback] ReactiveEffectSetup<TDependencies, TResource> setup,
        [NestedCallback] ReactiveEffectCleanup<TResource> cleanup)
        where TDependencies : struct, IEquatable<TDependencies>
        => new(new(
            dependencies,
            setup ?? throw new ArgumentNullException(nameof(setup)),
            cleanup ?? throw new ArgumentNullException(nameof(cleanup))));
}
