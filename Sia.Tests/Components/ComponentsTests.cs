using System.Numerics;
using System.Runtime.CompilerServices;
using Sia.Tests.Systems;

namespace Sia.Tests.Components;

public partial record struct Transform([Sia] Vector3 Position, [Sia] Quaternion Rotation, [Sia] float Scale)
{
    public static HList<Vector3, HList<Quaternion, HList<float, EmptyHList>>> Baked =>
        HList.Create(Vector3.Zero, Quaternion.Identity, 1.0f);

    public readonly record struct SetEuler(Vector3 Euler) : ICommand
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Vector3 Sin(Vector3 x) => new(MathF.Sin(x.X), MathF.Sin(x.Y), MathF.Sin(x.Z));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Vector3 Cos(Vector3 x) => new(MathF.Cos(x.X), MathF.Cos(x.Y), MathF.Cos(x.Z));

        public void Execute(World _, in EntityRef target)
        {
            ref var quaternion = ref target.Get<Quaternion>();
            var a = Euler * (MathF.PI / 180.0f);
            var s = Sin(a * 0.5f);
            var c = Cos(a * 0.5f);

            quaternion = new Quaternion
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
    private const float Epsilon = 1e-6f;

    [Fact]
    public void Component_Command_Test()
    {
        using var fixture = new WorldFixture();

        var entityRef = fixture.World.CreateInArrayHost(Transform.Baked);

        ref var position = ref entityRef.Get<Vector3>();
        ref var rotation = ref entityRef.Get<Quaternion>();

        Assert.Equal(Vector3.Zero, position, (a, b) =>
            Math.Abs(a.X - b.X) < Epsilon && Math.Abs(a.Y - b.Y) < Epsilon && Math.Abs(a.Z - b.Z) < Epsilon);

        fixture.World.Modify(entityRef, new Transform.SetEuler(Vector3.UnitY * 90.0f));

        Assert.Equal(new Quaternion(0.0f, 0.707107f, 0.0f, 0.707107f), rotation, (a, b) =>
            Math.Abs(a.X - b.X) < Epsilon && Math.Abs(a.Y - b.Y) < Epsilon &&
            Math.Abs(a.Z - b.Z) < Epsilon && Math.Abs(a.W - b.W) < Epsilon);
    }
}