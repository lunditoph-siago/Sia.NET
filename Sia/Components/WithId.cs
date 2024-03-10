namespace Sia;

public struct WithId<TEntity> : IComponentBundle
{
    public Identity Identity;
    [ComponentBundle] public TEntity Entity;
}