using System;
using System.Threading;
using System.Threading.Tasks;
using Equalizer.Application.Abstractions;
using Equalizer.Application.Services;
using Equalizer.Application.Models;
using Equalizer.Domain;

namespace Equalizer.Application.Services;

public sealed class EqualizerService : IEqualizerService
{
    private readonly IAudioInputPort _audio;
    private readonly ISettingsPort _settings;
    private readonly SpectrumProcessor _processor;
    private float[]? _previous;
    private double[]? _prevMag;
    private readonly double[] _fluxHistory = new double[64];
    private int _fluxIndex;
    private int _fluxCount;
    private readonly object _frameLock = new();
    private Task<VisualizerFrame>? _inFlight;
    private VisualizerFrame? _lastFrameCache;
    private DateTime _lastFrameAt;
    private double _silenceFade = 1.0; // 1=fully visible, 0=fully faded
    private DateTime _lastBeatAt = DateTime.MinValue;

    public EqualizerService(IAudioInputPort audio, ISettingsPort settings, SpectrumProcessor processor)
    {
        _audio = audio;
        _settings = settings;
        _processor = processor;
    }

    public async Task<float[]> GetBarsAsync(CancellationToken cancellationToken)
    {
        var vf = await GetVisualizerFrameAsync(cancellationToken);
        return vf.Bars;
    }

    public async Task<VisualizerFrame> GetVisualizerFrameAsync(CancellationToken cancellationToken)
    {
        var settings = await _settings.GetAsync();
        var now = DateTime.UtcNow;
        var minIntervalMs = 1000.0 / Math.Clamp(settings.TargetFps, 10, 240);
        if (_lastFrameCache != null && (now - _lastFrameAt).TotalMilliseconds < minIntervalMs)
        {
            return _lastFrameCache;
        }

        Task<VisualizerFrame>? task = null;
        lock (_frameLock)
        {
            if (_inFlight != null && !_inFlight.IsCompleted)
            {
                task = _inFlight;
            }
            else
            {
                _inFlight = ComputeFrameInternalAsync(settings, cancellationToken);
                task = _inFlight;
            }
        }

        var vf = await task;
        _lastFrameCache = vf;
        _lastFrameAt = DateTime.UtcNow;
        return vf;
    }

    private async Task<VisualizerFrame> ComputeFrameInternalAsync(EqualizerSettings settings, CancellationToken cancellationToken)
    {
        try
        {
            int minSamples;
            if (settings.Smoothing <= 0.3 && settings.TargetFps >= 120)
            {
                // Low-latency profile: smaller window for faster reaction
                minSamples = 512;
            }
            else if (settings.Smoothing >= 0.7 && settings.TargetFps <= 60)
            {
                // Smooth profile: larger window for more stable spectrum
                minSamples = 2048;
            }
            else
            {
                minSamples = 1024;
            }

            var audioFrame = await _audio.ReadFrameAsync(minSamples, cancellationToken);

        // Silence detection to prevent backlog-looking playback after pause
        double rms = 0;
        var samps = audioFrame.Samples;
        for (int i = 0; i < samps.Length; i++) { double v = samps[i]; rms += v * v; }
        rms = samps.Length > 0 ? Math.Sqrt(rms / samps.Length) : 0;
        bool isSilent = rms < 1e-3; // treat very low-level noise as silence for fading purposes

        double[] mag;
        float[] rawBars;
        if (isSilent)
        {
            mag = _prevMag is { Length: > 0 } ? new double[_prevMag.Length] : Array.Empty<double>();

            if (settings.FadeOnSilenceEnabled && _previous != null && _previous.Length == settings.BarsCount)
            {
                // Keep the last visible shape and let SilenceFade drive the visual fade-out
                rawBars = (float[])_previous.Clone();
            }
            else
            {
                // Legacy behaviour: decay to zero using smoothing
                rawBars = new float[settings.BarsCount];
            }
        }
        else
        {
            // Spectrum and bars
            mag = _processor.ComputeMagnitudes(audioFrame);
            rawBars = _processor.ComputeBarsFromMagnitudes(mag, audioFrame.SampleRate, settings.BarsCount);
        }

        // Apply user-controlled per-band emphasis (bass/treble) to the bar data.
        if (!isSilent && rawBars.Length > 0)
        {
            double bassEmphasis = Math.Clamp(settings.BassEmphasis, 0.0, 2.0);
            double trebleEmphasis = Math.Clamp(settings.TrebleEmphasis, 0.0, 2.0);

            if (Math.Abs(bassEmphasis - 1.0) > 1e-3 || Math.Abs(trebleEmphasis - 1.0) > 1e-3)
            {
                int n = rawBars.Length;
                double bassRegion = 0.45;   // lower ~45% of bars
                double trebleRegion = 0.45; // upper ~45% of bars

                for (int i = 0; i < n; i++)
                {
                    double t = n > 1 ? (double)i / (n - 1) : 0.0; // 0=lowest freq bar, 1=highest

                    double bassWeight = 1.0;
                    if (bassRegion > 0 && t < bassRegion)
                    {
                        double alpha = 1.0 - t / bassRegion; // strongest at very low bars
                        bassWeight = 1.0 + (bassEmphasis - 1.0) * alpha;
                    }

                    double trebleWeight = 1.0;
                    if (trebleRegion > 0 && t > 1.0 - trebleRegion)
                    {
                        double alpha = (t - (1.0 - trebleRegion)) / trebleRegion; // strongest at very high bars
                        trebleWeight = 1.0 + (trebleEmphasis - 1.0) * alpha;
                    }

                    double w = bassWeight * trebleWeight;
                    rawBars[i] = (float)Math.Clamp(rawBars[i] * w, 0.0, 1.0);
                }
            }
        }

        if (_previous == null || _previous.Length != rawBars.Length)
            _previous = new float[rawBars.Length];

        var output = new float[rawBars.Length];
        var smoothing = Math.Clamp(settings.Smoothing, 0.0, 1.0);
        if (isSilent && !settings.FadeOnSilenceEnabled)
        {
            // When not using explicit fade, still decay bars a bit faster on silence
            smoothing = Math.Min(smoothing, 0.2);
        }
        var responsiveness = Math.Clamp(settings.Responsiveness, 0.0, 1.0);
        for (int i = 0; i < rawBars.Length; i++)
        {
            var v = rawBars[i] * (float)(0.5 + responsiveness * 0.5);
            v = Math.Clamp(v, 0f, 1f);
            var smoothed = (float)(smoothing * _previous[i] + (1.0 - smoothing) * v);
            output[i] = smoothed;
            _previous[i] = smoothed;
        }

        // Band energies
        double nyquist = audioFrame.SampleRate / 2.0;
        float band(string name, double f1, double f2)
        {
            if (mag.Length == 0) return 0f;
            int i1 = (int)Math.Clamp(Math.Round(f1 / nyquist * (mag.Length - 1)), 1, mag.Length - 1);
            int i2 = (int)Math.Clamp(Math.Round(f2 / nyquist * (mag.Length - 1)), i1 + 1, mag.Length - 1);
            double sum = 0; int cnt = 0;
            for (int i = i1; i <= i2; i++) { sum += mag[i]; cnt++; }
            double avg = cnt > 0 ? sum / cnt : 0;
            return (float)Math.Clamp(Math.Sqrt(avg) * 2.0, 0.0, 1.0);
        }
        var bass = band("bass", 20, 250);
        var mid = band("mid", 250, 2000);
        var treble = band("treble", 2000, 16000);

        // Tonal / pitch analysis (simple chroma-based dominant pitch class)
        float pitchHue = 0f;
        float pitchStrength = 0f;
        if (!isSilent && mag.Length > 4)
        {
            var chroma = new double[12];
            double chromaTotal = 0.0;

            for (int i = 1; i < mag.Length; i++)
            {
                double f = (double)i / (mag.Length - 1) * nyquist;
                if (f < 60.0 || f > 5000.0) continue;
                double m = mag[i];
                if (m <= 0) continue;

                double midi = 69.0 + 12.0 * Math.Log(f / 440.0, 2.0);
                if (double.IsNaN(midi) || double.IsInfinity(midi)) continue;
                int note = (int)Math.Round(midi);
                int pc = ((note % 12) + 12) % 12;
                chroma[pc] += m;
                chromaTotal += m;
            }

            if (chromaTotal > 1e-6)
            {
                int bestIndex = 0;
                double bestValue = 0.0;
                for (int k = 0; k < 12; k++)
                {
                    if (chroma[k] > bestValue)
                    {
                        bestValue = chroma[k];
                        bestIndex = k;
                    }
                }

                if (bestValue > 0.0)
                {
                    pitchHue = (float)(bestIndex / 12.0); // 0..1 mapped around the color wheel
                    pitchStrength = (float)Math.Clamp(bestValue / chromaTotal, 0.0, 1.0);
                }
            }
        }

        // Spectral flux beat detection
        double flux = 0;
        if (isSilent)
        {
            // Reset beat state on silence
            if (_prevMag != null) Array.Clear(_prevMag, 0, _prevMag.Length);
            Array.Clear(_fluxHistory, 0, _fluxHistory.Length);
            _fluxIndex = 0;
            _fluxCount = 0;
        }
        else
        {
            if (_prevMag == null || _prevMag.Length != mag.Length)
            {
                _prevMag = new double[mag.Length];
                Array.Copy(mag, _prevMag, mag.Length);
                flux = 0;
            }
            else
            {
                double magSum = 0;
                for (int i = 0; i < mag.Length; i++)
                {
                    var m = mag[i];
                    magSum += m;
                    var diff = m - _prevMag[i];
                    if (diff > 0) flux += diff;
                    _prevMag[i] = m;
                }

                // Normalize by overall spectral energy so beats remain visible
                // even when listening at lower volumes.
                if (magSum > 1e-9)
                    flux /= magSum;
            }
        }

        // Maintain history for adaptive thresholding
        _fluxHistory[_fluxIndex] = flux;
        _fluxIndex = (_fluxIndex + 1) % _fluxHistory.Length;
        _fluxCount = Math.Min(_fluxCount + 1, _fluxHistory.Length);

        double mean = 0;
        for (int i = 0; i < _fluxCount; i++) mean += _fluxHistory[i];
        mean /= Math.Max(1, _fluxCount);

        double var = 0;
        for (int i = 0; i < _fluxCount; i++)
        {
            double d = _fluxHistory[i] - mean;
            var += d * d;
        }
        var /= Math.Max(1, _fluxCount);
        double std = Math.Sqrt(var);

        // Slightly lower threshold for a more responsive beat detector,
        // but keep it adaptive to local dynamics.
        double threshold = mean + 1.2 * std;

        bool isBeatFlag = false;
        float beatStrength = 0f;

        bool candidate = !isSilent && _fluxCount > 4 && flux > threshold && flux > 1e-6;
        if (candidate)
        {
            var nowBeat = DateTime.UtcNow;
            var sinceLastMs = (nowBeat - _lastBeatAt).TotalMilliseconds;

            // Basic refractory period (~80ms) to avoid double-triggers on a single hit
            if (_lastBeatAt == DateTime.MinValue || sinceLastMs >= 80.0)
            {
                isBeatFlag = true;
                _lastBeatAt = nowBeat;

                var excess = flux - threshold;
                var denom = threshold * 0.5 + 1e-6; // map moderate peaks to a healthy strength
                beatStrength = (float)Math.Clamp(excess / denom, 0.0, 1.0);
            }
        }

        // Smooth global fade factor for silence-based fading
        float silenceFadeValue = 1f;
        if (settings.FadeOnSilenceEnabled)
        {
            double targetFps = Math.Clamp(settings.TargetFps, 10, 240);
            double frameDt = 1.0 / targetFps;
            double fadeOutSeconds = Math.Clamp(settings.SilenceFadeOutSeconds, 0.05, 10.0);
            double fadeInSeconds = Math.Clamp(settings.SilenceFadeInSeconds, 0.05, 10.0);
            double fadeOutPerFrame = frameDt / fadeOutSeconds;
            double fadeInPerFrame = frameDt / fadeInSeconds;

            if (isSilent)
            {
                _silenceFade = Math.Max(0.0, _silenceFade - fadeOutPerFrame);
            }
            else
            {
                _silenceFade = Math.Min(1.0, _silenceFade + fadeInPerFrame);
            }

            silenceFadeValue = (float)_silenceFade;
        }

        return new VisualizerFrame(output, bass, mid, treble, isBeatFlag, beatStrength, silenceFadeValue, pitchHue, pitchStrength);
        }
        finally
        {
            lock (_frameLock)
            {
                if (_inFlight != null && _inFlight.IsCompleted)
                {
                    _inFlight = null;
                }
            }
        }
    }
}
