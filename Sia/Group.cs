namespace Sia;

using System.Collections;
using System.Runtime.InteropServices;

public class Group<T> : IReadOnlyList<T>
    where T : notnull 
{
    public int Count => _values.Count;
    public T this[int index] => _values[index];

    private readonly Dictionary<T, int> _indices = new();
    private readonly List<T> _values = new();

    public Group() {}

    public Group(IEnumerable<T> values)
    {
        int i = 0;
        foreach (var value in values) {
            _indices.Add(value, i);
            _values.Add(value);
            ++i;
        }
    }

    public Span<T> AsSpan()
        => CollectionsMarshal.AsSpan(_values);
    
    public Span<T>.Enumerator GetEnumerator()
        => CollectionsMarshal.AsSpan(_values).GetEnumerator();

    IEnumerator<T> IEnumerable<T>.GetEnumerator()
        => _values.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator()
        => ((IEnumerable)_values).GetEnumerator();

    public virtual bool Add(in T value)
    {
        if (!_indices.TryAdd(value, _values.Count)) {
            return false;
        }
        _values.Add(value);
        return true;
    }

    public virtual bool Remove(in T value)
    {
        if (!_indices.Remove(value, out int index)) {
            return false;
        }
        int lastIndex = _values.Count - 1;
        if (index != lastIndex) {
            _values[index] = _values[lastIndex];
        }
        _values.RemoveAt(lastIndex);
        return true;
    }

    public bool Contains(in T value)
        => _values.Contains(value);

    public virtual void Clear()
    {
        _indices.Clear();
        _values.Clear();
    }
}

public class Group : Group<EntityRef>
{
}