using Bodian.Core.Models;
using Bodian.Core.Services;
using Bodian.Core.Services.Abstractions;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Bodian.WinUI.ViewModels;

/// <summary>
/// 设置页「网络」分区：更新通道走哪条路。
/// </summary>
/// <remarks>
/// <para>
/// <b>只管更新检查那一条路。</b> 常规请求（登录、歌单、封面）固定直连，没有开关 ——
/// 见 <c>docs/proxy.md</c> §3。
/// </para>
/// <para>
/// <b>真实状态在 <see cref="UpdateProxy"/> 里</b>（它同时是 <c>IWebProxy</c>，每个请求都问它一次），
/// 这里只是它的读写窗口：初值读它、改动写回它并落盘。另存一份状态就会多出一处要对齐的东西 ——
/// 与设置页「主题、播放、歌词各由单例持有、这一页只转发」是同一条规矩。
/// </para>
/// </remarks>
public sealed partial class NetworkSettingsViewModel : ObservableObject
{
    /// <summary>下拉框的顺序即下标，与 <see cref="ProxyMode"/> 的对应关系由 <see cref="Modes"/> 定。</summary>
    private static readonly ProxyMode[] Modes = [ProxyMode.Direct, ProxyMode.System, ProxyMode.Custom];

    private readonly UpdateProxy _proxy;
    private readonly IProxySettingsStore _store;

    /// <summary>构造函数还在填初值。填的时候不写回、也不落盘。</summary>
    private bool _loaded;

    public NetworkSettingsViewModel(UpdateProxy proxy, IProxySettingsStore store)
    {
        ArgumentNullException.ThrowIfNull(proxy);
        ArgumentNullException.ThrowIfNull(store);

        _proxy = proxy;
        _store = store;

        // 从代理对象读，不重新读盘：它才是当前真正在用的那一份，而且 App 注册时已经从盘上读过一次。
        var current = _proxy.Current;
        ProxyModeIndex = IndexOf(current.Mode);
        ProxyAddress = current.Address;
        _loaded = true;

        RefreshStatus();
    }

    public IReadOnlyList<string> ProxyModeOptions { get; } = ["不使用代理", "跟随系统", "自定义"];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCustom))]
    public partial int ProxyModeIndex { get; set; }

    /// <summary>
    /// 自定义代理的地址。
    /// </summary>
    /// <remarks>
    /// <b>界面上绑的是「失焦才写回」</b>（不写 <c>UpdateSourceTrigger</c>，默认就是 LostFocus），
    /// 否则每敲一个字都会走一遍解析与落盘。
    /// </remarks>
    [ObservableProperty]
    public partial string ProxyAddress { get; set; } = "";

    /// <summary>地址那一行是否可用。只有选了「自定义」才有意义。</summary>
    public bool IsCustom => Modes[SafeIndex()] == ProxyMode.Custom;

    /// <summary>当前是什么状态，一句话。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatus))]
    public partial string StatusText { get; set; } = "";

    /// <summary>地址写错时的说明。<b>只在选「自定义」且地址不可用时非空。</b></summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string ErrorText { get; set; } = "";

    public bool HasStatus => StatusText.Length > 0;

    public bool HasError => ErrorText.Length > 0;

    partial void OnProxyModeIndexChanged(int value)
    {
        if (!_loaded || value < 0 || value >= Modes.Length)
        {
            return;
        }

        // 换模式时把地址一起带上：从「自定义」切走再切回来，原来那个地址还在。
        Apply(_proxy.Current with { Mode = Modes[value] });
    }

    partial void OnProxyAddressChanged(string value)
    {
        if (!_loaded)
        {
            return;
        }

        Apply(_proxy.Current with { Address = value });
    }

    /// <remarks>
    /// <b>先写进代理对象、再落盘。</b> 顺序反过来的话，「落盘成功但设置没生效」会留下一份
    /// 与运行状态不一致的文件；而这个设置本来就不该因为写盘失败而不生效。
    /// </remarks>
    private void Apply(ProxySettings next)
    {
        _proxy.Apply(next);
        _store.Save(next);
        RefreshStatus();
    }

    private void RefreshStatus()
    {
        var settings = _proxy.Current;

        switch (settings.Mode)
        {
            case ProxyMode.System:
                StatusText = "更新检查跟随系统代理。系统代理改过之后，重启应用最稳妥。";
                ErrorText = "";
                break;

            case ProxyMode.Custom:
                if (settings.TryGetCustomUri(out var uri, out var error))
                {
                    StatusText = $"更新检查将走 {uri}";
                    ErrorText = "";
                }
                else
                {
                    // 说清楚「现在实际发生了什么」，不要只说地址错 —— 用户要的是「那我的更新还能不能查」。
                    StatusText = "地址不可用时，更新检查仍然直连。";
                    ErrorText = error;
                }

                break;

            default:
                StatusText = "更新检查不使用代理。国内直连通常连不上 GitHub。";
                ErrorText = "";
                break;
        }
    }

    /// <summary>下标越界时退回直连（绑定刚建好时会先送一个 -1 进来）。</summary>
    private int SafeIndex() => Math.Clamp(ProxyModeIndex, 0, Modes.Length - 1);

    private static int IndexOf(ProxyMode mode)
    {
        var index = Array.IndexOf(Modes, mode);
        return index >= 0 ? index : 0;
    }
}
