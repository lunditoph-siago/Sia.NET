using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Sia;

public partial class World
{
    public event Action<IAddon>? OnAddonCreated;
    public event Action<IAddon>? OnAddonRemoved;

    public IEnumerable<IAddon> Addons {
        get {
            for (int i = 0, visitedAddons = 0; visitedAddons < _addonCount; ++i) {
                if (_addons[i] is { } addon) {
                    visitedAddons++;
                    yield return addon;
                }
            }
        }
    }

    private readonly IAddon?[] _addons = new IAddon?[2048];
    private readonly HashSet<int> _reservedAddonSlots = [];
    private bool _clearingAddons;
    private int _addonCount;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public TAddon AcquireAddon<TAddon>()
        where TAddon : class, IAddon, new()
    {
        EnsureAddonRegistration();
        var index = WorldAddonIndexer<TAddon>.Index;
        if (_addons[index] is { } addon) {
            return Unsafe.As<TAddon>(addon);
        }

        return CreateAddon(index, static () => new TAddon());
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public TAddon AddAddon<TAddon>()
        where TAddon : class, IAddon, new()
    {
        EnsureAddonRegistration();
        return CreateAddon(WorldAddonIndexer<TAddon>.Index, static () => new TAddon());
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public TAddon AddAddon<TAddon>(TAddon addon)
        where TAddon : class, IAddon
    {
        EnsureAddonRegistration();
        ArgumentNullException.ThrowIfNull(addon);
        var index = WorldAddonIndexer<TAddon>.Index;
        ReserveAddonSlot<TAddon>(index);
        return InitializeReservedAddon(index, addon);
    }

    private void ReserveAddonSlot<TAddon>(int index)
    {
        if (_addons[index] != null) {
            throw new Exception("Addon already exists: " + typeof(TAddon));
        }
        if (!_reservedAddonSlots.Add(index)) {
            throw new InvalidOperationException(
                "An addon of the same type is already being initialized or removed: " + typeof(TAddon));
        }
    }

    private TAddon CreateAddon<TAddon>(int index, Func<TAddon> create)
        where TAddon : class, IAddon
    {
        ReserveAddonSlot<TAddon>(index);

        TAddon addon;
        try {
            addon = create();
        }
        catch {
            _reservedAddonSlots.Remove(index);
            throw;
        }

        return InitializeReservedAddon(index, addon);
    }

    private TAddon InitializeReservedAddon<TAddon>(int index, TAddon addon)
        where TAddon : class, IAddon
    {
        try {
            addon.OnInitialize(this);
            _addons[index] = addon;
            _addonCount++;
        }
        finally {
            _reservedAddonSlots.Remove(index);
        }
        // Observers see a published addon and may remove it.
        NotifyObservers(OnAddonCreated, addon, Outcome<Exception>.Success).ThrowIfFailed();
        return addon;
    }

    private void EnsureAddonRegistration()
    {
        EnsureOwnerThread();
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        if (_clearingAddons) {
            throw new InvalidOperationException("Cannot register an addon while the world is clearing addons.");
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool RemoveAddon<TAddon>()
        where TAddon : class, IAddon
    {
        EnsureOwnerThread();
        var index = WorldAddonIndexer<TAddon>.Index;
        var addon = _addons[index];
        if (addon is null) {
            return false;
        }

        _reservedAddonSlots.Add(index);
        _addons[index] = null;
        _addonCount--;
        try {
            UninitializeAddon(addon, Outcome<Exception>.Success).ThrowIfFailed();
            return true;
        }
        finally {
            _reservedAddonSlots.Remove(index);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public TAddon GetAddon<TAddon>()
        where TAddon : class, IAddon
    {
        if (_addons[WorldAddonIndexer<TAddon>.Index] is { } exact) {
            return Unsafe.As<TAddon>(exact);
        }

        for (int i = 0, visitedAddons = 0; visitedAddons < _addonCount; ++i) {
            if (_addons[i] is { } addon) {
                if (addon is TAddon converted) {
                    return converted;
                }
                visitedAddons++;
            }
        }

        throw new KeyNotFoundException("Addon not found: " + typeof(TAddon));
    }

    public IEnumerable<TAddon> GetAddons<TAddon>()
        where TAddon : class, IAddon
    {
        var exactIndex = WorldAddonIndexer<TAddon>.Index;
        if (_addons[exactIndex] is { } exact) {
            yield return Unsafe.As<TAddon>(exact);
        }

        for (int i = 0, visitedAddons = 0; visitedAddons < _addonCount; ++i) {
            if (_addons[i] is { } addon) {
                visitedAddons++;
                if (i != exactIndex && addon is TAddon converted) {
                    yield return converted;
                }
            }
        }
    }

    public bool TryGetAddon<TAddon>([MaybeNullWhen(false)] out TAddon addon)
        where TAddon : class, IAddon
    {
        if (_addons[WorldAddonIndexer<TAddon>.Index] is { } exact) {
            addon = Unsafe.As<TAddon>(exact);
            return true;
        }

        for (int i = 0, visitedAddons = 0; visitedAddons < _addonCount; ++i) {
            if (_addons[i] is { } candidate) {
                if (candidate is TAddon converted) {
                    addon = converted;
                    return true;
                }
                visitedAddons++;
            }
        }

        addon = default;
        return false;
    }

    public void ClearAddons()
    {
        EnsureOwnerThread();
        EnsureCanClearAddons();
        ClearAddonsCore();
    }

    private void EnsureCanClearAddons()
    {
        if (_clearingAddons || _reservedAddonSlots.Count != 0) {
            throw new InvalidOperationException("Cannot clear addons during addon initialization or cleanup.");
        }
    }

    private void ClearAddonsCore()
    {
        var detachedAddons = new List<IAddon>(_addonCount);
        for (var i = 0; detachedAddons.Count < _addonCount; ++i) {
            if (_addons[i] is { } addon) {
                detachedAddons.Add(addon);
                _addons[i] = null;
            }
        }
        _addonCount = 0;

        _clearingAddons = true;
        try {
            var result = Outcome<Exception>.Success;
            foreach (var addon in detachedAddons) {
                result = UninitializeAddon(addon, result);
            }
            result.ThrowIfFailed();
        }
        finally {
            _clearingAddons = false;
        }
    }

    private Outcome<Exception> UninitializeAddon(IAddon addon, Outcome<Exception> result)
    {
        result = result.Attempt(() => addon.OnUninitialize(this));
        return NotifyObservers(OnAddonRemoved, addon, result);
    }
}
