namespace Sia_Examples;

using System.Diagnostics;
using System.Numerics;
using CommunityToolkit.HighPerformance;
using CommunityToolkit.HighPerformance.Buffers;
using Sia;

public static partial class Example4_LoadTest
{
    public partial record struct Request([Sia] int Value);

    private static readonly List<(string Name, TimeSpan Elapsed, object Result)> _results = [];

    private static void Report(string name, TimeSpan elapsed, object result)
    {
        _results.Add((name, elapsed, result));
        Console.WriteLine($"[{name,-20}] result={result} time={elapsed}");
    }

    public sealed class ForeachSumSystem() : SystemBase(
        Matchers.Of<Request>())
    {
        public override void Execute(World world, IEntityQuery query)
        {
            var watch = Stopwatch.StartNew();
            long acc = 0;
            foreach (var entity in query) {
                acc += entity.Get<Request>().Value;
            }
            watch.Stop();
            Report("foreach", watch.Elapsed, acc);
        }
    }

    public sealed class ForSliceSumSystem() : SystemBase(
        Matchers.Of<Request>())
    {
        private long _acc;

        private void Accumulate(ref Request r) => _acc += r.Value;

        public override void Execute(World world, IEntityQuery query)
        {
            var watch = Stopwatch.StartNew();
            _acc = 0;
            query.ForSlice<Request>(Accumulate);
            watch.Stop();
            Report("ForSlice", watch.Elapsed, _acc);
        }
    }

    public sealed class SimdSumSystem() : SystemBase(
        Matchers.Of<Request>())
    {
        public override void Execute(World world, IEntityQuery query)
        {
            var watch = Stopwatch.StartNew();

            using var mem = SpanOwner<float>.Allocate(query.Count);
            query.RecordSlices(mem.Span, static (ref Request r, out float value) => {
                value = r.Value;
            });
            var span = mem.Span;

            var vectors = span.Cast<float, Vector<float>>();
            var acc = Vector<float>.Zero;
            for (var i = 0; i < vectors.Length; ++i) {
                acc += vectors[i];
            }
            var result = Vector.Dot(acc, Vector<float>.One);
            for (var i = vectors.Length * Vector<float>.Count; i < span.Length; ++i) {
                result += span[i];
            }

            watch.Stop();
            Report("SIMD", watch.Elapsed, result);
        }
    }

    public sealed class ParallelForSliceSumSystem() : SystemBase(
        Matchers.Of<Request>())
    {
        private long _acc;

        public override void Execute(World world, IEntityQuery query)
        {
            var watch = Stopwatch.StartNew();
            Interlocked.Exchange(ref _acc, 0);
            query.ForSliceOnParallel((ref Request r) => {
                Interlocked.Add(ref _acc, r.Value);
            });
            watch.Stop();
            Report("ForSliceOnParallel", watch.Elapsed, Interlocked.Read(ref _acc));
        }
    }

    public sealed class StructuredParallelSumSystem() : ParallelSystemBase<Request>
    {
        private long _acc;

        public override void Execute(World world, IEntityQuery query)
        {
            var watch = Stopwatch.StartNew();
            Interlocked.Exchange(ref _acc, 0);
            base.Execute(world, query);
            watch.Stop();
            Report("ParallelSystemBase", watch.Elapsed, Interlocked.Read(ref _acc));
        }

        protected override void HandleSlice(ref Request r)
            => Interlocked.Add(ref _acc, r.Value);
    }

    public interface IConnectionPool
    {
        void Send(int bytes);
    }

    public sealed class WorkerPoolRunner(int degreeOfParallelism)
        : ParallelRunner(degreeOfParallelism)
    {
        private sealed class ConnectionPool(int workerId) : IConnectionPool
        {
            public void Send(int bytes)
                => Console.WriteLine($"  worker {workerId} sent {bytes} bytes over its own connection");
        }

        protected override void RunWorkerThread(int id, BlockingQueue<IJob> jobs)
        {
            Context<IConnectionPool>.Current = new ConnectionPool(id);
            base.RunWorkerThread(id, jobs);
        }
    }

    private static void RunWorkerPoolDemo()
    {
        Console.WriteLine();
        Console.WriteLine("-- bonus round: a worker pool with a connection per thread --");

        var pool = new WorkerPoolRunner(4);
        var barrier = RunnerBarrier.Get();
        for (var i = 0; i < 8; i++) {
            var bytes = (i + 1) * 64;
            pool.Run(() => {
                Context.Get<IConnectionPool>().Send(bytes);
            }, barrier);
        }
        barrier.WaitAndReturn();
    }

    public static void Run(World world)
    {
        _results.Clear();

        using var stage = SystemChain.Empty
            .Add<ForeachSumSystem>()
            .Add<ForSliceSumSystem>()
            .Add<SimdSumSystem>()
            .Add<ParallelForSliceSumSystem>()
            .Add<StructuredParallelSumSystem>()
            .CreateStage(world);

        const int requestCount = 1_000_000;
        for (var i = 0; i < requestCount; ++i) {
            world.Create(HList.From(new Request(1)));
        }

        Console.WriteLine($"-- load test: summing {requestCount:N0} requests, five ways --");
        stage.Tick();

        RunWorkerPoolDemo();

        Console.WriteLine();
        Console.WriteLine("-- leaderboard (fastest first) --");
        foreach (var (name, elapsed, _) in _results.OrderBy(r => r.Elapsed)) {
            Console.WriteLine($"  {name,-20} {elapsed}");
        }
    }
}
