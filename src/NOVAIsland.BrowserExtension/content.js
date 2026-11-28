/**
 * DOOM Island — Content Media Detector & Controller
 * Prioritizes standard Web Media Session API (navigator.mediaSession) with
 * HTMLMediaElement (<audio>/<video>) fallback. Never bypasses DRM or auth.
 */
(() => {
  if (window.__doomIslandMediaBridgeInjected) return;
  window.__doomIslandMediaBridgeInjected = true;

  let lastSnapshotHash = '';
  let lastState = 'none';
  let lastPositionTick = 0;
  let trackedElement = null;

  function findPrimaryMediaElement() {
    const elements = Array.from(document.querySelectorAll('video, audio'));
    if (elements.length === 0) return null;

    // Prefer actively playing element with audio/duration, then largest/most recent
    const playing = elements.find((el) => !el.paused && !el.ended && el.readyState >= 2);
    if (playing) return playing;

    const withDuration = elements.find((el) => Number.isFinite(el.duration) && el.duration > 3);
    return withDuration || elements[0];
  }

  function cleanTitle(rawTitle) {
    if (!rawTitle) return '';
    return rawTitle
      .replace(/\s*-\s*YouTube$/i, '')
      .replace(/\s*-\s*YouTube Music$/i, '')
      .replace(/^\(\d+\)\s*/, '')
      .trim();
  }

  function extractSiteFallbackMetadata() {
    const host = location.hostname.toLowerCase();
    let title = '';
    let artist = '';
    let album = '';
    let artworkUrl = '';

    // Generic OpenGraph / media meta tags
    const ogTitle = document.querySelector('meta[property="og:title"]')?.getAttribute('content');
    const ogImage = document.querySelector('meta[property="og:image"]')?.getAttribute('content');

    if (host.includes('youtube.com')) {
      const ytTitle =
        document.querySelector('ytd-watch-metadata h1 yt-formatted-string')?.textContent ||
        document.querySelector('ytmusic-player-bar .title')?.textContent ||
        ogTitle;
      const ytArtist =
        document.querySelector('ytmusic-player-bar .byline a')?.textContent ||
        document.querySelector('#owner #channel-name a')?.textContent ||
        '';
      title = (ytTitle || cleanTitle(document.title)).trim();
      artist = ytArtist.trim();
      artworkUrl = ogImage || '';
    } else {
      title = cleanTitle(ogTitle || document.title);
      artist = host.replace(/^www\./, '');
      artworkUrl = ogImage || '';
    }

    return { title, artist, album, artworkUrl };
  }

  function readSnapshot() {
    const el = findPrimaryMediaElement();
    if (el !== trackedElement) {
      attachElementListeners(el);
      trackedElement = el;
    }

    const ms = navigator.mediaSession;
    const msMeta = ms?.metadata;
    const fallback = extractSiteFallbackMetadata();

    const title = (msMeta?.title || fallback.title || '').trim();
    const artist = (msMeta?.artist || fallback.artist || location.hostname).trim();
    const album = (msMeta?.album || fallback.album || '').trim();

    let artworkUrl = fallback.artworkUrl || null;
    if (msMeta?.artwork && msMeta.artwork.length > 0) {
      // Pick highest-resolution artwork entry
      const sorted = [...msMeta.artwork].sort((a, b) => {
        const aw = parseInt((a.sizes || '0').split('x')[0], 10) || 0;
        const bw = parseInt((b.sizes || '0').split('x')[0], 10) || 0;
        return bw - aw;
      });
      artworkUrl = sorted[0]?.src || artworkUrl;
    }

    let playbackState = 'none';
    if (el && !el.paused && !el.ended) {
      playbackState = 'playing';
    } else if (ms?.playbackState === 'playing') {
      playbackState = 'playing';
    } else if (el && (el.currentTime > 0 || el.readyState >= 2) && !el.ended) {
      playbackState = 'paused';
    } else if (ms?.playbackState === 'paused') {
      playbackState = 'paused';
    }

    const durationSeconds = el && Number.isFinite(el.duration) ? el.duration : 0;
    const positionSeconds = el && Number.isFinite(el.currentTime) ? el.currentTime : 0;
    const playbackRate = el && Number.isFinite(el.playbackRate) ? el.playbackRate : 1.0;

    // Detect if site registers MediaSession action handlers or HTMLMediaElement exists
    const canPlayPause = Boolean(el) || playbackState !== 'none';
    const canSeek = Boolean(el && Number.isFinite(el.duration) && el.duration > 0);
    const hasNextButton = Boolean(
      document.querySelector('.ytp-next-button, .next-button, [data-testid="control-button-skip-forward"]')
    );
    const hasPrevButton = Boolean(
      document.querySelector('.ytp-prev-button, .previous-button, [data-testid="control-button-skip-back"]')
    );

    return {
      hasMedia: Boolean(el) || (Boolean(msMeta?.title) && playbackState !== 'none'),
      origin: location.origin,
      urlHost: location.hostname,
      tabTitle: document.title,
      title,
      artist,
      album,
      artworkUrl,
      durationSeconds,
      positionSeconds,
      playbackRate,
      playbackState,
      capabilities: {
        canPlayPause,
        canNext: hasNextButton || location.hostname.includes('youtube') || location.hostname.includes('spotify'),
        canPrevious: hasPrevButton || location.hostname.includes('youtube') || location.hostname.includes('spotify'),
        canSeek,
      },
    };
  }

  function emitIfChanged(forcePosition = false) {
    const snap = readSnapshot();
    if (!snap.hasMedia) {
      if (lastState !== 'none') {
        lastState = 'none';
        lastSnapshotHash = '';
        safeSend({ kind: 'media_stopped', reason: 'stopped' });
      }
      return;
    }

    const metaHash = JSON.stringify([
      snap.title,
      snap.artist,
      snap.album,
      snap.artworkUrl,
      Math.round(snap.durationSeconds),
      snap.capabilities,
    ]);

    if (lastState === 'none') {
      lastSnapshotHash = metaHash;
      lastState = snap.playbackState;
      safeSend({ kind: 'media_detected', snapshot: snap });
      return;
    }

    if (metaHash !== lastSnapshotHash) {
      lastSnapshotHash = metaHash;
      safeSend({ kind: 'media_metadata_updated', snapshot: snap });
    }

    if (snap.playbackState !== lastState) {
      lastState = snap.playbackState;
      safeSend({
        kind: 'playback_state_changed',
        playbackState: snap.playbackState,
        positionSeconds: snap.positionSeconds,
        playbackRate: snap.playbackRate,
      });
    }

    const now = Date.now();
    if (forcePosition || (snap.playbackState === 'playing' && now - lastPositionTick >= 950)) {
      lastPositionTick = now;
      safeSend({
        kind: 'playback_position_updated',
        positionSeconds: snap.positionSeconds,
        durationSeconds: snap.durationSeconds,
        playbackRate: snap.playbackRate,
      });
    }
  }

  function attachElementListeners(el) {
    if (!el || el.__doomAttached) return;
    el.__doomAttached = true;

    ['play', 'pause', 'ended', 'loadedmetadata', 'durationchange'].forEach((evt) => {
      el.addEventListener(evt, () => emitIfChanged(true), { passive: true });
    });
    el.addEventListener('seeked', () => emitIfChanged(true), { passive: true });
    el.addEventListener('ratechange', () => emitIfChanged(true), { passive: true });
  }

  function executeCommand(action, seekPositionSeconds) {
    const el = findPrimaryMediaElement();

    try {
      switch (action) {
        case 'play':
          if (!el) return { success: false, reason: 'no_media_element' };
          el.play().catch(() => {});
          return { success: true, reason: 'ok' };

        case 'pause':
          if (!el) return { success: false, reason: 'no_media_element' };
          el.pause();
          return { success: true, reason: 'ok' };

        case 'toggle': {
          if (el) {
            if (el.paused) el.play().catch(() => {});
            else el.pause();
            return { success: true, reason: 'ok' };
          }
          const btn = document.querySelector(
            '.ytp-play-button, #play-pause-button, [data-testid="control-button-playpause"]'
          );
          if (btn) {
            btn.click();
            return { success: true, reason: 'ok' };
          }
          return { success: false, reason: 'unsupported' };
        }

        case 'next': {
          const nextBtn = document.querySelector(
            '.ytp-next-button, ytmusic-player-bar .next-button, [data-testid="control-button-skip-forward"], button[aria-label*="Next" i]'
          );
          if (nextBtn) {
            nextBtn.click();
            return { success: true, reason: 'ok' };
          }
          return { success: false, reason: 'unsupported' };
        }

        case 'previous': {
          const prevBtn = document.querySelector(
            '.ytp-prev-button, ytmusic-player-bar .previous-button, [data-testid="control-button-skip-back"], button[aria-label*="Previous" i]'
          );
          if (prevBtn && !prevBtn.hasAttribute('disabled')) {
            prevBtn.click();
            return { success: true, reason: 'ok' };
          }
          if (el && el.currentTime > 3) {
            el.currentTime = 0;
            return { success: true, reason: 'ok' };
          }
          return { success: false, reason: 'unsupported' };
        }

        case 'seek': {
          if (!el || !Number.isFinite(seekPositionSeconds)) {
            return { success: false, reason: 'unsupported' };
          }
          const target = Math.max(0, Math.min(seekPositionSeconds, el.duration || seekPositionSeconds));
          el.currentTime = target;
          emitIfChanged(true);
          return { success: true, reason: 'ok' };
        }

        default:
          return { success: false, reason: 'unknown_action' };
      }
    } catch {
      return { success: false, reason: 'execution_error' };
    }
  }

  function safeSend(msg) {
    try {
      if (!chrome.runtime?.id) return;
      chrome.runtime.sendMessage(msg).catch(() => {});
    } catch {
      // Extension context invalidated during reload
    }
  }

  chrome.runtime.onMessage.addListener((msg, _sender, sendResponse) => {
    if (msg?.type === 'doom_execute_command') {
      const res = executeCommand(msg.action, msg.seekPositionSeconds);
      sendResponse(res);
      setTimeout(() => emitIfChanged(true), 120);
      return true;
    }
    if (msg?.type === 'doom_request_snapshot') {
      lastState = 'none';
      emitIfChanged(true);
      sendResponse({ ok: true });
      return true;
    }
    return false;
  });

  // Periodic lightweight sync for MediaSession-only players and position updates
  setInterval(() => emitIfChanged(false), 1000);
  const observer = new MutationObserver(() => emitIfChanged(false));
  if (document.body) {
    observer.observe(document.body, { childList: true, subtree: true });
  }
  setTimeout(() => emitIfChanged(true), 400);
})();

