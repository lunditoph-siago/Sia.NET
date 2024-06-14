# Sia.NET
[![Build Status](https://github.com/lunditoph-siago/Sia.NET/actions/workflows/nuget.yml/badge.svg?event=push)](https://github.com/lunditoph-siago/Sia.NET/actions/workflows/nuget.yml)
[![NuGet Badge MimeKit](https://buildstats.info/nuget/Sia)](https://www.nuget.org/packages/Sia)

Modern ECS framework for .NET

## Get started

To begin, you need to install the core package of the Sia Framework. Open your terminal and run the following command:

```console
dotnet add package Sia
```

This package contains the core features of the Sia framework.

If you would like to use the `source generation` features such as `template`, `view`, etc., you can install
the `Sia.CodeGenerators` package.

```console
dotnet add package Sia.CodeGenerators
```

> [!NOTE]
> The package uses [Roslyn Source Generators](https://docs.microsoft.com/en-us/dotnet/csharp/roslyn-sdk/source-generators-overview).
Because of this, you should use an IDE that's compatible with source generators. Previous IDE versions might experience
slow-downs or mark valid code as errors. The following IDEs are compatible with source generators:
> - Visual Studio 2022+
> - Rider 2021.3.3+
> - Visual Studio Code

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

The `BucketHost`, using a list of buckets (`List<Bucket?>`), is ideal for scenarios with large datasets that require
moderately sparse allocations.

```csharp
public static EntityRef<WithId<TEntity>> CreateInHashHost<
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TEntity>(
        this World world, in TEntity initial)
    where TEntity : struct
```

The `HashHost`, utilizing a dictionary (`Dictionary<int, T>`), is perfect for highly sparse and unpredictable datasets.

```csharp
public static EntityRef<WithId<TEntity>> CreateInArrayHost<
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TEntity>(
        this World world, in TEntity initial, int initialCapacity = 0)
    where TEntity : struct
```

The `ArrayHost`, employing a array (`T[]`), is suitable for simple, contiguous data storage needs with predictable growth.

```csharp
public static EntityRef<WithId<TEntity>> CreateInSparseHost<
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TEntity>(
        this World world, in TEntity initial, int pageSize = 256)
    where TEntity : struct
```

The `SparseHost`, using a sparse set (`SparseSet<T>`), is optimized for extremely sparse datasets, where memory
efficiency is crucial.

### Remove components from an entity

The `EntityRef` is a class inherit from `IDisposable` interface. We can either call `Dispose` or just use `unsing`
keyword to release the entity from the world.

### Read and write component values of entities

You can handle the entity by `Get` function easily, it will return a `ref` component type. You can update the value
directly.

## Systems overview

### Introduction to systems

We can register system to `world` in two approach:

1. using the `RegisterTo` method in a `SystemChain`:
   ```csharp
   SystemChain.Empty.Add<System>().RegisterTo(world, scheduler);
   ```
   system can also be registered by a lambda expression as well:
   ```csharp
   SystemChain.Empty.Add((ref object _) => {}).RegisterTo(world, scheduler);
   ```

3. using the register api in `World`:
   ```csharp
   world.RegisterSystem<System>(scheduler);
   ```

#### The core of System: ISystem

When creating a custom system, you can either inherit from interface or create from System action wrapper. You need
to review all properties and functions in the `ISystem` interface

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

1. When you inherit from `SystemBase`, `ParallelSystemBase` and etc. you can build your system as follows:

```csharp
public class MySystem(SystemChain next) : SystemBase(
    matcher: Matchers.Of<Transform>(),
    trigger: EventUnion.Of<Transform.SetPosition>(),
    filter: EventUnion.Of<HOEvents.Cancel<Transform.Lock>>(),
    children: next)
{
    public override void Initialize(World world, Scheduler scheduler)
    {
        // The logic When system is initialzed
    }
    public override void Uninitialize(World world, Scheduler scheduler)
    {
        // The logic When system un initialzed
    }
    public override void Execute(World world, Scheduler scheduler, IEntityQuery query)
    {
        // The logic When system is triggered
    }
}
```

2. The lambda style code:

```csharp
var system = (ref object obj) => {
    // The logic When system is triggered
};

var chain = SystemChain.Empty.Add(system,
    matcher: Matchers.Of<Transform>(),
    trigger: EventUnion.Of<Transform.SetPosition>(),
    filter: EventUnion.Of<HOEvents.Cancel<Transform.Lock>>());
```

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
            public void Execute(World world, Entity target)
                => new View(target, world).Value -= Value;
        }
    }

    public class HealthUpdateSystem() : SystemBase(
        Matchers.Of<Health>())
    {
        public override void Execute(World world, Scheduler scheduler, IEntityQuery query)
        {
            var game = world.GetAddon<Game>();

            foreach (var entity in query) {
                ref var health = ref entity.Get<Health>();
                if (health.Debuff != 0) {
                    entity.Modify(new Health.Damage(health.Debuff * game.DeltaTime));
                }
            }
        }
    }

    [AfterSystem<HealthUpdateSystem>]
    public class DeathSystem() : SystemBase(
        Matchers.Of<Health>())
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

    public class HealthSystems() : SystemBase(
        SystemChain.Empty
            .Add<HealthUpdateSystem>()
            .Add<DeathSystem>());

    public class LocationDamageSystem() : SystemBase(
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
                }
                if (pos.X == 1 && pos.Y == 2) {
                    health.Debuff = 100;
                }
            }
        }
    }

    [BeforeSystem<HealthSystems>]
    public class GameplaySystems() : SystemBase(
        SystemChain.Empty
            .Add<LocationDamageSystem>());
    
    [AfterSystem<HealthSystems>]
    [AfterSystem<GameplaySystems>]
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
            => world.CreateInArrayHost(HList.Create(
                new Transform(),
                new Health()
            ));

        public static Entity Create(World world, Vector2 position)
            => world.CreateInArrayHost(HList.Create(
                new Transform {
                    Position = position
                },
                new Health()
            ));
    }

    public static void Main()
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
            Console.WriteLine("Callback invoked after systems");
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
