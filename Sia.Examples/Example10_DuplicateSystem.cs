namespace Sia.Examples;

public static class Example10_DuplicateSystem
{
    public class PrintSystem : SystemBase
    {
        private readonly string _text;

        public PrintSystem(string text)
        {
            _text = text;
            Matcher = Matchers.Any;
        }

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