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
                throw new ArgumentException("Invalid index");
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
    private readonly MemoryOwner<int>[] _sparse;

    public SparseSet(int pageCount = 1024, int pageSize = 1024)
    {
        PageCount = pageCount;
        PageSize = pageSize;
        Capacity = pageCount * pageSize;

        _sparse = new MemoryOwner<int>[pageCount];
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
        => ((IEnumerable<KeyValuePair<int, T>>)this).GetEnumerator();

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
            throw new ArgumentException();
        }
    }

    void ICollection<KeyValuePair<int, T>>.Add(KeyValuePair<int, T> item)
    {
        if (!Add(item)) {
            throw new ArgumentException();
        }
    }

    public bool ContainsKey(int index)
        => Unsafe.IsNullRef(ref GetValueIndexRef(index));

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
        ref var valueIndexRef = ref GetValueIndexRef(index);
        if (Unsafe.IsNullRef(ref valueIndexRef)) {
            value = default;
            return false;
        }
        var span = CollectionsMarshal.AsSpan(_dense);
        value = span[valueIndexRef];
        RemoveValue(ref valueIndexRef, span);
        return true;
    }

    public bool Remove(int index)
    {
        ref var valueIndexRef = ref GetValueIndexRef(index);
        if (Unsafe.IsNullRef(ref valueIndexRef)) {
            return false;
        }
        var span = CollectionsMarshal.AsSpan(_dense);
        RemoveValue(ref valueIndexRef, span);
        return true;
    }

    public bool Remove(KeyValuePair<int, T> item)
    {
        ref var valueIndexRef = ref GetValueIndexRef(item.Key);
        if (Unsafe.IsNullRef(ref valueIndexRef)) {
            return false;
        }
        var span = CollectionsMarshal.AsSpan(_dense);
        if (!span[valueIndexRef]!.Equals(item.Key)) {
            return false;
        }
        RemoveValue(ref valueIndexRef, span);
        return true;
    }

    public void Clear()
    {
        if (_dense.Count == 0) {
            return;
        }

        _dense.Clear();
        _reverse.Clear();

        foreach (ref var page in _sparse.AsSpan()) {
            if (page != null) {
                page.Dispose();
                page = null;
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

        var sparsePage = _sparse[pageIndex];
        if (sparsePage == null) {
            sparsePage = MemoryOwner<int>.Allocate(PageSize);
            var pageSpan = sparsePage.Span;
            pageSpan.Fill(int.MaxValue);
            _sparse[pageIndex] = sparsePage;

            exists = false;
            return ref AddDefaultValue(ref pageSpan[entryIndex], index);
        }

        ref int valueIndexRef = ref sparsePage.Span[entryIndex];
        if (valueIndexRef == int.MaxValue) {
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

    private ref int GetValueIndexRef(int index)
    {
        if (index < 0 || index >= Capacity) {
            return ref Unsafe.NullRef<int>();
        }

        int pageIndex = index / PageSize;
        int entryIndex = index - pageIndex * PageSize;

        var sparsePage = _sparse[pageIndex];
        if (sparsePage == null) {
            return ref Unsafe.NullRef<int>();
        }

        ref int valueIndex = ref sparsePage.Span[entryIndex];
        if (valueIndex >= Count) {
            return ref Unsafe.NullRef<int>();
        }

        return ref valueIndex;
    }

    private ref int UnsafeGetValueIndexRef(int index)
    {
        int pageIndex = index / PageSize;
        int entryIndex = index - pageIndex * PageSize;
        return ref _sparse[pageIndex].Span[entryIndex];
    }

    public void CopyTo(KeyValuePair<int, T>[] array, int arrayIndex)
    {
        var count = _dense.Count;
        for (int i = 0; i < count; ++i) {
            array[i + arrayIndex] = KeyValuePair.Create(_reverse[i], _dense[i]);
        }
    }
}