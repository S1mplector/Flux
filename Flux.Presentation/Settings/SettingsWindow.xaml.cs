using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Flux.Application.Abstractions;
using Flux.Domain;
using Flux.Presentation.Controls;
using Wpf.Ui.Controls;
using Forms = System.Windows.Forms;

namespace Flux.Presentation.Settings;

public partial class SettingsWindow : FluentWindow
{
    private readonly ISettingsPort _settings;
    private readonly Overlay.IOverlayManager _overlay;
    private readonly IAudioDeviceProvider? _audioDevices;
    private readonly Dictionary<string, FrameworkElement> _pages;
    private readonly Dictionary<string, NavigationViewItem> _navItems;
    private readonly DispatcherTimer _resourceTimer;
    private readonly Process _currentProcess;
    private TimeSpan _lastCpuTime;
    private DateTime _lastCpuSample;
    private readonly List<PerformanceCounter> _gpuCounters = new();
    private bool _gpuAvailable;

    public SettingsWindow(ISettingsPort settings, Overlay.IOverlayManager overlay, IAudioDeviceProvider? audioDevices = null)
    {
        _settings = settings;
        _overlay = overlay;
        _audioDevices = audioDevices;
        InitializeComponent();

        _navItems = new Dictionary<string, NavigationViewItem>(StringComparer.OrdinalIgnoreCase)
        {
            { "Audio", NavAudio },
            { "Appearance", NavAppearance },
            { "Performance", NavPerformance },
            { "Display", NavDisplay },
            { "Effects", NavEffects },
            { "Presets", NavPresets }
        };

        _pages = new Dictionary<string, FrameworkElement>(StringComparer.OrdinalIgnoreCase)
        {
            { "Audio", AudioPage },
            { "Appearance", AppearancePage },
            { "Performance", PerformancePage },
            { "Display", DisplayPage },
            { "Effects", EffectsPage },
            { "Presets", PresetsPage }
        };

        foreach (var navItem in _navItems.Values)
        {
            navItem.Click += NavigationItem_Click;
        }
        NavigateToSection("Audio");

        Loaded += OnLoaded;
        SaveButton.Click += OnSave;
        CancelButton.Click += (_, __) => Close();

        BarsSlider.ValueChanged += (_, __) => BarsValue.Text = ((int)BarsSlider.Value).ToString();
        RespSlider.ValueChanged += (_, __) => RespValue.Text = RespSlider.Value.ToString("0.00");
        SmoothSlider.ValueChanged += (_, __) => SmoothValue.Text = SmoothSlider.Value.ToString("0.00");
        ColorR.ValueChanged += (_, __) => ColorRValue.Text = ((int)ColorR.Value).ToString();
        ColorG.ValueChanged += (_, __) => ColorGValue.Text = ((int)ColorG.Value).ToString();
        ColorB.ValueChanged += (_, __) => ColorBValue.Text = ((int)ColorB.Value).ToString();
        FpsSlider.ValueChanged += (_, __) =>
        {
            var fps = (int)FpsSlider.Value;
            FpsValue.Text = fps.ToString();
        };
        ColorCycleSpeed.ValueChanged += (_, __) => ColorCycleSpeedValue.Text = ColorCycleSpeed.Value.ToString("0.00");
        CornerRadiusSlider.ValueChanged += (_, __) => CornerRadiusValue.Text = CornerRadiusSlider.Value.ToString("0.0");
        BassEmphasisSlider.ValueChanged += (_, __) => BassEmphasisValue.Text = BassEmphasisSlider.Value.ToString("0.00");
        TrebleEmphasisSlider.ValueChanged += (_, __) => TrebleEmphasisValue.Text = TrebleEmphasisSlider.Value.ToString("0.00");
        DisplayModeCombo.SelectionChanged += OnDisplayModeChanged;
        PickColorButton.Click += OnPickColor;
        ProfileCombo.SelectionChanged += OnProfileChanged;
        FadeOutSlider.ValueChanged += (_, __) => FadeOutValue.Text = FadeOutSlider.Value.ToString("0.00");
        FadeInSlider.ValueChanged += (_, __) => FadeInValue.Text = FadeInSlider.Value.ToString("0.00");
        GradientEndR.ValueChanged += (_, __) => UpdateGradientPreview();
        GradientEndG.ValueChanged += (_, __) => UpdateGradientPreview();
        GradientEndB.ValueChanged += (_, __) => UpdateGradientPreview();
        CircleDiameterSlider.ValueChanged += (_, __) => CircleDiameterValue.Text = CircleDiameterSlider.Value.ToString("0");

        _currentProcess = Process.GetCurrentProcess();
        _lastCpuTime = _currentProcess.TotalProcessorTime;
        _lastCpuSample = DateTime.UtcNow;

        _resourceTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _resourceTimer.Tick += ResourceTimer_Tick;
        _resourceTimer.Start();
        InitializeGpuCounters();

        Closed += (_, __) =>
        {
            _resourceTimer.Stop();
            DisposeGpuCounters();
        };
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

            var gpuText = "GPU: n/a";
            if (_gpuAvailable)
            {
                double total = 0.0;
                int count = 0;
                foreach (var counter in _gpuCounters)
                {
                    try
                    {
                        total += counter.NextValue();
                        count++;
                    }
                    catch
                    {
                        // ignore individual counter failures
                    }
                }

                if (count > 0)
                {
                    var gpuPercent = Math.Clamp(total, 0.0, 100.0);
                    gpuText = $"GPU: {gpuPercent:0.0}%";
                }
            }
            GpuUsageText.Text = gpuText;

            var overlayFps = _overlay.GetCurrentFps();
            if (overlayFps.HasValue)
            {
                FpsText.Text = $"FPS: {overlayFps.Value:0}";
            }
            else
            {
                // Fall back to target FPS when overlay isn't rendering
                FpsText.Text = $"FPS: {(int)FpsSlider.Value} (target)";
            }

            // Calculate and display estimated audio latency
            var latencyMs = CalculateEstimatedLatency();
            LatencyText.Text = $"Latency: ~{latencyMs:0} ms";
        }
        catch
        {
            // Non-fatal: best-effort diagnostics only
        }
    }

    private void InitializeGpuCounters()
    {
        try
        {
            const string categoryName = "GPU Engine";
            if (!PerformanceCounterCategory.Exists(categoryName)) return;

            var category = new PerformanceCounterCategory(categoryName);
            foreach (var instance in category.GetInstanceNames())
            {
                if (!instance.Contains("engtype_3D", StringComparison.OrdinalIgnoreCase))
                    continue;

                foreach (var counter in category.GetCounters(instance))
                {
                    if (string.Equals(counter.CounterName, "Utilization Percentage", StringComparison.OrdinalIgnoreCase))
                    {
                        _gpuCounters.Add(counter);
                    }
                    else
                    {
                        counter.Dispose();
                    }
                }
            }

            _gpuAvailable = _gpuCounters.Count > 0;
        }
        catch
        {
            _gpuAvailable = false;
            DisposeGpuCounters();
        }
    }

    private void DisposeGpuCounters()
    {
        foreach (var c in _gpuCounters)
        {
            try { c.Dispose(); } catch { }
        }
        _gpuCounters.Clear();
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
        FpsText.Text = $"FPS: {s.TargetFps} (target)";
        ColorCycleEnabled.IsChecked = s.ColorCycleEnabled;
        ColorCycleSpeed.Value = s.ColorCycleSpeedHz;
        ColorCycleSpeedValue.Text = s.ColorCycleSpeedHz.ToString("0.00");
        CornerRadiusSlider.Value = s.BarCornerRadius;
        CornerRadiusValue.Text = s.BarCornerRadius.ToString("0.0");

        // Populate monitors with friendly names
        PopulateMonitorCombo(s.SpecificMonitorDeviceName);

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

        BassEmphasisSlider.Value = s.BassEmphasis;
        BassEmphasisValue.Text = s.BassEmphasis.ToString("0.00");
        TrebleEmphasisSlider.Value = s.TrebleEmphasis;
        TrebleEmphasisValue.Text = s.TrebleEmphasis.ToString("0.00");

        GlowEnabledCheckBox.IsChecked = s.GlowEnabled;
        BeatShapeEnabledCheckBox.IsChecked = s.BeatShapeEnabled;
        
        // Gradient
        GradientEnabledCheckBox.IsChecked = s.GradientEnabled;
        GradientEndR.Value = s.GradientEndColor.R;
        GradientEndG.Value = s.GradientEndColor.G;
        GradientEndB.Value = s.GradientEndColor.B;
        UpdateGradientPreview();
        
        // Audio devices
        PopulateAudioDevices(s.AudioDeviceId);
        
        // Theme presets
        PopulateThemePresets();

        // Performance profile (infer from current values)
        SetProfileFromCurrentValues();
        
        // Rendering mode
        SetRenderingModeSelection(s.RenderingMode);
    }
    
    private void SetRenderingModeSelection(RenderingMode mode)
    {
        int tag = (int)mode;
        foreach (ComboBoxItem item in RenderingModeCombo.Items)
        {
            if (int.TryParse(item.Tag?.ToString(), out int t) && t == tag)
            {
                RenderingModeCombo.SelectedItem = item;
                return;
            }
        }
    }
    
    private RenderingMode GetSelectedRenderingMode()
    {
        if (RenderingModeCombo.SelectedItem is ComboBoxItem item && int.TryParse(item.Tag?.ToString(), out int t))
        {
            return (RenderingMode)t;
        }
        return RenderingMode.Cpu;
    }
    
    private void PopulateAudioDevices(string? selectedDeviceId)
    {
        AudioDeviceCombo.Items.Clear();
        AudioDeviceCombo.Items.Add(new ComboBoxItem { Content = "(Default output device)", Tag = "" });
        AudioDeviceCombo.SelectedIndex = 0;
        
        if (_audioDevices == null) return;
        
        var devices = _audioDevices.GetOutputDevices();
        foreach (var device in devices)
        {
            var label = device.IsDefault ? $"{device.Name} (Default)" : device.Name;
            var item = new ComboBoxItem { Content = label, Tag = device.Id };
            AudioDeviceCombo.Items.Add(item);
            if (!string.IsNullOrEmpty(selectedDeviceId) && device.Id == selectedDeviceId)
            {
                AudioDeviceCombo.SelectedItem = item;
            }
        }
    }
    
    private void UpdateGradientPreview()
    {
        var color = System.Windows.Media.Color.FromRgb(
            (byte)GradientEndR.Value,
            (byte)GradientEndG.Value,
            (byte)GradientEndB.Value);
        GradientPreview.Background = new System.Windows.Media.SolidColorBrush(color);
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
            double bassEmphasis = BassEmphasisSlider.Value;
            double trebleEmphasis = TrebleEmphasisSlider.Value;
            bool glow = GlowEnabledCheckBox.IsChecked == true;
            bool beatShape = BeatShapeEnabledCheckBox.IsChecked == true;
            bool gradientEnabled = GradientEnabledCheckBox.IsChecked == true;
            var gradientEndColor = new ColorRgb(
                (byte)GradientEndR.Value,
                (byte)GradientEndG.Value,
                (byte)GradientEndB.Value);
            string? audioDeviceId = (AudioDeviceCombo.SelectedItem as ComboBoxItem)?.Tag as string;
            if (string.IsNullOrEmpty(audioDeviceId)) audioDeviceId = null;
            var renderingMode = GetSelectedRenderingMode();
            
            string? deviceName = null;
            if (displayMode == MonitorDisplayMode.Specific && MonitorCombo.SelectedItem is ComboBoxItem sel)
                deviceName = sel.Tag as string;

            // Preserve existing offsets and overlay visibility when saving from settings
            var s = new FluxSettings(
                bars, resp, smooth, new ColorRgb(r, g, b),
                fps, cycle, cycleHz, radius,
                displayMode, deviceName,
                current.OffsetX, current.OffsetY,
                visualizerMode, circleDiameter,
                current.OverlayVisible, fadeOnSilence,
                fadeOutSeconds, fadeInSeconds,
                pitchReactiveColorEnabled: current.PitchReactiveColorEnabled,
                bassEmphasis, trebleEmphasis,
                beatShapeEnabled: beatShape, glowEnabled: glow, perfOverlayEnabled: current.PerfOverlayEnabled,
                gradientEnabled: gradientEnabled, gradientEndColor: gradientEndColor, audioDeviceId: audioDeviceId,
                renderingMode: renderingMode);
            await _settings.SaveAsync(s);

            // Immediately reflect changes in overlays
            if (_overlay.IsVisible)
            {
                await _overlay.ShowAsync(); // re-applies monitor selection & styles
            }
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(this, ex.Message, "Invalid settings", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
    }

    private void OnDisplayModeChanged(object sender, SelectionChangedEventArgs e)
    {
        MonitorCombo.IsEnabled = GetSelectedDisplayMode() == MonitorDisplayMode.Specific;
    }

    private void NavigationView_SelectionChanged(object sender, RoutedEventArgs e)
    {
        if (NavigationView.SelectedItem is NavigationViewItem item && item.Tag is string tag)
        {
            NavigateToSection(tag);
        }
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

    private double CalculateEstimatedLatency()
    {
        // Estimate total audio pipeline latency based on current settings
        const int sampleRate = 48000; // Typical WASAPI sample rate
        
        double smoothing = SmoothSlider.Value;
        int targetFps = (int)FpsSlider.Value;
        
        // FFT window size depends on smoothing and FPS (mirrors FluxService logic)
        int fftSamples;
        if (smoothing <= 0.3 && targetFps >= 120)
            fftSamples = 256;
        else if (smoothing <= 0.5 && targetFps >= 60)
            fftSamples = 512;
        else if (smoothing >= 0.7 && targetFps <= 30)
            fftSamples = 2048;
        else
            fftSamples = 512;
        
        // Audio buffer latency (~30ms at 48kHz with current settings)
        double bufferLatencyMs = (sampleRate / 32.0) / sampleRate * 1000.0; // ~31ms
        
        // FFT window latency (half the window on average)
        double fftLatencyMs = (fftSamples / 2.0) / sampleRate * 1000.0;
        
        // Hop latency (25% of window)
        double hopLatencyMs = (fftSamples / 4.0) / sampleRate * 1000.0;
        
        // Frame interval latency
        double frameLatencyMs = 1000.0 / Math.Max(targetFps, 10);
        
        // Total estimated latency
        return bufferLatencyMs + fftLatencyMs + hopLatencyMs + frameLatencyMs;
    }
    
    private void PopulateThemePresets()
    {
        ThemePresetCombo.Items.Clear();
        ThemePresetCombo.Items.Add(new ComboBoxItem { Content = "(Select a preset)", Tag = null });
        foreach (var preset in ThemePreset.BuiltIn)
        {
            ThemePresetCombo.Items.Add(new ComboBoxItem { Content = preset.Name, Tag = preset });
        }
        ThemePresetCombo.SelectedIndex = 0;
    }
    
    private void ThemePresetCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Just selection change - actual apply happens on button click
    }
    
    private void ApplyPresetButton_Click(object sender, RoutedEventArgs e)
    {
        if (ThemePresetCombo.SelectedItem is not ComboBoxItem item || item.Tag is not ThemePreset preset)
            return;
        
        // Apply preset values to UI
        ColorR.Value = preset.Color.R;
        ColorG.Value = preset.Color.G;
        ColorB.Value = preset.Color.B;
        ColorRValue.Text = preset.Color.R.ToString();
        ColorGValue.Text = preset.Color.G.ToString();
        ColorBValue.Text = preset.Color.B.ToString();
        
        GradientEnabledCheckBox.IsChecked = preset.GradientEnabled;
        GradientEndR.Value = preset.GradientEndColor.R;
        GradientEndG.Value = preset.GradientEndColor.G;
        GradientEndB.Value = preset.GradientEndColor.B;
        UpdateGradientPreview();
        
        ColorCycleEnabled.IsChecked = preset.ColorCycleEnabled;
        ColorCycleSpeed.Value = preset.ColorCycleSpeedHz;
        ColorCycleSpeedValue.Text = preset.ColorCycleSpeedHz.ToString("0.00");
        
        CornerRadiusSlider.Value = preset.BarCornerRadius;
        CornerRadiusValue.Text = preset.BarCornerRadius.ToString("0.0");
        
        GlowEnabledCheckBox.IsChecked = preset.GlowEnabled;
        BeatShapeEnabledCheckBox.IsChecked = preset.BeatShapeEnabled;
        
        BassEmphasisSlider.Value = preset.BassEmphasis;
        BassEmphasisValue.Text = preset.BassEmphasis.ToString("0.00");
        TrebleEmphasisSlider.Value = preset.TrebleEmphasis;
        TrebleEmphasisValue.Text = preset.TrebleEmphasis.ToString("0.00");
    }
    
    private void PopulateMonitorCombo(string? selectedDeviceName)
    {
        MonitorCombo.Items.Clear();
        
        // Use friendly names from overlay manager if available
        var monitors = _overlay.GetMonitors();
        
        foreach (var monitor in monitors)
        {
            var item = new ComboBoxItem 
            { 
                Content = monitor.FriendlyName, 
                Tag = monitor.DeviceName 
            };
            MonitorCombo.Items.Add(item);
            
            if (!string.IsNullOrEmpty(selectedDeviceName) &&
                string.Equals(monitor.DeviceName, selectedDeviceName, StringComparison.OrdinalIgnoreCase))
            {
                MonitorCombo.SelectedItem = item;
            }
        }
        
        // Select primary if nothing selected
        if (MonitorCombo.SelectedItem == null && monitors.Count > 0)
        {
            var primary = monitors.FirstOrDefault(m => m.IsPrimary);
            if (primary != null)
            {
                foreach (ComboBoxItem item in MonitorCombo.Items)
                {
                    if (string.Equals((string)item.Tag, primary.DeviceName, StringComparison.OrdinalIgnoreCase))
                    {
                        MonitorCombo.SelectedItem = item;
                        break;
                    }
                }
            }
            else if (MonitorCombo.Items.Count > 0)
            {
                MonitorCombo.SelectedIndex = 0;
            }
        }
    }
    
    private async void RefreshMonitorsButton_Click(object sender, RoutedEventArgs e)
    {
        await _overlay.RefreshMonitorsAsync();
        
        // Re-populate the monitor combo with updated list
        var currentSelection = (MonitorCombo.SelectedItem as ComboBoxItem)?.Tag as string;
        PopulateMonitorCombo(currentSelection);
    }

    private void NavigationItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is NavigationViewItem item && item.Tag is string tag)
        {
            NavigateToSection(tag);
        }
    }

    private void NavigateToSection(string tag)
    {
        if (!_pages.ContainsKey(tag))
        {
            tag = "Audio";
        }

        foreach (var (key, page) in _pages)
        {
            page.Visibility = string.Equals(key, tag, StringComparison.OrdinalIgnoreCase)
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        foreach (var (key, navItem) in _navItems)
        {
            navItem.IsActive = string.Equals(key, tag, StringComparison.OrdinalIgnoreCase);
        }
    }
}
