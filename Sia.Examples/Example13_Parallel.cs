namespace Sia_Examples;

using System.Diagnostics;
using Sia;

public static partial class Example13_Parallel
{
    private static TimeSpan _monoThreadElapsed;
    private static TimeSpan _multiThreadElapsed;
    private static TimeSpan _parallelElapsed;

    public sealed class MonoThreadUpdateSystem() : SystemBase(
        Matchers.Of<int>())
    {
        public override void Execute(World world, IEntityQuery query)
        {
            var watch = new Stopwatch();
            watch.Start();

            query.ForSlice((ref int num) => {
                num++;
            });

            watch.Stop();
            _monoThreadElapsed = watch.Elapsed;
        }
    }

    public sealed class MultiThreadUpdateSystem() : SystemBase(
        Matchers.Of<int>())
    {
        public override void Execute(World world, IEntityQuery query)
        {
            var watch = new Stopwatch();
            watch.Start();

            query.ForSliceOnParallel((ref int num) => {
                num++;
            });

            watch.Stop();
            _multiThreadElapsed = watch.Elapsed;
        }
    }

    public sealed class ParallelUpdateSystem() : ParallelSystemBase<int>()
    {
        public override void Execute(World world, IEntityQuery query)
        {
            var watch = new Stopwatch();
            watch.Start();

            base.Execute(world, query);

            watch.Stop();
            _parallelElapsed = watch.Elapsed;
        }

        protected override void HandleSlice(ref int num)
            => num++;
    }

    public static void Run(World world)
    {
        var stage = SystemChain.Empty
            .Add<MonoThreadUpdateSystem>()
            .Add<MultiThreadUpdateSystem>()
            .Add<ParallelUpdateSystem>()
            .CreateStage(world);
        
        int entityCount = 100000;
        for (int i = 0; i < entityCount; ++i) {
            world.CreateInArrayHost(HList.Create(0));
        }
        for (int i = 0; i != 20; ++i) {
            stage.Tick();
        }

        Console.WriteLine("MonoThread: " + _monoThreadElapsed);
        Console.WriteLine("MultiThread: " + _multiThreadElapsed);
        Console.WriteLine("Parallel: " + _parallelElapsed);
    }
}