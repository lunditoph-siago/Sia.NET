namespace Sia;

using System.Collections.Immutable;
using System.Runtime.InteropServices;

public record SystemChain(
    ImmutableList<Type> Sequence, ImmutableDictionary<Type, SystemChain.Entry> Entries)
{
    public readonly record struct Entry(
        Func<SystemLibrary, Scheduler, IEnumerable<Scheduler.TaskGraphNode>?, SystemHandle> Registerer,
        ImmutableArray<Type> PreceedingSystemTypes,
        ImmutableArray<Type> FollowingSystemTypes);
    
    public sealed class Disposable : IDisposable
    {
        public IReadOnlyList<SystemHandle> Handles => _handles;

        private List<SystemHandle> _handles;

        internal Disposable(List<SystemHandle> handles)
        {
            _handles = handles;
        }

        public void Dispose()
        {
            if (_handles == null) {
                return;
            }
            for (int i = _handles.Count - 1; i >= 0; --i) {
                _handles[i].Dispose();
            }
            _handles = null!;
        }
    }
    
    public static readonly SystemChain Empty = new();

    private SystemChain() : this(ImmutableList<Type>.Empty, ImmutableDictionary<Type, Entry>.Empty) {}

    public SystemChain Add<TSystem>()
        where TSystem : ISystem, new()
    {
        var newEntries = Entries.Add(
            typeof(TSystem), new(
                (sysLib, scheduler, taskGraphNodes) => sysLib.Register<TSystem>(scheduler, taskGraphNodes),
                GetAttributedSystemTypes<TSystem>(typeof(BeforeSystemAttribute<>)),
                GetAttributedSystemTypes<TSystem>(typeof(AfterSystemAttribute<>))
            ));
        if (newEntries == Entries) {
            return this;
        }
        return new(Sequence.Add(typeof(TSystem)), newEntries);
    }
    
    public SystemChain Remove<TSystem>()
        where TSystem : ISystem, new()
    {
        var newEntries = Entries.Remove(typeof(TSystem));
        if (newEntries == Entries) {
            return this;
        }
        return new(Sequence.Remove(typeof(TSystem)), newEntries);
    }
    
    public Disposable RegisterTo(World world, Scheduler scheduler, IEnumerable<Scheduler.TaskGraphNode>? dependedTasks = null)
    {
        var sysLib = world.AcquireAddon<SystemLibrary>();
        var depSysTypesDict = new Dictionary<Type, HashSet<Type>?>();
        var sysHandles = new Dictionary<Type, SystemHandle?>();
        var sysHandleList = new List<SystemHandle>(Entries.Count);
        
        ref HashSet<Type>? AcquireDependedSystemTypes(Type type, in Entry entry)
        {
            ref var depSysTypes = ref CollectionsMarshal.GetValueRefOrAddDefault(depSysTypesDict, type, out bool exists);
            if (exists) {
                return ref depSysTypes;
            }

            var preSysTypes = entry.PreceedingSystemTypes;
            if (preSysTypes.Length != 0) {
                foreach (var preSysType in preSysTypes) {
                    if (preSysType == type) {
                        throw new InvalidSystemDependencyException(
                            $"System {preSysType} cannot preceed itself.");
                    }
                    if (!Entries.ContainsKey(preSysType)) {
                        throw new InvalidSystemDependencyException(
                            $"Proceeding system {preSysType} for system {type} is not found in the system chain.");
                    }
                }
                depSysTypes ??= new();
                depSysTypes.UnionWith(preSysTypes);
            }

            var flwSysTypes = entry.FollowingSystemTypes;
            if (flwSysTypes.Length != 0) {
                foreach (var flwSysType in flwSysTypes) {
                    if (flwSysType == type) {
                        throw new InvalidSystemDependencyException(
                            $"System {flwSysType} cannot follow itself.");
                    }
                    if (!Entries.TryGetValue(flwSysType, out var flwSysEntry)) {
                        throw new InvalidSystemDependencyException(
                            $"Following system {flwSysType} for system {type} is not found in the system chain.");
                    }
                    (AcquireDependedSystemTypes(flwSysType, flwSysEntry) ??= new()).Add(type);
                }
            }

            return ref depSysTypes;
        }

        SystemHandle DoRegister(Type type)
        {
            ref var sysHandle = ref CollectionsMarshal.GetValueRefOrAddDefault(sysHandles, type, out bool exists);
            if (exists) {
                if (sysHandle == null) {
                    foreach (var handle in sysHandles.Values) {
                        handle?.Dispose();
                    }
                    throw new InvalidSystemDependencyException($"Circular dependency found for system {type}.");
                }
                return sysHandle;
            }

            var depSysTypes = depSysTypesDict[type];
            var depSysHandles = depSysTypes?.Select(depSysType => DoRegister(depSysType).TaskGraphNode);

            depSysHandles = depSysHandles != null
                ? dependedTasks?.Concat(depSysHandles.ToArray()) ?? depSysHandles.ToArray()
                : dependedTasks;

            sysHandle = Entries[type].Registerer(sysLib, scheduler, depSysHandles);
            sysHandleList.Add(sysHandle);
            return sysHandle;
        }

        foreach (var (type, entry) in Entries) {
            AcquireDependedSystemTypes(type, entry);
        }
        foreach (var type in Sequence) {
            DoRegister(type);
        }
        return new Disposable(sysHandleList);
    }
    
    private static ImmutableArray<Type> GetAttributedSystemTypes<TSystem>(Type genericAttrType)
        where TSystem : ISystem
        => Attribute.GetCustomAttributes(typeof(TSystem))
            .Where(attr => attr.GetType().GetGenericTypeDefinition() == genericAttrType)
            .Cast<ISystemAttribute>()
            .Select(attr => attr.SystemType)
            .ToImmutableArray();
}