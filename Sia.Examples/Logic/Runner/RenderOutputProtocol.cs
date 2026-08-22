using System.Text;

namespace Sia_Examples.Notebook;

internal static class RenderOutputProtocol
{
    private const string _startToken = "\uE001R\uE001";
    private const string _endToken = "\uE001E\uE001";

    public static (string StandardOutput, string RenderOutput, bool RenderRequested)
        Split(string output)
    {
        var standard = new StringBuilder(output.Length);
        var render = new StringBuilder();
        var position = 0;
        var requested = false;
        while (position < output.Length) {
            var start = output.IndexOf(_startToken, position, StringComparison.Ordinal);
            if (start < 0) {
                standard.Append(output, position, output.Length - position);
                break;
            }
            standard.Append(output, position, start - position);
            var contentStart = start + _startToken.Length;
            var end = output.IndexOf(_endToken, contentStart, StringComparison.Ordinal);
            if (end < 0) {
                standard.Append(output, start, output.Length - start);
                break;
            }
            if (render.Length > 0) {
                render.AppendLine();
            }
            render.Append(output, contentStart, end - contentStart);
            requested = true;
            position = end + _endToken.Length;
        }
        return (standard.ToString(), render.ToString(), requested);
    }

    public static string BuildSupportSource()
        => $$"""
            #nullable enable

            namespace NotebookCell
            {
                public static class Notebook
                {
                    public static void Render(object? value = null)
                    {
                        global::System.Console.Out.Write("{{_startToken}}");
                        if (value is not null)
                        {
                            global::System.Console.Out.Write(value);
                        }
                        global::System.Console.Out.Write("{{_endToken}}");
                    }
                }
            }
            """;
}
