using System.Text.Json;
using Bowen.Core.Models;
using Bowen.Core.Services.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bowen.Core.Services.Implementations;

/// <summary>
/// 代理偏好落在一个明文 JSON 文件里（<c>proxy.json</c>）。
/// </summary>
/// <remarks>
/// 写法与 <see cref="JsonUpdatePreferencesStore"/> 逐条一致：原子替换（先写 <c>.tmp</c> 再 Move）、
/// 读坏了回落到默认值、任何失败都不抛只记一条 Warning。
/// </remarks>
public sealed class JsonProxySettingsStore(
    string? path = null,
    ILogger<JsonProxySettingsStore>? logger = null) : IProxySettingsStore
{
    private readonly string _path = path ?? AppPaths.ProxySettingsFile;
    private readonly ILogger<JsonProxySettingsStore> _logger = logger ?? NullLogger<JsonProxySettingsStore>.Instance;

    public ProxySettings Load()
    {
        if (!File.Exists(_path))
        {
            return ProxySettings.Default;
        }

        try
        {
            // WithKnownMode：文件里写了个数字（或以后删掉某个枚举项）时，模式会读出一个
            // 未定义的值，这里收口成直连。见 ProxySettings.WithKnownMode 的说明。
            return (JsonSerializer.Deserialize(
                File.ReadAllBytes(_path),
                ProxySettingsJsonContext.Default.ProxySettings) ?? ProxySettings.Default).WithKnownMode();
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            // 坏文件回落到直连：那是隔离性最好的一档，也绝不会把请求送到一个读不出来的代理上。
            _logger.LogWarning(exception, "代理偏好读取失败，按直连继续：{Path}", _path);
            return ProxySettings.Default;
        }
    }

    public bool Save(ProxySettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        try
        {
            var directory = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var bytes = JsonSerializer.SerializeToUtf8Bytes(
                settings, ProxySettingsJsonContext.Default.ProxySettings);
            var temporary = _path + ".tmp";
            File.WriteAllBytes(temporary, bytes);
            File.Move(temporary, _path, overwrite: true);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(exception, "代理偏好写入失败：{Path}", _path);
            return false;
        }
    }
}
