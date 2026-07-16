namespace Sia.Reactive;

public delegate TTree ExpandFn<TProps, TTree>(in TProps props, in ExpandContext ctx)
    where TProps : struct, IEquatable<TProps>
    where TTree : struct, ITerm<TTree>;

public readonly record struct Spec<TProps, TTree>(
    TProps Props,
    ExpandFn<TProps, TTree> Expansion)
    : ISpec<Spec<TProps, TTree>, Unit, TTree>
    where TProps : struct, IEquatable<TProps>
    where TTree : struct, ITerm<TTree>
{
    public ExpandFn<TProps, TTree> Expansion { get; init; }
        = Expansion ?? throw new ArgumentNullException(nameof(Expansion));

    public static TTree Expand(
        in Spec<TProps, TTree> spec,
        in Unit state,
        in ExpandContext context)
        => (spec.Expansion
            ?? throw new InvalidOperationException(
                "A default function spec does not define an expansion."))(
                    spec.Props, context);
}

public static class Spec
{
    public static Spec<TProps, TTree> Of<TProps, TTree>(
        in TProps props,
        ExpandFn<TProps, TTree> expand)
        where TProps : struct, IEquatable<TProps>
        where TTree : struct, ITerm<TTree>
        => new(props, expand ?? throw new ArgumentNullException(nameof(expand)));
}
