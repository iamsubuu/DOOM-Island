# DOOM Island — Security & Privacy Audit (Stage 14)

**Audit Date:** 2026-11-26  
**Result:** Passed (0 Critical / 0 High findings)

## Verified Controls

1. **No External Telemetry or Credentials:**
   - Zero Spotify Web API, Spotify OAuth, or third-party client secrets exist anywhere in the codebase.
   - Clipboard items, notification history, and browser tab metadata stay strictly in local SQLite (`%LocalAppData%\DOOMIsland\doom_island.db`).
2. **Local IPC Isolation:**
   - `BrowserMediaBridgeService` creates a per-user Named Pipe (`DOOMIsland.BrowserBridge.<UserSidHash>`) secured with `PipeSecurity` allowing `FullControl` **only** to `WindowsIdentity.GetCurrent().User`.
   - `NativeMessagingFramer` enforces Chrome/Edge's 1 MB frame ceiling (`NativeMessageTypes.MaxPayloadBytes`) and rejects malformed or truncated frames before forwarding.
   - Host manifest (`com.doomisland.host.json`) restricts callers via explicit `allowed_origins`.
3. **Clipboard & Browsing Privacy:**
   - `Win32ClipboardMonitor` honors `ExcludeClipboardContentFromMonitorProcessing` and `CanIncludeInClipboardHistory = 0` set by password managers.
   - `SensitiveContentDetector` filters JWTs, API tokens (`ghp_`, `sk_live_`, `AIza`, `xox...`), and private key headers when `ExcludeSensitive` is enabled.
   - Foreground window title & process checks honor `ExcludedApps` and `PauseInPrivateBrowsing` (`InPrivate`, `Incognito`, `Private Browsing`).
4. **Offline / Local-Only Cutoff:**
   - `PrivacySettings.LocalOnlyMode` cuts off outbound `lrclib.net` HTTP requests while continuing to serve cached SQLite lyrics.
5. **Least-Privilege Manifest V3 Extension:**
   - Requests only `tabs`, `scripting`, `nativeMessaging`, and `storage`. Never bypasses DRM, paywalls, or site authentication.
