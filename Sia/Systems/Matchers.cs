namespace Sia;

using System.Collections.Immutable;

public static class Matchers
{
    public static IMatcher Any { get; } = new AnyMatcher();
    public static IMatcher None { get; } = new NoneMatcher();

    public static IMatcher From<TTypeUnion>()
        where TTypeUnion : ITypeUnion, new()
        => new InclusiveMatcher(new TTypeUnion().ProxyTypes);

    public static IMatcher FromExclusive<TTypeUnion>()
        where TTypeUnion : ITypeUnion, new()
        => new ExclusiveMatcher(new TTypeUnion().ProxyTypes);

    public static IMatcher ToMatcher(this ITypeUnion typeUnion)
        => new InclusiveMatcher(typeUnion.ProxyTypes);

    public static IMatcher ToExclusiveMatcher(this ITypeUnion typeUnion)
        => new ExclusiveMatcher(typeUnion.ProxyTypes);
    
    public static IMatcher And(this IMatcher left, IMatcher right)
        => new AndMatcher(left, right);

    public static IMatcher Or(this IMatcher left, IMatcher right)
        => new OrMatcher(left, right);
    
    public static IMatcher Not(this IMatcher inner)
        => inner switch {
            NotMatcher m => m.Inner,
            InclusiveMatcher m => new ExclusiveMatcher(m.Types),
            ExclusiveMatcher m => new InclusiveMatcher(m.Types),
            AnyMatcher => None,
            NoneMatcher => Any,
            _ => new NotMatcher(inner)
        };

    public static IMatcher With(this IMatcher left, ITypeUnion right)
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

    public static IMatcher Without(this IMatcher left, ITypeUnion right)
        => left switch {
            ExclusiveMatcher m => new ExclusiveMatcher(m.Types.AddRange(right.ProxyTypes)),
            InclusiveMatcher m => new InclusiveAndExclusiveMatcher(m.Types, right.ProxyTypes),
            WithoutMatcher m => new WithoutMatcher(m.Left, m.Right.AddRange(right.ProxyTypes)),
            WithMatcher m => new WithAndWithoutMatcher(m.Left, m.Right, right.ProxyTypes),
            WithAndWithoutMatcher m => new WithAndWithoutMatcher(
                m.Inner, m.WithTypes, m.WithoutTypes.AddRange(right.ProxyTypes)),
            _ => new WithoutMatcher(left, right.ProxyTypes)
        };

    private record AnyMatcher : IMatcher
    {
        public bool Match(in EntityRef entity) => true;
    }

    private record NoneMatcher : IMatcher
    {
        public bool Match(in EntityRef entity) => false;
    }

    private record InclusiveMatcher(ImmutableArray<Type> Types) : IMatcher
    {
        public bool Match(in EntityRef entity)
        {
            foreach (var compType in Types.AsSpan()) {
                if (!entity.Contains(compType)) {
                    return false;
                }
            }
            return true;
        }
    }

    private record ExclusiveMatcher(ImmutableArray<Type> Types) : IMatcher
    {
        public bool Match(in EntityRef entity)
        {
            foreach (var compType in Types.AsSpan()) {
                if (entity.Contains(compType)) {
                    return false;
                }
            }
            return true;
        }
    }

    private record InclusiveAndExclusiveMatcher(
        ImmutableArray<Type> InclusiveTypes, ImmutableArray<Type> ExclusiveTypes) : IMatcher
    {
        public bool Match(in EntityRef entity)
        {
            foreach (var compType in InclusiveTypes.AsSpan()) {
                if (!entity.Contains(compType)) {
                    return false;
                }
            }
            foreach (var compType in ExclusiveTypes.AsSpan()) {
                if (entity.Contains(compType)) {
                    return false;
                }
            }
            return true;
        }
    }

    private record NotMatcher(IMatcher Inner) : IMatcher
    {
        public bool Match(in EntityRef entity)
            => !Inner.Match(entity);
    }

    private record AndMatcher(IMatcher Left, IMatcher Right) : IMatcher
    {
        public bool Match(in EntityRef entity)
            => Left.Match(entity) && Right.Match(entity);
    }

    private record OrMatcher(IMatcher Left, IMatcher Right) : IMatcher
    {
        public bool Match(in EntityRef entity)
            => Left.Match(entity) || Right.Match(entity);
    }

    private record WithMatcher(IMatcher Left, ImmutableArray<Type> Right) : IMatcher
    {
        public bool Match(in EntityRef entity)
        {
            if (!Left.Match(entity)) {
                return false;
            }
            foreach (var compType in Right.AsSpan()) {
                if (!entity.Contains(compType)) {
                    return false;
                }
            }
            return true;
        }
    }

    private record WithoutMatcher(IMatcher Left, ImmutableArray<Type> Right) : IMatcher
    {
        public bool Match(in EntityRef entity)
        {
            if (!Left.Match(entity)) {
                return false;
            }
            foreach (var compType in Right.AsSpan()) {
                if (entity.Contains(compType)) {
                    return false;
                }
            }
            return true;
        }
    }

    private record WithAndWithoutMatcher(
        IMatcher Inner, ImmutableArray<Type> WithTypes, ImmutableArray<Type> WithoutTypes) : IMatcher
    {
        public bool Match(in EntityRef entity)
        {
            if (!Inner.Match(entity)) {
                return false;
            }
            foreach (var compType in WithTypes.AsSpan()) {
                if (!entity.Contains(compType)) {
                    return false;
                }
            }
            foreach (var compType in WithoutTypes.AsSpan()) {
                if (entity.Contains(compType)) {
                    return false;
                }
            }
            return true;
        }
    }
}