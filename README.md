# Equalizer

A lightweight Windows WPF overlay that draws a configurable, reactive audio visualizer on top of your desktop wallpaper. Designed with a clean hexagonal architecture

## Architecture
- **Domain** (`Equalizer.Domain/`): Core models and invariants, e.g., `EqualizerSettings`, `ColorRgb`.
- **Application** (`Equalizer.Application/`): Use-cases and ports, services like `IEqualizerService`, `SpectrumProcessor`.
- **Infrastructure** (`Equalizer.Infrastructure/`): Adapters like `WASAPILoopbackAudioInput` (real audio capture with NAudio) and `JsonSettingsRepository`.
- **Presentation** (`Equalizer.Presentation/`): WPF app with DI host, system tray, overlay windows, settings UI, and global hotkeys.

## Features
- Real-time audio capture using WASAPI loopback (NAudio)
- FFT-based spectrum processing (MathNet.Numerics) with log-spaced band aggregation
- Multi-monitor overlay windows
- Click-through and always-on-top toggles (tray menu)
- Global hotkeys: Ctrl+Alt+Shift+E (toggle overlay), Ctrl+Alt+Shift+S (open settings)
- JSON settings persisted in `%AppData%/Equalizer/settings.json`

## Run
```powershell
# Build
dotnet build

# Run (WPF)
dotnet run --project .\Equalizer.Presentation\Equalizer.Presentation.csproj
```
Tray icon appears; use context menu to show/hide overlay and open settings.

## Tests
```powershell
dotnet test
```

## Packages
- NAudio (WASAPI loopback)
- MathNet.Numerics (FFT)
- Microsoft.Extensions.* (Hosting, DI)

## Roadmap
- Settings: more visualization options (themes, bar shape, sensitivity profiles)
- Packaging: MSIX/Squirrel, auto-start at login
- Telemetry-free diagnostics and logging controls
