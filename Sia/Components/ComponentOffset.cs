namespace Sia;

using System.Runtime.CompilerServices;

public readonly record struct ComponentOffset<T>(nint Value)
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref T Get<TEntity>(ref TEntity byteRef)
        => ref Unsafe.As<TEntity, T>(ref Unsafe.AddByteOffset(ref byteRef, Value));
    
    public static implicit operator nint(ComponentOffset<T> offset) => offset.Value;
    public static implicit operator ComponentOffset<T> (nint offset) => new(offset);
}
