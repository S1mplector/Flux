namespace Equalizer.Domain.Widgets;

public enum WidgetType
{
    Equalizer,
    Clock,
    Date,
    SystemInfo,
    Weather,
    Custom
}

public interface IWidget
{
    string Id { get; }
    string Name { get; }
    WidgetType Type { get; }
    bool IsEnabled { get; set; }
    WidgetPosition Position { get; set; }
    WidgetSize Size { get; set; }
}

public record WidgetPosition(double X, double Y, WidgetAnchor Anchor = WidgetAnchor.TopLeft);

public record WidgetSize(double Width, double Height);

public enum WidgetAnchor
{
    TopLeft,
    TopCenter,
    TopRight,
    MiddleLeft,
    Center,
    MiddleRight,
    BottomLeft,
    BottomCenter,
    BottomRight
}
