using System;
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
    private readonly SemaphoreSlim _dataAvailable = new(0, int.MaxValue);
    private bool _disposed;
    private readonly int _maxQueueSamples;
    private float[]? _prevFrame;
    private readonly float[] _buffer;
    private int _writeIndex;
    private int _readIndex;
    private int _availableSamples;

    public int SampleRate { get; }
    public int Channels { get; }

    public WASAPILoopbackAudioInput() : this(null) { }

    public WASAPILoopbackAudioInput(string? deviceId)
    {
        var enumerator = new MMDeviceEnumerator();
        MMDevice device;
        if (!string.IsNullOrEmpty(deviceId))
        {
            try
            {
                device = enumerator.GetDevice(deviceId);
            }
            catch
            {
                device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            }
        }
        else
        {
            device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        }
        _capture = new WasapiLoopbackCapture(device);
        SampleRate = _capture.WaveFormat.SampleRate;
        Channels = _capture.WaveFormat.Channels;
        _maxQueueSamples = Math.Max(SampleRate / 32, 1024); // keep ~30ms of mono samples max for lower latency
        _buffer = new float[_maxQueueSamples * 2]; // small headroom to avoid drop chatter
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
                            Enqueue_NoLock(wb.FloatBuffer[i]);
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
                            Enqueue_NoLock((float)(sum / channels));
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
                            Enqueue_NoLock(shorts[i] / 32768f);
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
                            Enqueue_NoLock(sum / (32768f * channels));
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
                            Enqueue_NoLock(val / 8388608f);
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
                            Enqueue_NoLock((float)(sum / (channels * 8388608.0)));
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
                            Enqueue_NoLock(ints[i] / 2147483648f);
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
                            Enqueue_NoLock((float)(sum / (channels * 2147483648.0)));
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
        if (minSamples <= 0) minSamples = 512;
        int hop = Math.Max(minSamples / 4, 64); // Smaller hop = fresher samples, lower latency
        if (_prevFrame != null && _prevFrame.Length != minSamples)
            _prevFrame = null;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int need = _prevFrame == null ? minSamples : hop;
            bool haveEnough;
            lock (_lock)
            {
                haveEnough = _availableSamples >= need;
            }
            if (!haveEnough)
            {
                await _dataAvailable.WaitAsync(cancellationToken);
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
                copied += DequeueSamples_NoLock(frame, copied, toDequeue);
            }

            _prevFrame = frame;
            return new AudioFrame(frame, SampleRate);
        }
    }

    // Must be called inside _lock
    private void Enqueue_NoLock(float value)
    {
        _buffer[_writeIndex] = value;
        _writeIndex = (_writeIndex + 1) % _buffer.Length;

        if (_availableSamples == _buffer.Length)
        {
            // Overwrite oldest when buffer is full
            _readIndex = (_readIndex + 1) % _buffer.Length;
        }
        else
        {
            _availableSamples++;
        }

        // Soft cap to keep latency bounded
        if (_availableSamples > _maxQueueSamples)
        {
            int toDrop = _availableSamples - _maxQueueSamples;
            _readIndex = (_readIndex + toDrop) % _buffer.Length;
            _availableSamples = _maxQueueSamples;
        }
    }

    // Must be called inside _lock
    private int DequeueSamples_NoLock(float[] dest, int destOffset, int count)
    {
        int remaining = Math.Min(count, _availableSamples);
        int copied = 0;
        int cap = _buffer.Length;

        while (copied < remaining)
        {
            int chunk = Math.Min(remaining - copied, cap - _readIndex);
            Array.Copy(_buffer, _readIndex, dest, destOffset + copied, chunk);
            _readIndex = (_readIndex + chunk) % cap;
            copied += chunk;
        }

        _availableSamples -= copied;
        return copied;
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
