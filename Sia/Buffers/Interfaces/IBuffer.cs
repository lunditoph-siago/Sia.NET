namespace Sia;

public interface IBuffer<T> : IDisposable
{
    int Capacity { get; }

    ref T CreateRef(int index);
    ref T GetRef(int index);
    bool IsAllocated(int index);
    bool Release(int index);
}