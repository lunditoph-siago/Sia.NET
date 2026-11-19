namespace Sia;

public interface IAddon
{
    void OnInitialize(World world) {}
    internal void EnsureCanUninitialize() {}
    void OnUninitialize(World world) {}
}
