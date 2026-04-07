namespace Sia.Serialization;

using System.Buffers;

public interface IWorldSerializer
{
    static abstract void Serialize<TBufferWriter>(ref TBufferWriter writer, World world)
        where TBufferWriter : IBufferWriter<byte>;

    static abstract void Deserialize(ref ReadOnlySequence<byte> buffer, World world);
}