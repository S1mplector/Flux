using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Equalizer.Application.Abstractions;
using Equalizer.Domain.Widgets;
using WpfApplication = System.Windows.Application;
using WpfPoint = System.Windows.Point;

namespace Equalizer.Presentation.Widgets;

public sealed class SystemInfoWidgetRenderer : IWidgetRenderer
{
    public string WidgetTypeId => "systeminfo";
    public string DisplayName => "System Info";

    private readonly Process _process;
    private double _cpuUsage;
    private double _ramUsageMb;
    private double _ramTotalMb;
    private TimeSpan _lastCpuTime;
    private DateTime _lastSample;
    private TimeSpan _updateAccumulator;

    public SystemInfoWidgetRenderer()
    {
        _process = Process.GetCurrentProcess();
        _lastCpuTime = _process.TotalProcessorTime;
        _lastSample = DateTime.UtcNow;
        _ramTotalMb = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / (1024.0 * 1024.0);
    }

    public void Update(TimeSpan elapsed)
    {
        _updateAccumulator += elapsed;
        if (_updateAccumulator.TotalMilliseconds < 1000) return;
        _updateAccumulator = TimeSpan.Zero;

        try
        {
            _process.Refresh();
            var now = DateTime.UtcNow;
            var cpuTime = _process.TotalProcessorTime;
            var elapsedTime = (now - _lastSample).TotalMilliseconds;
            var cpuUsedMs = (cpuTime - _lastCpuTime).TotalMilliseconds;
            
            if (elapsedTime > 0)
            {
                _cpuUsage = Math.Min(100, cpuUsedMs / elapsedTime / Environment.ProcessorCount * 100);
            }
            
            _lastCpuTime = cpuTime;
            _lastSample = now;
            _ramUsageMb = _process.WorkingSet64 / (1024.0 * 1024.0);
        }
        catch { }
    }

    public void Render(object drawingContext, WidgetConfig config, double canvasWidth, double canvasHeight)
    {
        if (drawingContext is not DrawingContext dc) return;

        var settings = SystemInfoWidgetSettings.FromConfig(config);
        var typeface = new Typeface(new System.Windows.Media.FontFamily(settings.FontFamily), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        var textBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(settings.TextColor.R, settings.TextColor.G, settings.TextColor.B));
        var accentBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(settings.AccentColor.R, settings.AccentColor.G, settings.AccentColor.B));
        var bgBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(60, 0, 0, 0));

        var (baseX, baseY) = CalculateBasePosition(config, canvasWidth, canvasHeight);
        double y = baseY;
        double lineHeight = settings.FontSize + 8;
        double barWidth = config.Width > 0 ? config.Width - 20 : 180;

        if (settings.ShowCpu)
        {
            DrawMetric(dc, $"CPU: {_cpuUsage:0.0}%", _cpuUsage / 100.0, baseX, y, barWidth, settings, typeface, textBrush, accentBrush, bgBrush);
            y += lineHeight + (settings.ShowBars ? settings.BarHeight + 4 : 0);
        }

        if (settings.ShowRam)
        {
            var ramPercent = _ramTotalMb > 0 ? _ramUsageMb / _ramTotalMb : 0;
            DrawMetric(dc, $"RAM: {_ramUsageMb:0} MB", ramPercent, baseX, y, barWidth, settings, typeface, textBrush, accentBrush, bgBrush);
        }
    }

    private void DrawMetric(DrawingContext dc, string text, double percent, double x, double y, double barWidth,
        SystemInfoWidgetSettings settings, Typeface typeface, SolidColorBrush textBrush, SolidColorBrush accentBrush, SolidColorBrush bgBrush)
    {
        var formattedText = new FormattedText(
            text,
            CultureInfo.CurrentCulture,
            System.Windows.FlowDirection.LeftToRight,
            typeface,
            settings.FontSize,
            textBrush,
            VisualTreeHelper.GetDpi(WpfApplication.Current.MainWindow).PixelsPerDip);

        dc.DrawText(formattedText, new WpfPoint(x, y));

        if (settings.ShowBars)
        {
            var barY = y + formattedText.Height + 2;
            dc.DrawRoundedRectangle(bgBrush, null, new Rect(x, barY, barWidth, settings.BarHeight), 2, 2);
            var fillWidth = Math.Max(0, Math.Min(barWidth, barWidth * percent));
            if (fillWidth > 0)
            {
                dc.DrawRoundedRectangle(accentBrush, null, new Rect(x, barY, fillWidth, settings.BarHeight), 2, 2);
            }
        }
    }

    private static (double x, double y) CalculateBasePosition(WidgetConfig config, double canvasWidth, double canvasHeight)
    {
        double x = config.X;
        double y = config.Y;
        double widgetWidth = config.Width > 0 ? config.Width : 200;
        double widgetHeight = config.Height > 0 ? config.Height : 100;

        switch (config.Anchor)
        {
            case WidgetAnchor.TopCenter:
                x = (canvasWidth - widgetWidth) / 2 + config.X;
                break;
            case WidgetAnchor.TopRight:
                x = canvasWidth - widgetWidth - config.X;
                break;
            case WidgetAnchor.MiddleLeft:
                y = (canvasHeight - widgetHeight) / 2 + config.Y;
                break;
            case WidgetAnchor.Center:
                x = (canvasWidth - widgetWidth) / 2 + config.X;
                y = (canvasHeight - widgetHeight) / 2 + config.Y;
                break;
            case WidgetAnchor.MiddleRight:
                x = canvasWidth - widgetWidth - config.X;
                y = (canvasHeight - widgetHeight) / 2 + config.Y;
                break;
            case WidgetAnchor.BottomLeft:
                y = canvasHeight - widgetHeight - config.Y;
                break;
            case WidgetAnchor.BottomCenter:
                x = (canvasWidth - widgetWidth) / 2 + config.X;
                y = canvasHeight - widgetHeight - config.Y;
                break;
            case WidgetAnchor.BottomRight:
                x = canvasWidth - widgetWidth - config.X;
                y = canvasHeight - widgetHeight - config.Y;
                break;
        }

        return (x, y);
    }
}
