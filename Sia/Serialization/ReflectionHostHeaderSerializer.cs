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

    private static readonly FrozenDictionary<Type, byte[]> s_storageAlias = new Dictionary<Type, byte[]> {
        [typeof(ArrayBufferStorage<>)] = Encoding.Unicode.GetBytes("$A"),
        [typeof(HashBufferStorage<>)] = Encoding.Unicode.GetBytes("$H"),
        [typeof(BucketBufferStorage<>)] = Encoding.Unicode.GetBytes("$B"),
        [typeof(SparseBufferStorage<>)] = Encoding.Unicode.GetBytes("$S"),

        [typeof(UnversionedArrayBufferStorage<>)] = Encoding.Unicode.GetBytes("$UA"),
        [typeof(UnversionedHashBufferStorage<>)] = Encoding.Unicode.GetBytes("$UH"),
        [typeof(UnversionedBucketBufferStorage<>)] = Encoding.Unicode.GetBytes("$UB"),
        [typeof(UnversionedSparseBufferStorage<>)] = Encoding.Unicode.GetBytes("$US")
    }.ToFrozenDictionary();

    private static readonly FrozenDictionary<string, Type> s_aliasStorageTypes = new Dictionary<string, Type> {
        ["$A"] = typeof(ArrayBufferStorage<>),
        ["$H"] = typeof(HashBufferStorage<>),
        ["$B"] = typeof(BucketBufferStorage<>),
        ["$S"] = typeof(SparseBufferStorage<>),

        ["$UA"] = typeof(UnversionedArrayBufferStorage<>),
        ["$UH"] = typeof(UnversionedHashBufferStorage<>),
        ["$UB"] = typeof(UnversionedBucketBufferStorage<>),
        ["$US"] = typeof(UnversionedSparseBufferStorage<>)
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
            var storageType = type.GetGenericArguments()[1];
            if (storageType.IsGenericType
                    && s_storageAlias.TryGetValue(storageType.GetGenericTypeDefinition(), out var alias)) {
                writer.Write(alias);
            }
            else {
                writer.Write(StorageAliasHeader);
                writer.Write(Encoding.Unicode.GetBytes(
                    storageType.AssemblyQualifiedName!.Replace(
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
        if (s_aliasStorageTypes.TryGetValue(hostTypeName, out var storageType)) {
            return GetWorldHost(world, storageType, Type.GetType(entityTypeName)!);
        }
        else if (hostTypeName[0] == '$') {
            storageType = Type.GetType(hostTypeName[1..].Replace("%1", entityTypeName))!;
            return GetWorldHost(world, storageType, Type.GetType(entityTypeName)!);
        }

        hostTypeName = hostTypeName.Replace("%1", entityTypeName);
        return GetHost(world, hostTypeName);
    }

    private static readonly MethodInfo s_acquireHostMethod = 
        typeof(ReflectionHostHeaderSerializer)
            .GetMethod("AcquireHost", BindingFlags.Static | BindingFlags.NonPublic)!;

    private static IReactiveEntityHost AcquireHost<THost>(World world)
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

    private static IReactiveEntityHost GetWorldHost(World world, Type storageGenericType, Type entityType)
    {
        var storageType = storageGenericType.MakeGenericType(
            typeof(HList<,>).MakeGenericType(typeof(Entity), entityType));
        var hostType = typeof(WorldEntityHost<,>).MakeGenericType(entityType, storageType);
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