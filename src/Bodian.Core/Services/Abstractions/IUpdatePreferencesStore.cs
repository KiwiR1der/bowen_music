using Bodian.Core.Models;

namespace Bodian.Core.Services.Abstractions;

/// <summary>
/// 更新偏好的读写（自动检查开关、已跳过的版本号）。
/// </summary>
/// <remarks>
/// <b>与凭据无关，明文 JSON</b>：里面没有账号标识，也没有任何能换算出访问能力的东西。
/// </remarks>
public interface IUpdatePreferencesStore
{
    /// <summary>读一次。<b>不抛</b>：文件缺失或坏了都回落到 <see cref="UpdatePreferences.Default"/>。</summary>
    UpdatePreferences Load();

    /// <summary>写一次。返回值表示是否落盘成功，失败不抛。</summary>
    bool Save(UpdatePreferences preferences);
}
