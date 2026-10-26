using System.Text.Json.Serialization;
using Bowen.Core.Models;

namespace Bowen.Core.Services;

/// <summary>
/// 界面字体偏好的 JSON 上下文。
/// </summary>
/// <remarks>
/// 与 <see cref="ThemeSettingsJsonContext"/> 同一条规矩：走源生成，不开运行时反射。
/// </remarks>
[JsonSourceGenerationOptions(WriteIndented = true, UseStringEnumConverter = true)]
[JsonSerializable(typeof(FontSettings))]
internal sealed partial class FontSettingsJsonContext : JsonSerializerContext;
