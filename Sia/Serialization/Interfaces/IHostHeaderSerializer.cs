using System.Buffers;

namespace Sia.Serialization;

public interface IHostHeaderSerializer
{
    abstract static void Serialize<TBufferWriter>(ref TBufferWriter writer, IEntityHost host)
        where TBufferWriter : IBufferWriter<byte>;
    
    abstract static IEntityHost? Deserialize(ref ReadOnlySequence<byte> buffer, World world);
}