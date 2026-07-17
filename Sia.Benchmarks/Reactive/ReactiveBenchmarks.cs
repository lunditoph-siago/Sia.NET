using BenchmarkDotNet.Attributes;
using Sia.Reactive;
using ScheduledSystems = Sia.Reactive.Group<
    Sia.Reactive.SystemTerm<Sia.Benchmarks.BenchmarkScheduledSystem<Sia.Benchmarks.ScheduleTag1>>,
    Sia.Reactive.SystemTerm<Sia.Benchmarks.BenchmarkScheduledSystem<Sia.Benchmarks.ScheduleTag2>>,
    Sia.Reactive.SystemTerm<Sia.Benchmarks.BenchmarkScheduledSystem<Sia.Benchmarks.ScheduleTag3>>,
    Sia.Reactive.SystemTerm<Sia.Benchmarks.BenchmarkScheduledSystem<Sia.Benchmarks.ScheduleTag4>>,
    Sia.Reactive.SystemTerm<Sia.Benchmarks.BenchmarkScheduledSystem<Sia.Benchmarks.ScheduleTag5>>,
    Sia.Reactive.SystemTerm<Sia.Benchmarks.BenchmarkScheduledSystem<Sia.Benchmarks.ScheduleTag6>>,
    Sia.Reactive.SystemTerm<Sia.Benchmarks.BenchmarkScheduledSystem<Sia.Benchmarks.ScheduleTag7>>,
    Sia.Reactive.SystemTerm<Sia.Benchmarks.BenchmarkScheduledSystem<Sia.Benchmarks.ScheduleTag8>>>;
using ValueList = Sia.HList<Sia.Benchmarks.BenchmarkReactiveValue, Sia.EmptyHList>;

namespace Sia.Benchmarks;

[MemoryDiagnoser]
[BenchmarkCategory("Prelude", "Reactive", "Lifecycle")]
public class ReactiveLifecycleBenchmarks
{
    private const int LifecycleOperations = 128;
    private const int UpdateOperations = 4_096;

    private World _world = null!;
    private Reconciler _reconciler = null!;
    private MountHandle<BenchmarkReactiveSpec> _mount;
    private int _value;

    [GlobalSetup]
    public void Setup()
    {
        _world = new World();
        _reconciler = _world.AcquireAddon<Reconciler>();
        _mount = _reconciler.Mount(new BenchmarkReactiveSpec(0));
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        if (_mount.IsMounted) {
            _mount.Unmount();
        }
        _world.Dispose();
    }

    [Benchmark(OperationsPerInvoke = LifecycleOperations)]
    public int MountAndUnmount()
    {
        for (var i = 0; i < LifecycleOperations; i++) {
            var mount = _reconciler.Mount(new BenchmarkReactiveSpec(i));
            mount.Unmount();
        }
        return _world.Count;
    }

    [Benchmark(OperationsPerInvoke = UpdateOperations)]
    public int UpdateAndFlush()
    {
        for (var i = 0; i < UpdateOperations; i++) {
            _mount.Update(new BenchmarkReactiveSpec(++_value));
            _reconciler.Flush();
        }
        return _world.Count;
    }
}

[MemoryDiagnoser]
[BenchmarkCategory("Prelude", "Reactive", "ForEach")]
public class ReactiveForEachBenchmarks
{
    private World _world = null!;
    private Reconciler _reconciler = null!;
    private Keyed<int, BenchmarkItemSpec>[] _first = null!;
    private Keyed<int, BenchmarkItemSpec>[] _second = null!;
    private MountHandle<BenchmarkListSpec> _mount;
    private bool _useSecond;

    [Params(16, 1_024)]
    public int ItemCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _world = new World();
        _reconciler = _world.AcquireAddon<Reconciler>();
        _first = new Keyed<int, BenchmarkItemSpec>[ItemCount];
        _second = new Keyed<int, BenchmarkItemSpec>[ItemCount];
        for (var i = 0; i < ItemCount; i++) {
            _first[i] = Term.Keyed(i, new BenchmarkItemSpec(i, i));

            var reversedKey = ItemCount - i - 1;
            _second[i] = Term.Keyed(
                reversedKey,
                new BenchmarkItemSpec(reversedKey, reversedKey + 1));
        }
        _mount = _reconciler.Mount(new BenchmarkListSpec(_first));
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        if (_mount.IsMounted) {
            _mount.Unmount();
        }
        _world.Dispose();
    }

    [Benchmark]
    public int ReconcileKeyedItems()
    {
        _useSecond = !_useSecond;
        _mount.Update(new BenchmarkListSpec(_useSecond ? _second : _first));
        _reconciler.Flush();
        return _world.Count;
    }

    [Benchmark]
    public int MountAndUnmountKeyedItems()
    {
        var mount = _reconciler.Mount(new BenchmarkListSpec(_first));
        mount.Unmount();
        return _world.Count;
    }
}

[MemoryDiagnoser]
[BenchmarkCategory("Prelude", "Reactive", "Scheduling")]
public class ReactiveScheduleBenchmarks
{
    private World _world = null!;
    private Reconciler _reconciler = null!;
    private Scheduler _scheduler = null!;
    private ScheduleRegistry _registry = null!;
    private MountHandle<BenchmarkScheduledSpec<BenchmarkPersistentSchedule>> _mount;

    [GlobalSetup]
    public void Setup()
    {
        _world = new World();
        _reconciler = _world.AcquireAddon<Reconciler>();
        _scheduler = _world.GetAddon<Scheduler>();
        _mount = _reconciler.Mount(
            new BenchmarkScheduledSpec<BenchmarkPersistentSchedule>());
        _registry = _reconciler.GetSchedules<BenchmarkPersistentSchedule>().Single();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        if (_mount.IsMounted) {
            _mount.Unmount();
        }
        _world.Dispose();
    }

    [Benchmark]
    public int TickEightSystems()
    {
        _scheduler.TickSchedule(_registry.Label);
        return _registry.Version;
    }

    [Benchmark]
    public int MountAndUnmountEightSystems()
    {
        var mount = _reconciler.Mount(
            new BenchmarkScheduledSpec<BenchmarkTransientSchedule>());
        mount.Unmount();
        return _world.Count;
    }
}

internal readonly record struct BenchmarkReactiveValue(int Key, int Value);

internal readonly record struct BenchmarkReactiveSpec(int Value)
    : ISpec<BenchmarkReactiveSpec, Unit, EntityTerm<ValueList, UnitTerm>>
{
    public static EntityTerm<ValueList, UnitTerm> Expand(
        in BenchmarkReactiveSpec props,
        in Unit state,
        in ExpandContext context)
        => Term.Entity(HList.From(new BenchmarkReactiveValue(0, props.Value)));
}

internal readonly record struct BenchmarkListSpec(
    ReadOnlyMemory<Keyed<int, BenchmarkItemSpec>> Items)
    : ISpec<BenchmarkListSpec, Unit, ForEachTerm<int, BenchmarkItemSpec>>
{
    public static ForEachTerm<int, BenchmarkItemSpec> Expand(
        in BenchmarkListSpec props,
        in Unit state,
        in ExpandContext context)
        => Term.ForEach<int, BenchmarkItemSpec>(props.Items);
}

internal readonly record struct BenchmarkItemSpec(int Key, int Value)
    : ISpec<BenchmarkItemSpec, Unit, EntityTerm<ValueList, UnitTerm>>
{
    public static EntityTerm<ValueList, UnitTerm> Expand(
        in BenchmarkItemSpec props,
        in Unit state,
        in ExpandContext context)
        => Term.Entity(HList.From(new BenchmarkReactiveValue(props.Key, props.Value)));
}

internal readonly record struct BenchmarkScheduledSpec<TLabel>
    : ISpec<BenchmarkScheduledSpec<TLabel>, Unit, ScheduleTerm<TLabel, ScheduledSystems>>
    where TLabel : struct
{
    public static ScheduleTerm<TLabel, ScheduledSystems> Expand(
        in BenchmarkScheduledSpec<TLabel> props,
        in Unit state,
        in ExpandContext context)
        => Term.Schedule(default(TLabel), Term.Group(
            Term.System<BenchmarkScheduledSystem<ScheduleTag1>>(),
            Term.System<BenchmarkScheduledSystem<ScheduleTag2>>(),
            Term.System<BenchmarkScheduledSystem<ScheduleTag3>>(),
            Term.System<BenchmarkScheduledSystem<ScheduleTag4>>(),
            Term.System<BenchmarkScheduledSystem<ScheduleTag5>>(),
            Term.System<BenchmarkScheduledSystem<ScheduleTag6>>(),
            Term.System<BenchmarkScheduledSystem<ScheduleTag7>>(),
            Term.System<BenchmarkScheduledSystem<ScheduleTag8>>()));
}

internal sealed class BenchmarkScheduledSystem<TTag>() : SystemBase(Matchers.Any)
{
    public override void Execute(World world, IEntityQuery query) {}
}

internal readonly record struct BenchmarkPersistentSchedule;
internal readonly record struct BenchmarkTransientSchedule;
internal readonly record struct ScheduleTag1;
internal readonly record struct ScheduleTag2;
internal readonly record struct ScheduleTag3;
internal readonly record struct ScheduleTag4;
internal readonly record struct ScheduleTag5;
internal readonly record struct ScheduleTag6;
internal readonly record struct ScheduleTag7;
internal readonly record struct ScheduleTag8;
