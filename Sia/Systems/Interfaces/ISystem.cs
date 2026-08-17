namespace Sia;

public interface ISystem
{
    SystemChain? Children { get; }
    IEntityMatcher? Matcher { get; }
    IEventUnion? Trigger { get; }
    IEventUnion? Filter { get; }

    void Initialize(World world);
    void Uninitialize(World world);
    void Execute(WorldContext context, IEntityQuery query);
}
