namespace Sia;

public static class SystemChainSchedulingExtensions
{
    public static SystemGraph ToGraph(this SystemChain chain) => new(chain);

    public static SystemStage CreateSortedStage(this SystemChain chain, World world)
        => chain.ToGraph().CreateStage(world);

    public static SystemChain With<TSystem>(
        this SystemChain chain,
        Func<SystemDescriptor, SystemDescriptor> configure)
        where TSystem : ISystem
        => chain.Configure<TSystem>(configure);

    public static SystemChain With(
        this SystemChain chain,
        SystemId id,
        Func<SystemDescriptor, SystemDescriptor> configure)
        => chain.Configure(id, configure);

    public static SystemChain Chain(this SystemChain chain)
    {
        var entries = chain.Entries;
        for (int i = 1; i < entries.Count; i++) {
            var prevId = entries[i - 1].Id;
            var currId = entries[i].Id;
            chain = chain.Configure(currId, descriptor => descriptor.After(prevId));
        }
        return chain;
    }
}
