using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sia.Examples.Notebook;

internal static class PackageManifest
{
    public const string Path = "packages.json";
    private static readonly JsonSerializerOptions s_Options = new(JsonSerializerDefaults.Web) {
        WriteIndented = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
    };

    public static PackageRef[] Read(string content)
        => JsonSerializer.Deserialize<PackageRef[]>(content, s_Options)
            ?? throw new InvalidDataException("packages.json must contain an array of package declarations.");

    public static string Write(IEnumerable<PackageRef> packages)
        => JsonSerializer.Serialize(packages, s_Options) + "\n";
}
