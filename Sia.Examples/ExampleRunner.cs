using System.Text;
using Sia_Examples;

namespace Sia.Examples;

public record struct ExampleItem(string Name, string Description, Action<World> Runner);

public class ExampleRunner : IDisposable
{
    private readonly StringWriter _outputWriter = new();
    private bool _disposed = false;

    public IReadOnlyList<ExampleItem> Examples { get; private set; }

    public ExampleRunner()
    {
        Examples = CreateExampleList();
        Console.WriteLine($"[ExampleRunner] Loaded {Examples.Count} examples");
    }

    private static List<ExampleItem> CreateExampleList() =>
    [
        new("Health Damage", "Basic health and damage system", Example1_HealthDamage.Run),
        new("Health Recover", "Health recovery system", Example2_HealthRecover.Run),
        new("Mover Rotator", "Movement and rotation system", Example3_MoveRotator.Run),
        new("Aggregator", "Data aggregator example", Example4_Aggregator.Run),
        new("Component Bundle", "Component bundle example", Example5_ComponentBundle.Run),
        new("Hierarchy", "Hierarchy structure example", Example6_Hierarchy.Run),
        new("Mapper", "Data mapper example", Example7_Mapper.Run),
        new("SIMD", "SIMD vectorization example", Example8_Sum.Run),
        new("Duplicate System", "Duplicate system detection", Example10_DuplicateSystem.Run),
        new("RPG", "RPG game system example", Example11_RPG.Run),
        new("Addon", "Addon system example", Example12_Addon.Run),
        new("Parallel", "Parallel processing example", Example13_Parallel.Run),
        new("Runner with Context", "Runner with context", Example14_RunnerWithContext.Run),
        new("Serialization", "Serialization example", Example15_Serialization.Run),
        new("Event System", "Event system example", Example16_EventSystem.Run),
    ];

    public string RunExample(int index)
    {
        if (index < 0 || index >= Examples.Count)
        {
            return $"Error: Example index {index} out of range (0-{Examples.Count - 1})";
        }

        var example = Examples[index];
        var originalOut = Console.Out;

        try
        {
            // Clear previous output
            _outputWriter.GetStringBuilder().Clear();

            // Redirect console output
            Console.SetOut(_outputWriter);

            Console.WriteLine($"== Running Example: {example.Name} ==");
            Console.WriteLine($"Description: {example.Description}");
            Console.WriteLine();

            // Create a new world to run the example
            using var world = new World();
            Context<World>.Current = world;

            // Run the example
            example.Runner(world);

            Console.WriteLine();
            Console.WriteLine("== Example execution completed ==");

            // Restore console output
            Console.SetOut(originalOut);

            var output = _outputWriter.ToString();
            Console.WriteLine($"[ExampleRunner] Example '{example.Name}' completed, output {output.Split('\n').Length} lines");

            return output;
        }
        catch (Exception ex)
        {
            Console.SetOut(originalOut);

            var errorOutput = $"== Example execution failed ==\n" +
                             $"Example: {example.Name}\n" +
                             $"Error: {ex.Message}\n" +
                             $"Stack trace:\n{ex.StackTrace}";

            _outputWriter.GetStringBuilder().Append(errorOutput);
            Console.WriteLine($"[ExampleRunner] Example '{example.Name}' failed: {ex.Message}");

            return _outputWriter.ToString();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _outputWriter.Dispose();
        Console.WriteLine("[ExampleRunner] Resources disposed");
    }
}