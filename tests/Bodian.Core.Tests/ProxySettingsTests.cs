using System.Net;
using Bodian.Core.Models;
using Bodian.Core.Services;
using Xunit;

namespace Bodian.Core.Tests;

/// <summary>
/// 代理地址的校验与那一套判定。**纯函数，零网络。**
/// </summary>
public sealed class ProxySettingsTests
{
    // ── 默认值 ─────────────────────────────────────────────────────────────

    [Fact]
    public void Default_IsDirect()
    {
        // 默认直连是这次改动的全部意义：应用开箱即与系统代理隔离。
        // 这条防止有人把默认值改回「跟随系统」。
        Assert.Equal(ProxyMode.Direct, ProxySettings.Default.Mode);
        Assert.Empty(ProxySettings.Default.Address);
    }

    // ── 地址校验 ───────────────────────────────────────────────────────────

    [Theory]
    [InlineData("http://127.0.0.1:7890")]
    [InlineData("https://proxy.example.com:8443")]
    [InlineData("socks5://127.0.0.1:1080")]
    [InlineData("socks4://127.0.0.1:1080")]
    [InlineData("http://user:pass@127.0.0.1:7890")]
    [InlineData("  http://127.0.0.1:7890  ")]     // 前后空白由我们自己剥
    public void Custom_AcceptsUsableAddresses(string address)
    {
        var settings = new ProxySettings(ProxyMode.Custom, address);

        Assert.True(settings.TryGetCustomUri(out var uri, out var error));
        Assert.NotNull(uri);
        Assert.Empty(error);
    }

    [Theory]
    [InlineData("", "还没有填")]
    [InlineData("   ", "还没有填")]
    [InlineData("127.0.0.1:7890", "写得不对")]        // 少了协议
    [InlineData("http://", "写得不对")]
    [InlineData("ftp://127.0.0.1:2121", "不支持的协议")]
    public void Custom_RejectsBadAddresses(string address, string expectedFragment)
    {
        var settings = new ProxySettings(ProxyMode.Custom, address);

        Assert.False(settings.TryGetCustomUri(out var uri, out var error));
        Assert.Null(uri);

        // 给用户看的永远是人话，而且要说清楚是哪不对。
        Assert.Contains(expectedFragment, error, StringComparison.Ordinal);
    }

    [Fact]
    public void Custom_RequiresAnExplicitPort()
    {
        // 漏写端口是最常见的一种写错。Uri.Port 对 http 会给默认的 80，
        // 所以「没写端口」这件事只能看原串 —— 这里就是钉住那个判据。
        var settings = new ProxySettings(ProxyMode.Custom, "http://127.0.0.1");

        Assert.False(settings.TryGetCustomUri(out _, out var error));
        Assert.Contains("端口", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Custom_AcceptsAnExplicitDefaultPort()
    {
        // http://host:80 在 Uri 眼里和没写端口一样（IsDefaultPort 为真），
        // 但用户确实写了。别误拒。
        var settings = new ProxySettings(ProxyMode.Custom, "http://127.0.0.1:80");

        Assert.True(settings.TryGetCustomUri(out _, out _));
    }

    // ── 那一套判定 ─────────────────────────────────────────────────────────

    [Fact]
    public void Direct_NeverUsesAProxy()
    {
        var proxy = new UpdateProxy(new ProxySettings(ProxyMode.Direct, "http://127.0.0.1:7890"));

        // 直连时地址填了也不作数 —— 判定只看模式。
        Assert.Null(proxy.GetProxy(new Uri("https://api.github.com/")));
        Assert.True(proxy.IsBypassed(new Uri("https://api.github.com/")));
    }

    [Fact]
    public void Custom_ReturnsTheConfiguredEndpoint()
    {
        var proxy = new UpdateProxy(new ProxySettings(ProxyMode.Custom, "http://127.0.0.1:7890"));

        Assert.Equal("http://127.0.0.1:7890/", proxy.GetProxy(new Uri("https://api.github.com/"))!.AbsoluteUri);
        Assert.False(proxy.IsBypassed(new Uri("https://api.github.com/")));
    }

    [Fact]
    public void Custom_WithAnInvalidAddressFallsBackToDirect()
    {
        var proxy = new UpdateProxy(new ProxySettings(ProxyMode.Custom, "写错了"));

        // 方向是刻意的：宁可这一次检查超时，也不要把请求发到一个解析不出来的地址上。
        Assert.Null(proxy.GetProxy(new Uri("https://api.github.com/")));
    }

    [Fact]
    public void Custom_KeepsCredentialsOutOfTheEndpoint()
    {
        var proxy = new UpdateProxy(
            new ProxySettings(ProxyMode.Custom, "http://alice:s3cret@127.0.0.1:7890"));

        var endpoint = proxy.GetProxy(new Uri("https://api.github.com/"))!;

        // 带 userinfo 的地址交给 SocketsHttpHandler 做端点时，用户名密码有可能被带进
        // CONNECT 请求行 —— 所以端点里必须是剥干净的，凭据单独走 Credentials。
        Assert.DoesNotContain("alice", endpoint.AbsoluteUri, StringComparison.Ordinal);
        Assert.DoesNotContain("s3cret", endpoint.AbsoluteUri, StringComparison.Ordinal);

        var credential = Assert.IsType<NetworkCredential>(proxy.Credentials);
        Assert.Equal("alice", credential.UserName);
        Assert.Equal("s3cret", credential.Password);
    }

    [Fact]
    public void Custom_WithoutUserInfoHasNoCredentials()
    {
        var proxy = new UpdateProxy(new ProxySettings(ProxyMode.Custom, "http://127.0.0.1:7890"));

        Assert.Null(proxy.Credentials);
    }

    [Fact]
    public void Custom_DecodesEscapedCredentials()
    {
        // 地址里那个 @ 必须写成 %40，否则整个地址解析不出来。解码要还回原样。
        var proxy = new UpdateProxy(
            new ProxySettings(ProxyMode.Custom, "http://alice%40corp:p%40ss@127.0.0.1:7890"));

        var credential = Assert.IsType<NetworkCredential>(proxy.Credentials);
        Assert.Equal("alice@corp", credential.UserName);
        Assert.Equal("p@ss", credential.Password);
    }

    [Fact]
    public void System_DelegatesToTheSystemProxy()
    {
        var system = new RecordingProxy(new Uri("http://system.invalid:3128/"));
        var proxy = new UpdateProxy(new ProxySettings(ProxyMode.System), system);

        Assert.Equal(
            "http://system.invalid:3128/",
            proxy.GetProxy(new Uri("https://api.github.com/"))!.AbsoluteUri);
    }

    [Fact]
    public void System_ForwardsTheSystemCredentials()
    {
        var expected = new NetworkCredential("svc", "pw");
        var system = new RecordingProxy(null) { Credentials = expected };
        var proxy = new UpdateProxy(new ProxySettings(ProxyMode.System), system);

        Assert.Same(expected, proxy.Credentials);
    }

    [Fact]
    public void Apply_SwitchesBehaviourWithoutRebuildingAnything()
    {
        var proxy = new UpdateProxy(new ProxySettings(ProxyMode.Direct));

        Assert.Null(proxy.GetProxy(new Uri("https://api.github.com/")));

        proxy.Apply(new ProxySettings(ProxyMode.Custom, "http://127.0.0.1:7890"));
        Assert.NotNull(proxy.GetProxy(new Uri("https://api.github.com/")));
        Assert.Equal("http://127.0.0.1:7890", proxy.Current.Address);

        proxy.Apply(ProxySettings.Default);
        Assert.Null(proxy.GetProxy(new Uri("https://api.github.com/")));
    }

    // ── handler ────────────────────────────────────────────────────────────

    [Fact]
    public void Handler_PointsAtItselfAndKeepsGzip()
    {
        using var proxy = new UpdateProxy(ProxySettings.Default);

        // UseProxy 必须是真的：不设的话默认也是 true，但那时代理会落回系统 —— 见 Handler 的说明。
        Assert.True(proxy.Handler.UseProxy);
        Assert.Same(proxy, proxy.Handler.Proxy);
        Assert.Equal(DecompressionMethods.All, proxy.Handler.AutomaticDecompression);
    }

    [Fact]
    public void Handler_IsCreatedOnceAndDisposedWithTheProxy()
    {
        var proxy = new UpdateProxy(ProxySettings.Default);

        // 取两次是同一个：归属在本类，不能每次现建一个把旧的漏掉。
        Assert.Same(proxy.Handler, proxy.Handler);

        // 容器释放本单例时要连带把它释放掉；重复释放不该炸。
        proxy.Dispose();
        proxy.Dispose();
    }

    [Fact]
    public void Dispose_WithoutEverTouchingTheHandler_IsFine()
    {
        // 默认直连的机器上根本不会去建 handler，这条路不能因为 _handler 是 null 就炸。
        new UpdateProxy(ProxySettings.Default).Dispose();
    }

    /// <summary>可控的系统代理桩件。</summary>
    private sealed class RecordingProxy(Uri? endpoint) : IWebProxy
    {
        public ICredentials? Credentials { get; set; }

        public Uri? GetProxy(Uri destination) => endpoint;

        public bool IsBypassed(Uri host) => endpoint is null;
    }
}
