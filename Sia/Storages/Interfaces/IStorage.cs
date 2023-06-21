namespace Sia;

public interface IStorage
{
    int Capacity { get; }
    int Count { get; }

    IntPtr Allocate();
    void Release(IntPtr pointer);
}

public interface IStorage<T> : IStorage
{
}