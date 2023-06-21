namespace Sia;

public interface IMergeable : ICommand
{
    uint? Id { get; }

    void Merge(ICommand other);
}