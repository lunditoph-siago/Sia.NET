namespace Sia.Serialization;

using System.Buffers;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;

public sealed class ReflectionHostHeaderSerializer : IHostHeaderSerializer
{
    private static readonly byte[] Dividor = Encoding.Unicode.GetBytes(";");

    public static void Serialize<TBufferWriter>(ref TBufferWriter writer, IEntityHost host)
        where TBufferWriter : IBufferWriter<byte>
    {
        var type = host.GetType().AssemblyQualifiedName!;
        var components = string.Join('|',
            host.Descriptor.Components.Skip(1).Select(c => c.Type.AssemblyQualifiedName));

        writer.Write(Encoding.Unicode.GetBytes(components));
        writer.Write(Dividor);

        writer.Write(Encoding.Unicode.GetBytes(
            type.Replace(host.Descriptor.Type.AssemblyQualifiedName!, "%1")
                .Replace(host.InnerEntityType.AssemblyQualifiedName!, "%2")));
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

        var hostTypeName = Encoding.Unicode.GetString(hostTypeRaw)
            .Replace("%1", "Sia.HList`2[[Sia.Entity, Sia], [" + entityTypeName + "]], Sia")
            .Replace("%2", entityTypeName);

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