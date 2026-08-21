namespace Sia_Examples.Notebook;

public sealed class NotebookLibrary(INotebookStorage storage)
{
    private static readonly IReadOnlyList<NotebookInfo> BuiltIn = [
        new("1. Browser",
            "Run C# and Sia.NET in the browser",
            "Example1_Guide.notebook.xml",
            NotebookOrigin.BuiltIn),
        new("2. Essentials",
            "Components, queries, systems, events, and addons",
            "Example2_Essentials.notebook.xml",
            NotebookOrigin.BuiltIn),
        new("3. Runtime",
            "Reactive lifecycles, concurrency, snapshots, and actors",
            "Example3_Prelude.notebook.xml",
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

    public async ValueTask<(NotebookDocument Document, string? Version)> LoadAsync(
        NotebookInfo info, CancellationToken cancellationToken = default)
    {
        if (info.Origin == NotebookOrigin.BuiltIn) {
            return (LoadBuiltIn(info), null);
        }

        var loaded = await _storage.LoadAsync(info.Key, cancellationToken)
            ?? throw new InvalidOperationException(
                $"'{info.Name}' no longer exists in storage. It may have been deleted elsewhere.");
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
