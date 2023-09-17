namespace Sia;

internal static class WorldEntityHostIndexerShared
{
    private static int _index;
    public static int GetNext() => Interlocked.Increment(ref _index);
}

public static class WorldEntityHostIndexer<T>
    where T : IEntityHost
{
    public static int Index { get; } = WorldEntityHostIndexerShared.GetNext();
}