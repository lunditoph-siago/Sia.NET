using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;

namespace Sia;

internal sealed class EntityStatePage(int firstId, int length)
{
    internal struct Row
    {
        public int Slot;
        public bool Releasing;
        public bool NotifyingMigration;
        public bool UsesDefaultHost;
    }

    internal sealed class Binding
    {
        public IEntityHost? Host;
        public IEventSink<Entity>? EventSink;
    }

    internal readonly int FirstId = firstId;
    internal readonly Row[] Rows = new Row[length];
    internal IEntityHost? DefaultHost { get; private set; }
    private int _defaultHostRows;
    private Binding?[]? _bindings;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal bool TryBindDefault(IEntityHost host, int count = 1)
    {
        var previous = DefaultHost;
        if (previous is null) {
            DefaultHost = host;
        }
        else if (!ReferenceEquals(previous, host)) {
            return false;
        }
        _defaultHostRows += count;
        return true;
    }

    internal void ReleaseDefault()
    {
        if (--_defaultHostRows == 0) DefaultHost = null;
    }

    internal Binding? GetBinding(int index) => _bindings?[index];

    internal Binding GetOrCreateBinding(int index)
        => (_bindings ??= new Binding?[Rows.Length])[index] ??= new Binding();

    internal void ClearBinding(int index)
    {
        if (_bindings is { } bindings) bindings[index] = null;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal Entity Initialize(IEntityHost? host, int slot, int id)
    {
        var index = unchecked(id - FirstId);
        ref var row = ref Rows[index];
        row.Slot = slot;
        if (host is not null) {
            if (TryBindDefault(host)) row.UsesDefaultHost = true;
            else GetOrCreateBinding(index).Host = host;
        }
        return new Entity(new EntityState(this, id));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal Entity InitializeDefault(int slot, int id)
    {
        ref var row = ref Rows[unchecked(id - FirstId)];
        row.Slot = slot;
        row.UsesDefaultHost = true;
        return new Entity(new EntityState(this, id));
    }
}

internal readonly struct EntityState(EntityStatePage page, int id)
{
    private readonly EntityStatePage? _page = page;
    private readonly int _id = id;

    private ref EntityStatePage.Row Row {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get {
            var page = _page!;
            return ref page.Rows[unchecked(_id - page.FirstId)];
        }
    }

    public EntityId Id => new(_id);
    public bool IsDefault => _page is null;

    public IEntityHost? Host {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get {
            var page = _page;
            if (page is null) return null;
            var index = unchecked(_id - page.FirstId);
            return page.Rows[index].UsesDefaultHost ? page.DefaultHost : page.GetBinding(index)?.Host;
        }
        set {
            var page = _page!;
            var index = unchecked(_id - page.FirstId);
            ref var row = ref page.Rows[index];
            if (row.UsesDefaultHost) {
                if (ReferenceEquals(value, page.DefaultHost)) return;
                if (value is not null) page.GetOrCreateBinding(index).Host = value;
                row.UsesDefaultHost = false;
                page.ReleaseDefault();
                return;
            }

            var binding = page.GetBinding(index);
            if (value is null || page.TryBindDefault(value)) {
                row.UsesDefaultHost = value is not null;
                if (binding is not null) {
                    binding.Host = null;
                    if (binding.EventSink is null) page.ClearBinding(index);
                }
            }
            else {
                (binding ?? page.GetOrCreateBinding(index)).Host = value;
            }
        }
    }

    public IEventSink<Entity>? EventSink {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get {
            var page = _page;
            return page?.GetBinding(unchecked(_id - page.FirstId))?.EventSink;
        }
        set {
            var page = _page!;
            var index = unchecked(_id - page.FirstId);
            if (value is not null) {
                page.GetOrCreateBinding(index).EventSink = value;
            }
            else if (page.GetBinding(index) is { } binding) {
                binding.EventSink = null;
                if (binding.Host is null) page.ClearBinding(index);
            }
        }
    }

    public ref int Slot => ref Row.Slot;
    public ref bool Releasing => ref Row.Releasing;
    public ref bool NotifyingMigration => ref Row.NotifyingMigration;
    public bool IsTransitioning => Releasing || NotifyingMigration;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsSame(EntityState other) => _id == other._id && ReferenceEquals(_page, other._page);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Invalidate()
    {
        var page = _page!;
        var index = unchecked(_id - page.FirstId);
        ref var row = ref page.Rows[index];
        if (row.UsesDefaultHost) page.ReleaseDefault();
        page.ClearBinding(index);
        row = new EntityStatePage.Row { Slot = -1 };
    }
}

internal sealed class EntityStatePool
{
    private const int PageSize = 256;

    private EntityStatePage? _page;
    private int _nextId;
    private int _idEnd;
    private bool _retired;

#if !BROWSER
    internal bool CanRentFreshRange => !_retired && _nextId == _idEnd;

    internal static void RentFreshMany(ParallelRunner runner, EntityStorage destination, IEntityHost host, ref int completed)
    {
        var count = destination.Count;
        var firstId = EntityId.Reserve(count);
        var split = count / 2;
        var left = 0;
        var right = 0;
        var completion = RunnerBarrier.GetForScopedWork();
        Exception? failure = null;
        try {
            runner.Run(1, _ => InitializeFresh(destination.AsSpan()[split..], host, split,
                unchecked(firstId + split), ref right), completion);
            InitializeFresh(destination.AsSpan()[..split], host, 0, firstId, ref left);
        }
        catch (Exception exception) {
            failure = exception;
        }
        try {
            completion.WaitAndReturnScopedWork();
        }
        catch (Exception exception) {
            failure ??= exception;
        }

        completed = left == split ? left + right : left;
        if (failure is not null) ExceptionDispatchInfo.Throw(failure);
    }

    private static void InitializeFresh(Span<Entity> destination, IEntityHost host, int firstSlot, int firstId, ref int completed)
    {
        var i = 0;
        try {
            while (i < destination.Length) {
                var count = Math.Min(PageSize, destination.Length - i);
                var pageId = unchecked(firstId + i);
                var page = new EntityStatePage(pageId, count);
                page.TryBindDefault(host, count);
                FillDefaultPage(destination.Slice(i, count), page, firstSlot + i, pageId);
                i += count;
            }
        }
        finally {
            completed = i;
        }
    }
#endif

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Entity Rent(IEntityHost? host = null, int slot = -1)
    {
        ObjectDisposedException.ThrowIf(_retired, this);
        if (_nextId == _idEnd) AllocatePage();
        return _page!.Initialize(host, slot, unchecked(_nextId++));
    }

    internal void RentMany(Span<Entity> entities, IEntityHost host, int firstSlot, ref int completed)
    {
        ObjectDisposedException.ThrowIf(_retired, this);
        var i = 0;
        try {
            while (i < entities.Length) {
                if (_nextId == _idEnd) AllocatePage();
                var count = Math.Min(entities.Length - i, unchecked(_idEnd - _nextId));
                var page = _page!;
                if (page.TryBindDefault(host, count)) {
                    FillDefaultPage(entities.Slice(i, count), page, firstSlot + i, _nextId);
                    _nextId = unchecked(_nextId + count);
                    i += count;
                }
                else {
                    entities[i] = page.Initialize(host, firstSlot + i, unchecked(_nextId++));
                    i++;
                }
            }
        }
        finally {
            completed = i;
        }
    }

    private static void FillDefaultPage(Span<Entity> entities, EntityStatePage page, int firstSlot, int firstId)
    {
        for (var i = 0; i < entities.Length; i++) {
            entities[i] = page.InitializeDefault(firstSlot + i, unchecked(firstId + i));
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void AllocatePage()
    {
        var firstId = EntityId.Reserve(PageSize);
        _page = new(firstId, PageSize);
        _nextId = firstId;
        _idEnd = unchecked(firstId + PageSize);
    }

    public void Retire()
    {
        _retired = true;
        _page = null;
    }
}
