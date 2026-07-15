using BenchmarkDotNet.Attributes;

namespace Sia.Benchmarks;

[MemoryDiagnoser]
[BenchmarkCategory("ECS", "ComponentAccess")]
public class ComponentAccessBenchmarks
{
    private const int Operations = 4096;
    private World _world = null!;
    private Entity[] _entities = null!;
    private int[] _indices = null!;

    [Params(AccessPattern.Sequential, AccessPattern.Permuted)]
    public AccessPattern Pattern { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _world = BenchmarkWorld.Create(Operations);
        _entities = BenchmarkWorld.Snapshot(_world);
        _indices = Enumerable.Range(0, Operations).ToArray();
        if (Pattern == AccessPattern.Permuted) {
            for (var i = 0; i < Operations; i++) {
                _indices[i] = i * 4051 & (Operations - 1);
            }
        }
    }

    [GlobalCleanup]
    public void Cleanup() => _world.Dispose();

    [Benchmark(OperationsPerInvoke = Operations)]
    public float EntityGetByRef()
    {
        var sum = 0f;
        foreach (var index in _indices) {
            var entity = _entities[index];
            ref var position = ref entity.Get<Position>();
            position.X += 1;
            sum += position.X;
        }
        return sum;
    }
}
