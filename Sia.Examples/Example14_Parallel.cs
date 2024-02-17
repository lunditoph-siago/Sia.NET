namespace Sia_Examples;

using System.Diagnostics;
using Sia;

public static partial class Example14_Parallel
{
    public sealed class MonoThreadUpdateSystem()
        : SystemBase(
            matcher: Matchers.Of<int>())
    {
        public override void Execute(World world, Scheduler scheduler, IEntityQuery query)
        {
            var watch = new Stopwatch();
            watch.Start();

            foreach (var entity in query) {
                entity.Get<int>()++;
            }

            watch.Stop();
            Console.WriteLine("MonoThread: " + watch.Elapsed);
        }
    }

    public sealed class MultiThreadUpdateSystem()
        : SystemBase(
            matcher: Matchers.Of<int>())
    {
        public override void Execute(World world, Scheduler scheduler, IEntityQuery query)
        {
            var watch = new Stopwatch();
            watch.Start();

            query.ForEachParallel(entity => {
                entity.Get<int>()++;
            });

            watch.Stop();
            Console.WriteLine("MultiThread: " + watch.Elapsed);
        }
    }

    public static void Run(World world)
    {
        var schduler = new Scheduler();

        SystemChain.Empty
            .Add<MonoThreadUpdateSystem>()
            .Add<MultiThreadUpdateSystem>()
            .RegisterTo(world, schduler);
        
        int entityCount = 100000;
        int entityPadding = 10;

        for (int i = 0; i < entityCount; ++i) {
            for (int j = 0; j < entityPadding; ++j) {
                world.CreateInArrayHost(Bundle.Create(1));
            }
            world.CreateInArrayHost(Bundle.Create(0));
        }

        var entity = world.CreateInArrayHost(Bundle.Create(0));
        
        schduler.Tick();
    }
}