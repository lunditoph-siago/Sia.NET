using System.Collections.Immutable;
using System.Numerics;

namespace Sia.Tests.Components;

public abstract record Test
{
    public Guid? Id { get; set; }
}

[SiaTemplate("TestObject")]
public partial record TestTemplate<T> : Test
{
    public T? Value { get; set; }

    [Sia(Item = "TestItem")]
    public ImmutableDictionary<Vector2, string> Test = ImmutableDictionary<Vector2, string>.Empty;

    [Sia(Item = "TestItem2")]
    public ImmutableArray<Vector2> Test2 = ImmutableArray<Vector2>.Empty;
}

public class TemplateTests
{
    [Theory]
    [InlineData("test")]
    public void Template_Test(string value)
    {
        var obj = new TestObject<string>(new TestTemplate<string> { Value = value });

        Assert.Equal(value, obj.Value);
    }
}