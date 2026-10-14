using Bodian.Core.Models;

namespace Bodian.Core.Services.Abstractions;

/// <summary>
/// 更新通道的代理偏好读写。
/// </summary>
/// <remarks>
/// <b>明文 JSON</b>。地址里可能带密码（用户自己填的），从简的理由见 <see cref="ProxySettings"/>。
/// </remarks>
public interface IProxySettingsStore
{
    /// <summary>读一次。<b>不抛</b>：文件缺失或坏了都回落到 <see cref="ProxySettings.Default"/>（直连）。</summary>
    ProxySettings Load();

    /// <summary>写一次。返回值表示是否落盘成功，失败不抛。</summary>
    bool Save(ProxySettings settings);
}
