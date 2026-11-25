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
    private const string ArrayHostAlias = "$A";
    private const string UnmanagedArrayHostAlias = "$U";
    // Metadata names cannot contain NUL. Existing component headers never start with it.
    private const char CompactLayoutMarker = '\0';
    private static readonly byte[] Dividor = Encoding.Unicode.GetBytes(";");
    private static readonly byte[] StorageAliasHeader = Encoding.Unicode.GetBytes("$");
    private static readonly byte[] s_arrayHostAlias = Encoding.Unicode.GetBytes(ArrayHostAlias);
    private static readonly byte[] s_unmanagedArrayHostAlias = Encoding.Unicode.GetBytes(UnmanagedArrayHostAlias);
    private static readonly ConditionalWeakTable<Type, byte[]> s_componentHeaders = new();
    private static readonly ConditionalWeakTable<Type, Func<World, IReactiveEntityHost>> s_hostAcquirers = new();

    private static readonly FrozenDictionary<Type, byte[]> s_hostAlias = new Dictionary<Type, byte[]> {
        [typeof(ArrayEntityHost<>)] = s_arrayHostAlias,
        [typeof(UnmanagedArrayEntityHost<>)] = s_unmanagedArrayHostAlias,
    }.ToFrozenDictionary();

    public static void Serialize<TBufferWriter>(ref TBufferWriter writer, IEntityHost host)
        where TBufferWriter : IBufferWriter<byte>
    {
        var descriptor = host.Descriptor;
        writer.Write(s_componentHeaders.GetOrAdd(descriptor.Type, static (_, layout) => {
            var components = string.Join('|',
                layout.Components.Select(c => c.Type.AssemblyQualifiedName));
            var header = HList.GetLayout(layout.Type) == HList.Layout.Compact
                ? CompactLayoutMarker + components : components;
            return Encoding.Unicode.GetBytes(header);
        }, descriptor));
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

        var components = Encoding.Unicode.GetString(componentsRaw);
        var entityTypeName = GenerateHListType(components);

        var hostTypeName = hostTypeRaw.SequenceEqual(s_arrayHostAlias) ? ArrayHostAlias
            : hostTypeRaw.SequenceEqual(s_unmanagedArrayHostAlias) ? UnmanagedArrayHostAlias
            : Encoding.Unicode.GetString(hostTypeRaw);
        var innerHostType = hostTypeName switch {
            ArrayHostAlias => typeof(ArrayEntityHost<>),
            UnmanagedArrayHostAlias => typeof(UnmanagedArrayEntityHost<>),
            _ => null
        };
        Type hostType;
        if (innerHostType != null || hostTypeName[0] == '$') {
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

        var acquire = s_hostAcquirers.GetValue(hostType, static type =>
            s_acquireHostMethod.MakeGenericMethod(type).CreateDelegate<Func<World, IReactiveEntityHost>>());
        IReactiveEntityHost host;
        try {
            host = acquire(world);
        }
        catch (Exception exception) {
            // Preserve the reflection boundary for failures from the host acquisition itself.
            throw new TargetInvocationException(exception);
        }
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

    private static string GenerateHListType(ReadOnlySpan<char> components)
    {
        if (components.IsEmpty) return "Sia.EmptyHList, Sia";

        var hasCompactTail = components[0] == CompactLayoutMarker;
        if (hasCompactTail) {
            components = components[1..];
            if (components.IsEmpty || components[^1] == '|') {
                throw new InvalidDataException("Missing component in compact host header.");
            }
        }

        var builder = new StringBuilder();
        var openNodes = 0;
        foreach (var range in components.Split('|')) {
            var component = components[range];
            if (hasCompactTail && component.IsEmpty) {
                throw new InvalidDataException("Missing component in compact host header.");
            }
            if (hasCompactTail && range.End.GetOffset(components.Length) == components.Length) {
                builder.Append("Sia.HList`1[[").Append(component).Append("]], Sia");
                break;
            }
            builder.Append("Sia.HList`2[[").Append(component).Append("], [");
            openNodes++;
        }

        if (!hasCompactTail) builder.Append("Sia.EmptyHList, Sia");
        for (var i = 0; i < openNodes; i++) {
            builder.Append("]], Sia");
        }

        return builder.ToString();
    }
}
