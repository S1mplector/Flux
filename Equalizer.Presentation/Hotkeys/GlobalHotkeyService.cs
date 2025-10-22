using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Extensions.Hosting;
using Equalizer.Presentation.Overlay;

namespace Equalizer.Presentation.Hotkeys;

public sealed class GlobalHotkeyService : IHostedService, IDisposable
{
    private readonly IOverlayManager _overlay;
    private readonly Settings.SettingsWindow _settings;
    private HwndSource? _source;
    private IntPtr _hwnd;

    private const int WM_HOTKEY = 0x0312;
    private const uint MOD_ALT = 0x0001;
    private const uint MOD_CONTROL = 0x0002;
    private const uint MOD_SHIFT = 0x0004;

    private const int HOTKEY_ID_TOGGLE_OVERLAY = 1;
    private const int HOTKEY_ID_OPEN_SETTINGS = 2;

    [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    public GlobalHotkeyService(IOverlayManager overlay, Settings.SettingsWindow settings)
    {
        _overlay = overlay;
        _settings = settings;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            var parameters = new HwndSourceParameters("GlobalHotkeySink")
            {
                Width = 0,
                Height = 0,
                PositionX = 0,
                PositionY = 0,
                WindowStyle = unchecked((int)0x80000000) /* WS_DISABLED */
            };
            _source = new HwndSource(parameters);
            _source.AddHook(WndProc);
            _hwnd = _source.Handle;

            RegisterHotKey(_hwnd, HOTKEY_ID_TOGGLE_OVERLAY, MOD_CONTROL | MOD_ALT | MOD_SHIFT, (uint)System.Windows.Input.KeyInterop.VirtualKeyFromKey(System.Windows.Input.Key.E));
            RegisterHotKey(_hwnd, HOTKEY_ID_OPEN_SETTINGS, MOD_CONTROL | MOD_ALT | MOD_SHIFT, (uint)System.Windows.Input.KeyInterop.VirtualKeyFromKey(System.Windows.Input.Key.S));
        });
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        if (_source != null)
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                if (_hwnd != IntPtr.Zero)
                {
                    UnregisterHotKey(_hwnd, HOTKEY_ID_TOGGLE_OVERLAY);
                    UnregisterHotKey(_hwnd, HOTKEY_ID_OPEN_SETTINGS);
                }
                _source.RemoveHook(WndProc);
                _source.Dispose();
                _source = null;
                _hwnd = IntPtr.Zero;
            });
        }
        return Task.CompletedTask;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY)
        {
            int id = wParam.ToInt32();
            switch (id)
            {
                case HOTKEY_ID_TOGGLE_OVERLAY:
                    _ = _overlay.ToggleAsync();
                    handled = true;
                    break;
                case HOTKEY_ID_OPEN_SETTINGS:
                    System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        if (!_settings.IsVisible) _settings.Show(); else { _settings.Activate(); _settings.Focus(); }
                    }));
                    handled = true;
                    break;
            }
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        _ = StopAsync(CancellationToken.None);
    }
}
