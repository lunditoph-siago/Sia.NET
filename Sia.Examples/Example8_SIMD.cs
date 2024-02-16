namespace Sia_Examples;

using System.Diagnostics;
using System.Numerics;
using CommunityToolkit.HighPerformance;
using Sia;

public static class Example8_SIMD
{
    public struct Number
    {
        public float Value;
    }

    public class SumSystem()
        : SystemBase(
            matcher: Matchers.Of<Number>())
    {
        public override void Execute(World world, Scheduler scheduler, IEntityQuery query)
        {
            var watch = new Stopwatch();
            watch.Start();

            float acc = 0;
            foreach (var entity in query) {
                acc += entity.Get<Number>().Value;
            }
            
            watch.Stop();
            Console.WriteLine("[SumSystem]");
            Console.WriteLine("Result: " + acc);
            Console.WriteLine("Time: " + watch.Elapsed);
        }
    }

    public class VectorizedSumSystem()
        : SystemBase(
            matcher: Matchers.Of<Number>())
    {
        public override void Execute(World world, Scheduler scheduler, IEntityQuery query)
        {
            var watch = new Stopwatch();
            watch.Start();

            float result = 0;

            foreach (var host in query.Hosts) {
                if (host is IEntityHost<Bundle<Number>> numberHost) {
                    using var numbers = numberHost.FetchAll();
                    var numbersSpan = numbers.Span;

                    var vectors = numbersSpan.Cast<Bundle<Number>, Vector<float>>();
                    var acc = Vector<float>.Zero;

                    for (int i = 0; i < vectors.Length; ++i) {
                        acc += vectors[i];
                    }

                    result = Vector.Dot(acc, Vector<float>.One);

                    for (int i = vectors.Length * Vector<float>.Count; i < numbers.Length; ++i) {
                        result += numbersSpan[i].Item1.Value;
                    }
                }
                else {
                    foreach (var entity in host) {
                        result += entity.Get<Number>().Value;
                    }
                }
            }

            watch.Stop();
            Console.WriteLine("[VectorizedSumSystem]");
            Console.WriteLine("Result: " + result);
            Console.WriteLine("Time: " + watch.Elapsed);
        }
    }

    public static void Run(World world)
    {
        var scheduler = new Scheduler();

        SystemChain.Empty
            .Add<SumSystem>()
            .Add<VectorizedSumSystem>()
            .RegisterTo(world, scheduler);
        
        for (int i = 0; i != 1000000; ++i) {
            world.CreateInArrayHost(Bundle.Create(
                new Number { Value = 1 }
            ));
        }

        scheduler.Tick();
    }
}