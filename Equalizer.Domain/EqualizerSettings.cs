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
    public VisualizerMode VisualizerMode { get; }
    public double CircleDiameter { get; }
    public bool OverlayVisible { get; }

    public EqualizerSettings(int barsCount, double responsiveness, double smoothing, ColorRgb color)
        : this(barsCount, responsiveness, smoothing, color,
            targetFps: 60,
            colorCycleEnabled: false,
            colorCycleSpeedHz: 0.2,
            barCornerRadius: 1.0,
            displayMode: MonitorDisplayMode.All,
            specificMonitorDeviceName: null,
            offsetX: 0.0,
            offsetY: 0.0,
            visualizerMode: VisualizerMode.Bars,
            circleDiameter: 400.0)
    {
    }

    public EqualizerSettings(int barsCount, double responsiveness, double smoothing, ColorRgb color,
        int targetFps, bool colorCycleEnabled, double colorCycleSpeedHz, double barCornerRadius,
        MonitorDisplayMode displayMode, string? specificMonitorDeviceName,
        double offsetX, double offsetY)
        : this(barsCount, responsiveness, smoothing, color,
            targetFps, colorCycleEnabled, colorCycleSpeedHz, barCornerRadius,
            displayMode, specificMonitorDeviceName,
            offsetX, offsetY,
            VisualizerMode.Bars, 400.0)
    {
    }

    // Backward-compatible overload without offsets
    public EqualizerSettings(int barsCount, double responsiveness, double smoothing, ColorRgb color,
        int targetFps, bool colorCycleEnabled, double colorCycleSpeedHz, double barCornerRadius,
        MonitorDisplayMode displayMode, string? specificMonitorDeviceName)
        : this(barsCount, responsiveness, smoothing, color,
            targetFps, colorCycleEnabled, colorCycleSpeedHz, barCornerRadius,
            displayMode, specificMonitorDeviceName,
            0.0, 0.0,
            VisualizerMode.Bars, 400.0)
    {
    }

    // Overload with visualizer mode and diameter but without offsets
    public EqualizerSettings(int barsCount, double responsiveness, double smoothing, ColorRgb color,
        int targetFps, bool colorCycleEnabled, double colorCycleSpeedHz, double barCornerRadius,
        MonitorDisplayMode displayMode, string? specificMonitorDeviceName,
        VisualizerMode visualizerMode, double circleDiameter)
        : this(barsCount, responsiveness, smoothing, color,
            targetFps, colorCycleEnabled, colorCycleSpeedHz, barCornerRadius,
            displayMode, specificMonitorDeviceName,
            0.0, 0.0,
            visualizerMode, circleDiameter)
    {
    }

    // Full constructor including offsets and visualizer configuration
    public EqualizerSettings(int barsCount, double responsiveness, double smoothing, ColorRgb color,
        int targetFps, bool colorCycleEnabled, double colorCycleSpeedHz, double barCornerRadius,
        MonitorDisplayMode displayMode, string? specificMonitorDeviceName,
        double offsetX, double offsetY,
        VisualizerMode visualizerMode, double circleDiameter)
        : this(barsCount, responsiveness, smoothing, color,
            targetFps, colorCycleEnabled, colorCycleSpeedHz, barCornerRadius,
            displayMode, specificMonitorDeviceName,
            offsetX, offsetY,
            visualizerMode, circleDiameter, overlayVisible: true)
    {
    }

    // Full constructor including offsets, visualizer configuration, and overlay visibility
    public EqualizerSettings(int barsCount, double responsiveness, double smoothing, ColorRgb color,
        int targetFps, bool colorCycleEnabled, double colorCycleSpeedHz, double barCornerRadius,
        MonitorDisplayMode displayMode, string? specificMonitorDeviceName,
        double offsetX, double offsetY,
        VisualizerMode visualizerMode, double circleDiameter, bool overlayVisible)
    {
        if (barsCount < 8 || barsCount > 256)
            throw new ArgumentOutOfRangeException(nameof(barsCount), "BarsCount must be between 8 and 256.");
        if (responsiveness < 0 || responsiveness > 1)
            throw new ArgumentOutOfRangeException(nameof(responsiveness), "Responsiveness must be between 0 and 1.");
        if (smoothing < 0 || smoothing > 1)
            throw new ArgumentOutOfRangeException(nameof(smoothing), "Smoothing must be between 0 and 1.");
        if (targetFps < 10 || targetFps > 240)
            throw new ArgumentOutOfRangeException(nameof(targetFps), "TargetFps must be between 10 and 240.");
        if (colorCycleSpeedHz < 0 || colorCycleSpeedHz > 10)
            throw new ArgumentOutOfRangeException(nameof(colorCycleSpeedHz), "ColorCycleSpeedHz must be between 0 and 10.");
        if (barCornerRadius < 0 || barCornerRadius > 16)
            throw new ArgumentOutOfRangeException(nameof(barCornerRadius), "BarCornerRadius must be between 0 and 16.");
        if (circleDiameter < 50 || circleDiameter > 5000)
            throw new ArgumentOutOfRangeException(nameof(circleDiameter), "CircleDiameter must be between 50 and 5000.");

        BarsCount = barsCount;
        Responsiveness = responsiveness;
        Smoothing = smoothing;
        Color = color;
        TargetFps = targetFps;
        ColorCycleEnabled = colorCycleEnabled;
        ColorCycleSpeedHz = colorCycleSpeedHz;
        BarCornerRadius = barCornerRadius;
        DisplayMode = displayMode;
        SpecificMonitorDeviceName = specificMonitorDeviceName;
        OffsetX = offsetX;
        OffsetY = offsetY;
        VisualizerMode = visualizerMode;
        CircleDiameter = circleDiameter;
        OverlayVisible = overlayVisible;
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
        offsetY: 0.0,
        visualizerMode: VisualizerMode.Bars,
        circleDiameter: 400.0,
        overlayVisible: true
    );
}
