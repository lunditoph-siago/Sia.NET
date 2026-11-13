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
    private static readonly byte[] s_HeaderSeparator = Encoding.Unicode.GetBytes(";");
    private static readonly byte[] s_WrappedHostPrefix = Encoding.Unicode.GetBytes("$");

    private static readonly FrozenDictionary<Type, byte[]> s_HostAliases = new Dictionary<Type, byte[]> {
        [typeof(ArrayEntityHost<>)] = Encoding.Unicode.GetBytes("$A"),
        [typeof(UnmanagedArrayEntityHost<>)] = Encoding.Unicode.GetBytes("$U"),
    }.ToFrozenDictionary();

    private static readonly FrozenDictionary<string, Type> s_HostTypesByAlias = new Dictionary<string, Type> {
        ["$A"] = typeof(ArrayEntityHost<>),
        ["$U"] = typeof(UnmanagedArrayEntityHost<>)
    }.ToFrozenDictionary();

    public static void Serialize<TBufferWriter>(ref TBufferWriter writer, IEntityHost host)
        where TBufferWriter : IBufferWriter<byte>
    {
        var components = string.Join('|',
            host.Descriptor.Components.Select(c => c.Type.AssemblyQualifiedName));

        writer.Write(Encoding.Unicode.GetBytes(components));
        writer.Write(s_HeaderSeparator);

        var type = host.GetType();
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(WorldEntityHost<,>)) {
            var innerHostType = type.GetGenericArguments()[1];
            if (innerHostType.IsGenericType
                    && s_HostAliases.TryGetValue(innerHostType.GetGenericTypeDefinition(), out var alias)) {
                writer.Write(alias);
            }
            else {
                writer.Write(s_WrappedHostPrefix);
                writer.Write(Encoding.Unicode.GetBytes(
                    innerHostType.AssemblyQualifiedName!.Replace(
                        host.EntityType.AssemblyQualifiedName!, "%1")));
            }
            writer.Write(s_HeaderSeparator);
            return;
        }

        var entityType = host.EntityType.AssemblyQualifiedName!;
        writer.Write(Encoding.Unicode.GetBytes(
            type.AssemblyQualifiedName!.Replace(entityType, "%1")));
        writer.Write(s_HeaderSeparator);
    }

    public static IEntityHost? Deserialize(ref ReadOnlySequence<byte> buffer, World world)
    {
        if (buffer.IsEmpty) {
            return null;
        }

        var reader = new SequenceReader<byte>(buffer);

        if (!reader.TryReadTo(out ReadOnlySpan<byte> componentNames, s_HeaderSeparator, advancePastDelimiter: true)
            || !reader.TryReadTo(out ReadOnlySpan<byte> hostName, s_HeaderSeparator, advancePastDelimiter: true)
            || hostName.IsEmpty) {
            throw new InvalidDataException("Incomplete world host header.");
        }

        var components = componentNames.IsEmpty ? [] : Encoding.Unicode.GetString(componentNames).Split('|');
        var entityTypeName = GenerateHListType(components);

        var hostTypeName = Encoding.Unicode.GetString(hostName);
        IReactiveEntityHost host;
        if (hostTypeName[0] == '$') {
            if (!s_HostTypesByAlias.TryGetValue(hostTypeName, out var innerHostType)) {
                innerHostType = ResolveType(hostTypeName[1..].Replace("%1", entityTypeName));
            }
            host = AcquireWrappedHost(world, innerHostType, ResolveType(entityTypeName));
        }
        else {
            host = AcquireNamedHost(world, hostTypeName.Replace("%1", entityTypeName));
        }

        buffer = reader.UnreadSequence;
        return host;
    }

    private static readonly MethodInfo s_AcquireHostMethod =
        typeof(ReflectionHostHeaderSerializer)
            .GetMethod(nameof(AcquireHost), BindingFlags.Static | BindingFlags.Public)!;

    public static IReactiveEntityHost AcquireHost<THost>(World world)
        where THost : class, IReactiveEntityHost
        => world.AcquireRawHost(static owner => (THost)Activator.CreateInstance(typeof(THost), owner)!);

    private static IReactiveEntityHost AcquireNamedHost(World world, string hostTypeName)
    {
        var hostType = ResolveType(hostTypeName);
        var acquireHostMethod = s_AcquireHostMethod.MakeGenericMethod(hostType);
        return Unsafe.As<IReactiveEntityHost>(acquireHostMethod.Invoke(null, [world]))!;
    }

    private static Type ResolveType(string typeName)
        => Type.GetType(typeName) ?? throw new InvalidDataException("Invalid world data type: " + typeName);

    private static IReactiveEntityHost AcquireWrappedHost(World world, Type innerHostType, Type entityType)
    {
        if (innerHostType.IsGenericTypeDefinition) {
            innerHostType = innerHostType.MakeGenericType(entityType);
        }
        var hostType = typeof(WorldEntityHost<,>).MakeGenericType(entityType, innerHostType);
        var acquireHostMethod = s_AcquireHostMethod.MakeGenericMethod(hostType);
        return Unsafe.As<IReactiveEntityHost>(acquireHostMethod.Invoke(null, [world]))!;
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
