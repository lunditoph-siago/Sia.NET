namespace Sia.Serialization;

using System.Buffers;

public interface IWorldSerializer
{
    abstract static void Serialize<TBufferWriter>(ref TBufferWriter writer, World world)
        where TBufferWriter : IBufferWriter<byte>;

    abstract static World Deserialize(ref ReadOnlySequence<byte> buffer);
}