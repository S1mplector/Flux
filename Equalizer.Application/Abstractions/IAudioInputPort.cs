using System.Threading;
using System.Threading.Tasks;
using Equalizer.Application.Audio;

namespace Equalizer.Application.Abstractions;

public interface IAudioInputPort
{
    Task<AudioFrame> ReadFrameAsync(int minSamples, CancellationToken cancellationToken);
}
