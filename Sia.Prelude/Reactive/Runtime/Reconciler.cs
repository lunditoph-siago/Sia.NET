namespace Sia.Reactive;

using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using Sia.Reactors;

public sealed class Reconciler : ReactorBase, IScheduleSource
{
    private readonly record struct DirtyEntry(EntityReference Cell, NodeIdentity Identity);

    private readonly List<DirtyEntry> _dirty = [];
    private readonly List<Action> _effectCleanups = [];
    private readonly List<Action> _effectSetups = [];
    private readonly Dictionary<long, EntityReference> _roots = [];
    private readonly SparseSet<IEntityHost> _graphHosts = [];
    private Entity _expandingCell;
    private int _dirtyHead;
    private int _reconcileDepth;
    private bool _flushing;
    private long _nextIdentity;

    private readonly Dictionary<Type, List<ScheduleRegistry>> _schedules = [];
    private readonly List<ScheduleRegistry> _rebuildQueue = [];
    private readonly Dictionary<int, Stack<CellSlot[]>> _cellSlotPools = [];
    private readonly Dictionary<Type, Stack<IRecyclableForEachCleanup>> _eachIndexPools = [];
    private Scheduler? _scheduler;
    private ScheduleRegistration? _sourceRegistration;

    public override void OnInitialize(World world)
    {
        base.OnInitialize(world);
        try {
            _scheduler = world.AcquireAddon<Scheduler>();
            _sourceRegistration = _scheduler.RegisterSource(this);
        }
        catch (Exception error) {
            Outcome<Exception>.Failure(error)
                .Attempt(() => base.OnUninitialize(world))
                .ThrowFailure();
        }
    }

    public override void OnUninitialize(World world)
    {
        var result = Outcome<Exception>.Success;
        result = result.Attempt(() => _sourceRegistration?.Dispose());
        _sourceRegistration = null;
        foreach (var (identity, reference) in _roots.ToArray()) {
            if (reference.TryGet(out var cell)) {
                result = result.Attempt(() => DestroyCell(cell, new(identity)));
            }
        }
        _roots.Clear();
        result = result.Attempt(DrainEffects);

        foreach (var registries in _schedules.Values) {
            foreach (var registry in registries) {
                result = result.Attempt(() => DisposeRegistry(registry));
            }
        }
        _schedules.Clear();
        _rebuildQueue.Clear();
        result = DisposeGraphHosts(result);
        _cellSlotPools.Clear();
        _eachIndexPools.Clear();
        _scheduler = null;
        _expandingCell = default;
        result = result.Attempt(() => base.OnUninitialize(world));
        result.ThrowIfFailed();
    }

    void IScheduleSource.OnBeginTick() => Flush();
    void IScheduleSource.OnBeforeSchedule(ScheduleLabel label) => Flush();

    public MountHandle<TSpec> Mount<TSpec>(in TSpec props)
        where TSpec : struct, ISpec
    {
        var cell = MountSub(
            props,
            parent: default,
            depth: 0,
            slotInParent: -1,
            schedule: null,
            scope: null,
            output: default);
        var identity = cell.GetUnchecked<Cell>().Identity;
        _roots.Add(identity.Value, new(cell));
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
        if (_reconcileDepth != 0) {
            throw new InvalidOperationException(
                "Reactive mounts cannot be unmounted while a spec is expanding "
                + "or its tree is reconciling.");
        }
        if (!IsCell(cell, identity)) {
            throw new ObjectDisposedException(nameof(MountHandle<int>));
        }
        _roots.Remove(identity.Value);
        var result = Outcome<Exception>.Success
            .Attempt(() => DestroyCell(cell, identity))
            .Attempt(DrainEffects);
        result.ThrowIfFailed();
    }

    internal void UpdateMount<TProps>(Entity cell, in TProps props)
        where TProps : struct
    {
        cell.GetUnchecked<TProps>() = props;
        EnqueueDirty(cell);
    }

    internal void InvalidateMount(Entity cell)
        => EnqueueDirty(cell);

    internal void QueueEffectCleanup(Action cleanup, bool prepend = false)
    {
        ArgumentNullException.ThrowIfNull(cleanup);
        if (prepend) {
            _effectCleanups.Insert(0, cleanup);
        }
        else {
            _effectCleanups.Add(cleanup);
        }
    }

    internal void QueueEffectSetup(Action setup)
    {
        ArgumentNullException.ThrowIfNull(setup);
        _effectSetups.Add(setup);
    }

    private void ExpandCell(Entity cell)
    {
        _reconcileDepth++;
        try {
            cell.GetUnchecked<Cell>().Expander.Expand(this, cell);
        }
        finally {
            _reconcileDepth--;
        }
    }

    internal void BeginExpansion(Entity cell)
    {
        if (_expandingCell.IsValid) {
            throw new InvalidOperationException("Reactive expansions cannot be nested.");
        }
        _expandingCell = cell;
        ref var data = ref cell.GetUnchecked<Cell>();
        data.States?.BeginExpansion();
        (data.PendingContextDependencies ??= []).Clear();
    }

    internal void CompleteExpansion(Entity cell)
    {
        try {
            ref var data = ref cell.GetUnchecked<Cell>();
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
                    var slot = new CellSlot(cell);
                    scope.Consumers[identity] = slot;
                }
            }
            (data.ContextDependencies, data.PendingContextDependencies) =
                (current, previous);
        } finally {
            _expandingCell = default;
        }
    }

    internal void AbortExpansion()
        => _expandingCell = default;

    internal void GuardStateMutation(Entity owner)
    {
        if (_expandingCell == owner) {
            throw new InvalidOperationException(
                "State cannot be mutated while its spec is expanding.");
        }
    }

    internal void RecordContextDependency(Entity cell, ContextScope scope)
    {
        var pending = cell.GetUnchecked<Cell>().PendingContextDependencies ??= [];
        if (!pending.Contains(scope)) {
            pending.Add(scope);
        }
    }

    internal StateCells EnsureStateCells(Entity cell)
    {
        ref var data = ref cell.GetUnchecked<Cell>();
        return data.States ??= new StateCells(data.HookLayoutInitialized);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal bool IsCell(Entity cell, NodeIdentity identity)
    {
        if (!cell.IsValid || !cell.ContainsUnchecked<Cell>()) {
            return false;
        }
        ref var data = ref cell.GetUnchecked<Cell>();
        return data.Identity == identity && !data.IsDestroying;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal bool IsCell(EntityReference cell, NodeIdentity identity)
    {
        if (!cell.IsValid || !cell.ContainsUnchecked<Cell>()) {
            return false;
        }
        ref var data = ref cell.GetUnchecked<Cell>();
        return data.Identity == identity && !data.IsDestroying;
    }

    internal NodeIdentity NextIdentity()
        => new(++_nextIdentity);

    internal CellSlot[] RentCellSlots(int length)
    {
        if (length == 0) {
            return [];
        }
        return _cellSlotPools.TryGetValue(length, out var pool)
            && pool.TryPop(out var slots)
                ? slots
                : new CellSlot[length];
    }

    private void ReturnCellSlots(CellSlot[] slots)
    {
        if (slots.Length == 0) {
            return;
        }
        if (!_cellSlotPools.TryGetValue(slots.Length, out var pool)) {
            pool = [];
            _cellSlotPools.Add(slots.Length, pool);
        }
        pool.Push(slots);
    }

    internal EachIndex<TKey> RentEachIndex<TKey>()
        where TKey : notnull
    {
        var type = typeof(EachIndex<TKey>);
        return _eachIndexPools.TryGetValue(type, out var pool)
            && pool.TryPop(out var index)
                ? (EachIndex<TKey>)index
                : new EachIndex<TKey>();
    }

    private void ReturnEachIndex(IRecyclableForEachCleanup index)
    {
        index.Reset();
        var type = index.GetType();
        if (!_eachIndexPools.TryGetValue(type, out var pool)) {
            pool = [];
            _eachIndexPools.Add(type, pool);
        }
        pool.Push(index);
    }

    internal Entity Validate(in CellSlot slot)
        => slot.Entity;

    internal Entity CreateOutput<TList>(in TList components)
        where TList : struct, IHList
        => World.Create(components);

    internal Entity CreateNode<T>(in T component)
        => CreateGraphEntity(HList.From(component));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private Entity CreateGraphEntity<TEntity>(in TEntity initial)
        where TEntity : struct, IHList
    {
        if (_scheduler == null) {
            throw new InvalidOperationException("The reconciler is not initialized.");
        }

        ref var rawHost = ref _graphHosts.GetOrAddValueRef(
            TypeIndexer<TEntity>.Index,
            out var exists);
        if (!exists) {
            rawHost = new ArrayEntityHost<TEntity>();
        }
        return ((ArrayEntityHost<TEntity>)rawHost).Create(initial);
    }

    private Outcome<Exception> DisposeGraphHosts(Outcome<Exception> result)
    {
        var hosts = _graphHosts.UnsafeRawValues;
        for (var i = 0; i < hosts.Count; i++) {
            result = result.Attempt(hosts[i].Dispose);
        }
        _graphHosts.Clear();
        return result;
    }

    internal Entity MountSub<TSpec>(
        in TSpec props, Entity parent, int depth, int slotInParent,
        ScheduleRegistry? schedule, ContextScope? scope, Entity output)
        where TSpec : struct, ISpec
    {
        var factory = new CellFactory<TSpec>(
            this,
            props,
            parent,
            depth,
            slotInParent,
            schedule,
            scope,
            output);
        TSpec.HandleSignature(ref factory);
        return factory.Result.IsValid
            ? factory.Result
            : throw new InvalidOperationException("Reactive cell factory produced no entity.");
    }

    public void EnqueueDirty(Entity cell)
    {
        if (!cell.IsValid || !cell.ContainsUnchecked<Cell>()) {
            return;
        }
        ref var cellData = ref cell.GetUnchecked<Cell>();
        if (cellData.InDirty) {
            return;
        }
        cellData.InDirty = true;
        InsertByDepth(new(new(cell), cellData.Identity), cellData.Depth);
    }

    public void Flush()
    {
        if (_flushing) {
            return;
        }
        _flushing = true;
        try {
            try {
                while (true) {
                    if (_dirtyHead < _dirty.Count) {
                        var entry = _dirty[_dirtyHead];
                        _dirty[_dirtyHead] = default;
                        _dirtyHead++;

                        if (!entry.Cell.TryGet(out var cell)
                            || !IsCell(cell, entry.Identity)) {
                            continue;
                        }
                        ref var cellData = ref cell.GetUnchecked<Cell>();
                        if (!cellData.InDirty) {
                            continue;
                        }
                        cellData.InDirty = false;
                        ExpandCell(cell);
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
            }
            catch (Exception error) {
                var result = DrainEffectCleanups(
                    Outcome<Exception>.Failure(error));
                result.ThrowFailure();
            }
            DrainEffects();
        }
        finally {
            _flushing = false;
        }
    }

    private Outcome<Exception> DrainEffectCleanups(
        Outcome<Exception> result)
    {
        var cleanups = _effectCleanups.ToArray();
        _effectCleanups.Clear();
        for (var index = cleanups.Length - 1; index >= 0; index--) {
            result = result.Attempt(cleanups[index]);
        }
        return result;
    }

    private void DrainEffects()
    {
        var result = DrainEffectCleanups(Outcome<Exception>.Success);
        var setups = _effectSetups.ToArray();
        _effectSetups.Clear();
        for (var index = 0; index < setups.Length; index++) {
            result = result.Attempt(setups[index]);
        }
        result.ThrowIfFailed();
    }

    public IReadOnlyList<ScheduleRegistry> GetSchedules<TLabel>()
        where TLabel : struct
        => _schedules.TryGetValue(typeof(TLabel), out var registries) ? registries : [];

    internal (ScheduleRegistry Registry, Entity Node) CreateSchedule(
        Type labelType,
        ScheduleRegistry? parent)
    {
        var label = ScheduleLabel.ForType(labelType);
        if (parent is { } inherited && inherited.Label == label) {
            inherited.ScopeCount++;
            try {
                return (inherited, CreateNode(new ScheduleNode(inherited)));
            }
            catch {
                inherited.ScopeCount--;
                throw;
            }
        }
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
        registry.ScopeCount = 1;
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
        if (slot.ContainsUnchecked<Cell>()) {
            DestroyCell(slot, slot.GetUnchecked<Cell>().Identity);
            return;
        }
        var result = Outcome<Exception>.Success;
        IRecyclableForEachCleanup? recyclable = null;
        if (slot.ContainsUnchecked<SystemNode>()) {
            SystemStage? runtime = null;
            result = result.Attempt(() => {
                var registry = slot.GetUnchecked<SystemNode>().Registry;
                runtime = registry.Remove(slot);
                QueueRebuild(registry);
            });
            if (runtime != null) {
                result = result.Attempt(runtime.Dispose);
            }
        } else if (slot.ContainsUnchecked<ScheduleNode>()) {
            var registry = slot.GetUnchecked<ScheduleNode>().Registry;
            registry.ScopeCount--;
            if (registry.ScopeCount == 0) {
                result = result.Attempt(() => RemoveSchedule(registry));
            }
        } else if (slot.ContainsUnchecked<EachNode>()) {
            var cleanup = slot.GetUnchecked<EachNode>().Cleanup;
            recyclable = cleanup as IRecyclableForEachCleanup;
            var operation = (Owner: this, Cleanup: cleanup);
            result = result.Attempt(
                operation,
                static (in (Reconciler Owner, IForEachCleanup Cleanup) value)
                    => value.Cleanup.DestroyChildren(value.Owner));
        } else if (slot.ContainsUnchecked<EffectNode>()) {
            result = result.Attempt(slot.GetUnchecked<EffectNode>().Cleanup.Unmount);
        }
        result = result.Attempt(
            slot,
            static (in Entity entity) => entity.Destroy());
        if (result.IsSuccess && recyclable != null) {
            ReturnEachIndex(recyclable);
        }
        result.ThrowIfFailed();
    }

    internal void DestroySlot(in CellSlot slot)
    {
        if (Validate(slot) is { IsValid: true } entity) {
            DestroySlot(entity);
        }
    }

    private void DestroyCell(Entity cell, NodeIdentity identity)
    {
        if (!IsCell(cell, identity)) {
            return;
        }
        ref var data = ref cell.GetUnchecked<Cell>();
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
            var operation = (Owner: this, Slot: slot);
            result = result.Attempt(
                operation,
                static (in (Reconciler Owner, CellSlot Slot) value)
                    => value.Owner.DestroySlot(value.Slot));
        }
        if (data.States is { } states) {
            result = result.Attempt(states.Unmount);
        }
        result = result.Attempt(
            cell,
            static (in Entity entity) => entity.Destroy());
        if (result.IsSuccess) {
            ReturnCellSlots(slots);
        }
        result.ThrowIfFailed();
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
        registry.ScopeCount = 0;
        DisposeRegistry(registry);
    }

    private void RebuildStage(ScheduleRegistry registry)
    {
        registry.Slots.Sort(static (a, b) => CompareTreeOrder(
            a.OwnerCell, a.SlotIndex, b.OwnerCell, b.SlotIndex));

        var entries = registry.Slots.Select(slot => slot.Entry).ToArray();
        var order = Planner.PlanOrder(entries);
        var planEntries = ImmutableArray.CreateBuilder<SystemChain.Entry>(
            order.Length);
        var runtimes = ImmutableArray.CreateBuilder<SystemStage>(order.Length);
        foreach (var index in order) {
            planEntries.Add(entries[index]);
            runtimes.Add(registry.Slots[index].Runtime);
        }
        registry.CurrentPlan = new ExecutionPlan(planEntries.MoveToImmutable());
        registry.RuntimeOrder = runtimes.MoveToImmutable();
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
        if (cellA == cellB) {
            return slotA.CompareTo(slotB);
        }

        var depthA = cellA.GetUnchecked<Cell>().Depth;
        var depthB = cellB.GetUnchecked<Cell>().Depth;
        while (depthA > depthB) {
            (cellA, slotA) = StepUp(cellA);
            depthA--;
        }
        while (depthB > depthA) {
            (cellB, slotB) = StepUp(cellB);
            depthB--;
        }
        while (cellA != cellB) {
            ref var cellDataA = ref cellA.GetUnchecked<Cell>();
            ref var cellDataB = ref cellB.GetUnchecked<Cell>();
            var parentA = cellDataA.ParentEntity;
            var parentB = cellDataB.ParentEntity;
            if (!parentA.IsValid || !parentB.IsValid) {
                return cellDataA.Identity.Value.CompareTo(cellDataB.Identity.Value);
            }
            slotA = cellDataA.SlotInParent;
            slotB = cellDataB.SlotInParent;
            cellA = parentA;
            cellB = parentB;
        }
        var order = slotA.CompareTo(slotB);
        return order != 0
            ? order
            : originalA.GetUnchecked<Cell>().Identity.Value.CompareTo(
                originalB.GetUnchecked<Cell>().Identity.Value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static (Entity, int) StepUp(Entity cell)
    {
        ref var cellData = ref cell.GetUnchecked<Cell>();
        var parent = cellData.ParentEntity;
        return parent.IsValid
            ? (parent, cellData.SlotInParent)
            : throw new InvalidOperationException("Reactive cell has no parent.");
    }

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
            ? entry.Cell.GetUnchecked<Cell>().Depth
            : int.MinValue;

    private struct CellFactory<TProps>(
        Reconciler reconciler, TProps props, Entity parent, int depth, int slotInParent,
        ScheduleRegistry? schedule, ContextScope? scope, Entity output)
        : ISpecSignatureHandler
        where TProps : struct, ISpec
    {
        public Entity Result;

        private readonly Reconciler _reconciler = reconciler;
        private TProps _props = props;
        private readonly EntityReference _parent = parent.IsValid ? new(parent) : default;
        private readonly int _depth = depth;
        private readonly int _slotInParent = slotInParent;
        private readonly ScheduleRegistry? _schedule = schedule;
        private readonly ContextScope? _scope = scope;
        private readonly Entity _output = output;

        public void Handle<TSpec, TState, TTree>()
            where TSpec : struct, ISpec<TSpec, TState, TTree>
            where TState : struct
            where TTree : struct, ITerm<TTree>
        {
            ref var typedProps = ref Unsafe.As<TProps, TSpec>(ref _props);
            var cell = _reconciler.CreateGraphEntity(HList.From(
                typedProps,
                TSpec.InitialState(typedProps),
                new PrevTree<TTree>(),
                new Cell {
                    Identity = _reconciler.NextIdentity(),
                    Parent = _parent.GetOrDefault(),
                    Depth = _depth,
                    SlotInParent = _slotInParent,
                    Slots = _reconciler.RentCellSlots(TTree.SlotCount),
                    Expander = Expander<TSpec, TState, TTree>.Instance,
                    Schedule = _schedule,
                    Scope = _scope,
                    Output = _output,
                }));
            Result = cell;
            try {
                _reconciler.ExpandCell(cell);
            }
            catch (Exception error) {
                var owner = _reconciler;
                var identity = cell.GetUnchecked<Cell>().Identity;
                Outcome<Exception>.Failure(error)
                    .Attempt(() => owner.DestroyCell(cell, identity))
                    .ThrowFailure();
            }
        }
    }
}
