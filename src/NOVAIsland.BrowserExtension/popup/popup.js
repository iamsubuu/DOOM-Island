const dot = document.getElementById('statusDot');
const connText = document.getElementById('connText');
const toggle = document.getElementById('enableToggle');
const trackTitle = document.getElementById('trackTitle');
const trackArtist = document.getElementById('trackArtist');

function refresh() {
  chrome.runtime.sendMessage({ type: 'doom_popup_status' }, (res) => {
    if (!res) return;
    toggle.checked = res.enabled;
    dot.className = res.connected ? 'dot ok' : 'dot';
    connText.textContent = !res.enabled
      ? 'Bridge paused'
      : res.connected
      ? `Connected to DOOM Island (${res.tabCount} media tab${res.tabCount === 1 ? '' : 's'})`
      : 'Waiting for native host connection...';

    if (res.activeTab) {
      trackTitle.textContent = res.activeTab.title || res.activeTab.tabTitle;
      trackArtist.textContent = `${res.activeTab.artist || res.activeTab.urlHost} · ${res.activeTab.playbackState}`;
    } else {
      trackTitle.textContent = 'No active media detected';
      trackArtist.textContent = 'Play audio or video in any tab';
    }
  });
}

toggle.addEventListener('change', () => {
  chrome.runtime.sendMessage({ type: 'doom_popup_toggle', enabled: toggle.checked }, () => refresh());
});

refresh();

