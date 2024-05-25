using System.Runtime.CompilerServices;

namespace Sia;

public interface IRelationComponent
{
    public Identity Target { get; set; }
}

public interface IRelation;
public interface IRelation<TArgument>;

public record struct Relation<TTag>(Identity Target) : IRelationComponent
    where TTag : IRelation;

public record struct Relation<TTag, TArgument>(Identity Target, TArgument Argument) : IRelationComponent
    where TTag : IRelation<TArgument>;

public static class RelationExtensions
{
    public static EntityRef Add<TRelation>(this EntityRef entity, EntityRef target)
        where TRelation : IRelation
        => entity.Add(new Relation<TRelation>(target.Id));

    public static EntityRef Add<TRelation, TArgument>(this EntityRef entity, Identity target, TArgument arg)
        where TRelation : IRelation<TArgument>
        => entity.Add(new Relation<TRelation, TArgument>(target, arg));

    public static EntityRef Add<TRelation, TArgument>(this EntityRef entity, EntityRef target, TArgument arg)
        where TRelation : IRelation<TArgument>
        => entity.Add(new Relation<TRelation, TArgument>(target.Id, arg));

    public static EntityRef Set<TRelation>(this EntityRef entity, EntityRef target)
        where TRelation : IRelation
        => entity.Set(new Relation<TRelation>(target.Id));

    public static EntityRef Set<TRelation, TArgument>(this EntityRef entity, Identity target, TArgument arg)
        where TRelation : IRelation<TArgument>
        => entity.Set(new Relation<TRelation, TArgument>(target, arg));

    public static EntityRef Set<TRelation, TArgument>(this EntityRef entity, EntityRef target, TArgument arg)
        where TRelation : IRelation<TArgument>
        => entity.Set(new Relation<TRelation, TArgument>(target.Id, arg));

    public static EntityRef Get<TRelation>(this EntityRef entity, World world)
        where TRelation : IRelation
        => world[entity.Get<Relation<TRelation>>().Target];

    public static EntityRef Get<TRelation>(this EntityRef entity)
        where TRelation : IRelation
        => entity.Get<TRelation>(World.Current);

    public static EntityRef Get<TRelation, TArgument>(
        this EntityRef entity, World world, out TArgument arg)
        where TRelation : IRelation<TArgument>
    {
        ref var relation = ref entity.Get<Relation<TRelation, TArgument>>();
        arg = relation.Argument;
        return world[relation.Target];
    }

    public static EntityRef Get<TRelation, TArgument>(
        this EntityRef entity, out TArgument arg)
        where TRelation : IRelation<TArgument>
        => entity.Get<TRelation, TArgument>(World.Current, out arg);
}