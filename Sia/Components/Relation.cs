namespace Sia;

public interface IRelation
{
    public Identity Target { get; set; }
}

public interface IRelationTag;

public record struct Relation<TTag>(Identity Target) : IRelation
    where TTag : IRelationTag;

public static class RelationExtensions
{
    public static EntityRef GetTarget<TRelationTag>(this EntityRef entity, World world)
        where TRelationTag : IRelationTag
        => world[entity.Get<Relation<TRelationTag>>().Target];

    public static EntityRef GetTarget<TRelationTag>(this EntityRef entity)
        where TRelationTag : IRelationTag
        => entity.GetTarget<TRelationTag>(World.Current);
}