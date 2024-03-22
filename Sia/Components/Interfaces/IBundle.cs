namespace Sia;

public interface IBundle
{
    public void HandleHead<THandler>(in THandler handler)
        where THandler : IGenericHandler;

    public void HandleTail<THandler>(in THandler handler)
        where THandler : IGenericHandler<IHList>;

    void Concat<THList, TResultHandler>(in THList list, TResultHandler handler)
        where THList : IHList
        where TResultHandler : IGenericHandler<IHList>;
}