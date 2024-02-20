namespace Sia_Examples;

using System.Diagnostics;
using Sia;

public static partial class Example14_Parallel
{
    private static TimeSpan _monoThreadElapsed;
    private static TimeSpan _multiThreadElapsed;

    public sealed class MonoThreadUpdateSystem()
        : SystemBase(
            matcher: Matchers.Of<int>())
    {
        public override void Execute(World world, Scheduler scheduler, IEntityQuery query)
        {
            var watch = new Stopwatch();
            watch.Start();

            query.ForEach((ref int num) => {
                num++;
            });

            watch.Stop();
            _monoThreadElapsed = watch.Elapsed;
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

            query.ForEachOnParallel((ref int num) => {
                num++;
            });

            watch.Stop();
            _multiThreadElapsed = watch.Elapsed;
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
        for (int i = 0; i < entityCount; ++i) {
            world.CreateInArrayHost(Bundle.Create(0));
        }
        for (int i = 0; i != 20; ++i) {
            schduler.Tick();
        }

        Console.WriteLine("MonoThread: " + _monoThreadElapsed);
        Console.WriteLine("MultiThread: " + _multiThreadElapsed);
    }
}