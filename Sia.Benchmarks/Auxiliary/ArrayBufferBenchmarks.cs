using BenchmarkDotNet.Attributes;

namespace Sia.Benchmarks;

[MemoryDiagnoser]
[BenchmarkCategory("Auxiliary", "ArrayBuffer")]
public class ArrayBufferBenchmarks
{
    private const int LookupOperations = 4_096;
    private ArrayBuffer<int> _buffer = null!;
    private int[] _indices = null!;

    [Params(1_024, 65_536)]
    public int Count { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _buffer = new ArrayBuffer<int>(Count) { Count = Count };
        for (var i = 0; i < Count; i++) _buffer.GetRef(i) = i;
        _indices = Enumerable.Range(0, LookupOperations)
            .Select(i => i * 104729 % Count).ToArray();
    }

    [Benchmark]
    public long SequentialRead()
    {
        long sum = 0;
        foreach (var value in _buffer.AsSpan()[..Count]) sum += value;
        return sum;
    }

    [Benchmark(OperationsPerInvoke = LookupOperations)]
    public long PermutedRead()
    {
        long sum = 0;
        foreach (var index in _indices) sum += _buffer.GetRef(index);
        return sum;
    }

    [Benchmark]
    public int AllocateAndGrow()
    {
        var buffer = new ArrayBuffer<int> { Count = Count };
        buffer.GetRef(Count - 1) = Count;
        return buffer.Count;
    }
}
