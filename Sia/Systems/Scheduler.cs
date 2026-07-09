namespace Sia;

using System.Runtime.ExceptionServices;

public sealed class Scheduler : IAddon, IDisposable
{
    private enum SchedulerPhase
    {
        Idle,
        BeginTick,
        BeforeSchedule,
        Executing
    }

    private sealed class ScheduleSlot(ScheduleLabel label)
    {
        public ScheduleLabel Label { get; } = label;
        public Schedule? Schedule { get; set; }
        public SystemStage? Stage { get; set; }
        public List<EntryRegistration> Entries { get; } = [];
        public bool EntriesNeedCompaction { get; set; }
    }

    private sealed class SourceRegistration(
        Scheduler scheduler,
        IScheduleSource source) : ScheduleRegistration
    {
        public override bool IsAttached => Active;
        public bool Active { get; private set; } = true;
        public IScheduleSource? Source { get; private set; } = source;

        private Scheduler? _scheduler = scheduler;

        public override void Dispose() => _scheduler?.DetachSource(this);

        public bool TryDeactivate(out IScheduleSource source)
        {
            if (!Active) {
                source = null!;
                return false;
            }
            Active = false;
            _scheduler = null;
            source = Source!;
            Source = null;
            return true;
        }
    }

    private sealed class EntryRegistration(
        Scheduler scheduler,
        ScheduleSlot slot,
        IScheduleEntry entry) : ScheduleRegistration
    {
        public override bool IsAttached => Active;
        public bool Active { get; private set; } = true;
        public IScheduleEntry? Entry { get; private set; } = entry;
        public ScheduleSlot? Slot { get; private set; } = slot;

        private Scheduler? _scheduler = scheduler;

        public override void Dispose() => _scheduler?.DetachEntry(this);

        public bool TryDeactivate(
            out IScheduleEntry entry,
            out ScheduleSlot slot)
        {
            if (!Active) {
                entry = null!;
                slot = null!;
                return false;
            }
            Active = false;
            _scheduler = null;
            entry = Entry!;
            slot = Slot!;
            Entry = null;
            Slot = null;
            return true;
        }
    }

    public bool IsDisposed { get; private set; }

    private readonly Dictionary<ScheduleLabel, ScheduleSlot> _slots = [];
    private readonly List<ScheduleSlot> _registrationOrder = [];
    private readonly List<ScheduleSlot> _executionOrder = [];
    private readonly List<SourceRegistration> _sourceRegistrations = [];
    private readonly List<ScheduleSlot> _dirtyEntrySlots = [];
    private SourceRegistration[] _sourceSnapshot = [];
    private bool _sourcesChanged;
    private bool _sourcesNeedCompaction;
    private bool _planValid;
    private int _lifecycleCallbackDepth;
    private SchedulerPhase _phase;
    private World _world = null!;

    void IAddon.OnInitialize(World world) => _world = world;
    void IAddon.OnUninitialize(World world) => Dispose();

    public Scheduler AddSchedule(Schedule schedule)
    {
        ArgumentNullException.ThrowIfNull(schedule);
        EnsureActive();
        EnsureScheduleMutable();
        var slot = GetOrCreateSlot(schedule.Label);
        slot.Schedule = schedule;
        InvalidateStages();
        return this;
    }

    public Scheduler ConfigureSchedule(
        ScheduleLabel label,
        Func<Schedule, Schedule> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        EnsureActive();
        EnsureScheduleMutable();
        var current = _slots.TryGetValue(label, out var existing)
            ? existing.Schedule
            : null;
        var schedule = configure(current ?? new Schedule(label));
        if (schedule.Label != label) {
            throw new ArgumentException(
                "A schedule configuration cannot change its label.",
                nameof(configure));
        }
        var slot = GetOrCreateSlot(label);
        slot.Schedule = schedule;
        InvalidateStages();
        return this;
    }

    public Schedule? TryGet(ScheduleLabel label)
        => _slots.TryGetValue(label, out var slot) ? slot.Schedule : null;

    public Scheduler UseCorePipeline()
        => ConfigureSchedule(CoreSchedules.Startup, static schedule => schedule.AsManual())
            .ConfigureSchedule(CoreSchedules.First,
                static schedule => schedule.Before(CoreSchedules.PreUpdate))
            .ConfigureSchedule(CoreSchedules.PreUpdate,
                static schedule => schedule.Before(CoreSchedules.Update))
            .ConfigureSchedule(CoreSchedules.Update,
                static schedule => schedule.Before(CoreSchedules.PostUpdate))
            .ConfigureSchedule(CoreSchedules.PostUpdate,
                static schedule => schedule.Before(CoreSchedules.Last));

    public ScheduleRegistration RegisterEntry(
        ScheduleLabel label,
        IScheduleEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        EnsureActive();
        var slot = GetOrCreateSlot(label);
        foreach (var registration in slot.Entries) {
            if (registration.Active && ReferenceEquals(registration.Entry, entry)) {
                throw new InvalidOperationException(
                    "The schedule entry is already registered for this label.");
            }
        }

        var state = new EntryRegistration(this, slot, entry);
        slot.Entries.Add(state);
        try {
            InvokeLifecycle(() => entry.OnAttached(this, label));
        }
        catch {
            state.TryDeactivate(out _, out _);
            MarkEntriesForCompaction(slot);
            CompactIfIdle();
            throw;
        }
        return state;
    }

    public ScheduleRegistration RegisterSource(IScheduleSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        EnsureActive();
        foreach (var registration in _sourceRegistrations) {
            if (registration.Active && ReferenceEquals(registration.Source, source)) {
                throw new InvalidOperationException(
                    "The schedule source is already registered.");
            }
        }

        var state = new SourceRegistration(this, source);
        _sourceRegistrations.Add(state);
        _sourcesChanged = true;
        try {
            InvokeLifecycle(() => source.OnAttached(this));
        }
        catch {
            state.TryDeactivate(out _);
            _sourcesNeedCompaction = true;
            CompactIfIdle();
            throw;
        }
        return state;
    }

    private void Build()
    {
        var sorted = TopologicalSort(_registrationOrder);
        try {
            foreach (var slot in sorted) {
                EnsureStage(slot);
            }
        }
        catch {
            foreach (var slot in _registrationOrder) {
                slot.Stage?.Dispose();
                slot.Stage = null;
            }
            throw;
        }

        _executionOrder.Clear();
        _executionOrder.AddRange(sorted);
        _planValid = true;
    }

    public void Tick()
    {
        EnsureActive();
        BeginTick();
        try {
            var sources = GetSourceSnapshot();
            for (var i = 0; i < sources.Length; i++) {
                var registration = sources[i];
                if (registration.Active) {
                    registration.Source!.OnBeginTick();
                }
            }
            if (!_planValid) {
                Build();
            }

            var scheduleCount = _executionOrder.Count;
            for (var i = 0; i < scheduleCount; i++) {
                var slot = _executionOrder[i];
                _phase = SchedulerPhase.BeforeSchedule;
                for (var j = 0; j < sources.Length; j++) {
                    var registration = sources[j];
                    if (registration.Active) {
                        registration.Source!.OnBeforeSchedule(slot.Label);
                    }
                }
                if (slot.Schedule is not { Manual: true }) {
                    _phase = SchedulerPhase.Executing;
                    TickSlot(slot);
                }
                _phase = SchedulerPhase.BeginTick;
            }
        }
        finally {
            _phase = SchedulerPhase.Idle;
            CompactRegistrations();
        }
    }

    public void TickSchedule(ScheduleLabel label)
    {
        EnsureActive();
        BeginTick();
        try {
            var sources = GetSourceSnapshot();
            _phase = SchedulerPhase.BeforeSchedule;
            for (var i = 0; i < sources.Length; i++) {
                var registration = sources[i];
                if (registration.Active) {
                    registration.Source!.OnBeforeSchedule(label);
                }
            }
            if (_slots.TryGetValue(label, out var slot)) {
                EnsureStage(slot);
                _phase = SchedulerPhase.Executing;
                TickSlot(slot);
            }
        }
        finally {
            _phase = SchedulerPhase.Idle;
            CompactRegistrations();
        }
    }

    public void Dispose()
    {
        if (IsDisposed) {
            return;
        }
        if (_phase != SchedulerPhase.Idle || _lifecycleCallbackDepth != 0) {
            throw new InvalidOperationException(
                "The scheduler cannot be disposed during a tick or lifecycle callback.");
        }
        IsDisposed = true;

        var detachedEntries = new List<(IScheduleEntry Entry, ScheduleLabel Label)>();
        foreach (var slot in _registrationOrder) {
            foreach (var registration in slot.Entries) {
                if (registration.TryDeactivate(out var entry, out _)) {
                    detachedEntries.Add((entry, slot.Label));
                }
            }
        }

        var detachedSources = new List<IScheduleSource>(_sourceRegistrations.Count);
        foreach (var registration in _sourceRegistrations) {
            if (registration.TryDeactivate(out var source)) {
                detachedSources.Add(source);
            }
        }

        var stages = new List<SystemStage>();
        foreach (var slot in _registrationOrder) {
            if (slot.Stage is { } stage) {
                stages.Add(stage);
                slot.Stage = null;
            }
        }

        _slots.Clear();
        _registrationOrder.Clear();
        _executionOrder.Clear();
        _sourceRegistrations.Clear();
        _sourceSnapshot = [];
        _dirtyEntrySlots.Clear();
        _sourcesChanged = false;
        _sourcesNeedCompaction = false;
        _planValid = false;

        List<Exception>? errors = null;
        foreach (var (entry, label) in detachedEntries) {
            try {
                InvokeLifecycle(() => entry.OnDetached(this, label));
            }
            catch (Exception error) {
                AddError(ref errors, error);
            }
        }
        foreach (var source in detachedSources) {
            try {
                InvokeLifecycle(() => source.OnDetached(this));
            }
            catch (Exception error) {
                AddError(ref errors, error);
            }
        }
        foreach (var stage in stages) {
            try {
                stage.Dispose();
            }
            catch (Exception error) {
                AddError(ref errors, error);
            }
        }
        ThrowErrors(errors);
    }

    private void DetachSource(SourceRegistration registration)
    {
        if (!registration.TryDeactivate(out var source)) {
            return;
        }
        _sourcesNeedCompaction = true;
        _sourcesChanged = true;
        try {
            InvokeLifecycle(() => source.OnDetached(this));
        }
        finally {
            CompactIfIdle();
        }
    }

    private void DetachEntry(EntryRegistration registration)
    {
        if (!registration.TryDeactivate(out var entry, out var slot)) {
            return;
        }
        MarkEntriesForCompaction(slot);
        try {
            InvokeLifecycle(() => entry.OnDetached(this, slot.Label));
        }
        finally {
            CompactIfIdle();
        }
    }

    private void BeginTick()
    {
        if (_phase != SchedulerPhase.Idle || _lifecycleCallbackDepth != 0) {
            throw new InvalidOperationException(
                "Scheduler ticks cannot be nested or started from a lifecycle callback.");
        }
        _phase = SchedulerPhase.BeginTick;
    }

    private static void TickSlot(ScheduleSlot slot)
    {
        slot.Stage?.Tick();
        var entries = slot.Entries;
        var count = entries.Count;
        for (var i = 0; i < count; i++) {
            var registration = entries[i];
            if (registration.Active) {
                registration.Entry!.Tick();
            }
        }
    }

    private void EnsureStage(ScheduleSlot slot)
    {
        if (slot.Stage is null && slot.Schedule is { } schedule
            && !schedule.Chain.Entries.IsEmpty) {
            slot.Stage = schedule.Build(_world);
        }
    }

    private ScheduleSlot GetOrCreateSlot(ScheduleLabel label)
    {
        if (!_slots.TryGetValue(label, out var slot)) {
            slot = new ScheduleSlot(label);
            _slots.Add(label, slot);
            _registrationOrder.Add(slot);
            _planValid = false;
        }
        return slot;
    }

    private void InvalidateStages()
    {
        foreach (var slot in _registrationOrder) {
            slot.Stage?.Dispose();
            slot.Stage = null;
        }
        _planValid = false;
    }

    private void MarkEntriesForCompaction(ScheduleSlot slot)
    {
        if (slot.EntriesNeedCompaction) {
            return;
        }
        slot.EntriesNeedCompaction = true;
        _dirtyEntrySlots.Add(slot);
    }

    private void CompactIfIdle()
    {
        if (_phase == SchedulerPhase.Idle && _lifecycleCallbackDepth == 0) {
            CompactRegistrations();
        }
    }

    private void CompactRegistrations()
    {
        if (_sourcesNeedCompaction) {
            _sourceRegistrations.RemoveAll(
                static registration => !registration.Active);
            _sourcesNeedCompaction = false;
            _sourcesChanged = true;
            _sourceSnapshot = [];
        }

        foreach (var slot in _dirtyEntrySlots) {
            slot.Entries.RemoveAll(static registration => !registration.Active);
            slot.EntriesNeedCompaction = false;
            if (slot.Schedule is null && slot.Entries.Count == 0) {
                _slots.Remove(slot.Label);
                _registrationOrder.Remove(slot);
                _executionOrder.Remove(slot);
                _planValid = false;
            }
        }
        _dirtyEntrySlots.Clear();
    }

    private void EnsureActive()
        => ObjectDisposedException.ThrowIf(IsDisposed, this);

    private void EnsureScheduleMutable()
    {
        if (_phase is SchedulerPhase.BeforeSchedule or SchedulerPhase.Executing) {
            throw new InvalidOperationException(
                "Schedules can only be changed while idle or from OnBeginTick.");
        }
    }

    private SourceRegistration[] GetSourceSnapshot()
    {
        if (_sourcesChanged) {
            _sourceSnapshot = [.. _sourceRegistrations.Where(
                static registration => registration.Active)];
            _sourcesChanged = false;
        }
        return _sourceSnapshot;
    }

    private void InvokeLifecycle(Action callback)
    {
        _lifecycleCallbackDepth++;
        try {
            callback();
        }
        finally {
            _lifecycleCallbackDepth--;
            if (_lifecycleCallbackDepth == 0
                && _phase == SchedulerPhase.Idle
                && !IsDisposed) {
                CompactRegistrations();
            }
        }
    }

    private static void AddError(
        ref List<Exception>? errors,
        Exception error)
    {
        errors ??= [];
        errors.Add(error);
    }

    private static void ThrowErrors(List<Exception>? errors)
    {
        if (errors is null) {
            return;
        }
        if (errors.Count == 1) {
            ExceptionDispatchInfo.Capture(errors[0]).Throw();
        }
        throw new AggregateException(errors);
    }

    private static ScheduleSlot[] TopologicalSort(IReadOnlyList<ScheduleSlot> slots)
    {
        var labelIndex = new Dictionary<ScheduleLabel, int>(slots.Count);
        for (var i = 0; i < slots.Count; i++) {
            labelIndex[slots[i].Label] = i;
        }

        var edges = new HashSet<(int From, int To)>();
        for (var i = 0; i < slots.Count; i++) {
            if (slots[i].Schedule is not { } schedule) {
                continue;
            }
            foreach (var before in schedule.RunsBefore) {
                if (labelIndex.TryGetValue(before, out var target) && target != i) {
                    edges.Add((i, target));
                }
            }
            foreach (var after in schedule.RunsAfter) {
                if (labelIndex.TryGetValue(after, out var predecessor) && predecessor != i) {
                    edges.Add((predecessor, i));
                }
            }
        }

        var inDegree = new int[slots.Count];
        var successors = new List<int>[slots.Count];
        for (var i = 0; i < successors.Length; i++) {
            successors[i] = [];
        }
        foreach (var (from, to) in edges) {
            successors[from].Add(to);
            inDegree[to]++;
        }
        foreach (var list in successors) {
            list.Sort();
        }

        var ready = new PriorityQueue<int, int>(slots.Count);
        for (var i = 0; i < slots.Count; i++) {
            if (inDegree[i] == 0) {
                ready.Enqueue(i, i);
            }
        }

        var result = new ScheduleSlot[slots.Count];
        var count = 0;
        while (ready.TryDequeue(out var current, out _)) {
            result[count++] = slots[current];
            foreach (var successor in successors[current]) {
                if (--inDegree[successor] == 0) {
                    ready.Enqueue(successor, successor);
                }
            }
        }
        if (count != slots.Count) {
            throw new InvalidSystemDependencyException(
                "Cycle detected in schedule dependency graph.");
        }
        return result;
    }
}
