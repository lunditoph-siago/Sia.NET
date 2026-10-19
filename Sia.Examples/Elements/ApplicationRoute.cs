using Sia.Examples.Notebook;

namespace Sia.Examples;

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
        var normalized = path.Trim('/');
        var userKey = normalized.StartsWith("notebooks/", StringComparison.Ordinal)
            ? Uri.UnescapeDataString(normalized[10..]) : null;
        return normalized.Split('/') switch {
            [""] or ["index.html"] => new(ApplicationDestination.Home),
            ["editor"] => new(ApplicationDestination.Editor),
            ["new"] => new(ApplicationDestination.NewNotebook),
            ["notebooks", _, ..] => notebooks
                .Select(static info => new ApplicationRoute(ApplicationDestination.Notebook, info))
                .FirstOrDefault(route => route.Info!.Origin == NotebookOrigin.User
                    ? userKey == route.Info.Key
                    : normalized == route.Path),
            _ => null,
        };
    }

    private static string BuiltInPath(string key) => key switch {
        "Example1_Guide.notebook.xml" => "browser",
        "Example2_Essentials.notebook.xml" => "essentials",
        "Example3_Prelude.notebook.xml" => "runtime",
        _ => throw new ArgumentOutOfRangeException(nameof(key)),
    };
}
