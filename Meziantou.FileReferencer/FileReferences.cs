using System.Text.Json.Serialization;

namespace Meziantou.FileReferencer;
internal sealed class FileReferences
{
    [JsonPropertyName("references")]
    public Dictionary<string, FileReference?>? References { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
