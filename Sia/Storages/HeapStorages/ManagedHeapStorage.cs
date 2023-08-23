namespace Sia;

using System.Runtime.Serialization;
using System.Runtime.CompilerServices;
using CommunityToolkit.HighPerformance;

public sealed class ManagedHeapStorage<T> : IStorage<T>
    where T : struct
{
    public static ManagedHeapStorage<T> Instance { get; } = new();

    public int Capacity { get; } = int.MaxValue;
    public int Count => _objects.Count;
    public int PointerValidBits => 32;
    public bool IsManaged => true;

    private Dictionary<long, Box<T>> _objects = new();
    private ObjectIDGenerator _idGenerator = new();

    private ManagedHeapStorage() {}

    public Pointer<T> Allocate()
        => Allocate(default);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Pointer<T> Allocate(in T initial)
    {
        Box<T> obj = initial;
        long id = _idGenerator.GetId(obj, out bool _);
        _objects[id] = obj;
        return new(id, this);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void UnsafeRelease(long rawPointer)
    {
        if (!_objects.Remove(rawPointer)) {
            throw new ArgumentException("Invalid pointer");
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref T UnsafeGetRef(long rawPointer)
        => ref _objects[rawPointer].GetReference();
    
    public void Dispose()
    {
        _objects = null!;
        _idGenerator = null!;
    }
}