namespace Sia_Examples;

using Sia;

public static partial class Example4_Aggregator
{
    public readonly record struct ObjectId(int Value)
    {
        public static implicit operator ObjectId(int id)
            => new(id);
    }

    public partial record struct ComponentCount([SiaProperty] int Value);

    public record struct TestEntity(
        Aggregation<ObjectId> Aggregation,
        ComponentCount ComponentCount) : IAggregationEntity<ObjectId>
    {
        public static EntityRef Create(World world)
            => world.CreateInBucketHost(new TestEntity {
                ComponentCount = new(0)
            });
    }

    public sealed class ComponentCountSystem()
        : SystemBase(
            matcher: Matchers.Of<ComponentCount, Aggregation<ObjectId>>(),
            trigger: EventUnion.Of<Aggregation.EntityAdded, Aggregation.EntityRemoved>())
    {
        public override void Execute(World world, Scheduler scheduler, IEntityQuery query)
        {
            query.ForEach(world, static (world, entity) => {
                ref var aggr = ref entity.Get<Aggregation<ObjectId>>();
                int count = aggr.Group.Count;
                entity.ComponentCount_SetValue(count);
                Console.WriteLine($"[{aggr.Id}] Count: " + count);
            });
        }
    }

    public static void Run(World world)
    {
        var scheduler = new Scheduler();

        var aggregator = world.AcquireAddon<Aggregator<TestEntity, ObjectId>>();

        SystemChain.Empty
            .Add<ComponentCountSystem>()
            .RegisterTo(world, scheduler);
        
        world.CreateInBucketHost(Tuple.Create(
            new Sid<ObjectId>(0)
        ));

        Console.WriteLine("Tick!");
        scheduler.Tick();

        world.CreateInBucketHost(Tuple.Create(
            new Sid<ObjectId>(1)
        ));

        Console.WriteLine("Tick!");
        scheduler.Tick();

        world.CreateInBucketHost(Tuple.Create(
            new Sid<ObjectId>(1)
        ));
        var e1 = world.CreateInBucketHost(Tuple.Create(
            new Sid<ObjectId>(1)
        ));
        var e2 = world.CreateInBucketHost(Tuple.Create(
            new Sid<ObjectId>(1)
        ));

        Console.WriteLine("Tick!");
        scheduler.Tick();

        e2.SetSid<ObjectId>(2);

        Console.WriteLine("Tick!");
        scheduler.Tick();

        e1.SetSid<ObjectId>(2);
        scheduler.Tick();

        var aggr = aggregator.Find(1);
        aggr!.Value.Dispose();

        Console.WriteLine(world.Count);
    }
}