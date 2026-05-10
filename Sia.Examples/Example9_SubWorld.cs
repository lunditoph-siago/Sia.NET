namespace Sia_Examples;

using System.Diagnostics;
using Sia;

public static partial class Example9_SubWorld
{
    public partial record struct Position([Sia] float X, [Sia] float Y);
    public partial record struct Velocity([Sia] float Vx, [Sia] float Vy);

    public sealed class PhysicsSystem() : SystemBase(Matchers.Of<Position, Velocity>())
    {
        public override void Execute(World world, IEntityQuery query)
            => query.ForSlice((ref Position p, ref Velocity v) => {
                p.X += v.Vx * (1f / 60f);
                p.Y += v.Vy * (1f / 60f);
            });
    }

    public readonly struct RenderItem(int entityId, float x, float y)
    {
        public readonly int EntityId = entityId;
        public readonly float X = x;
        public readonly float Y = y;
    }

    public sealed class RenderFrame
    {
        public int GameFrame;
        public RenderItem[] Items = [];
    }

    public sealed class LatestRenderFrame
    {
        private RenderFrame? _latest;
        private int _version;

        public int PublishedVersion => Volatile.Read(ref _version);

        public void Publish(RenderFrame frame)
        {
            Volatile.Write(ref _latest, frame);
            Interlocked.Increment(ref _version);
        }

        public bool TryReadNewer(int lastVersion, out int version, out RenderFrame? frame)
        {
            version = Volatile.Read(ref _version);
            if (version == lastVersion) {
                frame = null;
                return false;
            }
            frame = Volatile.Read(ref _latest);
            return frame is not null;
        }
    }

    public static void Run(World mainWorld)
    {
        const int entityCount = 10_000;
        const int gameFrameCount = 120;

        const int lightPhysicsWorkMs = 2;
        const int heavyPhysicsWorkMs = 14;

        const int lightRenderWorkMs = 3;
        const int heavyRenderWorkMs = 24;

        const float viewMinX = -80f;
        const float viewMaxX = 80f;
        const float viewMinY = -80f;
        const float viewMaxY = 80f;

        for (var i = 0; i < entityCount; i++) {
            mainWorld.Create(HList.From(
                new Position(
                    (Random.Shared.NextSingle() - 0.5f) * 300f,
                    (Random.Shared.NextSingle() - 0.5f) * 300f),
                new Velocity(
                    (Random.Shared.NextSingle() - 0.5f) * 20f,
                    (Random.Shared.NextSingle() - 0.5f) * 20f)));
        }

        var gameStage = SystemChain.Empty
            .Add<PhysicsSystem>()
            .CreateStage(mainWorld);

        var latestFrame = new LatestRenderFrame();
        var running = true;

        var renderedFrames = new long[1];
        var skippedFrames = new long[1];
        var totalVisible = new long[1];
        var renderWaitTicks = new long[1];
        var renderWorkTicks = new long[1];
        var gameWorkTicks = new long[1];

        var swTotal = Stopwatch.StartNew();

        var renderThread = new Thread(() => {
            var lastVersion = 0;
            var lastRenderedGameFrame = -1;

            while (running || latestFrame.PublishedVersion != lastVersion) {
                var waitStart = Stopwatch.GetTimestamp();
                while (running && !latestFrame.TryReadNewer(lastVersion, out _, out _))
                    Thread.Yield();
                Interlocked.Add(ref renderWaitTicks[0], Stopwatch.GetTimestamp() - waitStart);

                if (!latestFrame.TryReadNewer(lastVersion, out var version, out var frame) || frame is null)
                    continue;

                lastVersion = version;

                if (lastRenderedGameFrame >= 0) {
                    var skipped = frame.GameFrame - lastRenderedGameFrame - 1;
                    if (skipped > 0)
                        Interlocked.Add(ref skippedFrames[0], skipped);
                }
                lastRenderedGameFrame = frame.GameFrame;

                var workStart = Stopwatch.GetTimestamp();
                var visible = 0;
                foreach (var item in frame.Items)
                    if (IsVisible(item.X, item.Y))
                        visible++;

                var renderWork = frame.GameFrame % 3 == 0
                    ? heavyRenderWorkMs
                    : lightRenderWorkMs;
                SimulateWork(renderWork);

                Interlocked.Increment(ref renderedFrames[0]);
                Interlocked.Add(ref totalVisible[0], visible);
                Interlocked.Add(ref renderWorkTicks[0], Stopwatch.GetTimestamp() - workStart);

                Console.WriteLine(
                    $"  render frame {frame.GameFrame,3} | visible {visible,5}/{frame.Items.Length:N0} | work {renderWork,2} ms | version {version,3}");
            }
        }) { Name = "Render", IsBackground = true };

        renderThread.Start();

        for (var frame = 0; frame < gameFrameCount; frame++) {
            var workStart = Stopwatch.GetTimestamp();

            gameStage.Tick();

            var snapshot = ExtractRenderFrame(mainWorld, frame, entityCount);
            latestFrame.Publish(snapshot);

            var gameWork = frame % 3 == 1
                ? heavyPhysicsWorkMs
                : lightPhysicsWorkMs;
            SimulateWork(gameWork);

            Interlocked.Add(ref gameWorkTicks[0], Stopwatch.GetTimestamp() - workStart);

            Console.WriteLine(
                $"game frame   {frame,3} | published {snapshot.Items.Length:N0} items | work {gameWork,2} ms");
        }

        running = false;
        renderThread.Join(TimeSpan.FromSeconds(10));
        gameStage.Dispose();
        swTotal.Stop();

        var elapsedMs = swTotal.Elapsed.TotalMilliseconds;
        var gameWorkMs = gameWorkTicks[0] * 1000.0 / Stopwatch.Frequency;
        var renderWorkMs = renderWorkTicks[0] * 1000.0 / Stopwatch.Frequency;
        var renderWaitMs = renderWaitTicks[0] * 1000.0 / Stopwatch.Frequency;

        Console.WriteLine();
        Console.WriteLine("Decoupled render snapshot result");
        Console.WriteLine();
        Console.WriteLine($"  entities             : {entityCount:N0}");
        Console.WriteLine($"  game frames          : {gameFrameCount:N0}");
        Console.WriteLine($"  rendered frames      : {renderedFrames[0]:N0}");
        Console.WriteLine($"  skipped game frames  : {skippedFrames[0]:N0}");
        Console.WriteLine($"  total visible items  : {totalVisible[0]:N0}");
        Console.WriteLine($"  actual draws         : {totalVisible[0]:N0}");
        Console.WriteLine($"  total elapsed        : {elapsedMs:F0} ms");
        Console.WriteLine($"  game work time       : {gameWorkMs:F0} ms");
        Console.WriteLine($"  render work time     : {renderWorkMs:F0} ms");
        Console.WriteLine($"  render wait time     : {renderWaitMs:F0} ms");
    }

    private static RenderFrame ExtractRenderFrame(World world, int gameFrame, int expectedCount)
    {
        var items = new RenderItem[expectedCount];
        var index = 0;
        foreach (var entity in world) {
            if (!entity.Contains<Position>()) continue;
            ref var p = ref entity.Get<Position>();
            items[index++] = new RenderItem(entity.Id.Value, p.X, p.Y);
        }
        if (index != items.Length) Array.Resize(ref items, index);
        return new RenderFrame { GameFrame = gameFrame, Items = items };
    }

    private static bool IsVisible(float x, float y)
        => x >= -80f && x <= 80f && y >= -80f && y <= 80f;

    private static void SimulateWork(int targetMs)
    {
        var sw = Stopwatch.StartNew();
        var value = 0.0;
        while (sw.Elapsed.TotalMilliseconds < targetMs)
            value = Math.Sin(value + 1.0);
        if (value < -999) Console.WriteLine();
    }
}
