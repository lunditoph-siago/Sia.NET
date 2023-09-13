namespace Sia;

public interface IAddonUninitializeListener<T>
    where T : notnull
{
    void OnUninitialize(World<T> world);
}