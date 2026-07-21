using System.Diagnostics;

namespace Sia.Tests.Systems;

public partial record struct Health([Sia] int Value);
public partial record struct Armor([Sia] int Value);
public partial record struct Damage([Sia] int Value);
public partial record struct FrameCounter([Sia] int Value);

public readonly record struct HealthSnapshot(int EntityId, int HealthValue);
public readonly record struct FrameSnapshot(int EntityId, int FrameValue);

public sealed class ReadHealthSystem()
    : ExtractSystemBase(matcher: Matchers.Any, extractMatcher: Matchers.Of<Health>())
{
    public List<(int EntityId, int Health)> Seen { get; } = [];

    public override void Execute(World world, IEntityQuery query, IEntityQuery extract)
    {
        Seen.Clear();
        extract.ForSlice((Entity entity, ref Health health) => {
            Seen.Add((entity.Id.Value, health.Value));
        });
    }
}

public sealed class CountHealthSystem()
    : ExtractSystemBase(matcher: Matchers.Any, extractMatcher: Matchers.Of<Health>())
{
    public int Count { get; private set; }

    public override void Execute(World world, IEntityQuery query, IEntityQuery extract)
    {
        Count = 0;
        extract.ForSlice((ref Health _) => { Count++; });
    }
}

public sealed class ReadDamageSystem()
    : ExtractSystemBase(matcher: Matchers.Any, extractMatcher: Matchers.Of<Damage>())
{
    public int Value { get; private set; }

    public override void Execute(World world, IEntityQuery query, IEntityQuery extract)
    {
        extract.ForSlice((ref Damage damage) => { Value = damage.Value; });
    }
}

public sealed class HealthSnapshotSystem : SnapshotExtractSystem<HealthSnapshot>
{
    protected override IEntityMatcher ExtractMatcher => Matchers.Of<Health>();

    public List<HealthSnapshot> Rendered { get; } = [];
    public int RenderCallCount { get; private set; }

    protected override HealthSnapshot Extract(Entity entity)
        => new(entity.Id.Value, entity.Get<Health>().Value);

    protected override void Render(ReadOnlySpan<HealthSnapshot> data)
    {
        RenderCallCount++;
        Rendered.Clear();

        foreach (ref readonly var snapshot in data) {
            Rendered.Add(snapshot);
        }
    }
}

public sealed class FrameSnapshotSystem : SnapshotExtractSystem<FrameSnapshot>
{
    protected override IEntityMatcher ExtractMatcher => Matchers.Of<FrameCounter>();

    public List<FrameSnapshot> Rendered { get; } = [];

    protected override FrameSnapshot Extract(Entity entity)
        => new(entity.Id.Value, entity.Get<FrameCounter>().Value);

    protected override void Render(ReadOnlySpan<FrameSnapshot> data)
    {
        Rendered.Clear();

        foreach (ref readonly var snapshot in data) {
            Rendered.Add(snapshot);
        }
    }
}

public class ExtractSystemTests
{
    private sealed class RenderThreadHarness : IDisposable
    {
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

        private readonly AutoResetEvent _frameRequested = new(false);
        private readonly AutoResetEvent _frameCompleted = new(false);
        private readonly Action _renderFrame;
        private readonly Thread _thread;

        private Exception? _failure;
        private int _stopRequested;
        private int _disposed;

        public RenderThreadHarness(string threadName, Action renderFrame)
        {
            _renderFrame = renderFrame;
            _thread = new Thread(Run) {
                Name = threadName,
                IsBackground = true
            };
            _thread.Start();
        }

        public void RunFrame()
        {
            if (Volatile.Read(ref _disposed) != 0) {
                throw new ObjectDisposedException(nameof(RenderThreadHarness));
            }

            ThrowIfFailed();
            _frameRequested.Set();

            if (!_frameCompleted.WaitOne(Timeout)) {
                Volatile.Write(ref _stopRequested, 1);
                _frameRequested.Set();
                throw new TimeoutException("Render thread did not complete the frame in time.");
            }

            ThrowIfFailed();
        }

        private void Run()
        {
            while (true) {
                _frameRequested.WaitOne();

                if (Volatile.Read(ref _stopRequested) != 0) {
                    return;
                }

                try {
                    _renderFrame();
                }
                catch (Exception exception) {
                    Volatile.Write(ref _failure, exception);
                }
                finally {
                    _frameCompleted.Set();
                }

                if (Volatile.Read(ref _failure) is not null) {
                    return;
                }
            }
        }

        private void ThrowIfFailed()
        {
            if (Volatile.Read(ref _failure) is { } failure) {
                throw new InvalidOperationException("Render thread failed.", failure);
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) {
                return;
            }

            Volatile.Write(ref _stopRequested, 1);
            _frameRequested.Set();

            if (!_thread.Join(Timeout)) {
                return;
            }

            _frameRequested.Dispose();
            _frameCompleted.Dispose();
        }
    }

    [Fact]
    public void ExtractSystemBase_ReadsParentEntitiesThroughSubWorld()
    {
        using var parent = new World();
        parent.Create(HList.From(new Health(100)));
        parent.Create(HList.From(new Health(200)));
        parent.Create(HList.From(new Health(300)));

        var sub = parent.AcquireAddon<SubWorldAddon>().SubWorld;
        var system = new ReadHealthSystem();
        using var stage = SystemChain.Empty
            .Add<ReadHealthSystem>(() => system)
            .CreateStage(sub.World);

        sub.Tick(stage);

        Assert.Equal(3, system.Seen.Count);
        Assert.Equal([100, 200, 300], system.Seen.Select(entry => entry.Health).Order());
    }

    [Fact]
    public void ExtractSystemBase_SeesEntitiesAddedAfterInitialization()
    {
        using var parent = new World();
        parent.Create(HList.From(new Health(10)));

        var sub = parent.AcquireAddon<SubWorldAddon>().SubWorld;
        var system = new ReadHealthSystem();
        using var stage = SystemChain.Empty
            .Add<ReadHealthSystem>(() => system)
            .CreateStage(sub.World);

        parent.Create(HList.From(new Health(20)));
        parent.Create(HList.From(new Health(30)));
        sub.Tick(stage);

        Assert.Equal([10, 20, 30], system.Seen.Select(entry => entry.Health).Order());
    }

    [Fact]
    public void ExtractSystemBase_FallsBackToSameWorldWithoutSubWorld()
    {
        using var world = new World();
        Context<World>.Current = world;
        world.Create(HList.From(new Health(50)));
        world.Create(HList.From(new Health(60)));

        var system = new CountHealthSystem();
        using var stage = SystemChain.Empty
            .Add<CountHealthSystem>(() => system)
            .CreateStage(world);

        stage.Tick();

        Assert.Equal(2, system.Count);
    }

    [Fact]
    public void ExtractSystemBase_SeesLiveDataNotFrozenSnapshot()
    {
        using var parent = new World();
        parent.Create(HList.From(new Damage(10)));

        var sub = parent.AcquireAddon<SubWorldAddon>().SubWorld;
        var system = new ReadDamageSystem();
        using var stage = SystemChain.Empty
            .Add<ReadDamageSystem>(() => system)
            .CreateStage(sub.World);

        sub.Tick(stage);
        Assert.Equal(10, system.Value);

        foreach (var entity in parent) {
            if (entity.Contains<Damage>()) {
                entity.Get<Damage>().Value = 20;
            }
        }

        sub.Tick(stage);
        Assert.Equal(20, system.Value);
    }

    [Fact]
    public void SnapshotExtractSystem_FreezesDataDoesNotSeeLaterMutations()
    {
        using var world = new World();
        Context<World>.Current = world;
        world.Create(HList.From(new Health(100)));
        world.Create(HList.From(new Health(200)));

        var system = new HealthSnapshotSystem();
        using var stage = SystemChain.Empty
            .Add<HealthSnapshotSystem>(() => system)
            .CreateStage(world);

        system.RunExtract();

        foreach (var entity in world) {
            if (entity.Contains<Health>()) {
                entity.Get<Health>().Value = 999;
            }
        }

        stage.Tick();

        Assert.Equal(1, system.RenderCallCount);
        Assert.Equal([100, 200], system.Rendered.Select(snapshot => snapshot.HealthValue).Order());
    }

    [Fact]
    public void SnapshotExtractSystem_UsesOwnWorldWithoutSubWorldContext()
    {
        using var world = new World();
        Context<World>.Current = world;
        world.Create(HList.From(new Health(42)));

        var system = new HealthSnapshotSystem();
        using var stage = SystemChain.Empty
            .Add<HealthSnapshotSystem>(() => system)
            .CreateStage(world);

        system.RunExtract();

        var snapshot = Assert.Single(system.Data.ToArray());
        Assert.Equal(42, snapshot.HealthValue);
    }

    [Fact]
    public void SnapshotExtractSystem_GameThreadExtractsRenderThreadRenders()
    {
        using var world = new World();
        Context<World>.Current = world;

        const int entityCount = 100;
        const int frameCount = 30;

        for (var i = 0; i < entityCount; i++) {
            world.Create(HList.From(new FrameCounter(0)));
        }

        var system = new FrameSnapshotSystem();
        using var stage = SystemChain.Empty
            .Add<FrameSnapshotSystem>(() => system)
            .CreateStage(world);
        using var renderThread = new RenderThreadHarness("SnapshotRenderThread", () => {
            stage.Tick();
            Assert.Equal(entityCount, system.Rendered.Count);
            Assert.All(system.Rendered, snapshot => {
                Assert.InRange(snapshot.FrameValue, 0, frameCount);
            });
        });

        for (var frame = 0; frame < frameCount; frame++) {
            system.RunExtract();
            renderThread.RunFrame();

            foreach (var entity in world) {
                if (entity.Contains<FrameCounter>()) {
                    entity.Get<FrameCounter>().Value = frame + 1;
                }
            }
        }
    }

    [Fact]
    public void SnapshotExtractSystem_StressConcurrentExtractAndRender()
    {
        using var world = new World();
        Context<World>.Current = world;

        const int entityCount = 100;
        const int frameCount = 30;

        for (var i = 0; i < entityCount; i++) {
            world.Create(HList.From(new FrameCounter(0)));
        }

        var system = new FrameSnapshotSystem();
        using var stage = SystemChain.Empty
            .Add<FrameSnapshotSystem>(() => system)
            .CreateStage(world);

        var frame = 0;
        using var renderThread = new RenderThreadHarness("StressRenderThread", () => {
            stage.Tick();
            Assert.Equal(entityCount, system.Rendered.Count);
        });

        var stopwatch = Stopwatch.StartNew();

        for (frame = 0; frame < frameCount; frame++) {
            system.RunExtract();
            renderThread.RunFrame();

            foreach (var entity in world) {
                if (entity.Contains<FrameCounter>()) {
                    entity.Get<FrameCounter>().Value = frame + 1;
                }
            }
        }

        stopwatch.Stop();
        Assert.True(
            stopwatch.Elapsed < TimeSpan.FromSeconds(10),
            $"Stress test took {stopwatch.Elapsed.TotalSeconds:F1}s, expected < 10s.");
    }

    [Fact]
    public void ExtractChannel_PassesEventsCorrectly()
    {
        using var channel = new ExtractChannel<int>(capacity: 32);
        Assert.True(channel.TryWrite(1));
        Assert.True(channel.TryWrite(2));
        Assert.True(channel.TryWrite(3));

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
        Assert.True(channel.TryWrite(1));

        channel.Dispose();

        var received = new List<int>();
        channel.Drain(received.Add);

        Assert.Equal([1], received);
        Assert.True(channel.Reader.Completion.IsCompleted);
    }

    [Fact]
    public void ExtractChannel_GameThreadWritesRenderThreadDrains()
    {
        using var channel = new ExtractChannel<int>(capacity: 256);

        const int frameCount = 50;

        var received = new List<int>();
        using var renderThread = new RenderThreadHarness("ChannelRenderThread", () => {
            channel.Drain(received.Add);
        });

        for (var frame = 0; frame < frameCount; frame++) {
            Assert.True(channel.TryWrite(frame * 3));
            Assert.True(channel.TryWrite(frame * 3 + 1));
            Assert.True(channel.TryWrite(frame * 3 + 2));
            renderThread.RunFrame();
        }

        Assert.Equal(Enumerable.Range(0, frameCount * 3), received);
    }

    [Fact]
    public void ExtractSystem_AllMechanismsWorkTogether()
    {
        using var parent = new World();
        Context<World>.Current = parent;
        parent.Create(HList.From(new Health(100), new Damage(10)));
        parent.Create(HList.From(new Health(200), new Damage(20)));

        var sub = parent.AcquireAddon<SubWorldAddon>().SubWorld;
        var readHealth = new ReadHealthSystem();
        var readDamage = new ReadDamageSystem();
        using var subStage = SystemChain.Empty
            .Add<ReadHealthSystem>(() => readHealth)
            .Add<ReadDamageSystem>(() => readDamage)
            .CreateStage(sub.World);

        var snapshot = new HealthSnapshotSystem();
        using var snapshotStage = SystemChain.Empty
            .Add<HealthSnapshotSystem>(() => snapshot)
            .CreateStage(parent);
        using var events = new ExtractChannel<int>(capacity: 32);

        snapshot.RunExtract();
        sub.Tick(subStage);
        snapshotStage.Tick();
        Assert.True(events.TryWrite(1));
        Assert.True(events.TryWrite(2));

        Assert.Equal(2, readHealth.Seen.Count);
        Assert.Equal(1, snapshot.RenderCallCount);
        Assert.Equal(2, snapshot.Rendered.Count);

        var drained = new List<int>();
        events.Drain(drained.Add);
        Assert.Equal([1, 2], drained);

        foreach (var entity in parent) {
            if (entity.Contains<Health>()) {
                entity.Get<Health>().Value += 1000;
            }
            if (entity.Contains<Damage>()) {
                entity.Get<Damage>().Value += 1000;
            }
        }

        snapshot.RunExtract();
        snapshotStage.Tick();
        sub.Tick(subStage);

        Assert.All(snapshot.Rendered, value => Assert.True(value.HealthValue >= 1100));
        Assert.All(readHealth.Seen, value => Assert.True(value.Health >= 1100));
    }

    [Fact]
    public void ExtractSystem_ConcurrentSnapshotAndChannel()
    {
        using var world = new World();
        Context<World>.Current = world;
        world.Create(HList.From(new FrameCounter(0)));

        var snapshot = new FrameSnapshotSystem();
        using var stage = SystemChain.Empty
            .Add<FrameSnapshotSystem>(() => snapshot)
            .CreateStage(world);
        using var channel = new ExtractChannel<int>(capacity: 64);

        const int frameCount = 20;

        using var renderThread = new RenderThreadHarness("CombinedRenderThread", () => {
            stage.Tick();

            var snapshotValue = Assert.Single(snapshot.Rendered).FrameValue;
            var messages = new List<int>();
            channel.Drain(messages.Add);
            Assert.All(messages, message => Assert.True(
                message >= snapshotValue,
                $"Channel message {message} should be >= snapshot value {snapshotValue}."));
        });

        for (var frame = 0; frame < frameCount; frame++) {
            snapshot.RunExtract();
            Assert.True(channel.TryWrite(frame));
            renderThread.RunFrame();

            world.Query(Matchers.Of<FrameCounter>())
                .ForSlice((ref FrameCounter counter) => { counter.Value = frame + 1; });
        }
    }
}
