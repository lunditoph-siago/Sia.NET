using System.Diagnostics;

namespace Sia.Tests.Systems;

public partial record struct Health([Sia] int Value);
public partial record struct Armor([Sia] int Value);
public partial record struct Damage([Sia] int Value);
public partial record struct FrameCounter([Sia] int Value);

public struct HealthSnapshot
{
    public int EntityId;
    public int HealthValue;
}

public struct FrameSnapshot
{
    public int EntityId;
    public int FrameValue;
}

public sealed class ReadHealthSystem : ExtractSystemBase
{
    public readonly List<(int entityId, int health)> Seen = [];

    public ReadHealthSystem()
        : base(matcher: Matchers.Any, extractMatcher: Matchers.Of<Health>()) {}

    public override void Execute(World world, IEntityQuery query, IEntityQuery extract)
    {
        Seen.Clear();
        extract.ForSlice((Entity entity, ref Health health) => {
            Seen.Add((entity.Id.Value, health.Value));
        });
    }
}

public sealed class CountHealthSystem : ExtractSystemBase
{
    public int Count;

    public CountHealthSystem()
        : base(matcher: Matchers.Any, extractMatcher: Matchers.Of<Health>()) {}

    public override void Execute(World world, IEntityQuery query, IEntityQuery extract)
    {
        Count = 0;
        extract.ForSlice((ref Health _) => { Count++; });
    }
}

public sealed class ReadDamageSystem : ExtractSystemBase
{
    public int Value;

    public ReadDamageSystem()
        : base(matcher: Matchers.Any, extractMatcher: Matchers.Of<Damage>()) {}

    public override void Execute(World world, IEntityQuery query, IEntityQuery extract)
    {
        extract.ForSlice((ref Damage d) => { Value = d.Value; });
    }
}

public sealed class HealthSnapshotSystem : SnapshotExtractSystem<HealthSnapshot>
{
    public readonly List<HealthSnapshot> Rendered = [];
    public int RenderCallCount;

    public HealthSnapshotSystem()
    {
        ExtractMatcherProperty = Matchers.Of<Health>();
    }

    private IEntityMatcher ExtractMatcherProperty { get; }
    protected override IEntityMatcher ExtractMatcher => ExtractMatcherProperty;

    protected override HealthSnapshot Extract(Entity entity)
    {
        ref var health = ref entity.Get<Health>();
        return new HealthSnapshot { EntityId = entity.Id.Value, HealthValue = health.Value };
    }

    protected override void Render(ReadOnlySpan<HealthSnapshot> data)
    {
        RenderCallCount++;
        Rendered.Clear();

        foreach (ref readonly var snap in data)
            Rendered.Add(snap);
    }
}

public sealed class FrameSnapshotSystem : SnapshotExtractSystem<FrameSnapshot>
{
    public readonly List<FrameSnapshot> Rendered = [];

    public FrameSnapshotSystem()
    {
        ExtractMatcherProperty = Matchers.Of<FrameCounter>();
    }

    private IEntityMatcher ExtractMatcherProperty { get; }
    protected override IEntityMatcher ExtractMatcher => ExtractMatcherProperty;

    protected override FrameSnapshot Extract(Entity entity)
    {
        ref var fc = ref entity.Get<FrameCounter>();
        return new FrameSnapshot { EntityId = entity.Id.Value, FrameValue = fc.Value };
    }

    protected override void Render(ReadOnlySpan<FrameSnapshot> data)
    {
        Rendered.Clear();

        foreach (ref readonly var s in data)
            Rendered.Add(s);
    }
}

public class ExtractSystemTests
{
    [Fact]
    public void ExtractSystemBase_ReadsParentEntitiesThroughSubWorld()
    {
        using var parent = new World();
        parent.Create(HList.From(new Health(100)));
        parent.Create(HList.From(new Health(200)));
        parent.Create(HList.From(new Health(300)));

        var addon = parent.AcquireAddon<SubWorldAddon>();
        var sub = addon.SubWorld;

        var system = new ReadHealthSystem();
        var stage = SystemChain.Empty
            .Add<ReadHealthSystem>(() => system)
            .CreateStage(sub.World);

        sub.Tick(stage);

        Assert.Equal(3, system.Seen.Count);

        var values = system.Seen.ConvertAll(s => s.health);
        Assert.Contains(100, values);
        Assert.Contains(200, values);
        Assert.Contains(300, values);

        stage.Dispose();
    }

    [Fact]
    public void ExtractSystemBase_SeesEntitiesAddedAfterInitialization()
    {
        using var parent = new World();
        parent.Create(HList.From(new Health(10)));

        var addon = parent.AcquireAddon<SubWorldAddon>();
        var sub = addon.SubWorld;

        var system = new ReadHealthSystem();
        var stage = SystemChain.Empty
            .Add<ReadHealthSystem>(() => system)
            .CreateStage(sub.World);

        parent.Create(HList.From(new Health(20)));
        parent.Create(HList.From(new Health(30)));

        sub.Tick(stage);

        Assert.Equal(3, system.Seen.Count);

        stage.Dispose();
    }

    [Fact]
    public void ExtractSystemBase_FallsBackToSameWorldWithoutSubWorld()
    {
        using var world = new World();
        Context<World>.Current = world;

        world.Create(HList.From(new Health(50)));
        world.Create(HList.From(new Health(60)));

        var system = new CountHealthSystem();
        var stage = SystemChain.Empty
            .Add<CountHealthSystem>(() => system)
            .CreateStage(world);

        stage.Tick();

        Assert.Equal(2, system.Count);

        stage.Dispose();
    }

    [Fact]
    public void ExtractSystemBase_SeesLiveDataNotFrozenSnapshot()
    {
        using var parent = new World();
        parent.Create(HList.From(new Damage(10)));

        var addon = parent.AcquireAddon<SubWorldAddon>();
        var sub = addon.SubWorld;

        var system = new ReadDamageSystem();
        var stage = SystemChain.Empty
            .Add<ReadDamageSystem>(() => system)
            .CreateStage(sub.World);

        sub.Tick(stage);
        Assert.Equal(10, system.Value);

        foreach (var entity in parent)
            if (entity.Contains<Damage>())
                entity.Get<Damage>().Value = 20;

        sub.Tick(stage);
        Assert.Equal(20, system.Value);

        stage.Dispose();
    }

    [Fact]
    public void SnapshotExtractSystem_FreezesDataDoesNotSeeLaterMutations()
    {
        using var world = new World();
        Context<World>.Current = world;

        world.Create(HList.From(new Health(100)));
        world.Create(HList.From(new Health(200)));

        var system = new HealthSnapshotSystem();
        var stage = SystemChain.Empty
            .Add<HealthSnapshotSystem>(() => system)
            .CreateStage(world);

        system.RunExtract();

        foreach (var entity in world)
            if (entity.Contains<Health>())
                entity.Get<Health>().Value = 999;

        stage.Tick();

        Assert.Equal(1, system.RenderCallCount);
        Assert.Equal(2, system.Rendered.Count);
        Assert.All(system.Rendered, s => Assert.NotEqual(999, s.HealthValue));
        Assert.Contains(system.Rendered, s => s.HealthValue == 100);
        Assert.Contains(system.Rendered, s => s.HealthValue == 200);

        stage.Dispose();
    }

    [Fact]
    public void SnapshotExtractSystem_UsesOwnWorldWithoutSubWorldContext()
    {
        using var world = new World();
        Context<World>.Current = world;

        world.Create(HList.From(new Health(42)));

        var system = new HealthSnapshotSystem();
        var stage = SystemChain.Empty
            .Add<HealthSnapshotSystem>(() => system)
            .CreateStage(world);

        system.RunExtract();

        Assert.Equal(1, system.Data.Length);
        Assert.Equal(42, system.Data[0].HealthValue);

        stage.Dispose();
    }

    [Fact]
    public void SnapshotExtractSystem_GameThreadExtractsRenderThreadRenders()
    {
        using var world = new World();
        Context<World>.Current = world;

        const int entityCount = 100;
        const int frameCount = 30;

        for (var i = 0; i < entityCount; i++)
            world.Create(HList.From(new FrameCounter(0)));

        var system = new FrameSnapshotSystem();
        var stage = SystemChain.Empty
            .Add<FrameSnapshotSystem>(() => system)
            .CreateStage(world);

        using var gameReady = new Barrier(2);
        using var renderDone = new Barrier(2);

        var running = true;
        Exception? renderError = null;

        var renderThread = new Thread(() => {
            try {
                while (running) {
                    gameReady.SignalAndWait();

                    if (!running) break;

                    stage.Tick();

                    Assert.Equal(entityCount, system.Rendered.Count);
                    Assert.All(system.Rendered, s => {
                        Assert.True(
                            s.FrameValue >= 0 && s.FrameValue <= frameCount,
                            $"Snapshot value {s.FrameValue} out of range [0, {frameCount}]");
                    });

                    renderDone.SignalAndWait();
                }
            } catch (Exception ex) {
                renderError = ex;

                try { gameReady.SignalAndWait(); } catch { }
                try { renderDone.SignalAndWait(); } catch { }
            }
        })
        { Name = "RenderThread", IsBackground = true };

        renderThread.Start();

        for (var frame = 0; frame < frameCount; frame++) {
            system.RunExtract();

            gameReady.SignalAndWait();
            renderDone.SignalAndWait();

            if (renderError is not null)
                throw new Exception("Render thread failed.", renderError);

            var nextValue = frame + 1;

            foreach (var entity in world)
                if (entity.Contains<FrameCounter>())
                    entity.Get<FrameCounter>().Value = nextValue;
        }

        running = false;
        gameReady.SignalAndWait();
        renderThread.Join();

        Assert.Null(renderError);

        stage.Dispose();
    }

    [Fact]
    public void SnapshotExtractSystem_StressConcurrentExtractAndRender()
    {
        using var world = new World();
        Context<World>.Current = world;

        const int entityCount = 100;
        const int frameCount = 30;

        for (var i = 0; i < entityCount; i++)
            world.Create(HList.From(new FrameCounter(0)));

        var system = new FrameSnapshotSystem();
        var stage = SystemChain.Empty
            .Add<FrameSnapshotSystem>(() => system)
            .CreateStage(world);

        using var barrier = new Barrier(2);

        var running = true;
        var frame = 0;
        Exception? renderError = null;

        var renderThread = new Thread(() => {
            try {
                while (running) {
                    barrier.SignalAndWait();

                    if (!running) break;

                    stage.Tick();

                    if (system.Rendered.Count != entityCount) {
                        throw new Exception(
                            $"Expected {entityCount} snapshots, got {system.Rendered.Count} at frame {frame}");
                    }

                    barrier.SignalAndWait();
                }
            }
            catch (Exception ex) {
                renderError = ex;

                try { barrier.SignalAndWait(); } catch { }
            }
        })
        { Name = "StressRenderThread", IsBackground = true };

        renderThread.Start();

        var sw = Stopwatch.StartNew();

        for (frame = 0; frame < frameCount; frame++) {
            system.RunExtract();

            barrier.SignalAndWait();

            if (renderError is not null)
                break;

            barrier.SignalAndWait();

            if (renderError is not null)
                break;

            var value = frame + 1;

            foreach (var entity in world)
                if (entity.Contains<FrameCounter>())
                    entity.Get<FrameCounter>().Value = value;
        }

        sw.Stop();

        running = false;
        barrier.SignalAndWait();
        renderThread.Join(TimeSpan.FromSeconds(5));

        Assert.Null(renderError);
        Assert.True(
            sw.Elapsed.TotalSeconds < 10,
            $"Stress test took {sw.Elapsed.TotalSeconds:F1}s, expected < 10s");

        stage.Dispose();
    }

    [Fact]
    public void ExtractChannel_PassesEventsCorrectly()
    {
        using var channel = new ExtractChannel<int>(capacity: 32);

        channel.TryWrite(1);
        channel.TryWrite(2);
        channel.TryWrite(3);

        var received = new List<int>();
        var count = channel.Drain(received.Add);

        Assert.Equal(3, count);
        Assert.Equal([1, 2, 3], received);
        Assert.False(channel.TryRead(out _));
    }

    [Fact]
    public void ExtractChannel_CompletesWriterOnDispose()
    {
        var channel = new ExtractChannel<int>(capacity: 8);

        channel.TryWrite(1);
        channel.Dispose();

        var received = new List<int>();
        channel.Drain(received.Add);

        Assert.Single(received);
        Assert.Equal(1, received[0]);
    }

    [Fact]
    public void ExtractChannel_GameThreadWritesRenderThreadDrains()
    {
        using var channel = new ExtractChannel<int>(capacity: 256);

        const int frameCount = 50;

        var running = true;
        var renderDrained = 0;
        var allReceived = new List<int>();
        var lockObj = new object();
        Exception? renderError = null;

        using var ready = new Barrier(2);
        using var done = new Barrier(2);

        var renderThread = new Thread(() => {
            try {
                while (running) {
                    ready.SignalAndWait();

                    if (!running) break;

                    var received = new List<int>();
                    channel.Drain(received.Add);

                    lock (lockObj) {
                        allReceived.AddRange(received);
                        renderDrained += received.Count;
                    }

                    done.SignalAndWait();
                }
            } catch (Exception ex) {
                renderError = ex;

                try { ready.SignalAndWait(); } catch { }
                try { done.SignalAndWait(); } catch { }
            }
        })
        { Name = "ChannelRenderThread", IsBackground = true };

        renderThread.Start();

        var totalSent = 0;

        for (var frame = 0; frame < frameCount; frame++) {
            channel.TryWrite(frame * 3);
            channel.TryWrite(frame * 3 + 1);
            channel.TryWrite(frame * 3 + 2);

            totalSent += 3;

            ready.SignalAndWait();
            done.SignalAndWait();

            if (renderError is not null)
                throw new Exception("Render thread failed.", renderError);
        }

        running = false;
        ready.SignalAndWait();
        renderThread.Join();

        Assert.Null(renderError);
        Assert.Equal(totalSent, renderDrained);
        Assert.Equal(totalSent, allReceived.Count);

        for (var frame = 0; frame < frameCount; frame++) {
            var baseIdx = frame * 3;

            Assert.Equal(frame * 3, allReceived[baseIdx]);
            Assert.Equal(frame * 3 + 1, allReceived[baseIdx + 1]);
            Assert.Equal(frame * 3 + 2, allReceived[baseIdx + 2]);
        }
    }

    [Fact]
    public void ExtractSystem_AllMechanismsWorkTogether()
    {
        using var parent = new World();
        Context<World>.Current = parent;

        parent.Create(HList.From(new Health(100), new Damage(10)));
        parent.Create(HList.From(new Health(200), new Damage(20)));

        var addon = parent.AcquireAddon<SubWorldAddon>();
        var sub = addon.SubWorld;

        var readHealth = new ReadHealthSystem();
        var readDamage = new ReadDamageSystem();

        var subStage = SystemChain.Empty
            .Add<ReadHealthSystem>(() => readHealth)
            .Add<ReadDamageSystem>(() => readDamage)
            .CreateStage(sub.World);

        var snapshot = new HealthSnapshotSystem();

        var snapStage = SystemChain.Empty
            .Add<HealthSnapshotSystem>(() => snapshot)
            .CreateStage(parent);

        using var events = new ExtractChannel<int>(capacity: 32);

        snapshot.RunExtract();
        sub.Tick(subStage);
        snapStage.Tick();

        events.TryWrite(1);
        events.TryWrite(2);

        Assert.Equal(2, readHealth.Seen.Count);
        Assert.Equal(1, snapshot.RenderCallCount);
        Assert.Equal(2, snapshot.Rendered.Count);

        var drained = new List<int>();
        events.Drain(drained.Add);

        Assert.Equal([1, 2], drained);

        foreach (var entity in parent) {
            if (entity.Contains<Health>())
                entity.Get<Health>().Value += 1000;

            if (entity.Contains<Damage>())
                entity.Get<Damage>().Value += 1000;
        }

        snapshot.RunExtract();
        snapStage.Tick();
        sub.Tick(subStage);

        Assert.All(snapshot.Rendered, s => Assert.True(s.HealthValue >= 1100));
        Assert.All(readHealth.Seen, s => Assert.True(s.health >= 1100));

        subStage.Dispose();
        snapStage.Dispose();
    }

    [Fact]
    public void ExtractSystem_ConcurrentSnapshotAndChannel()
    {
        using var world = new World();
        Context<World>.Current = world;

        world.Create(HList.From(new FrameCounter(0)));

        var snapshot = new FrameSnapshotSystem();

        var stage = SystemChain.Empty
            .Add<FrameSnapshotSystem>(() => snapshot)
            .CreateStage(world);

        using var channel = new ExtractChannel<int>(capacity: 64);

        const int frameCount = 20;

        var running = true;
        Exception? renderError = null;

        using var barrier = new Barrier(2);

        var renderThread = new Thread(() => {
            try {
                while (running) {
                    barrier.SignalAndWait();

                    if (!running) break;

                    stage.Tick();

                    var snapValue = snapshot.Rendered is [var s]
                        ? s.FrameValue
                        : -1;

                    var messages = new List<int>();
                    channel.Drain(messages.Add);

                    if (messages.Count > 0 && snapValue >= 0) {
                        Assert.All(messages, message =>
                            Assert.True(
                                message >= snapValue,
                                $"Channel msg {message} should be >= snapshot value {snapValue}"));
                    }

                    barrier.SignalAndWait();
                }
            } catch (Exception ex) {
                renderError = ex;

                try { barrier.SignalAndWait(); } catch { }
            }
        })
        { Name = "CombinedRenderThread", IsBackground = true };

        renderThread.Start();

        for (var frame = 0; frame < frameCount; frame++) {
            snapshot.RunExtract();
            channel.TryWrite(frame);

            barrier.SignalAndWait();

            if (renderError is not null)
                break;

            barrier.SignalAndWait();

            if (renderError is not null)
                break;

            world.Query(Matchers.Of<FrameCounter>())
                .ForSlice((ref FrameCounter fc) => { fc.Value = frame + 1; });
        }

        running = false;
        barrier.SignalAndWait();
        renderThread.Join(TimeSpan.FromSeconds(5));

        Assert.Null(renderError);

        stage.Dispose();
    }
}
