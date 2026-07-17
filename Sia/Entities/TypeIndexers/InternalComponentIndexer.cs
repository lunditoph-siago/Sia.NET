namespace Sia;

internal static class InternalComponentIndexerShared
{
    private static int _index = -1;

    public static int GetNext()
        => Interlocked.Increment(ref _index);
}

internal static class InternalComponentIndexer<TComponent>
{
    public static int Index { get; } = InternalComponentIndexerShared.GetNext();
}
