using System.Buffers;

namespace Sia.Serialization;

public interface IHostHeaderSerializer
{
    static abstract void Serialize<TBufferWriter>(ref TBufferWriter writer, IEntityHost host)
        where TBufferWriter : IBufferWriter<byte>;

    static abstract IEntityHost? Deserialize(ref ReadOnlySequence<byte> buffer, World world);
}