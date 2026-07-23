namespace Sia.Reactive;

using System.Runtime.CompilerServices;

public delegate void ReactiveEventAction<TEvent, TCapture>(
    scoped in TEvent @event,
    scoped in TCapture capture)
    where TEvent : IEvent
    where TCapture : struct;

public readonly record struct EventActionTerm<TEvent, TCapture>(
    TCapture Capture,
    ReactiveEventAction<TEvent, TCapture> Action)
    : ITerm<EventActionTerm<TEvent, TCapture>>
    where TEvent : IEvent
    where TCapture : struct
{
    public static int SlotCount => 1;

    public static void Mount(
        in EventActionTerm<TEvent, TCapture> self,
        ref GraphContext context)
    {
        EnsureOutput(context.Output);
        var state = new EventActionState<TEvent, TCapture>(
            context.Reconciler,
            context.Output,
            self.Capture,
            self.Action);
        var reconciler = context.Reconciler;
        var node = reconciler.CreateNode(new EffectNode(state));
        try {
            state.ScheduleMount();
            context.SetSlot(node);
        }
        catch (Exception error) {
            Outcome<Exception>.Failure(error)
                .Attempt(() => reconciler.DestroySlot(node))
                .ThrowFailure();
        }
    }

    public static void Reconcile(
        in EventActionTerm<TEvent, TCapture> previous,
        in EventActionTerm<TEvent, TCapture> next,
        ref GraphContext context)
    {
        EnsureOutput(context.Output);
        var node = context.PeekSlot();
        if (node is not { IsValid: true }) {
            Mount(next, ref context);
            return;
        }
        var state = (EventActionState<TEvent, TCapture>)
            node.GetUnchecked<EffectNode>().Cleanup;
        state.Reconcile(context.Output, next.Capture, next.Action);
        context.Advance();
    }

    private static void EnsureOutput(Entity output)
    {
        if (!output.IsValid) {
            throw new InvalidOperationException(
                "Reactive.On action must be declared inside Reactive.Entity children.");
        }
    }
}

internal sealed class EventActionState<TEvent, TCapture>
    : IEffectCleanup, IEventListener<Entity>
    where TEvent : IEvent
    where TCapture : struct
{
    private readonly Reconciler _owner;
    private DeferredLifecycle _lifecycle;
    private Entity _target;
    private TCapture _capture;
    private ReactiveEventAction<TEvent, TCapture> _action;

    public EventActionState(
        Reconciler owner,
        Entity target,
        in TCapture capture,
        ReactiveEventAction<TEvent, TCapture> action)
    {
        _owner = owner;
        _target = target;
        _capture = capture;
        _action = action;
    }

    public void ScheduleMount()
    {
        var version = _lifecycle.NextVersion();
        _owner.QueueEffectSetup(() => Mount(version));
    }

    public void Reconcile(
        Entity target,
        in TCapture capture,
        ReactiveEventAction<TEvent, TCapture> action)
    {
        ThrowIfDisposed();
        _capture = capture;
        _action = action;
        if (_target == target) {
            return;
        }
        ScheduleCleanup();
        _target = target;
        ScheduleMount();
    }

    public bool OnEvent<TActual>(Entity target, in TActual @event)
        where TActual : IEvent
    {
        if (_lifecycle.Disposed
            || !_lifecycle.Mounted
            || target != _target
            || typeof(TActual) != typeof(TEvent)) {
            return false;
        }

        ref var actual = ref Unsafe.AsRef(in @event);
        _action(
            Unsafe.As<TActual, TEvent>(ref actual),
            _capture);
        return false;
    }

    public void Unmount()
    {
        if (!_lifecycle.TryBeginUnmount()) {
            return;
        }
        ScheduleCleanup(prepend: true);
    }

    private void Mount(long version)
    {
        if (_lifecycle.Mounted || !_lifecycle.IsCurrent(version)) {
            return;
        }
        _owner.World.Dispatcher.Listen(_target, this);
        _lifecycle.MarkMounted();
    }

    private void ScheduleCleanup(bool prepend = false)
    {
        if (!_lifecycle.TryBeginCleanup()) {
            return;
        }
        var target = _target;
        _owner.QueueEffectCleanup(
            () => _owner.World.Dispatcher.Unlisten(target, this),
            prepend);
    }

    private void ThrowIfDisposed()
    {
        if (_lifecycle.Disposed) {
            throw new InvalidOperationException(
                "Cannot reconcile an unmounted reactive event action.");
        }
    }
}
