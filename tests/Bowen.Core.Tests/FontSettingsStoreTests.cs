using Bowen.Core.Models;
using Bowen.Core.Services.Implementations;
using Xunit;

namespace Bowen.Core.Tests;

public sealed class FontSettingsStoreTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "bowen-font-settings-tests", Guid.NewGuid().ToString("N"));

    private string Path_ => Path.Combine(_directory, "font-settings.json");

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }

    [Fact]
    public void RoundTripsFontFamily()
    {
        var store = new JsonFontSettingsStore(Path_);
        store.Save(new FontSettings { FontFamily = "Microsoft YaHei UI" });

        Assert.Equal("Microsoft YaHei UI", new JsonFontSettingsStore(Path_).Load().FontFamily);
    }

    [Fact]
    public void MissingFontNameFallsBackToSystemDefault()
    {
        var loaded = new FontSettings { FontFamily = "  " }.Normalized();

        Assert.Equal(string.Empty, loaded.FontFamily);
    }
}
