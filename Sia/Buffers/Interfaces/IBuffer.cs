namespace Sia;

public interface IBuffer<T> : IDisposable
{
    bool IsManaged { get; }
    int Capacity { get; }
    int Count { get; set; }

    ref T GetRef(int index);
    ref T GetRefOrNullRef(int index);
}