namespace Sia;

public interface IRelationComponent
{
    public Entity Target { get; }
}

public interface IArgumentRelationComponent : IRelationComponent
{
    public void HandleRelation(IRefGenericHandler handler);
}

public interface IRelation;
public interface IRelation<TArgument>;

public record struct Relation<TRelation>(Entity Target) : IRelationComponent
    where TRelation : IRelation
{
    public readonly override string ToString()
        => typeof(TRelation) + "(" + Target + ")";
}

public record struct ArgRelation<TArgRelation>(Entity Target) : IArgumentRelationComponent
    where TArgRelation : IRelation
{
    public TArgRelation Relation {
        readonly get => _relation;
        init => _relation = value;
    }
    private TArgRelation _relation;

    public void HandleRelation(IRefGenericHandler handler)
        => handler.Handle(ref _relation);

    public readonly override string ToString()
        => _relation + " (" + Target + ")";
}

public static class RelationExtensions
{
    public static Entity AddRelation<TRelation>(this Entity entity, Entity target)
        where TRelation : IRelation
        => entity.Add(new Relation<TRelation>(target));

    public static Entity AddRelation<TArgRelation>(this Entity entity, TArgRelation relation, Entity target)
        where TArgRelation : IRelation
        => entity.Add(new ArgRelation<TArgRelation>(target) { Relation = relation });

    public static Entity SetRelation<TRelation>(this Entity entity, Entity target)
        where TRelation : IRelation
        => entity.Set(new Relation<TRelation>(target));

    public static Entity SetRelation<TArgRelation>(this Entity entity, TArgRelation relation, Entity target)
        where TArgRelation : IRelation
        => entity.Set(new ArgRelation<TArgRelation>(target) { Relation = relation });

    public static Entity GetRelation<TRelation>(this Entity entity)
        where TRelation : IRelation
        => entity.Get<Relation<TRelation>>().Target;

    public static Entity GetRelation<TArgRelation>(
        this Entity entity, out TArgRelation arg)
        where TArgRelation : IRelation
    {
        ref var relation = ref entity.Get<ArgRelation<TArgRelation>>();
        arg = relation.Relation;
        return relation.Target;
    }
}