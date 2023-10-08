namespace Sia;

public static class Matchers
{
    public static IEntityMatcher Any { get; } = new AnyMatcher();
    public static IEntityMatcher None { get; } = new NoneMatcher();
    
    public static IEntityMatcher AllOf<T1>()
        => From<TypeUnion<T1>>();
    public static IEntityMatcher AllOf<T1, T2>()
        => From<TypeUnion<T1, T2>>();
    public static IEntityMatcher AllOf<T1, T2, T3>()
        => From<TypeUnion<T1, T2, T3>>();
    public static IEntityMatcher AllOf<T1, T2, T3, T4>()
        => From<TypeUnion<T1, T2, T3, T4>>();
    public static IEntityMatcher AllOf<T1, T2, T3, T4, T5>()
        => From<TypeUnion<T1, T2, T3, T4, T5>>();
    public static IEntityMatcher AllOf<T1, T2, T3, T4, T5, T6>()
        => From<TypeUnion<T1, T2, T3, T4, T5, T6>>();
    public static IEntityMatcher AllOf<T1, T2, T3, T4, T5, T6, T7>()
        => From<TypeUnion<T1, T2, T3, T4, T5, T6, T7>>();
    public static IEntityMatcher AllOf<T1, T2, T3, T4, T5, T6, T7, T8>()
        => From<TypeUnion<T1, T2, T3, T4, T5, T6, T7, T8>>();
    public static IEntityMatcher AllOf<T1, T2, T3, T4, T5, T6, T7, T8, T9>()
        => From<TypeUnion<T1, T2, T3, T4, T5, T6, T7, T8, T9>>();
    public static IEntityMatcher AllOf<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>()
        => From<TypeUnion<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>>();
    public static IEntityMatcher AllOf<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11>()
        => From<TypeUnion<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11>>();
    public static IEntityMatcher AllOf<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>()
        => From<TypeUnion<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>>();
    public static IEntityMatcher AllOf<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13>()
        => From<TypeUnion<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13>>();
    public static IEntityMatcher AllOf<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14>()
        => From<TypeUnion<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14>>();
    public static IEntityMatcher AllOf<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15>()
        => From<TypeUnion<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15>>();
    public static IEntityMatcher AllOf<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16>()
        => From<TypeUnion<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16>>();

    public static IEntityMatcher Exclude<T1>()
        => FromExclusive<TypeUnion<T1>>();
    public static IEntityMatcher Exclude<T1, T2>()
        => FromExclusive<TypeUnion<T1, T2>>();
    public static IEntityMatcher Exclude<T1, T2, T3>()
        => FromExclusive<TypeUnion<T1, T2, T3>>();
    public static IEntityMatcher Exclude<T1, T2, T3, T4>()
        => FromExclusive<TypeUnion<T1, T2, T3, T4>>();
    public static IEntityMatcher Exclude<T1, T2, T3, T4, T5>()
        => FromExclusive<TypeUnion<T1, T2, T3, T4, T5>>();
    public static IEntityMatcher Exclude<T1, T2, T3, T4, T5, T6>()
        => FromExclusive<TypeUnion<T1, T2, T3, T4, T5, T6>>();
    public static IEntityMatcher Exclude<T1, T2, T3, T4, T5, T6, T7>()
        => FromExclusive<TypeUnion<T1, T2, T3, T4, T5, T6, T7>>();
    public static IEntityMatcher Exclude<T1, T2, T3, T4, T5, T6, T7, T8>()
        => FromExclusive<TypeUnion<T1, T2, T3, T4, T5, T6, T7, T8>>();
    public static IEntityMatcher Exclude<T1, T2, T3, T4, T5, T6, T7, T8, T9>()
        => FromExclusive<TypeUnion<T1, T2, T3, T4, T5, T6, T7, T8, T9>>();
    public static IEntityMatcher Exclude<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>()
        => FromExclusive<TypeUnion<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>>();
    public static IEntityMatcher Exclude<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11>()
        => FromExclusive<TypeUnion<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11>>();
    public static IEntityMatcher Exclude<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>()
        => FromExclusive<TypeUnion<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>>();
    public static IEntityMatcher Exclude<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13>()
        => FromExclusive<TypeUnion<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13>>();
    public static IEntityMatcher Exclude<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14>()
        => FromExclusive<TypeUnion<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14>>();
    public static IEntityMatcher Exclude<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15>()
        => FromExclusive<TypeUnion<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15>>();
    public static IEntityMatcher Exclude<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16>()
        => FromExclusive<TypeUnion<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16>>();

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