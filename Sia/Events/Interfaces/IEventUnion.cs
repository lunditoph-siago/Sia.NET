namespace Sia;

public interface IEventUnion : IStaticTypeUnion
{
    IEnumerable<Type> EventTypesWithPureEvents { get; }
}