namespace Sia;

public interface IEntityQuery : IDisposable
{
    void ForEach(EntityHandler handler);
    void ForEach<TData>(in TData data, EntityHandler<TData> handler);
}