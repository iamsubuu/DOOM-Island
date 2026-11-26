# DOOM Island — Native Messaging Protocol, LRCLIB & Windows Integration Specs

---

## 1. Browser-to-Windows Native Messaging Architecture

### 1.1 Transport Pipeline
1. **Browser Extension (`NOVAIsland.BrowserExtension`, Manifest V3)** connects via `chrome.runtime.connectNative("com.doomisland.host")`.
2. **Chrome / Microsoft Edge** spawns `NOVAIsland.BrowserHost.exe` and communicates over `stdin` / `stdout` using standard Native Messaging framing:
   - `[4-byte uint32 little-endian payload length][UTF-8 JSON payload]` (max inbound message enforced below 1 MB; outbound max 1 MB).
3. **`NOVAIsland.BrowserHost.exe`** bridges messages asynchronously over a **user-SID ACL Named Pipe** (`\\.\pipe\DOOMIsland.BrowserBridge.<UserSidHash>`) to `NOVAIsland.App`.
   - If `NOVAIsland.App` is not yet running or restarts, `BrowserHost` queues heartbeats/reconnects with exponential backoff without crashing the native port.
   - If the browser restarts or the MV3 service worker wakes up from idle suspension, `background.js` re-establishes the native port and emits `browser_connected` + a fresh active-tab state snapshot.

### 1.2 Host Registration (Chrome & Microsoft Edge)
Two host manifest JSON files are deployed alongside `NOVAIsland.BrowserHost.exe`:
- **Chrome:** `HKCU\Software\Google\Chrome\NativeMessagingHosts\com.doomisland.host` -> `...\com.doomisland.host.chrome.json`
- **Edge:** `HKCU\Software\Microsoft\Edge\NativeMessagingHosts\com.doomisland.host` -> `...\com.doomisland.host.edge.json`

```json
{
  "name": "com.doomisland.host",
  "description": "DOOM Island Native Messaging Host for browser media detection and control",
  "path": "C:\\Path\\To\\NOVAIsland.BrowserHost.exe",
  "type": "stdio",
  "allowed_origins": [
    "chrome-extension://<PUBLISHED_OR_DEV_EXTENSION_ID>/"
  ]
}
```
*(Installer / registration script supports both Chrome Web Store / Edge Add-ons IDs and local unpacked dev IDs.)*

---

## 2. Native Messaging JSON Protocol Schema

Every message shares a common envelope:
```json
{
  "version": 1,
  "type": "media_metadata_updated",
  "messageId": "uuid-v4",
  "timestampUtc": "2026-11-26T05:30:00.000Z",
  "browser": "edge",
  "tabId": 142,
  "windowId": 1,
  "payload": { }
}
```

### 2.1 Extension -> Windows App Messages

| `type` | Trigger | Required `payload` Fields |
|---|---|---|
| `browser_connected` | Native port opened / SW restarted | `extensionVersion`, `browserName`, `userAgent` |
| `browser_disconnected` | Port closing / browser shutting down | `reason` |
| `media_detected` | First playback or media element detected in tab | `origin` (`youtube.com`), `urlHost`, `tabTitle`, `title`, `artist`, `album`, `artworkUrl`, `durationSeconds`, `positionSeconds`, `playbackState` (`playing` \| `paused`), `capabilities` (`canPlayPause`, `canNext`, `canPrevious`, `canSeek`) |
| `media_metadata_updated` | Title/artist/album/artwork changed (deduplicated!) | Same metadata fields; only sent when hash of metadata changes |
| `playback_state_changed` | Play <-> Pause transition | `playbackState` (`playing` \| `paused` \| `none`), `positionSeconds`, `playbackRate` |
| `playback_position_updated` | Periodic/seek position sync (throttled ~1s while playing or on seek) | `positionSeconds`, `durationSeconds`, `playbackRate` |
| `active_tab_changed` | Browser active media tab switched or audible tab arbitration changed | `tabId`, `origin`, `isAudible`, `isActiveBrowserTab` |
| `media_stopped` | Tab closed, navigated away, or media ended/unloaded | `tabId`, `reason` (`tab_closed` \| `ended` \| `navigated` \| `stopped`) |

### 2.2 Windows App -> Extension Messages

| `type` | Trigger | `payload` Fields |
|---|---|---|
| `media_command` | User clicks Play/Pause/Prev/Next/Seek or focuses source tab in DOOM Island | `commandId`, `targetTabId`, `action` (`play` \| `pause` \| `toggle` \| `next` \| `previous` \| `seek` \| `focus_tab`), `seekPositionSeconds` (for `seek`) |
| `command_result` *(Ext -> App)* | Extension reports real execution outcome | `commandId`, `success` (`bool`), `reason` (`unsupported` \| `tab_not_found` \| `ok`) |

### 2.3 Multi-Tab Arbitration & Deduplication Rules
- **Multi-Tab Priority:**
  1. Currently audible (`tab.audible === true`) + active/most recently started playing tab.
  2. Most recently interacted paused media tab (if no tab is actively playing).
  3. Closing or stopping the selected tab automatically promotes the next active media tab, or transitions to `media_stopped` if none remain.
- **Deduplication:** `content.js` and `background.js` maintain a per-tab snapshot hash (`title|artist|album|artworkUrl|duration`) and only emit `media_metadata_updated` when values actually change. Position updates are throttled (e.g., 1000ms interval or immediate on `seeked`/`ratechange`/`play`/`pause`).
- **Command Honesty:** If a site does not register a `MediaSession` action handler (e.g., `nexttrack`/`previoustrack`) and no valid fallback exists, `content.js` returns `command_result { success: false, reason: "unsupported" }` and DOOM Island notifies/disables unsupported buttons cleanly rather than pretending the command succeeded.

### 2.4 Manifest V3 Permissions (Least Privilege)
- `tabs` — Read tab title/audible/active status and focus target tab
- `scripting` — Inject/coordinate media bridge cleanly when needed
- `nativeMessaging` — Communicate with `com.doomisland.host`
- `storage` — Persist extension toggle / debug preferences locally
- Host permissions: scoped as minimally as practical for content script media detection, with clear user toggle in **Settings > Browser Integration** and extension popup.

---

## 3. LRCLIB Synchronized Lyrics Integration

- **API Base:** `https://lrclib.net/api` (public HTTPS, no API key or Spotify dependency).
- **Automatic Lookup:** Triggered on `media_detected` / `media_metadata_updated` using normalized `(title, artist, album, durationSeconds)` via `GET /api/get` (fallback to `GET /api/search` if `/api/get` returns 404).
- **LRC Parser:** Parses `[mm:ss.xx]` and `[mm:ss.xxx]` timestamps, sorts chronologically, handles multi-timestamp chorus lines, strips metadata tags (`[ar:]`, `[ti:]`, `[al:]`, `[offset:]`, applying `[offset:±ms]`), and falls back to `plainLyrics` when `syncedLyrics` is empty.
- **Synchronization:** Uses high-precision local interpolation anchored to `playback_position_updated` + `playbackRate` while `playbackState === "playing"`, re-syncing on every seek or state transition.
- **Local Cache:** Stored in SQLite `lyrics_cache` table; respects `PrivacySettings.LocalOnlyMode` (disables outbound LRCLIB calls when local-only mode is enabled, serving cached/local `.lrc` only).

---

## 4. Windows Platform Integrations & Limitations

### 4.1 Floating Window & Composition (`WinUI 3` + `Win32`)
- **Transparency & Borderless Frame:** Uses `WinUIEx` / `WS_EX_LAYERED` + `OverlappedPresenter.SetBorderAndTitleBar(false, false)` + `WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE`.
- **Win10 (19045) vs Win11 (22000+):** `DWMWA_WINDOW_CORNER_PREFERENCE` and `MicaBackdrop` are Win11+ only; guarded via `OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000)`. The floating pill renders its rounded geometry via XAML `Border` + Composition clip/shadow so visual styling is identical on Windows 10 22H2 and Windows 11.
- **Backdrop Blur:** Win32 Acrylic behind a custom-rounded transparent window on Win10 can lag during resize/drag; Composition / `TransparentTintBackdrop` with pure-black (`#000000`) high-opacity material guarantees 60/120fps smooth morphing.

### 4.2 Windows Notifications (`UserNotificationListener`)
- **Package Identity Caveat:** `Windows.UI.Notifications.Management.UserNotificationListener` requires **MSIX / Sparse Package Identity** (`<uap3:Capability Name="userNotificationListener"/>`) + user approval in *Windows Settings > Privacy > Notifications*.
- **Action Scope:** Supports reading toast text, app identity, timestamp, and dismissing (`RemoveNotification`). Cannot programmatically submit inline reply text boxes on third-party toasts.
- **Fallback:** When running unpackaged or denied permission, `NotificationsPanel` and Settings clearly indicate the missing permission/identity status rather than faking alerts.

### 4.3 System Telemetry, Audio, Brightness & Activity
- **Audio Output Switching:** Enumerates endpoints via CoreAudio (`MMDeviceEnumerator`); switching default output uses COM `IPolicyConfig` (undocumented Windows interface, isolated behind `IAudioEndpointSwitcher` with safe error handling).
- **Brightness:** WMI `WmiMonitorBrightnessMethods` (laptop internal displays) + `dxva2.dll` DDC/CI (external monitors where hardware/cable supports it). Unsupported external monitors report "Not supported by display" instead of silently failing or faking via screen-dim overlays.
- **Mic & Camera Indicators:** Reads Windows Privacy `CapabilityAccessManager\ConsentStore` registry (`LastUsedTimeStop == 0`). Mic mute uses CoreAudio capture endpoint `IAudioEndpointVolume`.
- **Activity Monitoring (Honest Scope):**
  - **Screenshots:** Watches `Pictures\Screenshots` (`FileSystemWatcher`) + clipboard bitmap arrival (`Win+Shift+S` / Snipping Tool) + native quick-snip hotkey.
  - **Downloads:** Watches `%UserProfile%\Downloads` for active `.crdownload` / `.part` / `.tmp` files (or browser extension `chrome.downloads` API if user enables download permission in the extension!).
  - **File Transfers:** No unrestricted system-wide Explorer hook; supported via explicit transfer watch/drag-drop or Shell UI Automation best-effort where accessible, clearly documented.
