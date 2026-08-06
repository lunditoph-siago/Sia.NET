namespace Sia_Examples.Editor;

internal interface IFacet
{
    public object? DefaultBoxed { get; }
    public object? CombineBoxed(IReadOnlyList<object?> inputs);
}

public sealed class Facet<TInput, TOutput> : IFacet
{
    public string Id { get; }
    public TOutput DefaultValue { get; }
    public Func<TInput[], TOutput> Combine { get; }
    public Func<TInput, TInput, bool> CompareInput { get; }
    public Func<TOutput, TOutput, bool> Compare { get; }
    public bool IsStatic { get; }

    internal Facet(string id,
        Func<TInput[], TOutput> combine,
        Func<TInput, TInput, bool> compareInput,
        Func<TOutput, TOutput, bool> compare, bool isStatic)
    {
        Id = id; Combine = combine; CompareInput = compareInput; Compare = compare; IsStatic = isStatic;

        DefaultValue = combine([]);
    }

    public FacetProvider<TInput, TOutput> Of(TInput value) => new(this, value);

    public static Facet<TInput, TOutput> Define(
        Func<TInput[], TOutput>? combine = null,
        Func<TInput, TInput, bool>? compareInput = null,
        bool isStatic = false)
    {
        var id = $"facet_{Interlocked.Increment(ref FacetIdGen.Value)}";
        return new Facet<TInput, TOutput>(id,
            combine ?? (_ => throw new InvalidOperationException(
                $"Facet.Define<{typeof(TInput).Name},{typeof(TOutput).Name}> requires an explicit combine function " +
                "(CM6 defaults to the identity combine when TOutput is TInput[], which C# generics can't express here)")),
            compareInput ?? EqualityComparer<TInput>.Default.Equals,
            EqualityComparer<TOutput>.Default.Equals, isStatic);
    }

    object? IFacet.DefaultBoxed => DefaultValue;
    object? IFacet.CombineBoxed(IReadOnlyList<object?> inputs)
    {
        var typed = new TInput[inputs.Count];
        for (var i = 0; i < inputs.Count; i++) typed[i] = (TInput)inputs[i]!;
        return Combine(typed);
    }
}

internal static class FacetIdGen { public static int Value; }

internal interface IFacetProviderSlot
{
    public string FacetId { get; }
    public object? BoxedValue { get; }
    public IFacet FacetOwner { get; }
}

public sealed class FacetProvider<TInput, TOutput> : IExtension, IFacetProviderSlot
{
    public Facet<TInput, TOutput> Facet { get; }
    public TInput Value { get; }
    internal FacetProvider(Facet<TInput, TOutput> facet, TInput value) { Facet = facet; Value = value; }

    string IFacetProviderSlot.FacetId => Facet.Id;
    object? IFacetProviderSlot.BoxedValue => Value;
    IFacet IFacetProviderSlot.FacetOwner => Facet;
}
