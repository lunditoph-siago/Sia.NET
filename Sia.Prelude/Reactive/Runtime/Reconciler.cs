namespace Sia.Reactive;

using System.Runtime.CompilerServices;
using Sia.Reactors;

public sealed class Reconciler : ReactorBase, IScheduleSource
{
    private readonly record struct DirtyEntry(Entity Cell, NodeIdentity Identity);

    private readonly List<DirtyEntry> _dirty = [];
    private readonly Dictionary<long, Entity> _roots = [];
    private World? _graphWorld;
    private Entity? _expandingCell;
    private int _dirtyHead;
    private bool _flushing;

    private readonly Dictionary<Type, List<ScheduleRegistry>> _schedules = [];
    private readonly List<ScheduleRegistry> _rebuildQueue = [];
    private Scheduler? _scheduler;
    private ScheduleRegistration? _sourceRegistration;

    internal World GraphWorld
        => _graphWorld
            ?? throw new InvalidOperationException("The reconciler is not initialized.");

    public override void OnInitialize(World world)
    {
        base.OnInitialize(world);
        try {
            _graphWorld = new World();
            _scheduler = world.AcquireAddon<Scheduler>();
            _sourceRegistration = _scheduler.RegisterSource(this);
        }
        catch (Exception error) {
            Outcome<Exception>.Failure(error)
                .Attempt(() => _graphWorld?.Dispose())
                .Attempt(() => base.OnUninitialize(world))
                .ThrowFailure();
        }
    }

    public override void OnUninitialize(World world)
    {
        var result = Outcome<Exception>.Success;
        result = result.Attempt(() => _sourceRegistration?.Dispose());
        _sourceRegistration = null;
        foreach (var (identity, cell) in _roots.ToArray()) {
            result = result.Attempt(() => DestroyCell(cell, new(identity)));
        }
        _roots.Clear();

        foreach (var registries in _schedules.Values) {
            foreach (var registry in registries) {
                result = result.Attempt(() => DisposeRegistry(registry));
            }
        }
        _schedules.Clear();
        _rebuildQueue.Clear();
        result = result.Attempt(() => _graphWorld?.Dispose());
        _graphWorld = null;
        _scheduler = null;
        _expandingCell = null;
        result = result.Attempt(() => base.OnUninitialize(world));
        result.ThrowIfFailed();
    }

    void IScheduleSource.OnBeginTick() => Flush();
    void IScheduleSource.OnBeforeSchedule(ScheduleLabel label) => Flush();

    public MountHandle<TSpec> Mount<TSpec>(in TSpec props)
        where TSpec : struct, ISpec
    {
        var cell = MountSub(
            props, parent: null, depth: 0, slotInParent: -1, schedule: null, scope: null);
        var identity = cell.Get<Cell>().Identity;
        _roots.Add(identity.Value, cell);
        try {
            Flush();
            return new(this, cell, identity);
        }
        catch (Exception error) {
            _roots.Remove(identity.Value);
            return Outcome<Exception>.Failure(error)
                .Attempt(() => DestroyCell(cell, identity))
                .ThrowFailure<MountHandle<TSpec>>();
        }
    }

    internal void Unmount(Entity cell, NodeIdentity identity)
    {
        if (!IsCell(cell, identity)) {
            throw new ObjectDisposedException(nameof(MountHandle<int>));
        }
        _roots.Remove(identity.Value);
        DestroyCell(cell, identity);
    }

    internal void UpdateMount<TProps>(Entity cell, in TProps props)
        where TProps : struct
    {
        cell.Get<TProps>() = props;
        EnqueueDirty(cell);
    }

    internal void InvalidateMount(Entity cell)
        => EnqueueDirty(cell);

    internal void BeginExpansion(Entity cell)
    {
        if (_expandingCell != null) {
            throw new InvalidOperationException("Reactive expansions cannot be nested.");
        }
        _expandingCell = cell;
        ref var data = ref cell.Get<Cell>();
        data.States?.BeginExpansion();
        (data.PendingContextDependencies ??= []).Clear();
    }

    internal void CompleteExpansion(Entity cell)
    {
        try {
            ref var data = ref cell.Get<Cell>();
            data.States?.CompleteExpansion();
            var previous = data.ContextDependencies ??= [];
            var current = data.PendingContextDependencies ??= [];
            var identity = data.Identity.Value;
            foreach (var scope in previous) {
                if (!current.Contains(scope)) {
                    scope.Consumers.Remove(identity);
                }
            }
            foreach (var scope in current) {
                if (!previous.Contains(scope)) {
                    var slot = new CellSlot();
                    slot.Set(this, cell);
                    scope.Consumers[identity] = slot;
                }
            }
            (data.ContextDependencies, data.PendingContextDependencies) =
                (current, previous);
        } finally {
            _expandingCell = null;
        }
    }

    internal void AbortExpansion()
        => _expandingCell = null;

    internal void GuardStateMutation(Entity owner)
    {
        if (ReferenceEquals(_expandingCell, owner)) {
            throw new InvalidOperationException(
                "State cannot be mutated while its spec is expanding.");
        }
    }

    internal void RecordContextDependency(Entity cell, ContextScope scope)
    {
        var pending = cell.Get<Cell>().PendingContextDependencies ??= [];
        if (!pending.Contains(scope)) {
            pending.Add(scope);
        }
    }

    internal bool IsCell(Entity cell, NodeIdentity identity)
        => cell.IsValid
            && cell.Contains<Cell>()
            && cell.Get<Cell>().Identity == identity
            && !cell.Get<Cell>().IsDestroying;

    internal NodeIdentity NextIdentity()
        => NodeIdentity.Create();

    internal NodeIdentity GetIdentity(Entity entity)
        => entity.Contains<Cell>()
            ? entity.Get<Cell>().Identity
            : entity.Get<ReactiveNode>().Identity;

    internal Entity? Validate(in CellSlot slot)
    {
        if (slot.Entity is not { IsValid: true } entity) {
            return null;
        }
        if (entity.Contains<Cell>()) {
            return entity.Get<Cell>().Identity == slot.Identity ? entity : null;
        }
        return entity.Contains<ReactiveNode>()
            && entity.Get<ReactiveNode>().Identity == slot.Identity
                ? entity
                : null;
    }

    internal Entity CreateOutput<TList>(in TList components)
        where TList : struct, IHList
    {
        var entity = World.Create(components);
        try {
            entity.Add(new ReactiveNode(NextIdentity()));
            return entity;
        }
        catch (Exception error) {
            return Outcome<Exception>.Failure(error)
                .Attempt(entity.Destroy)
                .ThrowFailure<Entity>();
        }
    }

    internal Entity CreateNode<T>(in T component)
        => GraphWorld.Create(HList.From(
            component,
            new ReactiveNode(NextIdentity())));

    internal Entity MountSub<TSpec>(
        in TSpec props, Entity? parent, int depth, int slotInParent,
        ScheduleRegistry? schedule, ContextScope? scope)
        where TSpec : struct, ISpec
    {
        var factory = new CellFactory<TSpec>(
            this, props, parent, depth, slotInParent, schedule, scope);
        TSpec.HandleSignature(ref factory);
        return factory.Result!;
    }

    public void EnqueueDirty(Entity cell)
    {
        if (!cell.IsValid || !cell.Contains<Cell>()) {
            return;
        }
        ref var cellData = ref cell.Get<Cell>();
        if (cellData.InDirty) {
            return;
        }
        cellData.InDirty = true;
        InsertByDepth(new(cell, cellData.Identity), cellData.Depth);
    }

    public void Flush()
    {
        if (_flushing) {
            return;
        }
        _flushing = true;
        try {
            while (true) {
                if (_dirtyHead < _dirty.Count) {
                    var entry = _dirty[_dirtyHead];
                    _dirty[_dirtyHead] = default;
                    _dirtyHead++;

                    var cell = entry.Cell;
                    if (!IsCell(cell, entry.Identity)) {
                        continue;
                    }
                    ref var cellData = ref cell.Get<Cell>();
                    if (!cellData.InDirty) {
                        continue;
                    }
                    cellData.InDirty = false;
                    cellData.Expander.Expand(this, cell);
                    continue;
                }
                if (_rebuildQueue.Count > 0) {
                    var registry = _rebuildQueue[^1];
                    _rebuildQueue.RemoveAt(_rebuildQueue.Count - 1);
                    registry.RebuildQueued = false;
                    RebuildStage(registry);
                    continue;
                }
                break;
            }
            _dirty.Clear();
            _dirtyHead = 0;
        } finally {
            _flushing = false;
        }
    }

    public IReadOnlyList<ScheduleRegistry> GetSchedules<TLabel>()
        where TLabel : struct
        => _schedules.TryGetValue(typeof(TLabel), out var registries) ? registries : [];

    internal (ScheduleRegistry Registry, Entity Node) CreateSchedule(Type labelType)
    {
        var label = new ScheduleLabel(
            labelType.AssemblyQualifiedName
                ?? labelType.FullName
                ?? labelType.Name);
        var registry = new ScheduleRegistry(label);
        if (!_schedules.TryGetValue(labelType, out var registries)) {
            registries = [];
            _schedules.Add(labelType, registries);
        }
        registries.Add(registry);
        try {
            registry.Registration = (_scheduler
                ?? throw new InvalidOperationException("The reconciler is not initialized."))
                .RegisterEntry(label, registry);
        }
        catch {
            registries.Remove(registry);
            if (registries.Count == 0) {
                _schedules.Remove(labelType);
            }
            throw;
        }
        try {
            return (registry, CreateNode(new ScheduleNode(registry)));
        }
        catch (Exception error) {
            return Outcome<Exception>.Failure(error)
                .Attempt(() => RemoveSchedule(registry))
                .ThrowFailure<(ScheduleRegistry, Entity)>();
        }
    }

    internal Entity RegisterSystem<TSystem>(
        ScheduleRegistry? registry, Entity ownerCell, int slotIndex)
        where TSystem : ISystem, new()
    {
        if (registry == null) {
            throw new InvalidOperationException(
                "Term.System must be declared inside a Term.Schedule subtree.");
        }
        var instance = new TSystem();
        var entry = new SystemChain.Entry(
            SystemId.For<TSystem>(),
            () => instance,
            SystemDescriptorProvider.GetOrDefault(typeof(TSystem)));
        var runtime = SystemChain.Empty
            .Add(entry.Id, entry.Creator, entry.Descriptor)
            .CreateStage(World);
        Entity slotEntity;
        try {
            slotEntity = CreateNode(new SystemNode(registry));
        }
        catch (Exception error) {
            return Outcome<Exception>.Failure(error)
                .Attempt(runtime.Dispose)
                .ThrowFailure<Entity>();
        }
        registry.Slots.Add(new(slotEntity, ownerCell, slotIndex, entry, runtime));
        QueueRebuild(registry);
        return slotEntity;
    }

    internal void DestroySlot(Entity slot)
    {
        if (slot.Contains<Cell>()) {
            DestroyCell(slot, slot.Get<Cell>().Identity);
            return;
        }
        var result = Outcome<Exception>.Success;
        if (slot.Contains<SystemNode>()) {
            SystemStage? runtime = null;
            result = result.Attempt(() => {
                var registry = slot.Get<SystemNode>().Registry;
                runtime = registry.Remove(slot);
                QueueRebuild(registry);
            });
            if (runtime != null) {
                result = result.Attempt(runtime.Dispose);
            }
        } else if (slot.Contains<ScheduleNode>()) {
            result = result.Attempt(
                () => RemoveSchedule(slot.Get<ScheduleNode>().Registry));
        } else if (slot.Contains<EachNode>()) {
            result = result.Attempt(
                () => slot.Get<EachNode>().Cleanup.DestroyChildren(this));
        }
        result.Attempt(slot.Destroy).ThrowIfFailed();
    }

    internal void DestroySlot(in CellSlot slot)
    {
        if (Validate(slot) is { } entity) {
            DestroySlot(entity);
        }
    }

    private void DestroyCell(Entity cell, NodeIdentity identity)
    {
        if (!IsCell(cell, identity)) {
            return;
        }
        ref var data = ref cell.Get<Cell>();
        data.IsDestroying = true;
        data.InDirty = false;
        _roots.Remove(identity.Value);
        if (data.ContextDependencies is { } dependencies) {
            foreach (var scope in dependencies) {
                scope.Consumers.Remove(identity.Value);
            }
            dependencies.Clear();
        }
        var slots = data.Slots;
        var result = Outcome<Exception>.Success;
        for (var i = slots.Length - 1; i >= 0; i--) {
            var slot = slots[i];
            slots[i] = default;
            result = result.Attempt(() => DestroySlot(slot));
        }
        result.Attempt(cell.Destroy).ThrowIfFailed();
    }

    private void QueueRebuild(ScheduleRegistry registry)
    {
        if (registry.RebuildQueued) {
            return;
        }
        registry.RebuildQueued = true;
        _rebuildQueue.Add(registry);
    }

    private void RemoveSchedule(ScheduleRegistry registry)
    {
        foreach (var (labelType, registries) in _schedules.ToArray()) {
            if (!registries.Remove(registry)) {
                continue;
            }
            if (registries.Count == 0) {
                _schedules.Remove(labelType);
            }
            break;
        }
        _rebuildQueue.Remove(registry);
        registry.RebuildQueued = false;
        DisposeRegistry(registry);
    }

    private void RebuildStage(ScheduleRegistry registry)
    {
        // Tree order is the default execution order; descriptor edges
        // (SiaBefore/SiaAfter) are applied on top by the SystemGraph sort.
        registry.Slots.Sort(static (a, b) => CompareTreeOrder(
            a.OwnerCell, a.SlotIndex, b.OwnerCell, b.SlotIndex));

        var plan = Planner.Plan(registry.Slots.Select(slot => slot.Entry).ToArray());
        var runtimes = new SystemStage[plan.Entries.Length];
        for (var i = 0; i < plan.Entries.Length; i++) {
            var entry = plan.Entries[i];
            runtimes[i] = registry.Slots
                .First(slot => ReferenceEquals(slot.Entry.Creator, entry.Creator))
                .Runtime;
        }
        registry.CurrentPlan = plan;
        registry.RuntimeOrder = [.. runtimes];
        registry.Version++;
    }

    private static void DisposeRegistry(ScheduleRegistry registry)
    {
        var result = Outcome<Exception>.Success;
        result = result.Attempt(() => registry.Registration?.Dispose());
        registry.Registration = null;

        var disposed = new HashSet<SystemStage>();
        for (var i = registry.RuntimeOrder.Length - 1; i >= 0; i--) {
            var runtime = registry.RuntimeOrder[i];
            if (disposed.Add(runtime)) {
                result = result.Attempt(runtime.Dispose);
            }
        }
        for (var i = registry.Slots.Count - 1; i >= 0; i--) {
            var runtime = registry.Slots[i].Runtime;
            if (disposed.Add(runtime)) {
                result = result.Attempt(runtime.Dispose);
            }
        }
        registry.Slots.Clear();
        registry.RuntimeOrder = [];
        registry.CurrentPlan = null;
        result.ThrowIfFailed();
    }

    private static int CompareTreeOrder(Entity cellA, int slotA, Entity cellB, int slotB)
    {
        var originalA = cellA;
        var originalB = cellB;
        if (ReferenceEquals(cellA, cellB)) {
            return slotA.CompareTo(slotB);
        }

        var depthA = cellA.Get<Cell>().Depth;
        var depthB = cellB.Get<Cell>().Depth;
        while (depthA > depthB) {
            (cellA, slotA) = StepUp(cellA);
            depthA--;
        }
        while (depthB > depthA) {
            (cellB, slotB) = StepUp(cellB);
            depthB--;
        }
        while (!ReferenceEquals(cellA, cellB)) {
            ref var cellDataA = ref cellA.Get<Cell>();
            ref var cellDataB = ref cellB.Get<Cell>();
            if (cellDataA.Parent == null || cellDataB.Parent == null) {
                // Separate spawn roots: fall back to creation order.
                return cellDataA.Identity.Value.CompareTo(cellDataB.Identity.Value);
            }
            slotA = cellDataA.SlotInParent;
            slotB = cellDataB.SlotInParent;
            cellA = cellDataA.Parent;
            cellB = cellDataB.Parent;
        }
        var order = slotA.CompareTo(slotB);
        return order != 0
            ? order
            : originalA.Get<Cell>().Identity.Value.CompareTo(
                originalB.Get<Cell>().Identity.Value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static (Entity, int) StepUp(Entity cell)
    {
        ref var cellData = ref cell.Get<Cell>();
        return (cellData.Parent!, cellData.SlotInParent);
    }

    // Pending cells are kept sorted by depth so parents expand before children.
    private void InsertByDepth(DirtyEntry entry, int depth)
    {
        var dirty = _dirty;
        var index = dirty.Count;
        while (index > _dirtyHead && GetDepth(dirty[index - 1]) > depth) {
            index--;
        }
        dirty.Insert(index, entry);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int GetDepth(DirtyEntry entry)
        => IsCell(entry.Cell, entry.Identity)
            ? entry.Cell.Get<Cell>().Depth
            : int.MinValue;

    private struct CellFactory<TProps>(
        Reconciler reconciler, TProps props, Entity? parent, int depth, int slotInParent,
        ScheduleRegistry? schedule, ContextScope? scope)
        : ISpecSignatureHandler
        where TProps : struct, ISpec
    {
        public Entity? Result;

        private readonly Reconciler _reconciler = reconciler;
        private TProps _props = props;
        private readonly Entity? _parent = parent;
        private readonly int _depth = depth;
        private readonly int _slotInParent = slotInParent;
        private readonly ScheduleRegistry? _schedule = schedule;
        private readonly ContextScope? _scope = scope;

        public void Handle<TSpec, TState, TTree>()
            where TSpec : struct, ISpec<TSpec, TState, TTree>
            where TState : struct
            where TTree : struct, ITerm<TTree>
        {
            // HandleSignature dispatches to the spec's own signature, so
            // TSpec is TProps; reinterpret instead of boxing.
            ref var typedProps = ref Unsafe.As<TProps, TSpec>(ref _props);
            var cell = _reconciler.GraphWorld.Create(HList.From(
                typedProps,
                TSpec.InitialState(typedProps),
                new PrevTree<TTree>(),
                new Cell {
                    Identity = _reconciler.NextIdentity(),
                    Parent = _parent,
                    Depth = _depth,
                    SlotInParent = _slotInParent,
                    Slots = TTree.SlotCount > 0 ? new CellSlot[TTree.SlotCount] : [],
                    Expander = Expander<TSpec, TState, TTree>.Instance,
                    Schedule = _schedule,
                    Scope = _scope,
                }));
            Result = cell;
            try {
                Expander<TSpec, TState, TTree>.Instance.Expand(_reconciler, cell);
            }
            catch (Exception error) {
                var owner = _reconciler;
                var identity = cell.Get<Cell>().Identity;
                Outcome<Exception>.Failure(error)
                    .Attempt(() => owner.DestroyCell(cell, identity))
                    .ThrowFailure();
            }
        }
    }
}
