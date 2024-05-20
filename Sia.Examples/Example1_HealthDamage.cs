namespace Sia_Examples;

using System.Numerics;
using Sia;

public static partial class Example1_HealthDamage
{
    public class Game : IAddon
    {
        public float DeltaTime { get; private set; }
        public float Time { get; private set; }

        public Scheduler Scheduler { get; } = new();

        public void Update(float deltaTime)
        {
            DeltaTime = deltaTime;
            Time += deltaTime;
            Scheduler.Tick();
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
            public void Execute(World world, in EntityRef target)
                => new View(target, world).Value -= Value;
        }
    }

    public class HealthUpdateSystem()
        : SystemBase(Matchers.Of<Health>())
    {
        public override void Execute(World world, Scheduler scheduler, IEntityQuery query)
        {
            var game = world.GetAddon<Game>();

            foreach (var entity in query) {
                ref var health = ref entity.Get<Health>();
                if (health.Debuff != 0) {
                    entity.Modify(new Health.Damage(health.Debuff * game.DeltaTime));
                    Console.WriteLine($"Damage: HP {health.Value}");
                }
            }
        }
    }

    [AfterSystem<HealthUpdateSystem>]
    public class DeathSystem()
        : SystemBase(Matchers.Of<Health>())
    {
        public override void Execute(World world, Scheduler scheduler, IEntityQuery query)
        {
            foreach (var entity in query) {
                if (entity.Get<Health>().Value <= 0) {
                    entity.Dispose();
                    Console.WriteLine("Dead!");
                }
            }
        }
    }

    public class HealthSystems()
        : SystemBase(
            SystemChain.Empty
                .Add<HealthUpdateSystem>()
                .Add<DeathSystem>());

    public class LocationDamageSystem()
        : SystemBase(
            Matchers.Of<Transform, Health>(),
            EventUnion.Of<WorldEvents.Add<Health>, Transform.SetPosition>())
    {
        public override void Execute(World world, Scheduler scheduler, IEntityQuery query)
        {
            foreach (var entity in query) {
                var pos = entity.Get<Transform>().Position;
                var health = new Health.View(entity);

                if (pos.X == 1 && pos.Y == 1) {
                    entity.Modify(new Health.Damage(10));
                    Console.WriteLine($"Damage: HP {health.Value}");
                }
                if (pos.X == 1 && pos.Y == 2) {
                    health.Debuff = 100;
                    Console.WriteLine("Debuff!");
                }
            }
        }
    }

    [BeforeSystem<HealthSystems>]
    public class GameplaySystems()
        : SystemBase(
            SystemChain.Empty
                .Add<LocationDamageSystem>());

    public static class Player
    {
        public static EntityRef Create(World world)
            => world.CreateInArrayHost(HList.Create(
                new Transform(),
                new Health()
            ));

        public static EntityRef Create(World world, Vector2 position)
            => world.CreateInArrayHost(HList.Create(
                new Transform {
                    Position = position
                },
                new Health()
            ));
    }

    public static void Run(World world)
    {
        var game = world.AcquireAddon<Game>();

        var handle = SystemChain.Empty
            .Add<HealthSystems>()
            .Add<GameplaySystems>()
            .Add((ref Health health) => Console.WriteLine("Heath: " + health.Value),
                trigger: EventUnion.Of<Health.SetValue>())
            .Add((ref Transform transform) => Console.WriteLine("Position: " + transform.Position))
            .RegisterTo(world, game.Scheduler);
        
        var player = Player.Create(world, new(1, 1));
        game.Update(0.5f);

        var trans = new Transform.View(player) {
            Position = new(1, 2)
        };
        game.Update(0.5f);

        game.Scheduler.CreateTask(() => {
            Console.WriteLine("Callback invoked after health and gameplay systems");
            return true; // remove task
        }, handle.SystemTaskNodes);
    
        trans.Position = new(1, 3);

        game.Update(0.5f);
        game.Update(0.5f);
        game.Update(0.5f);
        game.Update(0.5f); // player dead

        handle.Dispose();
    }
}