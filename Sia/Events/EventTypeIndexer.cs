namespace Sia;

internal static class EventTypeIndexerShared
{
    private static int _index = -1;
    public static int GetNext() => Interlocked.Increment(ref _index);
}

internal static class EventTypeIndexState<T> where T : IEvent
{
    private static int _index;
    private static bool _assigned;

    internal static int Allocate()
    {
        var index = EventTypeIndexerShared.GetNext();
        _index = index;
        Volatile.Write(ref _assigned, true);
        return index;
    }

    internal static bool TryGetIndex(out int index)
    {
        if (Volatile.Read(ref _assigned)) {
            index = _index;
            return true;
        }
        index = -1;
        return false;
    }
}

public static class EventTypeIndexer<T>
    where T : IEvent
{
    public static int Index { get; } = EventTypeIndexState<T>.Allocate();
}
