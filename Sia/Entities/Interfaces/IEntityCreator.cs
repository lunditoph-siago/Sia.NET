namespace Sia;

public interface IEntityCreator
{
    public EntityRef<WithId<TEntity>> CreateEntity<TEntity>(TEntity initial)
        where TEntity : struct;
}