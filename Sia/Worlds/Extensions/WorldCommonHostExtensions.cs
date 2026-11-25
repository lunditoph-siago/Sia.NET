using System.Runtime.CompilerServices;

namespace Sia;

public static class WorldCommonHostExtensions
{
    #region ArrayHost

    public static WorldEntityHost<TEntity, ArrayEntityHost<TEntity>> GetArrayHost<TEntity>(this World world)
        where TEntity : struct, IHList
        => world.AcquireHost<TEntity, ArrayEntityHost<TEntity>>();

    public static Entity Create(this World world)
        => world.GetArrayHost<EmptyHList>().Create();

    public static Entity Create<TEntity>(this World world, in TEntity initial)
        where TEntity : struct, IHList
        => CompactCreation<TEntity>.IsRequested
            ? CompactCreation<TEntity>.Create(world, initial, unmanaged: false)
            : world.GetArrayHost<TEntity>().Create(initial);

    #endregion // ArrayHost

    #region UnmanagedArrayHost

    public static WorldEntityHost<TEntity, UnmanagedArrayEntityHost<TEntity>> GetUnmanagedArrayHost<TEntity>(this World world)
        where TEntity : struct, IHList
        => world.AcquireHost<TEntity, UnmanagedArrayEntityHost<TEntity>>();

    public static Entity CreateUnmanaged(this World world)
        => world.GetUnmanagedArrayHost<EmptyHList>().Create();

    public static Entity CreateUnmanaged<TEntity>(this World world, in TEntity initial)
        where TEntity : struct, IHList
        => CompactCreation<TEntity>.IsRequested
            ? CompactCreation<TEntity>.Create(world, initial, unmanaged: true)
            : world.GetUnmanagedArrayHost<TEntity>().Create(initial);

    #endregion // ArrayHost

    private static class CompactCreation<TRow> where TRow : struct, IHList
    {
        private enum Layout
        {
            Ordinary,
            Empty,
            Compact,
            Unsupported
        }

        private static readonly Layout RequestedLayout = GetLayout();

        public static bool IsRequested => RequestedLayout != Layout.Ordinary;

        private static Layout GetLayout()
        {
            var type = typeof(TRow);
            if (!type.IsConstructedGenericType || type.GetGenericTypeDefinition() != typeof(CompactLayout<>)) {
                return Layout.Ordinary;
            }

            var row = type.GenericTypeArguments[0];
            if (row == typeof(EmptyHList)) return Layout.Empty;
            while (row.IsConstructedGenericType && row.GetGenericTypeDefinition() == typeof(HList<,>)) {
                row = row.GenericTypeArguments[1];
            }
            return row == typeof(EmptyHList)
                || (row.IsConstructedGenericType && row.GetGenericTypeDefinition() == typeof(HList<>))
                ? Layout.Compact : Layout.Unsupported;
        }

        public static unsafe Entity Create(World world, in TRow initial, bool unmanaged)
        {
            if (RequestedLayout == Layout.Empty) {
                return unmanaged ? world.CreateUnmanaged() : world.Create();
            }
            if (RequestedLayout == Layout.Unsupported) {
                throw new ArgumentException("CompactLayout requires a built-in HList.", nameof(initial));
            }

            Entity entity = default;
            // The validated built-in nodes only read from the row and tails during Handle.
#pragma warning disable CS8500 // The pointer addresses a stack local and is used synchronously.
            Unsafe.AsRef(in initial).Handle(new CompactTail<StoreEntity>(new(world, &entity, unmanaged)));
#pragma warning restore CS8500
            return entity;
        }
    }

#pragma warning disable CS8500 // The pointer addresses a stack local and is used synchronously.
    private readonly unsafe struct StoreEntity(World world, Entity* result, bool unmanaged)
        : IGenericStructHandler<IHList>
    {
        public void Handle<TRow>(in TRow row) where TRow : struct, IHList
        {
            if (unmanaged) {
                world.GetUnmanagedArrayHost<TRow>().Create(row, out *result);
            }
            else {
                world.GetArrayHost<TRow>().Create(row, out *result);
            }
        }
    }
#pragma warning restore CS8500

    private readonly struct CompactTail<THandler>(in THandler handler) : IHListHandler
        where THandler : struct, IGenericStructHandler<IHList>
    {
        private readonly THandler _handler = handler;

        public void Handle<THead, TTail>(in THead head, in TTail tail) where TTail : IHList
        {
            if (typeof(TTail) == typeof(EmptyHList)) {
                _handler.Handle(new HList<THead>(head));
                return;
            }
            Unsafe.AsRef(in tail).Handle(new CompactTail<Prepend<THead, THandler>>(new(head, _handler)));
        }
    }

    private readonly struct Prepend<THead, THandler>(in THead head, in THandler handler)
        : IGenericStructHandler<IHList>
        where THandler : struct, IGenericStructHandler<IHList>
    {
        private readonly THead _head = head;
        private readonly THandler _handler = handler;

        public void Handle<TTail>(in TTail tail) where TTail : struct, IHList
            => _handler.Handle(HList.Cons(_head, tail));
    }
}
