namespace Sia;

public interface IMatcher
{
    bool Match(in EntityRef entity);
}