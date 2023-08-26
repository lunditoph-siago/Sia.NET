namespace Sia.Examples;

using System.Numerics;

public static partial class Example1
{
    public class GameWorld : World
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

        public class Damage
            : ImpurePropertyCommand<Damage, float>
        {
            public override void Execute(World<EntityRef> world, in EntityRef target)
                => world.Modify(target, Health.SetValue.Create(target.Get<Health>().Value - Value));
        }
    }

    public class HealthUpdateSystem : SystemBase<GameWorld>
    {
        public HealthUpdateSystem()
        {
            Matcher = Matchers.From<TypeUnion<Health>>();
        }

        public override void Execute(GameWorld world, Scheduler scheduler, in EntityRef entity)
        {
            ref var health = ref entity.Get<Health>();
            if (health.Debuff != 0) {
                world.Modify(entity, Health.Damage.Create(health.Debuff * world.DeltaTime));
                Console.WriteLine($"Damage: HP {entity.Get<Health>().Value}");
            }
        }
    }

    public class DeathSystem : SystemBase<GameWorld>
    {
        public DeathSystem()
        {
            Matcher = Matchers.From<TypeUnion<Health>>();
            Dependencies = new SystemUnion<HealthUpdateSystem>();
        }

        public override void Execute(GameWorld world, Scheduler scheduler, in EntityRef entity)
        {
            if (entity.Get<Health>().Value <= 0) {
                world.Remove(entity);
                Console.WriteLine("Dead!");
            }
        }
    }

    public class HealthSystems : SystemBase<GameWorld>
    {
        public HealthSystems()
        {
            Children = new SystemUnion<
                HealthUpdateSystem,
                DeathSystem>();
        }
    }

    public class LocationDamageSystem : SystemBase<GameWorld>
    {
        public LocationDamageSystem()
        {
            Matcher = Matchers.From<TypeUnion<Transform, Health>>();
            Trigger = new EventUnion<WorldEvents.Add, Transform.SetPosition>();
        }

        public override void Execute(GameWorld world, Scheduler scheduler, in EntityRef entity)
        {
            var pos = entity.Get<Transform>().Position;
            if (pos.X == 1 && pos.Y == 1) {
                world.Modify(entity, Health.Damage.Create(10));
                Console.WriteLine($"Damage: HP {entity.Get<Health>().Value}");
            }
            if (pos.X == 1 && pos.Y == 2) {
                world.Modify(entity, Health.SetDebuff.Create(100));
                Console.WriteLine("Debuff!");
            }
        }
    }

    public class GameplaySystems : SystemBase<GameWorld>
    {
        public GameplaySystems()
        {
            Children = new SystemUnion<LocationDamageSystem>();
            Dependencies = new SystemUnion<HealthSystems>();
        }
    }

    public struct Player
    {
        public static readonly Player Initial = new();

        public static EntityRef Create() => Create(Initial);
        public static EntityRef Create(in Player template)
            => EntityFactory<Player>.Hash.Create(template);

        public Transform Transform = new();
        public Health Health = new();

        public Player() {}
    }

    public static void Run()
    {
        var world = new GameWorld();

        var healthSystemsHandle =
            new HealthSystems().Register(world, world.Scheduler);
        var gameplaySystemsHandle =
            new GameplaySystems().Register(world, world.Scheduler);
        
        var playerRef = Player.Create(new() {
            Transform = new() {
                Position = new(1, 1)
            }
        });

        world.Add(playerRef);
        world.Update(0.5f);

        world.Modify(playerRef, Transform.SetPosition.Create(new(1, 2)));
        world.Update(0.5f);

        world.Scheduler.CreateTask(() => {
            Console.WriteLine("Callback invoked after health and gameplay systems");
            return true; // remove task
        }, new[] {healthSystemsHandle.Task, gameplaySystemsHandle.Task});
    
        world.Modify(playerRef, Transform.SetPosition.Create(new(1, 3)));
        world.Update(0.5f);
        world.Update(0.5f);
        world.Update(0.5f);
        world.Update(0.5f); // player dead

        gameplaySystemsHandle.Dispose();
        healthSystemsHandle.Dispose();
    }
}