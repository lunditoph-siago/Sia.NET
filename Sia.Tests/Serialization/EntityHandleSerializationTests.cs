namespace Sia.Tests.Serialization;

using System.Buffers;
using MemoryPack;
using Sia.Serialization.Binary;

[MemoryPackable]
public partial record struct SerializedEntityLink(Entity? Target);

public class EntityHandleSerializationTests
{
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
