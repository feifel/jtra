using System.Text.Json.Serialization;
using JtraShared.Models;

namespace JtraClient.Services;

[JsonSerializable(typeof(TimeEntry))]
[JsonSerializable(typeof(List<TimeEntry>))]
[JsonSerializable(typeof(AppSettings))]
[JsonSerializable(typeof(List<ConfigurableType>))]
[JsonSerializable(typeof(ConfigurableType))]
[JsonSerializable(typeof(TicketCache))]
[JsonSerializable(typeof(List<TicketCache>))]
[JsonSerializable(typeof(ConnectionState))]
[JsonSerializable(typeof(int))]
[JsonSerializable(typeof(string))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal partial class JtraJsonContext : JsonSerializerContext
{
}
