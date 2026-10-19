
using System.Security.Cryptography;
using System.Text;

namespace Sia.Examples.Notebook;

internal static class WorkspaceVersioning
{
    private const int k_VersionLength = 12;

    private static readonly UTF8Encoding s_Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    public static string ComputeVersion(string content)
    {
        var hash = SHA256.HashData(s_Utf8NoBom.GetBytes(content));
        return Convert.ToHexStringLower(hash)[..k_VersionLength];
    }
}
