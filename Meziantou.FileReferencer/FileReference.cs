using System.Text.Json.Serialization;

namespace Meziantou.FileReferencer;
internal sealed class FileReference
{
    [JsonPropertyName("ref")]
    public string? Ref { get; set; }
}