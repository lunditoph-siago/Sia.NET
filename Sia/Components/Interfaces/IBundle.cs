namespace Sia;

public interface IBundle
{
    void ToHList<THandler>(in THandler handler)
        where THandler : IGenericHandler<IHList>;

    void HandleHListType<THandler>(in THandler handler)
        where THandler : IGenericTypeHandler<IHList>;
}