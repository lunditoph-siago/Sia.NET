namespace Sia;

public interface IAddonInitializeListener<T>
    where T : notnull
{
    void OnInitialize(World<T> world);
}