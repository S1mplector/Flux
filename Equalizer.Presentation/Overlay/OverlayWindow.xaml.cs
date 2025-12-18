using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Equalizer.Application.Abstractions;
using Equalizer.Application.Models;
using Equalizer.Domain;
using Equalizer.Presentation.Controls;
using Equalizer.Presentation.Widgets;
using SkiaSharp;
using SkiaSharp.Views.Desktop;

namespace Equalizer.Presentation.Overlay;

public partial class OverlayWindow : Window
{
    private readonly IEqualizerService _service;
    private readonly ISettingsPort _settings;
    private readonly WidgetManager? _widgetManager;
    private readonly CancellationTokenSource _cts = new();
    private bool _rendering;
    private readonly SolidColorBrush _barBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0, 255, 128));
    private readonly SolidColorBrush _glowBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0, 255, 128)) { Opacity = 0.35 };
    private DateTime _lastFrame = DateTime.MinValue;
    private double _cyclePhase;
    private double _beatPulse;
    private Task<VisualizerFrame>? _pendingFrameTask;
    private VisualizerFrame? _lastFrameData;
    private float[]? _peaks;
    private readonly SolidColorBrush _peakBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 255, 255));
    private readonly System.Windows.Media.Pen _barPen;
    private readonly System.Windows.Media.Pen _glowPen;
    private readonly TranslateTransform _offset = new TranslateTransform();
    private readonly DispatcherTimer _settingsRefreshTimer;
    private bool _isDragging;
    private System.Windows.Point _dragStartPoint;
    private System.Windows.Point _startOffset;
    private ColorRgb? _quickColorOverride;
    private readonly object _settingsCacheLock = new();
    private EqualizerSettings? _settingsSnapshot;
    private DateTime _settingsSnapshotAt;
    private double _lastMeasuredFps;
    private DateTime _lastFpsSampleAt = DateTime.MinValue;
    private readonly SkiaOverlayRenderer _skiaRenderer = new();
    private RenderingMode _currentRenderingMode = RenderingMode.Cpu;
    private VisualizerFrame? _skiaFrameData;

    public OverlayWindow(IEqualizerService service, ISettingsPort settings, WidgetManager? widgetManager = null)
    {
        _service = service;
        _settings = settings;
        _widgetManager = widgetManager;
        if (_widgetManager != null)
        {
            _ = _widgetManager.LoadLayoutAsync();
        }
        _barPen = new System.Windows.Media.Pen(_barBrush, 1.0)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round
        };
        _glowPen = new System.Windows.Media.Pen(_glowBrush, 1.0)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round
        };
        InitializeComponent();

        Loaded += (_, __) => { System.Windows.Media.CompositionTarget.Rendering += OnRendering; };
        Unloaded += (_, __) => { System.Windows.Media.CompositionTarget.Rendering -= OnRendering; };
        Closed += (_, __) =>
        {
            _cts.Cancel();
            _settingsRefreshTimer.Stop();
        };
        BarsCanvas.RenderTransform = _offset;
        SkiaCanvas.RenderTransform = _offset;
        BarsCanvas.MouseLeftButtonDown += BarsCanvas_MouseLeftButtonDown;
        BarsCanvas.MouseMove += BarsCanvas_MouseMove;
        BarsCanvas.MouseLeftButtonUp += BarsCanvas_MouseLeftButtonUp;
        BarsCanvas.MouseRightButtonUp += BarsCanvas_MouseRightButtonUp;
        SkiaCanvas.MouseLeftButtonDown += BarsCanvas_MouseLeftButtonDown;
        SkiaCanvas.MouseMove += BarsCanvas_MouseMove;
        SkiaCanvas.MouseLeftButtonUp += BarsCanvas_MouseLeftButtonUp;
        SkiaCanvas.MouseRightButtonUp += BarsCanvas_MouseRightButtonUp;
        SaveMoveButton.Click += SaveMoveButton_Click;
        CancelMoveButton.Click += CancelMoveButton_Click;
        QuickApplyButton.Click += QuickApplyButton_Click;
        QuickCancelButton.Click += QuickCancelButton_Click;
        QuickColorR.ValueChanged += QuickColorSlider_ValueChanged;
        QuickColorG.ValueChanged += QuickColorSlider_ValueChanged;
        QuickColorB.ValueChanged += QuickColorSlider_ValueChanged;
        QuickEyedropperButton.Click += QuickEyedropperButton_Click;
        _ = ApplyInitialOffsetAsync();
        _settingsRefreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(250) // Refresh more frequently to keep cache fresh
        };
        _settingsRefreshTimer.Tick += async (_, __) => await RefreshSettingsSnapshotAsync();
        _settingsRefreshTimer.Start();
        _ = RefreshSettingsSnapshotAsync();
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

            if (!TryGetSettingsSnapshot(out var s) || s == null) return;

            var now = DateTime.UtcNow;
            var minIntervalMs = 1000.0 / Math.Clamp(s.TargetFps, 10, 240);
            double dt = 0.0;
            if (_lastFrame != DateTime.MinValue)
            {
                dt = (now - _lastFrame).TotalMilliseconds;
                if (dt < minIntervalMs) return;
            }
            _lastFrame = now;

            if (dt > 0.0)
            {
                var fpsInstant = 1000.0 / dt;
                const double alpha = 0.2; // light smoothing to avoid jitter
                _lastMeasuredFps = _lastMeasuredFps > 0 ? _lastMeasuredFps * (1 - alpha) + fpsInstant * alpha : fpsInstant;
                _lastFpsSampleAt = DateTime.UtcNow;
            }

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

            // Check if rendering mode changed
            if (_currentRenderingMode != s.RenderingMode)
            {
                _currentRenderingMode = s.RenderingMode;
                if (_currentRenderingMode == RenderingMode.Gpu)
                {
                    BarsCanvas.Visibility = Visibility.Collapsed;
                    SkiaCanvas.Visibility = Visibility.Visible;
                }
                else
                {
                    BarsCanvas.Visibility = Visibility.Visible;
                    SkiaCanvas.Visibility = Visibility.Collapsed;
                }
            }

            // GPU rendering mode - use SkiaSharp
            if (_currentRenderingMode == RenderingMode.Gpu)
            {
                _skiaFrameData = vf;
                _skiaRenderer.BeatPulse = _beatPulse;
                _skiaRenderer.CyclePhase = _cyclePhase / 360.0;
                SkiaCanvas.InvalidateVisual();
            }
            else
            {
                // CPU rendering mode - use WPF DrawingContext
                var baseColor = System.Windows.Media.Color.FromRgb(color.R, color.G, color.B);
                var pulsed = LerpColor(baseColor, System.Windows.Media.Colors.White, (float)(0.35 * _beatPulse));
                if (_barBrush.Color != pulsed)
                {
                    _barBrush.Color = pulsed;
                    _glowBrush.Color = pulsed;
                }

                using (var dc = BarsCanvas.RenderOpen())
                {
                    if (s.VisualizerMode == VisualizerMode.Circular)
                    {
                        RenderCircular(dc, vf, data, s, width, height);
                    }
                    else
                    {
                        RenderLinearBars(dc, vf, data, s, width, height, spacing);
                    }
                    
                    // Render additional widgets
                    if (_widgetManager != null)
                    {
                        _widgetManager.UpdateWidgets();
                        _widgetManager.RenderWidgets(dc, width, height);
                    }
                    
                    // Render widget edit overlay
                    if (_widgetManager?.EditMode == true)
                    {
                        _widgetManager.RenderEditOverlay(dc, width, height);
                    }
                }
            }

            // Perf overlay text (optional)
            if (s.PerfOverlayEnabled)
            {
                double fps = _lastMeasuredFps;
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

    private void RenderLinearBars(DrawingContext dc, VisualizerFrame vf, float[] data, EqualizerSettings s, double width, double height, double spacing)
    {
        if (data.Length == 0) return;

        var fade = Math.Clamp(vf.SilenceFade, 0f, 1f);
        var barWidth = Math.Max(1.0, (width - spacing * (data.Length - 1)) / data.Length);
        _glowBrush.Opacity = s.GlowEnabled ? 0.35 : 0.0;
        EnsurePeaks(data.Length);
        var peakBarHeight = Math.Max(2.0, Math.Min(4.0, height * 0.01));
        var cornerRadius = s.BarCornerRadius;
        
        // Gradient colors
        var startColor = s.Color;
        var endColor = s.GradientEndColor;
        var useGradient = s.GradientEnabled;

        for (int i = 0; i < data.Length; i++)
        {
            var scale = 1.0 + 0.12 * vf.Bass + 0.06 * vf.Treble + 0.18 * _beatPulse;
            var h = Math.Max(1.0, data[i] * height * scale * fade);
            var t = data.Length > 1 ? (double)i / (data.Length - 1) : 0.0;
            
            // Per-bar color for gradient mode
            SolidColorBrush barBrush = _barBrush;
            SolidColorBrush glowBrush = _glowBrush;
            if (useGradient)
            {
                var gradientColor = ColorRgb.Lerp(startColor, endColor, t);
                var pulsedGradient = LerpColor(
                    System.Windows.Media.Color.FromRgb(gradientColor.R, gradientColor.G, gradientColor.B),
                    System.Windows.Media.Colors.White,
                    (float)(0.35 * _beatPulse));
                barBrush = new SolidColorBrush(pulsedGradient);
                glowBrush = new SolidColorBrush(pulsedGradient) { Opacity = s.GlowEnabled ? 0.35 : 0.0 };
            }

            double widthScale = 1.0;
            if (s.BeatShapeEnabled)
            {
                double regionWeight = 0.0;
                if (vf.PitchStrength > 0.1f)
                {
                    double center = vf.PitchHue;
                    double region = 0.25;
                    double dist = Math.Abs(t - center);
                    regionWeight = Math.Max(0.0, 1.0 - dist / region);
                }

                double beatFactor = _beatPulse;
                widthScale = 1.0 + 0.4 * beatFactor * (0.5 + 0.5 * regionWeight);
            }

            var w = barWidth * widthScale;
            var left = i * (barWidth + spacing) + (barWidth - w) * 0.5;
            var top = height - h;

            if (s.GlowEnabled)
            {
                var glowW = w * 1.15;
                var glowH = Math.Max(1.0, h * 1.25);
                var glowLeft = left - (glowW - w) * 0.5;
                var glowTop = Math.Max(0.0, height - glowH);
                dc.DrawRoundedRectangle(glowBrush, null, new Rect(glowLeft, glowTop, glowW, glowH), cornerRadius, cornerRadius);
            }

            dc.DrawRoundedRectangle(barBrush, null, new Rect(left, top, w, h), cornerRadius, cornerRadius);

            var amp = (float)Math.Clamp(data[i] * scale * fade, 0.0, 1.0);
            var decayed = _peaks[i] * 0.985f;
            _peaks[i] = Math.Max(decayed, amp);
            var peakH = Math.Max(1.0, _peaks[i] * height * fade);
            var peakTop = Math.Max(0.0, height - peakH - peakBarHeight);
            dc.DrawRectangle(_peakBrush, null, new Rect(left, peakTop, w, peakBarHeight));
        }
    }

    private void RenderCircular(DrawingContext dc, VisualizerFrame vf, float[] data, EqualizerSettings s, double width, double height)
    {
        if (data.Length == 0) return;

        var fade = Math.Clamp(vf.SilenceFade, 0f, 1f);
        var cx = width / 2.0;
        var cy = height / 2.0;
        var maxRadius = Math.Min(width, height) / 2.0;
        var targetRadius = Math.Min(s.CircleDiameter / 2.0, maxRadius * 0.9);

        var innerRadius = targetRadius * 0.55;
        var outerRadius = targetRadius;

        var angleStep = 2.0 * Math.PI / data.Length;
        var arcPerBar = targetRadius * angleStep;
        var thickness = arcPerBar * 0.55;
        thickness = Math.Clamp(thickness, 1.5, targetRadius * 0.15);
        var glowEnabled = s.GlowEnabled;
        
        // Gradient colors
        var startColor = s.Color;
        var endColor = s.GradientEndColor;
        var useGradient = s.GradientEnabled;

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
            
            var t = data.Length > 1 ? (double)i / (data.Length - 1) : 0.0;
            
            // Per-bar pen for gradient mode
            System.Windows.Media.Pen barPen = _barPen;
            System.Windows.Media.Pen glowPen = _glowPen;
            if (useGradient)
            {
                var gradientColor = ColorRgb.Lerp(startColor, endColor, t);
                var pulsedGradient = LerpColor(
                    System.Windows.Media.Color.FromRgb(gradientColor.R, gradientColor.G, gradientColor.B),
                    System.Windows.Media.Colors.White,
                    (float)(0.35 * _beatPulse));
                var brush = new SolidColorBrush(pulsedGradient);
                barPen = new System.Windows.Media.Pen(brush, thickness);
                glowPen = new System.Windows.Media.Pen(new SolidColorBrush(pulsedGradient) { Opacity = 0.3 }, thickness * 1.5);
            }

            double localThickness = thickness;
            if (s.BeatShapeEnabled)
            {
                double pos = (double)i / data.Length;
                double center = vf.PitchHue;
                double region = 0.25;
                double dist = Math.Abs(pos - center);
                dist = Math.Min(dist, 1.0 - dist);
                double regionWeight = Math.Max(0.0, 1.0 - dist / region);
                double beatFactor = _beatPulse;
                localThickness = thickness * (1.0 + 0.5 * beatFactor * (0.5 + 0.5 * regionWeight));
            }

            if (glowEnabled)
            {
                glowPen.Thickness = localThickness * 1.5;
                dc.DrawLine(glowPen, new System.Windows.Point(x1, y1), new System.Windows.Point(x2, y2));
            }

            barPen.Thickness = localThickness;
            dc.DrawLine(barPen, new System.Windows.Point(x1, y1), new System.Windows.Point(x2, y2));
        }
    }

    private void EnsurePeaks(int count)
    {
        if (_peaks == null || _peaks.Length != count)
        {
            _peaks = count > 0 ? new float[count] : null;
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
            pitchReactiveColorEnabled: s.PitchReactiveColorEnabled,
            s.BassEmphasis, s.TrebleEmphasis,
            beatShapeEnabled: s.BeatShapeEnabled, s.GlowEnabled, s.PerfOverlayEnabled);
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
                pitchReactiveColorEnabled: s.PitchReactiveColorEnabled,
                s.BassEmphasis, s.TrebleEmphasis,
                beatShapeEnabled: s.BeatShapeEnabled, s.GlowEnabled, s.PerfOverlayEnabled);

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

    private bool TryGetSettingsSnapshot(out EqualizerSettings? settings)
    {
        lock (_settingsCacheLock)
        {
            // Extend staleness tolerance to 2s to avoid frame drops during transient delays
            // Settings don't change that frequently, so this is safe
            var fresh = _settingsSnapshot != null &&
                        (DateTime.UtcNow - _settingsSnapshotAt).TotalMilliseconds < 2000;
            settings = fresh ? _settingsSnapshot : null;
            return fresh;
        }
    }

    private async Task RefreshSettingsSnapshotAsync()
    {
        try
        {
            var s = await _settings.GetAsync();
            lock (_settingsCacheLock)
            {
                _settingsSnapshot = s;
                _settingsSnapshotAt = DateTime.UtcNow;
            }
        }
        catch
        {
            // Ignore transient settings load errors; keep last snapshot
        }
    }

    public double LastMeasuredFps => _lastMeasuredFps;
    public DateTime LastFpsSampleAt => _lastFpsSampleAt;

    private void SkiaCanvas_PaintSurface(object sender, SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        var width = e.Info.Width;
        var height = e.Info.Height;

        if (_skiaFrameData == null || !TryGetSettingsSnapshot(out var s) || s == null)
        {
            canvas.Clear(SKColors.Transparent);
            return;
        }

        _skiaRenderer.Render(canvas, width, height, _skiaFrameData, s, _quickColorOverride);
    }
}

public sealed class VisualizerSurface : FrameworkElement
{
    private readonly VisualCollection _visuals;
    public DrawingVisual Visual { get; }

    public VisualizerSurface()
    {
        Visual = new DrawingVisual();
        _visuals = new VisualCollection(this) { Visual };
    }

    public DrawingContext RenderOpen() => Visual.RenderOpen();

    protected override int VisualChildrenCount => _visuals.Count;

    protected override Visual GetVisualChild(int index) => _visuals[index];

    protected override HitTestResult HitTestCore(PointHitTestParameters hitTestParameters)
    {
        return new PointHitTestResult(this, hitTestParameters.HitPoint);
    }

    protected override System.Windows.Size MeasureOverride(System.Windows.Size availableSize) => availableSize;

    protected override System.Windows.Size ArrangeOverride(System.Windows.Size finalSize) => finalSize;
}
