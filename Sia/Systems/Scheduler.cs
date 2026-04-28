namespace Sia;

public sealed class Scheduler : IAddon, IDisposable
{
    private readonly List<Schedule> _schedules = [];
    private SystemStage[]? _stages;
    private World _world = null!;

    void IAddon.OnInitialize(World world) => _world = world;
    void IAddon.OnUninitialize(World world) => Dispose();

    public Scheduler AddSchedule(Schedule schedule)
        => AddOrReplace(schedule);

    public Scheduler ConfigureSchedule(
        ScheduleLabel label,
        Func<Schedule, Schedule> configure)
        => AddOrReplace(configure(TryGet(label) ?? new Schedule(label)));

    public Schedule GetOrCreate(ScheduleLabel label)
    {
        foreach (var s in _schedules) {
            if (s.Label == label) return s;
        }
        var schedule = new Schedule(label);
        AddSchedule(schedule);
        return schedule;
    }

    public Schedule? TryGet(ScheduleLabel label)
    {
        foreach (var s in _schedules) {
            if (s.Label == label) return s;
        }
        return null;
    }

    public void Build()
    {
        DisposeStages();
        var schedules = TopologicalSort(_schedules);
        var stages = new SystemStage[schedules.Length];
        for (var i = 0; i < schedules.Length; i++) {
            stages[i] = schedules[i].Build(_world);
        }
        _stages = stages;
    }

    public void Tick()
    {
        if (_stages == null) {
            throw new InvalidOperationException(
                "Scheduler.Build() must be called before Tick().");
        }
        foreach (var stage in _stages) {
            stage.Tick();
        }
    }

    public void Dispose()
    {
        DisposeStages();
        _schedules.Clear();
    }

    private Scheduler AddOrReplace(Schedule schedule)
    {
        var index = _schedules.FindIndex(s => s.Label == schedule.Label);
        if (index == -1) {
            _schedules.Add(schedule);
        }
        else {
            _schedules[index] = schedule;
        }
        DisposeStages();
        return this;
    }

    private void DisposeStages()
    {
        if (_stages == null) {
            return;
        }
        foreach (var stage in _stages) {
            stage.Dispose();
        }
        _stages = null;
    }

    private static Schedule[] TopologicalSort(IReadOnlyList<Schedule> schedules)
    {
        var labelIndex = new Dictionary<ScheduleLabel, int>(schedules.Count);
        for (var i = 0; i < schedules.Count; i++) {
            labelIndex[schedules[i].Label] = i;
        }

        var inDegree = new int[schedules.Count];
        var successors = new List<int>[schedules.Count];
        for (var i = 0; i < successors.Length; i++) {
            successors[i] = [];
        }

        for (var i = 0; i < schedules.Count; i++) {
            var schedule = schedules[i];
            foreach (var before in schedule.RunsBefore) {
                if (labelIndex.TryGetValue(before, out var target) && target != i) {
                    successors[i].Add(target);
                    inDegree[target]++;
                }
            }
            foreach (var after in schedule.RunsAfter) {
                if (labelIndex.TryGetValue(after, out var pred) && pred != i) {
                    successors[pred].Add(i);
                    inDegree[i]++;
                }
            }
        }

        var queue = new Queue<int>(schedules.Count);
        for (var i = 0; i < schedules.Count; i++) {
            if (inDegree[i] == 0) {
                queue.Enqueue(i);
            }
        }

        var result = new Schedule[schedules.Count];
        var count = 0;
        while (queue.Count > 0) {
            var current = queue.Dequeue();
            result[count++] = schedules[current];
            foreach (var successor in successors[current]) {
                if (--inDegree[successor] == 0) {
                    queue.Enqueue(successor);
                }
            }
        }

        if (count != schedules.Count) {
            throw new InvalidSystemDependencyException(
                "Cycle detected in schedule dependency graph.");
        }

        return result;
    }
}
