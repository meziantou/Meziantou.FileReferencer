using System.Text.Json.Serialization;

namespace Meziantou.FileReferencer;

[JsonSourceGenerationOptions()]
[JsonSerializable(typeof(FileReferences))]
internal partial class SourceGenerationContext : JsonSerializerContext
{
}
