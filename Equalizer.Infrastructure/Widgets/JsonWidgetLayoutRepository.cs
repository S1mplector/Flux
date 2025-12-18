using System.Text.Json;
using Equalizer.Application.Abstractions;
using Equalizer.Domain.Widgets;

namespace Equalizer.Infrastructure.Widgets;

public sealed class JsonWidgetLayoutRepository : IWidgetLayoutPort
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Equalizer", "widgets.json");

    private readonly SemaphoreSlim _lock = new(1, 1);
    private WidgetLayout? _cache;

    public async Task<WidgetLayout> GetLayoutAsync()
    {
        await _lock.WaitAsync();
        try
        {
            if (_cache != null) return _cache;

            if (!File.Exists(FilePath))
            {
                _cache = WidgetLayout.Default;
                return _cache;
            }

            var json = await File.ReadAllTextAsync(FilePath);
            var dto = JsonSerializer.Deserialize<WidgetLayoutDto>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            _cache = dto?.ToDomain() ?? WidgetLayout.Default;
            return _cache;
        }
        catch
        {
            _cache = WidgetLayout.Default;
            return _cache;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task SaveLayoutAsync(WidgetLayout layout)
    {
        await _lock.WaitAsync();
        try
        {
            _cache = layout;
            var dir = Path.GetDirectoryName(FilePath)!;
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

            var dto = WidgetLayoutDto.FromDomain(layout);
            var json = JsonSerializer.Serialize(dto, new JsonSerializerOptions
            {
                WriteIndented = true
            });
            await File.WriteAllTextAsync(FilePath, json);
        }
        finally
        {
            _lock.Release();
        }
    }

    private class WidgetLayoutDto
    {
        public List<WidgetConfigDto> Widgets { get; set; } = new();

        public WidgetLayout ToDomain() => new()
        {
            Widgets = Widgets.Select(w => w.ToDomain()).ToList()
        };

        public static WidgetLayoutDto FromDomain(WidgetLayout layout) => new()
        {
            Widgets = layout.Widgets.Select(WidgetConfigDto.FromDomain).ToList()
        };
    }

    private class WidgetConfigDto
    {
        public string Id { get; set; } = "";
        public string WidgetTypeId { get; set; } = "";
        public bool IsEnabled { get; set; } = true;
        public double X { get; set; }
        public double Y { get; set; }
        public int Anchor { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public string? MonitorDeviceName { get; set; }
        public Dictionary<string, object>? Settings { get; set; }

        public WidgetConfig ToDomain() => new()
        {
            Id = Id,
            WidgetTypeId = WidgetTypeId,
            IsEnabled = IsEnabled,
            X = X,
            Y = Y,
            Anchor = (WidgetAnchor)Anchor,
            Width = Width,
            Height = Height,
            MonitorDeviceName = MonitorDeviceName,
            Settings = Settings ?? new()
        };

        public static WidgetConfigDto FromDomain(WidgetConfig config) => new()
        {
            Id = config.Id,
            WidgetTypeId = config.WidgetTypeId,
            IsEnabled = config.IsEnabled,
            X = config.X,
            Y = config.Y,
            Anchor = (int)config.Anchor,
            Width = config.Width,
            Height = config.Height,
            MonitorDeviceName = config.MonitorDeviceName,
            Settings = config.Settings
        };
    }
}
