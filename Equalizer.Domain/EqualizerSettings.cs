namespace Equalizer.Domain;

public sealed class EqualizerSettings
{
    public int BarsCount { get; }
    public double Responsiveness { get; }
    public double Smoothing { get; }
    public ColorRgb Color { get; }
    public int TargetFps { get; }
    public bool ColorCycleEnabled { get; }
    public double ColorCycleSpeedHz { get; }
    public double BarCornerRadius { get; }
    public MonitorDisplayMode DisplayMode { get; }
    public string? SpecificMonitorDeviceName { get; }
    public double OffsetX { get; }
    public double OffsetY { get; }

    public EqualizerSettings(int barsCount, double responsiveness, double smoothing, ColorRgb color)
    {
        if (barsCount < 8 || barsCount > 256)
            throw new ArgumentOutOfRangeException(nameof(barsCount), "BarsCount must be between 8 and 256.");
        if (responsiveness < 0 || responsiveness > 1)
            throw new ArgumentOutOfRangeException(nameof(responsiveness), "Responsiveness must be between 0 and 1.");
        if (smoothing < 0 || smoothing > 1)
            throw new ArgumentOutOfRangeException(nameof(smoothing), "Smoothing must be between 0 and 1.");

        BarsCount = barsCount;
        Responsiveness = responsiveness;
        Smoothing = smoothing;
        Color = color;
        TargetFps = 60;
        ColorCycleEnabled = false;
        ColorCycleSpeedHz = 0.2;
        BarCornerRadius = 1.0;
        DisplayMode = MonitorDisplayMode.All;
        SpecificMonitorDeviceName = null;
        OffsetX = 0.0;
        OffsetY = 0.0;
    }
 
    public EqualizerSettings(int barsCount, double responsiveness, double smoothing, ColorRgb color,
        int targetFps, bool colorCycleEnabled, double colorCycleSpeedHz, double barCornerRadius,
        MonitorDisplayMode displayMode, string? specificMonitorDeviceName,
        double offsetX, double offsetY)
        : this(barsCount, responsiveness, smoothing, color)
    {
        if (targetFps < 10 || targetFps > 240)
            throw new ArgumentOutOfRangeException(nameof(targetFps), "TargetFps must be between 10 and 240.");
        if (colorCycleSpeedHz < 0 || colorCycleSpeedHz > 10)
            throw new ArgumentOutOfRangeException(nameof(colorCycleSpeedHz), "ColorCycleSpeedHz must be between 0 and 10.");
        if (barCornerRadius < 0 || barCornerRadius > 16)
            throw new ArgumentOutOfRangeException(nameof(barCornerRadius), "BarCornerRadius must be between 0 and 16.");

        TargetFps = targetFps;
        ColorCycleEnabled = colorCycleEnabled;
        ColorCycleSpeedHz = colorCycleSpeedHz;
        BarCornerRadius = barCornerRadius;
        DisplayMode = displayMode;
        SpecificMonitorDeviceName = specificMonitorDeviceName;
        OffsetX = offsetX;
        OffsetY = offsetY;
    }

    // Backward-compatible overload without offsets
    public EqualizerSettings(int barsCount, double responsiveness, double smoothing, ColorRgb color,
        int targetFps, bool colorCycleEnabled, double colorCycleSpeedHz, double barCornerRadius,
        MonitorDisplayMode displayMode, string? specificMonitorDeviceName)
        : this(barsCount, responsiveness, smoothing, color,
            targetFps, colorCycleEnabled, colorCycleSpeedHz, barCornerRadius,
            displayMode, specificMonitorDeviceName, 0.0, 0.0)
    {
    }

    public static EqualizerSettings Default => new(
        barsCount: 64,
        responsiveness: 0.7,
        smoothing: 0.5,
        color: new ColorRgb(0, 255, 128),
        targetFps: 60,
        colorCycleEnabled: false,
        colorCycleSpeedHz: 0.2,
        barCornerRadius: 1.0,
        displayMode: MonitorDisplayMode.All,
        specificMonitorDeviceName: null,
        offsetX: 0.0,
        offsetY: 0.0
    );
}
