using System.Collections.Generic;
using System.Threading.Tasks;

namespace Equalizer.Presentation.Overlay;

public record MonitorInfo(string DeviceName, string FriendlyName, int Width, int Height, bool IsPrimary, bool HasOverlay);

public interface IOverlayManager
{
    Task ShowAsync();
    Task HideAsync();
    Task ToggleAsync();
    bool IsVisible { get; }
    bool ClickThrough { get; }
    bool AlwaysOnTop { get; }
    /// <summary>
    /// Returns the most recent measured overlay FPS if available (across any visible overlays).
    /// </summary>
    double? GetCurrentFps();
    Task SetClickThroughAsync(bool value);
    Task ToggleClickThroughAsync();
    Task SetAlwaysOnTopAsync(bool value);
    Task ToggleAlwaysOnTopAsync();
    Task ResetPositionAsync();
    /// <summary>
    /// Returns info about all connected monitors and their overlay status.
    /// </summary>
    IReadOnlyList<MonitorInfo> GetMonitors();
    /// <summary>
    /// Refreshes monitor detection and reconfigures overlays.
    /// </summary>
    Task RefreshMonitorsAsync();
}
