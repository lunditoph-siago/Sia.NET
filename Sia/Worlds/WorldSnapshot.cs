namespace Sia;

public sealed class WorldSnapshot<T>
    where T : unmanaged
{
    public static WorldSnapshot<T> Empty { get; } = new(0, [], 0);

    public long Version { get; }
    public int Count { get; }

    private readonly T[] _data;

    internal WorldSnapshot(long version, T[] data, int count)
    {
        Version = version;
        _data = data;
        Count = count;
    }

    public ReadOnlySpan<T> Data => _data.AsSpan(0, Count);
}
