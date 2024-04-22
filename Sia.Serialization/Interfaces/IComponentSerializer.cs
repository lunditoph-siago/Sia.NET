namespace Sia.Serialization;

using System.Buffers;

public interface IComponentSerializer : IDisposable
{
    void Serialize<TBufferWriter, TComponent>(
        ref TBufferWriter writer, in TComponent component)
        where TBufferWriter : IBufferWriter<byte>;

    void Deserialize<TComponent>(
        ref ReadOnlySequence<byte> buffer, ref TComponent component);
}