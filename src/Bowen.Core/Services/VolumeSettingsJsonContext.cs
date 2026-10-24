using System.Text.Json.Serialization;
using Bowen.Core.Models;

namespace Bowen.Core.Services;

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(VolumeSettings))]
internal sealed partial class VolumeSettingsJsonContext : JsonSerializerContext;
