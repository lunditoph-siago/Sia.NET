namespace Sia_Examples.Notebook;

public static class WorkspaceSeed
{
    public const string FolderPath = "src";
    public const string ProgramPath = "src/Program.cs";
    public const string SystemsPath = "src/MovementSystem.cs";

    private const string ProgramSource = """
        using Sia;

        using var world = new World();
        var entity = world.Create(HList.From(
            new Position(10, 20),
            new Velocity(2, -1)));

        Console.WriteLine($"Created {entity}");
        Notebook.Render($"Render frame · {entity}");
        """;

    private const string SystemsSource = """
        using Sia;

        public static class MovementSystem
        {
            public static void Update(World world, float deltaTime)
            {
                _ = world;
                _ = deltaTime;
            }
        }

        public readonly record struct Position(float X, float Y);
        public readonly record struct Velocity(float X, float Y);
        """;

    public static IReadOnlyList<(string Path, string Content)> Files =>
    [
        (ProgramPath, ProgramSource),
        (SystemsPath, SystemsSource),
    ];

    public static async ValueTask EnsureSeededAsync(
        IWorkspaceStorage storage, CancellationToken cancellationToken = default)
    {
        if ((await storage.ListTreeAsync(cancellationToken)).Count > 0) {
            return;
        }
        await storage.CreateFolderAsync(FolderPath, cancellationToken);
        foreach (var (path, content) in Files) {
            await storage.CreateFileAsync(path, content, cancellationToken);
        }
    }
}
