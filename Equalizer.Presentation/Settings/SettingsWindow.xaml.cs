using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Equalizer.Application.Abstractions;
using Equalizer.Domain;
using Equalizer.Presentation.Controls;
using Forms = System.Windows.Forms;

namespace Equalizer.Presentation.Settings;

public partial class SettingsWindow : Window
{
    private readonly ISettingsPort _settings;
    private readonly Overlay.IOverlayManager _overlay;
    private readonly DispatcherTimer _resourceTimer;
    private readonly Process _currentProcess;
    private TimeSpan _lastCpuTime;
    private DateTime _lastCpuSample;

    public SettingsWindow(ISettingsPort settings, Overlay.IOverlayManager overlay)
    {
        _settings = settings;
        _overlay = overlay;
        InitializeComponent();
        Loaded += OnLoaded;
        SaveButton.Click += OnSave;
        CancelButton.Click += (_, __) => Close();

        BarsSlider.ValueChanged += (_, __) => BarsValue.Text = ((int)BarsSlider.Value).ToString();
        RespSlider.ValueChanged += (_, __) => RespValue.Text = RespSlider.Value.ToString("0.00");
        SmoothSlider.ValueChanged += (_, __) => SmoothValue.Text = SmoothSlider.Value.ToString("0.00");
        ColorR.ValueChanged += (_, __) => ColorRValue.Text = ((int)ColorR.Value).ToString();
        ColorG.ValueChanged += (_, __) => ColorGValue.Text = ((int)ColorG.Value).ToString();
        ColorB.ValueChanged += (_, __) => ColorBValue.Text = ((int)ColorB.Value).ToString();
        FpsSlider.ValueChanged += (_, __) => FpsValue.Text = ((int)FpsSlider.Value).ToString();
        ColorCycleSpeed.ValueChanged += (_, __) => ColorCycleSpeedValue.Text = ColorCycleSpeed.Value.ToString("0.00");
        CornerRadiusSlider.ValueChanged += (_, __) => CornerRadiusValue.Text = CornerRadiusSlider.Value.ToString("0.0");
        DisplayModeCombo.SelectionChanged += OnDisplayModeChanged;
        PickColorButton.Click += OnPickColor;
        ProfileCombo.SelectionChanged += OnProfileChanged;
        FadeOutSlider.ValueChanged += (_, __) => FadeOutValue.Text = FadeOutSlider.Value.ToString("0.00");
        FadeInSlider.ValueChanged += (_, __) => FadeInValue.Text = FadeInSlider.Value.ToString("0.00");

        _currentProcess = Process.GetCurrentProcess();
        _lastCpuTime = _currentProcess.TotalProcessorTime;
        _lastCpuSample = DateTime.UtcNow;

        _resourceTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _resourceTimer.Tick += ResourceTimer_Tick;
        _resourceTimer.Start();

        Closed += (_, __) => _resourceTimer.Stop();
    }

    private void ResourceTimer_Tick(object? sender, EventArgs e)
    {
        try
        {
            _currentProcess.Refresh();
            var now = DateTime.UtcNow;
            var cpuTime = _currentProcess.TotalProcessorTime;

            var cpuDeltaMs = (cpuTime - _lastCpuTime).TotalMilliseconds;
            var wallDeltaMs = (now - _lastCpuSample).TotalMilliseconds;
            double cpuPercent = 0.0;
            if (wallDeltaMs > 10)
            {
                cpuPercent = cpuDeltaMs / (wallDeltaMs * Environment.ProcessorCount) * 100.0;
            }

            _lastCpuTime = cpuTime;
            _lastCpuSample = now;

            var memMb = _currentProcess.WorkingSet64 / (1024.0 * 1024.0);

            CpuUsageText.Text = $"CPU: {cpuPercent:0.0}%";
            MemoryUsageText.Text = $"RAM: {memMb:0.0} MB";
        }
        catch
        {
            // Non-fatal: best-effort diagnostics only
        }
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        var s = await _settings.GetAsync();
        BarsSlider.Value = s.BarsCount;
        BarsValue.Text = s.BarsCount.ToString();
        RespSlider.Value = s.Responsiveness;
        RespValue.Text = s.Responsiveness.ToString("0.00");
        SmoothSlider.Value = s.Smoothing;
        SmoothValue.Text = s.Smoothing.ToString("0.00");
        ColorR.Value = s.Color.R;
        ColorG.Value = s.Color.G;
        ColorB.Value = s.Color.B;
        ColorRValue.Text = s.Color.R.ToString();
        ColorGValue.Text = s.Color.G.ToString();
        ColorBValue.Text = s.Color.B.ToString();

        FpsSlider.Value = s.TargetFps;
        FpsValue.Text = s.TargetFps.ToString();
        ColorCycleEnabled.IsChecked = s.ColorCycleEnabled;
        ColorCycleSpeed.Value = s.ColorCycleSpeedHz;
        ColorCycleSpeedValue.Text = s.ColorCycleSpeedHz.ToString("0.00");
        CornerRadiusSlider.Value = s.BarCornerRadius;
        CornerRadiusValue.Text = s.BarCornerRadius.ToString("0.0");

        // Populate monitors
        MonitorCombo.Items.Clear();
        foreach (var screen in Forms.Screen.AllScreens)
        {
            var item = new ComboBoxItem { Content = screen.DeviceName, Tag = screen.DeviceName };
            MonitorCombo.Items.Add(item);
            if (!string.IsNullOrEmpty(s.SpecificMonitorDeviceName) &&
                string.Equals(screen.DeviceName, s.SpecificMonitorDeviceName, StringComparison.OrdinalIgnoreCase))
            {
                MonitorCombo.SelectedItem = item;
            }
        }
        if (MonitorCombo.SelectedItem == null && Forms.Screen.PrimaryScreen != null)
        {
            var primary = Forms.Screen.PrimaryScreen.DeviceName;
            foreach (ComboBoxItem item in MonitorCombo.Items)
            {
                if (string.Equals((string)item.Tag, primary, StringComparison.OrdinalIgnoreCase))
                {
                    MonitorCombo.SelectedItem = item;
                    break;
                }
            }
        }

        // Display mode
        SetDisplayModeSelection(s.DisplayMode);
        MonitorCombo.IsEnabled = s.DisplayMode == MonitorDisplayMode.Specific;

        // Visualizer mode & circle diameter
        SetVisualizerModeSelection(s.VisualizerMode);
        CircleDiameterSlider.Value = s.CircleDiameter;
        CircleDiameterValue.Text = s.CircleDiameter.ToString("0");

        // Fade on silence
        FadeOnSilenceEnabledCheckBox.IsChecked = s.FadeOnSilenceEnabled;

        // Fade timings
        FadeOutSlider.Value = s.SilenceFadeOutSeconds;
        FadeOutValue.Text = s.SilenceFadeOutSeconds.ToString("0.00");
        FadeInSlider.Value = s.SilenceFadeInSeconds;
        FadeInValue.Text = s.SilenceFadeInSeconds.ToString("0.00");

        // Performance profile (infer from current values)
        SetProfileFromCurrentValues();
    }

    private async void OnSave(object sender, RoutedEventArgs e)
    {
        try
        {
            var current = await _settings.GetAsync();
            int bars = (int)BarsSlider.Value;
            double resp = RespSlider.Value;
            double smooth = SmoothSlider.Value;
            byte r = (byte)ColorR.Value;
            byte g = (byte)ColorG.Value;
            byte b = (byte)ColorB.Value;

            int fps = (int)FpsSlider.Value;
            bool cycle = ColorCycleEnabled.IsChecked == true;
            double cycleHz = ColorCycleSpeed.Value;
            double radius = CornerRadiusSlider.Value;

            var displayMode = GetSelectedDisplayMode();
            var visualizerMode = GetSelectedVisualizerMode();
            double circleDiameter = CircleDiameterSlider.Value;
            bool fadeOnSilence = FadeOnSilenceEnabledCheckBox.IsChecked == true;
            double fadeOutSeconds = FadeOutSlider.Value;
            double fadeInSeconds = FadeInSlider.Value;
            string? deviceName = null;
            if (displayMode == MonitorDisplayMode.Specific && MonitorCombo.SelectedItem is ComboBoxItem sel)
                deviceName = sel.Tag as string;

            // Preserve existing offsets and overlay visibility when saving from settings
            var s = new EqualizerSettings(
                bars, resp, smooth, new ColorRgb(r, g, b),
                fps, cycle, cycleHz, radius,
                displayMode, deviceName,
                current.OffsetX, current.OffsetY,
                visualizerMode, circleDiameter,
                current.OverlayVisible, fadeOnSilence,
                fadeOutSeconds, fadeInSeconds);
            await _settings.SaveAsync(s);

            // Immediately reflect changes in overlays
            if (_overlay.IsVisible)
            {
                await _overlay.ShowAsync(); // re-applies monitor selection & styles
            }
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(this, ex.Message, "Invalid settings", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnDisplayModeChanged(object sender, SelectionChangedEventArgs e)
    {
        MonitorCombo.IsEnabled = GetSelectedDisplayMode() == MonitorDisplayMode.Specific;
    }

    private void OnPickColor(object? sender, RoutedEventArgs e)
    {
        var initial = new ColorRgb((byte)ColorR.Value, (byte)ColorG.Value, (byte)ColorB.Value);
        var picker = new ColorPickerWindow(initial)
        {
            Owner = this
        };
        if (picker.ShowDialog() == true)
        {
            var c = picker.SelectedColor;
            ColorR.Value = c.R;
            ColorG.Value = c.G;
            ColorB.Value = c.B;
        }
    }

    private void SetDisplayModeSelection(MonitorDisplayMode mode)
    {
        int tag = mode switch
        {
            MonitorDisplayMode.PrimaryOnly => 1,
            MonitorDisplayMode.Specific => 2,
            _ => 0
        };
        foreach (ComboBoxItem item in DisplayModeCombo.Items)
        {
            if (int.TryParse(item.Tag?.ToString(), out int t) && t == tag)
            {
                DisplayModeCombo.SelectedItem = item;
                return;
            }
        }
    }

    private MonitorDisplayMode GetSelectedDisplayMode()
    {
        if (DisplayModeCombo.SelectedItem is ComboBoxItem item && int.TryParse(item.Tag?.ToString(), out int t))
        {
            return t switch
            {
                1 => MonitorDisplayMode.PrimaryOnly,
                2 => MonitorDisplayMode.Specific,
                _ => MonitorDisplayMode.All
            };
        }
        return MonitorDisplayMode.All;
    }

    private void OnProfileChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ProfileCombo.SelectedItem is not ComboBoxItem item) return;
        var tag = (item.Tag as string) ?? "custom";

        switch (tag)
        {
            case "smooth":
                RespSlider.Value = 0.6;
                SmoothSlider.Value = 0.8;
                FpsSlider.Value = 60;
                break;
            case "balanced":
                RespSlider.Value = 0.7;
                SmoothSlider.Value = 0.5;
                FpsSlider.Value = 60;
                break;
            case "low":
                RespSlider.Value = 0.9;
                SmoothSlider.Value = 0.25;
                FpsSlider.Value = 144;
                break;
            default:
                // custom: do nothing, user controls sliders
                break;
        }
    }

    private void SetProfileFromCurrentValues()
    {
        double resp = RespSlider.Value;
        double smooth = SmoothSlider.Value;
        int fps = (int)FpsSlider.Value;

        string tag = "custom";

        if (Math.Abs(resp - 0.7) < 0.01 && Math.Abs(smooth - 0.5) < 0.02 && fps == 60)
        {
            tag = "balanced";
        }
        else if (Math.Abs(resp - 0.6) < 0.05 && Math.Abs(smooth - 0.8) < 0.05 && fps == 60)
        {
            tag = "smooth";
        }
        else if (resp >= 0.85 && smooth <= 0.3 && fps >= 120)
        {
            tag = "low";
        }

        foreach (ComboBoxItem item in ProfileCombo.Items)
        {
            if (string.Equals((item.Tag as string) ?? "", tag, StringComparison.OrdinalIgnoreCase))
            {
                ProfileCombo.SelectedItem = item;
                return;
            }
        }
    }

    private void SetVisualizerModeSelection(VisualizerMode mode)
    {
        int tag = mode switch
        {
            VisualizerMode.Circular => 1,
            _ => 0
        };
        foreach (ComboBoxItem item in VisualizerModeCombo.Items)
        {
            if (int.TryParse(item.Tag?.ToString(), out int t) && t == tag)
            {
                VisualizerModeCombo.SelectedItem = item;
                return;
            }
        }
    }

    private VisualizerMode GetSelectedVisualizerMode()
    {
        if (VisualizerModeCombo.SelectedItem is ComboBoxItem item && int.TryParse(item.Tag?.ToString(), out int t))
        {
            return t switch
            {
                1 => VisualizerMode.Circular,
                _ => VisualizerMode.Bars
            };
        }
        return VisualizerMode.Bars;
    }
}
