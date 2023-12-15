namespace Sia;

using System.Collections;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Diagnostics.CodeAnalysis;

using CommunityToolkit.HighPerformance.Buffers;

public sealed class SparseSet<T>(int pageCount, int pageSize)
    : IDictionary<int, T>, IReadOnlyDictionary<int, T>
{
    public int PageCount { get; init; } = pageCount;
    public int PageSize { get; init; } = pageSize;
    public int Capacity { get; init; } = pageCount * pageSize;
    public int Count => _dense.Count;

    public T this[int index] {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get {
            ref var page = ref GetPage(index, out int entryIndex);
            if (entryIndex == -1) {
                throw new KeyNotFoundException("Index not found");
            }
            return _dense[page[entryIndex]];
        }
        set {
            GetOrAddValueRef(index, out bool _) = value;
        }
    }

    public IEnumerable<int> Keys => _reverse;
    public IEnumerable<T> Values => _dense;

    public List<int> UnsafeRawKeys => _reverse;
    public List<T> UnsafeRawValues => _dense;

    ICollection<int> IDictionary<int, T>.Keys => _reverse.AsReadOnly();
    ICollection<T> IDictionary<int, T>.Values => _dense.AsReadOnly();

    public bool IsReadOnly => false;

    private readonly List<T> _dense = [];
    private readonly List<int> _reverse = [];
    private readonly Page[] _pages = new Page[pageCount];

    private struct Page
    {
        public MemoryOwner<int>? Memory;
        public int Count;

        public readonly ref int this[int index] {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => ref Memory!.Span[index];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void DecreaseRef()
        {
            Count--;
            if (Count == 0) {
                Memory!.Dispose();
                this = default;
            }
            else if (Count < 0) {
                Count = 0;
                throw new InvalidOperationException("Page ref count should be less than 0");
            }
        }

    }

    public ReadOnlySpan<int> AsKeySpan() => CollectionsMarshal.AsSpan(_reverse);
    public Span<T> AsValueSpan() => CollectionsMarshal.AsSpan(_dense);

    public IEnumerator<KeyValuePair<int, T>> GetEnumerator()
    {
        for (int i = 0; i < _dense.Count; ++i) {
            yield return KeyValuePair.Create(_reverse[i], _dense[i]);
        }
    }

    IEnumerator IEnumerable.GetEnumerator()
        => GetEnumerator();

    public void CopyTo(KeyValuePair<int, T>[] array, int arrayIndex)
    {
        var count = _dense.Count;
        for (int i = 0; i < count; ++i) {
            array[i + arrayIndex] = KeyValuePair.Create(_reverse[i], _dense[i]);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Add(int index, T value)
    {
        ref var valueRef = ref GetOrAddValueRef(index, out bool exists);
        if (exists) {
            return false;
        }
        valueRef = value;
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Add(KeyValuePair<int, T> item)
        => Add(item.Key, item.Value);

    void IDictionary<int, T>.Add(int key, T value)
    {
        if (!Add(key, value)) {
            throw new ArgumentException("Invalid arguments");
        }
    }

    void ICollection<KeyValuePair<int, T>>.Add(KeyValuePair<int, T> item)
    {
        if (!Add(item)) {
            throw new ArgumentException("Invalid arguments");
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool ContainsKey(int index)
        => !Unsafe.IsNullRef(ref GetPage(index, out int _));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Contains(KeyValuePair<int, T> item)
    {
        ref var page = ref GetPage(item.Key, out int entryIndex);
        if (entryIndex == -1) {
            return false;
        }
        return _dense[page[entryIndex]]!.Equals(item.Value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGetValue(int index, [MaybeNullWhen(false)] out T value)
    {
        ref var page = ref GetPage(index, out int entryIndex);
        if (entryIndex == -1) {
            value = default;
            return false;
        }
        value = _dense[page[entryIndex]];
        return true;
    }

    public bool Remove(int index, [MaybeNullWhen(false)] out T value)
    {
        ref var page = ref GetPage(index, out int entryIndex);
        if (entryIndex == -1) {
            value = default;
            return false;
        }

        ref int valueIndexRef = ref page[entryIndex];
        var span = CollectionsMarshal.AsSpan(_dense);

        value = span[valueIndexRef];
        RemoveValue(ref valueIndexRef, span);
        page.DecreaseRef();
        return true;
    }

    public bool Remove(int index)
    {
        ref var page = ref GetPage(index, out int entryIndex);
        if (entryIndex == -1) {
            return false;
        }

        ref int valueIndexRef = ref page.Memory!.Span[entryIndex];
        var span = CollectionsMarshal.AsSpan(_dense);

        RemoveValue(ref valueIndexRef, span);
        page.DecreaseRef();
        return true;
    }

    public bool Remove(KeyValuePair<int, T> item)
    {
        ref var page = ref GetPage(item.Key, out int entryIndex);
        if (entryIndex == -1) {
            return false;
        }

        ref int valueIndexRef = ref page.Memory!.Span[entryIndex];
        var span = CollectionsMarshal.AsSpan(_dense);

        if (!span[valueIndexRef]!.Equals(item.Value)) {
            return false;
        }

        RemoveValue(ref valueIndexRef, span);
        page.DecreaseRef();
        return true;
    }

    public void Clear()
    {
        if (_dense.Count == 0) {
            return;
        }

        _dense.Clear();
        _reverse.Clear();

        foreach (ref var page in _pages.AsSpan()) {
            var memory = page.Memory;
            if (memory != null) {
                memory.Dispose();
                page = default;
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref T GetValueRefOrNullRef(int index)
    {
        ref var page = ref GetPage(index, out int entryIndex);
        if (entryIndex == -1) {
            return ref Unsafe.NullRef<T>();
        }
        var span = CollectionsMarshal.AsSpan(_dense);
        return ref span[page[entryIndex]];
    }

    public ref T GetOrAddValueRef(int index, out bool exists)
    {
        if (index < 0 || index >= Capacity) {
            throw new IndexOutOfRangeException();
        }

        int pageIndex = index / PageSize;
        int entryIndex = index - pageIndex * PageSize;

        ref var page = ref _pages[pageIndex];
        ref var memory = ref page.Memory;

        if (memory == null) {
            memory = MemoryOwner<int>.Allocate(PageSize);
            page.Count = 1;

            var span = memory.Span;
            span.Fill(int.MaxValue);

            exists = false;
            return ref AddDefaultValue(ref span[entryIndex], index);
        }

        ref int valueIndexRef = ref memory.Span[entryIndex];
        if (valueIndexRef == int.MaxValue) {
            page.Count++;
            exists = false;
            return ref AddDefaultValue(ref valueIndexRef, index);
        }
        else {
            exists = true;
            var span = CollectionsMarshal.AsSpan(_dense);
            return ref span[valueIndexRef];
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private ref T AddDefaultValue(ref int valueIndexRef, int index)
    {
        valueIndexRef = _dense.Count;

        _dense.Add(default!);
        _reverse.Add(index);

        var span = CollectionsMarshal.AsSpan(_dense);
        return ref span[valueIndexRef];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void RemoveValue(ref int valueIndexRef, Span<T> denseSpan)
    {
        int lastValueIndex = denseSpan.Length - 1;
        if (valueIndexRef != lastValueIndex) {
            denseSpan[valueIndexRef] = denseSpan[lastValueIndex];
            int lastIndex = _reverse[lastValueIndex];
            var page = UnsafeGetPage(lastIndex, out int entryIndex);
            page[entryIndex] = valueIndexRef;
            _reverse[valueIndexRef] = _reverse[lastValueIndex];
        }
        valueIndexRef = int.MaxValue;
        _dense.RemoveAt(lastValueIndex);
        _reverse.RemoveAt(lastValueIndex);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private ref Page GetPage(int index, out int entryIndex)
    {
        if (index < 0 || index >= Capacity) {
            entryIndex = -1;
            return ref Unsafe.NullRef<Page>();
        }

        int pageIndex = index / PageSize;

        ref var page = ref _pages[pageIndex];
        var memory = page.Memory;

        if (memory == null) {
            entryIndex = -1;
            return ref Unsafe.NullRef<Page>();
        }

        entryIndex = index - pageIndex * PageSize;
        ref int valueIndex = ref memory.Span[entryIndex];

        if (valueIndex >= Count) {
            entryIndex = -1;
            return ref Unsafe.NullRef<Page>();
        }

        return ref page;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private ref Page UnsafeGetPage(int index, out int pageEntryIndex)
    {
        int pageIndex = index / PageSize;
        pageEntryIndex = index - pageIndex * PageSize;
        return ref _pages[pageIndex];
    }
}