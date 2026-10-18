namespace Sia_Examples;

internal interface IApplicationNavigation : IDisposable
{
    string InitialPath { get; }
    bool IsCurrent(int ticket);
    Task HoldAsync(int ticket);
    Task CommitAsync(string path, int ticket = 0, bool replace = false);
}
