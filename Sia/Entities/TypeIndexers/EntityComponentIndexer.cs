namespace Sia;

internal static class EntityComponentIndexerShared
{
    private static int _index = -1;
    public static int GetNext() => Interlocked.Increment(ref _index);
}

public static class EntityComponentIndexer<TEntity, TComponent>
{
    public static int Index { get; } = TypeIndexerShared.GetNext();
}