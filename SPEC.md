# Flipclock — Specification

A realistic 3D split-flap desktop clock for Windows.

## Stack

- .NET 10, WinUI 3, **unpackaged**, shipped as a self-contained folder (zip). Single-file publish is optional — dropped if it causes friction.
- Direct3D 11 via **Vortice.Windows**, rendered into a WinUI `SwapChainPanel`.
- **SkiaSharp** for rasterizing digit glyphs to textures.
- **Tomlyn** for TOML config.
- **H.NotifyIcon.WinUI** for the tray icon.
- Must run from a writable folder (config lives next to the exe).

## Solution structure

| Project | Responsibility |
|---|---|
| `Flipclock.Core` | Digit-diff logic, date formatting, TOML load/validate/save, layout math. Covered by xUnit tests. |
| `Flipclock.Rendering` | D3D11 scene, tile geometry, PBR shading, shadows, flip animation, glyph textures. |
| `Flipclock.App` | WinUI shell, settings panel, tray, window management, audio. |

Renderer and UI are verified manually.

## Clock face

- 24h format `HH:mm:ss`, **six tiles** (one per digit), digits grouped in pairs.
- Separator `:` rendered as **two lit 3D dots** in the digit color.
- Seconds tiles scale via slider **30–100%** of hour/minute tile size (default 100%), **bottom-aligned**. The second colon stays at hour/minute size.

## Rendering

- Straight-on camera, low FOV, mild perspective.
- Tiles: rounded, bevelled, visible thickness, visible horizontal split gap, subtle hinge pins.
- **PBR** shading; finish slider matte ↔ glossy (default satin plastic).
- One directional key light (sliders: **angle**, **intensity**) + soft ambient.
- **PCF soft shadow maps** — tiles cast shadows onto the background.
- 4× MSAA.
- **Render on demand** only (during flips, resize, settings changes) — no constant loop.
- Fallback to **WARP** if hardware D3D11 is unavailable.
- Digits are SkiaSharp textures on tile faces, so any installed system font works and is lit by the scene.

## Flip animation

- Classic split-flap: top half rotates down around the hinge, its back reveals the new bottom half, settles with a bounce.
- Flip starts **on the second boundary**.
- Multi-digit changes cascade right → left.
- Sliders:
  - Flip duration: 150–1000 ms (default 450)
  - Cascade delay: 0–200 ms (default 60)
  - Bounce strength: 0 (none) → strong

## Sound

- **One tick per second change**, played when the **first flap lands**.
- Bundled default tick; user may select a custom `.mp3`.
- On/off toggle and volume slider.

## Layout & sizing

- Clock fills the window minus a uniform margin.
- Margin slider **0–500** DIP (default 250).
- Margin auto-shrinks so the clock never drops below ~40% of the window.
- Everything scales with window resize.

## Date line

- Shows weekday and date, e.g. **`Giovedì, 15 Ottobre 2026`** (first letter of weekday and month capitalized).
- **Date language is configurable: Italian (`it-IT`, default), English (`en-GB`), Spanish (`es-ES`).** Same format pattern `Weekday, D Month YYYY` in the selected language, e.g. `Thursday, 15 October 2026`, `Jueves, 15 Octubre 2026`.
- Position: **bottom-left / bottom-center / bottom-right**, inside the bottom margin.
- Size scales with window, plus a size multiplier; can be hidden.
- Refreshes at midnight.

## Customization

- Colors: tile face, digits, date text, background fallback color.
- Fonts: separate pickers for **digits** and **date**, listing installed system fonts, with weight selection.
- Background image: **Fill (uniform-to-fill, cropped)**; solid color when none; optional dim slider. Tiles cast shadows onto it.
- No theme presets in v1 — only current state is persisted.

## Window

- Normal resizable window.
- Fullscreen toggle: **F11** or double-click.
- Optional always-on-top.
- **Borderless** toggle anytime: panel switch or **Ctrl+B**. When borderless, drag anywhere to move, resize from invisible edges.
- Size, position and state persisted; if the saved position is on a missing monitor/off-screen, snap to a visible one.
- Minimum window size 480×270.

## Tray icon

- Always present.
- **Minimize → hides to tray. Close (X) → exits the application.**
- Menu: Show/Hide, Settings, Always on top, Borderless, Mute tick, Exit.
- Double-click toggles show/hide.

## Settings panel

- Opened by a **cog button in the top-right corner**; cog auto-fades after ~3 s mouse idle.
- Slide-in overlay from the right, **live preview**, **debounced autosave**.
- "Reset to defaults" (with confirmation).
- "Test tick" and "Test flip" preview buttons.
- Contains every option listed in this spec.

## Localization (app UI)

- Panel, tray menu and messages follow the Windows display language; ships **Italian** and **English**; English is the fallback.
- Date language is a separate setting (see Date line).

## Configuration file

- `config.toml` **next to the exe**. **No fallback location.**
- If the folder is not writable or saving fails: show a warning InfoBar once per launch and continue with in-memory settings.
- Invalid keys revert to defaults; a corrupt file is preserved as `config.toml.bak`.
- Background image and sound are stored as **original paths**; if missing, silently fall back to defaults.

## Out of scope for v1

12h mode, theme presets, start with Windows, multi-monitor spanning, anything not listed above.

## Reference

Layout inspiration: http://flipclock.online
