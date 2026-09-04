namespace Sia.Tests.Reactive;

using global::Sia.Reactive;
using global::Sia.Reactors;

public class ReactorHooksInteropTests(QueryTestHelpers helpers) : IClassFixture<QueryTestHelpers>
{
    public readonly record struct Marker;
    public readonly record struct Extra;
    public sealed class SceneTag;

    private readonly record struct LogCapture(List<string> Log);

    private sealed class TestReactor : ReactorBase<TypeUnion<Marker>>
    {
        public readonly List<string> Log = [];

        protected override void OnEntityAdded(Entity entity) => Log.Add($"added:{entity.Id}");
        protected override void OnEntityRemoved(Entity entity) => Log.Add($"removed:{entity.Id}");
    }

    [Fact]
    public void QuerySubscription_FailedInitialReplayDetachesEveryHandler()
    {
        using var world = new World();
        var existing = world.Create(HList.From(new Marker()));
        var addedCalls = 0;
        var removedCalls = 0;

        var error = Assert.Throws<InvalidOperationException>(() => {
            _ = new QuerySubscription(
                world.Query<TypeUnion<Marker>>(),
                _ => {
                    addedCalls++;
                    throw new InvalidOperationException("replay failed");
                },
                _ => removedCalls++);
        });

        Assert.Equal("replay failed", error.Message);
        Assert.Equal(1, addedCalls);

        existing.Destroy();
        world.Create(HList.From(new Marker()));
        world.Create(HList.From(new Marker(), new Extra()));
        Assert.Equal(1, addedCalls);
        Assert.Equal(0, removedCalls);
    }

    [Fact]
    public void QuerySubscription_HostRemovalDetachesBeforeCallbacks()
    {
        using var world = new World();
        world.Create(HList.From(new Marker()));
        var removedCalls = 0;
        using var subscription = new QuerySubscription(
            world.Query<TypeUnion<Marker>>(),
            _ => { },
            entity => {
                removedCalls++;
                if (removedCalls == 1) {
                    entity.Destroy();
                }
            });

        world.ClearHosts();

        Assert.Equal(1, removedCalls);
    }

    [Fact]
    public void UseQuery_ReplaysPreExistingEntities_AcrossMultipleArchetypes()
    {
        using var world = new World();
        world.AcquireAddon<Reconciler>();

        var bare = world.Create(HList.From(new Marker()));
        var withExtra = world.Create(HList.From(new Marker(), new Extra()));
        var log = new List<string>();

        world.Mount(
            (in Unit _, ref Hooks hooks) => {
                hooks.UseQuery<TypeUnion<Marker>, LogCapture>(
                    new LogCapture(log),
                    static (Entity e, in LogCapture c) => c.Log.Add($"added:{e.Id}"),
                    static (Entity e, in LogCapture c) => c.Log.Add($"removed:{e.Id}"));
                return Reactive.None;
            },
            default(Unit));

        Assert.Contains($"added:{bare.Id}", log);
        Assert.Contains($"added:{withExtra.Id}", log);
        Assert.Equal(2, log.Count);
    }

    [Fact]
    public void UseQuery_TracksRealTimeAddAndRemove_AfterMount()
    {
        using var world = new World();
        world.AcquireAddon<Reconciler>();
        var log = new List<string>();

        world.Mount(
            (in Unit _, ref Hooks hooks) => {
                hooks.UseQuery<TypeUnion<Marker>, LogCapture>(
                    new LogCapture(log),
                    static (Entity e, in LogCapture c) => c.Log.Add($"added:{e.Id}"),
                    static (Entity e, in LogCapture c) => c.Log.Add($"removed:{e.Id}"));
                return Reactive.None;
            },
            default(Unit));

        Assert.Empty(log);

        var a = world.Create(HList.From(new Marker()));
        Assert.Equal([$"added:{a.Id}"], log);

        var b = world.Create(HList.From(new Marker()));
        Assert.Equal([$"added:{a.Id}", $"added:{b.Id}"], log);

        a.Destroy();
        Assert.Equal([$"added:{a.Id}", $"added:{b.Id}", $"removed:{a.Id}"], log);
    }

    [Fact]
    public void UseQueryAndReactorBase_TrackComponentAddRemoveMigrationIdentically()
    {
        using var world = new World();
        world.AcquireAddon<Reconciler>();
        var reactor = world.AcquireAddon<TestReactor>();
        var hookLog = new List<string>();

        var entity = world.Create(HList.From(new Extra()));

        world.Mount(
            (in Unit _, ref Hooks hooks) => {
                hooks.UseQuery<TypeUnion<Marker>, LogCapture>(
                    new LogCapture(hookLog),
                    static (Entity e, in LogCapture c) => c.Log.Add($"added:{e.Id}"),
                    static (Entity e, in LogCapture c) => c.Log.Add($"removed:{e.Id}"));
                return Reactive.None;
            },
            default(Unit));

        entity.Add<Marker>();
        entity.Remove<Marker>();

        Assert.Equal([$"added:{entity.Id}", $"removed:{entity.Id}"], hookLog);
        Assert.Equal([$"added:{entity.Id}", $"removed:{entity.Id}"], reactor.Log);
        Assert.Equal(reactor.Log, hookLog);
    }

    [Fact]
    public void UseQuery_RefreshesCaptureAndCallbacksAcrossRenders_WithoutReplayingOrRemounting()
    {
        using var world = new World();
        world.AcquireAddon<Reconciler>();
        var logA = new List<string>();
        var logB = new List<string>();

        var mount = world.Mount(
            (in Unit _, ref Hooks hooks) => {
                var generation = hooks.UseState(0);
                var log = generation.Value == 0 ? logA : logB;
                hooks.UseQuery<TypeUnion<Marker>, LogCapture>(
                    new LogCapture(log),
                    static (Entity e, in LogCapture c) => c.Log.Add($"added:{e.Id}"),
                    static (Entity e, in LogCapture c) => c.Log.Add($"removed:{e.Id}"));
                return Reactive.None;
            },
            default(Unit));

        var pre = world.Create(HList.From(new Marker()));
        Assert.Equal([$"added:{pre.Id}"], logA);

        mount.GetState<int>(0).Set(1);
        world.FlushReactive();

        Assert.Equal([$"added:{pre.Id}"], logA);
        Assert.Empty(logB);

        var post = world.Create(HList.From(new Marker()));
        Assert.Equal([$"added:{pre.Id}"], logA);
        Assert.Equal([$"added:{post.Id}"], logB);
    }

    [Fact]
    public void UseQuery_UnmountDetachesSubscription_AndDoesNotBreakANewMount()
    {
        using var world = new World();
        world.AcquireAddon<Reconciler>();
        var log = new List<string>();

        var mount = world.Mount(
            (in Unit _, ref Hooks hooks) => {
                hooks.UseQuery<TypeUnion<Marker>, LogCapture>(
                    new LogCapture(log),
                    static (Entity e, in LogCapture c) => c.Log.Add($"added:{e.Id}"),
                    static (Entity e, in LogCapture c) => c.Log.Add($"removed:{e.Id}"));
                return Reactive.None;
            },
            default(Unit));

        var before = world.Create(HList.From(new Marker()));
        Assert.Single(log);

        mount.Unmount();
        log.Clear();

        before.Destroy();
        var afterUnmount = world.Create(HList.From(new Marker()));
        Assert.Empty(log);

        var log2 = new List<string>();
        world.Mount(
            (in Unit _, ref Hooks hooks) => {
                hooks.UseQuery<TypeUnion<Marker>, LogCapture>(
                    new LogCapture(log2),
                    static (Entity e, in LogCapture c) => c.Log.Add($"added:{e.Id}"),
                    static (Entity e, in LogCapture c) => c.Log.Add($"removed:{e.Id}"));
                return Reactive.None;
            },
            default(Unit));

        Assert.Equal([$"added:{afterUnmount.Id}"], log2);
    }

    [Fact]
    public void HooksComponent_CombinesUseQuery_ReactiveOn_AndSiaCommands_InOneComponent()
    {
        using var world = new World();
        world.AcquireAddon<Reconciler>();
        var log = new List<string>();

        var pre = world.Create(HList.From(new Sid<string>("pre")));

        world.Mount(
            (in Unit _, ref Hooks hooks) => {
                hooks.UseQuery<TypeUnion<Sid<string>>, LogCapture>(
                    new LogCapture(log),
                    static (Entity e, in LogCapture c) => c.Log.Add($"query-added:{e.Id}"),
                    static (Entity e, in LogCapture c) => c.Log.Add($"query-removed:{e.Id}"));

                return Reactive.Entity(
                    HList.From(new Marker(), new Sid<string>("output")),
                    Reactive.Group(
                        Reactive.On<WorldEvents.Set<Marker>, LogCapture>(
                            new LogCapture(log),
                            static (in WorldEvents.Set<Marker> _, in LogCapture c) => c.Log.Add("set-event")),
                        Reactive.On<Sid<string>.SetValue, LogCapture>(
                            new LogCapture(log),
                            static (in Sid<string>.SetValue e, in LogCapture c) => c.Log.Add($"sid-command:{e.Value}"))));
            },
            default(Unit));

        Assert.Contains($"query-added:{pre.Id}", log);

        var output = helpers.FindSingle<Marker>(world);
        Assert.Contains($"query-added:{output.Id}", log);

        output.Set(new Marker());
        Assert.Contains("set-event", log);

        output.Execute(new Sid<string>.SetValue("renamed"));
        Assert.Contains("sid-command:renamed", log);
    }

    [Fact]
    public void ReactorBaseAndHooksUseQuery_ObserveSameMatcher_IdenticallyAndIndependently()
    {
        using var world = new World();
        world.AcquireAddon<Reconciler>();
        var reactor = world.AcquireAddon<TestReactor>();
        var hookLog = new List<string>();

        world.Mount(
            (in Unit _, ref Hooks hooks) => {
                hooks.UseQuery<TypeUnion<Marker>, LogCapture>(
                    new LogCapture(hookLog),
                    static (Entity e, in LogCapture c) => c.Log.Add($"added:{e.Id}"),
                    static (Entity e, in LogCapture c) => c.Log.Add($"removed:{e.Id}"));
                return Reactive.None;
            },
            default(Unit));

        var pre = world.Create(HList.From(new Marker()));
        var a = world.Create(HList.From(new Marker()));
        var b = world.Create(HList.From(new Marker()));
        a.Destroy();
        b.Add<Extra>();

        Assert.Equal(reactor.Log, hookLog);
    }

    public readonly record struct CategoryId(string Value);
    private readonly record struct EnableLogCapture(List<(Entity Target, bool Enabled)> Log);

    [Fact]
    public void MixedReactorStack_HierarchyMapperAggregator_PlusHooksComponent_StayConsistent()
    {
        using var world = new World();
        world.AcquireAddon<Reconciler>();
        var hierarchy = world.AcquireAddon<Hierarchy<SceneTag>>();
        var mapper = world.AcquireAddon<Mapper<string>>();
        var aggregator = world.AcquireAddon<Aggregator<CategoryId>>();

        var aliveLog = new List<string>();
        var enableLog = new List<(Entity Target, bool Enabled)>();

        world.Mount(
            (in Unit _, ref Hooks hooks) => {
                hooks.UseQuery<TypeUnion<Node<SceneTag>>, LogCapture>(
                    new LogCapture(aliveLog),
                    static (Entity e, in LogCapture c) => c.Log.Add($"alive+:{e.Id}"),
                    static (Entity e, in LogCapture c) => c.Log.Add($"alive-:{e.Id}"));

                hooks.UseEvent<Node<SceneTag>.OnIsEnabledChanged, EnableLogCapture>(
                    new EnableLogCapture(enableLog),
                    static (Entity target, in Node<SceneTag>.OnIsEnabledChanged e, in EnableLogCapture c) =>
                        c.Log.Add((target, e.Enabled)));

                return Reactive.None;
            },
            default(Unit));

        var root = world.Create(HList.From(
            new Node<SceneTag>(), new Sid<string>("root"), new Sid<CategoryId>(new CategoryId("npc"))));
        var child1 = world.Create(HList.From(
            new Node<SceneTag>(root), new Sid<string>("child1"), new Sid<CategoryId>(new CategoryId("npc"))));
        var child2 = world.Create(HList.From(
            new Node<SceneTag>(root), new Sid<string>("child2"), new Sid<CategoryId>(new CategoryId("prop"))));
        var grandchild = world.Create(HList.From(
            new Node<SceneTag>(child1), new Sid<string>("grandchild"), new Sid<CategoryId>(new CategoryId("npc"))));

        var all = new[] { root, child1, child2, grandchild };
        Assert.Equal(all.Select(e => $"alive+:{e.Id}"), aliveLog);

        Assert.Single(hierarchy.Root);
        Assert.Contains(root, hierarchy.Root);
        Assert.Equal(
            new HashSet<Entity> { child1, child2 },
            root.Get<Node<SceneTag>>().Children.ToHashSet());
        Assert.Equal(
            new HashSet<Entity> { grandchild },
            child1.Get<Node<SceneTag>>().Children.ToHashSet());

        Assert.Equal(root, mapper["root"]);
        Assert.Equal(child1, mapper["child1"]);
        Assert.Equal(child2, mapper["child2"]);
        Assert.Equal(grandchild, mapper["grandchild"]);

        Assert.True(aggregator.TryGet(new CategoryId("npc"), out var npcAggr));
        Assert.Equal(
            new HashSet<Entity> { root, child1, grandchild },
            npcAggr.Get<Aggregation<CategoryId>>().Group);
        Assert.True(aggregator.TryGet(new CategoryId("prop"), out var propAggr));
        Assert.Equal(
            new HashSet<Entity> { child2 },
            propAggr.Get<Aggregation<CategoryId>>().Group);

        child2.Execute(new Sid<string>.SetValue("child2-renamed"));
        Assert.False(mapper.ContainsKey("child2"));
        Assert.Equal(child2, mapper["child2-renamed"]);

        world.Execute(root, new Node<SceneTag>.SetIsSelfEnabled(false));
        Assert.Equal(
            all.Select(e => (e, false)).ToHashSet(),
            enableLog.ToHashSet());

        grandchild.Destroy();
        Assert.Contains($"alive-:{grandchild.Id}", aliveLog);
        Assert.DoesNotContain(grandchild, child1.Get<Node<SceneTag>>().Children);

        root.Destroy();
        Assert.Contains($"alive-:{root.Id}", aliveLog);
        Assert.Contains($"alive-:{child1.Id}", aliveLog);
        Assert.Contains($"alive-:{child2.Id}", aliveLog);
        Assert.False(child1.IsValid);
        Assert.False(child2.IsValid);
        Assert.Empty(hierarchy.Root);
        Assert.False(mapper.ContainsKey("root"));
        Assert.False(mapper.ContainsKey("child1"));
        Assert.False(aggregator.TryGet(new CategoryId("prop"), out _));
    }
}
