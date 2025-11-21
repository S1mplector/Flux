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
    private readonly int _maxQueueSamples;
    private float[]? _prevFrame;

    public int SampleRate { get; }
    public int Channels { get; }

    public WASAPILoopbackAudioInput()
    {
        var enumerator = new MMDeviceEnumerator();
        var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        _capture = new WasapiLoopbackCapture(device);
        SampleRate = _capture.WaveFormat.SampleRate;
        Channels = _capture.WaveFormat.Channels;
        _maxQueueSamples = Math.Max(SampleRate / 16, 2048); // keep ~60ms of mono samples max to reduce latency
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
                        TrimQueue_NoLock();
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
                        TrimQueue_NoLock();
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
                        TrimQueue_NoLock();
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
                        TrimQueue_NoLock();
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
                        TrimQueue_NoLock();
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
                        TrimQueue_NoLock();
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
                        TrimQueue_NoLock();
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
                        TrimQueue_NoLock();
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

    // Must be called inside _lock
    private void TrimQueue_NoLock()
    {
        int max = _maxQueueSamples;
        while (_queue.Count > max)
            _queue.Dequeue();
    }

    public async Task<AudioFrame> ReadFrameAsync(int minSamples, CancellationToken cancellationToken)
    {
        if (minSamples <= 0) minSamples = 2048;
        int hop = Math.Max(minSamples / 2, 1);
        if (_prevFrame != null && _prevFrame.Length != minSamples)
            _prevFrame = null;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int need = _prevFrame == null ? minSamples : hop;
            bool haveEnough;
            lock (_lock)
            {
                haveEnough = _queue.Count >= need;
            }
            if (!haveEnough)
            {
                // Poll for new audio more frequently to reduce end-to-end latency
                await _dataAvailable.WaitAsync(TimeSpan.FromMilliseconds(5), cancellationToken);
                continue;
            }

            float[] frame = new float[minSamples];
            int keep = minSamples - hop;
            int copied = 0;
            if (_prevFrame != null)
            {
                Array.Copy(_prevFrame, _prevFrame.Length - keep, frame, 0, keep);
                copied = keep;
            }

            int toDequeue = _prevFrame == null ? minSamples : hop;
            lock (_lock)
            {
                for (int i = 0; i < toDequeue; i++)
                {
                    frame[copied++] = _queue.Dequeue();
                }
            }

            _prevFrame = frame;
            return new AudioFrame(frame, SampleRate);
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
