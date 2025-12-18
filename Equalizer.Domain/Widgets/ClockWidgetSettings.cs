namespace Equalizer.Domain.Widgets;

public class ClockWidgetSettings
{
    public string TimeFormat { get; set; } = "HH:mm:ss";
    public bool ShowSeconds { get; set; } = true;
    public bool Use24Hour { get; set; } = true;
    public double FontSize { get; set; } = 48;
    public string FontFamily { get; set; } = "Segoe UI";
    public ColorRgb TextColor { get; set; } = new(255, 255, 255);
    public double Opacity { get; set; } = 1.0;
    public bool ShowShadow { get; set; } = true;
    
    public static ClockWidgetSettings FromConfig(WidgetConfig config)
    {
        return new ClockWidgetSettings
        {
            TimeFormat = config.GetSetting("TimeFormat", "HH:mm:ss"),
            ShowSeconds = config.GetSetting("ShowSeconds", true),
            Use24Hour = config.GetSetting("Use24Hour", true),
            FontSize = config.GetSetting("FontSize", 48.0),
            FontFamily = config.GetSetting("FontFamily", "Segoe UI"),
            TextColor = new ColorRgb(
                config.GetSetting<byte>("TextColorR", 255),
                config.GetSetting<byte>("TextColorG", 255),
                config.GetSetting<byte>("TextColorB", 255)),
            Opacity = config.GetSetting("Opacity", 1.0),
            ShowShadow = config.GetSetting("ShowShadow", true)
        };
    }
    
    public void ApplyToConfig(WidgetConfig config)
    {
        config.SetSetting("TimeFormat", TimeFormat);
        config.SetSetting("ShowSeconds", ShowSeconds);
        config.SetSetting("Use24Hour", Use24Hour);
        config.SetSetting("FontSize", FontSize);
        config.SetSetting("FontFamily", FontFamily);
        config.SetSetting("TextColorR", TextColor.R);
        config.SetSetting("TextColorG", TextColor.G);
        config.SetSetting("TextColorB", TextColor.B);
        config.SetSetting("Opacity", Opacity);
        config.SetSetting("ShowShadow", ShowShadow);
    }
}

public class DateWidgetSettings
{
    public string DateFormat { get; set; } = "dddd, MMMM d, yyyy";
    public double FontSize { get; set; } = 24;
    public string FontFamily { get; set; } = "Segoe UI";
    public ColorRgb TextColor { get; set; } = new(255, 255, 255);
    public double Opacity { get; set; } = 1.0;
    public bool ShowShadow { get; set; } = true;
    
    public static DateWidgetSettings FromConfig(WidgetConfig config)
    {
        return new DateWidgetSettings
        {
            DateFormat = config.GetSetting("DateFormat", "dddd, MMMM d, yyyy"),
            FontSize = config.GetSetting("FontSize", 24.0),
            FontFamily = config.GetSetting("FontFamily", "Segoe UI"),
            TextColor = new ColorRgb(
                config.GetSetting<byte>("TextColorR", 255),
                config.GetSetting<byte>("TextColorG", 255),
                config.GetSetting<byte>("TextColorB", 255)),
            Opacity = config.GetSetting("Opacity", 1.0),
            ShowShadow = config.GetSetting("ShowShadow", true)
        };
    }
}

public class SystemInfoWidgetSettings
{
    public bool ShowCpu { get; set; } = true;
    public bool ShowRam { get; set; } = true;
    public bool ShowGpu { get; set; } = false;
    public bool ShowDisk { get; set; } = false;
    public bool ShowNetwork { get; set; } = false;
    public double FontSize { get; set; } = 14;
    public string FontFamily { get; set; } = "Segoe UI";
    public ColorRgb TextColor { get; set; } = new(255, 255, 255);
    public ColorRgb AccentColor { get; set; } = new(0, 255, 128);
    public bool ShowBars { get; set; } = true;
    public double BarHeight { get; set; } = 4;
    
    public static SystemInfoWidgetSettings FromConfig(WidgetConfig config)
    {
        return new SystemInfoWidgetSettings
        {
            ShowCpu = config.GetSetting("ShowCpu", true),
            ShowRam = config.GetSetting("ShowRam", true),
            ShowGpu = config.GetSetting("ShowGpu", false),
            ShowDisk = config.GetSetting("ShowDisk", false),
            ShowNetwork = config.GetSetting("ShowNetwork", false),
            FontSize = config.GetSetting("FontSize", 14.0),
            FontFamily = config.GetSetting("FontFamily", "Segoe UI"),
            TextColor = new ColorRgb(
                config.GetSetting<byte>("TextColorR", 255),
                config.GetSetting<byte>("TextColorG", 255),
                config.GetSetting<byte>("TextColorB", 255)),
            AccentColor = new ColorRgb(
                config.GetSetting<byte>("AccentColorR", 0),
                config.GetSetting<byte>("AccentColorG", 255),
                config.GetSetting<byte>("AccentColorB", 128)),
            ShowBars = config.GetSetting("ShowBars", true),
            BarHeight = config.GetSetting("BarHeight", 4.0)
        };
    }
}
