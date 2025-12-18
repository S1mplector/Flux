using System.Windows.Media;
using Flux.Application.Abstractions;
using Flux.Domain.Widgets;
using WpfPoint = System.Windows.Point;
using WpfRect = System.Windows.Rect;

namespace Flux.Presentation.Widgets;

public sealed class WidgetManager
{
    private readonly IWidgetRegistry _registry;
    private readonly IWidgetLayoutPort _layoutPort;
    private WidgetLayout? _currentLayout;
    private DateTime _lastUpdate;
    private bool _editMode;
    private WidgetConfig? _selectedWidget;
    private WidgetConfig? _draggingWidget;
    private WpfPoint _dragStartPoint;
    private double _dragStartX;
    private double _dragStartY;

    public event Action? LayoutChanged;

    public WidgetManager(IWidgetRegistry registry, IWidgetLayoutPort layoutPort)
    {
        _registry = registry;
        _layoutPort = layoutPort;
        _lastUpdate = DateTime.UtcNow;
    }
    
    public bool EditMode
    {
        get => _editMode;
        set
        {
            _editMode = value;
            if (!value)
            {
                _selectedWidget = null;
                _draggingWidget = null;
            }
        }
    }
    
    public WidgetConfig? SelectedWidget => _selectedWidget;

    public async Task LoadLayoutAsync()
    {
        _currentLayout = await _layoutPort.GetLayoutAsync();
    }

    public async Task SaveLayoutAsync()
    {
        if (_currentLayout != null)
        {
            await _layoutPort.SaveLayoutAsync(_currentLayout);
        }
    }

    public WidgetLayout? GetCurrentLayout() => _currentLayout;

    public void AddWidget(WidgetConfig config)
    {
        _currentLayout ??= new WidgetLayout();
        _currentLayout.Widgets.Add(config);
    }

    public void RemoveWidget(string widgetId)
    {
        _currentLayout?.Widgets.RemoveAll(w => w.Id == widgetId);
    }

    public void UpdateWidgets()
    {
        var now = DateTime.UtcNow;
        var elapsed = now - _lastUpdate;
        _lastUpdate = now;

        foreach (var renderer in _registry.GetAllRenderers())
        {
            renderer.Update(elapsed);
        }
    }

    public void RenderWidgets(DrawingContext dc, double canvasWidth, double canvasHeight, string? monitorDeviceName = null)
    {
        if (_currentLayout == null) return;

        foreach (var widgetConfig in _currentLayout.Widgets)
        {
            if (!widgetConfig.IsEnabled) continue;
            
            // Filter by monitor if specified
            if (!string.IsNullOrEmpty(widgetConfig.MonitorDeviceName) &&
                !string.IsNullOrEmpty(monitorDeviceName) &&
                !string.Equals(widgetConfig.MonitorDeviceName, monitorDeviceName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var renderer = _registry.GetRenderer(widgetConfig.WidgetTypeId);
            renderer?.Render(dc, widgetConfig, canvasWidth, canvasHeight);
        }
    }

    public IReadOnlyList<(string TypeId, string DisplayName)> GetAvailableWidgetTypes()
    {
        return _registry.GetAllRenderers()
            .Select(r => (r.WidgetTypeId, r.DisplayName))
            .ToList();
    }
    
    public WidgetConfig? HitTest(WpfPoint point, double canvasWidth, double canvasHeight, string? monitorDeviceName = null)
    {
        if (_currentLayout == null) return null;
        
        // Test in reverse order (top-most first)
        for (int i = _currentLayout.Widgets.Count - 1; i >= 0; i--)
        {
            var widget = _currentLayout.Widgets[i];
            if (!widget.IsEnabled) continue;
            if (!string.IsNullOrEmpty(monitorDeviceName) &&
                !string.IsNullOrEmpty(widget.MonitorDeviceName) &&
                !string.Equals(widget.MonitorDeviceName, monitorDeviceName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            
            var bounds = GetWidgetBounds(widget, canvasWidth, canvasHeight);
            if (bounds.Contains(point))
            {
                return widget;
            }
        }
        return null;
    }
    
    public WpfRect GetWidgetBounds(WidgetConfig widget, double canvasWidth, double canvasHeight)
    {
        double w = widget.Width > 0 ? widget.Width : 200;
        double h = widget.Height > 0 ? widget.Height : 60;
        
        double x = widget.X;
        double y = widget.Y;
        
        switch (widget.Anchor)
        {
            case WidgetAnchor.TopCenter:
                x = (canvasWidth - w) / 2 + widget.X;
                break;
            case WidgetAnchor.TopRight:
                x = canvasWidth - w - widget.X;
                break;
            case WidgetAnchor.MiddleLeft:
                y = (canvasHeight - h) / 2 + widget.Y;
                break;
            case WidgetAnchor.Center:
                x = (canvasWidth - w) / 2 + widget.X;
                y = (canvasHeight - h) / 2 + widget.Y;
                break;
            case WidgetAnchor.MiddleRight:
                x = canvasWidth - w - widget.X;
                y = (canvasHeight - h) / 2 + widget.Y;
                break;
            case WidgetAnchor.BottomLeft:
                y = canvasHeight - h - widget.Y;
                break;
            case WidgetAnchor.BottomCenter:
                x = (canvasWidth - w) / 2 + widget.X;
                y = canvasHeight - h - widget.Y;
                break;
            case WidgetAnchor.BottomRight:
                x = canvasWidth - w - widget.X;
                y = canvasHeight - h - widget.Y;
                break;
        }
        
        return new WpfRect(x, y, w, h);
    }
    
    public void StartDrag(WpfPoint point, double canvasWidth, double canvasHeight, string? monitorDeviceName = null)
    {
        if (!_editMode) return;
        
        var widget = HitTest(point, canvasWidth, canvasHeight, monitorDeviceName);
        if (widget != null)
        {
            if (!string.IsNullOrEmpty(monitorDeviceName))
            {
                widget.MonitorDeviceName = monitorDeviceName;
            }
            _selectedWidget = widget;
            _draggingWidget = widget;
            _dragStartPoint = point;
            _dragStartX = widget.X;
            _dragStartY = widget.Y;
        }
    }
    
    public void UpdateDrag(WpfPoint point)
    {
        if (_draggingWidget == null) return;
        
        var dx = point.X - _dragStartPoint.X;
        var dy = point.Y - _dragStartPoint.Y;
        
        // Update position based on anchor type
        switch (_draggingWidget.Anchor)
        {
            case WidgetAnchor.TopRight:
            case WidgetAnchor.MiddleRight:
            case WidgetAnchor.BottomRight:
                _draggingWidget.X = _dragStartX - dx;
                break;
            default:
                _draggingWidget.X = _dragStartX + dx;
                break;
        }
        
        switch (_draggingWidget.Anchor)
        {
            case WidgetAnchor.BottomLeft:
            case WidgetAnchor.BottomCenter:
            case WidgetAnchor.BottomRight:
                _draggingWidget.Y = _dragStartY - dy;
                break;
            default:
                _draggingWidget.Y = _dragStartY + dy;
                break;
        }
        
        LayoutChanged?.Invoke();
    }
    
    public void EndDrag()
    {
        _draggingWidget = null;
    }
    
    public void SelectWidget(WpfPoint point, double canvasWidth, double canvasHeight, string? monitorDeviceName = null)
    {
        if (!_editMode) return;
        _selectedWidget = HitTest(point, canvasWidth, canvasHeight, monitorDeviceName);
    }
    
    public void RenderEditOverlay(DrawingContext dc, double canvasWidth, double canvasHeight, string? monitorDeviceName = null)
    {
        if (!_editMode || _currentLayout == null) return;
        
        var selectionPen = new System.Windows.Media.Pen(System.Windows.Media.Brushes.Cyan, 2) { DashStyle = DashStyles.Dash };
        var handleBrush = System.Windows.Media.Brushes.Cyan;
        
        foreach (var widget in _currentLayout.Widgets)
        {
            if (!widget.IsEnabled) continue;
            if (!string.IsNullOrEmpty(monitorDeviceName) &&
                !string.IsNullOrEmpty(widget.MonitorDeviceName) &&
                !string.Equals(widget.MonitorDeviceName, monitorDeviceName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            
            var bounds = GetWidgetBounds(widget, canvasWidth, canvasHeight);
            
            // Draw selection border for selected widget
            if (widget == _selectedWidget)
            {
                dc.DrawRectangle(null, selectionPen, bounds);
                
                // Draw corner handles
                const double handleSize = 8;
                dc.DrawRectangle(handleBrush, null, new WpfRect(bounds.Left - handleSize/2, bounds.Top - handleSize/2, handleSize, handleSize));
                dc.DrawRectangle(handleBrush, null, new WpfRect(bounds.Right - handleSize/2, bounds.Top - handleSize/2, handleSize, handleSize));
                dc.DrawRectangle(handleBrush, null, new WpfRect(bounds.Left - handleSize/2, bounds.Bottom - handleSize/2, handleSize, handleSize));
                dc.DrawRectangle(handleBrush, null, new WpfRect(bounds.Right - handleSize/2, bounds.Bottom - handleSize/2, handleSize, handleSize));
            }
            else
            {
                // Draw subtle border for other widgets
                var dimPen = new System.Windows.Media.Pen(new SolidColorBrush(System.Windows.Media.Color.FromArgb(80, 255, 255, 255)), 1);
                dc.DrawRectangle(null, dimPen, bounds);
            }
        }
    }
}
