namespace Sia;

public interface IBundle
{
    void ToHList<THandler>(in THandler handler)
        where THandler : IGenericStructHandler<IHList>;

    void HandleHListType<THandler>(in THandler handler)
        where THandler : IGenericStructTypeHandler<IHList>;
}

public interface IStaticBundle : IBundle
{
    abstract static void StaticHandleHListType<THandler>(in THandler handler)
        where THandler : IGenericStructTypeHandler<IHList>;
}