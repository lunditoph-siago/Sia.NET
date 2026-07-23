namespace Sia.Reactive;

internal sealed class EffectLifetime<TDependencies, TResource>
    : IEffectCleanup
    where TDependencies : struct, IEquatable<TDependencies>
{
    private readonly Reconciler _owner;
    private DeferredLifecycle _lifecycle;
    private TDependencies _dependencies;
    private ReactiveEffectSetup<TDependencies, TResource> _setup;
    private ReactiveEffectCleanup<TResource> _cleanup;
    private TResource _resource = default!;
    private bool _cleanupFailed;

    public EffectLifetime(
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
        var version = _lifecycle.NextVersion();
        _owner.QueueEffectSetup(() => Setup(version));
    }

    public void Reconcile(
        in TDependencies dependencies,
        ReactiveEffectSetup<TDependencies, TResource> setup,
        ReactiveEffectCleanup<TResource> cleanup)
    {
        if (_lifecycle.Disposed) {
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
        if (!_lifecycle.TryBeginUnmount()) {
            return;
        }
        ScheduleCleanup(prepend: true);
    }

    private void Setup(long version)
    {
        if (_cleanupFailed || _lifecycle.Mounted || !_lifecycle.IsCurrent(version)) {
            return;
        }
        var resource = _setup(_dependencies);
        if (!_lifecycle.IsCurrent(version)) {
            var cleanup = _cleanup;
            _owner.QueueEffectCleanup(() => cleanup(resource));
            return;
        }
        _resource = resource;
        _lifecycle.MarkMounted();
    }

    private void ScheduleCleanup(bool prepend = false)
    {
        if (!_lifecycle.TryBeginCleanup()) {
            return;
        }
        var resource = _resource;
        var cleanup = _cleanup;
        _resource = default!;
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
