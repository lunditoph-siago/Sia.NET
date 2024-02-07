namespace Sia;

public interface IEntityCreator
{
    public EntityRef<TEntity> CreateEntity<TEntity>(TEntity initial)
        where TEntity : struct;
}