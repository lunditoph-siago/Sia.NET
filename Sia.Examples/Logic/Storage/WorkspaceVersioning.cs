namespace Sia_Examples.Notebook;

using System.Security.Cryptography;
using System.Text;

internal static class WorkspaceVersioning
{
    private const int VersionLength = 12;

    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    public static string ComputeVersion(string content)
    {
        var hash = SHA256.HashData(Utf8NoBom.GetBytes(content));
        return Convert.ToHexStringLower(hash)[..VersionLength];
    }
}
