namespace Sia;

public interface ISortable : ICommand
{
    int Priority { get; }
}