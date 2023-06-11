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

    public class TaskGraphNode
    {
        public required Func<bool>? Callback { get; init; }
        public IReadOnlySet<TaskGraphNode>? DependedTasks => _dependedTasks;
        public IReadOnlySet<TaskGraphNode>? DependingTasks => _dependingTasks;
        public Status Status { get; internal set; } = Status.Added;
        public int? OrphanSeqIndex { get; internal set; }

        internal HashSet<TaskGraphNode>? _dependedTasks;
        internal HashSet<TaskGraphNode>? _dependingTasks;

        internal TaskGraphNode() {}
    }

    public int TaskCount => _tasks.Count;

    public IReadOnlySet<TaskGraphNode> Tasks => _tasks;
    public IReadOnlyList<TaskGraphNode> OrphanTaskSequence => _orphanTaskSeq;
    public IReadOnlyList<TaskGraphNode> DependedTaskSequnce => _dependedTaskSeq;

    private HashSet<TaskGraphNode> _tasks = new();
    private List<TaskGraphNode> _orphanTaskSeq = new();
    private List<TaskGraphNode> _dependedTaskSeq = new();
    private List<TaskGraphNode> _tasksToRemove = new();

    private bool _dependedTaskSeqDirty = false;
    private bool _ticking = false;

    public TaskGraphNode CreateTask(Func<bool> callback)
    {
        var task = new TaskGraphNode {
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
            Callback = null
        };
        _tasks.Add(task);
        return task;
    }

    private void AddDependenciesToTask(TaskGraphNode task, IEnumerable<TaskGraphNode> dependencies)
    {
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

            dependedTask._dependingTasks ??= new();
            dependedTask._dependingTasks.Add(task);

            task._dependedTasks ??= new();
            task._dependedTasks.Add(dependedTask);
        }
    }

    public TaskGraphNode CreateTask(Func<bool> callback, IEnumerable<TaskGraphNode> dependencies)
    {
        var task = new TaskGraphNode {
            Callback = callback
        };
        AddDependenciesToTask(task, dependencies);
        _tasks.Add(task);
        _dependedTaskSeqDirty = true;
        return task;
    }

    public TaskGraphNode CreateTask(IEnumerable<TaskGraphNode> dependencies)
    {
        var task = new TaskGraphNode {
            Callback = null
        };
        AddDependenciesToTask(task, dependencies);
        _tasks.Add(task);
        _dependedTaskSeqDirty = true;
        return task;
    }

    private void RawRemoveTask(TaskGraphNode task)
    {
        if (task.DependingTasks != null && task.DependingTasks.Count != 0) {
            task.Status = Status.Added;
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

    public void RemoveTask(TaskGraphNode task)
    {
        if (task.Status == Status.Removed || !_tasks.Remove(task)) {
            throw new ArgumentException("Failed to remove task: invalid task");
        }
        if (_ticking) {
            _tasksToRemove.Add(task);
            return;
        }
        RawRemoveTask(task);
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
            foreach (var task in CollectionsMarshal.AsSpan(_orphanTaskSeq)) {
                if (task.Callback!()) {
                    _tasksToRemove.Add(task);
                }
            }
            foreach (var task in CollectionsMarshal.AsSpan(_dependedTaskSeq)) {
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
                            RawRemoveTask(task);
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