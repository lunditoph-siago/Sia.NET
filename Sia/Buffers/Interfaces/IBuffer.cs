namespace Sia;

public interface IBuffer<T> : IDisposable
{
    int Capacity { get; }

    ref T this[int index] { get; }
    ref T CreateRef(int index);

    bool Release(int index);
    bool IsAllocated(int index);

    void Clear();
}