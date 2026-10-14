using System.Net;
using Bodian.Core.Models;

namespace Bodian.Core.Services;

/// <summary>
/// 更新通道的代理：既是一个 <see cref="IWebProxy"/>，也是当前设置的持有者。
/// </summary>
/// <remarks>
/// <para>
/// <b>为什么合成一个类。</b> <see cref="SocketsHttpHandler"/> 拿着的是这个对象**本身**
/// （<c>Proxy = this</c>），每个请求都来问一次 <see cref="GetProxy"/>；设置一变，
/// 下一个请求立刻按新的走，不需要重建 handler、也不需要重建任何服务。
/// 界面那边要读的「当前设置」正好就是同一份值，所以不再另立一个持有者 ——
/// 那两个职责是同一件事的两面。
/// </para>
/// <para>
/// <b>只有更新通道用它。</b> 业务请求（API、封面）走 <c>BodianHttpTransport.CreateHandler</c>，
/// 那条路固定直连、根本没有代理对象。见 <c>docs/proxy.md</c>。
/// </para>
/// <para>
/// <b>线程安全靠一次快照交换。</b> <see cref="Apply"/> 在 UI 线程上被调，
/// <see cref="GetProxy"/> 在线程池线程上被调；设置、解析好的地址与凭据三者必须配套，
/// 所以打包进一个不可变对象，只留一个 <c>volatile</c> 字段做整体替换。
/// </para>
/// </remarks>
public sealed class UpdateProxy : IWebProxy, IDisposable
{
    private readonly IWebProxy _system;

    /// <summary>当前这一份解析结果。整体替换，不逐字段改。见类型注释。</summary>
    private volatile Resolution _current;

    private SocketsHttpHandler? _handler;

    public UpdateProxy(ProxySettings settings, IWebProxy? systemProxy = null)
    {
        ArgumentNullException.ThrowIfNull(settings);

        // 跟随系统那一路直接交给 .NET 自己那套（即改动之前的行为），我们不重造一份判定。
        _system = systemProxy ?? HttpClient.DefaultProxy;
        _current = Resolve(settings);
    }

    /// <summary>当前设置。界面读它、别处不该写它（写入口是 <see cref="Apply"/>）。</summary>
    public ProxySettings Current => _current.Settings;

    /// <summary>
    /// 代理凭据。**由本类自己按设置填**（自定义模式从地址里的 userinfo 解析，
    /// 跟随系统模式用系统那一份），外面不需要写它 —— 实现这个 setter 只是因为
    /// <see cref="IWebProxy"/> 要求。
    /// </summary>
    public ICredentials? Credentials
    {
        get => _current.Credentials;
        set => _current = _current with { Credentials = value };
    }

    /// <summary>换一份设置。下一个请求就按新的走。</summary>
    public void Apply(ProxySettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        _current = Resolve(settings);
    }

    /// <summary>这一跳走不走代理、走哪个。返回 <c>null</c> 即直连。</summary>
    public Uri? GetProxy(Uri destination)
    {
        ArgumentNullException.ThrowIfNull(destination);

        var snapshot = _current;

        return snapshot.Settings.Mode switch
        {
            ProxyMode.Direct => null,
            ProxyMode.System => _system.GetProxy(destination),

            // 地址还没填、或写得不合法时是 null —— 直连。**方向是刻意的**：
            // 宁可这一次检查超时，也不要把请求发到一个解析不出来的地址上。
            ProxyMode.Custom => snapshot.Custom,
            _ => null,
        };
    }

    /// <summary>与 <see cref="GetProxy"/> 同一套判定，两者不能各说各话。</summary>
    public bool IsBypassed(Uri host) => GetProxy(host) is null;

    /// <summary>
    /// 更新通道专用的 handler。第一次取到时建，之后是同一个。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b><c>UseProxy = true</c> 是必须写的。</b> 不写这一句，<c>UseProxy</c> 是默认的 <c>true</c>，
    /// 而 <c>Proxy</c> 为 null 时会落回系统代理 —— 那正是本次要修的那个 bug
    /// （见 <c>docs/proxy.md</c> §1）。写成显式的，读的人一眼能看出「这里由 <see cref="GetProxy"/> 决定」。
    /// </para>
    /// <para>
    /// <b>由本类持有并释放。</b> 它原来是容器里注册的共享单例、退出时由容器统一 Dispose；
    /// 换成这里现建之后，归属就落到本类头上 —— 交给容器释放才不会有漏掉的那一个。
    /// </para>
    /// </remarks>
    public SocketsHttpHandler Handler => _handler ??= new SocketsHttpHandler
    {
        AutomaticDecompression = DecompressionMethods.All,
        UseProxy = true,
        Proxy = this,

        // 与业务那条 handler 取同一个值：代理换了之后，旧连接不该继续被复用。
        PooledConnectionLifetime = TimeSpan.FromMinutes(2),
    };

    public void Dispose() => _handler?.Dispose();

    private Resolution Resolve(ProxySettings settings)
    {
        if (settings.Mode == ProxyMode.Custom && settings.TryGetCustomUri(out var uri, out _))
        {
            // ★ 把 userinfo 从地址里剥掉、单独交给 Credentials：带 userinfo 的地址交给
            //   SocketsHttpHandler 做端点时，用户名密码有可能被带进 CONNECT 请求行。
            var endpoint = new UriBuilder(uri!) { UserName = "", Password = "" }.Uri;
            return new Resolution(settings, endpoint, CredentialsFrom(uri!));
        }

        if (settings.Mode == ProxyMode.System)
        {
            return new Resolution(settings, null, _system.Credentials);
        }

        return new Resolution(settings, null, null);
    }

    private static NetworkCredential? CredentialsFrom(Uri uri)
    {
        var userInfo = uri.UserInfo;
        if (userInfo.Length == 0)
        {
            return null;
        }

        var separator = userInfo.IndexOf(':');

        return separator < 0
            ? new NetworkCredential(Uri.UnescapeDataString(userInfo), "")
            : new NetworkCredential(
                Uri.UnescapeDataString(userInfo[..separator]),
                Uri.UnescapeDataString(userInfo[(separator + 1)..]));
    }

    /// <summary>一份自洽的解析结果。见 <see cref="_current"/> 的说明。</summary>
    private sealed record Resolution(ProxySettings Settings, Uri? Custom, ICredentials? Credentials);
}
