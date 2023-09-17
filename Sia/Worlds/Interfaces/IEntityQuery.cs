namespace Sia;

public interface IEntityQuery : IDisposable
{
    void ForEach(WorldEntityHandler handler);
    void ForEach<TData>(in TData data, WorldEntityHandler<TData> handler);
}