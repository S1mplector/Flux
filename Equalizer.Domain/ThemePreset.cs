namespace Equalizer.Domain;

public sealed class ThemePreset
{
    public string Name { get; set; } = "Untitled";
    public ColorRgb Color { get; set; } = new(0, 255, 128);
    public ColorRgb GradientEndColor { get; set; } = new(255, 0, 128);
    public bool GradientEnabled { get; set; }
    public bool ColorCycleEnabled { get; set; }
    public double ColorCycleSpeedHz { get; set; } = 0.2;
    public double BarCornerRadius { get; set; } = 1.0;
    public bool GlowEnabled { get; set; }
    public bool BeatShapeEnabled { get; set; }
    public double BassEmphasis { get; set; } = 1.0;
    public double TrebleEmphasis { get; set; } = 1.0;
    
    public static ThemePreset FromSettings(EqualizerSettings s, string name) => new()
    {
        Name = name,
        Color = s.Color,
        GradientEndColor = s.GradientEndColor,
        GradientEnabled = s.GradientEnabled,
        ColorCycleEnabled = s.ColorCycleEnabled,
        ColorCycleSpeedHz = s.ColorCycleSpeedHz,
        BarCornerRadius = s.BarCornerRadius,
        GlowEnabled = s.GlowEnabled,
        BeatShapeEnabled = s.BeatShapeEnabled,
        BassEmphasis = s.BassEmphasis,
        TrebleEmphasis = s.TrebleEmphasis
    };
    
    public static readonly ThemePreset[] BuiltIn = new[]
    {
        new ThemePreset { Name = "Neon Green", Color = new(0, 255, 128), GradientEnabled = false, GlowEnabled = true },
        new ThemePreset { Name = "Sunset", Color = new(255, 100, 0), GradientEndColor = new(255, 0, 128), GradientEnabled = true, GlowEnabled = true },
        new ThemePreset { Name = "Ocean", Color = new(0, 150, 255), GradientEndColor = new(0, 255, 200), GradientEnabled = true, GlowEnabled = true },
        new ThemePreset { Name = "Fire", Color = new(255, 50, 0), GradientEndColor = new(255, 200, 0), GradientEnabled = true, BeatShapeEnabled = true, GlowEnabled = true },
        new ThemePreset { Name = "Purple Rain", Color = new(150, 0, 255), GradientEndColor = new(255, 0, 150), GradientEnabled = true, GlowEnabled = true },
        new ThemePreset { Name = "Minimal White", Color = new(255, 255, 255), GradientEnabled = false, GlowEnabled = false },
        new ThemePreset { Name = "Rainbow", Color = new(255, 0, 0), ColorCycleEnabled = true, ColorCycleSpeedHz = 0.3, GlowEnabled = true }
    };
}
