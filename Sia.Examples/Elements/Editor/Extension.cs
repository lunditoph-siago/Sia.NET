namespace Sia_Examples.Editor;

public interface IExtension { }

public enum PrecLevel { Highest = 0, High = 1, Default = 2, Low = 3, Lowest = 4 }

public static class Prec
{
    public static IExtension Highest(IExtension e) => new PrecExt(e, PrecLevel.Highest);
    public static IExtension High(IExtension e) => new PrecExt(e, PrecLevel.High);
    public static IExtension Default(IExtension e) => new PrecExt(e, PrecLevel.Default);
    public static IExtension Low(IExtension e) => new PrecExt(e, PrecLevel.Low);
    public static IExtension Lowest(IExtension e) => new PrecExt(e, PrecLevel.Lowest);
}

internal sealed class PrecExt(IExtension inner, PrecLevel prec) : IExtension
{
    public IExtension Inner => inner;
    public PrecLevel Prec => prec;
}

public sealed class Compartment
{
    public string Id { get; } = $"comp_{Interlocked.Increment(ref FacetIdGen.Value)}";
    public CompartmentInst Of(IExtension ext) => new(this, ext);
}

public sealed class CompartmentInst(Compartment comp, IExtension inner) : IExtension
{
    public Compartment Compartment => comp;
    public IExtension Inner => inner;
}
