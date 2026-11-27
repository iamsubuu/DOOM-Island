# DOOM Island

**A native Windows 10/11 Dynamic Island overlay built with C#, .NET 10, and WinUI 3.**

DOOM Island brings an interactive, hardware-accelerated Dynamic Island bar to the top of your Windows display — featuring automatic browser media sync, live WASAPI audio visualization, synchronized LRCLIB lyrics, clipboard history, system controls, timers, activity tracking, and Windows notifications.

> **Publisher:** ArcSubash · **Current Version:** `2.5.2` · **Target OS:** Windows 10 (Build 19041+) & Windows 11 (`x64`)

---

## Features

- **Borderless Floating Dynamic Island (`DynamicIslandWindow`)**
  - Pure-black translucent overlay (`WS_EX_LAYERED`, `WS_EX_TOOLWINDOW`, `WS_EX_NOACTIVATE`) with `WM_NCHITTEST` click-through outside the pill bounds
  - Stealth invisible top-edge hover bar (`StealthTopBar` + `HoverToOpen`), drag-to-reposition, and Per-Monitor V2 DPI multi-monitor positioning
  - Smooth `Microsoft.UI.Composition` spring animations (`Fluid`, `Snappy`, `Silk`, `Instant`), idle breathing, arrival bounce, and a quick **Refresh / Restart** action in the expanded bar
- **Browser-Driven Music Player & WASAPI Equalizer**
  - Zero-config automatic media detection and playback control for **Google Chrome, Microsoft Edge, and Opera** via Windows SMTC (`CompositeBrowserMediaBridge`)
  - Optional Manifest V3 extension (`NOVAIsland.BrowserExtension`) + Native Messaging Host (`NOVAIsland.BrowserHost`) for per-tab switching and **Focus Tab** control
  - Real-time audio-reactive equalizer bars powered by `NAudio` WASAPI loopback FFT (in-memory only; never recorded)
  - Zero Spotify Web API or OAuth credentials required
- **Synchronized Lyrics & Fullscreen Stage**
  - Automatic and manual lyric lookups via **LRCLIB** (`lrclib.net`), LRC timestamp parsing, offline SQLite `lyrics_cache`, synced `LyricsPanel`, and a blurred-artwork `FullscreenLyricsWindow`
- **Productivity, Clipboard, System & Activity**
  - **Timer:** Pomodoro, Countdown, and Stopwatch with lap tracking and circular progress ring
  - **Clipboard History:** Captures text, links, code, and images via `WM_CLIPBOARDUPDATE` into local SQLite with pin/copy/delete, retention rules, password-manager exclusion flags, sensitive-token filtering, excluded apps, and Incognito/InPrivate pause
  - **System Controls:** Battery status, CoreAudio volume & audio output device switcher, WMI/DDC-CI display brightness, Wi-Fi SSID/signal, CPU/RAM metrics, and microphone/camera privacy indicators
  - **Activity Tray:** Quick screenshot capture, `Pictures\Screenshots` watcher, and `Downloads` progress/file watcher
  - **Windows Notifications:** `UserNotificationListener` integration with Focus Assist detection and dismissal *(requires MSIX / Microsoft Store package identity)*
- **Dashboard Hub, 14-Section Settings & OS Integration**
  - `DashboardWindow` and responsive searchable `SettingsWindow` (14 sections, live Appearance & Position previews, SQLite/disk accounting, JSON export, factory reset)
  - System tray (`H.NotifyIcon.WinUI`), Windows startup registration, global hotkeys (`RegisterHotKey`), and lock-screen privacy masking (`WTSRegisterSessionNotification`)

---

## Installation (End Users)

Download the latest release from the **[GitHub Releases](https://github.com/iamsubuu/DOOM-Island/releases)** page:

### Option 1 — Standalone Windows Installer (Recommended for Direct Download)
1. Download `DOOMIsland-Setup-x64-v<version>.exe`.
2. Run the installer (GUI wizard, or silent install with `/SILENT /NOLAUNCH /DIR="<path>"`).
3. It installs DOOM Island, creates Start Menu/Desktop shortcuts, registers Add/Remove Programs uninstall support, and configures Native Messaging for Chrome, Edge, and Opera.

### Option 2 — MSIX Package (Enables Windows Notifications Listener)
1. Download `DOOMIsland-x64-v<version>.msix` (or install from **Microsoft Store** as **DOOM Island - Dynamic Island**).
2. For local sideloading from GitHub Releases, download `DOOMIsland-CodeSigning.cer` and `Install-DOOMIsland-MSIX.ps1` into the same folder and run:
   ```powershell
   powershell -ExecutionPolicy Bypass -File .\Install-DOOMIsland-MSIX.ps1
   ```
   *(Installing via MSIX grants Windows Package Identity, enabling third-party Windows toast notifications via `UserNotificationListener`.)*

### Option 3 — Portable ZIP
1. Download `DOOMIsland-Win64-Portable-v<version>.zip`.
2. Extract anywhere and run `app\DOOMIsland.exe`, or run `Install-DOOMIsland.ps1` to install and register shortcuts + Native Messaging.

### Optional: Browser Extension for Multi-Tab Control
Basic Chrome, Edge, and Opera media sync works **automatically** out of the box via Windows SMTC. If you also want multi-tab switching and **Focus Tab** support:
1. Download and extract `DOOMIsland-BrowserExtension-MV3.zip` (or use the `BrowserExtension` folder inside your install directory).
2. Open `chrome://extensions`, `edge://extensions`, or `opera://extensions`, enable **Developer mode**, and click **Load unpacked** pointing to the extracted folder.
3. The extension's pinned key resolves to ID `amplhmapoeandecddkchambamhafdcgp`, which the installer pre-registers in `com.doomisland.host.json`.

---

## Building from Source & Packaging Releases

### Prerequisites (Windows x64)
- **Git**
- **.NET 10 SDK** (or **.NET 11 SDK** — [Directory.Build.props](Directory.Build.props) sets `<RollForward>LatestMajor</RollForward>`)
- **Internet access** on first build to restore NuGet packages (automatically provides `Microsoft.Windows.SDK.BuildTools` with `makeappx.exe` and `signtool.exe`)
- *(Optional)* **Inno Setup 6** (`iscc.exe`) if you also want to generate the optional Inno Setup EXE from [installer/DOOMIsland.iss](installer/DOOMIsland.iss)

### Clone & Test
```powershell
git clone https://github.com/iamsubuu/DOOM-Island.git
cd DOOM-Island
dotnet test NOVAIsland.sln -p:Platform=x64
```

### Package Release Artifacts
Run [installer/Package-Release.ps1](installer/Package-Release.ps1) from the repository root:

```powershell
# Build everything (Setup EXE, Portable ZIP, MV3 Extension ZIP, and signed MSIX)
powershell -ExecutionPolicy Bypass -File .\installer\Package-Release.ps1

# Build only Setup EXE + Portable ZIP + Extension ZIP
powershell -ExecutionPolicy Bypass -File .\installer\Package-Release.ps1 -Format Exe

# Build only local sideloadable MSIX + Extension ZIP
powershell -ExecutionPolicy Bypass -File .\installer\Package-Release.ps1 -Format Msix

# Build unsigned MSIX ready for Microsoft Store Partner Center upload
powershell -ExecutionPolicy Bypass -File .\installer\Package-Release.ps1 -Format Msix -StorePackage
```

> **Signing Notes:**
> - If no certificate is provided, [installer/Sign-Artifacts.ps1](installer/Sign-Artifacts.ps1) creates and uses a local self-signed code-signing certificate in `Cert:\CurrentUser\My`.
> - For production Authenticode signing with a `.pfx`, pass `-PfxPath` and `-PfxPassword` (or set `$env:DOOM_SIGN_PFX` and `$env:DOOM_SIGN_PFX_PASSWORD`).
> - If `dotnet.exe` is not at `C:\Program Files\dotnet\dotnet.exe`, pass `-DotnetExe (Get-Command dotnet).Source`.

All release outputs are written to `artifacts\installer\`:

| Artifact | Purpose |
|---|---|
| `DOOMIsland-Setup-x64-v<version>.exe` | Standalone GUI & silent Windows installer |
| `DOOMIsland-Win64-Portable-v<version>.zip` | Self-contained portable bundle + PowerShell install/uninstall scripts |
| `DOOMIsland-x64-v<version>.msix` | Packaged MSIX build (with `userNotificationListener` capability) |
| `DOOMIsland-BrowserExtension-MV3.zip` | Chrome / Edge / Opera Manifest V3 companion extension |
| `DOOMIsland-CodeSigning.cer` & `Install-DOOMIsland-MSIX.ps1` | Local sideload helper and public test certificate |

---

## Solution Architecture

```text
NOVAIsland.sln
├── src/
│   ├── NOVAIsland.Core             # Domain models, enums, interfaces, ViewModels, AppConstants
│   ├── NOVAIsland.Services         # Media bridge coordinator, LRCLIB client, timer & settings services
│   ├── NOVAIsland.Infrastructure   # SQLite persistence, Win32/WinRT interop, WASAPI, clipboard, hotkeys
│   ├── NOVAIsland.App              # WinUI 3 shell (DynamicIsland, Dashboard, Settings, Fullscreen Lyrics)
│   ├── NOVAIsland.BrowserHost      # C# Native Messaging host (stdin/stdout JSON <-> ACL Named Pipe)
│   ├── NOVAIsland.BrowserExtension # Chrome / Edge / Opera Manifest V3 extension
│   └── NOVAIsland.Setup            # Self-contained Windows GUI & silent installer
├── tests/
│   └── NOVAIsland.Tests            # Unit & E2E integration tests (xUnit + NSubstitute)
├── installer/                      # Package-Release.ps1, Sign-Artifacts.ps1, Install/Uninstall scripts, .iss
└── docs/                           # Architecture, feature matrix, roadmap, and security review
```

### Documentation
- [Development Status](DEVELOPMENT_STATUS.md) — implementation status and verified checks
- [Architecture Overview](docs/architecture.md) — WinUI 3 + Native Messaging IPC design
- [Feature Matrix](docs/feature-status.md) — module-by-module parity & OS limitations
- [Security & Privacy Review](docs/security-review.md) — threat model, privacy filters, and IPC isolation

---

## Privacy & Security

- **Local-First Storage:** Settings, clipboard history, and cached lyrics stay strictly on your device in `%LocalAppData%\DOOMIsland\doom_island.db`.
- **Zero Telemetry:** No analytics, no crash uploads, no ads, and no third-party OAuth/API secrets.
- **IPC Isolation:** The Native Messaging Named Pipe (`DOOMIsland.BrowserBridge.<UserSidHash>`) restricts `FullControl` strictly to the current Windows user's SID.
- **Clipboard Privacy:** Honors password-manager exclusion flags, filters JWTs/API keys/private keys, respects excluded apps, and pauses during Incognito/InPrivate sessions.
- **Offline Cutoff:** Enable **Local-Only Mode** in Settings to disable all outbound `lrclib.net` lyrics requests while keeping cached lyrics available.
