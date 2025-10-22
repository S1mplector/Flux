using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Equalizer.Presentation.Overlay;
using Forms = System.Windows.Forms;
using Drawing = System.Drawing;
using WpfApp = System.Windows.Application;

namespace Equalizer.Presentation.Tray;

public sealed class TrayIconHostedService : IHostedService
{
    private readonly IOverlayManager _overlay;
    private Forms.NotifyIcon? _icon;

    public TrayIconHostedService(IOverlayManager overlay)
    {
        _overlay = overlay;
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
            var showItem = new Forms.ToolStripMenuItem("Show Overlay", null, async (_, __) => await _overlay.ShowAsync());
            var hideItem = new Forms.ToolStripMenuItem("Hide Overlay", null, async (_, __) => await _overlay.HideAsync());
            var toggleItem = new Forms.ToolStripMenuItem("Toggle Overlay", null, async (_, __) => await _overlay.ToggleAsync());
            var exitItem = new Forms.ToolStripMenuItem("Exit", null, (_, __) => WpfApp.Current.Shutdown());

            contextMenu.Items.Add(showItem);
            contextMenu.Items.Add(hideItem);
            contextMenu.Items.Add(toggleItem);
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
