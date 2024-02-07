namespace Sia;

internal static class WorldAddonIndexerShared
{
    private static int _index = -1;
    public static int GetNext() => Interlocked.Increment(ref _index);
}

public static class WorldAddonIndexer<T>
    where T : IAddon
{
    public static int Index { get; } = WorldAddonIndexerShared.GetNext();
}