namespace Sia;

using System.Runtime.InteropServices;

public class Scheduler
{
    public enum Status
    {
        Added,
        Unset,
        Adding,
        Removed
    }

    public class TaskGraphNode : IDisposable
    {
        public required Scheduler Scheduler { get; init; }
        public required Func<bool>? Callback { get; init; }

        public IReadOnlySet<TaskGraphNode>? DependedTasks => _dependedTasks;
        public IReadOnlySet<TaskGraphNode>? DependingTasks => _dependingTasks;
        public Status Status { get; internal set; } = Status.Added;
        public int? OrphanSeqIndex { get; internal set; }

        public object? UserData { get; set; }

        internal HashSet<TaskGraphNode>? _dependedTasks;
        internal HashSet<TaskGraphNode>? _dependingTasks;

        internal TaskGraphNode() { }

        public void Terminate()
            => DoStop(force: true);

        public void Dispose()
        {
            DoStop(force: false);
            GC.SuppressFinalize(this);
        }

        private void DoStop(bool force)
        {
            if (Status == Status.Removed || !Scheduler._tasks.Remove(this)) {
                throw new ArgumentException("Failed to remove task: invalid task");
            }
            if (Scheduler._ticking) {
                Scheduler._tasksToRemove.Add(this);
                return;
            }
            Scheduler.RawRemoveTask(this, force);
        }
    }

    public int TaskCount => _tasks.Count;

    public IReadOnlySet<TaskGraphNode> Tasks => _tasks;
    public IReadOnlyList<TaskGraphNode> OrphanTaskSequence => _orphanTaskSeq;
    public IReadOnlyList<TaskGraphNode> DependedTaskSequnce => _dependedTaskSeq;

    private readonly HashSet<TaskGraphNode> _tasks = [];
    private readonly List<TaskGraphNode> _orphanTaskSeq = [];
    private readonly List<TaskGraphNode> _dependedTaskSeq = [];
    private readonly List<TaskGraphNode> _tasksToRemove = [];

    private bool _dependedTaskSeqDirty = false;
    private bool _ticking = false;

    public TaskGraphNode CreateTask(Func<bool> callback)
    {
        var task = new TaskGraphNode {
            Scheduler = this,
            Callback = callback,
            OrphanSeqIndex = _orphanTaskSeq.Count
        };
        _tasks.Add(task);
        _orphanTaskSeq.Add(task);
        return task;
    }

    public TaskGraphNode CreateTask()
    {
        var task = new TaskGraphNode {
            Scheduler = this,
            Callback = null
        };
        _tasks.Add(task);
        return task;
    }

    private int AddDependenciesToTask(TaskGraphNode task, IEnumerable<TaskGraphNode> dependencies)
    {
        int count = 0;
        foreach (var dependedTask in dependencies) {
            if (!_tasks.Contains(dependedTask)) {
                foreach (var other in dependencies) {
                    if (other == dependedTask) {
                        break;
                    }
                    other._dependingTasks!.Remove(task);
                    if (other._dependingTasks.Count == 0) {
                        other._dependingTasks = null;
                    }
                }
                throw new InvalidTaskDependencyException("Failed to create task: invalid dependency: " + dependedTask);
            }

            dependedTask._dependingTasks ??= [];
            dependedTask._dependingTasks.Add(task);

            task._dependedTasks ??= [];
            task._dependedTasks.Add(dependedTask);
            count++;
        }
        return count;
    }

    public TaskGraphNode CreateTask(Func<bool> callback, IEnumerable<TaskGraphNode> dependencies)
    {
        var task = new TaskGraphNode {
            Scheduler = this,
            Callback = callback
        };
        if (AddDependenciesToTask(task, dependencies) == 0) {
            task.OrphanSeqIndex = _orphanTaskSeq.Count;
            _orphanTaskSeq.Add(task);
        }
        else {
            _dependedTaskSeqDirty = true;
        }
        _tasks.Add(task);
        return task;
    }

    public TaskGraphNode CreateTask(IEnumerable<TaskGraphNode> dependencies)
    {
        var task = new TaskGraphNode {
            Scheduler = this,
            Callback = null
        };
        AddDependenciesToTask(task, dependencies);
        _tasks.Add(task);
        return task;
    }

    private void RawRemoveTask(TaskGraphNode task, bool force)
    {
        if (!force && task.DependingTasks != null && task.DependingTasks.Count != 0) {
            throw new TaskDependedException("Failed to remove task: there are other tasks depending on it");
        }
        if (task.OrphanSeqIndex is int index) {
            int lastIndex = _orphanTaskSeq.Count - 1;
            if (index != lastIndex) {
                var lastTask = _orphanTaskSeq[lastIndex];
                _orphanTaskSeq[index] = lastTask;
                lastTask.OrphanSeqIndex = index;
            }
            _orphanTaskSeq.RemoveAt(lastIndex);
        }
        else if (task._dependedTasks != null) {
            foreach (var dependedTask in task._dependedTasks) {
                dependedTask._dependingTasks!.Remove(task);
            }
            _dependedTaskSeqDirty = true;
        }
        _tasks.Remove(task);
        task.Status = Status.Removed;
    }

    private void AddDepededTaskToSeq(TaskGraphNode task)
    {
        switch (task.Status) {
        case Status.Added:
            return;
        case Status.Adding:
            throw new InvalidTaskDependencyException("Failed to calculate task sequence: circular dependency found for " + task);
        default:
            if (task._dependedTasks == null || task._dependedTasks.Count == 0) {
                task.Status = Status.Added;
                return;
            }
            task.Status = Status.Adding;
            foreach (var dependedTask in task._dependedTasks) {
                AddDepededTaskToSeq(dependedTask);
            }
            if (task.Callback != null) {
                _dependedTaskSeq.Add(task);
            }
            task.Status = Status.Added;
            break;
        }
    }

    public void Tick()
    {
        if (_dependedTaskSeqDirty) {
            _dependedTaskSeq.Clear();
            foreach (var task in _tasks) {
                task.Status = Status.Unset;
            }
            foreach (var task in _tasks) {
                AddDepededTaskToSeq(task);
            }
            _dependedTaskSeqDirty = false;
        }

        _ticking = true;

        try {
            var count = _orphanTaskSeq.Count;
            for (int i = 0; i != count; ++i) {
                var task = _orphanTaskSeq[i];
                if (task.Callback!()) {
                    _tasksToRemove.Add(task);
                }
            }
            count = _dependedTaskSeq.Count;
            for (int i = 0; i != count; ++i) {
                var task = _dependedTaskSeq[i];
                if (task.Callback!()) {
                    _tasksToRemove.Add(task);
                }
            }
        }
        finally {
            _ticking = false;

            if (_tasksToRemove.Count != 0) {
                try {
                    foreach (var task in CollectionsMarshal.AsSpan(_tasksToRemove)) {
                        if (task.Status != Status.Removed) {
                            RawRemoveTask(task, false);
                        }
                    }
                }
                finally {
                    _tasksToRemove.Clear();
                }
            }
        }
    }
}