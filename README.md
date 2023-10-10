# Sia.NET

Modern ECS framework for .NET

## Example

```C#
using System.Numerics;
using Sia;

public static partial class Example1
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
                => world.Modify(target, new SetValue(target.Get<Health>().Value - Value));
        }
    }


    public class HealthUpdateSystem : SystemBase
    {
        private World? _world;
        private Game? _game;

        public HealthUpdateSystem()
        {
            Matcher = Matchers.Of<Health>();
        }

        public override void Initialize(World world, Scheduler scheduler)
        {
            _world = world;
            _game = world.GetAddon<Game>();
        }

        public override void Execute(World world, Scheduler scheduler, IEntityQuery query)
        {
            query.ForEach(this, static (sys, entity) => {
                ref var health = ref entity.Get<Health>();
                if (health.Debuff != 0) {
                    sys._world!.Modify(entity, new Health.Damage(health.Debuff * sys._game!.DeltaTime));
                    Console.WriteLine($"Damage: HP {entity.Get<Health>().Value}");
                }
            });
        }
    }

    public class DeathSystem : SystemBase
    {
        public DeathSystem()
        {
            Matcher = Matchers.Of<Health>();
        }

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

    public class HealthSystems : SystemBase
    {
        public HealthSystems()
        {
            Children = SystemChain.Empty
                .Add<HealthUpdateSystem>()
                .Add<DeathSystem>();
        }
    }

    public class LocationDamageSystem : SystemBase
    {
        public LocationDamageSystem()
        {
            Matcher = Matchers.Of<Transform, Health>();
            Trigger = new EventUnion<WorldEvents.Add, Transform.SetPosition>();
        }

        public override void Execute(World world, Scheduler scheduler, IEntityQuery query)
        {
            query.ForEach(world, static (world, entity) => {
                var pos = entity.Get<Transform>().Position;
                if (pos.X == 1 && pos.Y == 1) {
                    world.Modify(entity, new Health.Damage(10));
                    Console.WriteLine($"Damage: HP {entity.Get<Health>().Value}");
                }
                if (pos.X == 1 && pos.Y == 2) {
                    world.Modify(entity, new Health.SetDebuff(100));
                    Console.WriteLine("Debuff!");
                }
            });
        }
    }

    [BeforeSystem<HealthSystems>]
    public class GameplaySystems : SystemBase
    {
        public GameplaySystems()
        {
            Children = SystemChain.Empty
                .Add<LocationDamageSystem>();
        }
    }

    public static class Player
    {
        public static EntityRef Create(World world)
            => world.CreateInHashHost(Tuple.Create(
                new Transform(),
                new Health()
            ));

        public static EntityRef Create(World world, Vector2 position)
            => world.CreateInHashHost(Tuple.Create(
                new Transform {
                    Position = position
                },
                new Health()
            ));
    }

    public static void Run()
    {
        var world = new World();
        var game = world.AcquireAddon<Game>();

        var healthSystemsHandle =
            world.RegisterSystem<HealthSystems>(game.Scheduler);
        var gameplaySystemsHandle =
            world.RegisterSystem<GameplaySystems>(game.Scheduler);
        
        var playerRef = Player.Create(world, new(1, 1));
        game.Update(0.5f);

        world.Modify(playerRef, new Transform.SetPosition(new(1, 2)));
        game.Update(0.5f);

        game.Scheduler.CreateTask(() => {
            Console.WriteLine("Callback invoked after health and gameplay systems");
            return true; // remove task
        }, new[] {healthSystemsHandle.TaskGraphNode, gameplaySystemsHandle.TaskGraphNode});
    
        world.Modify(playerRef, new Transform.SetPosition(new(1, 3)));
        game.Update(0.5f);
        game.Update(0.5f);
        game.Update(0.5f);
        game.Update(0.5f); // player dead

        gameplaySystemsHandle.Dispose();
        healthSystemsHandle.Dispose();
    }
}
```