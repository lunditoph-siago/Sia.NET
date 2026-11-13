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

    private static readonly MethodInfo s_AcquireHostMethod =
        typeof(ReflectionHostHeaderSerializer)
            .GetMethod(nameof(AcquireHost), BindingFlags.Static | BindingFlags.Public)!;

    public static void Serialize<TBufferWriter>(ref TBufferWriter writer, IEntityHost host)
        where TBufferWriter : IBufferWriter<byte>
    {
        var components = string.Join('|',
            host.Descriptor.Components.Select(c => c.Type.AssemblyQualifiedName));

        writer.Write(Encoding.Unicode.GetBytes(components));
        writer.Write(s_HeaderSeparator);

        WriteHostType(ref writer, host);
        writer.Write(s_HeaderSeparator);
    }

    private static void WriteHostType<TBufferWriter>(ref TBufferWriter writer, IEntityHost host)
        where TBufferWriter : IBufferWriter<byte>
    {
        var hostType = host.GetType();
        if (hostType.IsGenericType && hostType.GetGenericTypeDefinition() == typeof(WorldEntityHost<,>)) {
            hostType = hostType.GetGenericArguments()[1];
            if (hostType.IsGenericType
                && s_HostAliases.TryGetValue(hostType.GetGenericTypeDefinition(), out var alias)) {
                writer.Write(alias);
                return;
            }
            writer.Write(s_WrappedHostPrefix);
        }

        writer.Write(Encoding.Unicode.GetBytes(
            hostType.AssemblyQualifiedName!.Replace(host.EntityType.AssemblyQualifiedName!, "%1")));
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

        var hostType = ResolveHostType(Encoding.Unicode.GetString(hostName), entityTypeName);
        var host = AcquireHostByType(world, hostType);

        buffer = reader.UnreadSequence;
        return host;
    }

    public static IReactiveEntityHost AcquireHost<THost>(World world)
        where THost : class, IReactiveEntityHost
        => world.AcquireRawHost(static owner => (THost)Activator.CreateInstance(typeof(THost), owner)!);

    private static IReactiveEntityHost AcquireHostByType(World world, Type hostType)
    {
        var acquireHostMethod = s_AcquireHostMethod.MakeGenericMethod(hostType);
        return Unsafe.As<IReactiveEntityHost>(acquireHostMethod.Invoke(null, [world]))!;
    }

    private static Type ResolveHostType(string hostTypeName, string entityTypeName)
    {
        if (hostTypeName[0] != '$') {
            return ResolveType(hostTypeName.Replace("%1", entityTypeName));
        }

        var innerHostType = s_HostTypesByAlias.TryGetValue(hostTypeName, out var aliasedType)
            ? aliasedType
            : ResolveType(hostTypeName[1..].Replace("%1", entityTypeName));
        var entityType = ResolveType(entityTypeName);
        if (innerHostType.IsGenericTypeDefinition) {
            innerHostType = innerHostType.MakeGenericType(entityType);
        }
        return typeof(WorldEntityHost<,>).MakeGenericType(entityType, innerHostType);
    }

    private static Type ResolveType(string typeName)
        => Type.GetType(typeName) ?? throw new InvalidDataException("Invalid world data type: " + typeName);

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
