using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Equalizer.Application.Abstractions;

namespace Equalizer.Presentation.Overlay;

public partial class OverlayWindow : Window
{
    private readonly IEqualizerService _service;
    private readonly List<System.Windows.Shapes.Rectangle> _bars = new();
    private readonly DispatcherTimer _timer = new();
    private readonly CancellationTokenSource _cts = new();
    private bool _rendering;

    public OverlayWindow(IEqualizerService service)
    {
        _service = service;
        InitializeComponent();

        _timer.Interval = TimeSpan.FromMilliseconds(33); // ~30 FPS
        _timer.Tick += async (_, __) => await RenderAsync();
        Loaded += (_, __) => { _timer.Start(); System.Windows.Media.CompositionTarget.Rendering += OnRendering; };
        Unloaded += (_, __) => { _timer.Stop(); System.Windows.Media.CompositionTarget.Rendering -= OnRendering; };
        Closed += (_, __) => _cts.Cancel();
        SizeChanged += (_, __) => LayoutBars();
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
            var data = await _service.GetBarsAsync(_cts.Token);
            EnsureBars(data.Length);

            var width = BarsCanvas.ActualWidth;
            var height = BarsCanvas.ActualHeight;
            if (width <= 0 || height <= 0) return;

            var spacing = 2.0;
            var barWidth = Math.Max(1.0, (width - spacing * (data.Length - 1)) / data.Length);

            for (int i = 0; i < data.Length; i++)
            {
                var h = Math.Max(1.0, data[i] * height);
                var left = i * (barWidth + spacing);
                var top = height - h;
                var rect = _bars[i];
                rect.Width = barWidth;
                rect.Height = h;
                Canvas.SetLeft(rect, left);
                Canvas.SetTop(rect, top);
            }
        }
        finally
        {
            _rendering = false;
        }
    }

    private void EnsureBars(int count)
    {
        if (_bars.Count == count) return;
        BarsCanvas.Children.Clear();
        _bars.Clear();

        var brush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0, 255, 128));
        for (int i = 0; i < count; i++)
        {
            var r = new System.Windows.Shapes.Rectangle
            {
                Fill = brush,
                RadiusX = 1,
                RadiusY = 1
            };
            _bars.Add(r);
            BarsCanvas.Children.Add(r);
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
        }
    }
}
