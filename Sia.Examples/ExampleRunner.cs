using System.Diagnostics.CodeAnalysis;
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
            ("OOM Killer",
                "Memory leak, GC, and the OOM killer",
                Example1_OomKiller.Run),
            ("Fork",
                "Process tree, cgroups, and PID reuse",
                Example2_Fork.Run),
            ("Htop",
                "Live monitoring from another thread",
                Example3_Htop.Run),
            ("Load Test",
                "Five ways to sum a million requests",
                Example4_LoadTest.Run),
            ("Git",
                "Commit log, diff, and reflog",
                Example5_Git.Run),
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
            using var world = new World();
            Context<World>.With(world, () => example.Runner(world));
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