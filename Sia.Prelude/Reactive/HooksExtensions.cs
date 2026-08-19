namespace Sia.Reactive;

using Sia.Reactors;

public delegate void ReactiveQueryAction<TCapture>(
    Entity entity,
    scoped in TCapture capture)
    where TCapture : struct;

public delegate void ReactiveGlobalEventAction<TEvent, TCapture>(
    Entity target,
    scoped in TEvent @event,
    scoped in TCapture capture)
    where TEvent : IEvent
    where TCapture : struct;

public static class HooksExtensions
{
    private sealed class QueryActionState<TCapture>
        where TCapture : struct
    {
        public TCapture Capture;
        public ReactiveQueryAction<TCapture> OnAdded = null!;
        public ReactiveQueryAction<TCapture> OnRemoved = null!;
    }

    private readonly record struct QueryDependencies<TCapture>(
        World World,
        HookRef<QueryActionState<TCapture>> State)
        where TCapture : struct;

    public static void UseQuery<TTypeUnion, TCapture>(
        this ref Hooks hooks,
        scoped in TCapture capture,
        [NestedCallback] ReactiveQueryAction<TCapture> onAdded,
        [NestedCallback] ReactiveQueryAction<TCapture> onRemoved)
        where TTypeUnion : ITypeUnion, new()
        where TCapture : struct
    {
        ArgumentNullException.ThrowIfNull(onAdded);
        ArgumentNullException.ThrowIfNull(onRemoved);

        var state = hooks.UseRef(static () => new QueryActionState<TCapture>());
        state.Value.Capture = capture;
        state.Value.OnAdded = onAdded;
        state.Value.OnRemoved = onRemoved;

        hooks.UseEffect(
            new QueryDependencies<TCapture>(hooks.World, state),
            static (in QueryDependencies<TCapture> deps) => {
                var world = deps.World;
                var state = deps.State;
                return new QuerySubscription(
                    world.Query<TTypeUnion>(),
                    entity => {
                        var s = state.Value;
                        s.OnAdded(entity, s.Capture);
                    },
                    entity => {
                        var s = state.Value;
                        s.OnRemoved(entity, s.Capture);
                    });
            },
            static (in QuerySubscription subscription) => subscription.Dispose());
    }

    private sealed class GlobalEventActionState<TEvent, TCapture>
        where TEvent : IEvent
        where TCapture : struct
    {
        public TCapture Capture;
        public ReactiveGlobalEventAction<TEvent, TCapture> Action = null!;
    }

    private readonly record struct GlobalEventDependencies<TEvent, TCapture>(
        World World,
        HookRef<GlobalEventActionState<TEvent, TCapture>> State)
        where TEvent : IEvent
        where TCapture : struct;

    private readonly record struct GlobalEventSubscription<TEvent>(
        World World,
        WorldDispatcher.Listener<TEvent> Listener)
        where TEvent : IEvent;

    public static void UseEvent<TEvent, TCapture>(
        this ref Hooks hooks,
        scoped in TCapture capture,
        [NestedCallback] ReactiveGlobalEventAction<TEvent, TCapture> action)
        where TEvent : IEvent
        where TCapture : struct
    {
        ArgumentNullException.ThrowIfNull(action);

        var state = hooks.UseRef(static () => new GlobalEventActionState<TEvent, TCapture>());
        state.Value.Capture = capture;
        state.Value.Action = action;

        hooks.UseEffect(
            new GlobalEventDependencies<TEvent, TCapture>(hooks.World, state),
            static (in GlobalEventDependencies<TEvent, TCapture> deps) => {
                var world = deps.World;
                var state = deps.State;
                bool Listener(Entity target, in TEvent e)
                {
                    var s = state.Value;
                    s.Action(target, e, s.Capture);
                    return false;
                }
                world.Dispatcher.Listen<TEvent>(Listener);
                return new GlobalEventSubscription<TEvent>(world, Listener);
            },
            static (in GlobalEventSubscription<TEvent> subscription) =>
                subscription.World.Dispatcher.Unlisten(subscription.Listener));
    }
}
