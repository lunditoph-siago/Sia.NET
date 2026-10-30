namespace Sia.Tests.Entities;

public sealed class CustomHListRemovalTests
{
    public struct Component { public int Value; }
    public struct Marker { public int Value; }

    public struct CustomRow : IHList
    {
        public Component Component;

        public readonly void Handle<THandler>(in THandler handler) where THandler : IHListHandler
            => handler.Handle(Component, EmptyHList.Default);
        public readonly void HandleHead<THandler>(in THandler handler) where THandler : IGenericHandler
            => handler.Handle(Component);
        public void HandleHeadRef<THandler>(ref THandler handler) where THandler : IRefGenericHandler
            => handler.Handle(ref Component);
        public readonly void HandleTail<THandler>(in THandler handler) where THandler : IGenericHandler<IHList>
            => handler.Handle(EmptyHList.Default);
        public readonly void HandleTailRef<THandler>(ref THandler handler) where THandler : IRefGenericHandler<IHList> { }
        public readonly void Filter<TPredicate, THandler>(in TPredicate predicate, in THandler handler)
            where TPredicate : IGenericPredicate where THandler : IGenericStructHandler<IHList> => handler.Handle(this);
        public readonly void Concat<TList, THandler>(in TList list, in THandler handler)
            where TList : struct, IHList where THandler : IGenericStructHandler<IHList> => handler.Handle(list);
        public readonly bool Remove<TValue, THandler>(TypeProxy<TValue> proxy, in THandler handler)
            where THandler : IGenericStructHandler<IHList>
        {
            if (typeof(TValue) != typeof(Component)) return false;
            var type = typeof(THandler);
            if (type.Name.StartsWith("TailConcater", StringComparison.Ordinal))
                type = type.GetGenericArguments()[^1];
            if (!type.Name.Equals("EntityMover", StringComparison.Ordinal))
                throw new InvalidOperationException("Unexpected handler type");
            handler.Handle(EmptyHList.Default);
            return true;
        }
        public readonly bool Remove<TValue, THandler>(in TValue value, in THandler handler)
            where TValue : IEquatable<TValue> where THandler : IGenericStructHandler<IHList> => false;
        public static void HandleTypes<THandler>(in THandler handler) where THandler : IGenericTypeHandler
            => handler.Handle<Component>();
    }

    [Fact]
    public void Custom_row_receives_the_original_remove_handler()
    {
        using var world = new World();
        var host = world.GetArrayHost<CustomRow>();
        var entity = host.Create(new CustomRow { Component = new Component { Value = 7 } });

        entity.Remove<Component>();

        Assert.True(entity.IsValid);
        Assert.False(entity.Contains<Component>());
    }

    [Fact]
    public void Custom_tail_receives_the_original_nested_remove_handler()
    {
        using var world = new World();
        var row = HList.Cons(new Marker { Value = 3 }, new CustomRow { Component = new Component { Value = 7 } });
        var entity = world.GetArrayHost<HList<Marker, CustomRow>>().Create(row);

        entity.Remove<Component>();

        Assert.True(entity.IsValid);
        Assert.Equal(3, entity.Get<Marker>().Value);
        Assert.False(entity.Contains<Component>());
    }
}
