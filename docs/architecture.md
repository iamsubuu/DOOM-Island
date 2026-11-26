# DOOM Island — Native WinUI 3 & Browser Extension Architecture

> **Document Status:** Revised Baseline Architecture (Pre-Implementation)  
> **Primary Visual Reference:** Imported Google AI Studio React prototype (`src/App.tsx`, `src/components/**`, `src/index.css`)  
> **Product Identity:** **DOOM Island** (configurable via `AppConstants` & General Settings; solution namespace prefix `NOVAIsland.*`)  
> **Target Stack:** C# · .NET 10 (`net10.0-windows10.0.19041.0`) · WinUI 3 · Windows App SDK `2.7.0` · Windows Composition APIs · Win32 Interop · SQLite · Chrome/Edge Manifest V3 Extension + Native Messaging

---

## 1. Architectural Mandate & Technology Constraints

### Mandatory Stack
- **Desktop UI:** WinUI 3 (`Microsoft.WindowsAppSDK` `2.7.0`) + XAML + `CommunityToolkit.Mvvm` (`8.4.2`) + Microsoft Dependency Injection (`Microsoft.Extensions.Hosting`).
- **Runtime & Compatibility (Verified):**
  - **Target Framework:** `net10.0-windows10.0.19041.0` (`NOVAIsland.App`, `NOVAIsland.Infrastructure`) and `net10.0` (`NOVAIsland.Core`, `NOVAIsland.Services`, `NOVAIsland.BrowserHost`, `NOVAIsland.Tests`).
  - **Minimum OS:** Windows 10 2004 (Build `19041`) and Windows 11 (`22000+`). Host verification confirmed Windows 10 Pro 22H2 (Build `19045`) and `.NET SDK 11.0.100` at `C:\Program Files\dotnet\sdk\11.0.100` (note: `dotnet` must be added to `PATH`, and `.NET 10` SDK or NuGet `net10.0` targeting pack restore is required at Phase 0).
  - **Verified NuGet Stable Versions (`nuget.org`):** `Microsoft.WindowsAppSDK` `2.7.0`, `Microsoft.Windows.SDK.BuildTools` `10.0.28000.2957`, `CommunityToolkit.Mvvm` `8.4.2`, `Microsoft.Data.Sqlite` `10.0.15`, `WinUIEx` `2.9.3`, `H.NotifyIcon.WinUI` `2.4.1`, `NAudio` `3.3.0`.
- **Animations & Windowing:** `Microsoft.UI.Composition` (`Compositor`, `SpringVector3NaturalMotionAnimation`, `SpringScalarNaturalMotionAnimation`, `Visual` transforms) paired with Win32 interop (`AppWindow`, `OverlappedPresenter`, `WS_EX_LAYERED`, `WS_EX_TOOLWINDOW`, `WS_EX_NOACTIVATE`, `SetWindowPos`, `WM_NCHITTEST`).
- **Storage:** Embedded SQLite (`Microsoft.Data.Sqlite`) + DPAPI (`ProtectedData`) for any sensitive tokens/settings if introduced.
- **Media Source:** Custom **Chrome/Edge Manifest V3 Browser Extension** (`NOVAIsland.BrowserExtension`) communicating with a C# **Native Messaging Host** (`NOVAIsland.BrowserHost`) over `stdin`/`stdout` JSON framing + user-scoped Named Pipe IPC to `NOVAIsland.App`.

### Explicitly Excluded Technologies
- **No WPF**
- **No Electron / Tauri / WebView desktop shell**
- **No Spotify Web API, Spotify OAuth, or Spotify API client secrets**

---

## 2. Two-Component System Architecture

```text
┌─────────────────────────────────────────────────────────────────────────┐
│ Chrome / Microsoft Edge (Manifest V3 Extension)                         │
│  ├── Content Scripts (content.js): MediaSession API + HTMLMediaElement  │
│  └── Service Worker (background.js): Tab arbitration, dedup, reconnect  │
└───────────────────────────────────┬─────────────────────────────────────┘
                                    │ Chrome/Edge Native Messaging
                                    │ (stdin/stdout: 4-byte uint32 LE + JSON)
                                    ▼
┌─────────────────────────────────────────────────────────────────────────┐
│ NOVAIsland.BrowserHost (C# Native Messaging Host Process)               │
│  ├── Validates JSON schema & message size                               │
│  └── Bridges bidirectionally over user-SID ACL Named Pipe               │
└───────────────────────────────────┬─────────────────────────────────────┘
                                    │ Named Pipe IPC (Local User Only)
                                    ▼
┌─────────────────────────────────────────────────────────────────────────┐
│ NOVAIsland.App (WinUI 3 + Windows App SDK 2.7.0)                        │
│  ├── DynamicIslandWindow (Borderless, Topmost, WS_EX_LAYERED/NOACTIVATE)│
│  ├── CompositionSpringAnimator (Windows.UI / Microsoft.UI.Composition)  │
│  ├── DashboardWindow & SettingsWindow (WinUI 3 NavigationView / Chrome) │
│  ├── FullscreenLyricsWindow                                             │
│  └── System Tray (H.NotifyIcon.WinUI) & Global Hotkeys                  │
├─────────────────────────────────────────────────────────────────────────┤
│ NOVAIsland.Services ──▶ NOVAIsland.Core ◀── NOVAIsland.Infrastructure   │
│  ├── BrowserMediaCoordinator       ├── Models & Enums  ├── SQLite DB    │
│  ├── LrclibLyricsService           ├── ViewModels      ├── Win32/WinRT  │
│  ├── Timer & Focus Orchestrator    └── Interfaces      ├── Clipboard    │
│  └── Settings & State Coordinators                     ├── Notifications│
│                                                        ├── WASAPI/Audio │
│                                                        └── Telemetry    │
└─────────────────────────────────────────────────────────────────────────┘
```

---

## 3. Solution & Project Structure

The native solution (`NOVAIsland.sln` at repository root) preserves the existing React prototype files untouched during migration:

```text
Notch-Island/
├── NOVAIsland.sln
├── DEVELOPMENT_STATUS.md                 # Live feature, test, and bug tracker
├── docs/
│   ├── architecture.md                   # This document
│   ├── implementation-roadmap.md         # 16-stage incremental roadmap
│   ├── feature-status.md                 # Feature & prototype parity matrix
│   └── api-integrations.md               # Native Messaging protocol, LRCLIB & Windows APIs
├── src/
│   ├── ... (existing React + Vite prototype preserved as visual reference)
│   ├── NOVAIsland.Core/
│   │   ├── Configuration/                # AppConstants.cs (DOOM Island / DOOM defaults)
│   │   ├── Enums/                        # IslandMode, ColorMode, ThemePreset, AnimationPreset, etc.
│   │   ├── Models/                       # BrowserMediaSession, Track, LyricLine, ClipboardItem,
│   │   │                                 # NotificationItem, ActivityItem, TimerState, SystemMetrics,
│   │   │                                 # AppSettings (incl. BrowserIntegrationSettings)
│   │   ├── Protocol/                     # Native Messaging DTOs & validation contracts
│   │   ├── Interfaces/                   # IBrowserMediaBridge, ILyricsService, IClipboardMonitor, etc.
│   │   ├── Services/                     # LrcParser, SensitiveContentDetector, TimeFormatter
│   │   └── ViewModels/                   # CommunityToolkit.Mvvm ObservableObject ViewModels
│   ├── NOVAIsland.Services/
│   │   ├── Media/                        # BrowserMediaCoordinator (tab prioritization, state merge)
│   │   ├── Lyrics/                       # LrclibLyricsService, LyricsSyncEngine, LocalLrcProvider
│   │   ├── Productivity/                 # TimerAndPomodoroService
│   │   ├── Clipboard/                    # ClipboardHistoryService
│   │   ├── Notifications/                # NotificationCoordinator
│   │   └── Settings/                     # SettingsCoordinator, ThemeCoordinator
│   ├── NOVAIsland.Infrastructure/
│   │   ├── Persistence/                  # SqliteDbContext, Migrations, Repositories
│   │   ├── Caching/                      # ArtworkCacheService, ClipboardImageStore, StorageFootprint
│   │   ├── Security/                     # DpapiProtector, NamedPipeSecurityHelper
│   │   ├── Logging/                      # Serilog structured local logging
│   │   └── Platform/                     # Win32Interop, AppWindowHelper, MultiMonitorPositioner,
│   │                                     # Win32ClipboardMonitor, WinRtNotificationService,
│   │                                     # WasapiEqualizerService, CoreAudioService, TelemetryService,
│   │                                     # BrightnessService, HotkeyService, StartupService, TrayService
│   ├── NOVAIsland.BrowserHost/
│   │   ├── Program.cs                    # Native Messaging stdin/stdout <-> Named Pipe bridge
│   │   ├── NativeMessagingFramer.cs      # 4-byte uint32 LE framing & size bounds enforcement
│   │   ├── PipeBridgeClient.cs           # Reconnecting Named Pipe client
│   │   └── Manifests/                    # com.doomisland.host.chrome.json, .edge.json, install scripts
│   ├── NOVAIsland.BrowserExtension/
│   │   ├── manifest.json                 # Manifest V3 (minimal permissions)
│   │   ├── background.js                 # Service worker: native port, reconnects, multi-tab tracking
│   │   ├── content.js                    # Generic MediaSession + HTMLMediaElement detector & controller
│   │   ├── adapters/                     # Optional lightweight adapters (YouTube, YT Music, Spotify Web)
│   │   └── popup/                        # Connection status, active media tab indicator, quick toggle
│   └── NOVAIsland.App/
│       ├── App.xaml / App.xaml.cs        # WinUI 3 Application, GenericHost DI, Single-instance mutex
│       ├── Themes/                       # Colors.xaml, DarkTheme.xaml, LightTheme.xaml, IslandStyles.xaml
│       ├── Animations/                   # CompositionSpringAnimator (Microsoft.UI.Composition)
│       ├── Controls/                     # AudioEqualizerControl, ProgressRingControl, SafeMediaImage,
│       │                                 # AppearanceLivePreviewControl, PositionPreviewCanvas
│       └── Views/
│           ├── Island/                   # DynamicIslandWindow.xaml + 7 panel UserControls
│           ├── Dashboard/                # DashboardWindow.xaml
│           ├── Settings/                 # SettingsWindow.xaml + 14 section UserControls
│           └── Lyrics/                   # FullscreenLyricsWindow.xaml
└── tests/
    └── NOVAIsland.Tests/                 # xUnit + NSubstitute tests (Core, Services, Infrastructure, Host)
```

---

## 4. Prototype-to-WinUI 3 Visual & Component Mapping

| Prototype Source | React Role | WinUI 3 Equivalent |
|---|---|---|
| `src/config/appConfig.ts` | Centralized app name/short name | `NOVAIsland.Core/Configuration/AppConstants.cs` (default `"DOOM Island"` / `"DOOM"`, editable in General Settings) |
| `src/types/island.ts` | Domain types & 14 settings sections | `NOVAIsland.Core/Models/*` (`spotify` section replaced by `BrowserIntegrationSettings`) |
| `src/index.css` | CSS tokens, scrollbars, sliders, idle shimmer | `NOVAIsland.App/Themes/*.xaml` + dynamic `AccentBrush` + Composition breathing animation |
| `src/components/ui/Controls.tsx` | `NovaSwitch`, `SettingRow`, `SettingSlider`, `SafeMediaImage` | Custom WinUI 3 `UserControl`s / `Style`s matching exact spacing, thumb size, and fallback visuals |
| `src/components/island/DynamicIsland.tsx` | Pure-black morphing pill (9 states), hover scale, bounce, auto-collapse | `DynamicIslandWindow.xaml` (`WS_EX_LAYERED`, `OverlappedPresenter.SetBorderAndTitleBar(false, false)`, `TransparentTintBackdrop` / Composition visual bounds) |
| `src/components/island/AudioEqualizer.tsx` | Animated 5/8-bar frequency bars | `AudioEqualizerControl` using Composition `Scale.Y` animations driven by WASAPI loopback FFT (or idle/BPM fallback) |
| `src/components/island/MusicPanel.tsx` | Artwork, source site, seekbar, transport, volume, queue/tabs | `MusicPanelView.xaml` bound to `BrowserMediaCoordinator` (displays originating website & active tab, executes Play/Pause/Prev/Next/Seek via Native Messaging) |
| `src/components/island/LyricsPanel.tsx` | Synced lyrics stream, blur inactive, manual LRCLIB search | `LyricsPanelView.xaml` + `LrclibLyricsService` |
| `src/components/island/ClipboardPanel.tsx` | Search, category pills, copy/pin/delete | `ClipboardPanelView.xaml` + `Win32ClipboardMonitor` + SQLite |
| `src/components/island/NotificationsPanel.tsx` | Active/History tabs, dismiss, clear, DND respect | `NotificationsPanelView.xaml` + `WinRtNotificationService` |
| `src/components/island/TimerPanel.tsx` | Pomodoro, Countdown, Stopwatch, progress ring, laps | `TimerPanelView.xaml` + Win2D / XAML `Path` Arc ring |
| `src/components/island/SystemPanel.tsx` | Wi-Fi, Battery, Mic, Camera, Volume, Brightness, Audio Outputs, CPU, RAM | `SystemPanelView.xaml` + real `NOVAIsland.Infrastructure.Platform` providers |
| `src/components/island/ActivityPanel.tsx` | Supported downloads, transfers, screenshots | `ActivityPanelView.xaml` (real supported watchers only) |
| `src/components/dashboard/DashboardWindow.tsx` | Desktop Control Hub | `DashboardWindow.xaml` (`ExtendsContentIntoTitleBar = true`, custom titlebar, `MicaBackdrop` / dark surface) |
| `src/components/settings/SettingsWindow.tsx` + `SettingsSections.tsx` | Searchable sidebar + 14 sections | `SettingsWindow.xaml` + 14 section `UserControl`s (replacing `Spotify` section with **Browser Integration** section) |
| `src/components/lyrics/FullscreenLyrics.tsx` | Blurred artwork stage + synced lyrics + LRCLIB drawer | `FullscreenLyricsWindow.xaml` |
| `src/components/desktop/Taskbar.tsx` | Simulated taskbar | Real Windows System Tray (`H.NotifyIcon.WinUI`) |

---

## 5. Key WinUI 3 Technical Decisions

### Decision 1: Borderless Transparent Floating Dynamic Island in WinUI 3
Unlike WPF, WinUI 3 `Window` does not offer an `AllowsTransparency` property directly. To achieve a true rounded, pure-black floating pill with subtle shadow/glow and transparent corners over the desktop:
- Use `WinUIEx` (`TransparentTintBackdrop` or Win32 `WS_EX_LAYERED` + `ICompositionSupportsSystemBackdrop` nulling) with `OverlappedPresenter.SetBorderAndTitleBar(false, false)`, `IsShownInSwitchers = false`, `IsAlwaysOnTop = true`, and `WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE` so clicking/hovering the island does not steal focus from active apps (focus is requested explicitly only when typing in search boxes).
- Apply `DWMWA_WINDOW_CORNER_PREFERENCE = DWMWCP_DONOTROUND` (Win11) so DWM does not draw an unwanted rectangular border around the transparent margin, and render the pill geometry + `ThemeShadow` / Composition `DropShadow` inside the XAML root.
- **Click-Through Outside Pill Bounds:** Subclass window `WndProc` for `WM_NCHITTEST` (or use `InputNonClientPointerSource` pass-through regions) so transparent shadow margins around the compact pill pass mouse clicks directly to underlying windows.

### Decision 2: Windows Composition Physics (`Microsoft.UI.Composition`)
- Instead of storyboarded layout resizing (which triggers costly XAML measure/arrange passes on every frame), animate the island pill's `Visual.Size`, `Visual.Offset`, `Visual.Scale`, and inner panel `Opacity` / `Translation` using `Compositor.CreateSpringVector3Animation()` / `SpringScalarNaturalMotionAnimation` (`DampingRatio`, `Period` mapped from `Fluid`, `Snappy`, `Silk`, `Instant` presets and `AnimationSpeed` multiplier).

### Decision 3: Browser Media Detection Over Native Messaging (Zero Spotify APIs)
- All media metadata, album artwork, playback state, and transport controls flow from **Chrome/Edge Manifest V3** tabs -> **`NOVAIsland.BrowserHost`** -> **Named Pipe** -> **`NOVAIsland.App`**.
- The extension relies primarily on the standard **Web Media Session API** (`navigator.mediaSession`) and **HTMLMediaElement** (`<audio>`, `<video>`) DOM observation, with lightweight non-invasive adapters only where generic detection lacks metadata or action handlers—never bypassing DRM, auth, or paywalls.

### Decision 4: Security Boundaries
- `NOVAIsland.BrowserHost` only allows registered Chrome/Edge extension IDs in its Native Messaging host manifest (`allowed_origins`).
- Named Pipe between `BrowserHost` and `App` enforces a `PipeSecurity` ACL restricting access strictly to the **current Windows user SID** (`WindowsIdentity.GetCurrent().User`), rejecting connections from other users/services.
- All incoming JSON messages are schema-validated and size-bounded (max 1 MB per Native Messaging spec) before reaching the UI thread.
- Clipboard data, browsing tab metadata, and notification history never leave the local machine.
