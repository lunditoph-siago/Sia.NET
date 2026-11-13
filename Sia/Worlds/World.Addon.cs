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
        EnsureAddonRegistration();
        var index = WorldAddonIndexer<TAddon>.Index;
        return _addons[index] is { } addon ? Unsafe.As<TAddon>(addon)
            : InitializeAddon(index, static () => new TAddon());
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public TAddon AddAddon<TAddon>()
        where TAddon : class, IAddon, new()
    {
        EnsureAddonRegistration();
        return InitializeAddon(WorldAddonIndexer<TAddon>.Index, static () => new TAddon());
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public TAddon AddAddon<TAddon>(TAddon addon)
        where TAddon : class, IAddon
    {
        EnsureAddonRegistration();
        ArgumentNullException.ThrowIfNull(addon);
        var index = WorldAddonIndexer<TAddon>.Index;
        BeginAddonInitialization<TAddon>(index);
        return InitializeAddon(index, addon);
    }

    private void BeginAddonInitialization<TAddon>(int index)
    {
        if (_addons[index] != null) {
            throw new Exception("Addon already exists: " + typeof(TAddon));
        }
        if (!_addonOperations.Add(index))
            throw new InvalidOperationException("An addon of the same type is already being initialized or removed: " + typeof(TAddon));
    }

    private TAddon InitializeAddon<TAddon>(int index, Func<TAddon> create)
        where TAddon : class, IAddon
    {
        BeginAddonInitialization<TAddon>(index);
        TAddon addon;
        try { addon = create(); }
        catch {
            _addonOperations.Remove(index);
            throw;
        }
        return InitializeAddon(index, addon);
    }

    private TAddon InitializeAddon<TAddon>(int index, TAddon addon)
        where TAddon : class, IAddon
    {
        try {
            addon.OnInitialize(this);
            _addons[index] = addon;
            _addonCount++;
        }
        finally {
            _addonOperations.Remove(index);
        }
        NotifyObservers(OnAddonCreated, addon, Outcome<Exception>.Success).ThrowIfFailed();
        return addon;
    }

    private void EnsureAddonRegistration()
    {
        EnsureOwnerThread();
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        if (_clearingAddons)
            throw new InvalidOperationException("Cannot register an addon while the world is clearing addons.");
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool RemoveAddon<TAddon>()
        where TAddon : class, IAddon
    {
        EnsureOwnerThread();
        var index = WorldAddonIndexer<TAddon>.Index;
        ref var addon = ref _addons[index];
        if (addon == null) {
            return false;
        }
        var removedAddon = addon;
        _addonOperations.Add(index);
        addon = null;
        _addonCount--;
        try {
            var result = Outcome<Exception>.Success.Attempt(() => removedAddon.OnUninitialize(this));
            NotifyObservers(OnAddonRemoved, removedAddon, result).ThrowIfFailed();
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
        EnsureOwnerThread();
        EnsureCanClearAddons();
        ClearAddonsCore();
    }

    private void EnsureCanClearAddons()
    {
        if (_clearingAddons || _addonOperations.Count != 0)
            throw new InvalidOperationException("Cannot clear addons during addon initialization or cleanup.");
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
