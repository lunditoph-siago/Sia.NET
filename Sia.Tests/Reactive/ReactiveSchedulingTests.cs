namespace Sia.Tests.Reactive;

using global::Sia.Reactive;

public class ReactiveSchedulingTests(QueryTestHelpers helpers) : IClassFixture<QueryTestHelpers>
{
    private readonly record struct TestSchedule;
    private record struct TickCounter(int Value);

    private sealed class IncrementSystem() : SystemBase(Matchers.Of<TickCounter>())
    {
        public override void Execute(WorldContext context, IEntityQuery query)
            => query.ForSlice(static (ref TickCounter counter) => counter.Value++);
    }

    [SiaSystem]
    [SiaBefore<CyclicSecondSystem>]
    public sealed class CyclicFirstSystem : SystemBase
    {
        public static int InitializeCount;

        public override void Initialize(World world) => InitializeCount++;
    }

    [SiaSystem]
    [SiaBefore<CyclicFirstSystem>]
    public sealed class CyclicSecondSystem : SystemBase
    {
        public static int InitializeCount;

        public override void Initialize(World world) => InitializeCount++;
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
        var mount = reconciler.Mount(new ScheduledSpec());
        var registry = Assert.Single(reconciler.GetSchedules<TestSchedule>());

        scheduler.TickSchedule(registry.Label);

        Assert.Equal(1, counter.Get<TickCounter>().Value);
        Assert.Single(registry.CurrentPlan!.Entries);

        mount.Unmount();
        scheduler.TickSchedule(registry.Label);

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

        var output = helpers.FindSingle<ReactiveValue>(world);
        Assert.Equal(7, output.Get<ReactiveValue>().Value);
    }

    [Fact]
    public void FailedScheduleRebuildRemainsPendingUntilTheTreeIsCorrected()
    {
        using var world = new World();
        world.AcquireAddon<Reconciler>();
        CyclicFirstSystem.InitializeCount = 0;
        CyclicSecondSystem.InitializeCount = 0;
        var mount = world.Mount(
            (in bool includeCycle, ref Hooks _) => Reactive.Schedule(
                default(TestSchedule),
                Reactive.Group(
                    Reactive.System<CyclicFirstSystem>(),
                    Reactive.When(
                        includeCycle,
                        Reactive.System<CyclicSecondSystem>()))),
            false);

        Assert.Equal(1, CyclicFirstSystem.InitializeCount);
        Assert.Equal(0, CyclicSecondSystem.InitializeCount);

        mount.Update(true);
        Assert.Throws<SystemCycleException>(world.FlushReactive);
        Assert.Throws<SystemCycleException>(world.FlushReactive);
        Assert.Equal(0, CyclicSecondSystem.InitializeCount);

        mount.Update(false);
        world.FlushReactive();
        mount.Unmount();
    }
}
