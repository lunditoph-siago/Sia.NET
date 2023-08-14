namespace Sia;

[AttributeUsage(AttributeTargets.Struct, Inherited = false, AllowMultiple = true)]
public sealed class RequireSystemAttribute<TSystem> : Attribute
    where TSystem : ISystem
{
}