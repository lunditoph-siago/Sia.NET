namespace Sia.Serialization;

using System.Buffers;
using System.Diagnostics.CodeAnalysis;

public interface IWorldSerializer
{
    [RequiresUnreferencedCode(
        "Implementations may resolve serialized types by name. Their members must be preserved by the caller.")]
    [RequiresDynamicCode(
        "Implementations may construct generic types and methods at runtime while deserializing.")]
    static abstract void Serialize<TBufferWriter>(ref TBufferWriter writer, World world)
        where TBufferWriter : IBufferWriter<byte>;

    [RequiresUnreferencedCode(
        "Implementations may resolve serialized types by name. Their members must be preserved by the caller.")]
    [RequiresDynamicCode(
        "Implementations may construct generic types and methods at runtime while deserializing.")]
    static abstract void Deserialize(ref ReadOnlySequence<byte> buffer, World world);
}