# DOOM Island — Development Status

> **Last Updated:** 2026-11-26  
> **Version:** `2.3.1` (`DOOMIsland.exe`)  
> **Current Phase:** All Stages (1–16) Implemented, Polished & Verified  
> **Policy:** A feature is listed under **Completed Features** ONLY when its native WinUI 3 / C# / Extension implementation is genuinely implemented, integrated, and verified.

---

## 1. Completed Features

- **Solution & Architecture (`Stage 1–3`):** `NOVAIsland.sln` with `NOVAIsland.Core`, `NOVAIsland.Services`, `NOVAIsland.Infrastructure`, `NOVAIsland.BrowserHost`, `NOVAIsland.BrowserExtension`, `NOVAIsland.App`, and `NOVAIsland.Tests` targeting `.NET 10` + `Microsoft.WindowsAppSDK 2.7.0`.
- **WinUI 3 Design System & Bundled Typography (`Stage 4`):** Pure-black `#000000` island material, Dark/Light/System windows, dynamic `NovaAccentBrush`, bundled `Plus Jakarta Sans`, `Manrope`, `Outfit`, `JetBrains Mono` fonts, `AudioEqualizerControl`, `ProgressRingControl`, and `SafeMediaImage`.
- **Borderless Floating Dynamic Island & Composition Animations (`Stage 5`):** `DynamicIslandWindow` (`DOOMIsland.exe`) using `TransparentTintBackdrop`, `WS_EX_LAYERED | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE`, `OverlappedPresenter.SetBorderAndTitleBar(false, false)`, `WM_NCHITTEST` pass-through outside pill bounds, stealth invisible top-edge hover bar (`StealthTopBar` + `HoverToOpen`), drag-to-reposition, multi-monitor Per-Monitor V2 DPI positioning, `Microsoft.UI.Composition` spring physics (`Fluid`, `Snappy`, `Silk`, `Instant`), idle breathing, and notification arrival bounce.
- **Chrome/Edge Manifest V3 Extension & Native Messaging Host (`Stage 6–7`):** `NOVAIsland.BrowserExtension` (`manifest.json`, `content.js` using `MediaSession` + `HTMLMediaElement`, `background.js` multi-tab arbitration & deduplication, `popup/`) + `NOVAIsland.BrowserHost` (4-byte uint32 LE JSON framing + user-SID ACL Named Pipe bridge) + `Register-NativeMessagingHost.ps1`. Verified end-to-end with live process + Named Pipe integration test.
- **Browser-Driven Music Player & WASAPI Equalizer (`Stage 8`):** `MusicPanelView` + `Music` compact pill + `CompositeBrowserMediaBridge` (automatic zero-config Windows SMTC detection & control for **Google Chrome, Microsoft Edge, and Opera** + `BrowserMediaBridgeService` Native Messaging extension priority) + `WasapiAudioVisualizerService` (`NAudio` loopback FFT). Zero Spotify Web API/OAuth usage.
- **Independent LRCLIB Lyrics & Fullscreen Stage (`Stage 9`):** `LrclibLyricsService` (`/api/get`, `/api/search`), `LrcParser`, SQLite `lyrics_cache`, `LyricsPanelView`, and `FullscreenLyricsWindow` with ambient artwork backdrop and slide-over manual search drawer.
- **Clipboard, System Widgets, Activity & Productivity (`Stage 10–11`):**
  - `Win32ClipboardMonitor` (`WM_CLIPBOARDUPDATE`, text/link/code/image, retention, pin/copy/delete, password-manager format & excluded-app privacy, InPrivate/Incognito pause) backed by `SqliteClipboardRepository`.
  - `WindowsSystemMonitorService` (Battery, CoreAudio Volume & Output Device switcher, WMI/DDC-CI Brightness where supported, Wi-Fi SSID/signal, CPU/RAM, Mic/Camera privacy indicators).
  - `WindowsActivityMonitorService` (native quick screenshot capture, `Pictures\Screenshots` watcher, `Downloads` partial download watcher).
  - `TimerViewModel` & `TimerPanelView` (Pomodoro, Countdown, Stopwatch with laps, progress ring, background 100ms/1s ticks).
- **Dashboard Hub & 14-Section Settings (`Stage 12`):** `DashboardWindow` and responsive searchable `SettingsWindow` (adapts between wide sidebar and compact dropdown layout with uniform control alignment) covering all 14 sections (`General` through `About`), live Appearance & Position previews, real SQLite/disk MB accounting, JSON export, and factory reset.
- **Tray, Startup, Global Hotkeys & Lock Masking (`Stage 13`):** `H.NotifyIcon.WinUI` tray icon + menu, `StartupRegistrationService`, `GlobalHotkeyService` (`RegisterHotKey`), and `SessionLockWatcher` (`WTSRegisterSessionNotification`).
- **Security Audit & Packaging (`Stage 14–16`):** Security review documented in `docs/security-review.md`; Authenticode-signed standalone native Windows installer (`DOOMIsland-Setup-x64-v2.3.1.exe`), Release self-contained `win-x64` artifacts (`DOOMIsland-Win64-Portable-v2.3.1.zip`, `DOOMIsland-BrowserExtension-MV3.zip`), `Install-DOOMIsland.ps1`, `Uninstall-DOOMIsland.ps1`, `Sign-Artifacts.ps1`, and `DOOMIsland.iss` created and verified end-to-end.

---

## 2. Partially Implemented / OS-Limited Features

- **Windows Notifications (`WinRtNotificationListenerService`):** Fully implemented against `UserNotificationListener` with Focus Assist check and dismiss support; however, Windows requires **MSIX / Sparse Package Identity** (`userNotificationListener` capability) and user consent in Windows Settings to read third-party toast alerts. Unpackaged builds accurately display this status in `NotificationsPanel` and Settings.
- **Optional MV3 Extension Multi-Tab Controls:** Chrome, Edge, and Opera media playback/artwork/timeline/controls connect **automatically** via Windows SMTC (`CompositeBrowserMediaBridge`) immediately after installing `DOOMIsland-Setup-x64-v1.0.0.exe`. If you also want per-tab switching / Focus Tab across multiple tabs in the same browser, load `BrowserExtension` unpacked in `chrome://extensions` / `edge://extensions` / `opera://extensions` (its pinned key yields `amplhmapoeandecddkchambamhafdcgp`, which the installer pre-registers in `com.doomisland.host.json`).

---

## 3. Not Yet Implemented

*None from the core roadmap.*

---

## 4. Known Bugs & Environment Findings

| ID | Severity | Description | Status |
|---|---|---|---|
| ENV-01 | `Mitigated` | Host has `.NET SDK 11.0.100` (`C:\Program Files\dotnet\sdk\11.0.100`) without a `10.0.x` runtime; `<RollForward>LatestMajor</RollForward>` in `Directory.Build.props` enables seamless execution on `.NET 11.0.0` and Release builds publish self-contained `win-x64`. | `Mitigated` |
| ENV-02 | `Info` | Host runs Windows 10 Pro 22H2 (Build `19045`); Win11-only DWM attributes are guarded (`>= 22000`). | `Verified` |
| ENV-03 | `Info` | `UserNotificationListener` requires MSIX / Sparse Package Identity (`userNotificationListener` capability). | `Tracked` |

---

## 5. Tests Passed & Failed

| Suite / Check | Passed | Failed | Skipped | Notes |
|---|---|---|---|---|
| `NOVAIsland.Tests` (Core, SQLite, LRCLIB, Framer, Bridge E2E) | 15 | 0 | 0 | `dotnet test` (`net10.0-windows10.0.19041.0`) — 2 s |
| `DOOMIsland.exe` Debug & Release Self-Contained Launch | 2 | 0 | 0 | Clean launch + `doom_island.db` WAL initialization verified |
| `NOVAIsland.BrowserExtension` JS Syntax (`node --check`) | 3 | 0 | 0 | `background.js`, `content.js`, `popup.js` |
| `DOOMIsland-Setup-x64-v2.3.1.exe` / `Uninstall-DOOMIsland.ps1` E2E | 1 | 0 | 0 | Signed silent EXE install, Chrome/Opera/Edge Native Messaging registration, `BrowserHost --self-check`, `DOOMIsland.exe` launch, clean uninstall |
