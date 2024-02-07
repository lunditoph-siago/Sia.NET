namespace Sia;

internal static class EventTypeIndexerShared
{
    private static int _index = -1;
    public static int GetNext() => Interlocked.Increment(ref _index);
}

public static class EventTypeIndexer<T>
    where T : IEvent
{
    public static int Index { get; } = EventTypeIndexerShared.GetNext();
}