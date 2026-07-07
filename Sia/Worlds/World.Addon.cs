namespace Sia;

using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

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
    private int _addonCount = 0;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public TAddon AcquireAddon<TAddon>()
        where TAddon : class, IAddon, new()
    {
        ref var addon = ref _addons[WorldAddonIndexer<TAddon>.Index];
        if (addon != null) {
            return Unsafe.As<TAddon>(addon);
        }
        var newAddon = CreateAddon<TAddon>();
        addon = newAddon;
        OnAddonCreated?.Invoke(newAddon);
        return newAddon;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public TAddon AddAddon<TAddon>()
        where TAddon : class, IAddon, new()
    {
        ref var addon = ref _addons[WorldAddonIndexer<TAddon>.Index];
        if (addon != null) {
            throw new Exception("Addon already exists: " + typeof(TAddon));
        }
        var newAddon = CreateAddon<TAddon>();
        addon = newAddon;
        OnAddonCreated?.Invoke(newAddon);
        return newAddon;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private TAddon CreateAddon<TAddon>()
        where TAddon : class, IAddon, new()
    {
        var addon = new TAddon();
        addon.OnInitialize(this);
        _addonCount++;
        return addon;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool RemoveAddon<TAddon>()
        where TAddon : class, IAddon
    {
        ref var addon = ref _addons[WorldAddonIndexer<TAddon>.Index];
        if (addon == null) {
            return false;
        }
        var removedAddon = addon;
        addon.OnUninitialize(this);
        addon = null;
        _addonCount--;
        OnAddonRemoved?.Invoke(removedAddon);
        return true;
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
        for (var i = 0; _addonCount != 0; ++i) {
            if (_addons[i] is { } addon) {
                addon.OnUninitialize(this);
                _addons[i] = null;
                _addonCount--;
                OnAddonRemoved?.Invoke(addon);
            }
        }
    }
}
