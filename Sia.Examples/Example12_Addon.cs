namespace Sia_Examples;

using Sia;

public static partial class Example12_Addon
{
    public interface ITestAddon : IAddon {}
    public class Addon1 : ITestAddon {}
    public class Addon2 : ITestAddon {}

    public static void Run(World world)
    {
        var a1 = world.AcquireAddon<Addon1>();
        var a2 = world.GetAddon<ITestAddon>();
        world.AddAddon<Addon2>();
        Console.WriteLine("Found addon by interface: " + (a1 == a2));

        foreach (var addon in world.GetAddons<ITestAddon>()) {
            Console.WriteLine(addon.GetType());
        }
    }
}