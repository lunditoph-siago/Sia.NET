using System.Text.Json.Serialization;

namespace Sia;

public static class Matchers
{
    public static IEntityMatcher Any { get; } = new AnyMatcher();
    public static IEntityMatcher None { get; } = new NoneMatcher();

    public static IEntityMatcher From<TTypeUnion>()
        where TTypeUnion : ITypeUnion, new()
        => new InclusiveMatcher(new TTypeUnion());

    public static IEntityMatcher FromExclusive<TTypeUnion>()
        where TTypeUnion : ITypeUnion, new()
        => new ExclusiveMatcher(new TTypeUnion());

    public static IEntityMatcher ToMatcher(this ITypeUnion typeUnion)
        => new InclusiveMatcher(typeUnion);

    public static IEntityMatcher ToExclusiveMatcher(this ITypeUnion typeUnion)
        => new ExclusiveMatcher(typeUnion);
    
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
            InclusiveMatcher m => new InclusiveMatcher(m.Types.Merge(right)),
            ExclusiveMatcher m => new InclusiveAndExclusiveMatcher(right, m.Types),
            WithMatcher m => new WithMatcher(m.Left, m.Right.Merge(right)),
            WithoutMatcher m => new WithAndWithoutMatcher(m.Left, right, m.Right),
            WithAndWithoutMatcher m => new WithAndWithoutMatcher(
                m.Inner, m.WithTypes.Merge(right), m.WithoutTypes),
            AnyMatcher => Any,
            NoneMatcher => None,
            _ => new WithMatcher(left, right)
        };

    public static IEntityMatcher Without(this IEntityMatcher left, ITypeUnion right)
        => left switch {
            ExclusiveMatcher m => new ExclusiveMatcher(m.Types.Merge(right)),
            InclusiveMatcher m => new InclusiveAndExclusiveMatcher(m.Types, right),
            WithoutMatcher m => new WithoutMatcher(m.Left, m.Right.Merge(right)),
            WithMatcher m => new WithAndWithoutMatcher(m.Left, m.Right, right),
            WithAndWithoutMatcher m => new WithAndWithoutMatcher(
                m.Inner, m.WithTypes, m.WithoutTypes.Merge(right)),
            _ => new WithoutMatcher(left, right)
        };

    private record AnyMatcher : IEntityMatcher
    {
        public bool Match(EntityDescriptor descriptor) => true;
    }

    private record NoneMatcher : IEntityMatcher
    {
        public bool Match(EntityDescriptor descriptor) => false;
    }

    private record InclusiveMatcher(ITypeUnion Types) : IEntityMatcher
    {
        public bool Match(EntityDescriptor descriptor)
        {
            foreach (var compType in Types.Types) {
                if (!descriptor.Contains(compType)) {
                    return false;
                }
            }
            return true;
        }
    }

    private record ExclusiveMatcher(ITypeUnion Types) : IEntityMatcher
    {
        public bool Match(EntityDescriptor descriptor)
        {
            foreach (var compType in Types.Types) {
                if (descriptor.Contains(compType)) {
                    return false;
                }
            }
            return true;
        }
    }

    private record InclusiveAndExclusiveMatcher(
        ITypeUnion InclusiveTypes, ITypeUnion ExclusiveTypes) : IEntityMatcher
    {
        public bool Match(EntityDescriptor descriptor)
        {
            foreach (var compType in InclusiveTypes.Types) {
                if (!descriptor.Contains(compType)) {
                    return false;
                }
            }
            foreach (var compType in ExclusiveTypes.Types) {
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

    private record WithMatcher(IEntityMatcher Left, ITypeUnion Right) : IEntityMatcher
    {
        public bool Match(EntityDescriptor descriptor)
        {
            if (!Left.Match(descriptor)) {
                return false;
            }
            foreach (var compType in Right.Types) {
                if (!descriptor.Contains(compType)) {
                    return false;
                }
            }
            return true;
        }
    }

    private record WithoutMatcher(IEntityMatcher Left, ITypeUnion Right) : IEntityMatcher
    {
        public bool Match(EntityDescriptor descriptor)
        {
            if (!Left.Match(descriptor)) {
                return false;
            }
            foreach (var compType in Right.Types) {
                if (descriptor.Contains(compType)) {
                    return false;
                }
            }
            return true;
        }
    }

    private record WithAndWithoutMatcher(
        IEntityMatcher Inner, ITypeUnion WithTypes, ITypeUnion WithoutTypes) : IEntityMatcher
    {
        public bool Match(EntityDescriptor descriptor)
        {
            if (!Inner.Match(descriptor)) {
                return false;
            }
            foreach (var compType in WithTypes.Types) {
                if (!descriptor.Contains(compType)) {
                    return false;
                }
            }
            foreach (var compType in WithoutTypes.Types) {
                if (descriptor.Contains(compType)) {
                    return false;
                }
            }
            return true;
        }
    }
}