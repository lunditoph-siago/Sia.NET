namespace Sia_Examples.Editor;

internal enum SlotStatus { Unresolved, Computing, Computed }

internal abstract class Slot
{
    public abstract object? Create(EditorState state);
    public abstract object? Update(object? oldValue, Transaction tr);
}

internal sealed class FieldSlot(IStateFieldSlot field) : Slot
{
    public override object? Create(EditorState state) => field.CreateBoxed(state);
    public override object? Update(object? oldValue, Transaction tr) => field.UpdateBoxed(oldValue, tr);
}

internal sealed class FacetSlot(IFacet facet, IReadOnlyList<IFacetProviderSlot> providers) : Slot
{
    public override object? Create(EditorState state) => Combine();
    public override object? Update(object? oldValue, Transaction tr) => Combine();

    private object? Combine()
        => providers.Count == 0 ? facet.DefaultBoxed : facet.CombineBoxed([.. providers.Select(p => p.BoxedValue)]);
}

public sealed class Configuration
{
    internal readonly Slot[] Slots;
    private readonly Dictionary<string, int> _address;

    public static readonly Configuration Empty = new([], []);

    private Configuration(Slot[] slots, Dictionary<string, int> address)
    { Slots = slots; _address = address; }

    internal int AddressOf(string id) => _address.TryGetValue(id, out var addr) ? addr : -1;

    internal static Configuration Resolve(IReadOnlyList<IExtension>? extensions)
    {
        if (extensions is null || extensions.Count == 0) return Empty;

        var slots = new List<Slot>();
        var address = new Dictionary<string, int>();
        var facetProviders = new Dictionary<string, (IFacet Facet, List<IFacetProviderSlot> Providers)>();
        var facetOrder = new List<string>();

        foreach (var ext in extensions) {
            switch (ext) {
                case IStateFieldSlot field:
                    address[field.Id] = slots.Count;
                    slots.Add(new FieldSlot(field));
                    break;
                case IFacetProviderSlot provider:
                    if (!facetProviders.TryGetValue(provider.FacetId, out var entry)) {
                        entry = (provider.FacetOwner, []);
                        facetProviders[provider.FacetId] = entry;
                        facetOrder.Add(provider.FacetId);
                    }
                    entry.Providers.Add(provider);
                    break;
            }
        }
        foreach (var facetId in facetOrder) {
            var (facet, providers) = facetProviders[facetId];
            address[facetId] = slots.Count;
            slots.Add(new FacetSlot(facet, providers));
        }

        return new Configuration([.. slots], address);
    }
}
