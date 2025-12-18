using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Equalizer.Application.Audio;

namespace Equalizer.Application.Abstractions;

public interface IAudioInputPort
{
    Task<AudioFrame> ReadFrameAsync(int minSamples, CancellationToken cancellationToken);
}

public record AudioDeviceInfo(string Id, string Name, bool IsDefault);

public interface IAudioDeviceProvider
{
    IReadOnlyList<AudioDeviceInfo> GetOutputDevices();
}
