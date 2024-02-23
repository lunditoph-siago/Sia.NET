namespace Sia_Examples;

using System.Collections.Immutable;
using System.Numerics;
using Sia;

public static partial class Example9_Templates
{
    public abstract record Test
    {
        public Guid? Id { get; set; }
    }

    [SiaTemplate("TestObject")]
    public record TestTemplate<T> : Test
    {
        public T? Value { get; set; }

        [Sia(Item = "TestItem")]
        public ImmutableDictionary<Vector2, string> Test = ImmutableDictionary<Vector2, string>.Empty;

        [Sia(Item = "TestItem2")]
        public ImmutableArray<Vector2> Test2 = ImmutableArray<Vector2>.Empty;
    }
}