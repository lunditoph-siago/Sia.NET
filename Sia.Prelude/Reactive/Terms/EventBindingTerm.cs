namespace Sia.Reactive;

using System.Runtime.CompilerServices;

public readonly record struct EventBindingTerm<TEvent, TMessage>(
    ReactiveEventHandler<TEvent, TMessage> Handler)
    : ITerm<EventBindingTerm<TEvent, TMessage>>
    where TEvent : IEvent
{
    public static int SlotCount => 1;

    public static void Mount(
        in EventBindingTerm<TEvent, TMessage> self,
        ref GraphContext context)
    {
        if (!context.Output.IsValid) {
            throw new InvalidOperationException(
                "Reactive.On must be declared inside Reactive.Entity children.");
        }

        var reconciler = context.Reconciler;
        var messageOwner = context.MessageOwner;
        if (!messageOwner.IsValid) {
            throw new InvalidOperationException(
                "Reactive.On requires a functional component reducer.");
        }
        var identity = messageOwner.GetUnchecked<Cell>().Identity;
        var state = new EventBindingState(
            reconciler,
            messageOwner,
            identity,
            context.Output,
            self.Handler);
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
        in EventBindingTerm<TEvent, TMessage> previous,
        in EventBindingTerm<TEvent, TMessage> next,
        ref GraphContext context)
    {
        if (!context.Output.IsValid) {
            throw new InvalidOperationException(
                "Reactive.On must be declared inside Reactive.Entity children.");
        }
        var node = context.PeekSlot();
        if (node is not { IsValid: true }) {
            Mount(next, ref context);
            return;
        }
        var state = (EventBindingState)
            node.GetUnchecked<EffectNode>().Cleanup;
        state.Reconcile(context.Output, next.Handler);
        context.Advance();
    }

    private sealed class EventBindingState
        : IEffectCleanup, IEventListener<Entity>
    {
        private readonly Reconciler _owner;
        private readonly EntityReference _cell;
        private readonly NodeIdentity _identity;
        private Entity _target;
        private ReactiveEventHandler<TEvent, TMessage> _handler;
        private long _version;
        private bool _mounted;
        private bool _disposed;

        public EventBindingState(
            Reconciler owner,
            Entity cell,
            NodeIdentity identity,
            Entity target,
            ReactiveEventHandler<TEvent, TMessage> handler)
        {
            _owner = owner;
            _cell = new(cell);
            _identity = identity;
            _target = target;
            _handler = handler;
        }

        public void ScheduleMount()
        {
            var version = ++_version;
            _owner.QueueEffectSetup(() => Mount(version));
        }

        public void Reconcile(
            Entity target,
            ReactiveEventHandler<TEvent, TMessage> handler)
        {
            if (_disposed) {
                throw new InvalidOperationException(
                    "Cannot reconcile an unmounted event binding.");
            }
            _handler = handler;
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
            if (_disposed
                || !_mounted
                || target != _target
                || typeof(TActual) != typeof(TEvent)
                || !_cell.TryGet(out var cell)
                || !_owner.IsCell(cell, _identity)) {
                return false;
            }

            ref var actual = ref Unsafe.AsRef(in @event);
            var message = _handler(Unsafe.As<TActual, TEvent>(ref actual))
                ?? throw new InvalidOperationException(
                    "A reactive event handler returned a null message.");
            _owner.DispatchMessage(cell, _identity, message);
            return false;
        }

        public void Unmount()
        {
            if (_disposed) {
                return;
            }
            _disposed = true;
            _version++;
            ScheduleCleanup(prepend: true);
        }

        private void Mount(long version)
        {
            if (_disposed || _mounted || version != _version) {
                return;
            }
            _owner.World.Dispatcher.Listen(_target, this);
            _mounted = true;
        }

        private void ScheduleCleanup(bool prepend = false)
        {
            if (!_mounted) {
                return;
            }
            var target = _target;
            _mounted = false;
            _owner.QueueEffectCleanup(
                () => _owner.World.Dispatcher.Unlisten(target, this),
                prepend);
        }
    }
}
