using System.Text.Json.Serialization;
using Bowen.Core.Models;

namespace Bowen.Core.Services;

/// <summary>
/// 更新偏好的 JSON 上下文。
/// </summary>
/// <remarks>
/// 与 <see cref="ViewModeSettingsJsonContext"/> 同一条规矩：走源生成，不开运行时反射
/// （Core 开了 <c>IsAotCompatible</c> + <c>TreatWarningsAsErrors</c>）。
/// 这份没有枚举项，<c>UseStringEnumConverter</c> 留着是为了以后加档位时不必再改。
/// </remarks>
[JsonSourceGenerationOptions(WriteIndented = true, UseStringEnumConverter = true)]
[JsonSerializable(typeof(UpdatePreferences))]
internal sealed partial class UpdatePreferencesJsonContext : JsonSerializerContext;
