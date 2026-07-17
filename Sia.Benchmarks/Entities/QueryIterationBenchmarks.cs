using BenchmarkDotNet.Attributes;

namespace Sia.Benchmarks;

[MemoryDiagnoser]
[BenchmarkCategory("ECS", "Query")]
public class QueryIterationBenchmarks
{
    private World _world = null!;
    private IReactiveEntityQuery _query = null!;
    private Entity _first;

    [Params(1_024, 65_536)]
    public int EntityCount { get; set; }

    [Params(1, 4)]
    public int ArchetypeCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _world = BenchmarkWorld.Create(EntityCount, ArchetypeCount);
        _query = _world.Query(Matchers.Of<Position, Velocity>());
        _first = BenchmarkWorld.Snapshot(_world)[0];
        if (_query.Count != EntityCount) {
            throw new InvalidOperationException("Fixture does not match the requested entity count.");
        }
    }

    [GlobalCleanup]
    public void Cleanup() => _world.Dispose();

    [Benchmark]
    public float EntityEnumeration()
    {
        var sum = 0f;
        foreach (var entity in _query) {
            ref var position = ref entity.Get<Position>();
            ref var velocity = ref entity.Get<Velocity>();
            position.X += velocity.X;
            sum += position.X;
        }
        return sum;
    }

    [Benchmark]
    public float SliceIteration()
    {
        _query.ForSlice(static (ref Position position, ref Velocity velocity) => {
            position.X += velocity.X;
        });
        return _first.Get<Position>().X;
    }

}
