namespace Sia;

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
        public List<IScheduleEntry> Entries { get; } = [];
    }

    public bool IsDisposed { get; private set; }

    private readonly Dictionary<ScheduleLabel, ScheduleSlot> _slots = [];
    private readonly List<ScheduleSlot> _registrationOrder = [];
    private readonly List<ScheduleSlot> _executionOrder = [];
    private readonly List<IScheduleSource> _sources = [];
    private IScheduleSource[] _sourceSnapshot = [];
    private bool _sourcesChanged;
    private bool _planValid;
    private SchedulerPhase _phase;
    private World _world = null!;

    void IAddon.OnInitialize(World world) => _world = world;
    void IAddon.OnUninitialize(World world) => Dispose();

    public Scheduler AddSchedule(Schedule schedule)
    {
        ArgumentNullException.ThrowIfNull(schedule);
        EnsureMutable();
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
        EnsureMutable();
        EnsureScheduleMutable();
        var current = _slots.TryGetValue(label, out var existing)
            ? existing.Schedule
            : null;
        var schedule = configure(current ?? new Schedule(label));
        if (schedule.Label != label) {
            throw new ArgumentException("A schedule configuration cannot change its label.", nameof(configure));
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

    public void AddEntry(ScheduleLabel label, IScheduleEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        EnsureMutable();
        var entries = GetOrCreateSlot(label).Entries;
        for (var i = 0; i < entries.Count; i++) {
            if (ReferenceEquals(entries[i], entry)) {
                return;
            }
        }
        entries.Add(entry);
    }

    public void RemoveEntry(ScheduleLabel label, IScheduleEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        EnsureMutable();
        if (!_slots.TryGetValue(label, out var slot)) {
            return;
        }
        for (var i = 0; i < slot.Entries.Count; i++) {
            if (ReferenceEquals(slot.Entries[i], entry)) {
                slot.Entries.RemoveAt(i);
                return;
            }
        }
    }

    public void AddSource(IScheduleSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        EnsureMutable();
        for (var i = 0; i < _sources.Count; i++) {
            if (ReferenceEquals(_sources[i], source)) {
                return;
            }
        }
        _sources.Add(source);
        _sourcesChanged = true;
    }

    public void RemoveSource(IScheduleSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        EnsureMutable();
        for (var i = 0; i < _sources.Count; i++) {
            if (ReferenceEquals(_sources[i], source)) {
                _sources.RemoveAt(i);
                _sourcesChanged = true;
                return;
            }
        }
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
        EnsureMutable();
        BeginTick();
        try {
            var sources = GetSourceSnapshot();
            for (var i = 0; i < sources.Length; i++) {
                sources[i].OnBeginTick();
            }
            if (!_planValid) {
                Build();
            }

            var scheduleCount = _executionOrder.Count;
            for (var i = 0; i < scheduleCount; i++) {
                var slot = _executionOrder[i];
                _phase = SchedulerPhase.BeforeSchedule;
                for (var j = 0; j < sources.Length; j++) {
                    sources[j].OnBeforeSchedule(slot.Label);
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
        }
    }

    public void TickSchedule(ScheduleLabel label)
    {
        EnsureMutable();
        BeginTick();
        try {
            var sources = GetSourceSnapshot();
            _phase = SchedulerPhase.BeforeSchedule;
            for (var i = 0; i < sources.Length; i++) {
                sources[i].OnBeforeSchedule(label);
            }
            if (_slots.TryGetValue(label, out var slot)) {
                EnsureStage(slot);
                _phase = SchedulerPhase.Executing;
                TickSlot(slot);
            }
        }
        finally {
            _phase = SchedulerPhase.Idle;
        }
    }

    public void Dispose()
    {
        if (IsDisposed) {
            return;
        }
        if (_phase != SchedulerPhase.Idle) {
            throw new InvalidOperationException("The scheduler cannot be disposed during a tick.");
        }
        IsDisposed = true;
        foreach (var slot in _registrationOrder) {
            slot.Stage?.Dispose();
            slot.Stage = null;
        }
        _slots.Clear();
        _registrationOrder.Clear();
        _executionOrder.Clear();
        _sources.Clear();
        _sourceSnapshot = [];
        _sourcesChanged = false;
        _planValid = false;
    }

    private void BeginTick()
    {
        if (_phase != SchedulerPhase.Idle) {
            throw new InvalidOperationException("Scheduler ticks cannot be nested.");
        }
        _phase = SchedulerPhase.BeginTick;
    }

    private static void TickSlot(ScheduleSlot slot)
    {
        slot.Stage?.Tick();
        for (var i = 0; i < slot.Entries.Count; i++) {
            slot.Entries[i].Tick();
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

    private void EnsureMutable()
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        if (_phase == SchedulerPhase.Executing) {
            throw new InvalidOperationException(
                "Scheduler structure cannot be changed while schedules are executing.");
        }
    }

    private void EnsureScheduleMutable()
    {
        if (_phase == SchedulerPhase.BeforeSchedule) {
            throw new InvalidOperationException(
                "Schedules cannot be changed from an OnBeforeSchedule callback.");
        }
    }

    private IScheduleSource[] GetSourceSnapshot()
    {
        if (_sourcesChanged) {
            _sourceSnapshot = [.. _sources];
            _sourcesChanged = false;
        }
        return _sourceSnapshot;
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
