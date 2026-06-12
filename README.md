# Flux

A lightweight Windows desktop customization tool featuring a reactive audio visualizer and widget system. A modern Rainmeter-like alternative built for Windows with WPF, WASAPI, and clean hexagonal architecture.

## Features
- **Audio Visualizer**: Real-time spectrum visualization with bars or circular mode
- **Widget System**: Clock, Date, System Info widgets with drag-and-drop positioning
- **GPU Rendering**: Optional SkiaSharp-powered GPU acceleration
- **Multi-monitor**: Overlay spans all monitors or specific ones
- **Customizable**: Colors, gradients, glow effects, beat reactivity
- **Settings persistence**: JSON in `%AppData%/Flux/`
- **Windows-native audio**: Direct WASAPI loopback capture through NAudio

## Architecture
- **Domain** (`Flux.Domain/`): Core models - `FluxSettings`, `ColorRgb`, widget configs
- **Application** (`Flux.Application/`): Services like `IFluxService`, `SpectrumProcessor`
- **Infrastructure** (`Flux.Infrastructure/`): Windows audio capture (NAudio/WASAPI), settings persistence
- **Presentation** (`Flux.Presentation/`): Windows-native WPF app, tray icon, overlays, modern settings UI
- **Avalonia** (`Flux.Avalonia/`): Windows-targeted alternate/experimental UI shell

## Setup
```powershell
winget install --id Microsoft.DotNet.SDK.9 --exact --source winget
```

Restart the terminal if `dotnet` is not immediately available on `PATH`.

## Run
```powershell
dotnet build
dotnet run --project .\Flux.Presentation\Flux.Presentation.csproj
```
Tray icon appears; right-click for options.

## Controls
- **Tray Menu**: Toggle overlay, settings, widgets, edit mode
- **Global Hotkeys**: Ctrl+Alt+Shift+E (toggle), Ctrl+Alt+Shift+S (settings)

## Tests
```powershell
dotnet test
```

## Packages
- NAudio (WASAPI loopback audio)
- MathNet.Numerics (FFT)
- SkiaSharp (GPU rendering)
- Microsoft.Extensions.* (Hosting, DI)
