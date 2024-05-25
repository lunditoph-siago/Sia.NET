namespace Sia;

internal static class InternalEntityComponentIndexerShared
{
    private static int _index = -1;
    public static int GetNext() => Interlocked.Increment(ref _index);
}

internal static class InternalEntityComponentIndexer<TEntity, TComponent>
{
    public static int Index { get; } = InternalEntityComponentIndexerShared.GetNext();
}