using System;
using System.Linq;
using System.Numerics;
using MathNet.Numerics.IntegralTransforms;
using Equalizer.Application.Audio;

namespace Equalizer.Application.Services;

public sealed class SpectrumProcessor
{
    public float[] ComputeBars(AudioFrame frame, int bars)
    {
        if (bars <= 0) return Array.Empty<float>();
        var samples = frame.Samples;
        if (samples.Length == 0) return new float[bars];

        int n = NextPowerOfTwo(Math.Min(samples.Length, 4096));
        var complex = new Complex[n];
        var winSum = 0.0; // for potential window energy correction (not strictly needed if we normalize by N)
        for (int i = 0; i < n; i++)
        {
            double s = i < samples.Length ? samples[i] : 0.0;
            double w = 0.5 * (1 - Math.Cos(2 * Math.PI * i / (n - 1))); // Hann window
            winSum += w;
            complex[i] = new Complex(s * w, 0);
        }

        Fourier.Forward(complex, FourierOptions.Matlab);

        var mag = new double[n / 2];
        for (int i = 0; i < mag.Length; i++)
        {
            // Amplitude normalization: MathNet forward FFT is unnormalized, scale ~ N
            // Convert to single-sided amplitude spectrum in ~[-1,1] domain
            var c = complex[i];
            mag[i] = (2.0 / n) * c.Magnitude; // 2x for single-sided spectrum, divide by N
        }

        var result = new float[bars];
        double nyquist = frame.SampleRate / 2.0;
        double fMin = 50.0;
        double fMax = Math.Min(18000.0, nyquist);
        if (fMax <= fMin) return result;

        for (int b = 0; b < bars; b++)
        {
            double t1 = (double)b / bars;
            double t2 = (double)(b + 1) / bars;
            double f1 = fMin * Math.Pow(fMax / fMin, t1);
            double f2 = fMin * Math.Pow(fMax / fMin, t2);

            int i1 = (int)Math.Clamp(Math.Round(f1 / nyquist * (mag.Length - 1)), 1, mag.Length - 1);
            int i2 = (int)Math.Clamp(Math.Round(f2 / nyquist * (mag.Length - 1)), i1 + 1, mag.Length - 1);

            double sum = 0.0;
            int count = 0;
            for (int i = i1; i <= i2; i++) { sum += mag[i]; count++; }
            double avg = count > 0 ? sum / count : 0.0;

            double compressed = Math.Sqrt(avg); // perceptual compression
            // Conservative gain to avoid saturation; add small headroom
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
}
