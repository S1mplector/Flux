using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Equalizer.Application.Abstractions;
using Equalizer.Domain.Widgets;
using WpfApplication = System.Windows.Application;
using WpfPoint = System.Windows.Point;

namespace Equalizer.Presentation.Widgets;

public sealed class DateWidgetRenderer : IWidgetRenderer
{
    public string WidgetTypeId => "date";
    public string DisplayName => "Date";

    private DateTime _currentDate = DateTime.Today;

    public void Update(TimeSpan elapsed)
    {
        _currentDate = DateTime.Today;
    }

    public void Render(object drawingContext, WidgetConfig config, double canvasWidth, double canvasHeight)
    {
        if (drawingContext is not DrawingContext dc) return;

        var settings = DateWidgetSettings.FromConfig(config);
        var dateText = _currentDate.ToString(settings.DateFormat, CultureInfo.CurrentCulture);

        var typeface = new Typeface(new System.Windows.Media.FontFamily(settings.FontFamily), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        var brush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(settings.TextColor.R, settings.TextColor.G, settings.TextColor.B))
        {
            Opacity = settings.Opacity
        };

        var formattedText = new FormattedText(
            dateText,
            CultureInfo.CurrentCulture,
            System.Windows.FlowDirection.LeftToRight,
            typeface,
            settings.FontSize,
            brush,
            VisualTreeHelper.GetDpi(WpfApplication.Current.MainWindow).PixelsPerDip);

        var (x, y) = CalculatePosition(config, formattedText.Width, formattedText.Height, canvasWidth, canvasHeight);

        if (settings.ShowShadow)
        {
            var shadowBrush = new SolidColorBrush(Colors.Black) { Opacity = 0.5 * settings.Opacity };
            var shadowText = new FormattedText(dateText, CultureInfo.CurrentCulture, System.Windows.FlowDirection.LeftToRight,
                typeface, settings.FontSize, shadowBrush, VisualTreeHelper.GetDpi(WpfApplication.Current.MainWindow).PixelsPerDip);
            dc.DrawText(shadowText, new WpfPoint(x + 1, y + 1));
        }

        dc.DrawText(formattedText, new WpfPoint(x, y));
    }

    private static (double x, double y) CalculatePosition(WidgetConfig config, double textWidth, double textHeight, double canvasWidth, double canvasHeight)
    {
        double x = config.X;
        double y = config.Y;

        switch (config.Anchor)
        {
            case WidgetAnchor.TopCenter:
                x = (canvasWidth - textWidth) / 2 + config.X;
                break;
            case WidgetAnchor.TopRight:
                x = canvasWidth - textWidth - config.X;
                break;
            case WidgetAnchor.MiddleLeft:
                y = (canvasHeight - textHeight) / 2 + config.Y;
                break;
            case WidgetAnchor.Center:
                x = (canvasWidth - textWidth) / 2 + config.X;
                y = (canvasHeight - textHeight) / 2 + config.Y;
                break;
            case WidgetAnchor.MiddleRight:
                x = canvasWidth - textWidth - config.X;
                y = (canvasHeight - textHeight) / 2 + config.Y;
                break;
            case WidgetAnchor.BottomLeft:
                y = canvasHeight - textHeight - config.Y;
                break;
            case WidgetAnchor.BottomCenter:
                x = (canvasWidth - textWidth) / 2 + config.X;
                y = canvasHeight - textHeight - config.Y;
                break;
            case WidgetAnchor.BottomRight:
                x = canvasWidth - textWidth - config.X;
                y = canvasHeight - textHeight - config.Y;
                break;
        }

        return (x, y);
    }
}
