namespace Sia_Examples;

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Numerics;
using Sia;

public static class QuaternionExtensions
{
    public const float TwoPI = MathF.PI * 2;
    public const float DegreeToRadian = TwoPI / 360;
    public const float RadianToDegree = 360 / TwoPI;

    public static Vector3 ToEulerAngles(this Quaternion q)
    {
        var angles = new Vector3();

        float sinrCosp = 2 * (q.W * q.X + q.Y * q.Z);
        float cosrCosp = 1 - 2 * (q.X * q.X + q.Y * q.Y);
        angles.X = MathF.Atan2(sinrCosp, cosrCosp);

        float sinp = 2 * (q.W * q.Y - q.Z * q.X);
        angles.Y = MathF.Abs(sinp) >= 1
            ? MathF.CopySign(MathF.PI / 2, sinp)
            : MathF.Asin(sinp);

        float sinyCosp = 2 * (q.W * q.Z + q.X * q.Y);
        float cosyCosp = 1 - 2 * (q.Y * q.Y + q.Z * q.Z);
        angles.Z = MathF.Atan2(sinyCosp, cosyCosp);

        return angles * RadianToDegree;
    }

    public static Quaternion ToQuaternion(this Vector3 v)
    {
        v *= DegreeToRadian;

        float cy = MathF.Cos(v.Z * 0.5f);
        float sy = MathF.Sin(v.Z * 0.5f);
        float cp = MathF.Cos(v.Y * 0.5f);
        float sp = MathF.Sin(v.Y * 0.5f);
        float cr = MathF.Cos(v.X * 0.5f);
        float sr = MathF.Sin(v.X * 0.5f);

        return new Quaternion {
            W = cr * cp * cy + sr * sp * sy,
            X = sr * cp * cy - cr * sp * sy,
            Y = cr * sp * cy + sr * cp * sy,
            Z = cr * cp * sy - sr * sp * cy
        };
    }
}

public static partial class Example3_MoveRotator
{
    public partial record struct Position([Sia] Vector3 Value);
    public partial record struct Rotation([Sia] Quaternion Value)
    {
        public Rotation() : this(Quaternion.Identity) {}
    }
    public partial record struct Mover([Sia] float Speed);
    public partial record struct Rotator([Sia] Vector3 AngularSpeed);

    public sealed class Frame : IAddon
    {
        public float Delta { get; set; }
    }

    public sealed class PositionChangePrintSystem() : SystemBase(
        Matchers.Of<Position>(),
        EventUnion.Of<Position.SetValue>())
    {
        public override void Execute(World world, IEntityQuery query)
        {
            Console.WriteLine("PositionChangePrintSystem query count: " + query.Count);
        }
    }

    public sealed class MoverUpdateSystem() : SystemBase(
        Matchers.Of<Mover, Position, Rotation>())
    {
        private Frame _frame = null!;

        public override void Initialize(World world)
        {
            _frame = world.GetAddon<Frame>();
        }

        public override void Execute(World world, IEntityQuery query)
        {
            query.ForSlice(_frame,
                static (Entity e, in Frame frame, ref Mover mover, ref Position pos, ref Rotation rot) => {
                    pos.Value += Vector3.Transform(Vector3.UnitZ, rot.Value) * mover.Speed * frame.Delta;
                    e.Send(new Position.SetValue(pos.Value));
                });
        }
    }

    public sealed class RotatorUpdateSystem() : SystemBase(
        Matchers.Of<Rotator, Rotation>())
    {
        private Frame _frame = null!;

        public override void Initialize(World world)
        {
            _frame = world.GetAddon<Frame>();
        }

        public override void Execute(World world, IEntityQuery query)
        {
            query.ForSlice(_frame,
                static (Entity e, in Frame frame, ref Rotator rotator, ref Rotation rot) => {
                    var newRot = rot.Value.ToEulerAngles() + rotator.AngularSpeed * frame.Delta;
                    rot.Value = newRot.ToQuaternion();
                    e.Send(new Rotation.SetValue(rot.Value));
                });
        }
    }

    public sealed class MoverRandomDestroySystem() : SystemBase(
        Matchers.Of<Mover, Position>())
    {
        private readonly ConcurrentStack<Entity> _entitiesToDestroy = [];

        public override void Execute(World world, IEntityQuery query)
        {
            int entityCount = world.Count;
            query.ForEachOnParallel(this, static (sys, entity) => {
                if (Random.Shared.Next(3) == 2) {
                    sys._entitiesToDestroy.Push(entity);
                }
            });
            foreach (var entity in _entitiesToDestroy) {
                entity.Destroy();
            }
            _entitiesToDestroy.Clear();
            Console.WriteLine($"Destroyed {entityCount - world.Count} entities.");
        }
    }

    public static class TestObject
    {
        public static Entity Create(World world, Vector3 position)
        {
            return world.Create(HList.From(
                new Position(position),
                new Rotation(),
                new Mover(5f),
                new Rotator(new(0, 5f, 0))
            ));
        }
    }

    public static void Run(World world)
    {
        var frame = world.AcquireAddon<Frame>();
        frame.Delta = 0.5f;

        var stage = SystemChain.Empty
            .Add<MoverUpdateSystem>()
            .Add<RotatorUpdateSystem>()
            .Add<MoverRandomDestroySystem>()
            .Add<PositionChangePrintSystem>()
            .CreateStage(world);

        for (int i = 0; i != 100000; ++i) {
            TestObject.Create(world,
                new Vector3(
                    Random.Shared.NextSingle() * 100 - 50, 0,
                    Random.Shared.NextSingle() * 100 - 50));
        }

        stage.Tick();

        var sw = new Stopwatch();
        sw.Start();
        for (int i = 0; i < 4; ++i) {
            stage.Tick();
        }
        sw.Stop();
        Console.WriteLine(sw.Elapsed);
    }
}