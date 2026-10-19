using System.Text;

namespace Sia.Examples.Notebook;

internal static class RenderOutputProtocol
{
    private const string k_StartToken = "\uE001R\uE001";
    private const string k_EndToken = "\uE001E\uE001";

    public static (string StandardOutput, string RenderOutput, bool RenderRequested)
        Split(string output)
    {
        var standard = new StringBuilder(output.Length);
        var render = new StringBuilder();
        var position = 0;
        var requested = false;
        while (position < output.Length) {
            var start = output.IndexOf(k_StartToken, position, StringComparison.Ordinal);
            if (start < 0) {
                standard.Append(output, position, output.Length - position);
                break;
            }
            standard.Append(output, position, start - position);
            var contentStart = start + k_StartToken.Length;
            var end = output.IndexOf(k_EndToken, contentStart, StringComparison.Ordinal);
            if (end < 0) {
                standard.Append(output, start, output.Length - start);
                break;
            }
            if (render.Length > 0) {
                render.AppendLine();
            }
            render.Append(output, contentStart, end - contentStart);
            requested = true;
            position = end + k_EndToken.Length;
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
                        global::System.Console.Out.Write("{{k_StartToken}}");
                        if (value is not null)
                        {
                            global::System.Console.Out.Write(value);
                        }
                        global::System.Console.Out.Write("{{k_EndToken}}");
                    }
                }
            }
            """;
}
