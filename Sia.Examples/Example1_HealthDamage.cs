namespace Sia_Examples;

using System.Numerics;
using Sia;

public static partial class Example1_HealthDamage
{
    public class Game : IAddon
    {
        public float DeltaTime { get; private set; }
        public float Time { get; private set; }

        public void BeginFrame(float deltaTime)
        {
            DeltaTime = deltaTime;
            Time += deltaTime;
        }
    }

    public partial record struct Transform(
        [Sia] Vector2 Position,
        [Sia] float Angle);

    public partial record struct Health(
        [Sia] float Value,
        [Sia] float Debuff)
    {
        public Health() : this(100, 0) {}

        public readonly record struct Damage(float Value) : ICommand
        {
            public void Execute(World world, Entity target)
                => new View(target, world).Value -= Value;
        }
    }

    public class HealthUpdateSystem() : SystemBase(
        Matchers.Of<Health>())
    {
        public override void Execute(World world, IEntityQuery query)
        {
            var game = world.GetAddon<Game>();

            foreach (var entity in query) {
                ref var health = ref entity.Get<Health>();
                if (health.Debuff != 0) {
                    entity.Execute(new Health.Damage(health.Debuff * game.DeltaTime));
                }
            }
        }
    }

    public class DeathSystem() : SystemBase(
        Matchers.Of<Health>())
    {
        public override void Execute(World world, IEntityQuery query)
        {
            // faster than foreach
            query.ForSlice((Entity entity, ref Health health) => {
                if (health.Value <= 0) {
                    entity.Destroy();
                    Console.WriteLine("Dead!");
                }
            });
        }
    }

    public class HealthSystems() : SystemBase(
        SystemChain.Empty
            .Add<HealthUpdateSystem>()
            .Add<DeathSystem>());

    public class LocationDamageSystem() : SystemBase(
        Matchers.Of<Transform, Health>(),
        EventUnion.Of<WorldEvents.Add<Health>, Transform.SetPosition>())
    {
        public override void Execute(World world, IEntityQuery query)
        {
            foreach (var entity in query) {
                var pos = entity.Get<Transform>().Position;
                var health = new Health.View(entity);

                if (pos.X == 1 && pos.Y == 1) {
                    entity.Execute(new Health.Damage(10));
                }
                if (pos.X == 1 && pos.Y == 2) {
                    health.Debuff = 100;
                }
            }
        }
    }

    public class GameplaySystems() : SystemBase(
        SystemChain.Empty
            .Add<LocationDamageSystem>());
    
    public class MonitorSystems() : SystemBase(
        SystemChain.Empty
            .Add((ref Health health) => Console.WriteLine("Damage: HP " + health.Value),
                trigger: EventUnion.Of<Health.Damage>())
            .Add((ref Health health) => Console.WriteLine("Set Debuff: " + health.Debuff),
                trigger: EventUnion.Of<Health.SetDebuff>())
            .Add((ref Transform transform) => Console.WriteLine("Position: " + transform.Position)));

    public static class Player
    {
        public static Entity Create(World world)
            => world.Create(HList.From(
                new Transform(),
                new Health()
            ));

        public static Entity Create(World world, Vector2 position)
            => world.Create(HList.From(
                new Transform {
                    Position = position
                },
                new Health()
            ));
    }

    public static void Run(World world)
    {
        var game = world.AcquireAddon<Game>();

        var stage = SystemChain.Empty
            .Add<HealthSystems>()
            .Add<GameplaySystems>()
            .Add<MonitorSystems>()
            .CreateStage(world);
        
        var player = Player.Create(world, new(1, 1));
        game.BeginFrame(0.5f);
        stage.Tick();

        var trans = new Transform.View(player) {
            Position = new(1, 2)
        };
        game.BeginFrame(0.5f);
        stage.Tick();
        trans.Position = new(1, 3);

        game.BeginFrame(0.5f);
        stage.Tick();

        game.BeginFrame(0.5f);
        stage.Tick();

        game.BeginFrame(0.5f);
        stage.Tick();

        game.BeginFrame(0.5f); // player dead
        stage.Tick();

        stage.Dispose();
    }
}