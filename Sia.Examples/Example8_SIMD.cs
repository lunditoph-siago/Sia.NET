namespace Sia_Examples;

using System.Diagnostics;
using System.Numerics;
using CommunityToolkit.HighPerformance;
using CommunityToolkit.HighPerformance.Buffers;
using Sia;

public static class Example8_Sum
{
    public struct Number
    {
        public int Value;
    }

    public class SumSystem() : SystemBase(
        Matchers.Of<Number>())
    {
        private int _acc = 0;

        private void Accumulate(ref Number num)
            => _acc += num.Value;

        public override void Execute(World world, IEntityQuery query)
        {
            var watch = new Stopwatch();
            watch.Start();

            _acc = 0;
            query.ForSlice<Number>(Accumulate);
            
            watch.Stop();
            Console.WriteLine("[SumSystem]");
            Console.WriteLine("Result: " + _acc);
            Console.WriteLine("Time: " + watch.Elapsed);
        }
    }

    public class RecordSumSystem() : SystemBase(
        Matchers.Of<Number>())
    {
        public override void Execute(World world, IEntityQuery query)
        {
            var watch = new Stopwatch();
            watch.Start();

            using var mem = SpanOwner<float>.Allocate(query.Count);
            query.RecordSlices(mem.DangerousGetArray(), static (ref Number num, out float value) => {
                value = num.Value;
            });
            var result = mem.DangerousGetArray().Sum();
            
            watch.Stop();
            Console.WriteLine("[RecordSumSystem]");
            Console.WriteLine("Result: " + result);
            Console.WriteLine("Time: " + watch.Elapsed);
        }
    }

    public class VectorizedSumSystem() : SystemBase(
        Matchers.Of<Number>())
    {
        public override void Execute(World world, IEntityQuery query)
        {
            var watch = new Stopwatch();
            watch.Start();

            var mem = SpanOwner<float>.Allocate(query.Count);
            query.RecordSlices(mem.Span, static (ref Number num, out float value) => {
                value = num.Value;
            });
            var span = mem.Span;

            var vectors = span.Cast<float, Vector<float>>();
            var acc = Vector<float>.Zero;

            for (int i = 0; i < vectors.Length; ++i) {
                acc += vectors[i];
            }

            var result = Vector.Dot(acc, Vector<float>.One);

            for (int i = vectors.Length * Vector<float>.Count; i < span.Length; ++i) {
                result += span[i];
            }

            watch.Stop();
            Console.WriteLine("[VectorizedSumSystem]");
            Console.WriteLine("Result: " + result);
            Console.WriteLine("Time: " + watch.Elapsed);
        }
    }

    public static void Run(World world)
    {
        var stage = SystemChain.Empty
            .Add<SumSystem>()
            .Add<RecordSumSystem>()
            .Add<VectorizedSumSystem>()
            .CreateStage(world);
        
        for (int i = 0; i != 1000000; ++i) {
            world.Create(HList.Create(
                new Number { Value = 1 }
            ));
        }

        stage.Tick();
    }
}