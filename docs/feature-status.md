# DOOM Island — Feature Parity & Integration Matrix

> **Last Updated:** 2026-11-26  
> **Rule:** Never mark a feature `Completed` merely because its XAML exists. Status reflects actual native implementation + verification on Windows 10 Build `19045`. For test counts and environment caveats, see [DEVELOPMENT_STATUS.md](../DEVELOPMENT_STATUS.md).

---

## 1. Platform & Shell Foundation

| ID | Feature | Prototype (React) | Native Target (WinUI 3) | Status |
|---|---|---|---|---|
| CORE-01 | Solution (`NOVAIsland.*`), DI (`GenericHost`), Configurable Branding (`DOOM Island`) | `appConfig.ts` | `NOVAIsland.Core`, `NOVAIsland.App` | `Completed` |
| CORE-02 | SQLite local database, schema migrations, real disk cache accounting | `localStorage` | `NOVAIsland.Infrastructure` | `Completed` |
| CORE-03 | Structured local diagnostic logging (`Serilog`, privacy-gated) | None | `NOVAIsland.Infrastructure` | `Completed` |
| UI-01 | WinUI 3 Design System (Dark/Light/System theme, live accent color, bundled fonts) | `index.css` | `NOVAIsland.App/Themes/` | `Completed` |
| UI-02 | Borderless floating Dynamic Island window (`WS_EX_LAYERED`, `NOACTIVATE`, `WM_NCHITTEST` click-through) | `DynamicIsland.tsx` | `DynamicIslandWindow.xaml` | `Completed` |
| UI-03 | Windows Composition spring animations (`Fluid`, `Snappy`, `Silk`, `Instant`) | `motion/react` | `CompositionSpringAnimator` | `Completed` |
| UI-04 | Multi-monitor positioning (`TopLeft`, `TopCenter`, `TopRight`, `Custom X/Y`, Drag, DPI) | `PositionPreview.tsx` | `MultiMonitorPositioner` | `Completed` |
| OS-01 | Windows System Tray (`H.NotifyIcon.WinUI`), Startup, Global Hotkeys, Lock Masking | `Taskbar.tsx` (sim) | `NOVAIsland.Infrastructure`, `TrayHostService` | `Completed` |

## 2. Browser Extension & Media Player (No Spotify APIs)

| ID | Feature | Prototype (React) | Native Target | Status |
|---|---|---|---|---|
| EXT-01 | Chrome/Edge/Opera Manifest V3 Extension (`MediaSession` + `HTMLMediaElement` + multi-tab) | None | `NOVAIsland.BrowserExtension` | `Completed (JS syntax & pinned key ID amplhmapoeandecddkchambamhafdcgp verified; auto-registered by installer)` |
| EXT-02 | Native Messaging Host (`stdin`/`stdout` JSON + user-ACL Named Pipe + reconnects) | None | `NOVAIsland.BrowserHost` | `Completed (E2E process + pipe test passed)` |
| MUS-01 | Compact & Expanded Music Player fed by automatic Chrome/Edge/Opera SMTC + MV3 Extension | Mock tracks | `MusicPanelView` + `CompositeBrowserMediaBridge` | `Completed` |
| MUS-02 | Real browser playback controls (Play/Pause, Next, Prev, Seek, Focus Tab/Window) | Mock state | `CompositeBrowserMediaBridge` (SMTC + Extension) | `Completed` |
| MUS-03 | Audio-reactive Equalizer (`AudioEqualizer`) | Mock BPM math | `NAudio` WASAPI loopback FFT | `Completed` |

## 3. Synchronized Lyrics (LRCLIB)

| ID | Feature | Prototype (React) | Native Target | Status |
|---|---|---|---|---|
| LYR-01 | Automatic LRCLIB lookup, LRC parser, plain-text fallback, SQLite cache | Mock `LRCLIB_CATALOG` | `LrclibLyricsService` | `Completed` |
| LYR-02 | Island `LyricsPanel` (synced highlight, smooth scroll, click-to-seek, manual search) | `LyricsPanel.tsx` | `LyricsPanelView.xaml` | `Completed` |
| LYR-03 | `FullscreenLyricsWindow` (blurred artwork stage, transport, slide-over LRCLIB search) | `FullscreenLyrics.tsx` | `FullscreenLyricsWindow.xaml` | `Completed` |

## 4. Productivity, Clipboard, Notifications, System & Activity

| ID | Feature | Prototype (React) | Native Target | Status |
|---|---|---|---|---|
| TMR-01 | Timer, Stopwatch (laps), Pomodoro & Focus sessions + progress ring | `TimerPanel.tsx` | `TimerViewModel` + `TimerPanelView` | `Completed` |
| CLP-01 | Clipboard history (Text, URL, Code, Image), search, pin, copy, delete, retention, exclusions | `ClipboardPanel.tsx` | `Win32ClipboardMonitor` + SQLite | `Completed` |
| NOT-01 | Windows Notifications listener, previews, history, dismiss, DND/Focus Assist sync | `NotificationsPanel.tsx` | `WinRtNotificationListenerService` | `Partial (OS-Limited: requires MSIX/Sparse Package Identity for UserNotificationListener; reports status honestly in unpackaged builds)` |
| SYS-01 | Battery, CoreAudio Volume/Output Switcher, WMI/DDC-CI Brightness, Wi-Fi, CPU/RAM, Mic/Cam | `SystemPanel.tsx` | `WindowsSystemMonitorService` | `Completed` |
| ACT-01 | Supported Downloads (`Downloads` partial watcher) & Screenshots (`Pictures\Screenshots` / Snip) | `ActivityPanel.tsx` | `WindowsActivityMonitorService` + `ActivityPanelView` | `Completed` |

## 5. Desktop Hub, 14-Section Settings & Packaging

| ID | Feature | Prototype (React) | Native Target | Status |
|---|---|---|---|---|
| DSK-01 | `DashboardWindow` (State bar, Now Playing, Quick Toggles, Overview cards, Recent feeds) | `DashboardWindow.tsx` | `DashboardWindow.xaml` | `Completed` |
| SET-01 | `SettingsWindow` (Search + 14 sections incl. `Browser Integration`, live previews, export/reset) | `SettingsWindow.tsx` | `SettingsWindow.xaml` | `Completed` |
| PKG-01 | Security/Privacy audit, End-to-End tests, Standalone GUI `.exe` Setup Installer & Release Bundles | None | `installer/`, `src/NOVAIsland.Setup/`, `docs/security-review.md` | `Completed (DOOMIsland-Setup-x64-v1.0.0.exe + Portable ZIP + PS1 Installer verified E2E)` |
