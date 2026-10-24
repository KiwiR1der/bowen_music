using Bowen.Core.Services.Abstractions;

namespace Bowen.Core.Tests.Support;

/// <summary>固定设备标识，避免测试去读真实的 <c>%LOCALAPPDATA%\Bowen\devid.txt</c>。</summary>
internal sealed class FakeDeviceIdentity(string value = "0123456789abcdef0123456789abcdef") : IDeviceIdentity
{
    public string Value { get; } = value;
}
