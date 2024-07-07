namespace Sia_Examples;

using Sia;

public static class Example10_DuplicateSystem
{
    public class PrintSystem(string text) : SystemBase(
        Matchers.Any)
    {
        private readonly string _text = text;

        public override void Execute(World world, IEntityQuery query)
        {
            Console.WriteLine(_text);
        }
    }

    public static void Run(World world)
    {
        var stage = SystemChain.Empty
            .Add<PrintSystem>(() => new("Hello!"))
            .Add<PrintSystem>(() => new("World!"))
            .CreateStage(world);
        
        stage.Tick();
        stage.Tick();
    }
}