namespace Sia.Tests;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public class PriorityAttribute(int priority) : Attribute
{
    public int Priority { get; private set; } = priority;
}