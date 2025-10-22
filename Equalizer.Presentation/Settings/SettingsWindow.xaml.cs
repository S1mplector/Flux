using System;
using System.Windows;
using System.Windows.Controls;
using Equalizer.Application.Abstractions;
using Equalizer.Domain;
using Forms = System.Windows.Forms;

namespace Equalizer.Presentation.Settings;

public partial class SettingsWindow : Window
{
    private readonly ISettingsPort _settings;
    private readonly Overlay.IOverlayManager _overlay;

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
    }

    private async void OnSave(object sender, RoutedEventArgs e)
    {
        try
        {
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
            string? deviceName = null;
            if (displayMode == MonitorDisplayMode.Specific && MonitorCombo.SelectedItem is ComboBoxItem sel)
                deviceName = sel.Tag as string;

            var s = new EqualizerSettings(bars, resp, smooth, new ColorRgb(r, g, b), fps, cycle, cycleHz, radius, displayMode, deviceName);
            await _settings.SaveAsync(s);

            // Immediately reflect changes in overlays
            if (_overlay.IsVisible)
            {
                await _overlay.ShowAsync(); // re-applies monitor selection & styles
            }
            Close();
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
        using var dlg = new Forms.ColorDialog
        {
            AllowFullOpen = true,
            AnyColor = true,
            FullOpen = true,
            Color = System.Drawing.Color.FromArgb((int)ColorR.Value, (int)ColorG.Value, (int)ColorB.Value)
        };
        if (dlg.ShowDialog() == Forms.DialogResult.OK)
        {
            ColorR.Value = dlg.Color.R;
            ColorG.Value = dlg.Color.G;
            ColorB.Value = dlg.Color.B;
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
}
