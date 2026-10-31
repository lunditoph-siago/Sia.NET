namespace Sia.Tests.Systems;

public class FunctionSystemIdentityTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    public void EntityHandlers_CanBeOrderedAndRemovedIndividually(int arity)
    {
        var (chain, first, second) = arity switch {
            1 => CreateRegistration<ComponentHandlerWithEntity<int>>(
                static (Entity entity, ref int c1) => { },
                static (Entity entity, ref int c1) => { },
                static (chain, handler) => chain.Add(handler)),
            2 => CreateRegistration<ComponentHandlerWithEntity<int, int>>(
                static (Entity entity, ref int c1, ref int c2) => { },
                static (Entity entity, ref int c1, ref int c2) => { },
                static (chain, handler) => chain.Add(handler)),
            3 => CreateRegistration<ComponentHandlerWithEntity<int, int, int>>(
                static (Entity entity, ref int c1, ref int c2, ref int c3) => { },
                static (Entity entity, ref int c1, ref int c2, ref int c3) => { },
                static (chain, handler) => chain.Add(handler)),
            4 => CreateRegistration<ComponentHandlerWithEntity<int, int, int, int>>(
                static (Entity entity, ref int c1, ref int c2, ref int c3, ref int c4) => { },
                static (Entity entity, ref int c1, ref int c2, ref int c3, ref int c4) => { },
                static (chain, handler) => chain.Add(handler)),
            5 => CreateRegistration<ComponentHandlerWithEntity<int, int, int, int, int>>(
                static (Entity entity, ref int c1, ref int c2, ref int c3, ref int c4, ref int c5) => { },
                static (Entity entity, ref int c1, ref int c2, ref int c3, ref int c4, ref int c5) => { },
                static (chain, handler) => chain.Add(handler)),
            6 => CreateRegistration<ComponentHandlerWithEntity<int, int, int, int, int, int>>(
                static (Entity entity, ref int c1, ref int c2, ref int c3, ref int c4, ref int c5, ref int c6) => { },
                static (Entity entity, ref int c1, ref int c2, ref int c3, ref int c4, ref int c5, ref int c6) => { },
                static (chain, handler) => chain.Add(handler)),
            _ => throw new ArgumentOutOfRangeException(nameof(arity))
        };

        var configured = chain.Configure(first, descriptor => descriptor.Before(second));

        Assert.Equal(new[] { first, second }, configured.Plan().Entries.Select(entry => entry.Id));
        Assert.Equal(second, Assert.Single(configured.Remove(first).Plan().Entries).Id);
        Assert.Equal(first, Assert.Single(configured.RemoveAll(second).Plan().Entries).Id);
        Assert.Equal(new[] { second, first }, chain.Plan().Entries.Select(entry => entry.Id));
    }

    private static (SystemChain Chain, SystemId First, SystemId Second) CreateRegistration<THandler>(
        THandler first, THandler second, Func<SystemChain, THandler, SystemChain> add)
        where THandler : Delegate
        => (
            add(add(SystemChain.Empty, second), first),
            SystemId.ForDelegate(first),
            SystemId.ForDelegate(second));
}
