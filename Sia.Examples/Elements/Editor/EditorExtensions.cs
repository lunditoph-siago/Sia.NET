namespace Sia_Examples.Editor;

public sealed class EditorExtensions
{
    public static readonly EditorExtensions Empty = new(Configuration.Empty, [], []);

    internal readonly Configuration Config;
    private readonly object?[] _values;
    private readonly SlotStatus[] _status;

    private EditorExtensions(Configuration config, object?[] values, SlotStatus[] status)
    { Config = config; _values = values; _status = status; }

    internal static EditorExtensions Create(IReadOnlyList<IExtension> extensions, EditorState state)
    {
        var config = Configuration.Resolve(extensions);
        var values = new object?[config.Slots.Length];
        var status = new SlotStatus[config.Slots.Length];
        var ext = new EditorExtensions(config, values, status);
        for (var addr = 0; addr < config.Slots.Length; addr++) ext.EnsureAddr(addr, slot => slot.Create(state));
        return ext;
    }

    internal EditorExtensions Update(Transaction tr)
    {
        if (Config.Slots.Length == 0) return this;
        var values = new object?[Config.Slots.Length];
        var status = new SlotStatus[Config.Slots.Length];
        var next = new EditorExtensions(Config, values, status);
        for (var addr = 0; addr < Config.Slots.Length; addr++) {
            var oldValue = _values[addr];
            next.EnsureAddr(addr, slot => slot.Update(oldValue, tr));
        }
        return next;
    }

    private void EnsureAddr(int addr, Func<Slot, object?> compute)
    {
        if (_status[addr] == SlotStatus.Computed) return;
        if (_status[addr] == SlotStatus.Computing)
            throw new InvalidOperationException("Cyclic dependency between fields and/or facets");
        _status[addr] = SlotStatus.Computing;
        _values[addr] = compute(Config.Slots[addr]);
        _status[addr] = SlotStatus.Computed;
    }

    public T Field<T>(StateField<T> field)
    {
        var addr = Config.AddressOf(field.Id);
        return addr >= 0 && _values[addr] is T t ? t : default!;
    }

    public TOutput Facet<TInput, TOutput>(Facet<TInput, TOutput> facet)
    {
        var addr = Config.AddressOf(facet.Id);
        return addr >= 0 && _values[addr] is TOutput t ? t : facet.DefaultValue;
    }
}
