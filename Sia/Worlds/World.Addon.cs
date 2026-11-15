using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Sia;

public partial class World
{
    public event Action<IAddon>? OnAddonCreated;
    public event Action<IAddon>? OnAddonRemoved;

    public IEnumerable<IAddon> Addons {
        get {
            for (int i = 0, addonAcc = 0; addonAcc < _addonCount; ++i) {
                if (_addons[i] is { } addon) {
                    addonAcc++;
                    yield return addon;
                }
            }
        }
    }

    private readonly IAddon?[] _addons = new IAddon?[2048];
    private readonly HashSet<int> _reservedAddonSlots = [];
    private bool _clearingAddons;
    private int _addonCount = 0;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public TAddon AcquireAddon<TAddon>()
        where TAddon : class, IAddon, new()
    {
        var index = WorldAddonIndexer<TAddon>.Index;
        return _addons[index] is { } addon
            ? Unsafe.As<TAddon>(addon)
            : RegisterAddon<TAddon>(index, null, static () => new TAddon());
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public TAddon AddAddon<TAddon>()
        where TAddon : class, IAddon, new()
        => RegisterAddon<TAddon>(WorldAddonIndexer<TAddon>.Index, null, static () => new TAddon());

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public TAddon AddAddon<TAddon>(TAddon addon)
        where TAddon : class, IAddon
    {
        ArgumentNullException.ThrowIfNull(addon);
        return RegisterAddon(WorldAddonIndexer<TAddon>.Index, addon, null);
    }

    private TAddon RegisterAddon<TAddon>(int index, TAddon? addon, Func<TAddon>? create)
        where TAddon : class, IAddon
    {
        EnsureOwnerThread();
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        if (_clearingAddons) {
            throw new InvalidOperationException("Cannot register an addon while the world is clearing addons.");
        }
        if (_addons[index] != null) {
            throw new Exception("Addon already exists: " + typeof(TAddon));
        }
        if (!_reservedAddonSlots.Add(index)) {
            throw new InvalidOperationException(
                "An addon of the same type is already being initialized or removed: " + typeof(TAddon));
        }

        try {
            addon ??= create!();
            addon.OnInitialize(this);
            _addons[index] = addon;
            _addonCount++;
        }
        finally {
            _reservedAddonSlots.Remove(index);
        }
        OnAddonCreated?.Invoke(addon);
        return addon;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool RemoveAddon<TAddon>()
        where TAddon : class, IAddon
    {
        EnsureOwnerThread();
        var index = WorldAddonIndexer<TAddon>.Index;
        var addon = _addons[index];
        if (addon is null) return false;

        _reservedAddonSlots.Add(index);
        _addons[index] = null;
        _addonCount--;
        try {
            var result = Outcome<Exception>.Success.Attempt(() => addon.OnUninitialize(this));
            NotifyObservers(OnAddonRemoved, addon, result).ThrowIfFailed();
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

        for (int i = 0, addonAcc = 0; addonAcc < _addonCount; ++i) {
            if (_addons[i] is { } addon) {
                if (addon is TAddon converted) {
                    return converted;
                }
                addonAcc++;
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

        for (int i = 0, addonAcc = 0; addonAcc < _addonCount; ++i) {
            if (_addons[i] is { } addon) {
                addonAcc++;
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

        for (int i = 0, addonAcc = 0; addonAcc < _addonCount; ++i) {
            if (_addons[i] is { } candidate) {
                if (candidate is TAddon converted) {
                    addon = converted;
                    return true;
                }
                addonAcc++;
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
        var addons = new List<IAddon>(_addonCount);
        for (var i = 0; addons.Count < _addonCount; ++i) {
            if (_addons[i] is { } addon) {
                addons.Add(addon);
                _addons[i] = null;
            }
        }
        _addonCount = 0;

        _clearingAddons = true;
        try {
            var result = Outcome<Exception>.Success;
            foreach (var addon in addons) {
                result = result.Attempt(() => addon.OnUninitialize(this));
                result = NotifyObservers(OnAddonRemoved, addon, result);
            }
            result.ThrowIfFailed();
        }
        finally {
            _clearingAddons = false;
        }
    }
}
