using System;
using System.Windows;
using SkiaSharp;
using Flux.Application.Models;
using Flux.Domain;
using Flux.Domain.Widgets;
using Flux.Presentation.Widgets;

namespace Flux.Presentation.Overlay;

public sealed class SkiaOverlayRenderer : IDisposable
{
    private float[]? _peaks;
    private double _beatPulse;
    private double _cyclePhase;
    private bool _disposed;
    private readonly SKPaint _barFillPaint = new SKPaint
    {
        IsAntialias = true,
        Style = SKPaintStyle.Fill
    };
    private readonly SKPaint _glowFillPaint = new SKPaint
    {
        IsAntialias = true,
        Style = SKPaintStyle.Fill,
        MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 4)
    };
    private readonly SKPaint _peakFillPaint = new SKPaint
    {
        IsAntialias = true,
        Style = SKPaintStyle.Fill,
        Color = SKColors.White
    };
    private readonly SKPaint _barStrokePaint = new SKPaint
    {
        IsAntialias = true,
        Style = SKPaintStyle.Stroke,
        StrokeCap = SKStrokeCap.Round
    };
    private readonly SKPaint _glowStrokePaint = new SKPaint
    {
        IsAntialias = true,
        Style = SKPaintStyle.Stroke,
        StrokeCap = SKStrokeCap.Round,
        MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 2)
    };
    
    public double BeatPulse
    {
        get => _beatPulse;
        set => _beatPulse = value;
    }
    
    public double CyclePhase
    {
        get => _cyclePhase;
        set => _cyclePhase = value;
    }

    public void Render(SKCanvas canvas, int width, int height, VisualizerFrame frame, FluxSettings settings, ColorRgb? colorOverride = null)
    {
        canvas.Clear(SKColors.Transparent);
        
        var data = frame.Bars;
        if (data.Length == 0) return;
        
        var fade = Math.Clamp(frame.SilenceFade, 0f, 1f);
        var baseColor = colorOverride ?? settings.Color;
        
        // Apply color cycling if enabled
        if (settings.ColorCycleEnabled && colorOverride == null)
        {
            baseColor = GetCycledColor(_cyclePhase);
        }
        
        // Apply beat pulse to color
        var pulsedColor = LerpColor(baseColor, new ColorRgb(255, 255, 255), (float)(0.35 * _beatPulse));
        
        if (settings.VisualizerMode == VisualizerMode.Circular)
        {
            RenderCircular(canvas, width, height, frame, data, settings, pulsedColor, fade);
        }
        else
        {
            RenderLinearBars(canvas, width, height, frame, data, settings, pulsedColor, fade);
        }
    }

    private void RenderLinearBars(SKCanvas canvas, int width, int height, VisualizerFrame frame, float[] data, 
        FluxSettings settings, ColorRgb color, float fade)
    {
        var spacing = 2.0f;
        var barWidth = Math.Max(1.0f, (width - spacing * (data.Length - 1)) / data.Length);
        EnsurePeaks(data.Length);
        var peakBarHeight = Math.Max(2.0f, Math.Min(4.0f, height * 0.01f));
        var cornerRadius = (float)settings.BarCornerRadius;
        
        // Gradient colors
        var startColor = settings.Color;
        var endColor = settings.GradientEndColor;
        var useGradient = settings.GradientEnabled;

        var barPaint = _barFillPaint;
        var glowPaint = _glowFillPaint;
        var peakPaint = _peakFillPaint;

        for (int i = 0; i < data.Length; i++)
        {
            var scale = 1.0f + 0.12f * frame.Bass + 0.06f * frame.Treble + 0.18f * (float)_beatPulse;
            var h = Math.Max(1.0f, data[i] * height * scale * fade);
            var t = data.Length > 1 ? (float)i / (data.Length - 1) : 0.0f;

            float widthScale = 1.0f;
            if (settings.BeatShapeEnabled)
            {
                float regionWeight = 0.0f;
                if (frame.PitchStrength > 0.1f)
                {
                    float center = frame.PitchHue;
                    float region = 0.25f;
                    float dist = Math.Abs(t - center);
                    regionWeight = Math.Max(0.0f, 1.0f - dist / region);
                }
                float beatFactor = (float)_beatPulse;
                widthScale = 1.0f + 0.4f * beatFactor * (0.5f + 0.5f * regionWeight);
            }

            var w = barWidth * widthScale;
            var left = i * (barWidth + spacing) + (barWidth - w) * 0.5f;
            var top = height - h;
            
            // Determine bar color
            ColorRgb barColor;
            if (useGradient)
            {
                barColor = ColorRgb.Lerp(startColor, endColor, t);
                barColor = LerpColor(barColor, new ColorRgb(255, 255, 255), (float)(0.35 * _beatPulse));
            }
            else
            {
                barColor = color;
            }
            
            var skColor = new SKColor(barColor.R, barColor.G, barColor.B);
            barPaint.Color = skColor;

            // Draw glow
            if (settings.GlowEnabled)
            {
                var glowW = w * 1.15f;
                var glowH = Math.Max(1.0f, h * 1.25f);
                var glowLeft = left - (glowW - w) * 0.5f;
                var glowTop = Math.Max(0.0f, height - glowH);
                glowPaint.Color = skColor.WithAlpha(90);
                canvas.DrawRoundRect(new SKRect(glowLeft, glowTop, glowLeft + glowW, glowTop + glowH), cornerRadius, cornerRadius, glowPaint);
            }

            // Draw bar
            canvas.DrawRoundRect(new SKRect(left, top, left + w, top + h), cornerRadius, cornerRadius, barPaint);

            // Update and draw peaks
            var amp = Math.Clamp(data[i] * scale * fade, 0.0f, 1.0f);
            var decayed = _peaks![i] * 0.985f;
            _peaks[i] = Math.Max(decayed, amp);
            var peakH = Math.Max(1.0f, _peaks[i] * height * fade);
            var peakTop = Math.Max(0.0f, height - peakH - peakBarHeight);
            canvas.DrawRect(new SKRect(left, peakTop, left + w, peakTop + peakBarHeight), peakPaint);
        }
    }

    private void RenderCircular(SKCanvas canvas, int width, int height, VisualizerFrame frame, float[] data, 
        FluxSettings settings, ColorRgb color, float fade)
    {
        var cx = width / 2.0f;
        var cy = height / 2.0f;
        var maxRadius = Math.Min(width, height) / 2.0f;
        var targetRadius = Math.Min((float)settings.CircleDiameter / 2.0f, maxRadius * 0.9f);

        var innerRadius = targetRadius * 0.55f;
        var outerRadius = targetRadius;

        var angleStep = 2.0f * (float)Math.PI / data.Length;
        var arcPerBar = targetRadius * angleStep;
        var thickness = arcPerBar * 0.55f;
        thickness = Math.Clamp(thickness, 1.5f, targetRadius * 0.15f);
        
        // Gradient colors
        var startColor = settings.Color;
        var endColor = settings.GradientEndColor;
        var useGradient = settings.GradientEnabled;

        var barPaint = _barStrokePaint;
        var glowPaint = _glowStrokePaint;

        for (int i = 0; i < data.Length; i++)
        {
            var scale = 1.0f + 0.12f * frame.Bass + 0.06f * frame.Treble + 0.18f * (float)_beatPulse;
            var amp = Math.Clamp(data[i] * scale * fade, 0.0f, 1.0f);
            var radius = innerRadius + (outerRadius - innerRadius) * amp;

            var angle = 2.0f * (float)Math.PI * i / data.Length;
            var cos = (float)Math.Cos(angle);
            var sin = (float)Math.Sin(angle);

            var x1 = cx + cos * innerRadius;
            var y1 = cy + sin * innerRadius;
            var x2 = cx + cos * radius;
            var y2 = cy + sin * radius;
            
            var t = data.Length > 1 ? (float)i / (data.Length - 1) : 0.0f;

            float localThickness = thickness;
            if (settings.BeatShapeEnabled)
            {
                float pos = (float)i / data.Length;
                float center = frame.PitchHue;
                float region = 0.25f;
                float dist = Math.Abs(pos - center);
                dist = Math.Min(dist, 1.0f - dist);
                float regionWeight = Math.Max(0.0f, 1.0f - dist / region);
                float beatFactor = (float)_beatPulse;
                localThickness = thickness * (1.0f + 0.5f * beatFactor * (0.5f + 0.5f * regionWeight));
            }
            
            // Determine bar color
            ColorRgb barColor;
            if (useGradient)
            {
                barColor = ColorRgb.Lerp(startColor, endColor, t);
                barColor = LerpColor(barColor, new ColorRgb(255, 255, 255), (float)(0.35 * _beatPulse));
            }
            else
            {
                barColor = color;
            }
            
            var skColor = new SKColor(barColor.R, barColor.G, barColor.B);
            barPaint.Color = skColor;
            barPaint.StrokeWidth = localThickness;

            // Draw glow
            if (settings.GlowEnabled)
            {
                glowPaint.Color = skColor.WithAlpha(76);
                glowPaint.StrokeWidth = localThickness * 1.5f;
                canvas.DrawLine(x1, y1, x2, y2, glowPaint);
            }

            // Draw bar
            canvas.DrawLine(x1, y1, x2, y2, barPaint);
        }
    }

    private void EnsurePeaks(int count)
    {
        if (_peaks == null || _peaks.Length != count)
        {
            _peaks = new float[count];
        }
    }

    private static ColorRgb GetCycledColor(double phase)
    {
        var hue = (phase % 1.0) * 360.0;
        return HsvToRgb(hue, 1.0, 1.0);
    }

    private static ColorRgb HsvToRgb(double h, double s, double v)
    {
        var c = v * s;
        var x = c * (1 - Math.Abs((h / 60) % 2 - 1));
        var m = v - c;
        double r, g, b;
        if (h < 60) { r = c; g = x; b = 0; }
        else if (h < 120) { r = x; g = c; b = 0; }
        else if (h < 180) { r = 0; g = c; b = x; }
        else if (h < 240) { r = 0; g = x; b = c; }
        else if (h < 300) { r = x; g = 0; b = c; }
        else { r = c; g = 0; b = x; }
        return new ColorRgb((byte)((r + m) * 255), (byte)((g + m) * 255), (byte)((b + m) * 255));
    }

    private static ColorRgb LerpColor(ColorRgb a, ColorRgb b, float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return new ColorRgb(
            (byte)(a.R + (b.R - a.R) * t),
            (byte)(a.G + (b.G - a.G) * t),
            (byte)(a.B + (b.B - a.B) * t));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _barFillPaint.Dispose();
        _glowFillPaint.Dispose();
        _peakFillPaint.Dispose();
        _barStrokePaint.Dispose();
        _glowStrokePaint.Dispose();
    }
}
