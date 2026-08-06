namespace Sia_Examples.Editor;

internal interface IStateFieldSlot
{
    public string Id { get; }
    public object? CreateBoxed(EditorState state);
    public object? UpdateBoxed(object? value, Transaction tr);
}

public sealed class StateField<TValue> : IExtension, IStateFieldSlot
{
    public string Id { get; }
    internal Func<EditorState, TValue> CreateFn { get; }
    internal Func<TValue, Transaction, TValue> UpdateFn { get; }
    internal Func<TValue, TValue, bool> CompareFn { get; }

    internal StateField(string id,
        Func<EditorState, TValue> create,
        Func<TValue, Transaction, TValue> update,
        Func<TValue, TValue, bool>? compare = null)
    { Id = id; CreateFn = create; UpdateFn = update; CompareFn = compare ?? EqualityComparer<TValue>.Default.Equals; }

    public static StateField<TValue> Define(
        Func<EditorState, TValue> create,
        Func<TValue, Transaction, TValue> update,
        Func<TValue, TValue, bool>? compare = null)
    {
        var id = $"field_{Interlocked.Increment(ref FacetIdGen.Value)}";
        return new StateField<TValue>(id, create, update, compare);
    }

    object? IStateFieldSlot.CreateBoxed(EditorState state) => CreateFn(state);
    object? IStateFieldSlot.UpdateBoxed(object? value, Transaction tr) => UpdateFn(value is TValue v ? v : default!, tr);
}
