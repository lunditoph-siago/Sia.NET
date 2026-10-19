namespace Sia.Examples;

internal interface IApplicationNavigation : IDisposable
{
    public string InitialPath { get; }
    public bool IsCurrent(int ticket);
    public Task HoldAsync(int ticket);
    public Task CommitAsync(string path, int ticket = 0, bool replace = false);
}
