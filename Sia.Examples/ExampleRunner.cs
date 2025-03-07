using Sia;

namespace Sia_Examples;

public class ExampleRunner
{
    public delegate void ExampleDelegate(World world);

    public record ExampleInfo(string Name, string Description, ExampleDelegate Runner);

    private readonly List<ExampleInfo> _examples = [];
    private readonly StringWriter _outputWriter = new();
    private readonly TextWriter _originalConsoleOut = Console.Out;

    public IReadOnlyList<ExampleInfo> Examples => _examples;
    public string CurrentOutput => _outputWriter.ToString();

    public ExampleRunner()
    {
        InitializeExamples();
    }

    private void InitializeExamples()
    {
        var examples = new (string Name, string Description, ExampleDelegate Runner)[]
        {
            ("Health & Damage", "Demonstrates health and damage system", Example1_HealthDamage.Run),
            ("Health Recovery", "Demonstrates automatic health recovery system", Example2_HealthRecover.Run),
            ("Move & Rotate", "Demonstrates movement and rotation system", Example3_MoveRotator.Run),
            ("Aggregator", "Demonstrates aggregator system", Example4_Aggregator.Run),
            ("Component Bundle", "Demonstrates component bundles", Example5_ComponentBundle.Run),
            ("Hierarchy", "Demonstrates hierarchy system", Example6_Hierarchy.Run),
            ("Mapper", "Demonstrates mapper functionality", Example7_Mapper.Run),
            ("SIMD", "Demonstrates SIMD operations", Example8_Sum.Run),
            ("Duplicate System", "Demonstrates duplicate system", Example10_DuplicateSystem.Run),
            ("RPG System", "Demonstrates RPG system", Example11_RPG.Run),
            ("Addon", "Demonstrates addon system", Example12_Addon.Run),
            ("Parallel", "Demonstrates parallel processing", Example13_Parallel.Run),
            ("Runner Context", "Demonstrates runner context", Example14_RunnerWithContext.Run),
            ("Serialization", "Demonstrates serialization", Example15_Serialization.Run),
            ("Event System", "Demonstrates event system", Example16_EventSystem.Run)
        };

        foreach (var (name, description, runner) in examples)
            _examples.Add(new ExampleInfo(name, description, runner));
    }

    public string RunExample(int index)
    {
        if (index < 0 || index >= _examples.Count)
            return "Invalid example index";

        var example = _examples[index];

        try
        {
            _outputWriter.GetStringBuilder().Clear();
            Console.SetOut(_outputWriter);

            Console.WriteLine($"== {example.Name} ==");
            var world = new World();
            Context<World>.Current = world;
            example.Runner(world);
            world.Dispose();
            Console.WriteLine();

            return _outputWriter.ToString();
        }
        catch (Exception e)
        {
            return $"Error running example: {e.Message}\n{e.StackTrace}";
        }
        finally
        {
            Console.SetOut(_originalConsoleOut);
        }
    }

    public void Dispose()
    {
        _outputWriter.Dispose();
        Console.SetOut(_originalConsoleOut);
    }
}