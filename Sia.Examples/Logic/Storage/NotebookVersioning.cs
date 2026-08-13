using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;

namespace Sia_Examples.Notebook;

internal static class NotebookVersioning
{
    private const int VersionLength = 12;

    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    public static string ComputeVersion(string xml)
    {
        var hash = SHA256.HashData(Utf8NoBom.GetBytes(xml));
        return Convert.ToHexStringLower(hash)[..VersionLength];
    }

    public static string PeekTitle(string xml)
    {
        using var reader = new StringReader(xml);
        var document = XDocument.Load(reader, LoadOptions.None);
        return (string?)document.Root?.Attribute("Title") ?? "";
    }
}
