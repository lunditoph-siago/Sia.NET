using System.Buffers;
using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;

namespace Sia.Serialization;

[RequiresUnreferencedCode(
    "Serialized host and component types are resolved by name. Their members must be preserved by the caller.")]
[RequiresDynamicCode(
    "Deserializing arbitrary host types constructs generic types and methods at runtime.")]
public sealed class ReflectionHostHeaderSerializer : IHostHeaderSerializer
{
    private static readonly byte[] Dividor = Encoding.Unicode.GetBytes(";");
    private static readonly byte[] StorageAliasHeader = Encoding.Unicode.GetBytes("$");
    private static readonly ConditionalWeakTable<EntityDescriptor, byte[]> s_componentHeaders = new();
    private static readonly ConditionalWeakTable<Type, Func<World, IReactiveEntityHost>> s_hostAcquirers = new();

    private static readonly FrozenDictionary<Type, byte[]> s_hostAlias = new Dictionary<Type, byte[]> {
        [typeof(ArrayEntityHost<>)] = Encoding.Unicode.GetBytes("$A"),
        [typeof(UnmanagedArrayEntityHost<>)] = Encoding.Unicode.GetBytes("$U"),
    }.ToFrozenDictionary();

    private static readonly FrozenDictionary<string, Type> s_aliasHostTypes = new Dictionary<string, Type> {
        ["$A"] = typeof(ArrayEntityHost<>),
        ["$U"] = typeof(UnmanagedArrayEntityHost<>)
    }.ToFrozenDictionary();

    public static void Serialize<TBufferWriter>(ref TBufferWriter writer, IEntityHost host)
        where TBufferWriter : IBufferWriter<byte>
    {
        writer.Write(s_componentHeaders.GetValue(host.Descriptor, static descriptor => {
            var components = string.Join('|',
                descriptor.Components.Select(c => c.Type.AssemblyQualifiedName));
            return Encoding.Unicode.GetBytes(components);
        }));
        writer.Write(Dividor);

        var type = host.GetType();
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(WorldEntityHost<,>)) {
            var innerHostType = type.GetGenericArguments()[1];
            if (innerHostType.IsGenericType
                    && s_hostAlias.TryGetValue(innerHostType.GetGenericTypeDefinition(), out var alias)) {
                writer.Write(alias);
            }
            else {
                writer.Write(StorageAliasHeader);
                writer.Write(Encoding.Unicode.GetBytes(
                    innerHostType.AssemblyQualifiedName!.Replace(
                        host.EntityType.AssemblyQualifiedName!, "%1")));
            }
            writer.Write(Dividor);
            return;
        }

        var entityType = host.EntityType.AssemblyQualifiedName!;
        writer.Write(Encoding.Unicode.GetBytes(
            type.AssemblyQualifiedName!.Replace(entityType, "%1")));
        writer.Write(Dividor);
    }

    public static IEntityHost? Deserialize(ref ReadOnlySequence<byte> buffer, World world)
    {
        if (buffer.IsEmpty) return null;

        var reader = new SequenceReader<byte>(buffer);

        if (!reader.TryReadTo(out ReadOnlySpan<byte> componentsRaw, Dividor, advancePastDelimiter: true)
            || !reader.TryReadTo(out ReadOnlySpan<byte> hostTypeRaw, Dividor, advancePastDelimiter: true)
            || hostTypeRaw.IsEmpty) {
            throw new InvalidDataException("Incomplete world host header.");
        }

        var components = componentsRaw.IsEmpty ? [] : Encoding.Unicode.GetString(componentsRaw).Split('|');
        var entityTypeName = GenerateHListType(components);

        var hostTypeName = Encoding.Unicode.GetString(hostTypeRaw);
        Type hostType;
        if (s_aliasHostTypes.TryGetValue(hostTypeName, out var innerHostType) || hostTypeName[0] == '$') {
            innerHostType ??= Type.GetType(hostTypeName[1..].Replace("%1", entityTypeName))
                ?? throw new InvalidDataException("Invalid host type: " + hostTypeName);
            var entityType = Type.GetType(entityTypeName)
                ?? throw new InvalidDataException("Invalid entity type: " + entityTypeName);
            if (innerHostType.IsGenericTypeDefinition) innerHostType = innerHostType.MakeGenericType(entityType);
            hostType = typeof(WorldEntityHost<,>).MakeGenericType(entityType, innerHostType);
        }
        else {
            hostTypeName = hostTypeName.Replace("%1", entityTypeName);
            hostType = Type.GetType(hostTypeName)
                ?? throw new InvalidDataException("Invalid host type: " + hostTypeName);
        }
        var host = InvokeAcquireHost(world, hostType);
        buffer = reader.UnreadSequence;
        return host;
    }

    private static readonly MethodInfo s_acquireHostMethod =
        typeof(ReflectionHostHeaderSerializer)
            .GetMethod("AcquireHost", BindingFlags.Static | BindingFlags.NonPublic)!;

    private static IReactiveEntityHost AcquireHost<THost>(World world)
        where THost : class, IReactiveEntityHost
        => world.TryGetHost<THost>(out var host)
            ? host : world.AddHost<THost>(static owner =>
                Unsafe.As<THost>(Activator.CreateInstance(typeof(THost), owner))!);

    private static IReactiveEntityHost InvokeAcquireHost(World world, Type hostType)
    {
        var acquire = s_hostAcquirers.GetValue(hostType, static type =>
            s_acquireHostMethod.MakeGenericMethod(type).CreateDelegate<Func<World, IReactiveEntityHost>>());
        try {
            return acquire(world);
        }
        catch (Exception exception) {
            // Preserve the reflection boundary for failures from the host acquisition itself.
            throw new TargetInvocationException(exception);
        }
    }

    private static string GenerateHListType(string[] components)
    {
        var builder = new StringBuilder();

        foreach (var component in components) {
            builder.Append("Sia.HList`2[[");
            builder.Append(component);
            builder.Append("], [");
        }

        builder.Append("Sia.EmptyHList, Sia");
        for (var i = 0; i != components.Length; ++i) {
            builder.Append("]], Sia");
        }

        return builder.ToString();
    }
}
