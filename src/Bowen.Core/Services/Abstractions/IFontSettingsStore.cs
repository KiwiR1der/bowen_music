using Bowen.Core.Models;

namespace Bowen.Core.Services.Abstractions;

/// <summary>
/// 界面字体偏好的本地记忆。
/// </summary>
/// <remarks>
/// <b>接口是同步的</b>，与 <see cref="IThemeSettingsStore"/> 同一个理由：
/// 字体必须在第一帧渲染前定下来，异步读会先闪一下默认字体再切。
/// </remarks>
public interface IFontSettingsStore
{
    /// <summary>读上次的选择。没有记录或记录不可用时返回 <see cref="FontSettings.Default"/>。</summary>
    FontSettings Load();

    /// <summary>写回当前选择。写失败只记日志，不抛。</summary>
    void Save(FontSettings settings);
}
