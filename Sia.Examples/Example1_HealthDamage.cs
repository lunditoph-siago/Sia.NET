namespace Sia.Examples;

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
        [SiaProperty] Vector2 Position,
        [SiaProperty] float Angle);

    public partial record struct Health(
        [SiaProperty] float Value,
        [SiaProperty] float Debuff)
    {
        public Health() : this(100, 0) {}

        public readonly record struct Damage(float Value) : ICommand
        {
            public void Execute(World world, in EntityRef target)
                => target.Health_SetValue(target.Get<Health>().Value - Value);
        }
    }

    public class HealthUpdateSystem()
        : SystemBase(
            matcher: Matchers.Of<Health>())
    {
        public override void Execute(World world, Scheduler scheduler, IEntityQuery query)
        {
            var game = world.GetAddon<Game>();

            query.ForEach(game, static (game, entity) => {
                ref var health = ref entity.Get<Health>();
                if (health.Debuff != 0) {
                    entity.Modify(new Health.Damage(health.Debuff * game.DeltaTime));
                    Console.WriteLine($"Damage: HP {entity.Get<Health>().Value}");
                }
            });
        }
    }

    [AfterSystem<HealthUpdateSystem>]
    public class DeathSystem()
        : SystemBase(
            matcher: Matchers.Of<Health>())
    {
        public override void Execute(World world, Scheduler scheduler, IEntityQuery query)
        {
            query.ForEach(static entity => {
                if (entity.Get<Health>().Value <= 0) {
                    entity.Dispose();
                    Console.WriteLine("Dead!");
                }
            });
        }
    }

    public class HealthSystems()
        : SystemBase(
            children: SystemChain.Empty
                .Add<HealthUpdateSystem>()
                .Add<DeathSystem>());

    public class LocationDamageSystem()
        : SystemBase(
            matcher: Matchers.Of<Transform, Health>(),
            trigger: EventUnion.Of<WorldEvents.Add, Transform.SetPosition>())
    {
        public override void Execute(World world, Scheduler scheduler, IEntityQuery query)
        {
            query.ForEach(static entity => {
                var pos = entity.Get<Transform>().Position;
                if (pos.X == 1 && pos.Y == 1) {
                    entity.Modify(new Health.Damage(10));
                    Console.WriteLine($"Damage: HP {entity.Get<Health>().Value}");
                }
                if (pos.X == 1 && pos.Y == 2) {
                    entity.Health_SetDebuff(100);
                    Console.WriteLine("Debuff!");
                }
            });
        }
    }

    [BeforeSystem<HealthSystems>]
    public class GameplaySystems()
        : SystemBase(
            children: SystemChain.Empty
                .Add<LocationDamageSystem>());

    public static class Player
    {
        public static EntityRef Create(World world)
            => world.CreateInBucketHost(Tuple.Create(
                new Transform(),
                new Health()
            ));

        public static EntityRef Create(World world, Vector2 position)
            => world.CreateInBucketHost(Tuple.Create(
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
            .RegisterTo(world, game.Scheduler);
        
        var player = Player.Create(world, new(1, 1));
        game.Update(0.5f);

        player.Transform_SetPosition(new(1, 2));
        game.Update(0.5f);

        game.Scheduler.CreateTask(() => {
            Console.WriteLine("Callback invoked after health and gameplay systems");
            return true; // remove task
        }, handle.TaskGraphNodes);
    
        player.Modify(new Transform.SetPosition(new(1, 3)));
        game.Update(0.5f);
        game.Update(0.5f);
        game.Update(0.5f);
        game.Update(0.5f); // player dead

        handle.Dispose();
    }
}