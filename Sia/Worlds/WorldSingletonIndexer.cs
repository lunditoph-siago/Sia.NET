namespace Sia;

internal static class WorldSingletonIndexerShared
{
    private static int _index;
    public static int GetNext() => Interlocked.Increment(ref _index);
}

public static class WorldSingletonIndexer<T>
{
    public static int Index { get; } = WorldSingletonIndexerShared.GetNext();
}