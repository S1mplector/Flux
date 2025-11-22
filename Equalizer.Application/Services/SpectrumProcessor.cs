using System;
using System.Linq;
using System.Numerics;
using MathNet.Numerics.IntegralTransforms;
using Equalizer.Application.Audio;

namespace Equalizer.Application.Services;

public sealed class SpectrumProcessor
{
    private readonly object _lock = new();
    private Complex[]? _complex;
    private double[]? _mag;
    private double[]? _hann;
    private int _n;
    private BinCache? _binCache;

    public float[] ComputeBars(AudioFrame frame, int bars)
    {
        if (bars <= 0) return Array.Empty<float>();
        var samples = frame.Samples;
        if (samples.Length == 0) return new float[bars];

        var mag = ComputeMagnitudes(frame);
        return ComputeBarsFromMagnitudes(mag, frame.SampleRate, bars);
    }

    public double[] ComputeMagnitudes(AudioFrame frame)
    {
        var samples = frame.Samples;
        int n = NextPowerOfTwo(Math.Min(samples.Length, 4096));
        lock (_lock)
        {
            if (n != _n || _complex == null || _hann == null || _mag == null)
            {
                _n = n;
                _complex = new Complex[n];
                _hann = new double[n];
                for (int i = 0; i < n; i++)
                {
                    _hann[i] = 0.5 * (1 - Math.Cos(2 * Math.PI * i / (n - 1))); // Hann window
                }
                _mag = new double[n / 2];
            }

            var complex = _complex!;
            var hann = _hann!;
            for (int i = 0; i < n; i++)
            {
                double s = i < samples.Length ? samples[i] : 0.0;
                complex[i] = new Complex(s * hann[i], 0);
            }

            Fourier.Forward(complex, FourierOptions.Matlab);
            var mag = _mag!;
            for (int i = 0; i < mag.Length; i++)
            {
                var c = complex[i];
                mag[i] = (2.0 / n) * c.Magnitude;
            }
            return mag;
        }
    }

    public float[] ComputeBarsFromMagnitudes(double[] mag, int sampleRate, int bars)
    {
        var result = new float[bars];
        double nyquist = sampleRate / 2.0;
        double fMin = 50.0;
        double fMax = Math.Min(18000.0, nyquist);
        if (fMax <= fMin) return result;

        // Reuse precomputed bin ranges for this bars/sampleRate/magLength combination.
        var bins = GetOrBuildBins(bars, sampleRate, mag.Length, fMin, fMax, nyquist);
        var starts = bins.Starts;
        var ends = bins.Ends;

        for (int b = 0; b < bars; b++)
        {
            int i1 = starts[b];
            int i2 = ends[b];
            double sum = 0.0;
            int count = 0;
            for (int i = i1; i <= i2; i++) { sum += mag[i]; count++; }
            double avg = count > 0 ? sum / count : 0.0;

            double compressed = Math.Sqrt(avg);
            double scaled = compressed * 1.8;
            result[b] = (float)Math.Clamp(scaled, 0.0, 1.0);
        }
        return result;
    }

    private static int NextPowerOfTwo(int x)
    {
        int p = 1;
        while (p < x) p <<= 1;
        return p;
    }

    private BinCache GetOrBuildBins(int bars, int sampleRate, int magLength, double fMin, double fMax, double nyquist)
    {
        var key = new BinCacheKey(bars, sampleRate, magLength);
        var cache = _binCache;
        if (cache.HasValue && cache.Value.Key.Equals(key))
        {
            return cache.Value;
        }

        var starts = new int[bars];
        var ends = new int[bars];
        for (int b = 0; b < bars; b++)
        {
            double t1 = (double)b / bars;
            double t2 = (double)(b + 1) / bars;
            double f1 = fMin * Math.Pow(fMax / fMin, t1);
            double f2 = fMin * Math.Pow(fMax / fMin, t2);

            int i1 = (int)Math.Clamp(Math.Round(f1 / nyquist * (magLength - 1)), 1, magLength - 1);
            int i2 = (int)Math.Clamp(Math.Round(f2 / nyquist * (magLength - 1)), i1 + 1, magLength - 1);
            starts[b] = i1;
            ends[b] = i2;
        }

        var built = new BinCache(key, starts, ends);
        _binCache = built;
        return built;
    }

    private readonly struct BinCacheKey : IEquatable<BinCacheKey>
    {
        public BinCacheKey(int bars, int sampleRate, int magLength)
        {
            Bars = bars;
            SampleRate = sampleRate;
            MagLength = magLength;
        }

        public int Bars { get; }
        public int SampleRate { get; }
        public int MagLength { get; }

        public bool Equals(BinCacheKey other) =>
            Bars == other.Bars && SampleRate == other.SampleRate && MagLength == other.MagLength;
        public override bool Equals(object? obj) => obj is BinCacheKey other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(Bars, SampleRate, MagLength);
    }

    private readonly struct BinCache
    {
        public BinCache(BinCacheKey key, int[] starts, int[] ends)
        {
            Key = key;
            Starts = starts;
            Ends = ends;
        }

        public BinCacheKey Key { get; }
        public int[] Starts { get; }
        public int[] Ends { get; }
        public bool HasValue => Starts != null && Ends != null;
    }
}
