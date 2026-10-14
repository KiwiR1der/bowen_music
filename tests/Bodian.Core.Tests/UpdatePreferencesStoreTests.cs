using Bodian.Core.Models;
using Bodian.Core.Services.Implementations;
using Xunit;

namespace Bodian.Core.Tests;

public sealed class UpdatePreferencesStoreTests
{
    private static string NewPath() => Path.Combine(Path.GetTempPath(), "bodian-update-prefs-tests",
        Guid.NewGuid().ToString("N"), "update.json");

    [Fact]
    public void MissingFile_DefaultsToAutoCheckOn_WithoutCreatingAFile()
    {
        var path = NewPath();

        var loaded = new JsonUpdatePreferencesStore(path).Load();

        // 默认关掉的「检查更新」等于没有 —— 这条是防止默认值被改掉。
        Assert.True(loaded.AutoCheck);
        Assert.Empty(loaded.SkippedVersion);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void AutoCheckOffAndSkippedVersion_RoundTrip()
    {
        var path = NewPath();
        var store = new JsonUpdatePreferencesStore(path);

        Assert.True(store.Save(new UpdatePreferences(AutoCheck: false, SkippedVersion: "0.2.0")));

        var loaded = store.Load();
        Assert.False(loaded.AutoCheck);
        Assert.Equal("0.2.0", loaded.SkippedVersion);
    }

    [Theory]
    [InlineData("{broken")]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("""{"AutoCheck":"yes"}""")]
    public void InvalidPreference_FallsBackToDefaultsAndPreservesFile(string json)
    {
        var path = NewPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, json);

        var loaded = new JsonUpdatePreferencesStore(path).Load();

        // 坏文件回落到「开着、没跳过任何版本」：宁可多查一次，
        // 也不要因为一个坏字节让用户从此收不到更新提示。
        Assert.True(loaded.AutoCheck);
        Assert.Empty(loaded.SkippedVersion);
        Assert.Equal(json, File.ReadAllText(path));
    }

    [Fact]
    public void SaveFailure_IsReported()
    {
        var path = NewPath();
        Directory.CreateDirectory(path);

        Assert.False(new JsonUpdatePreferencesStore(path).Save(new UpdatePreferences(AutoCheck: false)));
    }
}
