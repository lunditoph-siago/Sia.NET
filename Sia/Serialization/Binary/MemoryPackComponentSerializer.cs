using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using MemoryPack;
using MemoryPack.Formatters;

namespace Sia.Serialization.Binary;

public struct MemoryPackComponentSerializer() : IComponentSerializer
{
    private MemoryPackWriterOptionalState? _writerState;
    private MemoryPackReaderOptionalState? _readerState;
    private Dictionary<EntityId, Entity>? _entityMap;

    private static class FormatterType<T>
    {
        internal static Type? Unmanaged;
    }

    private static class StringState
    {
        // The exact built-in StringFormatter uses encoding options and leaves reference tables alone.
        internal static readonly MemoryPackWriterOptionalState Writer =
            MemoryPackWriterOptionalStatePool.Rent(MemoryPackSerializerOptions.Default);
        internal static readonly MemoryPackReaderOptionalState Reader =
            MemoryPackReaderOptionalStatePool.Rent(MemoryPackSerializerOptions.Default);
    }

    private record DeserializerServiceProvider(Dictionary<EntityId, Entity> EntityMap)
        : IServiceProvider
    {
        public object? GetService(Type serviceType)
        {
            return null;
        }
    }

    private class EntityFormatter : MemoryPackFormatter<Entity>
    {
        public override void Serialize<TBufferWriter>(
            ref MemoryPackWriter<TBufferWriter> writer, scoped ref Entity value)
            => writer.WriteValue(value.Id.Value);

        public override void Deserialize(ref MemoryPackReader reader, scoped ref Entity value)
        {
            var map = ((DeserializerServiceProvider)reader.Options.ServiceProvider!).EntityMap;
            var id = reader.ReadValue<int>();
            value = map.TryGetValue(new(id), out var entity) ? entity : default;
        }
    }

    private class NullableEntityFormatter : MemoryPackFormatter<Entity?>
    {
        public override void Serialize<TBufferWriter>(
            ref MemoryPackWriter<TBufferWriter> writer, scoped ref Entity? value)
            => writer.WriteValue(value?.Id.Value ?? 0);

        public override void Deserialize(ref MemoryPackReader reader, scoped ref Entity? value)
        {
            var map = ((DeserializerServiceProvider)reader.Options.ServiceProvider!).EntityMap;
            var id = reader.ReadValue<int>();
            value = id != 0 && map.TryGetValue(new(id), out var entity)
                ? entity
                : null;
        }
    }

    static MemoryPackComponentSerializer()
    {
        MemoryPackFormatterProvider.Register(new EntityFormatter());
        MemoryPackFormatterProvider.Register(new NullableEntityFormatter());
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Serialize<TBufferWriter, TComponent>(
        ref TBufferWriter writer, in TComponent component)
        where TBufferWriter : IBufferWriter<byte>
    {
        try {
            if (UsesUnmanagedFormatter<TComponent>()) {
                var size = Unsafe.SizeOf<TComponent>();
                var span = writer.GetSpan(size)[..size];
                Unsafe.WriteUnaligned(ref MemoryMarshal.GetReference(span), component);
                writer.Advance(size);
                return true;
            }

            var stringFormatter = GetDefaultStringFormatter<TComponent>();
            if (stringFormatter != null) {
                var mpWriter = new MemoryPackWriter<TBufferWriter>(ref writer, StringState.Writer);
                mpWriter.WriteValueWithFormatter(stringFormatter, component);
                mpWriter.Flush();
            }
            else {
                _writerState ??= MemoryPackWriterOptionalStatePool.Rent(MemoryPackSerializerOptions.Default);
                var mpWriter = new MemoryPackWriter<TBufferWriter>(ref writer, _writerState);
                MemoryPackSerializer.Serialize(ref mpWriter, component);
            }
            return true;
        }
        catch (MemoryPackSerializationException) {
            return false;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Deserialize<TComponent>(
        ref ReadOnlySequence<byte> buffer, ref TComponent component, Dictionary<EntityId, Entity> entityMap)
    {
        if (UsesUnmanagedFormatter<TComponent>()) {
            return TryReadUnmanaged(ref buffer, ref component);
        }

        var stringFormatter = GetDefaultStringFormatter<TComponent>();
        if (stringFormatter == null && (_readerState == null || _entityMap != entityMap)) {
            var previous = _readerState;
            _readerState = MemoryPackReaderOptionalStatePool.Rent(
                MemoryPackSerializerOptions.Default with {
                    ServiceProvider = new DeserializerServiceProvider(entityMap)
                });
            _entityMap = entityMap;
            ((IDisposable?)previous)?.Dispose();
        }

        var mpReader = new MemoryPackReader(buffer, stringFormatter == null ? _readerState! : StringState.Reader);
        try {
            if (stringFormatter != null) {
                mpReader.ReadValueWithFormatter(stringFormatter, ref component!);
            }
            else {
                mpReader.ReadValue(ref component!);
            }
            buffer = buffer.Slice(mpReader.Consumed);
            return true;
        }
        catch (MemoryPackSerializationException) {
            return false;
        }
        finally {
            mpReader.Dispose();
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool UsesUnmanagedFormatter<T>()
    {
        if (RuntimeHelpers.IsReferenceOrContainsReferences<T>()) {
            return false;
        }
        // The provider can replace a formatter even after its first use.
        var type = default(MemoryPackReader).GetFormatter<T>().GetType();
        if (ReferenceEquals(type, Volatile.Read(ref FormatterType<T>.Unmanaged))) {
            return true;
        }
        if (type == typeof(DangerousUnmanagedFormatter<T>)
            || (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(UnmanagedFormatter<>))) {
            Volatile.Write(ref FormatterType<T>.Unmanaged, type);
            return true;
        }
        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static IMemoryPackFormatter<T>? GetDefaultStringFormatter<T>()
    {
        if (typeof(T) == typeof(string)) {
            var formatter = default(MemoryPackReader).GetFormatter<T>();
            if (formatter.GetType() == typeof(StringFormatter)) {
                return formatter;
            }
        }
        return null;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool TryReadUnmanaged<T>(ref ReadOnlySequence<byte> buffer, ref T component)
    {
        var size = Unsafe.SizeOf<T>();
        if (buffer.Length < size) {
            return false;
        }
        var span = buffer.FirstSpan;
        if (span.Length >= size) {
            component = Unsafe.ReadUnaligned<T>(ref MemoryMarshal.GetReference(span));
        }
        else {
            T value = default!;
            var bytes = MemoryMarshal.CreateSpan(ref Unsafe.As<T, byte>(ref value), size);
            buffer.Slice(0, size).CopyTo(bytes);
            component = value;
        }
        buffer = buffer.Slice(size);
        return true;
    }

    public void Dispose()
    {
        ((IDisposable?)_writerState)?.Dispose();
        ((IDisposable?)_readerState)?.Dispose();

        _writerState = null;
        _readerState = null;
        _entityMap = null;
    }
}
