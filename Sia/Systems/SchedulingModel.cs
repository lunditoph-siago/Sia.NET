namespace Sia;

using System.Collections.Immutable;
using System.Reflection;

public readonly record struct ScheduleLabel(string Name)
{
    public override string ToString() => $"Schedule<{Name}>";
}

public static class CoreSchedules
{
    public static readonly ScheduleLabel Startup = new(nameof(Startup));
    public static readonly ScheduleLabel First = new(nameof(First));
    public static readonly ScheduleLabel PreUpdate = new(nameof(PreUpdate));
    public static readonly ScheduleLabel Update = new(nameof(Update));
    public static readonly ScheduleLabel PostUpdate = new(nameof(PostUpdate));
    public static readonly ScheduleLabel Last = new(nameof(Last));
}

public readonly record struct SystemSetLabel(string Name)
{
    public override string ToString() => $"SystemSet<{Name}>";

    public static SystemSetLabel For<TSet>()
        where TSet : ISystemSet
        => new(typeof(TSet).FullName ?? typeof(TSet).Name);
}

public interface ISystemSet;

public readonly struct SystemId : IEquatable<SystemId>
{
    private readonly Type? _type;
    private readonly string? _name;

    public string Name => _name ?? _type?.FullName ?? string.Empty;
    public Type? Type => _type;
    public bool IsType => _type is not null;

    private SystemId(Type? type, string? name)
    {
        _type = type;
        _name = name;
    }

    public static SystemId For<TSystem>()
        where TSystem : ISystem
        => ForType(typeof(TSystem));

    public static SystemId ForType(Type systemType)
        => new(systemType, null);

    public static SystemId Func(string name)
        => new(null, name);

    public static SystemId ForDelegate(Delegate handler)
    {
        var method = handler.Method;
        var typeName = method.DeclaringType?.FullName ?? "<global>";
        return Func($"{typeName}.{method.Name}");
    }

    public bool Equals(SystemId other)
    {
        if (_type is not null || other._type is not null) {
            return ReferenceEquals(_type, other._type);
        }
        return string.Equals(_name, other._name, StringComparison.Ordinal);
    }

    public override bool Equals(object? obj)
        => obj is SystemId other && Equals(other);

    public override int GetHashCode()
        => _type?.GetHashCode()
            ?? (_name is null ? 0 : StringComparer.Ordinal.GetHashCode(_name));

    public static bool operator ==(SystemId left, SystemId right)
        => left.Equals(right);

    public static bool operator !=(SystemId left, SystemId right)
        => !left.Equals(right);

    public override string ToString() => _type?.FullName ?? _name ?? "<default>";
}

public enum SystemDependencyTargetKind
{
    System,
    Set
}

public readonly record struct SystemDependencyTarget
{
    public SystemDependencyTargetKind Kind { get; }
    public SystemId System { get; }
    public SystemSetLabel Set { get; }

    private SystemDependencyTarget(
        SystemDependencyTargetKind kind,
        SystemId system,
        SystemSetLabel set)
    {
        Kind = kind;
        System = system;
        Set = set;
    }

    public static SystemDependencyTarget ForSystem(SystemId system)
        => new(SystemDependencyTargetKind.System, system, default);

    public static SystemDependencyTarget ForSet(SystemSetLabel set)
        => new(SystemDependencyTargetKind.Set, default, set);
}

public sealed record SystemDescriptor(
    SystemId Id,
    ImmutableHashSet<SystemSetLabel> MemberOf,
    ImmutableHashSet<SystemDependencyTarget> RunsBefore,
    ImmutableHashSet<SystemDependencyTarget> RunsAfter)
{
    public static SystemDescriptor For<TSystem>()
        where TSystem : ISystem
        => ForId(SystemId.For<TSystem>());

    public static SystemDescriptor ForId(SystemId id)
        => new(
            id,
            ImmutableHashSet<SystemSetLabel>.Empty,
            ImmutableHashSet<SystemDependencyTarget>.Empty,
            ImmutableHashSet<SystemDependencyTarget>.Empty);

    public SystemDescriptor InSet(SystemSetLabel set)
        => this with { MemberOf = MemberOf.Add(set) };

    public SystemDescriptor InSet<TSet>()
        where TSet : ISystemSet
        => InSet(SystemSetLabel.For<TSet>());

    public SystemDescriptor Before<TOther>()
        where TOther : ISystem
        => Before(SystemId.For<TOther>());

    public SystemDescriptor Before(SystemId system)
        => this with {
            RunsBefore = RunsBefore.Add(SystemDependencyTarget.ForSystem(system))
        };

    public SystemDescriptor Before(SystemSetLabel set)
        => this with { RunsBefore = RunsBefore.Add(SystemDependencyTarget.ForSet(set)) };

    public SystemDescriptor BeforeSet<TSet>()
        where TSet : ISystemSet
        => Before(SystemSetLabel.For<TSet>());

    public SystemDescriptor After<TOther>()
        where TOther : ISystem
        => After(SystemId.For<TOther>());

    public SystemDescriptor After(SystemId system)
        => this with {
            RunsAfter = RunsAfter.Add(SystemDependencyTarget.ForSystem(system))
        };

    public SystemDescriptor After(SystemSetLabel set)
        => this with { RunsAfter = RunsAfter.Add(SystemDependencyTarget.ForSet(set)) };

    public SystemDescriptor AfterSet<TSet>()
        where TSet : ISystemSet
        => After(SystemSetLabel.For<TSet>());
}

public interface ISystemDescriptorProvider
{
    bool TryGet(Type systemType, out SystemDescriptor descriptor);
}

[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
public sealed class SiaSystemDescriptorProviderAttribute(Type providerType) : Attribute
{
    public Type ProviderType { get; } = providerType;
}

public static class SystemDescriptorProvider
{
    private static readonly Dictionary<Assembly, ISystemDescriptorProvider[]> Providers = [];

    public static SystemDescriptor GetOrDefault(Type systemType)
    {
        foreach (var provider in GetProviders(systemType.Assembly)) {
            if (provider.TryGet(systemType, out var descriptor)) {
                return descriptor;
            }
        }
        return SystemDescriptor.ForId(SystemId.ForType(systemType));
    }

    private static ISystemDescriptorProvider[] GetProviders(Assembly assembly)
    {
        lock (Providers) {
            if (Providers.TryGetValue(assembly, out var providers)) {
                return providers;
            }

            providers = [
                ..assembly
                    .GetCustomAttributes<SiaSystemDescriptorProviderAttribute>()
                    .Select(static attribute =>
                        (ISystemDescriptorProvider)Activator.CreateInstance(
                            attribute.ProviderType,
                            nonPublic: true)!)
            ];
            Providers[assembly] = providers;
            return providers;
        }
    }
}
