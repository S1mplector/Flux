using System;
using System.Threading;
using System.Threading.Tasks;
using Equalizer.Application.Abstractions;
using Equalizer.Application.Services;

namespace Equalizer.Application.Services;

public sealed class EqualizerService : IEqualizerService
{
    private readonly IAudioInputPort _audio;
    private readonly ISettingsPort _settings;
    private readonly SpectrumProcessor _processor;
    private float[]? _previous;

    public EqualizerService(IAudioInputPort audio, ISettingsPort settings, SpectrumProcessor processor)
    {
        _audio = audio;
        _settings = settings;
        _processor = processor;
    }

    public async Task<float[]> GetBarsAsync(CancellationToken cancellationToken)
    {
        var settings = await _settings.GetAsync();
        var frame = await _audio.ReadFrameAsync(minSamples: 4096, cancellationToken);
        var raw = _processor.ComputeBars(frame, settings.BarsCount);

        if (_previous == null || _previous.Length != raw.Length)
            _previous = new float[raw.Length];

        var output = new float[raw.Length];
        var smoothing = Math.Clamp(settings.Smoothing, 0.0, 1.0);
        var responsiveness = Math.Clamp(settings.Responsiveness, 0.0, 1.0);

        for (int i = 0; i < raw.Length; i++)
        {
            var v = raw[i] * (float)(0.5 + responsiveness * 0.5); // scale 0.5..1.0
            v = Math.Clamp(v, 0f, 1f);

            var smoothed = (float)(smoothing * _previous[i] + (1.0 - smoothing) * v);
            output[i] = smoothed;
            _previous[i] = smoothed;
        }

        return output;
    }
}
