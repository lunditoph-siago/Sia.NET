namespace Sia;

internal static class TypeIndexerShared
{
    private static int _index;
    public static int GetNext() => Interlocked.Increment(ref _index);
}

public static class TypeIndexer<T>
{
    public static int Index { get; } = TypeIndexerShared.GetNext();
}