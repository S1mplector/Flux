using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Equalizer.Application.Abstractions;
using Equalizer.Domain;

namespace Equalizer.Infrastructure.Settings;

public sealed class JsonSettingsRepository : ISettingsPort
{
    private readonly string _filePath;
    private readonly SemaphoreSlim _mutex = new(1, 1);
    private EqualizerSettings? _cache;

    public JsonSettingsRepository()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var dir = Path.Combine(appData, "Equalizer");
        Directory.CreateDirectory(dir);
        _filePath = Path.Combine(dir, "settings.json");
    }

    public async Task<EqualizerSettings> GetAsync()
    {
        await _mutex.WaitAsync();
        try
        {
            if (_cache != null) return _cache;
            if (!File.Exists(_filePath))
            {
                _cache = EqualizerSettings.Default;
                await SaveInternalAsync(_cache);
                return _cache;
            }
            var json = await File.ReadAllTextAsync(_filePath);
            var dto = JsonSerializer.Deserialize<SettingsDto>(json) ?? new SettingsDto();
            _cache = dto.ToDomain();
            return _cache;
        }
        finally { _mutex.Release(); }
    }

    public async Task SaveAsync(EqualizerSettings settings)
    {
        await _mutex.WaitAsync();
        try
        {
            _cache = settings;
            await SaveInternalAsync(settings);
        }
        finally { _mutex.Release(); }
    }

    private Task SaveInternalAsync(EqualizerSettings s)
    {
        var dto = SettingsDto.FromDomain(s);
        var json = JsonSerializer.Serialize(dto, new JsonSerializerOptions { WriteIndented = true });
        return File.WriteAllTextAsync(_filePath, json);
    }

    private sealed class SettingsDto
    {
        public int BarsCount { get; set; } = 64;
        public double Responsiveness { get; set; } = 0.7;
        public double Smoothing { get; set; } = 0.5;
        public byte ColorR { get; set; } = 0;
        public byte ColorG { get; set; } = 255;
        public byte ColorB { get; set; } = 128;

        public int TargetFps { get; set; } = 60;
        public bool ColorCycleEnabled { get; set; } = false;
        public double ColorCycleSpeedHz { get; set; } = 0.2;
        public double BarCornerRadius { get; set; } = 1.0;
        public int DisplayMode { get; set; } = 0; // MonitorDisplayMode
        public string? SpecificMonitorDeviceName { get; set; }
        public double OffsetX { get; set; } = 0.0;
        public double OffsetY { get; set; } = 0.0;
        public int VisualizerMode { get; set; } = 0; // VisualizerMode.Bars
        public double CircleDiameter { get; set; } = 400.0;
        public bool OverlayVisible { get; set; } = true;
        public bool FadeOnSilenceEnabled { get; set; } = false;
        public double SilenceFadeOutSeconds { get; set; } = 0.5;
        public double SilenceFadeInSeconds { get; set; } = 0.2;
        public bool PitchReactiveColorEnabled { get; set; } = false;
        public double BassEmphasis { get; set; } = 1.0;
        public double TrebleEmphasis { get; set; } = 1.0;
        public bool BeatShapeEnabled { get; set; } = false;
        public bool GlowEnabled { get; set; } = false;
        public bool PerfOverlayEnabled { get; set; } = false;
        public bool GradientEnabled { get; set; } = false;
        public byte GradientEndR { get; set; } = 255;
        public byte GradientEndG { get; set; } = 0;
        public byte GradientEndB { get; set; } = 128;
        public string? AudioDeviceId { get; set; } = null;
        public int RenderingMode { get; set; } = 0; // RenderingMode.Cpu

        public EqualizerSettings ToDomain() => new EqualizerSettings(
            BarsCount, Responsiveness, Smoothing, new ColorRgb(ColorR, ColorG, ColorB),
            TargetFps, ColorCycleEnabled, ColorCycleSpeedHz, BarCornerRadius,
            (MonitorDisplayMode)DisplayMode, SpecificMonitorDeviceName,
            OffsetX, OffsetY,
            (VisualizerMode)VisualizerMode, CircleDiameter, OverlayVisible, FadeOnSilenceEnabled,
            SilenceFadeOutSeconds, SilenceFadeInSeconds,
            PitchReactiveColorEnabled,
            BassEmphasis, TrebleEmphasis,
            BeatShapeEnabled, GlowEnabled, PerfOverlayEnabled,
            GradientEnabled, new ColorRgb(GradientEndR, GradientEndG, GradientEndB), AudioDeviceId,
            (RenderingMode)RenderingMode);

        public static SettingsDto FromDomain(EqualizerSettings s) => new SettingsDto
        {
            BarsCount = s.BarsCount,
            Responsiveness = s.Responsiveness,
            Smoothing = s.Smoothing,
            ColorR = s.Color.R,
            ColorG = s.Color.G,
            ColorB = s.Color.B,
            TargetFps = s.TargetFps,
            ColorCycleEnabled = s.ColorCycleEnabled,
            ColorCycleSpeedHz = s.ColorCycleSpeedHz,
            BarCornerRadius = s.BarCornerRadius,
            DisplayMode = (int)s.DisplayMode,
            SpecificMonitorDeviceName = s.SpecificMonitorDeviceName,
            OffsetX = s.OffsetX,
            OffsetY = s.OffsetY,
            VisualizerMode = (int)s.VisualizerMode,
            CircleDiameter = s.CircleDiameter,
            OverlayVisible = s.OverlayVisible,
            FadeOnSilenceEnabled = s.FadeOnSilenceEnabled,
            SilenceFadeOutSeconds = s.SilenceFadeOutSeconds,
            SilenceFadeInSeconds = s.SilenceFadeInSeconds,
            PitchReactiveColorEnabled = s.PitchReactiveColorEnabled,
            BassEmphasis = s.BassEmphasis,
            TrebleEmphasis = s.TrebleEmphasis,
            BeatShapeEnabled = s.BeatShapeEnabled,
            GlowEnabled = s.GlowEnabled,
            PerfOverlayEnabled = s.PerfOverlayEnabled,
            GradientEnabled = s.GradientEnabled,
            GradientEndR = s.GradientEndColor.R,
            GradientEndG = s.GradientEndColor.G,
            GradientEndB = s.GradientEndColor.B,
            AudioDeviceId = s.AudioDeviceId,
            RenderingMode = (int)s.RenderingMode
        };
    }
}
