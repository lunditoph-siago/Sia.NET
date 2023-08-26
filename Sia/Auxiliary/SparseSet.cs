namespace Sia;

using System.Collections;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Diagnostics.CodeAnalysis;

using CommunityToolkit.HighPerformance.Buffers;

public sealed class SparseSet<T> : IDictionary<int, T>, IReadOnlyDictionary<int, T>
{
    public int PageCount { get; init; }
    public int PageSize { get; init; }
    public int Capacity { get; init; }
    public int Count => _dense.Count;

    public T this[int index] {
        get {
            ref var valueRef = ref GetValueRefOrNullRef(index);
            if (Unsafe.IsNullRef(ref valueRef)) {
                throw new KeyNotFoundException("Index not found");
            }
            return valueRef;
        }
        set {
            GetOrAddValueRef(index, out bool _) = value;
        }
    }

    public IEnumerable<int> Keys => _reverse;
    public IEnumerable<T> Values => _dense;

    ICollection<int> IDictionary<int, T>.Keys => _reverse.AsReadOnly();
    ICollection<T> IDictionary<int, T>.Values => _dense.AsReadOnly();

    public bool IsReadOnly => false;

    private readonly List<T> _dense = new();
    private readonly List<int> _reverse = new();
    private readonly Page[] _pages;

    private struct Page
    {
        public MemoryOwner<int>? Memory;
        public int Count;

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

    public SparseSet(int pageCount, int pageSize)
    {
        PageCount = pageCount;
        PageSize = pageSize;
        Capacity = pageCount * pageSize;

        _pages = new Page[pageCount];
    }

    public Span<int> AsKeySpan() => CollectionsMarshal.AsSpan(_reverse);
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

    public bool Add(int index, T value)
    {
        ref var valueRef = ref GetOrAddValueRef(index, out bool exists);
        if (exists) {
            return false;
        }
        valueRef = value;
        return true;
    }

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

    public bool ContainsKey(int index)
        => Unsafe.IsNullRef(ref GetPage(index, out int _));

    public bool Contains(KeyValuePair<int, T> item)
    {
        ref int valueIndexRef = ref GetValueIndexRef(item.Key);
        if (Unsafe.IsNullRef(ref valueIndexRef)) {
            return false;
        }
        return _dense[valueIndexRef]!.Equals(item.Value);
    }

    public bool TryGetValue(int index, [MaybeNullWhen(false)] out T value)
    {
        ref T valueRef = ref GetValueRefOrNullRef(index);
        if (Unsafe.IsNullRef(ref valueRef)) {
            value = default;
            return false;
        }
        value = valueRef;
        return true;
    }

    public bool Remove(int index, [MaybeNullWhen(false)] out T value)
    {
        ref var page = ref GetPage(index, out int entryIndex);
        if (Unsafe.IsNullRef(ref page)) {
            value = default;
            return false;
        }

        ref int valueIndexRef = ref page.Memory!.Span[entryIndex];
        var span = CollectionsMarshal.AsSpan(_dense);

        value = span[valueIndexRef];
        RemoveValue(ref valueIndexRef, span);
        page.DecreaseRef();
        return true;
    }

    public bool Remove(int index)
    {
        ref var page = ref GetPage(index, out int entryIndex);
        if (Unsafe.IsNullRef(ref page)) {
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
        if (Unsafe.IsNullRef(ref page)) {
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

    public ref T GetValueRefOrNullRef(int index)
    {
        ref int valueIndex = ref GetValueIndexRef(index);
        if (Unsafe.IsNullRef(ref valueIndex)) {
            return ref Unsafe.NullRef<T>();
        }
        var span = CollectionsMarshal.AsSpan(_dense);
        return ref span[valueIndex];
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
            UnsafeGetValueIndexRef(lastIndex) = valueIndexRef;
            _reverse[valueIndexRef] = _reverse[lastValueIndex];
        }
        valueIndexRef = int.MaxValue;
        _dense.RemoveAt(lastValueIndex);
        _reverse.RemoveAt(lastValueIndex);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private ref int GetValueIndexRef(int index)
    {
        ref var page = ref GetPage(index, out int entryIndex);
        if (Unsafe.IsNullRef(ref page)) {
            return ref Unsafe.NullRef<int>();
        }
        return ref page.Memory!.Span[entryIndex];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private ref int UnsafeGetValueIndexRef(int index)
    {
        ref var page = ref UnsafeGetPage(index, out int entryIndex);
        return ref page.Memory!.Span[entryIndex];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private ref Page GetPage(int index, out int pageEntryIndex)
    {
        if (index < 0 || index >= Capacity) {
            pageEntryIndex = 0;
            return ref Unsafe.NullRef<Page>();
        }

        int pageIndex = index / PageSize;
        int entryIndex = index - pageIndex * PageSize;

        ref var page = ref _pages[pageIndex];
        var memory = page.Memory;

        if (memory == null) {
            pageEntryIndex = 0;
            return ref Unsafe.NullRef<Page>();
        }

        ref int valueIndex = ref memory.Span[entryIndex];
        if (valueIndex >= Count) {
            pageEntryIndex = 0;
            return ref Unsafe.NullRef<Page>();
        }

        pageEntryIndex = entryIndex;
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