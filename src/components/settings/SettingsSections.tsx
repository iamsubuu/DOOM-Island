import React, { useEffect, useState } from 'react';
import {
  CheckCircle2,
  Disc3,
  Download,
  Keyboard,
  Play,
  Plus,
  RefreshCw,
  RotateCcw,
  Trash2,
  X,
} from 'lucide-react';
import { APP_CONFIG } from '../../config/appConfig';
import { useNova } from '../../context/NovaContext';
import type {
  AnimationPreset,
  CollapseBehavior,
  ColorMode,
  DoubleClickAction,
  FontFamilyOption,
  NotificationItem,
  ScreenPosition,
  SettingsSectionId,
  ThemePreset,
} from '../../types/island';
import { NovaSwitch, SettingRow, SettingSlider } from '../ui/Controls';
import { AppearanceLivePreview } from './AppearanceLivePreview';
import { PositionPreview } from './PositionPreview';

const ACCENT_SWATCHES = [
  { name: 'Steel Blue', hex: '#7aa2f7' },
  { name: 'Arctic Ice', hex: '#38bdf8' },
  { name: 'Emerald Mint', hex: '#34d399' },
  { name: 'Soft Violet', hex: '#a78bfa' },
  { name: 'Warm Amber', hex: '#f59e0b' },
  { name: 'Coral Rose', hex: '#fb7185' },
  { name: 'Pure Silver', hex: '#e4e4e7' },
];

const SELECT_CLS =
  'bg-white dark:bg-[#171922] border border-zinc-300 dark:border-white/[0.11] rounded-lg px-3 py-1.5 text-[12.5px] text-zinc-900 dark:text-zinc-100 focus:outline-none focus:border-zinc-500 dark:focus:border-white/30';

const SEGMENT_WRAP_CLS =
  'flex items-center gap-0.5 p-0.5 rounded-lg bg-zinc-200/80 dark:bg-white/[0.05]';

const segmentBtn = (active: boolean) =>
  `px-2.5 py-1 rounded-md text-[12px] font-medium transition-colors whitespace-nowrap cursor-pointer ${
    active
      ? 'bg-white text-zinc-900 shadow-sm dark:bg-white/[0.14] dark:text-white'
      : 'text-zinc-600 dark:text-zinc-400 hover:text-zinc-900 dark:hover:text-zinc-200'
  }`;

export function SettingsSectionContent({ section }: { section: SettingsSectionId }) {
  const {
    appName,
    settings,
    updateSettingsSection,
    updateShortcut,
    resetAppearance,
    resetPosition,
    resetShortcuts,
    clearStorageBucket,
    resetAllSettings,
    setIslandMode,
    islandMode,
    showToast,
  } = useNova();

  const [newExcludedApp, setNewExcludedApp] = useState('');
  const [recordingId, setRecordingId] = useState<string | null>(null);
  const [checkingUpdate, setCheckingUpdate] = useState(false);
  const [updateResult, setUpdateResult] = useState<string | null>(null);

  // Live Keyboard Shortcut Recorder
  useEffect(() => {
    if (!recordingId) return;
    const onKeyDown = (e: KeyboardEvent) => {
      e.preventDefault();
      e.stopPropagation();
      if (e.key === 'Escape') {
        setRecordingId(null);
        return;
      }
      if (['Control', 'Alt', 'Shift', 'Meta'].includes(e.key)) return;
      const parts: string[] = [];
      if (e.ctrlKey) parts.push('Ctrl');
      if (e.altKey) parts.push('Alt');
      if (e.shiftKey) parts.push('Shift');
      const keyLabel = e.key === ' ' ? 'Space' : e.key.length === 1 ? e.key.toUpperCase() : e.key;
      parts.push(keyLabel);
      updateShortcut(recordingId, parts.join(' + '));
      setRecordingId(null);
    };
    window.addEventListener('keydown', onKeyDown, true);
    return () => window.removeEventListener('keydown', onKeyDown, true);
  }, [recordingId, updateShortcut]);

  const handleExportSettings = () => {
    const blob = new Blob([JSON.stringify(settings, null, 2)], { type: 'application/json' });
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = `${appName.toLowerCase().replace(/\s+/g, '-')}-settings.json`;
    a.click();
    URL.revokeObjectURL(url);
    showToast('Exported settings JSON file');
  };

  if (section === 'general') {
    const g = settings.general;
    return (
      <div className="flex flex-col">
        <SettingRow
          title="Application display name"
          description="Configurable brand name propagated across windows, island, and title bars."
        >
          <input
            type="text"
            value={g.appName}
            onChange={(e) => updateSettingsSection('general', { appName: e.target.value })}
            aria-label="Application display name"
            className="w-44 bg-white dark:bg-[#171922] border border-zinc-300 dark:border-white/[0.11] rounded-lg px-3 py-1.5 text-[12.5px] text-zinc-900 dark:text-zinc-100 focus:outline-none focus:border-zinc-500 dark:focus:border-white/30"
          />
        </SettingRow>

        <SettingRow
          title="Compact pill wordmark"
          description="Short label displayed inside the idle compact Dynamic Island pill."
        >
          <input
            type="text"
            value={g.appShortName}
            maxLength={14}
            onChange={(e) => updateSettingsSection('general', { appShortName: e.target.value })}
            aria-label="Compact pill wordmark"
            className="w-32 bg-white dark:bg-[#171922] border border-zinc-300 dark:border-white/[0.11] rounded-lg px-3 py-1.5 text-[12.5px] text-zinc-900 dark:text-zinc-100 focus:outline-none focus:border-zinc-500 dark:focus:border-white/30"
          />
        </SettingRow>

        <SettingRow
          title="Launch on Windows startup"
          description="Automatically start minimized in the system tray upon sign-in (simulated in prototype)."
        >
          <NovaSwitch
            checked={g.launchOnStartup}
            onChange={(v) => updateSettingsSection('general', { launchOnStartup: v })}
            ariaLabel="Launch on startup"
          />
        </SettingRow>

        <SettingRow
          title="Always on top"
          description="Keep the floating island above active desktop windows."
        >
          <NovaSwitch
            checked={g.alwaysOnTop}
            onChange={(v) => updateSettingsSection('general', { alwaysOnTop: v })}
            ariaLabel="Always on top"
          />
        </SettingRow>

        <SettingSlider
          title="Auto-collapse duration"
          description="Seconds of inactivity before expanded views collapse automatically (0 = disabled)."
          value={g.autoCollapseSeconds}
          min={0}
          max={30}
          step={1}
          unit={g.autoCollapseSeconds === 0 ? ' (Off)' : 's'}
          onChange={(v) => updateSettingsSection('general', { autoCollapseSeconds: v })}
        />

        <SettingRow
          title="Collapse behavior"
          description="Choose how the expanded island returns to its compact pill."
        >
          <select
            value={g.collapseBehavior}
            onChange={(e) =>
              updateSettingsSection('general', {
                collapseBehavior: e.target.value as CollapseBehavior,
              })
            }
            aria-label="Collapse behavior"
            className={SELECT_CLS}
          >
            <option value="outside-click">Collapse on outside click</option>
            <option value="mouse-leave">Collapse when pointer leaves</option>
            <option value="manual">Manual collapse button only</option>
          </select>
        </SettingRow>

        <SettingRow
          title="Double-click action"
          description="Action triggered when double-clicking the Dynamic Island surface."
        >
          <select
            value={g.doubleClickAction}
            onChange={(e) =>
              updateSettingsSection('general', {
                doubleClickAction: e.target.value as DoubleClickAction,
              })
            }
            aria-label="Double-click action"
            className={SELECT_CLS}
          >
            <option value="toggle-lyrics">Toggle Island Lyrics view</option>
            <option value="open-fullscreen-lyrics">Open Fullscreen Lyrics</option>
            <option value="open-dashboard">Open Dashboard</option>
            <option value="toggle-play">Play / Pause current track</option>
          </select>
        </SettingRow>
      </div>
    );
  }

  if (section === 'appearance') {
    const a = settings.appearance;
    return (
      <div className="flex flex-col">
        <AppearanceLivePreview />

        <div className="flex items-center justify-between pb-3 mb-1 border-b border-zinc-200/80 dark:border-white/[0.06]">
          <p className="text-[12px] text-zinc-500 dark:text-zinc-400">
            All changes persist automatically in local storage and update the live island immediately.
          </p>
          <button
            type="button"
            onClick={resetAppearance}
            className="flex items-center gap-1.5 px-2.5 py-1 rounded-md text-[12px] text-zinc-700 dark:text-zinc-300 bg-zinc-200/70 hover:bg-zinc-300/70 dark:bg-white/[0.06] dark:hover:bg-white/[0.12] transition-colors cursor-pointer whitespace-nowrap"
          >
            <RotateCcw className="w-3.5 h-3.5" />
            <span>Reset Appearance</span>
          </button>
        </div>

        <SettingRow
          title="Application theme"
          description="Switch the desktop workspace and windows between Dark, Light, or System preference."
        >
          <div className={SEGMENT_WRAP_CLS}>
            {(
              [
                ['dark', 'Dark'],
                ['light', 'Light'],
                ['system', 'System'],
              ] as [ColorMode, string][]
            ).map(([mode, label]) => (
              <button
                key={mode}
                type="button"
                onClick={() => updateSettingsSection('appearance', { colorMode: mode })}
                className={segmentBtn(a.colorMode === mode)}
              >
                {label}
              </button>
            ))}
          </div>
        </SettingRow>

        <SettingRow
          title="Island hardware material"
          description="Pure Black matches the macOS/iOS Dynamic Island hardware silhouette."
        >
          <div className={SEGMENT_WRAP_CLS}>
            {(
              [
                ['pure-black', 'Pure Black'],
                ['obsidian', 'Obsidian'],
                ['graphite', 'Graphite'],
                ['contrast', 'Contrast'],
              ] as [ThemePreset, string][]
            ).map(([t, label]) => (
              <button
                key={t}
                type="button"
                onClick={() => updateSettingsSection('appearance', { theme: t })}
                className={segmentBtn(a.theme === t)}
              >
                {label}
              </button>
            ))}
          </div>
        </SettingRow>

        <SettingRow
          title="Accent color"
          description="Applied to progress indicators, equalizer bars, switches, and focus highlights."
        >
          <div className="flex items-center gap-2">
            {ACCENT_SWATCHES.map((swatch) => (
              <button
                key={swatch.hex}
                type="button"
                onClick={() => updateSettingsSection('appearance', { accentColor: swatch.hex })}
                title={swatch.name}
                aria-label={`Select ${swatch.name}`}
                className={`w-6 h-6 rounded-full transition-transform cursor-pointer border border-black/15 dark:border-white/15 ${
                  a.accentColor.toLowerCase() === swatch.hex.toLowerCase()
                    ? 'scale-110 ring-2 ring-zinc-900 dark:ring-white ring-offset-2 ring-offset-white dark:ring-offset-[#101116]'
                    : 'hover:scale-105'
                }`}
                style={{ backgroundColor: swatch.hex }}
              />
            ))}
            <input
              type="color"
              value={a.accentColor}
              onChange={(e) => updateSettingsSection('appearance', { accentColor: e.target.value })}
              aria-label="Custom accent color"
              className="w-7 h-7 rounded-md bg-transparent border border-zinc-300 dark:border-white/15 cursor-pointer"
            />
          </div>
        </SettingRow>

        <SettingRow title="Font family" description="Primary UI typeface across island panels.">
          <select
            value={a.fontFamily}
            onChange={(e) =>
              updateSettingsSection('appearance', {
                fontFamily: e.target.value as FontFamilyOption,
              })
            }
            aria-label="Font family"
            className={SELECT_CLS}
          >
            <option value="Plus Jakarta Sans">Plus Jakarta Sans</option>
            <option value="Manrope">Manrope</option>
            <option value="Outfit">Outfit</option>
            <option value="Segoe UI Variable">Segoe UI Variable</option>
          </select>
        </SettingRow>

        <SettingSlider
          title="Font size"
          value={a.fontSize}
          min={11}
          max={16}
          unit="px"
          onChange={(v) => updateSettingsSection('appearance', { fontSize: v })}
        />

        <SettingRow title="Font weight" description="Base text weight for optical clarity on pure black.">
          <div className={SEGMENT_WRAP_CLS}>
            {([400, 500, 600] as const).map((w) => (
              <button
                key={w}
                type="button"
                onClick={() => updateSettingsSection('appearance', { fontWeight: w })}
                className={`${segmentBtn(a.fontWeight === w)} font-mono-tabular`}
              >
                {w}
              </button>
            ))}
          </div>
        </SettingRow>

        <SettingSlider
          title="Compact island width"
          value={a.islandWidth}
          min={170}
          max={300}
          unit="px"
          onChange={(v) => updateSettingsSection('appearance', { islandWidth: v })}
        />

        <SettingSlider
          title="Compact island height"
          value={a.islandHeight}
          min={32}
          max={48}
          unit="px"
          onChange={(v) => updateSettingsSection('appearance', { islandHeight: v })}
        />

        <SettingSlider
          title="Corner radius"
          value={a.cornerRadius}
          min={14}
          max={32}
          unit="px"
          onChange={(v) => updateSettingsSection('appearance', { cornerRadius: v })}
        />

        <SettingSlider
          title="Surface opacity"
          value={a.opacity}
          min={80}
          max={100}
          unit="%"
          onChange={(v) => updateSettingsSection('appearance', { opacity: v })}
        />

        <SettingSlider
          title="Subtle backdrop blur"
          value={a.blur}
          min={0}
          max={32}
          unit="px"
          onChange={(v) => updateSettingsSection('appearance', { blur: v })}
        />

        <SettingSlider
          title="Expanded panel padding"
          value={a.spacing}
          min={10}
          max={24}
          unit="px"
          onChange={(v) => updateSettingsSection('appearance', { spacing: v })}
        />

        <SettingSlider
          title="Animation speed"
          description="Global spring physics velocity multiplier."
          value={a.animationSpeed}
          min={0.5}
          max={1.8}
          step={0.1}
          unit="x"
          onChange={(v) => updateSettingsSection('appearance', { animationSpeed: v })}
        />
      </div>
    );
  }

  if (section === 'position') {
    const pos = settings.position;
    return (
      <div className="flex flex-col">
        <PositionPreview />

        <div className="flex items-center justify-between pb-3 mb-1 border-b border-zinc-200/80 dark:border-white/[0.06]">
          <p className="text-[12px] text-zinc-500 dark:text-zinc-400">
            Configure anchor preset or fine-tune exact screen coordinates.
          </p>
          <button
            type="button"
            onClick={resetPosition}
            className="flex items-center gap-1.5 px-2.5 py-1 rounded-md text-[12px] text-zinc-700 dark:text-zinc-300 bg-zinc-200/70 hover:bg-zinc-300/70 dark:bg-white/[0.06] dark:hover:bg-white/[0.12] transition-colors cursor-pointer whitespace-nowrap"
          >
            <RotateCcw className="w-3.5 h-3.5" />
            <span>Reset Position</span>
          </button>
        </div>

        <SettingRow
          title="Anchor preset"
          description="Snap to a standard edge anchor or switch to Custom X/Y coordinates."
        >
          <div className={SEGMENT_WRAP_CLS}>
            {(
              [
                ['top-left', 'Top Left'],
                ['top-center', 'Top Center'],
                ['top-right', 'Top Right'],
                ['custom', 'Custom X/Y'],
              ] as [ScreenPosition, string][]
            ).map(([anchor, label]) => (
              <button
                key={anchor}
                type="button"
                onClick={() => updateSettingsSection('position', { screenPosition: anchor })}
                className={segmentBtn(pos.screenPosition === anchor)}
              >
                {label}
              </button>
            ))}
          </div>
        </SettingRow>

        <SettingSlider
          title="Custom horizontal position (X)"
          description="Horizontal center offset across display width (switches anchor to Custom)."
          value={pos.customX}
          min={12}
          max={88}
          unit="%"
          onChange={(v) =>
            updateSettingsSection('position', { screenPosition: 'custom', customX: v })
          }
        />

        <SettingSlider
          title="Custom vertical offset (Y)"
          description="Distance in pixels from the top edge of the monitor."
          value={pos.customY}
          min={8}
          max={140}
          unit="px"
          onChange={(v) =>
            updateSettingsSection('position', { screenPosition: 'custom', customY: v })
          }
        />

        <SettingRow
          title="Target monitor"
          description="Select which display hosts the floating island in multi-monitor setups."
        >
          <select
            value={pos.monitor}
            onChange={(e) => updateSettingsSection('position', { monitor: e.target.value })}
            aria-label="Target monitor"
            className={SELECT_CLS}
          >
            <option value="Display 1 · Studio Pro 4K (3840×2160)">
              Display 1 · Studio Pro 4K (3840×2160)
            </option>
            <option value="Display 2 · Ultrawide QHD (3440×1440)">
              Display 2 · Ultrawide QHD (3440×1440)
            </option>
            <option value="Follow active cursor monitor">Follow active cursor monitor</option>
            <option value="Show on all monitors">Show on all monitors</option>
          </select>
        </SettingRow>

        <SettingRow
          title="Remember last position"
          description="Persist custom X/Y placement across reloads using local storage."
        >
          <NovaSwitch
            checked={pos.rememberLastPosition}
            onChange={(v) => updateSettingsSection('position', { rememberLastPosition: v })}
            ariaLabel="Remember last position"
          />
        </SettingRow>
      </div>
    );
  }

  if (section === 'animation') {
    const an = settings.animation;
    return (
      <div className="flex flex-col">
        <div className="mb-4 p-4 rounded-xl bg-zinc-100/80 dark:bg-white/[0.03] border border-zinc-200/90 dark:border-white/[0.08] flex items-center justify-between gap-4">
          <div>
            <div className="text-[13px] font-medium text-zinc-900 dark:text-zinc-100">
              Preview Spring Morph
            </div>
            <div className="text-[12px] text-zinc-500 dark:text-zinc-400">
              Toggle the live island between compact and expanded states to test physics feel.
            </div>
          </div>
          <button
            type="button"
            onClick={() => setIslandMode(islandMode === 'compact' ? 'expanded' : 'compact')}
            className="flex items-center gap-1.5 px-3 py-1.5 rounded-lg text-[12px] font-medium bg-zinc-900 text-white dark:bg-white/[0.12] dark:hover:bg-white/[0.18] transition-colors cursor-pointer whitespace-nowrap"
          >
            <Play className="w-3.5 h-3.5 fill-current" />
            <span>Test Morph</span>
          </button>
        </div>

        <SettingRow
          title="Motion physics preset"
          description="Defines spring tension and damping during island expansion and collapse."
        >
          <div className={SEGMENT_WRAP_CLS}>
            {(
              [
                ['fluid', 'Fluid'],
                ['snappy', 'Snappy'],
                ['silk', 'Silk'],
                ['instant', 'Instant'],
              ] as [AnimationPreset, string][]
            ).map(([p, label]) => (
              <button
                key={p}
                type="button"
                onClick={() => updateSettingsSection('animation', { preset: p })}
                className={segmentBtn(an.preset === p)}
              >
                {label}
              </button>
            ))}
          </div>
        </SettingRow>

        <SettingSlider
          title="Animation speed multiplier"
          value={settings.appearance.animationSpeed}
          min={0.5}
          max={1.8}
          step={0.1}
          unit="x"
          onChange={(v) => updateSettingsSection('appearance', { animationSpeed: v })}
        />

        <SettingRow
          title="Reduce motion"
          description="Replace spring transitions with minimal opacity crossfades."
        >
          <NovaSwitch
            checked={an.reduceMotion}
            onChange={(v) => updateSettingsSection('animation', { reduceMotion: v })}
            ariaLabel="Reduce motion"
          />
        </SettingRow>

        <SettingRow
          title="Subtle hover scale"
          description="Slightly elevate the compact pill when hovering with the mouse cursor."
        >
          <NovaSwitch
            checked={an.hoverExpand}
            onChange={(v) => updateSettingsSection('animation', { hoverExpand: v })}
            ariaLabel="Hover scale"
          />
        </SettingRow>

        <SettingRow
          title="Notification arrival bounce"
          description="Animate a brief tactile spring settle when a new alert arrives."
        >
          <NovaSwitch
            checked={an.notificationBounce}
            onChange={(v) => updateSettingsSection('animation', { notificationBounce: v })}
            ariaLabel="Notification bounce"
          />
        </SettingRow>

        <SettingRow
          title="Idle ambient breathing"
          description="Display a restrained breathing indicator inside the compact pill while idle."
        >
          <NovaSwitch
            checked={an.idleBreathing}
            onChange={(v) => updateSettingsSection('animation', { idleBreathing: v })}
            ariaLabel="Idle ambient breathing"
          />
        </SettingRow>

        <SettingRow
          title="Smooth progress rings"
          description="Interpolate timer and Pomodoro SVG arc transitions continuously."
        >
          <NovaSwitch
            checked={an.smoothProgressRings}
            onChange={(v) => updateSettingsSection('animation', { smoothProgressRings: v })}
            ariaLabel="Smooth progress rings"
          />
        </SettingRow>
      </div>
    );
  }

  if (section === 'music') {
    const m = settings.music;
    return (
      <div className="flex flex-col">
        <SettingRow
          title="Show album artwork in compact pill"
          description="Render circular miniature cover art on the left edge of the music pill."
        >
          <NovaSwitch
            checked={m.showAlbumArtInCompact}
            onChange={(v) => updateSettingsSection('music', { showAlbumArtInCompact: v })}
            ariaLabel="Show album artwork in compact pill"
          />
        </SettingRow>

        <SettingRow
          title="Audio-reactive animated equalizer"
          description="Display rhythmic multi-band equalizer bars paced to playback BPM."
        >
          <NovaSwitch
            checked={m.showAudioVisualizer}
            onChange={(v) => updateSettingsSection('music', { showAudioVisualizer: v })}
            ariaLabel="Show audio visualizer"
          />
        </SettingRow>

        <SettingRow
          title="Compact playback progress hairline"
          description="Show a thin bottom progress bar inside the collapsed music pill."
        >
          <NovaSwitch
            checked={m.showCompactProgress}
            onChange={(v) => updateSettingsSection('music', { showCompactProgress: v })}
            ariaLabel="Show compact progress bar"
          />
        </SettingRow>

        <SettingRow
          title="Auto-expand on track change"
          description="Briefly open the expanded music player whenever the next track begins."
        >
          <NovaSwitch
            checked={m.autoExpandOnTrackChange}
            onChange={(v) => updateSettingsSection('music', { autoExpandOnTrackChange: v })}
            ariaLabel="Auto expand on track change"
          />
        </SettingRow>

        <SettingRow
          title="Scroll long track titles"
          description="Smoothly pan long song titles across the compact pill."
        >
          <NovaSwitch
            checked={m.scrollLongTitles}
            onChange={(v) => updateSettingsSection('music', { scrollLongTitles: v })}
            ariaLabel="Scroll long titles"
          />
        </SettingRow>

        <SettingRow
          title="Route hardware media keys"
          description="Bind keyboard Play/Pause, Next, and Previous controls (planned for native WPF)."
        >
          <NovaSwitch
            checked={m.interceptMediaKeys}
            onChange={(v) => updateSettingsSection('music', { interceptMediaKeys: v })}
            ariaLabel="Intercept media keys"
          />
        </SettingRow>
      </div>
    );
  }

  if (section === 'spotify') {
    const sp = settings.spotify;
    return (
      <div className="flex flex-col">
        <div className="mb-4 p-4 rounded-xl bg-zinc-100/80 dark:bg-white/[0.03] border border-zinc-200/90 dark:border-white/[0.08] flex flex-col sm:flex-row sm:items-center justify-between gap-4">
          <div className="flex items-center gap-3">
            <div className="w-10 h-10 rounded-xl bg-zinc-900 border border-white/10 flex items-center justify-center shrink-0">
              <Disc3
                className="w-5 h-5"
                style={{ color: sp.connected ? '#1DB954' : '#71717a' }}
              />
            </div>
            <div>
              <div className="text-[13.5px] font-semibold text-zinc-900 dark:text-white">
                Spotify Mock Session Bridge
              </div>
              <div className="text-[12px] text-zinc-500 dark:text-zinc-400">
                {sp.connected
                  ? `Simulated session · ${sp.accountEmail} · ${sp.activeDevice}`
                  : 'Disconnected · Using local sample audio playback'}
              </div>
            </div>
          </div>

          <button
            type="button"
            onClick={() => {
              updateSettingsSection('spotify', { connected: !sp.connected });
              showToast(
                sp.connected ? 'Disconnected simulated Spotify session' : 'Connected simulated Spotify session'
              );
            }}
            className={`px-3.5 py-1.5 rounded-lg text-[12px] font-medium transition-colors whitespace-nowrap shrink-0 cursor-pointer ${
              sp.connected
                ? 'bg-zinc-200/80 text-zinc-800 hover:bg-zinc-300/80 dark:bg-white/[0.08] dark:text-zinc-200 dark:hover:bg-white/[0.14]'
                : 'bg-zinc-900 text-white hover:bg-zinc-800 dark:bg-white dark:text-zinc-950 dark:hover:bg-zinc-200'
            }`}
          >
            {sp.connected ? 'Disconnect' : 'Connect Mock Session'}
          </button>
        </div>

        <SettingRow
          title="Simulated playback device"
          description="Target endpoint displayed in the music player header."
        >
          <select
            value={sp.activeDevice}
            onChange={(e) => updateSettingsSection('spotify', { activeDevice: e.target.value })}
            aria-label="Simulated playback device"
            className={SELECT_CLS}
          >
            <option value="DESKTOP-NOVA (Simulated)">DESKTOP-NOVA (Simulated)</option>
            <option value="Studio DAC · USB-C (Simulated)">Studio DAC · USB-C (Simulated)</option>
            <option value="Living Room Speaker (Simulated)">Living Room Speaker (Simulated)</option>
          </select>
        </SettingRow>

        <SettingRow
          title="Playback state poll interval"
          description="Planned polling cadence for native WPF Spotify Web API / SMTC integration."
        >
          <div className={SEGMENT_WRAP_CLS}>
            {([1000, 2000, 5000] as const).map((ms) => (
              <button
                key={ms}
                type="button"
                onClick={() => updateSettingsSection('spotify', { pollIntervalMs: ms })}
                className={`${segmentBtn(sp.pollIntervalMs === ms)} font-mono-tabular`}
              >
                {ms / 1000}s
              </button>
            ))}
          </div>
        </SettingRow>

        <SettingRow
          title="Sync album artwork & upcoming queue"
          description="Display full track queue thumbnails inside the expanded Music panel."
        >
          <NovaSwitch
            checked={sp.syncArtworkAndQueue}
            onChange={(v) => updateSettingsSection('spotify', { syncArtworkAndQueue: v })}
            ariaLabel="Sync album artwork and queue"
          />
        </SettingRow>

        <SettingRow
          title="Prefer high-resolution album covers"
          description="Request 640×640 cover art for fullscreen lyrics ambient backdrops."
        >
          <NovaSwitch
            checked={sp.preferHighResCovers}
            onChange={(v) => updateSettingsSection('spotify', { preferHighResCovers: v })}
            ariaLabel="Prefer high-resolution covers"
          />
        </SettingRow>

        <p className="pt-4 text-[11.5px] text-zinc-500 dark:text-zinc-400 leading-relaxed">
          Prototype note: Spotify playback, device selection, and account status operate strictly on local mock data in this frontend prototype. No real OAuth tokens or external Spotify API requests are executed.
        </p>
      </div>
    );
  }

  if (section === 'lyrics') {
    const l = settings.lyrics;
    return (
      <div className="flex flex-col">
        <SettingRow
          title="Prefer synchronized lyrics"
          description="Prioritize line-by-line LRC timestamps over static plain-text lyrics."
        >
          <NovaSwitch
            checked={l.preferSynchronized}
            onChange={(v) => updateSettingsSection('lyrics', { preferSynchronized: v })}
            ariaLabel="Prefer synchronized lyrics"
          />
        </SettingRow>

        <SettingRow
          title="LRCLIB automatic fallback"
          description="Fall back to the mock LRCLIB catalog when primary lyrics are missing."
        >
          <NovaSwitch
            checked={l.lrclibFallback}
            onChange={(v) => updateSettingsSection('lyrics', { lrclibFallback: v })}
            ariaLabel="LRCLIB fallback"
          />
        </SettingRow>

        <SettingRow
          title="Provider resolution order"
          description="Order in which lyrics providers are queried for each track."
        >
          <select
            value={l.sourcePriority}
            onChange={(e) =>
              updateSettingsSection('lyrics', {
                sourcePriority: e.target.value as typeof l.sourcePriority,
              })
            }
            aria-label="Lyrics source priority"
            className={SELECT_CLS}
          >
            <option value="Spotify -> LRCLIB -> Local">Spotify → LRCLIB → Local .lrc</option>
            <option value="LRCLIB -> Spotify -> Local">LRCLIB → Spotify → Local .lrc</option>
            <option value="Local -> LRCLIB">Local .lrc → LRCLIB</option>
          </select>
        </SettingRow>

        <SettingSlider
          title="Lyrics font size"
          value={l.fontSize}
          min={13}
          max={22}
          unit="px"
          onChange={(v) => updateSettingsSection('lyrics', { fontSize: v })}
        />

        <SettingSlider
          title="Line spacing"
          value={l.lineSpacing}
          min={1.3}
          max={2.0}
          step={0.05}
          onChange={(v) => updateSettingsSection('lyrics', { lineSpacing: v })}
        />

        <SettingRow
          title="Smooth automatic scrolling"
          description="Center the active lyric line smoothly as playback progresses."
        >
          <NovaSwitch
            checked={l.smoothScroll}
            onChange={(v) => updateSettingsSection('lyrics', { smoothScroll: v })}
            ariaLabel="Smooth scrolling"
          />
        </SettingRow>

        <SettingRow
          title="Soften inactive lyric lines"
          description="Apply subtle depth blur to distant upcoming and past lines."
        >
          <NovaSwitch
            checked={l.blurInactiveLines}
            onChange={(v) => updateSettingsSection('lyrics', { blurInactiveLines: v })}
            ariaLabel="Blur inactive lines"
          />
        </SettingRow>

        <SettingRow
          title="Display secondary translations"
          description="Show subtitle annotations beneath active lyric lines when present."
        >
          <NovaSwitch
            checked={l.showTranslations}
            onChange={(v) => updateSettingsSection('lyrics', { showTranslations: v })}
            ariaLabel="Show translations"
          />
        </SettingRow>

        <SettingSlider
          title="Fullscreen backdrop intensity"
          description="Controls ambient artwork bloom visibility behind fullscreen lyrics."
          value={l.fullscreenBackdropOpacity}
          min={20}
          max={90}
          unit="%"
          onChange={(v) => updateSettingsSection('lyrics', { fullscreenBackdropOpacity: v })}
        />
      </div>
    );
  }

  if (section === 'clipboard') {
    const c = settings.clipboard;
    const handleAddApp = (e: React.FormEvent) => {
      e.preventDefault();
      const cleaned = newExcludedApp.trim();
      if (!cleaned || c.excludedApps.includes(cleaned)) return;
      updateSettingsSection('clipboard', { excludedApps: [...c.excludedApps, cleaned] });
      setNewExcludedApp('');
    };

    return (
      <div className="flex flex-col">
        <SettingRow
          title="Enable clipboard history monitoring"
          description="Record copied text, links, code blocks, and images locally."
        >
          <NovaSwitch
            checked={c.enabled}
            onChange={(v) => updateSettingsSection('clipboard', { enabled: v })}
            ariaLabel="Enable clipboard monitoring"
          />
        </SettingRow>

        <SettingRow
          title="History retention limit"
          description="Maximum unpinned items retained in the local clipboard ring buffer."
        >
          <div className={SEGMENT_WRAP_CLS}>
            {([25, 50, 100, 250] as const).map((limit) => (
              <button
                key={limit}
                type="button"
                onClick={() => updateSettingsSection('clipboard', { historyLimit: limit })}
                className={`${segmentBtn(c.historyLimit === limit)} font-mono-tabular`}
              >
                {limit}
              </button>
            ))}
          </div>
        </SettingRow>

        <SettingRow
          title="Auto-clear unpinned entries"
          description="Automatically purge non-pinned clipboard items on schedule."
        >
          <select
            value={c.autoClear}
            onChange={(e) =>
              updateSettingsSection('clipboard', {
                autoClear: e.target.value as typeof c.autoClear,
              })
            }
            aria-label="Auto-clear schedule"
            className={SELECT_CLS}
          >
            <option value="never">Never</option>
            <option value="1h">After 1 hour</option>
            <option value="24h">After 24 hours</option>
            <option value="restart">On app restart</option>
          </select>
        </SettingRow>

        <SettingRow
          title="Exclude sensitive content"
          description="Automatically ignore password manager tokens, API keys, and concealed formats."
        >
          <NovaSwitch
            checked={c.excludeSensitive}
            onChange={(v) => updateSettingsSection('clipboard', { excludeSensitive: v })}
            ariaLabel="Exclude sensitive content"
          />
        </SettingRow>

        <SettingRow
          title="Capture images & screenshots"
          description="Store thumbnails of copied bitmaps and screenshot captures."
        >
          <NovaSwitch
            checked={c.captureImages}
            onChange={(v) => updateSettingsSection('clipboard', { captureImages: v })}
            ariaLabel="Capture images"
          />
        </SettingRow>

        <SettingRow
          title="Persist pinned items across restarts"
          description="Keep pinned code snippets and references stored locally."
        >
          <NovaSwitch
            checked={c.persistPinned}
            onChange={(v) => updateSettingsSection('clipboard', { persistPinned: v })}
            ariaLabel="Persist pinned items"
          />
        </SettingRow>

        {/* Excluded Applications Manager */}
        <div className="pt-4 flex flex-col gap-3">
          <div>
            <div className="text-[13.5px] font-medium text-zinc-900 dark:text-zinc-100">
              Excluded applications
            </div>
            <p className="text-[12px] text-zinc-500 dark:text-zinc-400 mt-0.5">
              Clipboard events originating from these executables are never recorded.
            </p>
          </div>

          <form onSubmit={handleAddApp} className="flex items-center gap-2">
            <input
              type="text"
              value={newExcludedApp}
              onChange={(e) => setNewExcludedApp(e.target.value)}
              placeholder="Add process name (e.g. Vault.exe)"
              className="flex-1 bg-white dark:bg-white/[0.04] border border-zinc-300 dark:border-white/[0.10] rounded-lg px-3 py-1.5 text-[12.5px] text-zinc-900 dark:text-white placeholder:text-zinc-400 dark:placeholder:text-zinc-500 focus:outline-none focus:border-zinc-500 dark:focus:border-white/25"
            />
            <button
              type="submit"
              className="flex items-center gap-1 px-3 py-1.5 rounded-lg text-[12px] font-medium bg-zinc-900 text-white hover:bg-zinc-800 dark:bg-white/[0.10] dark:hover:bg-white/[0.17] transition-colors cursor-pointer"
            >
              <Plus className="w-3.5 h-3.5" />
              <span>Exclude</span>
            </button>
          </form>

          <div className="divide-y divide-zinc-200/80 dark:divide-white/[0.06] border-t border-zinc-200/80 dark:border-white/[0.06]">
            {c.excludedApps.map((procName) => (
              <div key={procName} className="py-2 flex items-center justify-between text-[12.5px]">
                <span className="font-mono-tabular text-zinc-800 dark:text-zinc-200">
                  {procName}
                </span>
                <button
                  type="button"
                  onClick={() =>
                    updateSettingsSection('clipboard', {
                      excludedApps: c.excludedApps.filter((a) => a !== procName),
                    })
                  }
                  className="p-1 rounded-md text-zinc-400 hover:text-rose-500 hover:bg-zinc-200/60 dark:hover:bg-white/[0.06] cursor-pointer"
                  aria-label={`Remove ${procName}`}
                >
                  <X className="w-3.5 h-3.5" />
                </button>
              </div>
            ))}
          </div>
        </div>
      </div>
    );
  }

  if (section === 'notifications') {
    const n = settings.notifications;
    const apps = Object.keys(n.enabledApps) as NotificationItem['appName'][];
    return (
      <div className="flex flex-col">
        <SettingRow
          title="Intercept desktop toast notifications"
          description="Route alerts through the floating island with unified typography."
        >
          <NovaSwitch
            checked={n.enabled}
            onChange={(v) => updateSettingsSection('notifications', { enabled: v })}
            ariaLabel="Enable notifications"
          />
        </SettingRow>

        <SettingSlider
          title="Notification preview duration"
          value={n.displayDuration}
          min={2}
          max={15}
          unit="s"
          onChange={(v) => updateSettingsSection('notifications', { displayDuration: v })}
        />

        <SettingRow
          title="Sync with Windows Focus Assist / DND"
          description="Automatically silence non-priority banners when Focus Assist is active."
        >
          <NovaSwitch
            checked={n.syncWindowsDnd}
            onChange={(v) => updateSettingsSection('notifications', { syncWindowsDnd: v })}
            ariaLabel="Sync Windows DND"
          />
        </SettingRow>

        <SettingRow
          title="Show message body previews"
          description="Hide notification body text until hovered when disabled."
        >
          <NovaSwitch
            checked={n.showPreviews}
            onChange={(v) => updateSettingsSection('notifications', { showPreviews: v })}
            ariaLabel="Show previews"
          />
        </SettingRow>

        <div className="pt-4">
          <div className="text-[13px] font-semibold text-zinc-800 dark:text-zinc-200 mb-2">
            Per-application filters
          </div>
          <div className="divide-y divide-zinc-200/80 dark:divide-white/[0.06]">
            {apps.map((app) => (
              <div key={app} className="py-2.5 flex items-center justify-between">
                <span className="text-[13px] text-zinc-800 dark:text-zinc-200">{app}</span>
                <NovaSwitch
                  checked={n.enabledApps[app]}
                  onChange={(v) =>
                    updateSettingsSection('notifications', {
                      enabledApps: { ...n.enabledApps, [app]: v },
                    })
                  }
                  ariaLabel={`Toggle ${app} notifications`}
                />
              </div>
            ))}
          </div>
        </div>
      </div>
    );
  }

  if (section === 'widgets') {
    const w = settings.widgets;
    const items: {
      key: keyof Omit<typeof w, 'refreshIntervalMs'>;
      label: string;
      desc: string;
    }[] = [
      { key: 'battery', label: 'Battery & AC Charging', desc: 'Power percentage and adapter status' },
      { key: 'volume', label: 'Master Volume Slider', desc: 'Output level and mute toggle' },
      { key: 'brightness', label: 'Display Brightness', desc: 'Simulated screen luminance dimming slider' },
      { key: 'wifi', label: 'Wi-Fi Network', desc: 'SSID indicator and quick connection toggle' },
      { key: 'audioDevices', label: 'Audio Output Devices', desc: 'Switch between speakers, headphones, and USB DACs' },
      { key: 'microphone', label: 'Microphone Indicator', desc: 'Hardware input privacy mute toggle' },
      { key: 'camera', label: 'Camera Privacy Status', desc: 'Active webcam capture indicator' },
      { key: 'cpu', label: 'CPU Utilization', desc: 'Simulated real-time processor load bar' },
      { key: 'ram', label: 'Memory Usage', desc: 'Simulated committed RAM in GB and percentage' },
      { key: 'downloads', label: 'Browser Downloads', desc: 'Live download progress in Activity tab' },
      { key: 'transfers', label: 'File Transfers', desc: 'Local and network copy progress' },
      { key: 'screenshots', label: 'Screenshot Capture Alerts', desc: 'Instant preview with copy/save actions' },
    ];

    return (
      <div className="flex flex-col">
        <SettingRow
          title="Telemetry polling interval"
          description="Frequency for simulated CPU, RAM, and battery sensor updates."
        >
          <div className={SEGMENT_WRAP_CLS}>
            {([1000, 2000, 5000] as const).map((ms) => (
              <button
                key={ms}
                type="button"
                onClick={() => updateSettingsSection('widgets', { refreshIntervalMs: ms })}
                className={`${segmentBtn(w.refreshIntervalMs === ms)} font-mono-tabular`}
              >
                {ms / 1000}s
              </button>
            ))}
          </div>
        </SettingRow>

        {items.map((item) => (
          <SettingRow key={item.key} title={item.label} description={item.desc}>
            <NovaSwitch
              checked={w[item.key]}
              onChange={(v) => updateSettingsSection('widgets', { [item.key]: v })}
              ariaLabel={`Toggle ${item.label}`}
            />
          </SettingRow>
        ))}
      </div>
    );
  }

  if (section === 'shortcuts') {
    return (
      <div className="flex flex-col">
        <div className="flex items-center justify-between pb-3 mb-1 border-b border-zinc-200/80 dark:border-white/[0.06]">
          <p className="text-[12px] text-zinc-500 dark:text-zinc-400">
            Click any shortcut button and press a new key combination. Press Esc to cancel.
          </p>
          <button
            type="button"
            onClick={resetShortcuts}
            className="flex items-center gap-1.5 px-2.5 py-1 rounded-md text-[12px] text-zinc-700 dark:text-zinc-300 bg-zinc-200/70 hover:bg-zinc-300/70 dark:bg-white/[0.06] dark:hover:bg-white/[0.12] transition-colors cursor-pointer whitespace-nowrap"
          >
            <RotateCcw className="w-3.5 h-3.5" />
            <span>Reset Defaults</span>
          </button>
        </div>

        {settings.shortcuts.map((sc) => {
          const isRecording = recordingId === sc.id;
          return (
            <SettingRow key={sc.id} title={sc.label} description={sc.description}>
              <button
                type="button"
                onClick={() => setRecordingId(isRecording ? null : sc.id)}
                className={`flex items-center gap-2 px-3 py-1.5 rounded-lg border font-mono-tabular text-[12px] transition-colors cursor-pointer whitespace-nowrap ${
                  isRecording
                    ? 'border-zinc-900 bg-zinc-900 text-white dark:border-white/50 dark:bg-white/[0.14] dark:text-white'
                    : 'border-zinc-300 bg-zinc-100 text-zinc-800 hover:bg-zinc-200/80 dark:border-white/[0.11] dark:bg-white/[0.04] dark:text-zinc-200 dark:hover:bg-white/[0.09]'
                }`}
              >
                <Keyboard className="w-3.5 h-3.5 opacity-70" />
                <span>{isRecording ? 'Press keys...' : sc.keys}</span>
              </button>
            </SettingRow>
          );
        })}
      </div>
    );
  }

  if (section === 'privacy') {
    const p = settings.privacy;
    return (
      <div className="flex flex-col">
        <SettingRow
          title="Local-only processing mode"
          description="Keep clipboard entries, preferences, and notification history strictly on this device."
        >
          <NovaSwitch
            checked={p.localOnlyMode}
            onChange={(v) => updateSettingsSection('privacy', { localOnlyMode: v })}
            ariaLabel="Local only mode"
          />
        </SettingRow>

        <SettingRow
          title="Pause clipboard capture in private browser windows"
          description="Suspend recording while InPrivate or Incognito windows have focus."
        >
          <NovaSwitch
            checked={p.pauseInPrivateBrowsing}
            onChange={(v) => updateSettingsSection('privacy', { pauseInPrivateBrowsing: v })}
            ariaLabel="Pause in private browsing"
          />
        </SettingRow>

        <SettingRow
          title="Mask sensitive previews when workstation is locked"
          description="Hide notification messages and clipboard thumbnails on the lock screen."
        >
          <NovaSwitch
            checked={p.maskWhenLocked}
            onChange={(v) => updateSettingsSection('privacy', { maskWhenLocked: v })}
            ariaLabel="Mask when locked"
          />
        </SettingRow>

        <SettingRow
          title="Local diagnostic logging"
          description="Planned local log file path for native WPF builds."
        >
          <NovaSwitch
            checked={p.diagnosticLogs}
            onChange={(v) => updateSettingsSection('privacy', { diagnosticLogs: v })}
            ariaLabel="Diagnostic logs"
          />
        </SettingRow>
      </div>
    );
  }

  if (section === 'storage') {
    const st = settings.storage;
    const total = st.albumCacheMb + st.lyricsCacheMb + st.clipboardImagesMb + st.configDbMb;
    const buckets: {
      key: keyof typeof st;
      label: string;
      desc: string;
      size: number;
      color: string;
    }[] = [
      {
        key: 'albumCacheMb',
        label: 'Album Artwork Cache',
        desc: 'Simulated cover thumbnails cached locally',
        size: st.albumCacheMb,
        color: 'var(--nova-accent)',
      },
      {
        key: 'clipboardImagesMb',
        label: 'Clipboard Image Previews',
        desc: 'Simulated PNG screenshots and copied bitmaps',
        size: st.clipboardImagesMb,
        color: '#a1a1aa',
      },
      {
        key: 'lyricsCacheMb',
        label: 'Synchronized Lyrics Cache',
        desc: 'Offline .lrc timestamps retrieved from LRCLIB',
        size: st.lyricsCacheMb,
        color: '#34d399',
      },
      {
        key: 'configDbMb',
        label: 'Settings & Profiles (Local Storage)',
        desc: `Persisted under key "${APP_CONFIG.storageKey}"`,
        size: st.configDbMb,
        color: '#71717a',
      },
    ];

    return (
      <div className="flex flex-col gap-5">
        <div className="p-4 rounded-xl bg-zinc-100/80 dark:bg-white/[0.03] border border-zinc-200/90 dark:border-white/[0.07] flex flex-col gap-3">
          <div className="flex items-baseline justify-between">
            <span className="text-[13px] font-medium text-zinc-800 dark:text-zinc-200">
              Simulated Local Footprint
            </span>
            <span className="font-mono-tabular text-[14px] font-semibold text-zinc-900 dark:text-white">
              {total.toFixed(1)} MB
            </span>
          </div>
          <div className="h-2 w-full rounded-full bg-zinc-200 dark:bg-white/[0.07] overflow-hidden flex">
            {buckets.map((b) => (
              <div
                key={b.key}
                style={{
                  width: total > 0 ? `${(b.size / total) * 100}%` : '0%',
                  backgroundColor: b.color,
                }}
                className="h-full transition-[width] duration-300"
              />
            ))}
          </div>
        </div>

        <div className="flex flex-col">
          {buckets.map((b) => (
            <SettingRow key={b.key} title={b.label} description={b.desc}>
              <span className="font-mono-tabular text-[12px] text-zinc-500 dark:text-zinc-400">
                {b.size.toFixed(1)} MB
              </span>
              <button
                type="button"
                onClick={() => clearStorageBucket(b.key)}
                disabled={b.size === 0}
                className="flex items-center gap-1 px-2.5 py-1 rounded-md text-[11.5px] font-medium bg-zinc-200/80 hover:bg-zinc-300/80 dark:bg-white/[0.06] dark:hover:bg-white/[0.12] disabled:opacity-40 text-zinc-800 dark:text-zinc-200 transition-colors cursor-pointer"
              >
                <Trash2 className="w-3 h-3" />
                <span>Clear</span>
              </button>
            </SettingRow>
          ))}
        </div>

        <div className="pt-2 flex flex-wrap items-center gap-2.5">
          <button
            type="button"
            onClick={handleExportSettings}
            className="flex items-center gap-1.5 px-3.5 py-2 rounded-lg text-[12.5px] font-medium bg-zinc-900 text-white hover:bg-zinc-800 dark:bg-white/[0.09] dark:hover:bg-white/[0.16] transition-colors cursor-pointer"
          >
            <Download className="w-3.5 h-3.5" />
            <span>Export Settings JSON</span>
          </button>
          <button
            type="button"
            onClick={resetAllSettings}
            className="flex items-center gap-1.5 px-3.5 py-2 rounded-lg text-[12.5px] font-medium bg-rose-500/10 hover:bg-rose-500/20 text-rose-600 dark:text-rose-300 border border-rose-500/25 transition-colors cursor-pointer"
          >
            <RotateCcw className="w-3.5 h-3.5" />
            <span>Reset All Preferences</span>
          </button>
        </div>
      </div>
    );
  }

  // ABOUT SECTION
  return (
    <div className="flex flex-col gap-6">
      <div className="p-5 rounded-2xl bg-zinc-100/80 dark:bg-white/[0.03] border border-zinc-200/90 dark:border-white/[0.08] flex flex-col sm:flex-row sm:items-center justify-between gap-4">
        <div className="flex items-center gap-4">
          <div className="w-12 h-12 rounded-2xl bg-black border border-white/15 flex items-center justify-center shadow-md shrink-0">
            <span
              className="w-4 h-1.5 rounded-full"
              style={{ backgroundColor: 'var(--nova-accent)' }}
            />
          </div>
          <div>
            <h3 className="text-[18px] font-semibold text-zinc-900 dark:text-white tracking-tight">
              {appName}
            </h3>
            <p className="text-[12.5px] text-zinc-500 dark:text-zinc-400 mt-0.5">
              macOS-Inspired Windows Dynamic Island · Interactive UI Prototype
            </p>
            <p className="font-mono-tabular text-[11.5px] text-zinc-500 mt-1">
              Version {APP_CONFIG.version} · {APP_CONFIG.targetPlatform}
            </p>
          </div>
        </div>

        <button
          type="button"
          onClick={() => {
            setCheckingUpdate(true);
            setUpdateResult(null);
            window.setTimeout(() => {
              setCheckingUpdate(false);
              setUpdateResult(`${appName} ${APP_CONFIG.version} is up to date.`);
            }, 600);
          }}
          className="flex items-center gap-2 px-3.5 py-2 rounded-lg text-[12.5px] font-medium bg-zinc-900 text-white hover:bg-zinc-800 dark:bg-white/[0.09] dark:hover:bg-white/[0.16] transition-colors whitespace-nowrap shrink-0 cursor-pointer"
        >
          <RefreshCw className={`w-3.5 h-3.5 ${checkingUpdate ? 'animate-spin' : ''}`} />
          <span>{checkingUpdate ? 'Checking...' : 'Check for Updates'}</span>
        </button>
      </div>

      {updateResult && (
        <div className="flex items-center gap-2 text-[12.5px] text-emerald-600 dark:text-emerald-400">
          <CheckCircle2 className="w-4 h-4" />
          <span>{updateResult}</span>
        </div>
      )}

      <div className="divide-y divide-zinc-200/80 dark:divide-white/[0.06] border-t border-zinc-200/80 dark:border-white/[0.06]">
        <div className="py-3 flex items-center justify-between text-[13px]">
          <span className="text-zinc-500 dark:text-zinc-400">Planned Native Port</span>
          <span className="font-mono-tabular text-zinc-900 dark:text-zinc-200">
            C# WPF (.NET 9 MVVM · Antigravity)
          </span>
        </div>
        <div className="py-3 flex items-center justify-between text-[13px]">
          <span className="text-zinc-500 dark:text-zinc-400">Configurable Brand Source</span>
          <span className="font-mono-tabular text-zinc-900 dark:text-zinc-200">
            src/config/appConfig.ts & General Settings
          </span>
        </div>
        <div className="py-3 flex items-center justify-between text-[13px]">
          <span className="text-zinc-500 dark:text-zinc-400">Persistence Engine</span>
          <span className="font-mono-tabular text-zinc-900 dark:text-zinc-200">
            Browser localStorage (Prototype)
          </span>
        </div>
        <div className="py-3 flex items-center justify-between text-[13px]">
          <span className="text-zinc-500 dark:text-zinc-400">OS & API Integrations</span>
          <span className="font-mono-tabular text-zinc-900 dark:text-zinc-200">
            Local Mock Simulation Only
          </span>
        </div>
      </div>

      <p className="text-[12px] text-zinc-500 dark:text-zinc-400 leading-relaxed">
        Note: This application is an interactive frontend UI prototype built to finalize layout, animations, theming, and user flows prior to native C# WPF implementation. All system sensors, Spotify playback, LRCLIB lookups, and Windows notification hooks are simulated locally.
      </p>
    </div>
  );
}
