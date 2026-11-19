using System.Buffers;
using MemoryPack;
using Sia.Serialization.Binary;

namespace Sia.Tests.Serialization;

[MemoryPackable]
public partial record struct SerializedEntityLink(Entity? Target);

public class EntityHandleSerializationTests
{
    private sealed class CustomHost<T> : ArrayEntityHost<T> where T : struct, IHList;

    [Fact]
    public void BinaryWorldSerializer_RoundTripsCustomInnerHost()
    {
        using var source = new World();
        source.AcquireHost<HList<int, EmptyHList>, CustomHost<HList<int, EmptyHList>>>().Create(HList.From(42));
        var writer = new ArrayBufferWriter<byte>();
        BinaryWorldSerializer.Serialize(ref writer, source);

        using var restored = new World();
        var sequence = new ReadOnlySequence<byte>(writer.WrittenMemory);
        BinaryWorldSerializer.Deserialize(ref sequence, restored);

        var host = Assert.IsType<WorldEntityHost<HList<int, EmptyHList>, CustomHost<HList<int, EmptyHList>>>>(
            Assert.Single(restored.Hosts));
        Assert.Equal(42, Assert.Single(host).Get<int>());
        Assert.Equal(1, restored.Count);
        Assert.Equal(0, sequence.Length);
    }

    [Fact]
    public void BinaryWorldSerializer_RoundTripsNullableEntityHandle()
    {
        using var source = new World();
        var target = source.Create(HList.From(42));
        source.Create(HList.From(new SerializedEntityLink(target)));

        var writer = new ArrayBufferWriter<byte>();
        BinaryWorldSerializer.Serialize(ref writer, source);

        var payload = writer.WrittenMemory;
        var sequence = new ReadOnlySequence<byte>(payload);
        using var restored = new World();
        BinaryWorldSerializer.Deserialize(ref sequence, restored);

        var entities = restored.Hosts.SelectMany(static host => host);
        var restoredTarget = entities.Single(entity => entity.Contains<int>());
        var restoredSource = entities.Single(
            entity => entity.Contains<SerializedEntityLink>());

        Assert.Equal(restoredTarget, restoredSource.Get<SerializedEntityLink>().Target);
        Assert.Equal(0, sequence.Length);
    }
}
