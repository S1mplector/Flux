using System;
using System.Threading;
using System.Threading.Tasks;
using Equalizer.Application.Abstractions;
using Equalizer.Application.Audio;

namespace Equalizer.Infrastructure.Audio;

public sealed class RandomAudioInput : IAudioInputPort
{
    private readonly Random _rng = new();
    private double _phase;

    public Task<AudioFrame> ReadFrameAsync(int minSamples, CancellationToken cancellationToken)
    {
        int sampleRate = 48000;
        int n = Math.Max(1024, minSamples);
        var samples = new float[n];
        // Generate a simple musical-like waveform: two sines + noise
        double f1 = 220.0; // A3
        double f2 = 440.0; // A4
        double dt = 1.0 / sampleRate;
        for (int i = 0; i < n; i++)
        {
            _phase += dt;
            double v = 0.6 * Math.Sin(2 * Math.PI * f1 * _phase)
                     + 0.4 * Math.Sin(2 * Math.PI * f2 * _phase)
                     + ( _rng.NextDouble() - 0.5) * 0.1;
            samples[i] = (float)Math.Clamp(v * 0.5, -1.0, 1.0);
        }
        return Task.FromResult(new AudioFrame(samples, sampleRate));
    }
}
