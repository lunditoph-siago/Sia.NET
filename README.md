# Sia.NET
[![Build Status](https://github.com/sicusa/Sia.NET/actions/workflows/nuget.yml/badge.svg?event=push)](https://github.com/sicusa/Sia.NET/actions/workflows/nuget.yml)
[![NuGet Badge MimeKit](https://buildstats.info/nuget/Sia)](https://www.nuget.org/packages/Sia)

Modern ECS framework for .NET

## Get started

```console
dotnet add package Sia
```

The package uses [Roslyn Source Generators](https://docs.microsoft.com/en-us/dotnet/csharp/roslyn-sdk/source-generators-overview).
Because of this, you should use an IDE that's compatible with source generators. Previous IDE versions might experience
slow-downs or mark valid code as errors. The following IDEs are compatible with source generators:

- Visual Studio 2022+
- Rider 2021.3.3+
- ~~Visual Studio Code (preview LPS)~~

## Components overview

Components represent the data in the Entity Component System (ECS) architecture. 

### Add components to an entity

To add components to an entity, we provided multiple approachs for different scenario.

```csharp
public static EntityRef<WithId<TEntity>> CreateInBucketHost<
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TEntity>(
        this World world, in TEntity initial, int bucketCapacity = 256)
    where TEntity : struct
```

```csharp
public static EntityRef<WithId<TEntity>> CreateInHashHost<
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TEntity>(
        this World world, in TEntity initial)
    where TEntity : struct
```

```csharp
public static EntityRef<WithId<TEntity>> CreateInArrayHost<
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TEntity>(
        this World world, in TEntity initial, int initialCapacity = 0)
    where TEntity : struct
```

```csharp
public static EntityRef<WithId<TEntity>> CreateInSparseHost<
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TEntity>(
        this World world, in TEntity initial, int pageSize = 256)
    where TEntity : struct
```

### Remove components from an entity

The `EntityRef` is a class inherit from `IDisposable` interface. We can call `Dispose` to release the entity from the
world.

### Read and write component values of entities

TODO: I don't known about trigger related.

## Systems overview

### Introduction to systems

We can register system to `world` in two approach:

1. use method `RegisterTo` to `SystemChain`:
   ```csharp
   SystemChain.Empty.Add<System>().RegisterTo(world, scheduler);
   ```

2. use register api in `World`:
   ```csharp
   world.RegisterSystem<System>(scheduler);
   ```

#### The core of System: ISystem

```csharp
public interface ISystem
{
    SystemChain? Children { get; }
    IEntityMatcher? Matcher { get; }
    IEventUnion? Trigger { get; }
    IEventUnion? Filter { get; }

    void Initialize(World world, Scheduler scheduler);
    void Uninitialize(World world, Scheduler scheduler);
    void Execute(World world, Scheduler scheduler, IEntityQuery query);
}
```

#### Using SystemBase

TODO: Trigger, children, Match, Filter

#### Using ParallelSystemBase

This system is a wrapper for `SystemBase`

### Iterate over component data

Iterating over data is one of the most common tasks you need to perform when you create a system. A system typically
processes a set of entities, reads data from one or more components, performs a calculation, and then writes the result
to another component.

1. Use `Enumerator` for `query` each entity:
   ```csharp
   public class System : SystemBase
   {
       public override void Execute(World world, Scheduler scheduler, IEntityQuery query)
       {
           foreach (var entity in query) {
               // Process each entity.
           }
       }
   }
   ```

2. Use `lambda` style for `query` either component or entity:
   ```csharp
   public class System : SystemBase
   {
       public override void Execute(World world, Scheduler scheduler, IEntityQuery query)
       {
           query.ForEach((ref T component) => {
               // process each component.
           });
           query.ForSliceOnParallel((ref T component) => {
               // process each component.
           });
           query.ForEach(entity => {
               // process each entity.
           });
       }
   }
   ```

## Example

```C#
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
        : SystemBase(
            matcher: Matchers.Of<Health>())
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
        : SystemBase(
            matcher: Matchers.Of<Health>())
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
            children: SystemChain.Empty
                .Add<LocationDamageSystem>());

    public static class Player
    {
        public static EntityRef Create(World world)
            => world.CreateInArrayHost(Bundle.Create(
                new Transform(),
                new Health()
            ));

        public static EntityRef Create(World world, Vector2 position)
            => world.CreateInArrayHost(Bundle.Create(
                new Transform {
                    Position = position
                },
                new Health()
            ));
    }

    public static void Run()
    {
        var world = new World();
        Context<World>.Current = world;

        var game = world.AcquireAddon<Game>();

        var handle = SystemChain.Empty
            .Add<HealthSystems>()
            .Add<GameplaySystems>()
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
```
