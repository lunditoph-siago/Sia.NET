namespace Sia;

internal static class EventTypeIndexerShared
{
    private static int _index = -1;
    public static int GetNext() => Interlocked.Increment(ref _index);
}

internal static class EventTypeIndexState<T> where T : IEvent
{
    private static int _index = -1;

    internal static int Allocate()
    {
        var index = EventTypeIndexerShared.GetNext();
        Volatile.Write(ref _index, index);
        return index;
    }

    internal static bool TryGetIndex(out int index)
    {
        index = Volatile.Read(ref _index);
        return index >= 0;
    }
}

public static class EventTypeIndexer<T>
    where T : IEvent
{
    public static int Index { get; } = EventTypeIndexState<T>.Allocate();
}
