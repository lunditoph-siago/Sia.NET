namespace Sia;

using System.Runtime.Serialization;
using CommunityToolkit.HighPerformance;

public sealed class ManagedHeapStorage<T> : IStorage<T>
    where T : struct
{
    public static ManagedHeapStorage<T> Instance { get; } = new();

    public int Capacity { get; } = int.MaxValue;
    public int Count => _objects.Count;
    public bool IsManaged => true;

    private readonly Dictionary<long, Box<T>> _objects = new();
    private readonly ObjectIDGenerator _idGenerator = new();

    private ManagedHeapStorage() {}

    public Pointer<T> Allocate()
        => Allocate(default);

    public Pointer<T> Allocate(in T initial)
    {
        Box<T> obj = initial;
        long id = _idGenerator.GetId(obj, out bool _);
        _objects[id] = obj;
        return new(id, this);
    }

    public void UnsafeRelease(long rawPointer)
    {
        if (!_objects.Remove(rawPointer)) {
            throw new ArgumentException("Invalid pointer");
        }
    }

    public ref T UnsafeGetRef(long rawPointer)
        => ref _objects[rawPointer].GetReference();
}