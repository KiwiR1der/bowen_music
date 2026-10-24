using System.Text.Json.Serialization;
using Bowen.Core.Models;

namespace Bowen.Core.Services;

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(InputMethodSettings))]
internal sealed partial class InputMethodSettingsJsonContext : JsonSerializerContext;
