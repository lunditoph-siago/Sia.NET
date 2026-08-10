namespace Sia_Examples.Notebook;

public sealed class NotebookLibrary(INotebookStorage storage)
{
    private static readonly IReadOnlyList<NotebookInfo> BuiltIn = [
        new("OOM Killer",
            "Memory leak, GC, and the OOM killer",
            "Example1_OomKiller.notebook.xml",
            NotebookOrigin.BuiltIn),
        new("Fork",
            "Process tree, cgroups, and PID reuse",
            "Example2_Fork.notebook.xml",
            NotebookOrigin.BuiltIn),
        new("Htop",
            "Live monitoring from another thread",
            "Example3_Htop.notebook.xml",
            NotebookOrigin.BuiltIn),
        new("Load Test",
            "Five ways to sum a million requests",
            "Example4_LoadTest.notebook.xml",
            NotebookOrigin.BuiltIn),
        new("Git",
            "Commit log, diff, and reflog",
            "Example5_Git.notebook.xml",
            NotebookOrigin.BuiltIn),
    ];

    private readonly INotebookStorage _storage = storage;
    private readonly Dictionary<string, NotebookDocument> _builtInCache = [];

    public IReadOnlyList<NotebookInfo> Notebooks { get; private set; } = BuiltIn;

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        var entries = await _storage.ListAsync(cancellationToken);
        var user = entries
            .OrderByDescending(static entry => entry.SavedAt)
            .Select(static entry => new NotebookInfo(
                entry.Title.Length > 0 ? entry.Title : "(untitled)",
                "",
                entry.Key,
                NotebookOrigin.User))
            .ToList();
        Notebooks = [.. BuiltIn, .. user];
    }

    public async ValueTask<(NotebookDocument Document, int? Version)> LoadAsync(
        NotebookInfo info, CancellationToken cancellationToken = default)
    {
        if (info.Origin == NotebookOrigin.BuiltIn) {
            return (LoadBuiltIn(info), null);
        }

        var loaded = await _storage.LoadAsync(info.Key, cancellationToken)
            ?? throw new InvalidOperationException(
                $"'{info.Name}' no longer exists in storage — it may have been deleted elsewhere.");
        return (NotebookDocumentParser.Parse(loaded.Xml), loaded.Version);
    }

    private NotebookDocument LoadBuiltIn(NotebookInfo info)
    {
        if (_builtInCache.TryGetValue(info.Key, out var cached)) {
            return cached;
        }

        var assembly = typeof(NotebookLibrary).Assembly;
        using var stream = assembly.GetManifestResourceStream(info.Key)
            ?? throw new InvalidOperationException(
                $"Embedded resource '{info.Key}' not found.");
        using var reader = new StreamReader(stream);
        var document = NotebookDocumentParser.Parse(reader.ReadToEnd());
        _builtInCache[info.Key] = document;
        return document;
    }
}
