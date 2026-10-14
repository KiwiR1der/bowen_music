using System.Text.Json;
using Bodian.Core.Models;
using Bodian.Core.Services.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bodian.Core.Services.Implementations;

/// <summary>
/// 更新偏好落在一个明文 JSON 文件里（<c>update.json</c>）。
/// </summary>
/// <remarks>
/// 写法与 <see cref="JsonInputMethodSettingsStore"/> 逐条一致：原子替换（先写 <c>.tmp</c> 再 Move）、
/// 读坏了回落到默认值、任何失败都不抛只记一条 Warning。
/// </remarks>
public sealed class JsonUpdatePreferencesStore(
    string? path = null,
    ILogger<JsonUpdatePreferencesStore>? logger = null) : IUpdatePreferencesStore
{
    private readonly string _path = path ?? AppPaths.UpdatePreferencesFile;
    private readonly ILogger<JsonUpdatePreferencesStore> _logger = logger ?? NullLogger<JsonUpdatePreferencesStore>.Instance;

    public UpdatePreferences Load()
    {
        if (!File.Exists(_path))
        {
            return UpdatePreferences.Default;
        }

        try
        {
            return JsonSerializer.Deserialize(
                File.ReadAllBytes(_path),
                UpdatePreferencesJsonContext.Default.UpdatePreferences) ?? UpdatePreferences.Default;
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            // 坏文件回落到「自动检查开着、没跳过任何版本」：宁可多查一次，
            // 也不要因为一个坏字节让用户从此收不到更新提示。
            _logger.LogWarning(exception, "更新偏好读取失败，按默认继续：{Path}", _path);
            return UpdatePreferences.Default;
        }
    }

    public bool Save(UpdatePreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);

        try
        {
            var directory = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var bytes = JsonSerializer.SerializeToUtf8Bytes(
                preferences, UpdatePreferencesJsonContext.Default.UpdatePreferences);
            var temporary = _path + ".tmp";
            File.WriteAllBytes(temporary, bytes);
            File.Move(temporary, _path, overwrite: true);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(exception, "更新偏好写入失败：{Path}", _path);
            return false;
        }
    }
}
