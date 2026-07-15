namespace Sia.Tests.Reactive;

using global::Sia.Reactive;

public class ReactiveSchedulingTests
{
    private readonly record struct TestSchedule;
    private record struct TickCounter(int Value);

    private sealed class IncrementSystem() : SystemBase(Matchers.Of<TickCounter>())
    {
        public override void Execute(World world, IEntityQuery query)
            => query.ForSlice(static (ref TickCounter counter) => counter.Value++);
    }

    private readonly record struct ScheduledSpec
        : ISpec<ScheduledSpec, int, ScheduleTerm<TestSchedule, SystemTerm<IncrementSystem>>>
    {
        public static ScheduleTerm<TestSchedule, SystemTerm<IncrementSystem>> Expand(
            in ScheduledSpec props,
            in int state,
            in ExpandContext context)
            => Term.Schedule(new TestSchedule(), Term.System<IncrementSystem>());
    }

    [Fact]
    public void ScheduleTerm_RegistersExecutesAndDetachesItsSystems()
    {
        using var world = new World();
        var counter = world.Create(HList.From(new TickCounter()));
        var reconciler = world.AcquireAddon<Reconciler>();
        var scheduler = world.GetAddon<Scheduler>();
        var label = new ScheduleLabel(typeof(TestSchedule).FullName!);
        var mount = reconciler.Mount(new ScheduledSpec());

        scheduler.TickSchedule(label);

        Assert.Equal(1, counter.Get<TickCounter>().Value);
        var registry = Assert.Single(reconciler.GetSchedules<TestSchedule>());
        Assert.Single(registry.CurrentPlan!.Entries);

        mount.Unmount();
        scheduler.TickSchedule(label);

        Assert.Equal(1, counter.Get<TickCounter>().Value);
        Assert.Empty(reconciler.GetSchedules<TestSchedule>());
    }

    [Fact]
    public void SchedulerFlushesReactiveStateBeforeEnteringASchedule()
    {
        using var world = new World();
        var reconciler = world.AcquireAddon<Reconciler>();
        var scheduler = world.GetAddon<Scheduler>();
        var probe = new LifecycleProbe();
        reconciler.Mount(new LifecycleSpec(probe));
        probe.State.Set(7);

        scheduler.TickSchedule(new("unregistered"));

        using var query = world.Query(Matchers.Of<ReactiveValue>());
        var output = Assert.Single(query.Hosts.SelectMany(static host => host));
        Assert.Equal(7, output.Get<ReactiveValue>().Value);
    }
}
