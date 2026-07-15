using BenchmarkDotNet.Attributes;
using Sia.Reactors;

namespace Sia.Benchmarks;

[MemoryDiagnoser]
[BenchmarkCategory("Prelude", "Mapper")]
public class MapperBenchmarks
{
    private const int LookupOperations = 4096;
    private World _world = null!;
    private Mapper<int> _mapper = null!;
    private int[] _keys = null!;
    private Entity[] _entities = null!;

    [Params(1_024, 65_536)]
    public int EntityCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _world = new World();
        Context<World>.Current = _world;
        _mapper = _world.AcquireAddon<Mapper<int>>();
        _entities = new Entity[EntityCount];
        for (var i = 0; i < EntityCount; i++) {
            var entity = _world.Create(HList.From(Sid.From(i)));
            _entities[i] = entity;
        }
        _keys = Enumerable.Range(0, LookupOperations)
            .Select(i => i * 104729 % EntityCount).ToArray();
        if (_mapper.Count != EntityCount) {
            throw new InvalidOperationException("Mapper did not observe all fixture entities.");
        }
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        if (ReferenceEquals(Context<World>.Current, _world)) {
            Context<World>.Current = null;
        }
        _world.Dispose();
    }

    [Benchmark(OperationsPerInvoke = LookupOperations)]
    [BenchmarkCategory("Lookup")]
    public int MapperLookup()
    {
        var checksum = 0;
        foreach (var key in _keys) checksum ^= _mapper[key].Slot;
        return checksum;
    }

    [Benchmark(OperationsPerInvoke = LookupOperations * 2)]
    [BenchmarkCategory("Update", "EndToEnd")]
    public int ChangeIdentifierRoundTrip()
    {
        for (var i = 0; i < LookupOperations; i++) {
            var entity = _entities[i % EntityCount];
            entity.SetSid(EntityCount + i);
            entity.SetSid(i % EntityCount);
        }
        return _mapper.Count;
    }
}
