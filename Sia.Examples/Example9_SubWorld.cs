namespace Sia_Examples;

using System.Diagnostics;
using Sia;

public static partial class Example9_SubWorld
{
    public partial record struct Transform(
        [Sia] float X,
        [Sia] float Y,
        [Sia] float Z);

    public partial record struct Velocity(
        [Sia] float Vx,
        [Sia] float Vy,
        [Sia] float Vz);

    public partial record struct RenderSnapshot(
        [Sia] float X,
        [Sia] float Y,
        [Sia] float Z,
        [Sia] bool Dirty);

    public partial record struct RenderState(
        [Sia] int LastRenderedFrame,
        [Sia] long DrawCallCount);

    public sealed class PhysicsSystem() : SystemBase(Matchers.Of<Transform, Velocity>())
    {
        public override void Execute(World world, IEntityQuery query)
        {
            const float dt = 1f / 60f;
            query.ForSliceOnParallel((ref Transform t, ref Velocity v) => {
                t.X += v.Vx * dt;
                t.Y += v.Vy * dt;
                t.Z += v.Vz * dt;
            });
        }
    }

    public sealed class SnapshotWriteSystem() : SystemBase(Matchers.Of<Transform, RenderSnapshot>())
    {
        public override void Execute(World world, IEntityQuery query)
        {
            query.ForSlice((ref Transform t, ref RenderSnapshot snap) => {
                snap.X = t.X;
                snap.Y = t.Y;
                snap.Z = t.Z;
                snap.Dirty = true;
            });
        }
    }

    public sealed class RenderSystem(int frameIndex) : ExtractSystemBase(
        matcher: Matchers.Any,
        extractMatcher: Matchers.Of<RenderSnapshot, RenderState>())
    {
        public long TotalDrawCalls { get; private set; }

        public override void Execute(World world, IEntityQuery query, IEntityQuery extract)
        {
            long drawCalls = 0;

            extract.ForSlice((Entity entity, ref RenderSnapshot snap, ref RenderState state) => {
                if (!snap.Dirty) return;

                // Simulate draw call work
                drawCalls++;

                // Clear the dirty flag so we don't render the same data twice.
                snap.Dirty = false;

                // Feed back render state to the game thread.
                state.LastRenderedFrame = frameIndex;
                state.DrawCallCount++;
            });

            TotalDrawCalls += drawCalls;
        }
    }

    public sealed class RenderStatsSystem(int frameIndex, Stopwatch sw) : ExtractSystemBase(
        matcher: Matchers.Any,
        extractMatcher: Matchers.Of<RenderSnapshot, RenderState>())
    {
        public override void Execute(World world, IEntityQuery query, IEntityQuery extract)
        {
            int dirty = 0;
            int skipped = 0;

            extract.ForSlice((ref RenderSnapshot snap, ref RenderState state) => {
                if (state.LastRenderedFrame == frameIndex) dirty++;
                else skipped++;
            });

            Console.WriteLine(
                $"  [Render  F{frameIndex}] rendered={dirty,4}  skipped={skipped,4}" +
                $"  elapsed={sw.Elapsed.TotalMicroseconds,8:F0} µs");
        }
    }

    public static void Run(World mainWorld)
    {
        const int entityCount = 10_000;
        const int simFrames = 5;

        var addon = mainWorld.AcquireAddon<SubWorldAddon>();
        var subWorld = addon.SubWorld;

        var gameStage = SystemChain.Empty
            .Add<PhysicsSystem>()
            .Add<SnapshotWriteSystem>()
            .CreateStage(mainWorld);

        // Populate entities
        var rng = new Random(42);

        Console.WriteLine($"  Spawning {entityCount:N0} entities …");
        for (int i = 0; i < entityCount; i++) {
            mainWorld.Create(HList.From(
                new Transform(
                    rng.NextSingle() * 100f,
                    rng.NextSingle() * 100f,
                    rng.NextSingle() * 100f),
                new Velocity(
                    (rng.NextSingle() - 0.5f) * 20f,
                    (rng.NextSingle() - 0.5f) * 20f,
                    (rng.NextSingle() - 0.5f) * 20f),
                new RenderSnapshot(0f, 0f, 0f, Dirty: false),
                new RenderState(LastRenderedFrame: -1, DrawCallCount: 0)));
        }

        // Simulate game frame N → render frame N+1 loop
        long totalDrawCalls = 0;
        var physicsWatch = new Stopwatch();
        var renderWatch = new Stopwatch();

        Console.WriteLine();
        Console.WriteLine($"  Simulating {simFrames} frame pairs …");
        Console.WriteLine();

        for (int frame = 0; frame < simFrames; frame++) {
            physicsWatch.Restart();
            Context<World>.With(mainWorld, gameStage.Tick);
            physicsWatch.Stop();

            Console.WriteLine(
                $"  [Game    F{frame}] physics+snapshot" +
                $"  elapsed={physicsWatch.Elapsed.TotalMicroseconds,8:F0} µs");

            renderWatch.Restart();

            var renderSystem = new RenderSystem(frame);
            var statsSystem = new RenderStatsSystem(frame, renderWatch);
            var renderStage = SystemChain.Empty
                .Add<RenderSystem>(() => renderSystem)
                .Add<RenderStatsSystem>(() => statsSystem)
                .CreateStage(subWorld.World);

            subWorld.Tick(renderStage);
            renderStage.Dispose();

            totalDrawCalls += renderSystem.TotalDrawCalls;
        }

        // Verify bidirectional sync: read render state back on game thread
        Console.WriteLine();
        Console.WriteLine("  --- Final sync check (game thread reads render state) ---");

        var mainQuery = mainWorld.Query(Matchers.Of<RenderState>());
        int renderedCount = 0;
        int neverRendered = 0;

        mainQuery.ForSlice((ref RenderState state) => {
            if (state.LastRenderedFrame >= 0) renderedCount++;
            else neverRendered++;
        });
        mainQuery.Dispose();

        long expectedDrawCalls = (long)simFrames * entityCount;
        Console.WriteLine($"  entities rendered at least once : {renderedCount:N0}");
        Console.WriteLine($"  entities never rendered         : {neverRendered:N0}");
        Console.WriteLine($"  total draw calls (render world) : {totalDrawCalls:N0}");
        Console.WriteLine($"  expected draw calls             : {expectedDrawCalls:N0}");
        Console.WriteLine($"  draw-call match                 : {(totalDrawCalls == expectedDrawCalls ? "PASS" : "FAIL")}");

        // Clean up
        gameStage.Dispose();
    }
}
