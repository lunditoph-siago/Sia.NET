namespace Sia;

public static class CompactLayout
{
    /// <summary>Requests compact storage for a built-in HList when creating an entity.</summary>
    public static CompactLayout<TRow> From<TRow>(in TRow row)
        where TRow : struct, IHList
        => new(row);
}

/// <summary>
/// Requests compact storage when passed as a whole row to
/// <see cref="WorldCommonHostExtensions.Create{TEntity}(World, in TEntity)"/> or
/// <see cref="WorldCommonHostExtensions.CreateUnmanaged{TEntity}(World, in TEntity)"/>.
/// </summary>
/// <remarks>
/// The request copies the supplied built-in HList. Use the resulting compact HList
/// type when accessing the entity through a typed Host.
/// </remarks>
public struct CompactLayout<TRow> : IHList
    where TRow : struct, IHList
{
    private TRow _row;

    internal CompactLayout(in TRow row) => _row = row;

    void IHList.Handle<THandler>(in THandler handler)
        => _row.Handle(handler);

    void IHList.HandleHead<THandler>(in THandler handler)
        => _row.HandleHead(handler);

    void IHList.HandleHeadRef<THandler>(ref THandler handler)
        => _row.HandleHeadRef(ref handler);

    void IHList.HandleTail<THandler>(in THandler handler)
        => _row.HandleTail(handler);

    void IHList.HandleTailRef<THandler>(ref THandler handler)
        => _row.HandleTailRef(ref handler);

    void IHList.Filter<TPredicate, THandler>(in TPredicate predicate, in THandler handler)
        => _row.Filter(predicate, handler);

    void IHList.Concat<THList, THandler>(in THList list, in THandler handler)
        => _row.Concat(list, handler);

    bool IHList.Remove<TValue, THandler>(TypeProxy<TValue> proxy, in THandler handler)
        => _row.Remove(proxy, handler);

    bool IHList.Remove<TValue, THandler>(in TValue value, in THandler handler)
        => _row.Remove(value, handler);

    static void IHList.HandleTypes<THandler>(in THandler handler)
        => TRow.HandleTypes(handler);
}
