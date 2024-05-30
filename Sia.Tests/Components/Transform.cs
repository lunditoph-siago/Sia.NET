using System.Numerics;
using System.Runtime.CompilerServices;

namespace Sia.Tests.Components;

public partial record struct Position([Sia] Vector3 Value)
{
    public static implicit operator Position(Vector3 value)
        => new(value);
}

public partial record struct Rotation([Sia] Quaternion Value)
{
    public static implicit operator Rotation(Quaternion value)
        => new(value);
}

public partial record struct Scale([Sia] float Value)
{
    public static implicit operator Scale(float value)
        => new(value);
}

[SiaBundle]
public partial record struct Transform([Sia] Position Position, [Sia] Rotation Rotation, [Sia] Scale Scale)
{
    public static HList<Position, HList<Rotation, HList<Scale, EmptyHList>>> BakedEntity =>
        HList.Create((Position)Vector3.Zero, (Rotation)Quaternion.Identity, (Scale)1.0f);

    public readonly record struct SetEuler(Vector3 Euler) : ICommand
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Vector3 Sin(Vector3 x) => new(MathF.Sin(x.X), MathF.Sin(x.Y), MathF.Sin(x.Z));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Vector3 Cos(Vector3 x) => new(MathF.Cos(x.X), MathF.Cos(x.Y), MathF.Cos(x.Z));

        public void Execute(World _, Entity target)
        {
            ref var quaternion = ref target.GetRef<Rotation>();
            var a = Euler * (MathF.PI / 180.0f);
            var s = Sin(a * 0.5f);
            var c = Cos(a * 0.5f);

            quaternion.Value = new Quaternion
            {
                X = s.X * c.Y * c.Z - s.Y * s.Z * c.X,
                Y = s.Y * c.X * c.Z + s.X * s.Z * c.Y,
                Z = s.Z * c.X * c.Y - s.X * s.Y * c.Z,
                W = c.X * c.Y * c.Z + s.Y * s.Z * s.X
            };
        }
    }
}