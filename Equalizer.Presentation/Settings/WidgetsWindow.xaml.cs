using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Equalizer.Application.Abstractions;
using Equalizer.Domain.Widgets;
using Equalizer.Presentation.Widgets;

namespace Equalizer.Presentation.Settings;

public partial class WidgetsWindow : Window
{
    private readonly WidgetManager _widgetManager;
    private readonly IWidgetRegistry _registry;
    private readonly ObservableCollection<WidgetViewModel> _widgets = new();
    private WidgetViewModel? _selectedWidget;
    private bool _isUpdating;

    public WidgetsWindow(WidgetManager widgetManager, IWidgetRegistry registry)
    {
        _widgetManager = widgetManager;
        _registry = registry;
        InitializeComponent();
        
        WidgetList.ItemsSource = _widgets;
        AddWidgetTypeCombo.SelectedIndex = 0;
        
        Loaded += async (_, __) =>
        {
            await _widgetManager.LoadLayoutAsync();
            RefreshWidgetList();
        };
    }

    private void RefreshWidgetList()
    {
        _widgets.Clear();
        var layout = _widgetManager.GetCurrentLayout();
        if (layout == null) return;

        foreach (var config in layout.Widgets)
        {
            var renderer = _registry.GetRenderer(config.WidgetTypeId);
            _widgets.Add(new WidgetViewModel
            {
                Config = config,
                DisplayName = renderer?.DisplayName ?? config.WidgetTypeId,
                IsEnabled = config.IsEnabled
            });
        }
    }

    private void WidgetList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _selectedWidget = WidgetList.SelectedItem as WidgetViewModel;
        UpdateSettingsPanel();
    }

    private void UpdateSettingsPanel()
    {
        _isUpdating = true;
        
        if (_selectedWidget == null)
        {
            NoSelectionText.Visibility = Visibility.Visible;
            CommonSettings.Visibility = Visibility.Collapsed;
            _isUpdating = false;
            return;
        }

        NoSelectionText.Visibility = Visibility.Collapsed;
        CommonSettings.Visibility = Visibility.Visible;

        var config = _selectedWidget.Config;
        
        // Common position settings
        AnchorCombo.SelectedIndex = (int)config.Anchor;
        XOffsetSlider.Value = config.X;
        YOffsetSlider.Value = config.Y;
        XOffsetValue.Text = config.X.ToString("0");
        YOffsetValue.Text = config.Y.ToString("0");
        WidthSlider.Value = config.Width;
        HeightSlider.Value = config.Height;
        WidthValue.Text = config.Width.ToString("0");
        HeightValue.Text = config.Height.ToString("0");

        // Widget-specific settings
        ClockSettings.Visibility = Visibility.Collapsed;
        DateSettings.Visibility = Visibility.Collapsed;
        SystemInfoSettings.Visibility = Visibility.Collapsed;

        switch (config.WidgetTypeId.ToLowerInvariant())
        {
            case "clock":
                ClockSettings.Visibility = Visibility.Visible;
                Clock24Hour.IsChecked = config.GetSetting("Use24Hour", true);
                ClockShowSeconds.IsChecked = config.GetSetting("ShowSeconds", true);
                ClockFontSize.Value = config.GetSetting("FontSize", 48.0);
                ClockFontSizeValue.Text = ClockFontSize.Value.ToString("0");
                break;
                
            case "date":
                DateSettings.Visibility = Visibility.Visible;
                var format = config.GetSetting("DateFormat", "dddd, MMMM d, yyyy");
                foreach (ComboBoxItem item in DateFormatCombo.Items)
                {
                    if (item.Tag?.ToString() == format)
                    {
                        DateFormatCombo.SelectedItem = item;
                        break;
                    }
                }
                DateFontSize.Value = config.GetSetting("FontSize", 24.0);
                DateFontSizeValue.Text = DateFontSize.Value.ToString("0");
                break;
                
            case "systeminfo":
                SystemInfoSettings.Visibility = Visibility.Visible;
                SysShowCpu.IsChecked = config.GetSetting("ShowCpu", true);
                SysShowRam.IsChecked = config.GetSetting("ShowRam", true);
                SysShowBars.IsChecked = config.GetSetting("ShowBars", true);
                break;
        }

        _isUpdating = false;
    }

    private void WidgetEnabled_Changed(object sender, RoutedEventArgs e)
    {
        if (_isUpdating) return;
        if (sender is System.Windows.Controls.CheckBox cb && cb.DataContext is WidgetViewModel vm)
        {
            vm.Config.IsEnabled = cb.IsChecked == true;
            vm.IsEnabled = vm.Config.IsEnabled;
        }
    }

    private void AnchorCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isUpdating || _selectedWidget == null) return;
        if (AnchorCombo.SelectedItem is ComboBoxItem item && int.TryParse(item.Tag?.ToString(), out int anchor))
        {
            _selectedWidget.Config.Anchor = (WidgetAnchor)anchor;
        }
    }

    private void PositionSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isUpdating || _selectedWidget == null) return;
        _selectedWidget.Config.X = XOffsetSlider.Value;
        _selectedWidget.Config.Y = YOffsetSlider.Value;
        XOffsetValue.Text = XOffsetSlider.Value.ToString("0");
        YOffsetValue.Text = YOffsetSlider.Value.ToString("0");
    }

    private void SizeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isUpdating || _selectedWidget == null) return;
        _selectedWidget.Config.Width = WidthSlider.Value;
        _selectedWidget.Config.Height = HeightSlider.Value;
        WidthValue.Text = WidthSlider.Value.ToString("0");
        HeightValue.Text = HeightSlider.Value.ToString("0");
    }

    private void ClockSetting_Changed(object sender, RoutedEventArgs e)
    {
        if (_isUpdating || _selectedWidget == null) return;
        _selectedWidget.Config.SetSetting("Use24Hour", Clock24Hour.IsChecked == true);
        _selectedWidget.Config.SetSetting("ShowSeconds", ClockShowSeconds.IsChecked == true);
        _selectedWidget.Config.SetSetting("FontSize", ClockFontSize.Value);
        ClockFontSizeValue.Text = ClockFontSize.Value.ToString("0");
    }

    private void DateFormat_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_isUpdating || _selectedWidget == null) return;
        if (DateFormatCombo.SelectedItem is ComboBoxItem item)
        {
            _selectedWidget.Config.SetSetting("DateFormat", item.Tag?.ToString() ?? "dddd, MMMM d, yyyy");
        }
    }

    private void DateSetting_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isUpdating || _selectedWidget == null) return;
        _selectedWidget.Config.SetSetting("FontSize", DateFontSize.Value);
        DateFontSizeValue.Text = DateFontSize.Value.ToString("0");
    }

    private void SysInfoSetting_Changed(object sender, RoutedEventArgs e)
    {
        if (_isUpdating || _selectedWidget == null) return;
        _selectedWidget.Config.SetSetting("ShowCpu", SysShowCpu.IsChecked == true);
        _selectedWidget.Config.SetSetting("ShowRam", SysShowRam.IsChecked == true);
        _selectedWidget.Config.SetSetting("ShowBars", SysShowBars.IsChecked == true);
    }

    private void AddWidget_Click(object sender, RoutedEventArgs e)
    {
        if (AddWidgetTypeCombo.SelectedItem is not ComboBoxItem item) return;
        var typeId = item.Tag?.ToString() ?? "clock";
        
        var config = new WidgetConfig
        {
            WidgetTypeId = typeId,
            IsEnabled = true,
            Anchor = WidgetAnchor.TopRight,
            X = 20,
            Y = 20,
            Width = typeId == "systeminfo" ? 200 : 0,
            Height = typeId == "systeminfo" ? 80 : 0
        };
        
        // Set default settings based on type
        switch (typeId)
        {
            case "clock":
                config.SetSetting("Use24Hour", true);
                config.SetSetting("ShowSeconds", true);
                config.SetSetting("FontSize", 48.0);
                break;
            case "date":
                config.SetSetting("DateFormat", "dddd, MMMM d, yyyy");
                config.SetSetting("FontSize", 24.0);
                break;
            case "systeminfo":
                config.SetSetting("ShowCpu", true);
                config.SetSetting("ShowRam", true);
                config.SetSetting("ShowBars", true);
                break;
        }

        _widgetManager.AddWidget(config);
        RefreshWidgetList();
        
        // Select the new widget
        WidgetList.SelectedIndex = _widgets.Count - 1;
    }

    private void DeleteWidget_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedWidget == null) return;
        
        var result = System.Windows.MessageBox.Show($"Delete widget '{_selectedWidget.DisplayName}'?", "Confirm Delete", 
            MessageBoxButton.YesNo, MessageBoxImage.Question);
        
        if (result == MessageBoxResult.Yes)
        {
            _widgetManager.RemoveWidget(_selectedWidget.Config.Id);
            RefreshWidgetList();
            _selectedWidget = null;
            UpdateSettingsPanel();
        }
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        await _widgetManager.SaveLayoutAsync();
        System.Windows.MessageBox.Show("Widget layout saved.", "Saved", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}

public class WidgetViewModel
{
    public WidgetConfig Config { get; set; } = new();
    public string DisplayName { get; set; } = "";
    public bool IsEnabled { get; set; }
}
