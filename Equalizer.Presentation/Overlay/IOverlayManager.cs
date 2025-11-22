using System.Threading.Tasks;

namespace Equalizer.Presentation.Overlay;

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
}
