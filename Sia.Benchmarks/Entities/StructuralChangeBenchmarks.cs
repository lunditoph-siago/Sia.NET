using BenchmarkDotNet.Attributes;

namespace Sia.Benchmarks;

[MemoryDiagnoser]
[BenchmarkCategory("ECS", "StructuralChange")]
public class StructuralChangeBenchmarks
{
    private const int BatchSize = 1_048_576;
    private World _world = null!;
    private Entity[] _entities = null!;

    [IterationSetup]
    public void Setup()
    {
        _world = BenchmarkWorld.Create(BatchSize);
        _entities = BenchmarkWorld.Snapshot(_world);
    }

    [IterationCleanup]
    public void Cleanup() => _world.Dispose();

    [Benchmark(OperationsPerInvoke = BatchSize)]
    public int CreateEntityInWarmArchetype()
    {
        for (var i = 0; i < BatchSize; i++) {
            _world.Create(HList.From(
                new Position(i, i + 1, i + 2),
                new Velocity(1, 2, 3),
                new Padding1()));
        }
        return _world.Count;
    }

    [Benchmark(OperationsPerInvoke = BatchSize)]
    public int AddComponent()
    {
        foreach (var entity in _entities) entity.Add(new Health(100));
        return _world.Count;
    }

    [Benchmark(OperationsPerInvoke = BatchSize)]
    public int DestroyEntity()
    {
        foreach (var entity in _entities) entity.Destroy();
        return _world.Count;
    }
}

[MemoryDiagnoser]
[BenchmarkCategory("ECS", "StructuralChange", "RoundTrip")]
public class StructuralRoundTripBenchmarks
{
    private const int BatchSize = 4_096;
    private World _world = null!;
    private Entity[] _entities = null!;

    [GlobalSetup]
    public void Setup()
    {
        _world = BenchmarkWorld.Create(BatchSize);
        _entities = BenchmarkWorld.Snapshot(_world);
    }

    [GlobalCleanup]
    public void Cleanup() => _world.Dispose();

    [Benchmark(OperationsPerInvoke = BatchSize * 2)]
    public int AddThenRemoveComponent()
    {
        foreach (var entity in _entities) entity.Add(new Health(100));
        foreach (var entity in _entities) entity.Remove<Health>();
        return _world.Count;
    }
}
