namespace Sia.Serialization;

using System.Buffers;

public interface IComponentSerializer : IDisposable
{
    bool Serialize<TBufferWriter, TComponent>(
        ref TBufferWriter writer, in TComponent component)
        where TBufferWriter : IBufferWriter<byte>;

    bool Deserialize<TComponent>(
        ref ReadOnlySequence<byte> buffer, ref TComponent component, Dictionary<EntityId, Entity> entityMap);
}