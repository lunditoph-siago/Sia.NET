namespace Sia.Reactive;

public readonly record struct DeferredEffectTerm<TDependencies, TResource>(
    TDependencies Dependencies,
    ReactiveEffectSetup<TDependencies, TResource> Setup,
    ReactiveEffectCleanup<TResource> Cleanup)
    : ITerm<DeferredEffectTerm<TDependencies, TResource>>
    where TDependencies : struct, IEquatable<TDependencies>
{
    public static int SlotCount => 1;

    public static void Mount(
        in DeferredEffectTerm<TDependencies, TResource> self,
        ref GraphContext context)
    {
        var reconciler = context.Reconciler;
        var state = new DeferredEffectState(
            reconciler,
            self.Dependencies,
            self.Setup,
            self.Cleanup);
        var node = reconciler.CreateNode(new EffectNode(state));
        try {
            state.ScheduleSetup();
            context.SetSlot(node);
        }
        catch (Exception error) {
            Outcome<Exception>.Failure(error)
                .Attempt(() => reconciler.DestroySlot(node))
                .ThrowFailure();
        }
    }

    public static void Reconcile(
        in DeferredEffectTerm<TDependencies, TResource> previous,
        in DeferredEffectTerm<TDependencies, TResource> next,
        ref GraphContext context)
    {
        var node = context.PeekSlot();
        if (node is not { IsValid: true }) {
            Mount(next, ref context);
            return;
        }

        var state = (DeferredEffectState)
            node.GetUnchecked<EffectNode>().Cleanup;
        state.Reconcile(next.Dependencies, next.Setup, next.Cleanup);
        context.Advance();
    }

    private sealed class DeferredEffectState : IEffectCleanup
    {
        private readonly Reconciler _owner;
        private TDependencies _dependencies;
        private ReactiveEffectSetup<TDependencies, TResource> _setup;
        private ReactiveEffectCleanup<TResource> _cleanup;
        private TResource _resource = default!;
        private long _version;
        private bool _mounted;
        private bool _disposed;
        private bool _cleanupFailed;

        public DeferredEffectState(
            Reconciler owner,
            in TDependencies dependencies,
            ReactiveEffectSetup<TDependencies, TResource> setup,
            ReactiveEffectCleanup<TResource> cleanup)
        {
            _owner = owner;
            _dependencies = dependencies;
            _setup = setup;
            _cleanup = cleanup;
        }

        public void ScheduleSetup()
        {
            var version = ++_version;
            _owner.QueueEffectSetup(() => Setup(version));
        }

        public void Reconcile(
            in TDependencies dependencies,
            ReactiveEffectSetup<TDependencies, TResource> setup,
            ReactiveEffectCleanup<TResource> cleanup)
        {
            if (_disposed) {
                throw new InvalidOperationException(
                    "Cannot reconcile an unmounted reactive effect.");
            }
            if (EqualityComparer<TDependencies>.Default.Equals(
                    _dependencies, dependencies)) {
                return;
            }

            ScheduleCleanup();
            _dependencies = dependencies;
            _setup = setup;
            _cleanup = cleanup;
            ScheduleSetup();
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

        private void Setup(long version)
        {
            if (_disposed || _mounted || _cleanupFailed || version != _version) {
                return;
            }
            var resource = _setup(_dependencies);
            if (_disposed || version != _version) {
                var cleanup = _cleanup;
                _owner.QueueEffectCleanup(() => cleanup(resource));
                return;
            }
            _resource = resource;
            _mounted = true;
        }

        private void ScheduleCleanup(bool prepend = false)
        {
            if (!_mounted) {
                return;
            }
            var resource = _resource;
            var cleanup = _cleanup;
            _resource = default!;
            _mounted = false;
            _owner.QueueEffectCleanup(
                () => Cleanup(cleanup, resource),
                prepend);
        }

        private void Cleanup(
            ReactiveEffectCleanup<TResource> cleanup,
            TResource resource)
        {
            try {
                cleanup(resource);
            }
            catch {
                _cleanupFailed = true;
                throw;
            }
        }
    }
}
