namespace Sia;

public interface IMergableCommand : ICommand
{
    uint? Id { get; }

    void Merge(ICommand other);
}