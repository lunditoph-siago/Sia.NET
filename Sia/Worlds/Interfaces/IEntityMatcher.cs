namespace Sia;

public interface IEntityMatcher
{
    bool Match(EntityDescriptor descriptor);
}