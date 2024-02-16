namespace Sia;

public interface IBuffer<T> : IDisposable
{
    int Capacity { get; }

    ref T CreateRef(int index);
    bool Release(int index);
    bool IsAllocated(int index);

    ref T GetRef(int index);
    ref T GetRefOrNullRef(int index);

    void Clear();
}