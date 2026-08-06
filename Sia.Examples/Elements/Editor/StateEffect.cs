namespace Sia_Examples.Editor;

public sealed class Annotation<T>
{
    public AnnotationType<T> Type { get; }
    public T Value { get; }
    internal Annotation(AnnotationType<T> type, T value) { Type = type; Value = value; }
}

public sealed class AnnotationType<T>
{
    public Annotation<T> Of(T value) => new(this, value);
    public static AnnotationType<T> Define() => new();
}

public sealed class StateEffectType<T>
{
    internal readonly Func<object, ChangeDesc, object?> MapFn;
    internal StateEffectType(Func<object, ChangeDesc, object?> mapFn) { MapFn = mapFn; }
    public StateEffect<T> Of(T value) => new(this, value);
    public static StateEffectType<T> Define(Func<T, ChangeDesc, T?>? map = null)
        => new(map != null ? (v, d) => map((T)v, d)! : (v, _) => v);
}

public sealed class StateEffect<T>
{
    public StateEffectType<T> Type { get; }
    public T Value { get; }
    internal StateEffect(StateEffectType<T> type, T value) { Type = type; Value = value; }
    public StateEffect<T>? Map(ChangeDesc m)
    {
        var mapped = Type.MapFn(Value!, m);
        return mapped == null ? null : Equals(mapped, Value) ? this : new StateEffect<T>(Type, (T)mapped!);
    }
}

public static class StateEffects
{
    public static StateEffect<T>[] MapAll<T>(StateEffect<T>[] effects, ChangeDesc m)
    {
        if (effects.Length == 0) return effects;
        var r = new List<StateEffect<T>>();
        foreach (var e in effects) { var x = e.Map(m); if (x != null) r.Add(x); }
        return [.. r];
    }

    public static T? FindLast<T>(object[] effects, StateEffectType<T> type)
    {
        for (var i = effects.Length - 1; i >= 0; i--) {
            if (effects[i] is StateEffect<T> e && e.Type == type) return e.Value;
        }
        return default;
    }
}
