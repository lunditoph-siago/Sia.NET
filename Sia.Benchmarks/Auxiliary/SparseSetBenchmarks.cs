using BenchmarkDotNet.Attributes;

namespace Sia.Benchmarks;

[MemoryDiagnoser]
[BenchmarkCategory("Auxiliary", "SparseSet")]
public class SparseSetBenchmarks
{
    private const int LookupOperations = 4_096;
    private SparseSet<int> _set = null!;
    private int[] _hitKeys = null!;
    private int[] _missKeys = null!;

    [Params(1_024, 65_536)]
    public int Count { get; set; }

    [Params(256, 257)]
    public int PageSize { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _set = new SparseSet<int>(PageSize);
        for (var i = 0; i < Count; i++) _set[i * 4] = i;
        _hitKeys = Enumerable.Range(0, LookupOperations)
            .Select(i => (i * 104729 % Count) * 4).ToArray();
        _missKeys = _hitKeys.Select(key => key + 1).ToArray();
    }

    [GlobalCleanup]
    public void Cleanup() => _set.Clear();

    [Benchmark(OperationsPerInvoke = LookupOperations)]
    public long LookupHit()
    {
        long sum = 0;
        foreach (var key in _hitKeys) {
            if (_set.TryGetValue(key, out var value)) sum += value;
        }
        return sum;
    }

    [Benchmark(OperationsPerInvoke = LookupOperations)]
    public int LookupMiss()
    {
        var found = 0;
        foreach (var key in _missKeys) found += _set.ContainsKey(key) ? 1 : 0;
        return found;
    }

    [Benchmark(OperationsPerInvoke = LookupOperations)]
    public int RemoveThenAdd()
    {
        var removed = 0;
        foreach (var key in _hitKeys) {
            if (_set.Remove(key, out var value)) {
                _set.Add(key, value);
                removed++;
            }
        }
        return removed;
    }

    [Benchmark]
    public long EnumerateDenseValues()
    {
        long sum = 0;
        foreach (var value in _set.ValueSpan) sum += value;
        return sum;
    }
}
