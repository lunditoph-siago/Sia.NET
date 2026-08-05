using System.Buffers;
using System.Diagnostics.CodeAnalysis;

namespace Sia.Serialization;

public interface IHostHeaderSerializer
{
    [RequiresUnreferencedCode(
        "Serialized host and component types are resolved by name. Their members must be preserved by the caller.")]
    [RequiresDynamicCode(
        "Deserializing arbitrary host types constructs generic types and methods at runtime.")]
    static abstract void Serialize<TBufferWriter>(ref TBufferWriter writer, IEntityHost host)
        where TBufferWriter : IBufferWriter<byte>;

    [RequiresUnreferencedCode(
        "Serialized host and component types are resolved by name. Their members must be preserved by the caller.")]
    [RequiresDynamicCode(
        "Deserializing arbitrary host types constructs generic types and methods at runtime.")]
    static abstract IEntityHost? Deserialize(ref ReadOnlySequence<byte> buffer, World world);
}