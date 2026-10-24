using Bowen.Core.Models;
namespace Bowen.Core.Services.Abstractions;
public interface IAudioQualitySettingsStore
{
    AudioQuality Load();
    void Save(AudioQuality quality);
}
