using System.Xml;
using System.Xml.Linq;
using Sia_Examples.Editor;

namespace Sia_Examples.Notebook;

public sealed class NotebookLibrary(IWorkspaceStorage storage)
{
    internal const string Extension = ".notebook.xml";

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

    private readonly IWorkspaceStorage _storage = storage;
    private readonly Dictionary<string, NotebookDocument> _builtInCache = [];

    public IReadOnlyList<NotebookInfo> Notebooks { get; private set; } = BuiltIn;

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        var entries = await _storage.ListTreeAsync(cancellationToken);
        List<(NotebookInfo Info, DateTimeOffset ModifiedAt)> user = [];
        foreach (var entry in entries) {
            if (entry.Kind != WorkspaceEntryKind.File
                || !entry.Path.EndsWith(Extension, StringComparison.Ordinal)) {
                continue;
            }
            if (await _storage.ReadFileAsync(entry.Path, cancellationToken) is not { } content) {
                continue;
            }
            string title;
            try {
                title = PeekTitle(content.Content);
            }
            catch (XmlException) {
                continue;
            }
            var key = entry.Path[..^Extension.Length];
            user.Add((
                new NotebookInfo(title.Length > 0 ? title : "(untitled)", "", key, NotebookOrigin.User),
                entry.ModifiedAt ?? DateTimeOffset.MinValue));
        }
        Notebooks = [
            .. BuiltIn,
            .. user
                .OrderByDescending(static pair => pair.ModifiedAt)
                .Select(static pair => pair.Info),
        ];
    }

    public async ValueTask<(NotebookDocument Document, string? Version)> LoadAsync(
        NotebookInfo info, CancellationToken cancellationToken = default)
    {
        if (info.Origin == NotebookOrigin.BuiltIn) {
            return (LoadBuiltIn(info), null);
        }

        var loaded = await _storage.ReadFileAsync(PathOf(info.Key), cancellationToken)
            ?? throw new InvalidOperationException(
                $"'{info.Name}' no longer exists in storage. It may have been deleted elsewhere.");
        return (NotebookDocumentParser.Parse(loaded.Content), loaded.Version);
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

    internal static string PathOf(string key) => key + Extension;

    private static string PeekTitle(string xml)
    {
        using var reader = new StringReader(xml);
        var document = XDocument.Load(reader, LoadOptions.None);
        return (string?)document.Root?.Attribute("Title") ?? "";
    }
}
