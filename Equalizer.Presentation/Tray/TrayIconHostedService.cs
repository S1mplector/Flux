using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Equalizer.Presentation.Overlay;
using Equalizer.Presentation.Widgets;
using Forms = System.Windows.Forms;
using Drawing = System.Drawing;
using WpfApp = System.Windows.Application;
using System;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;

namespace Equalizer.Presentation.Tray;

public sealed class TrayIconHostedService : IHostedService
{
    private readonly IOverlayManager _overlay;
    private readonly IServiceProvider _services;
    private readonly WidgetManager _widgetManager;
    private Forms.NotifyIcon? _icon;

    public TrayIconHostedService(IOverlayManager overlay, IServiceProvider services, WidgetManager widgetManager)
    {
        _overlay = overlay;
        _services = services;
        _widgetManager = widgetManager;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        WpfApp.Current.Dispatcher.Invoke(() =>
        {
            _icon = new Forms.NotifyIcon
            {
                Text = "Equalizer",
                Visible = true,
                Icon = Drawing.SystemIcons.Application
            };

            var contextMenu = new Forms.ContextMenuStrip();
            var toggleItem = new Forms.ToolStripMenuItem("Toggle Overlay", null, async (_, __) => await _overlay.ToggleAsync());
            var resetPositionItem = new Forms.ToolStripMenuItem("Reset position", null, async (_, __) => await _overlay.ResetPositionAsync());
            var clickThroughItem = new Forms.ToolStripMenuItem("Click-through") { CheckOnClick = true, Checked = _overlay.ClickThrough };
            clickThroughItem.Click += async (_, __) =>
            {
                await _overlay.ToggleClickThroughAsync();
                clickThroughItem.Checked = _overlay.ClickThrough;
            };
            var alwaysOnTopItem = new Forms.ToolStripMenuItem("Always on top") { CheckOnClick = true, Checked = _overlay.AlwaysOnTop };
            alwaysOnTopItem.Click += async (_, __) =>
            {
                await _overlay.ToggleAlwaysOnTopAsync();
                alwaysOnTopItem.Checked = _overlay.AlwaysOnTop;
            };
            var settingsItem = new Forms.ToolStripMenuItem("Settings...");
            settingsItem.Click += (_, __) =>
            {
                if (App.IsShuttingDown) return;
                var existing = WpfApp.Current.Windows.OfType<Settings.SettingsWindow>().FirstOrDefault();
                if (existing == null || existing.IsLoaded == false)
                {
                    var win = _services.GetRequiredService<Settings.SettingsWindow>();
                    win.Show();
                }
                else
                {
                    if (existing.WindowState == System.Windows.WindowState.Minimized)
                        existing.WindowState = System.Windows.WindowState.Normal;
                    existing.Activate();
                    existing.Focus();
                }
            };
            var widgetsItem = new Forms.ToolStripMenuItem("Widgets...");
            widgetsItem.Click += (_, __) =>
            {
                if (App.IsShuttingDown) return;
                var existing = WpfApp.Current.Windows.OfType<Settings.WidgetsWindow>().FirstOrDefault();
                if (existing == null || existing.IsLoaded == false)
                {
                    var win = _services.GetRequiredService<Settings.WidgetsWindow>();
                    win.Show();
                }
                else
                {
                    if (existing.WindowState == System.Windows.WindowState.Minimized)
                        existing.WindowState = System.Windows.WindowState.Normal;
                    existing.Activate();
                    existing.Focus();
                }
            };
            var editWidgetsItem = new Forms.ToolStripMenuItem("Edit Widgets") { CheckOnClick = true, Checked = _widgetManager.EditMode };
            editWidgetsItem.Click += async (_, __) =>
            {
                _widgetManager.EditMode = editWidgetsItem.Checked;
                // Temporarily disable click-through while editing
                if (_widgetManager.EditMode && _overlay.ClickThrough)
                {
                    await _overlay.ToggleClickThroughAsync();
                    clickThroughItem.Checked = false;
                }
            };
            var exitItem = new Forms.ToolStripMenuItem("Exit", null, (_, __) => WpfApp.Current.Shutdown());

            contextMenu.Items.Add(toggleItem);
            contextMenu.Items.Add(resetPositionItem);
            contextMenu.Items.Add(new Forms.ToolStripSeparator());
            contextMenu.Items.Add(clickThroughItem);
            contextMenu.Items.Add(alwaysOnTopItem);
            contextMenu.Items.Add(settingsItem);
            contextMenu.Items.Add(widgetsItem);
            contextMenu.Items.Add(editWidgetsItem);
            contextMenu.Items.Add(new Forms.ToolStripSeparator());
            contextMenu.Items.Add(exitItem);
            _icon.ContextMenuStrip = contextMenu;

            _icon.DoubleClick += async (_, __) => await _overlay.ToggleAsync();
        });

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        if (_icon != null)
        {
            WpfApp.Current.Dispatcher.Invoke(() =>
            {
                _icon.Visible = false;
                _icon.Dispose();
                _icon = null;
            });
        }
        return Task.CompletedTask;
    }
}
