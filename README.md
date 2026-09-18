# MUTHUR 6000 // CRT Terminal Emulator

<div align="center">
  <img width="706" height="546" alt="Screenshot 2026-09-17 125216" src="https://github.com/user-attachments/assets/385aea76-3f24-40a2-9d90-8838c06be057" />
  <br>
  <strong>WEYLAND-YUTANI CORP • NOSTROMO OVERMONITORING MATRIX • 2122</strong>
  <br>
  <em>A vintage single-color phosphor CRT monitor terminal emulator for Windows with character streaming, baud rate emulation, and monochrome retro effects.</em>
</div>

---

## Overview

**MUTHUR 6000** is a lightweight, frameless desktop application for Windows that replicates the iconic mainframe terminal console from the movie *Alien*. It reads telemetry `.txt` files from a local directory and types them out character-by-character at authentic serial baud rates, complete with single-color phosphor bloom glow, vintage CRT scanlines, and an authentic monochrome glitch suite.

---

## Telemetry File Ingestion & Boot Order Rules

The application reads all `.txt` documents placed in the `text/` subdirectory adjacent to the application executable.

### 🔴 The Boot Priority Rule (First File Played)
- **Files beginning with `BOOT_`** (case-insensitive, e.g., `BOOT_nostromo_mother_mainframe_telemetry.txt`) will **ALWAYS** be the very first file streamed and typed out to the terminal screen whenever the application is launched or the boot sequence is restarted.
- If multiple `BOOT_` files are present, the first alphabetical `BOOT_` file takes priority.

### 🔄 Shuffled Playlist & Non-Repeating Playback
- Once the initial `BOOT_` file has completed streaming:
  1. The terminal holds for **3 seconds** at the end of the file.
  2. The screen automatically clears.
  3. The engine selects another `.txt` file at random from the remaining files in the `text/` folder.
- **No text file will repeat** until **all** `.txt` files in the `text/` directory have been displayed at least once.
- Once every file in the directory has been displayed, the playlist deck is automatically reshuffled and the continuous stream continues endlessly.

---

## Features

### ⚡ Baud Rate Character Emulation
- Streams text character-by-character at true serial baud rates (8N1):
  - **300 Baud**: Slow, vintage teletype cadence (~30 chars/s)
  - **600 Baud**: Standard retro telecom cadence (~60 chars/s)
  - **1200 Baud**: **Default MUTHUR speed** (~120 chars/s)
  - **2400 Baud**: High-speed mainframe stream (~240 chars/s)
  - **4800 Baud**: Fast serial terminal feed (~480 chars/s)
  - **9600 Baud**: High-throughput bursts (~960 chars/s)
- **Instant Mid-Stream Switching**: Seamlessly change speeds on the fly via the header baud badge or the right-click menu without freezing or buffering pauses.

### 📺 Monochrome Single-Color Phosphor Effects
- **Monochrome CRT Glitch**: Generates horizontal slice dropouts and frame jitter matching only the active phosphor and background colors (no chromatic RGB artifacts).
- **Right-to-Left Beam Scan & Reverse-Video Highlight**: A horizontal beam shoots from the right side of the monitor across the screen, briefly illuminating a target character in **reverse-video** (solid phosphor background block with inverted dark text) before disappearing.
- **Adjustable Phosphor Bloom Glow**: Phosphor bloom applied to the terminal text with a user-adjustable intensity slider (0px to 25px).
- **Vintage CRT Scanlines**: Toggleable scanlines (1px to 20px thickness) with customizable density (Off by default).
- **CRT Snow Static Noise**: Toggleable monochrome static noise overlay with adjustable intensity.

### 🎨 Color & Transparency Customization
- **Phosphor (Text) Colors**:
  - Matrix / Nostromo Green (`#00FF66` - Default)
  - Solar Amber / Alien Gold (`#FFB000`)
  - Nostromo Cyan (`#00F0FF`)
  - Phosphor White (`#F0F0F0`)
  - Glitch Red (`#FF2244`)
  - Night City Violet (`#A040FF`)
  - Synthwave Magenta (`#FF007F`)
  - Custom Hex Color dialog
- **Background CRT Glass Colors**:
  - Tinted CRT Dark Green (`#08140B` - Default)
  - Pitch Black Void (`#000000`)
  - Dark Charcoal (`#0E1116`)
  - Deep Amber Dark (`#140B04`)
  - Deep Space Navy (`#040812`)
  - Custom Hex Color dialog
- **Dual Opacity Sliders**:
  - Independent **Background Opacity** slider (0% to 100%)
  - Independent **Phosphor Text Opacity** slider (10% to 100%)

### 🔤 Typography & Cursor
- **Blinking Block Cursor**: Classic retro solid block cursor `█` rendered at the typing head.
- **Font Selection**: Monospace quick-select presets (Cascadia Code, Consolas, Lucida Console, Courier New) plus a full **System Font Picker** browser.
- **Font Sizes**: 10 pt to 24 pt.

### 🖥️ Interactive Controls
- **Header Bar**:
  - Left: `[ MUTHUR 6000 ]` identity badge
  - Center: `[ OVERMONITORING MATRIX ]` badge (dynamically matches phosphor color)
  - Right: `[ 1200 BAUD ]` quick-cycle badge, `PAUSE/RESUME`, `SKIP`, `CLR`, and `✕` close button
- **Footer Telemetry**: Displays current file feed name, streaming status, and playlist deck state.
- **Persistence**: All settings (window position, dimensions, colors, baud rate, effects, opacities) are automatically saved to `%APPDATA%\MUTHUR6000\settings.json`.
- **Zero-Footprint Clean Exit**: Complete process teardown ensuring no background threads or worker memory remain in Windows Task Manager upon closing.

---

## Controls Summary

| Action | Control |
|---|---|
| **Move Window** | Click & drag title bar or glass background |
| **Resize Window** | Drag bottom-right corner grip |
| **Cycle Baud Rate** | Click `[ 1200 BAUD ]` badge in header bar |
| **Pause / Resume** | Click `PAUSE` button or right-click menu |
| **Skip File** | Click `SKIP` button or right-click menu |
| **Clear Screen** | Click `CLR` button or right-click menu |
| **Open Settings** | Right-click anywhere on the widget |
| **Close Application** | Click `✕` button or right-click `Close MUTHUR Console` |

---

## Building & Packaging

### Prerequisites
- Windows 10/11 x64
- [.NET 10.0 SDK](https://dotnet.microsoft.com/download)

### Build Debug
```powershell
dotnet build
dotnet run
```

### Build GitHub Release Packages
Run the automated release script:
```powershell
pwsh -File .\build_release.ps1 -Version "1.0.0"
```

This compiles and packages:
1. **Standalone (Self-Contained) Package**: Single `.exe` binary that runs out-of-the-box without requiring .NET 10 runtime installed.
2. **Framework-Dependent Package**: Compact single `.exe` binary for systems with .NET 10 installed.
3. Release `.zip` archives with SHA256 checksums in `release/`.

---

## License

This project is licensed under the [MIT License](LICENSE).

