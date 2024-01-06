namespace Sia_Examples;

using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
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

            using var spanOwner = query.Record((in EntityRef entity, ref float value) => {
                value = entity.Get<Number>().Value;
            });
            var span = spanOwner.Span;

            float acc = 0;
            for (int i = 0; i != span.Length; ++i) {
                acc += span[i];
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

            using var spanOwner = query.Record((in EntityRef entity, ref float value) => {
                value = entity.Get<Number>().Value;
            });

            var numbers = spanOwner.Span;
            var vectors = MemoryMarshal.Cast<float, Vector<float>>(numbers);

            var acc = Vector<float>.Zero;
            for (int i = 0; i < vectors.Length; ++i) {
                acc += vectors[i];
            }

            float result = Vector.Dot(acc, Vector<float>.One);
            for (int i = vectors.Length * Vector<float>.Count; i < numbers.Length; ++i) {
                result += numbers[i];
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
            world.CreateInBucketHost(Tuple.Create(
                new Number { Value = 1 }
            ));
        }

        scheduler.Tick();
    }
}