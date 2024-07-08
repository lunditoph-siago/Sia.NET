namespace Sia_Examples;

using Sia;
using Sia.Reactors;

public static partial class Example4_Aggregator
{
    public readonly record struct ObjectId(int Value)
    {
        public static implicit operator ObjectId(int id)
            => new(id);
    }

    public sealed class ComponentCountSystem() : SystemBase(
        Matchers.Of<Aggregation<ObjectId>>(),
        EventUnion.Of<Aggregation<ObjectId>.EntityAdded, Aggregation<ObjectId>.EntityRemoved>())
    {
        public override void Execute(World world, IEntityQuery query)
        {
            foreach (var entity in query) {
                ref var aggr = ref entity.Get<Aggregation<ObjectId>>();
                int count = aggr.Group.Count;
                Console.WriteLine($"[{aggr.Id}] Count: " + count);
            }
        }
    }

    public static void Run(World world)
    {
        var aggregator = world.AcquireAddon<Aggregator<ObjectId>>();

        var stage = SystemChain.Empty
            .Add<ComponentCountSystem>()
            .CreateStage(world);
        
        world.Create(HList.Create(
            new Sid<ObjectId>(0)
        ));

        Console.WriteLine("Tick!");
        stage.Tick();

        world.Create(HList.Create(
            new Sid<ObjectId>(1)
        ));

        Console.WriteLine("Tick!");
        stage.Tick();

        world.Create(HList.Create(
            new Sid<ObjectId>(1)
        ));
        var e1 = world.Create(HList.Create(
            new Sid<ObjectId>(1)
        ));
        var e2 = world.Create(HList.Create(
            new Sid<ObjectId>(1)
        ));

        Console.WriteLine("Tick!");
        stage.Tick();

        e2.SetSid(new ObjectId(2));

        Console.WriteLine("Tick!");
        stage.Tick();

        e1.SetSid(new ObjectId(2));
        stage.Tick();

        aggregator.TryGet(1, out var aggrEntity);
        aggrEntity!.Dispose();

        Console.WriteLine(world.Count);
    }
}