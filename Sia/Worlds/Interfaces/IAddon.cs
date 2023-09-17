namespace Sia;

public interface IAddon
{
    void OnInitialize(World world) {}
    void OnUninitialize(World world) {}
}