namespace Sia_Examples;

using System.Diagnostics;
using Sia;

public static class Example8_Sum
{
    public struct Number
    {
        public int Value;
    }

    public class SumSystem()
        : SystemBase(
            matcher: Matchers.Of<Number>())
    {
        private int _acc = 0;

        private void Accumulate(ref Number num)
            => _acc += num.Value;

        public override void Execute(World world, Scheduler scheduler, IEntityQuery query)
        {
            var watch = new Stopwatch();
            watch.Start();

            _acc = 0;
            query.ForEach<Number>(Accumulate);
            
            watch.Stop();
            Console.WriteLine("[SumSystem]");
            Console.WriteLine("Result: " + _acc);
            Console.WriteLine("Time: " + watch.Elapsed);
        }
    }

    public static void Run(World world)
    {
        var scheduler = new Scheduler();

        SystemChain.Empty
            .Add<SumSystem>()
            .RegisterTo(world, scheduler);
        
        for (int i = 0; i != 1000000; ++i) {
            world.CreateInArrayHost(Bundle.Create(
                new Number { Value = 1 }
            ));
        }

        scheduler.Tick();
    }
}