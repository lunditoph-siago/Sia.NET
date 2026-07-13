namespace Sia.Reactive;

using System.Runtime.CompilerServices;
using Sia.Reactors;

public sealed class Reconciler : ReactorBase<TypeUnion<Cell>>
{
    private readonly List<Entity> _dirty = [];
    private int _dirtyHead;
    private bool _flushing;

    private readonly Dictionary<Type, List<ScheduleRegistry>> _schedules = [];
    private readonly List<ScheduleRegistry> _rebuildQueue = [];

    public override void OnInitialize(World world)
    {
        base.OnInitialize(world);
        Listen((Entity target, in CellEvents.Invalidate e) => EnqueueDirty(target));
    }

    public override void OnUninitialize(World world)
    {
        foreach (var registries in _schedules.Values) {
            foreach (var registry in registries) {
                DisposeStage(registry);
            }
        }
        _schedules.Clear();
        base.OnUninitialize(world);
    }

    public Entity Mount<TSpec>(in TSpec props)
        where TSpec : struct, ISpec
    {
        var cell = MountSub(
            props, parent: null, depth: 0, slotInParent: -1, schedule: null, scope: null);
        Flush();
        return cell;
    }

    public Entity Mount<TProps>(Spec<TProps> spec, in TProps props)
        where TProps : struct, IEquatable<TProps>
    {
        var cell = spec.MountCell(
            this, props, parent: null, depth: 0, slotInParent: -1, schedule: null, scope: null);
        Flush();
        return cell;
    }

    public void Unmount(Entity cell) => cell.Destroy();

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
        InsertByDepth(cell, cellData.Depth);
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
                    var cell = _dirty[_dirtyHead];
                    _dirty[_dirtyHead] = null!;
                    _dirtyHead++;

                    if (!cell.IsValid) {
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
        }
        finally {
            _flushing = false;
        }
    }

    public void Tick<TLabel>()
        where TLabel : struct
    {
        Flush();
        if (!_schedules.TryGetValue(typeof(TLabel), out var registries)) {
            return;
        }
        foreach (var registry in registries) {
            registry.Stage?.Tick();
        }
    }

    public IReadOnlyList<ScheduleRegistry> GetSchedules<TLabel>()
        where TLabel : struct
        => _schedules.TryGetValue(typeof(TLabel), out var registries) ? registries : [];

    internal ScheduleRegistry CreateSchedule(Type labelType)
    {
        var registry = new ScheduleRegistry(labelType);
        if (!_schedules.TryGetValue(labelType, out var registries)) {
            registries = [];
            _schedules.Add(labelType, registries);
        }
        registries.Add(registry);
        return registry;
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
        var slotEntity = World.Create(HList.From(new SystemNode { Registry = registry }));
        registry.Slots.Add(new ScheduleRegistry.Slot {
            SlotEntity = slotEntity,
            OwnerCell = ownerCell,
            SlotIndex = slotIndex,
            Entry = new(
                SystemId.For<TSystem>(),
                () => instance,
                SystemDescriptorProvider.GetOrDefault(typeof(TSystem))),
        });
        QueueRebuild(registry);
        return slotEntity;
    }

    internal void DestroySlot(Entity slot)
    {
        if (slot.Contains<SystemNode>()) {
            var registry = slot.Get<SystemNode>().Registry;
            registry.Remove(slot);
            QueueRebuild(registry);
        }
        else if (slot.Contains<ScheduleNode>()) {
            RemoveSchedule(slot.Get<ScheduleNode>().Registry);
        }
        else if (slot.Contains<EachNode>()) {
            slot.Get<EachNode>().Cleanup.DestroyChildren(this);
        }
        slot.Destroy();
    }

    protected override void OnEntityAdded(Entity entity) {}

    protected override void OnEntityRemoved(Entity entity)
    {
        var slots = entity.Get<Cell>().Slots;
        for (var i = 0; i < slots.Length; i++) {
            var slot = slots[i];
            if (slot is { IsValid: true }) {
                DestroySlot(slot);
            }
            slots[i] = null;
        }
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
        DisposeStage(registry);
        if (_schedules.TryGetValue(registry.LabelType, out var registries)) {
            registries.Remove(registry);
        }
        _rebuildQueue.Remove(registry);
        registry.RebuildQueued = false;
    }

    private void RebuildStage(ScheduleRegistry registry)
    {
        // Tree order is the default execution order; descriptor edges
        // (SiaBefore/SiaAfter) are applied on top by the SystemGraph sort.
        registry.Slots.Sort(static (a, b) => CompareTreeOrder(
            a.OwnerCell, a.SlotIndex, b.OwnerCell, b.SlotIndex));

        var chain = SystemChain.Empty;
        foreach (var slot in registry.Slots) {
            var entry = slot.Entry;
            chain = chain.Add(entry.Id, entry.Creator, entry.Descriptor);
        }

        DisposeStage(registry);
        registry.Stage = chain.CreateStage(World);
        registry.Version++;
    }

    private void DisposeStage(ScheduleRegistry registry)
    {
        var stage = registry.Stage;
        if (stage == null) {
            return;
        }
        registry.Stage = null;
        if (!World.IsDisposed) {
            stage.Dispose();
            return;
        }
        // The world is tearing down; dispose best-effort without letting the
        // finalizer rethrow against already-released hosts.
        try {
            stage.Dispose();
        }
        catch {
            // world already torn down
        }
        finally {
            GC.SuppressFinalize(stage);
        }
    }

    private static int CompareTreeOrder(Entity cellA, int slotA, Entity cellB, int slotB)
    {
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
                return cellA.Id.Value.CompareTo(cellB.Id.Value);
            }
            slotA = cellDataA.SlotInParent;
            slotB = cellDataB.SlotInParent;
            cellA = cellDataA.Parent;
            cellB = cellDataB.Parent;
        }
        return slotA.CompareTo(slotB);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static (Entity, int) StepUp(Entity cell)
    {
        ref var cellData = ref cell.Get<Cell>();
        return (cellData.Parent!, cellData.SlotInParent);
    }

    // Pending cells are kept sorted by depth so parents expand before children.
    private void InsertByDepth(Entity cell, int depth)
    {
        var dirty = _dirty;
        var index = dirty.Count;
        while (index > _dirtyHead && GetDepth(dirty[index - 1]) > depth) {
            index--;
        }
        dirty.Insert(index, cell);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int GetDepth(Entity cell)
        => cell.IsValid ? cell.Get<Cell>().Depth : int.MinValue;

    private struct CellFactory<TProps>(
        Reconciler reconciler, TProps props, Entity? parent, int depth, int slotInParent,
        ScheduleRegistry? schedule, ContextScope? scope)
        : ISpecSignatureHandler
        where TProps : struct, ISpec
    {
        public Entity? Result;

        private TProps _props = props;

        public void Handle<TSpec, TState, TTree>()
            where TSpec : struct, ISpec<TSpec, TState, TTree>
            where TState : struct
            where TTree : struct, ITerm<TTree>
        {
            // HandleSignature dispatches to the spec's own signature, so
            // TSpec is TProps; reinterpret instead of boxing.
            ref var typedProps = ref Unsafe.As<TProps, TSpec>(ref _props);
            var cell = reconciler.World.Create(HList.From(
                typedProps,
                TSpec.InitialState(typedProps),
                new PrevTree<TTree>(),
                new Cell {
                    Parent = parent,
                    Depth = depth,
                    SlotInParent = slotInParent,
                    Slots = TTree.SlotCount > 0 ? new Entity?[TTree.SlotCount] : [],
                    Expander = Expander<TSpec, TState, TTree>.Instance,
                    Schedule = schedule,
                    Scope = scope,
                }));
            Result = cell;
            Expander<TSpec, TState, TTree>.Instance.Expand(reconciler, cell);
        }
    }
}
