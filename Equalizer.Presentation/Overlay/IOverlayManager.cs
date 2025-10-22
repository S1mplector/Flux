using System.Threading.Tasks;

namespace Equalizer.Presentation.Overlay;

public interface IOverlayManager
{
    Task ShowAsync();
    Task HideAsync();
    Task ToggleAsync();
    bool IsVisible { get; }
}
