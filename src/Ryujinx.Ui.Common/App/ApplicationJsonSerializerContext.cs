using System.Text.Json.Serialization;

namespace Ryujinx.Ui.Common.App
{
    [JsonSourceGenerationOptions(WriteIndented = true)]
    [JsonSerializable(typeof(ApplicationMetadata))]
    internal partial class ApplicationJsonSerializerContext : JsonSerializerContext
    {
    }
}