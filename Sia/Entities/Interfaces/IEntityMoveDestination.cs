namespace Sia;

internal interface IEntityMoveDestination : IEntityHost
{
    void EnsureCanMoveIn();
}
