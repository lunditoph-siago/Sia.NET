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
    private readonly HashSet<int> _addonOperations = [];
    private bool _clearingAddons;
    private int _addonCount = 0;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public TAddon AcquireAddon<TAddon>()
        where TAddon : class, IAddon, new()
    {
        ref var addon = ref _addons[WorldAddonIndexer<TAddon>.Index];
        if (addon != null) {
            return Unsafe.As<TAddon>(addon);
        }
        return AddAddon(static () => new TAddon());
    }

    public TAddon AddAddon<TAddon>(Func<TAddon> create)
        where TAddon : class, IAddon
    {
        EnsureOwnerThread();
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        var index = WorldAddonIndexer<TAddon>.Index;
        if (_addons[index] != null) {
            throw new Exception("Addon already exists: " + typeof(TAddon));
        }
        if (_clearingAddons || !_addonOperations.Add(index)) {
            throw new InvalidOperationException("Cannot register an addon during its lifecycle operation or while clearing addons.");
        }
        TAddon addon;
        try {
            addon = create();
            addon.OnInitialize(this);
            _addons[index] = addon;
            _addonCount++;
        }
        finally {
            _addonOperations.Remove(index);
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
        if (_addons[index] is not { } addon) return false;
        if (addon is Scheduler { DisposalErrorMessage: { } message }) {
            throw new InvalidOperationException(message);
        }
        _addonOperations.Add(index);
        _addons[index] = null;
        _addonCount--;
        try {
            var result = Outcome<Exception>.Success.Attempt((addon, world: this),
                static (in item) => item.addon.OnUninitialize(item.world));
            NotifyObservers(OnAddonRemoved, addon, result).ThrowIfFailed();
            return true;
        }
        finally {
            _addonOperations.Remove(index);
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
        if (!IsDisposed) {
            EnsureOwnerThread();
        }
        if (_clearingAddons || _addonOperations.Count != 0) {
            throw new InvalidOperationException("Cannot clear addons during addon initialization or removal.");
        }
        if (_addonCount == 0) return;

        var addons = new IAddon[_addonCount];
        var i = 0;
        for (var addonIndex = 0; addonIndex < addons.Length; ++i) {
            if (_addons[i] is { } addon) {
                if (addon is Scheduler { DisposalErrorMessage: { } message }) {
                    throw new InvalidOperationException(message);
                }
                addons[addonIndex++] = addon;
            }
        }
        Array.Clear(_addons, 0, i);
        _addonCount = 0;

        _clearingAddons = true;
        try {
            var result = Outcome<Exception>.Success;
            foreach (var addon in addons) {
                result = result.Attempt((addon, world: this),
                    static (in item) => item.addon.OnUninitialize(item.world));
                result = NotifyObservers(OnAddonRemoved, addon, result);
            }
            result.ThrowIfFailed();
        }
        finally {
            _clearingAddons = false;
        }
    }
}
