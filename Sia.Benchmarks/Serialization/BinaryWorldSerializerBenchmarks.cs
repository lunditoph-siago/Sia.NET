using System.Buffers;
using BenchmarkDotNet.Attributes;
using Sia.Serialization.Binary;

namespace Sia.Benchmarks;

[MemoryDiagnoser]
[BenchmarkCategory("Serialization", "EndToEnd")]
public class BinaryWorldSerializerBenchmarks
{
    private World _world = null!;
    private byte[] _payload = null!;

    [Params(1_024, 16_384)]
    public int EntityCount { get; set; }

    [Params(1, 3, 6)]
    public int ComponentCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _world = new World();
        for (var i = 0; i < EntityCount; i++) {
            switch (ComponentCount) {
                case 1:
                    _world.Create(HList.From(i));
                    break;
                case 3:
                    _world.Create(HList.From(i, (float)i, (double)i));
                    break;
                case 6:
                    _world.Create(HList.From(
                        i, (float)i, (double)i, (long)i, (short)i, (byte)i));
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(ComponentCount));
            }
        }
        var writer = new ArrayBufferWriter<byte>();
        BinaryWorldSerializer.Serialize(ref writer, _world);
        _payload = writer.WrittenSpan.ToArray();

        var sequence = new ReadOnlySequence<byte>(_payload);
        using var validationWorld = new World();
        BinaryWorldSerializer.Deserialize(ref sequence, validationWorld);
        if (validationWorld.Count != EntityCount || sequence.Length != 0) {
            throw new InvalidOperationException("Serialization fixture failed to round-trip.");
        }
    }

    [GlobalCleanup]
    public void Cleanup() => _world.Dispose();

    [Benchmark]
    [BenchmarkCategory("Serialize")]
    public int SerializeWorld()
    {
        var writer = new ArrayBufferWriter<byte>();
        BinaryWorldSerializer.Serialize(ref writer, _world);
        return writer.WrittenCount;
    }

    [Benchmark]
    [BenchmarkCategory("Deserialize")]
    public int DeserializeWorld()
    {
        var sequence = new ReadOnlySequence<byte>(_payload);
        using var world = new World();
        BinaryWorldSerializer.Deserialize(ref sequence, world);
        return world.Count;
    }
}
