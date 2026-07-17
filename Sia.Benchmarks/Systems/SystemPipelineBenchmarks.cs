using BenchmarkDotNet.Attributes;

namespace Sia.Benchmarks;

[MemoryDiagnoser]
[ThreadingDiagnoser]
[BenchmarkCategory("Systems", "Continuous")]
public class ContinuousSystemPipelineBenchmarks
{
    private World _world = null!;
    private SystemStage _stage = null!;
    private Entity _first;

    [Params(1_024, 65_536)]
    public int EntityCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _world = BenchmarkWorld.Create(EntityCount);
        _first = BenchmarkWorld.Snapshot(_world)[0];
        _stage = SystemChain.Empty.Add<MovementSystem>().CreateStage(_world);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _stage.Dispose();
        _world.Dispose();
    }

    [Benchmark]
    public float StageTick()
    {
        _stage.Tick();
        return _first.Get<Position>().X;
    }
}

[MemoryDiagnoser]
[ThreadingDiagnoser]
[BenchmarkCategory("Systems", "Reactive", "EndToEnd")]
public class ReactiveSystemPipelineBenchmarks
{
    private World _world = null!;
    private SystemStage _stage = null!;
    private Entity[] _entities = null!;

    [Params(1_024, 65_536)]
    public int EntityCount { get; set; }

    [Params(1, 16, 256)]
    public int EventStride { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _world = BenchmarkWorld.Create(EntityCount);
        _entities = BenchmarkWorld.Snapshot(_world);
        _stage = SystemChain.Empty.Add<ReactiveMovementSystem>().CreateStage(_world);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _stage.Dispose();
        _world.Dispose();
    }

    [Benchmark]
    public float DispatchSubsetThenStageTick()
    {
        for (var i = 0; i < _entities.Length; i += EventStride) {
            _world.Send(_entities[i], new BenchmarkEvent(1));
        }
        _stage.Tick();
        return _entities[0].Get<Position>().X;
    }
}

internal sealed class MovementSystem() : SystemBase(Matchers.Of<Position, Velocity>())
{
    public override void Execute(World world, IEntityQuery query)
        => query.ForSlice(static (ref Position position, ref Velocity velocity) => {
            position.X += velocity.X;
        });
}

internal sealed class ReactiveMovementSystem() : SystemBase(
    Matchers.Of<Position, Velocity>(), EventUnion.Of<BenchmarkEvent>())
{
    public override void Execute(World world, IEntityQuery query)
        => query.ForSlice(static (ref Position position, ref Velocity velocity) => {
            position.X += velocity.X;
        });
}
