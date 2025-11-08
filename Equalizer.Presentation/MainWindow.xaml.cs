using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Equalizer.Application.Abstractions;

namespace Equalizer.Presentation;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    private readonly IEqualizerService _service;
    private readonly List<System.Windows.Shapes.Rectangle> _bars = new();
    private readonly CancellationTokenSource _cts = new();
    private bool _rendering;
    private DateTime _lastFrame = DateTime.MinValue;
    private Task<float[]>? _pendingBars;
    private float[]? _lastBars;

    public MainWindow(IEqualizerService service)
    {
        _service = service;
        InitializeComponent();
        Loaded += (_, __) => System.Windows.Media.CompositionTarget.Rendering += OnRendering;
        Unloaded += (_, __) => System.Windows.Media.CompositionTarget.Rendering -= OnRendering;
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
            var now = DateTime.UtcNow;
            var minIntervalMs = 1000.0 / 60.0; // target ~60 FPS
            if (_lastFrame != DateTime.MinValue)
            {
                var dt = (now - _lastFrame).TotalMilliseconds;
                if (dt < minIntervalMs) return;
            }
            _lastFrame = now;

            if (_pendingBars == null || _pendingBars.IsCompleted)
            {
                _pendingBars = _service.GetBarsAsync(_cts.Token);
            }
            if (_pendingBars != null && _pendingBars.IsCompletedSuccessfully)
            {
                _lastBars = _pendingBars.Result;
            }

            var data = _lastBars;
            if (data == null) return;
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