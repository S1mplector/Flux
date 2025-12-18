namespace Equalizer.Domain.Widgets;

public class WidgetConfig
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string WidgetTypeId { get; set; } = "";
    public bool IsEnabled { get; set; } = true;
    public double X { get; set; }
    public double Y { get; set; }
    public WidgetAnchor Anchor { get; set; } = WidgetAnchor.TopLeft;
    public double Width { get; set; } = 200;
    public double Height { get; set; } = 100;
    public string? MonitorDeviceName { get; set; }
    public Dictionary<string, object> Settings { get; set; } = new();
    
    public T GetSetting<T>(string key, T defaultValue)
    {
        if (Settings.TryGetValue(key, out var value))
        {
            try
            {
                if (value is T typedValue) return typedValue;
                return (T)Convert.ChangeType(value, typeof(T));
            }
            catch { }
        }
        return defaultValue;
    }
    
    public void SetSetting<T>(string key, T value)
    {
        Settings[key] = value!;
    }
}

public class WidgetLayout
{
    public List<WidgetConfig> Widgets { get; set; } = new();
    
    public static WidgetLayout Default => new()
    {
        Widgets = new List<WidgetConfig>
        {
            new WidgetConfig
            {
                Id = "equalizer-main",
                WidgetTypeId = "equalizer",
                IsEnabled = true,
                X = 0,
                Y = 0,
                Anchor = WidgetAnchor.BottomLeft,
                Width = 0, // Full width
                Height = 150
            }
        }
    };
}
