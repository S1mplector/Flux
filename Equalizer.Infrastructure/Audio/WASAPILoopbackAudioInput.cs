using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Equalizer.Application.Abstractions;
using Equalizer.Application.Audio;
using NAudio.Wave;
using NAudio.CoreAudioApi;

namespace Equalizer.Infrastructure.Audio;

public sealed class WASAPILoopbackAudioInput : IAudioInputPort, IDisposable
{
    private readonly WasapiLoopbackCapture _capture;
    private readonly object _lock = new();
    private readonly Queue<float> _queue = new();
    private readonly SemaphoreSlim _dataAvailable = new(0);
    private bool _disposed;

    public int SampleRate { get; }
    public int Channels { get; }

    public WASAPILoopbackAudioInput()
    {
        var enumerator = new MMDeviceEnumerator();
        var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        _capture = new WasapiLoopbackCapture(device);
        SampleRate = _capture.WaveFormat.SampleRate;
        Channels = _capture.WaveFormat.Channels;
        _capture.DataAvailable += OnDataAvailable;
        _capture.RecordingStopped += (_, __) => _dataAvailable.Release();
        _capture.StartRecording();
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        if (_disposed || e.BytesRecorded <= 0) return;
        var wf = _capture.WaveFormat;
        int channels = wf.Channels;
        bool enqueued = false;
        try
        {
            // Many WASAPI formats are reported as Extensible with IEEE float subformat.
            if ((wf.BitsPerSample == 32 && (wf.Encoding == WaveFormatEncoding.IeeeFloat || wf.Encoding == WaveFormatEncoding.Extensible)))
            {
                var wb = new WaveBuffer(e.Buffer);
                int sampleCount = e.BytesRecorded / sizeof(float);
                if (channels == 1)
                {
                    lock (_lock)
                    {
                        for (int i = 0; i < sampleCount; i++)
                        {
                            _queue.Enqueue(wb.FloatBuffer[i]);
                        }
                        enqueued = sampleCount > 0;
                    }
                }
                else
                {
                    int frames = sampleCount / channels;
                    lock (_lock)
                    {
                        int idx = 0;
                        for (int f = 0; f < frames; f++)
                        {
                            double sum = 0;
                            for (int c = 0; c < channels; c++)
                                sum += wb.FloatBuffer[idx++];
                            _queue.Enqueue((float)(sum / channels));
                        }
                        enqueued = frames > 0;
                    }
                }
            }
            else if (wf.Encoding == WaveFormatEncoding.Pcm && wf.BitsPerSample == 16)
            {
                int sampleCount = e.BytesRecorded / sizeof(short);
                var shorts = new short[sampleCount];
                Buffer.BlockCopy(e.Buffer, 0, shorts, 0, e.BytesRecorded);
                if (channels == 1)
                {
                    lock (_lock)
                    {
                        for (int i = 0; i < sampleCount; i++)
                            _queue.Enqueue(shorts[i] / 32768f);
                        enqueued = sampleCount > 0;
                    }
                }
                else
                {
                    int frames = sampleCount / channels;
                    lock (_lock)
                    {
                        int idx = 0;
                        for (int f = 0; f < frames; f++)
                        {
                            int sum = 0;
                            for (int c = 0; c < channels; c++)
                                sum += shorts[idx++];
                            _queue.Enqueue(sum / (32768f * channels));
                        }
                        enqueued = frames > 0;
                    }
                }
            }
            else if ((wf.Encoding == WaveFormatEncoding.Pcm || wf.Encoding == WaveFormatEncoding.Extensible) && wf.BitsPerSample == 24)
            {
                // 24-bit little-endian PCM
                int bytesPerSample = 3;
                int sampleCount = e.BytesRecorded / bytesPerSample;
                if (channels == 1)
                {
                    lock (_lock)
                    {
                        int idx = 0;
                        for (int i = 0; i < sampleCount; i++)
                        {
                            int b0 = e.Buffer[idx++];
                            int b1 = e.Buffer[idx++];
                            int b2 = e.Buffer[idx++];
                            int val = (b0 | (b1 << 8) | (b2 << 16));
                            if ((val & 0x800000) != 0) val |= unchecked((int)0xFF000000); // sign extend
                            _queue.Enqueue(val / 8388608f);
                        }
                        enqueued = sampleCount > 0;
                    }
                }
                else
                {
                    lock (_lock)
                    {
                        int idx = 0;
                        int frames = sampleCount / channels;
                        for (int f = 0; f < frames; f++)
                        {
                            long sum = 0;
                            for (int c = 0; c < channels; c++)
                            {
                                int b0 = e.Buffer[idx++];
                                int b1 = e.Buffer[idx++];
                                int b2 = e.Buffer[idx++];
                                int val = (b0 | (b1 << 8) | (b2 << 16));
                                if ((val & 0x800000) != 0) val |= unchecked((int)0xFF000000);
                                sum += val;
                            }
                            _queue.Enqueue((float)(sum / (channels * 8388608.0)));
                        }
                        enqueued = frames > 0;
                    }
                }
            }
            else if ((wf.Encoding == WaveFormatEncoding.Pcm || wf.Encoding == WaveFormatEncoding.Extensible) && wf.BitsPerSample == 32)
            {
                // 32-bit integer PCM
                int sampleCount = e.BytesRecorded / sizeof(int);
                var ints = new int[sampleCount];
                Buffer.BlockCopy(e.Buffer, 0, ints, 0, e.BytesRecorded);
                if (channels == 1)
                {
                    lock (_lock)
                    {
                        for (int i = 0; i < sampleCount; i++)
                            _queue.Enqueue(ints[i] / 2147483648f);
                        enqueued = sampleCount > 0;
                    }
                }
                else
                {
                    int frames = sampleCount / channels;
                    lock (_lock)
                    {
                        int idx = 0;
                        for (int f = 0; f < frames; f++)
                        {
                            long sum = 0;
                            for (int c = 0; c < channels; c++)
                                sum += ints[idx++];
                            _queue.Enqueue((float)(sum / (channels * 2147483648.0)));
                        }
                        enqueued = frames > 0;
                    }
                }
            }
            else
            {
                // Unsupported format: ignore
                return;
            }
        }
        finally
        {
            if (enqueued)
                _dataAvailable.Release();
        }
    }

    public async Task<AudioFrame> ReadFrameAsync(int minSamples, CancellationToken cancellationToken)
    {
        if (minSamples <= 0) minSamples = 2048;
        float[] buffer = new float[minSamples];

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int copied = 0;
            lock (_lock)
            {
                while (_queue.Count > 0 && copied < minSamples)
                {
                    buffer[copied++] = _queue.Dequeue();
                }
            }
            if (copied >= minSamples)
            {
                if (copied == minSamples)
                    return new AudioFrame(buffer, SampleRate);
                // If more were available (shouldn't happen with the logic above), truncate.
                var exact = new float[minSamples];
                Array.Copy(buffer, exact, minSamples);
                return new AudioFrame(exact, SampleRate);
            }
            // If we have at least half the samples, return zero-padded for responsiveness
            if (copied >= minSamples / 2)
            {
                var exact = new float[minSamples];
                Array.Copy(buffer, exact, copied);
                // remaining are zeros by default
                return new AudioFrame(exact, SampleRate);
            }
            await _dataAvailable.WaitAsync(TimeSpan.FromMilliseconds(15), cancellationToken);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try
        {
            _capture.StopRecording();
        }
        catch { }
        _capture.DataAvailable -= OnDataAvailable;
        _capture.Dispose();
        _dataAvailable.Dispose();
    }
}
