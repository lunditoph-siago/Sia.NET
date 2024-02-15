namespace Sia;

public interface IEntityMatcher
{
    bool Match(IEntityHost host);
}