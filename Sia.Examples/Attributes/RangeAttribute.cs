namespace Sia;

[AttributeUsage(AttributeTargets.Field | AttributeTargets.Parameter)]
public sealed class RangeAttribute(double minimum, double maximum) : Attribute
{
    public double Minimum { get; } = minimum;
    public double Maximum { get; } = maximum;
}
