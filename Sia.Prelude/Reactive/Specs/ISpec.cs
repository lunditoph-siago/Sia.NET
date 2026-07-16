namespace Sia.Reactive;

public interface ISpecSignatureHandler
{
    void Handle<TSpec, TState, TTree>()
        where TSpec : struct, ISpec<TSpec, TState, TTree>
        where TState : struct
        where TTree : struct, ITerm<TTree>;
}

public interface ISpec
{
    static abstract void HandleSignature<THandler>(ref THandler handler)
        where THandler : ISpecSignatureHandler;
}

public interface ISpec<TSelf, TState, TTree> : ISpec
    where TSelf : struct, ISpec<TSelf, TState, TTree>
    where TState : struct
    where TTree : struct, ITerm<TTree>
{
    static virtual TState InitialState(in TSelf props) => default;

    static abstract TTree Expand(in TSelf props, in TState state, in ExpandContext ctx);

    static void ISpec.HandleSignature<THandler>(ref THandler handler)
        => handler.Handle<TSelf, TState, TTree>();
}
