using System.Numerics;

namespace Sia.Tests.Components;

public class ComponentsTests
{
    private const float Epsilon = 1e-6f;

    [Fact]
    public void Component_Command_Test()
    {
        using var fixture = new WorldFixture();

        var entityRef = fixture.World.CreateInArrayHost(Transform.BakedEntity);

        ref var position = ref entityRef.Get<Position>();
        ref var rotation = ref entityRef.Get<Rotation>();

        Assert.Equal(Vector3.Zero, position, (a, b) =>
            Math.Abs(a.Value.X - b.Value.X) < Epsilon &&
            Math.Abs(a.Value.Y - b.Value.Y) < Epsilon &&
            Math.Abs(a.Value.Z - b.Value.Z) < Epsilon);

        fixture.World.Modify(entityRef, new Transform.SetEuler(Vector3.UnitY * 90.0f));

        Assert.Equal(new Quaternion(0.0f, 0.707107f, 0.0f, 0.707107f), rotation, (a, b) =>
            Math.Abs(a.Value.X - b.Value.X) < Epsilon &&
            Math.Abs(a.Value.Y - b.Value.Y) < Epsilon &&
            Math.Abs(a.Value.Z - b.Value.Z) < Epsilon &&
            Math.Abs(a.Value.W - b.Value.W) < Epsilon);
    }
}