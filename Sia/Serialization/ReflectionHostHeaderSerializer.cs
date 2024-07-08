namespace Sia.Serialization;

using System.Buffers;
using System.Collections.Frozen;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;

public sealed class ReflectionHostHeaderSerializer : IHostHeaderSerializer
{
    private static readonly byte[] Dividor = Encoding.Unicode.GetBytes(";");
    private static readonly byte[] StorageAliasHeader = Encoding.Unicode.GetBytes("$");

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
        var components = string.Join('|',
            host.Descriptor.Components.Select(c => c.Type.AssemblyQualifiedName));

        writer.Write(Encoding.Unicode.GetBytes(components));
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
        var reader = new SequenceReader<byte>(buffer);

        if (!reader.TryReadTo(out ReadOnlySpan<byte> componentsRaw, Dividor, advancePastDelimiter: true)) {
            return null;
        }
        if (!reader.TryReadTo(out ReadOnlySpan<byte> hostTypeRaw, Dividor, advancePastDelimiter: true)) {
            buffer = reader.UnreadSequence;
            return null;
        }

        buffer = reader.UnreadSequence;

        var components = Encoding.Unicode.GetString(componentsRaw).Split('|');
        var entityTypeName = GenerateHListType(components);

        var hostTypeName = Encoding.Unicode.GetString(hostTypeRaw);
        if (s_aliasHostTypes.TryGetValue(hostTypeName, out var innerHostType)) {
            return GetWorldHost(world, innerHostType, Type.GetType(entityTypeName)!);
        }
        else if (hostTypeName[0] == '$') {
            innerHostType = Type.GetType(hostTypeName[1..].Replace("%1", entityTypeName))!;
            return GetWorldHost(world, innerHostType, Type.GetType(entityTypeName)!);
        }

        hostTypeName = hostTypeName.Replace("%1", entityTypeName);
        return GetHost(world, hostTypeName);
    }

    private static readonly MethodInfo s_acquireHostMethod = 
        typeof(ReflectionHostHeaderSerializer)
            .GetMethod("AcquireHost", BindingFlags.Static | BindingFlags.Public)!;

    public static IReactiveEntityHost AcquireHost<THost>(World world)
        where THost : class, IReactiveEntityHost
        => world.TryGetHost<THost>(out var host)
            ? host : world.UnsafeAddRawHost(
                Unsafe.As<THost>(Activator.CreateInstance(typeof(THost), world))!);

    private static IReactiveEntityHost GetHost(World world, string hostTypeName)
    {
        var hostType = Type.GetType(hostTypeName)
            ?? throw new InvalidDataException("Invalid host type: " + hostTypeName);
        var acquireHostMethod = s_acquireHostMethod.MakeGenericMethod(hostType);
        return Unsafe.As<IReactiveEntityHost>(acquireHostMethod.Invoke(null, [world]))!;
    }

    private static IReactiveEntityHost GetWorldHost(World world, Type hostGenericType, Type entityType)
    {
        var innerHostType = hostGenericType.MakeGenericType(entityType);
        var hostType = typeof(WorldEntityHost<,>).MakeGenericType(entityType, innerHostType);
        var acquireHostMethod = s_acquireHostMethod.MakeGenericMethod(hostType);
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
        for (int i = 0; i != components.Length; ++i) {
            builder.Append("]], Sia");
        }

        return builder.ToString();
    }
}