namespace Sia;

[AttributeUsage(AttributeTargets.Class, Inherited = true, AllowMultiple = true)]
public sealed class AfterSystemAttribute<TSystem> : Attribute, ISystemAttribute
    where TSystem : ISystem
{
    public Type SystemType => typeof(TSystem);
}