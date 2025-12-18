using Equalizer.Domain.Widgets;

namespace Equalizer.Application.Abstractions;

public interface IWidgetLayoutPort
{
    Task<WidgetLayout> GetLayoutAsync();
    Task SaveLayoutAsync(WidgetLayout layout);
}

public interface IWidgetRenderer
{
    string WidgetTypeId { get; }
    string DisplayName { get; }
    void Render(object drawingContext, WidgetConfig config, double canvasWidth, double canvasHeight);
    void Update(TimeSpan elapsed);
}

public interface IWidgetRegistry
{
    IReadOnlyList<IWidgetRenderer> GetAllRenderers();
    IWidgetRenderer? GetRenderer(string widgetTypeId);
    void Register(IWidgetRenderer renderer);
}
