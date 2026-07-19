namespace Sia.Reactive;

[AttributeUsage(
    AttributeTargets.Class,
    Inherited = false,
    AllowMultiple = false)]
public sealed class ReactiveComponentAttribute : Attribute;

public delegate TState ReactiveInitial<TProps, TState>(scoped in TProps props)
    where TProps : struct
    where TState : struct;

public delegate TState ReactiveReducer<TState, TMessage>(
    scoped in TState state,
    scoped in TMessage message)
    where TState : struct;

public delegate ReactiveNode ReactiveRenderer<TProps, TState>(
    scoped in TProps props,
    scoped in TState state)
    where TProps : struct
    where TState : struct;

public sealed class ReactiveComponent<TProps, TState, TMessage>
    where TProps : struct
    where TState : struct
{
    public ReactiveInitial<TProps, TState> Initial { get; }
    public ReactiveReducer<TState, TMessage> Reducer { get; }
    public ReactiveRenderer<TProps, TState> Renderer { get; }

    public ReactiveComponent(
        ReactiveInitial<TProps, TState> initial,
        ReactiveReducer<TState, TMessage> reducer,
        ReactiveRenderer<TProps, TState> renderer)
    {
        Initial = initial ?? throw new ArgumentNullException(nameof(initial));
        Reducer = reducer ?? throw new ArgumentNullException(nameof(reducer));
        Renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
    }
}

internal readonly record struct FunctionalSpec<TProps, TState, TMessage>(
    ReactiveComponent<TProps, TState, TMessage> Component,
    TProps Props)
    : ISpec<FunctionalSpec<TProps, TState, TMessage>, TState, OpaqueTerm>
    where TProps : struct
    where TState : struct
{
    public static TState InitialState(
        in FunctionalSpec<TProps, TState, TMessage> props)
        => props.Component.Initial(props.Props);

    public static OpaqueTerm Expand(
        in FunctionalSpec<TProps, TState, TMessage> props,
        in TState state,
        in ExpandContext context)
    {
        ref var cell = ref context.Cell.GetUnchecked<Cell>();
        cell.DispatchMessage ??= Dispatch;
        cell.MessageOwner = context.Cell;
        return new(props.Component.Renderer(props.Props, state));
    }

    private static void Dispatch(
        Reconciler reconciler,
        Entity cell,
        object message)
    {
        if (message is not TMessage typedMessage) {
            throw new ArgumentException(
                $"Message type '{message.GetType()}' is not assignable to "
                + $"'{typeof(TMessage)}'.",
                nameof(message));
        }

        var spec = cell.GetUnchecked<FunctionalSpec<TProps, TState, TMessage>>();
        var previous = cell.GetUnchecked<TState>();
        var next = spec.Component.Reducer(previous, typedMessage);
        if (EqualityComparer<TState>.Default.Equals(previous, next)) {
            return;
        }
        cell.GetUnchecked<TState>() = next;
        reconciler.EnqueueDirty(cell);
    }
}

public readonly record struct FunctionalComponentTerm<
    TProps, TState, TMessage>(
    ReactiveComponent<TProps, TState, TMessage> Component,
    TProps Props)
    : ITerm<FunctionalComponentTerm<TProps, TState, TMessage>>
    where TProps : struct
    where TState : struct
{
    public static int SlotCount => 1;

    public static void Mount(
        in FunctionalComponentTerm<TProps, TState, TMessage> self,
        ref GraphContext context)
        => context.SetSlot(context.Reconciler.MountSub(
            new FunctionalSpec<TProps, TState, TMessage>(
                self.Component,
                self.Props),
            context.Cell,
            context.Depth + 1,
            context.NextSlotIndex,
            context.Schedule,
            context.Scope,
            context.Output,
            context.MessageOwner));

    public static void Reconcile(
        in FunctionalComponentTerm<TProps, TState, TMessage> previous,
        in FunctionalComponentTerm<TProps, TState, TMessage> next,
        ref GraphContext context)
    {
        var slot = context.NextSlotIndex;
        var child = context.PeekSlot();
        if (child is not { IsValid: true }
            || !child.ContainsUnchecked<FunctionalSpec<
                TProps, TState, TMessage>>()) {
            Mount(next, ref context);
            return;
        }

        ref var current = ref child.GetUnchecked<FunctionalSpec<
            TProps, TState, TMessage>>();
        if (!ReferenceEquals(current.Component, next.Component)) {
            context.DestroyRange(1);
            context.RewindTo(slot);
            Mount(next, ref context);
            return;
        }
        if (!EqualityComparer<TProps>.Default.Equals(
                current.Props, next.Props)) {
            context.Reconciler.UpdateMount(
                child,
                new FunctionalSpec<TProps, TState, TMessage>(
                    next.Component,
                    next.Props));
        }
        context.Advance();
    }
}

public readonly record struct ContextRenderSpec<TContext, TTerm>(
    ReactiveContextRenderer<TContext, TTerm> Render)
    : ISpec<ContextRenderSpec<TContext, TTerm>, Unit, TTerm>
    where TContext : struct
    where TTerm : struct, ITerm<TTerm>
{
    public static TTerm Expand(
        in ContextRenderSpec<TContext, TTerm> props,
        in Unit state,
        in ExpandContext context)
    {
        var value = context.Use<TContext>();
        return props.Render(value).Term;
    }
}
