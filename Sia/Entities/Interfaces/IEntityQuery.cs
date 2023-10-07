namespace Sia;

public interface IEntityQuery : IDisposable
{
    int Count { get; }
    
    void ForEach(EntityHandler handler);
    void ForEach(SimpleEntityHandler handler);
    void ForEach<TData>(in TData data, EntityHandler<TData> handler);
    void ForEach<TData>(in TData data, SimpleEntityHandler<TData> handler);
}