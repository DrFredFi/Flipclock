# Flipclock

A realistic **3D split-flap desktop clock** for Windows.

Flipclock renders a six-tile `HH:mm:ss` clock in real 3D: rounded, bevelled tiles with visible thickness, a split gap and hinge pins, lit by a movable light and casting soft shadows onto the background. Every second the changing tiles flip like a real mechanical clock: the top flap rotates down on its hinge, shows the new digit on its back and settles with a small bounce. A mechanical *tick* plays when the flap lands.

The app is built to sit on a second monitor, a TV or a corner of the desktop. You can run it fullscreen, borderless or always-on-top, and almost every visual detail can be changed from a live settings panel.

## Features

- **Real 3D rendering**: physically based shading, a directional key light, soft shadow maps and 4× MSAA.
- **Authentic flip animation**: hinge rotation with bounce. When several digits change, the flips cascade from right to left.
- **Tick sound** in time with the flap landing. A tick is built in, and you can use your own `.mp3` instead.
- **Date line** (for example `Friday, 16 October 2026`) in Italian, English or Spanish.
- **Any installed font** for the digits and the date, with weight selection.
- **Background image** (fill and crop) with an optional dim. The tiles cast shadows onto it.
- **Window modes**: normal, fullscreen, borderless (drag anywhere to move) and always-on-top.
- **Tray icon**: minimizing hides the clock to the tray, and the tray menu has quick toggles.
- **Live settings panel** with instant preview and automatic saving.
- **Portable**: the settings live in a `config.toml` next to the executable. Nothing is written to the registry or `AppData`.
- **Light on resources**: the clock only redraws while something changes (a flip, a resize or a settings change). Between flips the GPU is idle.
- **Localized UI** in English and Italian, following the Windows display language.

## Customization

Open the settings panel with the **cog button** in the top-right corner, which fades out after a few seconds without mouse movement, or from the tray menu. Every change previews live and is saved automatically.

| Group | Option | Range / values | Default |
|---|---|---|---|
| **Clock** | Seconds tile size | 30–100 % of the hour/minute tiles | 100 % |
| | Margin around the clock | 0–500 DIP | 250 |
| **Colors** | Tiles, digits, date, background | any color | dark tiles, off-white digits, slate background |
| **Fonts** | Digit font and weight | any installed font, 100–950 | Segoe UI Bold |
| | Date font and weight | any installed font, 100–950 | Segoe UI Regular |
| **Lighting** | Light angle | 0–360° | 135° |
| | Light intensity | 0.2–2.0 | 1.0 |
| | Finish (matte ↔ glossy) | 0–1 | 0.45 (satin) |
| **Animation** | Flip duration | 150–1000 ms | 450 ms |
| | Cascade delay between digits | 0–200 ms | 60 ms |
| | Bounce | none → strong | medium |
| **Sound** | Tick on/off, volume | 0–100 % | on, 60 % |
| | Custom tick sound | any `.mp3` (or other format Windows can decode) | built-in tick |
| **Background** | Image | any image file, fill and crop | none (solid color) |
| | Dim | 0–90 % | 0 % |
| **Date** | Visible | on/off | on |
| | Position | bottom left / center / right | center |
| | Size multiplier | 0.5–3× | 1× |
| | Language | Italian, English, Spanish | Italian |
| **Window** | Always on top, borderless, fullscreen | on/off | off |

The panel also has **Test tick**, **Test flip** and **Reset to defaults** buttons.

### Keyboard and mouse

| Action | Shortcut |
|---|---|
| Toggle fullscreen | `F11` or double-click the clock |
| Toggle borderless | `Ctrl+B` |
| Close the panel / leave fullscreen | `Esc` |
| Move a borderless window | drag anywhere on the clock |
| Hide to the tray | minimize |
| Show / hide from the tray | double-click the tray icon |

### Configuration file

Settings are stored in `config.toml` next to `Flipclock.exe`, grouped into readable sections (`[clock]`, `[colors]`, `[fonts]`, `[lighting]`, `[animation]`, `[sound]`, `[background]`, `[date]`, `[window]`). You can edit the file by hand while the app is closed:

```toml
[colors]
tile = "#1C1C1E"
digit = "#F2F2F0"

[animation]
flip_duration_ms = 450
cascade_delay_ms = 60
bounce = 0.5
```

The app is forgiving about mistakes in this file:

- A missing or out-of-range value falls back to its default.
- A file that cannot be parsed is kept as `config.toml.bak`, and the app starts with the defaults.
- If the folder is not writable, a warning appears once and the settings are kept in memory for that session.
- If a background image or sound file has gone missing, the app quietly uses the default.

## Getting started

### Requirements

- Windows 10 version 1809 or later, or Windows 11 (x64 or ARM64)
- A Direct3D 11 GPU. Without one, the app falls back to the WARP software renderer.
- To build: the [.NET 10 SDK](https://dotnet.microsoft.com/download)

### Build and run

```powershell
git clone https://github.com/DrFredFi/Flipclock.git
cd Flipclock
dotnet build src/Flipclock.App/Flipclock.App.csproj -c Release -p:Platform=x64
```

The executable is written to `src/Flipclock.App/bin/x64/Release/net10.0-windows10.0.19041.0/win-x64/Flipclock.exe`.

### Publish a portable folder

The app is **unpackaged and self-contained**, so the published folder runs on any machine without installing .NET or the Windows App SDK:

```powershell
dotnet publish src/Flipclock.App/Flipclock.App.csproj -c Release -p:Platform=x64 -r win-x64 -o publish/win-x64
```

Zip the folder and put it anywhere **writable**, because `config.toml` is saved next to the executable. For ARM64, use `-p:Platform=ARM64 -r win-arm64`.

### Run the tests

```powershell
dotnet test tests/Flipclock.Core.Tests
```

## How it works

### Technologies

| Technology | What it is used for |
|---|---|
| **.NET 10 / C#** | The whole application. |
| **WinUI 3 (Windows App SDK)** | Window, settings panel, info bars and window modes (through `AppWindow` and `OverlappedPresenter`). The app is unpackaged and self-contained. |
| **Direct3D 11** via [Vortice.Windows](https://github.com/amerkoleci/Vortice.Windows) | The 3D renderer. Frames go into a composition swap chain hosted in a WinUI `SwapChainPanel`. HLSL shaders are compiled at startup with the D3D compiler. |
| **HLSL** | One shader file with PBR-style shading for the tiles, a depth-only pass for shadows, and the background pass (solid color or image with dim). |
| [**SkiaSharp**](https://github.com/mono/SkiaSharp) | Draws each digit with the chosen system font into a texture that is mapped onto the tile faces, so any font works and is lit like the rest of the scene. It also decodes the background image. |
| [**NAudio**](https://github.com/naudio/NAudio) (WASAPI) | Low-latency tick playback. The sound is decoded once into memory and mixed into a WASAPI stream that is always open, so a tick starts within milliseconds. Custom files are decoded through Media Foundation. |
| [**Tomlyn**](https://github.com/xoofx/Tomlyn) | Reads and writes `config.toml`. |
| [**H.NotifyIcon.WinUI**](https://github.com/HavenDV/H.NotifyIcon) | Tray icon and its context menu. |
| **xUnit** | Unit tests for the core logic. |

### Solution layout

```
Flipclock.slnx
├─ src/
│  ├─ Flipclock.Core/        UI-independent logic
│  │   ├─ Clock/             digit diffing, flip animator, layout math, date formatting
│  │   └─ Settings/          settings model, TOML schema and validation, load/save
│  ├─ Flipclock.Rendering/   Direct3D 11 renderer
│  │   ├─ ClockRenderer.cs   device, swap chain, shadow map, MSAA, draw passes
│  │   ├─ ClockScene.cs      tile placement and per-frame tile state
│  │   ├─ Mesh.cs            procedural tile geometry (rounded, bevelled, split)
│  │   ├─ GlyphRasterizer.cs SkiaSharp digit textures and background image decoding
│  │   └─ Shaders/Clock.hlsl
│  └─ Flipclock.App/         WinUI 3 shell
│      ├─ MainWindow         timing, animation loop, layout, input
│      ├─ SettingsPanel      the settings UI
│      ├─ WindowController   borderless / fullscreen / always-on-top, placement, tray minimize
│      ├─ TickPlayer         WASAPI tick playback
│      └─ TrayIcon, Strings  tray menu, localization
└─ tests/
   └─ Flipclock.Core.Tests/  xUnit tests for Core
```

`Flipclock.Core` has no UI or GPU dependencies, so the logic that is easy to get wrong (which digits changed, animation timing, layout, config validation) is covered by fast unit tests. The renderer and the UI are checked by hand.

### The life of one second

1. A timer fires just after each second boundary. `ClockDigits` compares the new time with the digits on screen and works out which tiles must flip.
2. `FlipAnimator` schedules those flips from right to left, using the cascade delay, and returns the moment the first flap will land. A second timer is set for that moment to play the tick.
3. While a flip is running, the app redraws on every frame (`CompositionTarget.Rendering`). For each tile, the animator works out the flap angle, using an eased rotation with a bounce at the end.
4. Each frame has two passes. The first renders the tiles from the light's point of view into a 2048² shadow map. The second renders the scene with 4× MSAA, sampling that shadow map with PCF filtering for soft shadows.
5. When every flap has settled, the app stops redrawing until the next second.

If the GPU device is lost (for example after a driver update), the renderer rebuilds itself on the next frame.

## Roadmap ideas

These are not in v1:

- 12-hour mode
- Theme presets
- Start with Windows
- Spanning several monitors

## Acknowledgements

Layout inspired by [flipclock.online](http://flipclock.online).
