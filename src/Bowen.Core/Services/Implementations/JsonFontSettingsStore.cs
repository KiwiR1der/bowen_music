using System.Text.Json;
using Bowen.Core.Models;
using Bowen.Core.Services.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bowen.Core.Services.Implementations;

/// <inheritdoc cref="IFontSettingsStore" />
/// <remarks>
/// <para>
/// <b>明文 JSON，不加密。</b> 显示偏好不是凭据，也不含任何账号标识。
/// </para>
/// <para>
/// <b>读失败一律返回默认，且不删坏文件</b> —— 留着还能人工看一眼出了什么事。
/// </para>
/// </remarks>
public sealed class JsonFontSettingsStore : IFontSettingsStore
{
    private readonly string _path;
    private readonly ILogger<JsonFontSettingsStore> _logger;

    /// <param name="path">默认 <c>font-settings.json</c>。测试可注入临时路径。</param>
    public JsonFontSettingsStore(
        string? path = null,
        ILogger<JsonFontSettingsStore>? logger = null)
    {
        _path = path ?? AppPaths.FontSettingsFile;
        _logger = logger ?? NullLogger<JsonFontSettingsStore>.Instance;
    }

    public FontSettings Load()
    {
        if (!File.Exists(_path))
        {
            return FontSettings.Default;
        }

        try
        {
            var bytes = File.ReadAllBytes(_path);
            var loaded = JsonSerializer.Deserialize(bytes, FontSettingsJsonContext.Default.FontSettings);
            return loaded?.Normalized() ?? FontSettings.Default;
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(exception, "界面字体设置读取失败，退回默认");
            return FontSettings.Default;
        }
    }

    public void Save(FontSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        try
        {
            var directory = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

            var bytes = JsonSerializer.SerializeToUtf8Bytes(settings.Normalized(), FontSettingsJsonContext.Default.FontSettings);
            var temp = _path + ".tmp";
            File.WriteAllBytes(temp, bytes);
            File.Move(temp, _path, overwrite: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(exception, "界面字体设置写入失败");
        }
    }
}
