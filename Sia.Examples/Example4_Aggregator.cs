namespace Sia_Examples;

using Sia;

public static partial class Example4_Aggregator
{
    public readonly record struct ObjectId(int Value)
    {
        public static implicit operator ObjectId(int id)
            => new(id);
    }

    public sealed class ComponentCountSystem()
        : SystemBase(
            matcher: Matchers.Of<Aggregation<ObjectId>>(),
            trigger: EventUnion.Of<Aggregation.EntityAdded, Aggregation.EntityRemoved>())
    {
        public override void Execute(World world, Scheduler scheduler, IEntityQuery query)
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
        var scheduler = new Scheduler();

        var aggregator = world.AcquireAddon<Aggregator<ObjectId>>();

        SystemChain.Empty
            .Add<ComponentCountSystem>()
            .RegisterTo(world, scheduler);
        
        world.CreateInArrayHost(HList.Create(
            new Sid<ObjectId>(0)
        ));

        Console.WriteLine("Tick!");
        scheduler.Tick();

        world.CreateInArrayHost(HList.Create(
            new Sid<ObjectId>(1)
        ));

        Console.WriteLine("Tick!");
        scheduler.Tick();

        world.CreateInArrayHost(HList.Create(
            new Sid<ObjectId>(1)
        ));
        var e1 = world.CreateInArrayHost(HList.Create(
            new Sid<ObjectId>(1)
        ));
        var e2 = world.CreateInArrayHost(HList.Create(
            new Sid<ObjectId>(1)
        ));

        Console.WriteLine("Tick!");
        scheduler.Tick();

        e2.SetSid(new ObjectId(2));

        Console.WriteLine("Tick!");
        scheduler.Tick();

        e1.SetSid(new ObjectId(2));
        scheduler.Tick();

        var aggr = aggregator.Find(1);
        aggr!.Value.Dispose();

        Console.WriteLine(world.Count);
    }
}