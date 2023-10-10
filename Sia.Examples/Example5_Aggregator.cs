namespace Sia.Examples;

public static partial class Example5_Aggregator
{
    public partial record struct ComponentCount([SiaProperty] int Value);

    public partial record Id

    public static void Run()
    {
        var world = new World();
        var scheduler = new Scheduler();

        world.AcquireAddon<Aggregator<>>
    }
}