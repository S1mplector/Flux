using System;
using System.Threading;
using System.Threading.Tasks;

namespace Flux.Application.Abstractions;

/// <summary>
/// Platform-specific window operations abstraction.
/// </summary>
public interface IPlatformWindowService
{
    /// <summary>
    /// Makes a window click-through (mouse events pass through).
    /// </summary>
    void SetClickThrough(object windowHandle, bool clickThrough);

    /// <summary>
    /// Sends window to bottom of Z-order (desktop level).
    /// </summary>
    void SendToBottom(object windowHandle);

    /// <summary>
    /// Sets window always-on-top state.
    /// </summary>
    void SetTopMost(object windowHandle, bool topMost);

    /// <summary>
    /// Applies overlay-specific window styles (tool window, layered, etc.).
    /// </summary>
    void ApplyOverlayStyles(object windowHandle);
}

/// <summary>
/// Platform-specific global hotkey registration.
/// </summary>
public interface IGlobalHotkeyService : IDisposable
{
    /// <summary>
    /// Registers a global hotkey combination.
    /// </summary>
    /// <param name="id">Unique identifier for this hotkey.</param>
    /// <param name="modifiers">Modifier keys (Ctrl, Alt, Shift, etc.).</param>
    /// <param name="key">The main key.</param>
    /// <param name="callback">Action to invoke when hotkey is pressed.</param>
    /// <returns>True if registration succeeded.</returns>
    bool Register(int id, HotkeyModifiers modifiers, string key, Action callback);

    /// <summary>
    /// Unregisters a previously registered hotkey.
    /// </summary>
    bool Unregister(int id);

    /// <summary>
    /// Starts listening for hotkey events.
    /// </summary>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Stops listening for hotkey events.
    /// </summary>
    Task StopAsync(CancellationToken cancellationToken = default);
}

[Flags]
public enum HotkeyModifiers
{
    None = 0,
    Alt = 1,
    Control = 2,
    Shift = 4,
    Meta = 8  // Windows key on Windows, Command on macOS
}

/// <summary>
/// Platform-specific system tray icon abstraction.
/// </summary>
public interface ITrayIconService : IDisposable
{
    /// <summary>
    /// Shows the tray icon.
    /// </summary>
    void Show();

    /// <summary>
    /// Hides the tray icon.
    /// </summary>
    void Hide();

    /// <summary>
    /// Sets the tray icon tooltip text.
    /// </summary>
    void SetTooltip(string text);

    /// <summary>
    /// Event raised when tray icon is clicked.
    /// </summary>
    event EventHandler? Clicked;

    /// <summary>
    /// Event raised when tray icon is double-clicked.
    /// </summary>
    event EventHandler? DoubleClicked;
}

