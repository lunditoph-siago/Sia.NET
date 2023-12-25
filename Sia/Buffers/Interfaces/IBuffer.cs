namespace Sia;

public interface IBuffer<T> : IDisposable
{
    int Capacity { get; }
    ref T GetRef(int index);
    bool IsAllocated(int index);
}