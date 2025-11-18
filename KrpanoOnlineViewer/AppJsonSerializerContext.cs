using KrpanoOnlineViewer;
using System.Text.Json.Serialization;

[JsonSerializable(typeof(List<PanoramaInfo>))]
[JsonSerializable(typeof(ProcessingStatus))]
[JsonSerializable(typeof(PanoramaInfo))]
public partial class AppJsonSerializerContext : JsonSerializerContext
{
}