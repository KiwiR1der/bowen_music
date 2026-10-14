using System.Text.Json.Serialization;
using Bodian.Core.Models;

namespace Bodian.Core.Services;

/// <summary>
/// 代理偏好的 JSON 上下文。
/// </summary>
/// <remarks>
/// 与 <see cref="UpdatePreferencesJsonContext"/> 同一条规矩：走源生成，不开运行时反射
/// （Core 开了 <c>IsAotCompatible</c> + <c>TreatWarningsAsErrors</c>）。
/// 有枚举项（<see cref="ProxyMode"/>），所以 <c>UseStringEnumConverter</c> 这次是真的在用 ——
/// 存成 <c>"Custom"</c> 而不是 <c>2</c>，将来枚举顺序变了也不会读错。
/// </remarks>
[JsonSourceGenerationOptions(WriteIndented = true, UseStringEnumConverter = true)]
[JsonSerializable(typeof(ProxySettings))]
internal sealed partial class ProxySettingsJsonContext : JsonSerializerContext;
