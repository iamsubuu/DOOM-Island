# DOOM Island — 16-Stage Incremental Implementation Roadmap

> **Operating Rule:** Implement strictly stage-by-stage. Inspect existing files before modifying, write real working integrations (zero fake data or placeholder buttons), run tests after each stage, and update [DEVELOPMENT_STATUS.md](../DEVELOPMENT_STATUS.md) before advancing.

---

## Stage 1 & 2 — Prototype Inspection & Design Extraction *(Completed)*
- Audited all 31 files in the imported Google AI Studio React prototype (`src/`).
- Cataloged all 9 Dynamic Island states, Fullscreen Lyrics stage, Dashboard Hub, 14 Settings sections, typography, colors, spacing, and motion presets.

## Stage 3 — WinUI 3 Solution & Modular Architecture
- **Scope:** Verify `.NET 10` / `WindowsAppSDK 2.7.0` build tooling on host; create `NOVAIsland.sln`, `Directory.Build.props`, `Directory.Packages.props`, and all 7 modules (`NOVAIsland.Core`, `NOVAIsland.Services`, `NOVAIsland.Infrastructure`, `NOVAIsland.BrowserHost`, `NOVAIsland.BrowserExtension`, `NOVAIsland.App`, `NOVAIsland.Tests`).
- **Exit Gate:** `dotnet build NOVAIsland.sln` and `dotnet test` pass with 0 errors; empty WinUI 3 window launches cleanly on Windows 10/11.

## Stage 4 — Faithful WinUI 3 Visual Design System & Core Views
- **Scope:** Port `AppConstants` (configurable `"DOOM Island"` / `"DOOM"` branding), bundled fonts (`Plus Jakarta Sans`, `Manrope`, `Outfit`, `JetBrains Mono`), `DarkTheme.xaml` / `LightTheme.xaml` / `IslandStyles.xaml`, Lucide-faithful vector `IconGeometry` dictionary, custom `NovaToggleSwitch`, `SettingRow`, `SettingSlider`, `SafeMediaImage`, `ProgressRingControl`, and initial XAML layouts for the Dynamic Island pill & panels.
- **Exit Gate:** Visual inspection confirms faithful dark/light surfaces, pure-black `#000000` island styling, and live accent color propagation.

## Stage 5 — Floating Dynamic Island Window & Composition Animations
- **Scope:** Implement `DynamicIslandWindow` (`OverlappedPresenter.SetBorderAndTitleBar(false, false)`, `WS_EX_LAYERED`, `WS_EX_TOOLWINDOW`, `WS_EX_NOACTIVATE`, `WM_NCHITTEST` click-through margins), multi-monitor & DPI-aware positioning (`TopLeft`, `TopCenter`, `TopRight`, `Custom X/Y`, drag-to-reposition), hover/click/double-click/outside-click/mouse-leave/auto-collapse behaviors, and `Microsoft.UI.Composition` spring morphing (`Fluid`, `Snappy`, `Silk`, `Instant`) + idle breathing & arrival bounce.
- **Exit Gate:** Island morphs smoothly at 60/120fps across monitors without stealing focus or blocking underlying clicks.

## Stage 6 — Manifest V3 Chrome/Edge Browser Extension (`NOVAIsland.BrowserExtension`)
- **Scope:** Implement `manifest.json` (MV3), `content.js` (`navigator.mediaSession` + `HTMLMediaElement` detection, deduplication, play/pause/next/prev/seek handlers), `background.js` (multi-tab audible/active arbitration, port lifecycle, reconnects), optional site adapters (YouTube, YouTube Music, Spotify Web Player) where generic APIs fall short without bypassing DRM/auth, and `popup/` status UI.
- **Exit Gate:** Extension loads unpacked in Chrome & Edge, detects media across multiple tabs, and passes console/unit verification.

## Stage 7 — Native Messaging Host & IPC Bridge (`NOVAIsland.BrowserHost`)
- **Scope:** Implement C# `NOVAIsland.BrowserHost` (`stdin`/`stdout` 4-byte uint32 LE JSON framer, schema validation, user-SID ACL Named Pipe client/server, reconnects), Chrome & Edge host manifests + HKCU registration scripts, and `INativeMediaBridge` host listener in `NOVAIsland.App`.
- **Exit Gate:** Automated tests for framing/validation/reconnects pass; live Chrome/Edge extension exchanges round-trip messages with `NOVAIsland.App`.

## Stage 8 — Browser Media Player Integration
- **Scope:** Wire `BrowserMediaCoordinator` -> `MusicViewModel` -> `MusicPanelView` & `Compact` / `Music` pill states. Display real album artwork, title, artist, album, progress/duration, playback state, and originating website/tab switcher; dispatch real `play`/`pause`/`next`/`previous`/`seek` commands and handle `command_result` errors honestly. Wire `NAudio` WASAPI loopback FFT to `AudioEqualizerControl`.
- **Exit Gate:** Controlling YouTube, YouTube Music, and Spotify Web Player from DOOM Island updates in real time and handles unsupported actions gracefully.

## Stage 9 — Independent Synchronized Lyrics System (LRCLIB)
- **Scope:** Implement `LrclibHttpClient`, `LrcParser`, local `.lrc` fallback, SQLite `lyrics_cache`, `LyricsSyncEngine` synced to browser playback position, `LyricsPanelView` (auto-scroll, click-to-seek, blur inactive lines, manual search), and `FullscreenLyricsWindow` (blurred artwork backdrop, transport, slide-over LRCLIB drawer, `Esc` exit).
- **Exit Gate:** Unit tests verify LRC parsing & LRCLIB API client; live tracks auto-load, cache, and sync lyrics accurately.

## Stage 10 — Clipboard, Notifications & System Widgets
- **Scope:** Implement SQLite repositories; `Win32ClipboardMonitor` (`WM_CLIPBOARDUPDATE`, text/link/code/image, sensitive-format & excluded-app filtering, private-browsing pause, copy/pin/delete/retention); `WinRtNotificationService` (with MSIX identity check, DND/Focus Assist sync, history, dismiss); `SystemWidgets` (Battery, CoreAudio Volume & Output Device switcher, WMI/DDC-CI Brightness where supported, Wi-Fi, CPU/RAM, Mic/Camera privacy indicators); and supported `ActivityPanel` watchers (screenshots + optional extension downloads).
- **Exit Gate:** Persistence tests & live hardware/OS integration tests pass; unsupported capabilities report status honestly.

## Stage 11 — Productivity Tools (Timer, Stopwatch, Pomodoro, Focus)
- **Scope:** Implement `TimerAndPomodoroService` + `TimerPanelView` with accurate background timing, presets, lap splits, Focus DND linkage, completion sound/toast, and compact pill countdown badge.
- **Exit Gate:** Unit tests verify timer/pomodoro transitions and stopwatch precision; UI progress ring animates smoothly.

## Stage 12 — Dashboard Hub & Comprehensive 14-Section Settings
- **Scope:** Build `DashboardWindow` and `SettingsWindow` (searchable sidebar + all 14 sections: `General`, `Appearance` with live preview, `Position & Display` with interactive multi-monitor canvas, `Animation`, `Music`, `Browser Integration`, `Lyrics`, `Clipboard`, `Notifications`, `Widgets`, `Shortcuts`, `Privacy`, `Storage` with real SQLite/disk MB & JSON export/reset, `About`). Zero non-functional toggles or fake data.
- **Exit Gate:** Every setting persists in SQLite and updates the live island/services immediately.

## Stage 13 — Startup, System Tray & Global Shortcuts
- **Scope:** Implement `H.NotifyIcon.WinUI` tray icon & menu, `RegisterHotKey` global shortcuts + live keybinding recorder, Windows Startup registration (`HKCU\...\Run` / `StartupTask`), single-instance activation, and `SessionSwitch` lock-screen masking.
- **Exit Gate:** App minimizes/closes to tray cleanly, launches on sign-in when enabled, and responds globally to configured hotkeys.

## Stage 14 — Security & Privacy Review
- **Scope:** Audit Named Pipe ACLs, Native Messaging `allowed_origins`, schema validation/fuzzing, DPAPI usage, `LocalOnlyMode` network cutoff, clipboard sensitive/private-browsing exclusions, and extension permissions.
- **Exit Gate:** Security checklist signed off with zero critical/high findings.

## Stage 15 — End-to-End Integration Testing & Bug Stabilization
- **Scope:** Execute full `NOVAIsland.Tests` suite + multi-browser (Chrome & Edge) + multi-monitor DPI (100%/150%/200%) + idle CPU/memory profiling + bug fixes logged in `DEVELOPMENT_STATUS.md`.
- **Exit Gate:** All automated tests pass; all critical/major bugs resolved.

## Stage 16 — Windows Installer & Extension Packaging
- **Scope:** Package self-contained / framework-dependent Windows installer (MSIX / Sparse Package for `UserNotificationListener` support + Inno Setup/WiX bootstrapper registering `NOVAIsland.BrowserHost` for Chrome/Edge) + packaged `.zip` / store-ready Manifest V3 extension. Verify clean install, upgrade, and uninstall on a clean Windows environment.
- **Exit Gate:** Verified Windows installer + verified browser extension pairing.
