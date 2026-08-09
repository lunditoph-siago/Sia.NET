namespace Sia_Examples.Notebook;

public sealed class NotebookLibrary
{
    private readonly Dictionary<string, NotebookDocument> _cache = [];

    public IReadOnlyList<NotebookInfo> Notebooks { get; } = [
        new("OOM Killer",
            "Memory leak, GC, and the OOM killer",
            "Example1_OomKiller.notebook.xml"),
        new("Fork",
            "Process tree, cgroups, and PID reuse",
            "Example2_Fork.notebook.xml"),
        new("Htop",
            "Live monitoring from another thread",
            "Example3_Htop.notebook.xml"),
        new("Load Test",
            "Five ways to sum a million requests",
            "Example4_LoadTest.notebook.xml"),
        new("Git",
            "Commit log, diff, and reflog",
            "Example5_Git.notebook.xml"),
    ];

    public NotebookDocument Load(NotebookInfo info)
    {
        if (_cache.TryGetValue(info.ResourceName, out var cached)) {
            return cached;
        }

        var assembly = typeof(NotebookLibrary).Assembly;
        using var stream = assembly.GetManifestResourceStream(info.ResourceName)
            ?? throw new InvalidOperationException(
                $"Embedded resource '{info.ResourceName}' not found.");
        using var reader = new StreamReader(stream);
        var document = NotebookDocumentParser.Parse(reader.ReadToEnd());
        _cache[info.ResourceName] = document;
        return document;
    }
}
