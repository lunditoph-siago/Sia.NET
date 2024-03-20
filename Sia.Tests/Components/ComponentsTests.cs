using System.Numerics;
using System.Runtime.CompilerServices;
using Sia.Tests.Systems;

namespace Sia.Tests.Components;

public partial record struct Transform([Sia] Vector3 Position, [Sia] Quaternion Rotation, [Sia] float Scale)
{
    public static Transform Default => new()
    {
        Position = Vector3.Zero,
        Rotation = Quaternion.Identity,
        Scale = 1.0f
    };

    public readonly record struct SetEuler(Vector3 Euler) : ICommand
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Vector3 Sin(Vector3 x) => new(MathF.Sin(x.X), MathF.Sin(x.Y), MathF.Sin(x.Z));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Vector3 Cos(Vector3 x) => new(MathF.Cos(x.X), MathF.Cos(x.Y), MathF.Cos(x.Z));

        public void Execute(World _, in EntityRef target)
        {
            ref var transform = ref target.Get<Transform>();
            var s = Sin(Euler * 0.5f);
            var c = Cos(Euler * 0.5f);

            transform.Rotation = new Quaternion
            {
                X = s.X * c.Y * c.Z - s.Y * s.Z * c.X,
                Y = s.Y * c.X * c.Z + s.X * s.Z * c.Y,
                Z = s.Z * c.X * c.Y - s.X * s.Y * c.Z,
                W = c.X * c.Y * c.Z + s.Y * s.Z * s.X
            };
        }
    }
}

public class ComponentsTests
{
    [Fact]
    public void Component_Command_Test()
    {
        using var fixture = new WorldFixture();

        var entity = fixture.World.GetBucketHost<Transform>().Create(Transform.Default);

        ref var position = ref entity.Get<Vector3>();

        Assert.Equal(Vector3.Zero, position);

        fixture.World.Modify(entity, new Transform.SetEuler(Vector3.UnitY));

        Assert.Equal(Vector3.One, position);
    }
}