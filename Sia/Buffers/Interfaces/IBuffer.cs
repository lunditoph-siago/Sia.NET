namespace Sia;

public interface IBuffer<T> : IDisposable
{
    int Capacity { get; }
    int Count { get; }

    ref T GetOrAddValueRef(int index, out bool exists);
    ref T GetValueRefOrNullRef(int index);

    bool Remove(int index);
}