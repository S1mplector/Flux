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

        public EqualizerSettings ToDomain() => new EqualizerSettings(
            BarsCount, Responsiveness, Smoothing, new ColorRgb(ColorR, ColorG, ColorB),
            TargetFps, ColorCycleEnabled, ColorCycleSpeedHz, BarCornerRadius,
            (MonitorDisplayMode)DisplayMode, SpecificMonitorDeviceName,
            OffsetX, OffsetY);

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
            OffsetY = s.OffsetY
        };
    }
}
