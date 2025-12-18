using Equalizer.Application.Abstractions;

namespace Equalizer.Presentation.Widgets;

public sealed class WidgetRegistry : IWidgetRegistry
{
    private readonly Dictionary<string, IWidgetRenderer> _renderers = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<IWidgetRenderer> GetAllRenderers() => _renderers.Values.ToList();

    public IWidgetRenderer? GetRenderer(string widgetTypeId)
    {
        return _renderers.TryGetValue(widgetTypeId, out var renderer) ? renderer : null;
    }

    public void Register(IWidgetRenderer renderer)
    {
        _renderers[renderer.WidgetTypeId] = renderer;
    }
}
