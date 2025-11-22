using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Equalizer.Application.Abstractions;
using Equalizer.Application.Models;
using Equalizer.Domain;
using Equalizer.Presentation.Controls;

namespace Equalizer.Presentation.Overlay;

public partial class OverlayWindow : Window
{
    private readonly IEqualizerService _service;
    private readonly ISettingsPort _settings;
    private readonly List<System.Windows.Shapes.Rectangle> _bars = new();
    private readonly List<System.Windows.Shapes.Rectangle> _peakBars = new();
    private readonly List<System.Windows.Shapes.Rectangle> _glowBars = new();
    private readonly CancellationTokenSource _cts = new();
    private bool _rendering;
    private SolidColorBrush _barBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0, 255, 128));
    private DateTime _lastFrame = DateTime.MinValue;
    private double _cyclePhase;
    private double _beatPulse;
    private Task<VisualizerFrame>? _pendingFrameTask;
    private VisualizerFrame? _lastFrameData;
    private float[]? _peaks;
    private SolidColorBrush _peakBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 255, 255));
    private readonly TranslateTransform _offset = new TranslateTransform();
    private bool _isDragging;
    private System.Windows.Point _dragStartPoint;
    private System.Windows.Point _startOffset;
    private ColorRgb? _quickColorOverride;
    private readonly List<System.Windows.Shapes.Line> _circleLines = new();
    private readonly List<System.Windows.Shapes.Line> _circleGlowLines = new();
    private VisualizerMode? _lastMode;
    private int _lastBarCount = -1;
    private readonly object _settingsCacheLock = new();
    private EqualizerSettings? _settingsSnapshot;
    private DateTime _settingsSnapshotAt;
    private Task<EqualizerSettings>? _settingsFetchTask;

    public OverlayWindow(IEqualizerService service, ISettingsPort settings)
    {
        _service = service;
        _settings = settings;
        InitializeComponent();

        Loaded += (_, __) => { System.Windows.Media.CompositionTarget.Rendering += OnRendering; };
        Unloaded += (_, __) => { System.Windows.Media.CompositionTarget.Rendering -= OnRendering; };
        Closed += (_, __) => _cts.Cancel();
        SizeChanged += (_, __) => LayoutBars();
        BarsCanvas.RenderTransform = _offset;
        BarsCanvas.MouseLeftButtonDown += BarsCanvas_MouseLeftButtonDown;
        BarsCanvas.MouseMove += BarsCanvas_MouseMove;
        BarsCanvas.MouseLeftButtonUp += BarsCanvas_MouseLeftButtonUp;
        BarsCanvas.MouseRightButtonUp += BarsCanvas_MouseRightButtonUp;
        SaveMoveButton.Click += SaveMoveButton_Click;
        CancelMoveButton.Click += CancelMoveButton_Click;
        QuickApplyButton.Click += QuickApplyButton_Click;
        QuickCancelButton.Click += QuickCancelButton_Click;
        QuickColorR.ValueChanged += QuickColorSlider_ValueChanged;
        QuickColorG.ValueChanged += QuickColorSlider_ValueChanged;
        QuickColorB.ValueChanged += QuickColorSlider_ValueChanged;
        QuickEyedropperButton.Click += QuickEyedropperButton_Click;
        _ = ApplyInitialOffsetAsync();
    }

    private async Task ApplyInitialOffsetAsync()
    {
        var s = await _settings.GetAsync();
        _offset.X = s.OffsetX;
        _offset.Y = s.OffsetY;
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        _ = RenderAsync();
    }

    private async Task RenderAsync()
    {
        if (_rendering) return;
        _rendering = true;
        try
        {
            if (!IsVisible) return;

            var s = await GetSettingsSnapshotAsync();

            var now = DateTime.UtcNow;
            var minIntervalMs = 1000.0 / Math.Clamp(s.TargetFps, 10, 240);
            double dt = 0.0;
            if (_lastFrame != DateTime.MinValue)
            {
                dt = (now - _lastFrame).TotalMilliseconds;
                if (dt < minIntervalMs) return;
            }
            _lastFrame = now;

            var frameStart = DateTime.UtcNow;

            if (_pendingFrameTask == null || _pendingFrameTask.IsCompleted)
            {
                _pendingFrameTask = _service.GetVisualizerFrameAsync(_cts.Token);
            }
            if (_pendingFrameTask != null && _pendingFrameTask.IsCompletedSuccessfully)
            {
                _lastFrameData = _pendingFrameTask.Result;
            }

            var vf = _lastFrameData;
            if (vf == null) return;
            var data = vf.Bars;

            var width = BarsCanvas.ActualWidth;
            var height = BarsCanvas.ActualHeight;
            if (width <= 0 || height <= 0) return;

            var spacing = 2.0;

            var color = s.Color;
            if (s.ColorCycleEnabled)
            {
                _cyclePhase += s.ColorCycleSpeedHz * (minIntervalMs / 1000.0) * 360.0;
                _cyclePhase %= 360.0;
                var rgb = HsvToRgb(_cyclePhase, 1.0, 1.0);
                color = new ColorRgb((byte)rgb.r, (byte)rgb.g, (byte)rgb.b);
            }
            // Beat pulse
            if (vf.IsBeat) _beatPulse = Math.Min(1.0, _beatPulse + vf.BeatStrength * 1.2);
            _beatPulse *= 0.94; // slightly slower decay for a more visible pulse

            var baseColor = System.Windows.Media.Color.FromRgb(color.R, color.G, color.B);
            var pulsed = LerpColor(baseColor, System.Windows.Media.Colors.White, (float)(0.35 * _beatPulse));
            if (_barBrush.Color != pulsed) _barBrush.Color = pulsed;

            if (s.VisualizerMode == VisualizerMode.Circular)
            {
                if (_lastMode != VisualizerMode.Circular || _circleLines.Count != data.Length)
                {
                    ClearAllShapes();
                    EnsureCircularLines(data.Length, s.GlowEnabled);
                    _lastMode = VisualizerMode.Circular;
                    _lastBarCount = data.Length;
                }
                else if (_lastBarCount != data.Length)
                {
                    EnsureCircularLines(data.Length, s.GlowEnabled);
                    _lastBarCount = data.Length;
                }
                RenderCircular(vf, data, s, width, height);
            }
            else
            {
                if (_lastMode != VisualizerMode.Bars)
                {
                    ClearAllShapes();
                    _lastMode = VisualizerMode.Bars;
                    _lastBarCount = -1;
                }
                EnsureBars(data.Length);
                _lastBarCount = data.Length;
                RenderLinearBars(vf, data, s, width, height, spacing);
            }

            // Perf overlay text (optional)
            if (s.PerfOverlayEnabled)
            {
                double fps = dt > 0.0 ? 1000.0 / dt : 0.0;
                var procMs = (DateTime.UtcNow - frameStart).TotalMilliseconds;
                PerfText.Visibility = Visibility.Visible;
                PerfText.Text = $"FPS ~ {fps:0}    frame {procMs:0.0} ms";
            }
            else
            {
                PerfText.Visibility = Visibility.Collapsed;
            }
        }
        finally
        {
            _rendering = false;
        }
    }

    private void RenderLinearBars(VisualizerFrame vf, float[] data, EqualizerSettings s, double width, double height, double spacing)
    {
        var fade = Math.Clamp(vf.SilenceFade, 0f, 1f);
        var barWidth = Math.Max(1.0, (width - spacing * (data.Length - 1)) / data.Length);
        for (int i = 0; i < data.Length; i++)
        {
            // Slight bass/treble emphasis and stronger beat pulse scaling
            var scale = 1.0 + 0.12 * vf.Bass + 0.06 * vf.Treble + 0.18 * _beatPulse;
            var h = Math.Max(1.0, data[i] * height * scale * fade);
            var t = data.Length > 1 ? (double)i / (data.Length - 1) : 0.0;

            // Beat/pitch-driven width modulation (optional)
            double widthScale = 1.0;
            if (s.BeatShapeEnabled)
            {
                double regionWeight = 0.0;
                if (vf.PitchStrength > 0.1f)
                {
                    double center = vf.PitchHue; // 0..1 across bars
                    double region = 0.25;        // fraction of bars affected around the pitch
                    double dist = Math.Abs(t - center);
                    regionWeight = Math.Max(0.0, 1.0 - dist / region);
                }

                double beatFactor = _beatPulse; // 0..1
                widthScale = 1.0 + 0.4 * beatFactor * (0.5 + 0.5 * regionWeight);
            }

            var w = barWidth * widthScale;
            var left = i * (barWidth + spacing) + (barWidth - w) * 0.5;
            var top = height - h;
            var rect = _bars[i];
            rect.Width = w;
            rect.Height = h;
            rect.RadiusX = s.BarCornerRadius;
            rect.RadiusY = s.BarCornerRadius;
            Canvas.SetLeft(rect, left);
            Canvas.SetTop(rect, top);

            // Optional glow bar behind the main bar
            if (_glowBars.Count == data.Length)
            {
                var glowRect = _glowBars[i];
                if (s.GlowEnabled)
                {
                    glowRect.Opacity = 0.35;
                    glowRect.Width = w * 1.15;
                    var glowH = Math.Max(1.0, h * 1.25);
                    glowRect.Height = glowH;
                    Canvas.SetLeft(glowRect, left - (glowRect.Width - w) * 0.5);
                    Canvas.SetTop(glowRect, Math.Max(0.0, height - glowH));
                }
                else
                {
                    glowRect.Opacity = 0.0;
                }
            }

            if (_peaks == null || _peaks.Length != data.Length) _peaks = new float[data.Length];
            var amp = (float)Math.Clamp(data[i] * scale * fade, 0.0, 1.0);
            var decayed = _peaks[i] * 0.985f;
            _peaks[i] = Math.Max(decayed, amp);
            var peakH = Math.Max(1.0, _peaks[i] * height * fade);
            var peakRect = _peakBars[i];
            peakRect.Width = w;
            peakRect.Height = Math.Max(2.0, Math.Min(4.0, height * 0.01));
            Canvas.SetLeft(peakRect, left);
            Canvas.SetTop(peakRect, Math.Max(0.0, height - peakH - peakRect.Height));
        }
    }

    private void RenderCircular(VisualizerFrame vf, float[] data, EqualizerSettings s, double width, double height)
    {
        if (data.Length == 0) return;

        var fade = Math.Clamp(vf.SilenceFade, 0f, 1f);
        if (fade <= 0.001f) return;

        var cx = width / 2.0;
        var cy = height / 2.0;
        var maxRadius = Math.Min(width, height) / 2.0;
        var targetRadius = Math.Min(s.CircleDiameter / 2.0, maxRadius * 0.9);

        // Keep a fairly open center and a thinner active ring for a cleaner look
        var innerRadius = targetRadius * 0.55;
        var outerRadius = targetRadius;

        // Derive stroke thickness from angular spacing so bars don't merge into a solid ring
        var angleStep = 2.0 * Math.PI / data.Length;
        var arcPerBar = targetRadius * angleStep; // arc length at target radius per bar
        var thickness = arcPerBar * 0.55;        // use ~55% of available arc to leave visible gaps
        thickness = Math.Clamp(thickness, 1.5, targetRadius * 0.15);

        for (int i = 0; i < data.Length; i++)
        {
            var scale = 1.0 + 0.12 * vf.Bass + 0.06 * vf.Treble + 0.18 * _beatPulse;
            var amp = Math.Clamp(data[i] * scale * fade, 0.0, 1.0);
            var radius = innerRadius + (outerRadius - innerRadius) * amp;

            var angle = 2.0 * Math.PI * i / data.Length;
            var cos = Math.Cos(angle);
            var sin = Math.Sin(angle);

            var x1 = cx + cos * innerRadius;
            var y1 = cy + sin * innerRadius;
            var x2 = cx + cos * radius;
            var y2 = cy + sin * radius;

            // Beat/pitch-driven thickness modulation (optional)
            double localThickness = thickness;
            if (s.BeatShapeEnabled)
            {
                double pos = (double)i / data.Length; // 0..1 around circle
                double center = vf.PitchHue;
                double region = 0.25;
                double dist = Math.Abs(pos - center);
                dist = Math.Min(dist, 1.0 - dist); // wrap around circle
                double regionWeight = Math.Max(0.0, 1.0 - dist / region);
                double beatFactor = _beatPulse;
                localThickness = thickness * (1.0 + 0.5 * beatFactor * (0.5 + 0.5 * regionWeight));
            }

            // Optional glow: a thicker, low-opacity line behind the main bar
            if (s.GlowEnabled && _circleGlowLines.Count == data.Length)
            {
                var glowLine = _circleGlowLines[i];
                glowLine.X1 = x1;
                glowLine.Y1 = y1;
                glowLine.X2 = x2;
                glowLine.Y2 = y2;
                glowLine.StrokeThickness = localThickness * 1.5;
                glowLine.Opacity = 0.3;
                if (!ReferenceEquals(glowLine.Stroke, _barBrush)) glowLine.Stroke = _barBrush;
            }
            else if (_circleGlowLines.Count == data.Length)
            {
                _circleGlowLines[i].Opacity = 0.0;
            }

            if (_circleLines.Count == data.Length)
            {
                var line = _circleLines[i];
                line.X1 = x1;
                line.Y1 = y1;
                line.X2 = x2;
                line.Y2 = y2;
                line.StrokeThickness = localThickness;
                if (!ReferenceEquals(line.Stroke, _barBrush)) line.Stroke = _barBrush;
            }
        }
    }

    private void EnsureBars(int count)
    {
        if (_bars.Count == count) return;
        BarsCanvas.Children.Clear();
        _bars.Clear();
        _peakBars.Clear();
        _glowBars.Clear();
        _peaks = count > 0 ? new float[count] : null;

        for (int i = 0; i < count; i++)
        {
            var glow = new System.Windows.Shapes.Rectangle
            {
                Fill = _barBrush,
                RadiusX = 1,
                RadiusY = 1,
                Opacity = 0.0
            };
            _glowBars.Add(glow);
            BarsCanvas.Children.Add(glow);

            var r = new System.Windows.Shapes.Rectangle
            {
                Fill = _barBrush,
                RadiusX = 1,
                RadiusY = 1
            };
            _bars.Add(r);
            BarsCanvas.Children.Add(r);
            var peak = new System.Windows.Shapes.Rectangle
            {
                Fill = _peakBrush,
                Opacity = 0.85
            };
            _peakBars.Add(peak);
            BarsCanvas.Children.Add(peak);
        }
        LayoutBars();
    }

    private void LayoutBars()
    {
        if (_bars.Count == 0) return;
        var width = BarsCanvas.ActualWidth;
        var height = BarsCanvas.ActualHeight;
        if (width <= 0 || height <= 0) return;

        var spacing = 2.0;
        var barWidth = Math.Max(1.0, (width - spacing * (_bars.Count - 1)) / _bars.Count);
        for (int i = 0; i < _bars.Count; i++)
        {
            var left = i * (barWidth + spacing);
            var rect = _bars[i];
            rect.Width = barWidth;
            Canvas.SetLeft(rect, left);
            var peak = _peakBars[i];
            peak.Width = barWidth;
            Canvas.SetLeft(peak, left);
        }
    }

    private void ClearAllShapes()
    {
        BarsCanvas.Children.Clear();
        _bars.Clear();
        _peakBars.Clear();
        _glowBars.Clear();
        _circleLines.Clear();
        _circleGlowLines.Clear();
        _peaks = null;
    }

    private void EnsureCircularLines(int count, bool glowEnabled)
    {
        if (_circleLines.Count == count && (!glowEnabled || _circleGlowLines.Count == count))
            return;

        ClearAllShapes();

        for (int i = 0; i < count; i++)
        {
            if (glowEnabled)
            {
                var glowLine = new System.Windows.Shapes.Line
                {
                    Stroke = _barBrush,
                    StrokeThickness = 1.0,
                    StrokeStartLineCap = PenLineCap.Round,
                    StrokeEndLineCap = PenLineCap.Round,
                    Opacity = 0.0
                };
                _circleGlowLines.Add(glowLine);
                BarsCanvas.Children.Add(glowLine);
            }

            var line = new System.Windows.Shapes.Line
            {
                Stroke = _barBrush,
                StrokeThickness = 1.0,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round
            };
            _circleLines.Add(line);
            BarsCanvas.Children.Add(line);
        }
    }

    public void ResetOffset()
    {
        _offset.X = 0.0;
        _offset.Y = 0.0;
        ConfirmPanel.Visibility = Visibility.Collapsed;
    }

    private void BarsCanvas_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        _isDragging = true;
        _dragStartPoint = e.GetPosition(this);
        _startOffset = new System.Windows.Point(_offset.X, _offset.Y);
        BarsCanvas.CaptureMouse();
        ConfirmPanel.Visibility = Visibility.Collapsed;
    }

    private void BarsCanvas_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!_isDragging) return;
        var p = e.GetPosition(this);
        var dx = p.X - _dragStartPoint.X;
        var dy = p.Y - _dragStartPoint.Y;
        _offset.X = _startOffset.X + dx;
        _offset.Y = _startOffset.Y + dy;
    }

    private void BarsCanvas_MouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (!_isDragging) return;
        _isDragging = false;
        BarsCanvas.ReleaseMouseCapture();
        ConfirmPanel.Visibility = Visibility.Visible;
    }

    private async void BarsCanvas_MouseRightButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (QuickSettingsPanel.Visibility == Visibility.Visible)
        {
            QuickSettingsPanel.Visibility = Visibility.Collapsed;
            return;
        }

        var s = await _settings.GetAsync();
        QuickBarsSlider.Value = s.BarsCount;
        QuickColorCycleEnabled.IsChecked = s.ColorCycleEnabled;
        SetQuickColor(s.Color);

        QuickSettingsPanel.Visibility = Visibility.Visible;
    }

    private async void SaveMoveButton_Click(object sender, RoutedEventArgs e)
    {
        var s = await _settings.GetAsync();
        var updated = new EqualizerSettings(
            s.BarsCount, s.Responsiveness, s.Smoothing, s.Color,
            s.TargetFps, s.ColorCycleEnabled, s.ColorCycleSpeedHz, s.BarCornerRadius,
            s.DisplayMode, s.SpecificMonitorDeviceName,
            _offset.X, _offset.Y,
            s.VisualizerMode, s.CircleDiameter,
            s.OverlayVisible, s.FadeOnSilenceEnabled,
            s.SilenceFadeOutSeconds, s.SilenceFadeInSeconds,
            pitchReactiveColorEnabled: false,
            s.BassEmphasis, s.TrebleEmphasis,
            beatShapeEnabled: false, s.GlowEnabled, s.PerfOverlayEnabled);
        await _settings.SaveAsync(updated);
        ConfirmPanel.Visibility = Visibility.Collapsed;
    }

    private async void CancelMoveButton_Click(object sender, RoutedEventArgs e)
    {
        var s = await _settings.GetAsync();
        _offset.X = s.OffsetX;
        _offset.Y = s.OffsetY;
        ConfirmPanel.Visibility = Visibility.Collapsed;
    }

    private async void QuickApplyButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var s = await _settings.GetAsync();
            int bars = (int)QuickBarsSlider.Value;
            bool cycle = QuickColorCycleEnabled.IsChecked == true;

            var color = _quickColorOverride ?? s.Color;

            var updated = new EqualizerSettings(
                bars,
                s.Responsiveness,
                s.Smoothing,
                color,
                s.TargetFps,
                cycle,
                s.ColorCycleSpeedHz,
                s.BarCornerRadius,
                s.DisplayMode,
                s.SpecificMonitorDeviceName,
                s.OffsetX,
                s.OffsetY,
                s.VisualizerMode,
                s.CircleDiameter,
                s.OverlayVisible,
                s.FadeOnSilenceEnabled,
                s.SilenceFadeOutSeconds,
                s.SilenceFadeInSeconds,
                pitchReactiveColorEnabled: false,
                s.BassEmphasis, s.TrebleEmphasis,
                beatShapeEnabled: false, s.GlowEnabled, s.PerfOverlayEnabled);

            await _settings.SaveAsync(updated);
            QuickSettingsPanel.Visibility = Visibility.Collapsed;
        }
        catch (Exception)
        {
            // Swallow invalid quick settings to avoid intrusive dialogs from the overlay
        }
    }

    private void QuickCancelButton_Click(object sender, RoutedEventArgs e)
    {
        QuickSettingsPanel.Visibility = Visibility.Collapsed;
    }

    private void QuickEyedropperButton_Click(object? sender, RoutedEventArgs e)
    {
        var current = _quickColorOverride ?? new ColorRgb(0, 255, 128);
        var picker = new ColorPickerWindow(current)
        {
            Owner = this
        };
        if (picker.ShowDialog() == true)
        {
            SetQuickColor(picker.SelectedColor);
        }
    }

    private void QuickColorSlider_ValueChanged(object? sender, RoutedPropertyChangedEventArgs<double> e)
    {
        var rgb = new ColorRgb(
            (byte)QuickColorR.Value,
            (byte)QuickColorG.Value,
            (byte)QuickColorB.Value);
        ApplyQuickColor(rgb);
    }

    private void QuickColorPreview_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        var current = _quickColorOverride ?? new ColorRgb(0, 255, 128);
        var picker = new ColorPickerWindow(current)
        {
            Owner = this
        };
        if (picker.ShowDialog() == true)
        {
            SetQuickColor(picker.SelectedColor);
        }
    }

    private void SetQuickColor(ColorRgb rgb)
    {
        _quickColorOverride = rgb;
        QuickColorR.Value = rgb.R;
        QuickColorG.Value = rgb.G;
        QuickColorB.Value = rgb.B;
        ApplyQuickColor(rgb);
    }

    private void ApplyQuickColor(ColorRgb rgb)
    {
        _quickColorOverride = rgb;
        var preview = System.Windows.Media.Color.FromRgb(rgb.R, rgb.G, rgb.B);
        QuickColorPreview.Background = new SolidColorBrush(preview);
    }

    private static System.Windows.Media.Color LerpColor(System.Windows.Media.Color a, System.Windows.Media.Color b, float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        byte r = (byte)(a.R + (b.R - a.R) * t);
        byte g = (byte)(a.G + (b.G - a.G) * t);
        byte bl = (byte)(a.B + (b.B - a.B) * t);
        return System.Windows.Media.Color.FromRgb(r, g, bl);
    }

    private static (int r, int g, int b) HsvToRgb(double h, double s, double v)
    {
        h = (h % 360 + 360) % 360;
        int i = (int)Math.Floor(h / 60.0) % 6;
        double f = h / 60.0 - Math.Floor(h / 60.0);
        double p = v * (1 - s);
        double q = v * (1 - f * s);
        double t = v * (1 - (1 - f) * s);
        double r = 0, g = 0, b = 0;
        switch (i)
        {
            case 0: r = v; g = t; b = p; break;
            case 1: r = q; g = v; b = p; break;
            case 2: r = p; g = v; b = t; break;
            case 3: r = p; g = q; b = v; break;
            case 4: r = t; g = p; b = v; break;
            case 5: r = v; g = p; b = q; break;
        }
        return ((int)(r * 255), (int)(g * 255), (int)(b * 255));
    }

    private Task<EqualizerSettings> GetSettingsSnapshotAsync()
    {
        lock (_settingsCacheLock)
        {
            if (_settingsSnapshot != null && (DateTime.UtcNow - _settingsSnapshotAt).TotalMilliseconds < 500)
            {
                return Task.FromResult(_settingsSnapshot);
            }

            if (_settingsFetchTask == null || _settingsFetchTask.IsCompleted)
            {
                _settingsFetchTask = FetchSettingsAsync();
            }

            return _settingsFetchTask;
        }
    }

    private async Task<EqualizerSettings> FetchSettingsAsync()
    {
        var s = await _settings.GetAsync();
        lock (_settingsCacheLock)
        {
            _settingsSnapshot = s;
            _settingsSnapshotAt = DateTime.UtcNow;
        }
        return s;
    }
}
