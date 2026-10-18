using Sia_Examples.Notebook;

namespace Sia_Examples;

internal enum ApplicationDestination { Home, Editor, Notebook, NewNotebook, Quit }

internal sealed record ApplicationRoute(ApplicationDestination Target, NotebookInfo? Info = null)
{
    public string Path => Target switch {
        ApplicationDestination.Editor => "editor",
        ApplicationDestination.NewNotebook => "new",
        ApplicationDestination.Notebook when Info is { Origin: NotebookOrigin.BuiltIn } => "notebooks/" + BuiltInPath(Info.Key),
        ApplicationDestination.Notebook when Info is not null => "notebooks/" + Uri.EscapeDataString(Info.Key),
        _ => "",
    };

    public static ApplicationRoute? Resolve(string path, IReadOnlyList<NotebookInfo> notebooks)
    {
        path = path.Trim('/');
        if (path.Length == 0 || path == "index.html") return new(ApplicationDestination.Home);
        if (path == "editor") return new(ApplicationDestination.Editor);
        if (path == "new") return new(ApplicationDestination.NewNotebook);
        var userKey = path.StartsWith("notebooks/", StringComparison.Ordinal)
            ? Uri.UnescapeDataString(path[10..]) : null;
        foreach (var info in notebooks) {
            var route = new ApplicationRoute(ApplicationDestination.Notebook, info);
            if (info.Origin == NotebookOrigin.User
                ? userKey == info.Key
                : path == route.Path) return route;
        }
        return null;
    }

    private static string BuiltInPath(string key) => key switch {
        "Example1_Guide.notebook.xml" => "browser",
        "Example2_Essentials.notebook.xml" => "essentials",
        "Example3_Prelude.notebook.xml" => "runtime",
        _ => throw new ArgumentOutOfRangeException(nameof(key)),
    };
}
