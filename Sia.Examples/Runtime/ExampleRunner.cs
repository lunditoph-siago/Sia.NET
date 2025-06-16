namespace Sia.Examples.Runtime;

public readonly record struct ExampleItem(string Name, string Description, Action<World> Runner);

public sealed class ExampleRunner(IReadOnlyList<ExampleItem> examples) : IDisposable
{
    private readonly StringWriter _outputWriter = new();
    private bool _disposed;

    public string RunExample(int index) =>
        index >= 0 && index < examples.Count
            ? ExecuteExample(examples[index])
            : $"Error: Example index {index} out of range (0-{examples.Count - 1})";

    private string ExecuteExample(ExampleItem example)
    {
        var originalOut = Console.Out;

        try
        {
            _outputWriter.GetStringBuilder().Clear();
            Console.SetOut(_outputWriter);

            Console.WriteLine($"== Running Example: {example.Name} ==");
            Console.WriteLine($"Description: {example.Description}");
            Console.WriteLine();

            using var world = new World();
            Context<World>.Current = world;

            example.Runner(world);

            Console.WriteLine();
            Console.WriteLine("== Example execution completed ==");

            return GetFormattedOutput(example.Name);
        }
        catch (Exception ex)
        {
            return FormatErrorOutput(example.Name, ex);
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }

    private string GetFormattedOutput(string exampleName)
    {
        var output = _outputWriter.ToString();
        var lineCount = output.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length;
        Console.WriteLine($"[ExampleRunner] Example '{exampleName}' completed, output {lineCount} lines");
        return output;
    }

    private string FormatErrorOutput(string exampleName, Exception ex)
    {
        var errorOutput = $"""
                           == Example execution failed ==
                           Example: {exampleName}
                           Error: {ex.Message}
                           Stack trace:
                           {ex.StackTrace}
                           """;

        _outputWriter.GetStringBuilder().Append(errorOutput);
        Console.WriteLine($"[ExampleRunner] Example '{exampleName}' failed: {ex.Message}");
        return _outputWriter.ToString();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _outputWriter.Dispose();
        Console.WriteLine("[ExampleRunner] Resources disposed");
    }
}