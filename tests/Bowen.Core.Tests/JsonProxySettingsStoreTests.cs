using Bowen.Core.Models;
using Bowen.Core.Services.Implementations;
using Xunit;

namespace Bowen.Core.Tests;

public sealed class JsonProxySettingsStoreTests
{
    private static string NewPath() => Path.Combine(Path.GetTempPath(), "bowen-proxy-tests",
        Guid.NewGuid().ToString("N"), "proxy.json");

    [Fact]
    public void MissingFile_DefaultsToDirect_WithoutCreatingAFile()
    {
        var path = NewPath();

        var loaded = new JsonProxySettingsStore(path).Load();

        // 默认直连是这次改动的意义所在，这条防止默认值被人改回去。
        Assert.Equal(ProxyMode.Direct, loaded.Mode);
        Assert.Empty(loaded.Address);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void CustomAddress_RoundTrips()
    {
        var path = NewPath();
        var store = new JsonProxySettingsStore(path);

        Assert.True(store.Save(new ProxySettings(ProxyMode.Custom, "http://127.0.0.1:7890")));

        var loaded = store.Load();
        Assert.Equal(ProxyMode.Custom, loaded.Mode);
        Assert.Equal("http://127.0.0.1:7890", loaded.Address);
    }

    [Fact]
    public void Mode_IsStoredByNameNotByNumber()
    {
        // 存成 "Custom" 而不是 2：将来枚举顺序变了也不会把用户的设置读错。
        var path = NewPath();
        new JsonProxySettingsStore(path).Save(new ProxySettings(ProxyMode.System));

        Assert.Contains("\"System\"", File.ReadAllText(path), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("{broken")]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("""{"Mode":"Nonsense"}""")]
    [InlineData("""{"Mode":7}""")]
    public void InvalidPreference_FallsBackToDirectAndPreservesFile(string json)
    {
        var path = NewPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, json);

        // 坏文件回落到直连：隔离性最好的一档，也绝不会把请求送到一个读不出来的代理上。
        Assert.Equal(ProxyMode.Direct, new JsonProxySettingsStore(path).Load().Mode);
        Assert.Equal(json, File.ReadAllText(path));
    }

    [Fact]
    public void SaveFailure_IsReported()
    {
        var path = NewPath();
        Directory.CreateDirectory(path);

        Assert.False(new JsonProxySettingsStore(path).Save(ProxySettings.Default));
    }
}
