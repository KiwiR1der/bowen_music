using System.Text.Json.Serialization;

namespace Bowen.Core.Services;

[JsonSerializable(typeof(string[]))]
internal sealed partial class SearchHistoryJsonContext : JsonSerializerContext;
