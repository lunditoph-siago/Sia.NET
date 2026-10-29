namespace Sia;

using System.Buffers;
using System.Collections;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Diagnostics.CodeAnalysis;

using CommunityToolkit.HighPerformance;

public sealed class SparseSet<T>(int pageSize = 256)
    : IDictionary<int, T>, IReadOnlyDictionary<int, T>
{
    public int PageSize { get; init; } = pageSize;
    public int Count => _dense.Count;

    public T this[int index] {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get {
            ref var value = ref GetValueRefOrNullRef(index);
            if (Unsafe.IsNullRef(ref value)) {
                throw new KeyNotFoundException("Index not found");
            }
            return value;
        }
        set => GetOrAddValueRef(index, out var _) = value;
    }

    public IEnumerable<int> Keys => _reverse;
    public IEnumerable<T> Values => _dense;

    public ReadOnlySpan<int> KeySpan => _reverse.AsSpan();
    public Span<T> ValueSpan => _dense.AsSpan();

    public List<int> UnsafeRawKeys => _reverse;
    public List<T> UnsafeRawValues => _dense;

    ICollection<int> IDictionary<int, T>.Keys => _reverse.AsReadOnly();
    ICollection<T> IDictionary<int, T>.Values => _dense.AsReadOnly();

    public bool IsReadOnly => false;

    private readonly List<T> _dense = [];
    private readonly List<int> _reverse = [];
    private Page[] _pages = [];

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private (int Page, int Offset) GetPosition(int index)
    {
        var size = PageSize;
        return size == 256 && index >= 0
            ? (index >> 8, index & 255)
            : Math.DivRem(index, size);
    }

    private struct Page
    {
        public int[]? Indices;
        public int Count;
    }

    public IEnumerator<KeyValuePair<int, T>> GetEnumerator()
    {
        for (var i = 0; i < _dense.Count; ++i) {
            yield return KeyValuePair.Create(_reverse[i], _dense[i]);
        }
    }

    IEnumerator IEnumerable.GetEnumerator()
        => GetEnumerator();

    public void CopyTo(KeyValuePair<int, T>[] array, int arrayIndex)
    {
        var count = _dense.Count;
        for (var i = 0; i < count; ++i) {
            array[i + arrayIndex] = KeyValuePair.Create(_reverse[i], _dense[i]);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Add(int index, T value)
    {
        ref var valueRef = ref GetOrAddValueRef(index, out var exists);
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
        => !Unsafe.IsNullRef(ref GetPage(index, out _));

    public bool Contains(KeyValuePair<int, T> item)
    {
        ref var value = ref GetValueRefOrNullRef(item.Key);
        return !Unsafe.IsNullRef(ref value) && EqualityComparer<T>.Default.Equals(value, item.Value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGetValue(int index, [MaybeNullWhen(false)] out T value)
    {
        ref var found = ref GetValueRefOrNullRef(index);
        if (Unsafe.IsNullRef(ref found)) {
            value = default;
            return false;
        }
        value = found;
        return true;
    }

    public bool Remove(int index, [MaybeNullWhen(false)] out T value)
    {
        ref var page = ref GetPage(index, out var entryIndex);
        if (entryIndex == -1) {
            value = default;
            return false;
        }
        ref var slot = ref page.Indices![entryIndex];
        value = _dense[slot - 1];
        RemoveValue(ref slot);
        if (--page.Count == 0) ReleasePage(ref page);
        return true;
    }

    public bool Remove(int index)
    {
        ref var page = ref GetPage(index, out var entryIndex);
        if (entryIndex == -1) return false;
        ref var slot = ref page.Indices![entryIndex];
        RemoveValue(ref slot);
        if (--page.Count == 0) ReleasePage(ref page);
        return true;
    }

    public bool Remove(KeyValuePair<int, T> item)
    {
        ref var page = ref GetPage(item.Key, out var entryIndex);
        if (entryIndex == -1) return false;
        ref var slot = ref page.Indices![entryIndex];
        if (!EqualityComparer<T>.Default.Equals(_dense[slot - 1], item.Value)) return false;
        RemoveValue(ref slot);
        if (--page.Count == 0) ReleasePage(ref page);
        return true;
    }

    public void Clear()
    {
        if (_dense.Count == 0) return;
        _dense.Clear();
        _reverse.Clear();
        foreach (ref var page in _pages.AsSpan()) {
            if (page.Indices != null) ReleasePage(ref page);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref T GetValueRefOrNullRef(int index)
    {
        var (pageIndex, entryIndex) = GetPosition(index);
        var pages = _pages;
        if (pageIndex >= pages.Length) return ref Unsafe.NullRef<T>();
        var indices = pages[pageIndex].Indices;
        if (indices == null) return ref Unsafe.NullRef<T>();
        var denseIndex = indices[entryIndex] - 1;
        var dense = CollectionsMarshal.AsSpan(_dense);
        if ((uint)denseIndex >= (uint)dense.Length) return ref Unsafe.NullRef<T>();
        return ref dense[denseIndex];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref T GetOrAddValueRef(int index, out bool exists)
    {
        var size = PageSize;
        var (pageIndex, entryIndex) = GetPosition(index);
        if (pageIndex >= _pages.Length) {
            var capacity = checked((int)Math.Max((long)pageIndex + 1, Math.Max(4L, (long)_pages.Length * 2)));
            Array.Resize(ref _pages, capacity);
        }
        ref var page = ref _pages[pageIndex];
        if (entryIndex < 0) throw new IndexOutOfRangeException();
        var indices = page.Indices;
        if (indices == null) {
            indices = ArrayPool<int>.Shared.Rent(size);
            indices.AsSpan(0, size).Clear();
            page.Indices = indices;
        }
        ref var slot = ref indices[entryIndex];
        if (slot != 0) {
            exists = true;
            return ref CollectionsMarshal.AsSpan(_dense)[slot - 1];
        }
        exists = false;
        page.Count++;
        slot = _dense.Count + 1;
        _dense.Add(default!);
        _reverse.Add(index);
        return ref CollectionsMarshal.AsSpan(_dense)[slot - 1];
    }

    private void RemoveValue(ref int slot)
    {
        var valueIndex = slot - 1;
        var lastValueIndex = _dense.Count - 1;
        if (valueIndex != lastValueIndex) {
            _dense[valueIndex] = _dense[lastValueIndex];
            var lastIndex = _reverse[lastValueIndex];
            var (pageIndex, entryIndex) = GetPosition(lastIndex);
            _pages[pageIndex].Indices![entryIndex] = slot;
            _reverse[valueIndex] = lastIndex;
        }
        slot = 0;
        _dense.RemoveAt(lastValueIndex);
        _reverse.RemoveAt(lastValueIndex);
    }

    private static void ReleasePage(ref Page page)
    {
        var indices = page.Indices!;
        page = default;
        ArrayPool<int>.Shared.Return(indices);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private ref Page GetPage(int index, out int entryIndex)
    {
        var (pageIndex, offset) = GetPosition(index);
        var pages = _pages;
        if (pageIndex >= pages.Length) {
            entryIndex = -1;
            return ref Unsafe.NullRef<Page>();
        }
        ref var page = ref pages[pageIndex];
        var indices = page.Indices;
        if (indices == null) {
            entryIndex = -1;
            return ref Unsafe.NullRef<Page>();
        }
        entryIndex = offset;
        if ((uint)(indices[entryIndex] - 1) >= (uint)_dense.Count) {
            entryIndex = -1;
            return ref Unsafe.NullRef<Page>();
        }
        return ref page;
    }
}
