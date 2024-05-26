namespace Sia;

public interface IRelationComponent
{
    public Entity Target { get; }
}

public interface IRelationComponent<TArgument> : IRelationComponent
{
    public object? BoxedArgument { get; }
}

public interface IRelation;
public interface IRelation<TArgument>;

public record struct Relation<TTag>(Entity Target) : IRelationComponent
    where TTag : IRelation;

public record struct Relation<TTag, TArgument>(Entity Target, TArgument Argument) : IRelationComponent<TArgument>
    where TTag : IRelation<TArgument>
{
    public readonly object? BoxedArgument => Argument;
}

public static class RelationExtensions
{
    public static Entity AddRelation<TRelation>(this Entity entity, Entity target)
        where TRelation : IRelation
        => entity.Add(new Relation<TRelation>(target));

    public static Entity AddRelation<TRelation, TArgument>(this Entity entity, Entity target, TArgument arg)
        where TRelation : IRelation<TArgument>
        => entity.Add(new Relation<TRelation, TArgument>(target, arg));

    public static Entity SetRelation<TRelation>(this Entity entity, Entity target)
        where TRelation : IRelation
        => entity.Set(new Relation<TRelation>(target));

    public static Entity SetRelation<TRelation, TArgument>(this Entity entity, Entity target, TArgument arg)
        where TRelation : IRelation<TArgument>
        => entity.Set(new Relation<TRelation, TArgument>(target, arg));

    public static Entity GetRelation<TRelation>(this Entity entity)
        where TRelation : IRelation
        => entity.Get<Relation<TRelation>>().Target;

    public static Entity GetRelation<TRelation, TArgument>(
        this Entity entity, out TArgument arg)
        where TRelation : IRelation<TArgument>
    {
        ref var relation = ref entity.Get<Relation<TRelation, TArgument>>();
        arg = relation.Argument;
        return relation.Target;
    }
}