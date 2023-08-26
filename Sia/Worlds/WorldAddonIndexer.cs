namespace Sia;

internal static class WorldAddonIndexerShared
{
    private static int _index;
    public static int GetNext() => Interlocked.Increment(ref _index);
}

public static class WorldAddonIndexer<T>
{
    public static int Index { get; } = WorldAddonIndexerShared.GetNext();
}