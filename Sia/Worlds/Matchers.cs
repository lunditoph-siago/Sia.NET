namespace Sia;

using System.Collections.Immutable;

public static class Matchers
{
    public static IEntityMatcher Any { get; } = new AnyMatcher();
    public static IEntityMatcher None { get; } = new NoneMatcher();

    public static IEntityMatcher From<TTypeUnion>()
        where TTypeUnion : ITypeUnion, new()
        => new InclusiveMatcher(new TTypeUnion().ProxyTypes);

    public static IEntityMatcher FromExclusive<TTypeUnion>()
        where TTypeUnion : ITypeUnion, new()
        => new ExclusiveMatcher(new TTypeUnion().ProxyTypes);

    public static IEntityMatcher ToMatcher(this ITypeUnion typeUnion)
        => new InclusiveMatcher(typeUnion.ProxyTypes);

    public static IEntityMatcher ToExclusiveMatcher(this ITypeUnion typeUnion)
        => new ExclusiveMatcher(typeUnion.ProxyTypes);
    
    public static IEntityMatcher And(this IEntityMatcher left, IEntityMatcher right)
        => new AndMatcher(left, right);

    public static IEntityMatcher Or(this IEntityMatcher left, IEntityMatcher right)
        => new OrMatcher(left, right);
    
    public static IEntityMatcher Not(this IEntityMatcher inner)
        => inner switch {
            NotMatcher m => m.Inner,
            InclusiveMatcher m => new ExclusiveMatcher(m.Types),
            ExclusiveMatcher m => new InclusiveMatcher(m.Types),
            AnyMatcher => None,
            NoneMatcher => Any,
            _ => new NotMatcher(inner)
        };

    public static IEntityMatcher With(this IEntityMatcher left, ITypeUnion right)
        => left switch {
            InclusiveMatcher m => new InclusiveMatcher(m.Types.AddRange(right.ProxyTypes)),
            ExclusiveMatcher m => new InclusiveAndExclusiveMatcher(right.ProxyTypes, m.Types),
            WithMatcher m => new WithMatcher(m.Left, m.Right.AddRange(right.ProxyTypes)),
            WithoutMatcher m => new WithAndWithoutMatcher(m.Left, right.ProxyTypes, m.Right),
            WithAndWithoutMatcher m => new WithAndWithoutMatcher(
                m.Inner, m.WithTypes.AddRange(right.ProxyTypes), m.WithoutTypes),
            AnyMatcher => Any,
            NoneMatcher => None,
            _ => new WithMatcher(left, right.ProxyTypes)
        };

    public static IEntityMatcher Without(this IEntityMatcher left, ITypeUnion right)
        => left switch {
            ExclusiveMatcher m => new ExclusiveMatcher(m.Types.AddRange(right.ProxyTypes)),
            InclusiveMatcher m => new InclusiveAndExclusiveMatcher(m.Types, right.ProxyTypes),
            WithoutMatcher m => new WithoutMatcher(m.Left, m.Right.AddRange(right.ProxyTypes)),
            WithMatcher m => new WithAndWithoutMatcher(m.Left, m.Right, right.ProxyTypes),
            WithAndWithoutMatcher m => new WithAndWithoutMatcher(
                m.Inner, m.WithTypes, m.WithoutTypes.AddRange(right.ProxyTypes)),
            _ => new WithoutMatcher(left, right.ProxyTypes)
        };

    private record AnyMatcher : IEntityMatcher
    {
        public bool Match(EntityDescriptor descriptor) => true;
    }

    private record NoneMatcher : IEntityMatcher
    {
        public bool Match(EntityDescriptor descriptor) => false;
    }

    private record InclusiveMatcher(ImmutableArray<Type> Types) : IEntityMatcher
    {
        public bool Match(EntityDescriptor descriptor)
        {
            foreach (var compType in Types.AsSpan()) {
                if (!descriptor.Contains(compType)) {
                    return false;
                }
            }
            return true;
        }
    }

    private record ExclusiveMatcher(ImmutableArray<Type> Types) : IEntityMatcher
    {
        public bool Match(EntityDescriptor descriptor)
        {
            foreach (var compType in Types.AsSpan()) {
                if (descriptor.Contains(compType)) {
                    return false;
                }
            }
            return true;
        }
    }

    private record InclusiveAndExclusiveMatcher(
        ImmutableArray<Type> InclusiveTypes, ImmutableArray<Type> ExclusiveTypes) : IEntityMatcher
    {
        public bool Match(EntityDescriptor descriptor)
        {
            foreach (var compType in InclusiveTypes.AsSpan()) {
                if (!descriptor.Contains(compType)) {
                    return false;
                }
            }
            foreach (var compType in ExclusiveTypes.AsSpan()) {
                if (descriptor.Contains(compType)) {
                    return false;
                }
            }
            return true;
        }
    }

    private record NotMatcher(IEntityMatcher Inner) : IEntityMatcher
    {
        public bool Match(EntityDescriptor descriptor)
            => !Inner.Match(descriptor);
    }

    private record AndMatcher(IEntityMatcher Left, IEntityMatcher Right) : IEntityMatcher
    {
        public bool Match(EntityDescriptor descriptor)
            => Left.Match(descriptor) && Right.Match(descriptor);
    }

    private record OrMatcher(IEntityMatcher Left, IEntityMatcher Right) : IEntityMatcher
    {
        public bool Match(EntityDescriptor descriptor)
            => Left.Match(descriptor) || Right.Match(descriptor);
    }

    private record WithMatcher(IEntityMatcher Left, ImmutableArray<Type> Right) : IEntityMatcher
    {
        public bool Match(EntityDescriptor descriptor)
        {
            if (!Left.Match(descriptor)) {
                return false;
            }
            foreach (var compType in Right.AsSpan()) {
                if (!descriptor.Contains(compType)) {
                    return false;
                }
            }
            return true;
        }
    }

    private record WithoutMatcher(IEntityMatcher Left, ImmutableArray<Type> Right) : IEntityMatcher
    {
        public bool Match(EntityDescriptor descriptor)
        {
            if (!Left.Match(descriptor)) {
                return false;
            }
            foreach (var compType in Right.AsSpan()) {
                if (descriptor.Contains(compType)) {
                    return false;
                }
            }
            return true;
        }
    }

    private record WithAndWithoutMatcher(
        IEntityMatcher Inner, ImmutableArray<Type> WithTypes, ImmutableArray<Type> WithoutTypes) : IEntityMatcher
    {
        public bool Match(EntityDescriptor descriptor)
        {
            if (!Inner.Match(descriptor)) {
                return false;
            }
            foreach (var compType in WithTypes.AsSpan()) {
                if (!descriptor.Contains(compType)) {
                    return false;
                }
            }
            foreach (var compType in WithoutTypes.AsSpan()) {
                if (descriptor.Contains(compType)) {
                    return false;
                }
            }
            return true;
        }
    }
}