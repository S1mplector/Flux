namespace Equalizer.Domain;

public readonly struct ColorRgb
{
    public byte R { get; }
    public byte G { get; }
    public byte B { get; }

    public ColorRgb(byte r, byte g, byte b)
    {
        R = r;
        G = g;
        B = b;
    }

    public static ColorRgb Lerp(ColorRgb a, ColorRgb b, double t)
    {
        t = Math.Clamp(t, 0.0, 1.0);
        return new ColorRgb(
            (byte)(a.R + (b.R - a.R) * t),
            (byte)(a.G + (b.G - a.G) * t),
            (byte)(a.B + (b.B - a.B) * t));
    }
}
