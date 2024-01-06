namespace Sia_Examples;

using Sia;

public static class Example10_DuplicateSystem
{
    public class PrintSystem(string text)
        : SystemBase(
            matcher: Matchers.Any)
    {
        private readonly string _text = text;

        public override void Execute(World world, Scheduler scheduler, IEntityQuery query)
        {
            Console.WriteLine(_text);
        }
    }

    public static void Run(World world)
    {
        var scheduler = new Scheduler();

        SystemChain.Empty
            .Add<PrintSystem>(() => new("Hello!"))
            .Add<PrintSystem>(() => new("World!"))
            .RegisterTo(world, scheduler);
        
        scheduler.Tick();
        scheduler.Tick();
    }
}