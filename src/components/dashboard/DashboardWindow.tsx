import React from 'react';
import {
  Activity,
  ArrowDownToLine,
  ArrowUpRight,
  Bell,
  Camera,
  Check,
  ClipboardList,
  Copy,
  Disc3,
  FolderSync,
  LayoutGrid,
  Maximize2,
  MicVocal,
  Move,
  Pause,
  Pin,
  Play,
  Settings,
  SkipBack,
  SkipForward,
  SlidersHorizontal,
  Timer,
} from 'lucide-react';
import { formatTime, useNova } from '../../context/NovaContext';
import type { IslandMode } from '../../types/island';
import { DesktopWindow } from '../desktop/DesktopWindow';
import { NovaSwitch, SafeMediaImage } from '../ui/Controls';
import { AppBrandIcon } from '../island/NotificationsPanel';

const ISLAND_PREVIEW_MODES: { id: IslandMode; label: string }[] = [
  { id: 'compact', label: 'Compact' },
  { id: 'music', label: 'Music' },
  { id: 'expanded', label: 'Expanded' },
  { id: 'lyrics', label: 'Lyrics' },
  { id: 'clipboard', label: 'Clipboard' },
  { id: 'notifications', label: 'Notifications' },
  { id: 'timer', label: 'Timer' },
  { id: 'system', label: 'System' },
  { id: 'activity', label: 'Activity' },
];

export function DashboardWindow() {
  const {
    appName,
    islandMode,
    setIslandMode,
    openWindows,
    activeWindow,
    focusWindow,
    closeWindow,
    openWindow,
    setFullscreenLyricsOpen,
    currentTrack,
    isPlaying,
    currentTime,
    togglePlay,
    nextTrack,
    prevTrack,
    seekTo,
    clipboardItems,
    copiedId,
    copyClipboardItem,
    togglePinClipboard,
    activities,
    notifications,
    simulateDownload,
    simulateTransfer,
    simulateScreenshot,
    simulateIncomingNotification,
    system,
    settings,
    updateSettingsSection,
  } = useNova();

  if (!openWindows.dashboard) return null;

  const progressPct =
    currentTrack.duration > 0 ? Math.min(100, (currentTime / currentTrack.duration) * 100) : 0;

  const activeAudio =
    system.audioDevices.find((d) => d.id === system.activeAudioDeviceId) ?? system.audioDevices[0];

  return (
    <DesktopWindow
      id="dashboard"
      title={appName}
      subtitle="Desktop Control Hub"
      icon={<LayoutGrid className="w-4 h-4" />}
      isActive={activeWindow === 'dashboard'}
      onFocus={() => focusWindow('dashboard')}
      onClose={() => closeWindow('dashboard')}
      defaultWidth={1060}
      defaultHeight={650}
      offsetX={-12}
      offsetY={4}
    >
      <div className="flex-1 overflow-y-auto nova-scroll p-6 lg:p-7 flex flex-col gap-7">
        {/* Top Row: Workspace Heading + Direct Settings Launchers */}
        <div className="flex flex-col lg:flex-row lg:items-end justify-between gap-4 pb-5 border-b border-zinc-200/80 dark:border-white/[0.07]">
          <div>
            <h1 className="text-[22px] font-semibold tracking-tight text-zinc-900 dark:text-white leading-tight">
              {appName} Dashboard
            </h1>
            <p className="text-[13px] text-zinc-500 dark:text-zinc-400 mt-1">
              Interactive desktop UI prototype · Local mock state synced with the live floating island.
            </p>
          </div>

          <div className="flex items-center gap-2 shrink-0">
            <button
              type="button"
              onClick={() => openWindow('settings', 'position')}
              className="flex items-center gap-1.5 px-3 py-2 rounded-lg text-[12.5px] font-medium text-zinc-700 dark:text-zinc-200 bg-zinc-100 hover:bg-zinc-200/80 dark:bg-white/[0.05] dark:hover:bg-white/[0.10] border border-zinc-200/90 dark:border-white/[0.07] transition-colors whitespace-nowrap cursor-pointer"
            >
              <Move className="w-3.5 h-3.5" />
              <span>Positioning</span>
            </button>
            <button
              type="button"
              onClick={() => openWindow('settings', 'appearance')}
              className="flex items-center gap-1.5 px-3.5 py-2 rounded-lg text-[12.5px] font-medium text-white bg-zinc-900 hover:bg-zinc-800 dark:bg-white/[0.11] dark:hover:bg-white/[0.17] border border-transparent dark:border-white/[0.08] transition-colors whitespace-nowrap cursor-pointer"
            >
              <Settings className="w-3.5 h-3.5" />
              <span>Customize Island</span>
            </button>
          </div>
        </div>

        {/* Interactive Island State Bar */}
        <section className="flex flex-col gap-3">
          <div className="flex flex-wrap items-center justify-between gap-2">
            <h2 className="text-[13px] font-semibold text-zinc-800 dark:text-zinc-200">
              Active Island State
            </h2>
            <div className="flex flex-wrap items-center gap-1.5 text-[12px] text-zinc-500 dark:text-zinc-400">
              <span className="mr-0.5">Trigger event:</span>
              <button
                type="button"
                onClick={simulateDownload}
                className="flex items-center gap-1 px-2.5 py-1 rounded-md bg-zinc-100 hover:bg-zinc-200/80 dark:bg-white/[0.05] dark:hover:bg-white/[0.11] text-zinc-700 dark:text-zinc-200 transition-colors whitespace-nowrap cursor-pointer"
              >
                <ArrowDownToLine className="w-3 h-3" />
                <span>Download</span>
              </button>
              <button
                type="button"
                onClick={simulateTransfer}
                className="flex items-center gap-1 px-2.5 py-1 rounded-md bg-zinc-100 hover:bg-zinc-200/80 dark:bg-white/[0.05] dark:hover:bg-white/[0.11] text-zinc-700 dark:text-zinc-200 transition-colors whitespace-nowrap cursor-pointer"
              >
                <FolderSync className="w-3 h-3" />
                <span>File Transfer</span>
              </button>
              <button
                type="button"
                onClick={simulateScreenshot}
                className="flex items-center gap-1 px-2.5 py-1 rounded-md bg-zinc-100 hover:bg-zinc-200/80 dark:bg-white/[0.05] dark:hover:bg-white/[0.11] text-zinc-700 dark:text-zinc-200 transition-colors whitespace-nowrap cursor-pointer"
              >
                <Camera className="w-3 h-3" />
                <span>Screenshot</span>
              </button>
              <button
                type="button"
                onClick={simulateIncomingNotification}
                className="flex items-center gap-1 px-2.5 py-1 rounded-md bg-zinc-100 hover:bg-zinc-200/80 dark:bg-white/[0.05] dark:hover:bg-white/[0.11] text-zinc-700 dark:text-zinc-200 transition-colors whitespace-nowrap cursor-pointer"
              >
                <Bell className="w-3 h-3" />
                <span>Notification</span>
              </button>
            </div>
          </div>

          <div className="flex items-center gap-1 p-1 rounded-xl bg-zinc-100/90 dark:bg-white/[0.03] border border-zinc-200/80 dark:border-white/[0.07] overflow-x-auto nova-scroll">
            {ISLAND_PREVIEW_MODES.map((m) => (
              <button
                key={m.id}
                type="button"
                onClick={() => setIslandMode(m.id)}
                className={`flex-1 min-w-[82px] px-3 py-2 rounded-lg text-[12px] font-medium transition-colors whitespace-nowrap cursor-pointer ${
                  islandMode === m.id
                    ? 'bg-white text-zinc-900 shadow-sm dark:bg-white/[0.13] dark:text-white'
                    : 'text-zinc-600 dark:text-zinc-400 hover:text-zinc-900 dark:hover:text-zinc-200 hover:bg-white/60 dark:hover:bg-white/[0.05]'
                }`}
              >
                {m.label}
              </button>
            ))}
          </div>
        </section>

        {/* Primary 2-Column Overview Grid: Now Playing + Quick Toggles / Connections */}
        <div className="grid grid-cols-1 lg:grid-cols-12 gap-6 items-stretch">
          {/* Currently Playing Track Card */}
          <section className="lg:col-span-7 rounded-2xl bg-white dark:bg-white/[0.025] border border-zinc-200/90 dark:border-white/[0.07] p-5 flex flex-col justify-between gap-5">
            <div className="flex items-center justify-between gap-3">
              <div className="flex items-center gap-2 text-[12px] text-zinc-500 dark:text-zinc-400">
                <Disc3
                  className="w-4 h-4"
                  style={{ color: settings.spotify.connected ? '#1DB954' : undefined }}
                />
                <span className="font-medium text-zinc-800 dark:text-zinc-200">
                  Currently Playing
                </span>
                <span aria-hidden="true">·</span>
                <span>{settings.spotify.connected ? 'Spotify Mock' : 'Local Audio'}</span>
                <span aria-hidden="true">·</span>
                <span>{currentTrack.lyricsSource}</span>
              </div>

              <div className="flex items-center gap-1.5">
                <button
                  type="button"
                  onClick={() => setIslandMode('lyrics')}
                  className="flex items-center gap-1 px-2.5 py-1 rounded-md text-[11.5px] font-medium text-zinc-700 dark:text-zinc-300 bg-zinc-100 hover:bg-zinc-200/80 dark:bg-white/[0.06] dark:hover:bg-white/[0.12] dark:hover:text-white transition-colors whitespace-nowrap cursor-pointer"
                >
                  <MicVocal className="w-3.5 h-3.5" />
                  <span>Island Lyrics</span>
                </button>
                <button
                  type="button"
                  onClick={() => setFullscreenLyricsOpen(true)}
                  className="flex items-center gap-1 px-2.5 py-1 rounded-md text-[11.5px] font-medium text-zinc-700 dark:text-zinc-300 bg-zinc-100 hover:bg-zinc-200/80 dark:bg-white/[0.06] dark:hover:bg-white/[0.12] dark:hover:text-white transition-colors whitespace-nowrap cursor-pointer"
                >
                  <Maximize2 className="w-3.5 h-3.5" />
                  <span>Fullscreen</span>
                </button>
              </div>
            </div>

            <div className="flex items-center gap-4">
              <SafeMediaImage
                src={currentTrack.artwork}
                alt={currentTrack.title}
                className="w-20 h-20 rounded-xl object-cover shrink-0 border border-zinc-200 dark:border-white/10 shadow-md"
              />
              <div className="min-w-0 flex-1">
                <h3 className="text-[17px] font-semibold text-zinc-900 dark:text-white truncate">
                  {currentTrack.title}
                </h3>
                <p className="text-[13px] text-zinc-600 dark:text-zinc-300 truncate mt-0.5">
                  {currentTrack.artist}
                </p>
                <p className="text-[12px] text-zinc-500 truncate mt-0.5">
                  {currentTrack.album} · {settings.spotify.activeDevice}
                </p>
              </div>
              <div className="flex items-center gap-1 shrink-0">
                <button
                  type="button"
                  onClick={prevTrack}
                  className="p-2 rounded-lg text-zinc-600 dark:text-zinc-300 hover:text-zinc-900 dark:hover:text-white hover:bg-zinc-100 dark:hover:bg-white/[0.08] cursor-pointer"
                  aria-label="Previous track"
                >
                  <SkipBack className="w-4 h-4 fill-current" />
                </button>
                <button
                  type="button"
                  onClick={togglePlay}
                  className="w-10 h-10 rounded-full bg-zinc-950 dark:bg-white text-white dark:text-zinc-950 flex items-center justify-center hover:scale-[1.04] transition-transform cursor-pointer"
                  aria-label={isPlaying ? 'Pause' : 'Play'}
                >
                  {isPlaying ? (
                    <Pause className="w-4 h-4 fill-current" />
                  ) : (
                    <Play className="w-4 h-4 fill-current ml-0.5" />
                  )}
                </button>
                <button
                  type="button"
                  onClick={nextTrack}
                  className="p-2 rounded-lg text-zinc-600 dark:text-zinc-300 hover:text-zinc-900 dark:hover:text-white hover:bg-zinc-100 dark:hover:bg-white/[0.08] cursor-pointer"
                  aria-label="Next track"
                >
                  <SkipForward className="w-4 h-4 fill-current" />
                </button>
              </div>
            </div>

            <div className="flex flex-col gap-1.5">
              <div className="relative flex items-center">
                <div className="w-full h-1.5 rounded-full bg-zinc-200 dark:bg-white/[0.09] overflow-hidden">
                  <div
                    className="h-full rounded-full transition-[width] duration-200"
                    style={{
                      width: `${progressPct}%`,
                      backgroundColor: 'var(--nova-accent)',
                    }}
                  />
                </div>
                <input
                  type="range"
                  min={0}
                  max={currentTrack.duration}
                  value={currentTime}
                  onChange={(e) => seekTo(Number(e.target.value))}
                  aria-label="Track progress"
                  className="absolute inset-0 w-full h-full opacity-0 cursor-pointer"
                />
              </div>
              <div className="flex justify-between font-mono-tabular text-[11px] text-zinc-500 dark:text-zinc-400">
                <span>{formatTime(currentTime)}</span>
                <span>{formatTime(currentTrack.duration)}</span>
              </div>
            </div>
          </section>

          {/* Quick Toggles & Mock Service Bridges */}
          <section className="lg:col-span-5 rounded-2xl bg-white dark:bg-white/[0.025] border border-zinc-200/90 dark:border-white/[0.07] p-5 flex flex-col justify-between gap-4">
            <div className="flex items-center justify-between">
              <h2 className="text-[13.5px] font-semibold text-zinc-900 dark:text-white">
                Quick Toggles & Simulated Bridges
              </h2>
              <button
                type="button"
                onClick={() => openWindow('settings', 'general')}
                className="text-[11.5px] text-zinc-500 dark:text-zinc-400 hover:text-zinc-900 dark:hover:text-white transition-colors cursor-pointer"
              >
                All Settings
              </button>
            </div>

            <div className="divide-y divide-zinc-200/80 dark:divide-white/[0.06]">
              <div className="py-2.5 flex items-center justify-between gap-3">
                <div>
                  <div className="text-[13px] font-medium text-zinc-900 dark:text-zinc-100">
                    Dynamic Island Visible
                  </div>
                  <div className="text-[11.5px] text-zinc-500 dark:text-zinc-400">
                    Anchor: {settings.position.screenPosition.replace('-', ' ')}
                    {settings.position.screenPosition === 'custom'
                      ? ` (${settings.position.customX}%, ${settings.position.customY}px)`
                      : ''}
                  </div>
                </div>
                <NovaSwitch
                  checked={settings.general.islandEnabled}
                  onChange={(v) => updateSettingsSection('general', { islandEnabled: v })}
                  ariaLabel="Toggle Dynamic Island visibility"
                />
              </div>

              <div className="py-2.5 flex items-center justify-between gap-3">
                <div>
                  <div className="text-[13px] font-medium text-zinc-900 dark:text-zinc-100">
                    Spotify Session (Simulated)
                  </div>
                  <div className="text-[11.5px] text-zinc-500 dark:text-zinc-400">
                    {settings.spotify.connected
                      ? `${settings.spotify.accountEmail} · Connected`
                      : 'Disconnected'}
                  </div>
                </div>
                <NovaSwitch
                  checked={settings.spotify.connected}
                  onChange={(v) => updateSettingsSection('spotify', { connected: v })}
                  ariaLabel="Toggle Spotify mock connection"
                />
              </div>

              <div className="py-2.5 flex items-center justify-between gap-3">
                <div>
                  <div className="text-[13px] font-medium text-zinc-900 dark:text-zinc-100">
                    LRCLIB Lyrics Fallback
                  </div>
                  <div className="text-[11.5px] text-zinc-500 dark:text-zinc-400">
                    Local mock LRC timestamp matching
                  </div>
                </div>
                <NovaSwitch
                  checked={settings.lyrics.lrclibFallback}
                  onChange={(v) => updateSettingsSection('lyrics', { lrclibFallback: v })}
                  ariaLabel="Toggle LRCLIB lyrics fallback"
                />
              </div>

              <div className="py-2.5 flex items-center justify-between gap-3">
                <div>
                  <div className="text-[13px] font-medium text-zinc-900 dark:text-zinc-100">
                    Clipboard History Monitor
                  </div>
                  <div className="text-[11.5px] text-zinc-500 dark:text-zinc-400">
                    {clipboardItems.length} / {settings.clipboard.historyLimit} mock entries
                  </div>
                </div>
                <NovaSwitch
                  checked={settings.clipboard.enabled}
                  onChange={(v) => updateSettingsSection('clipboard', { enabled: v })}
                  ariaLabel="Toggle clipboard monitoring"
                />
              </div>

              <div className="py-2.5 flex items-center justify-between gap-3">
                <div>
                  <div className="text-[13px] font-medium text-zinc-900 dark:text-zinc-100">
                    Focus / Do Not Disturb
                  </div>
                  <div className="text-[11.5px] text-zinc-500 dark:text-zinc-400">
                    Suppress notification counts in compact pill
                  </div>
                </div>
                <NovaSwitch
                  checked={settings.general.focusDndMode}
                  onChange={(v) => updateSettingsSection('general', { focusDndMode: v })}
                  ariaLabel="Toggle Focus DND mode"
                />
              </div>
            </div>
          </section>
        </div>

        {/* Feature Overview Strip (4 Clean Launchers) */}
        <section className="grid grid-cols-2 lg:grid-cols-4 gap-4">
          {[
            {
              title: 'System & Audio',
              value: `CPU ${system.cpuUsage}% · RAM ${system.ramUsage}%`,
              meta: `${activeAudio?.name ?? 'Audio'} · ${system.batteryLevel}%`,
              icon: SlidersHorizontal,
              target: 'system' as IslandMode,
            },
            {
              title: 'Focus Timer',
              value: 'Pomodoro & Stopwatch',
              meta: 'Animated ring · Live controls',
              icon: Timer,
              target: 'timer' as IslandMode,
            },
            {
              title: 'Clipboard Vault',
              value: `${clipboardItems.length} Saved Items`,
              meta: `${clipboardItems.filter((i) => i.pinned).length} pinned · Sensitive filter on`,
              icon: ClipboardList,
              target: 'clipboard' as IslandMode,
            },
            {
              title: 'Activity & Alerts',
              value: `${activities.filter((a) => a.status === 'active').length} Live Tasks`,
              meta: `${notifications.filter((n) => !n.dismissed).length} active notifications`,
              icon: Activity,
              target: 'activity' as IslandMode,
            },
          ].map((item) => {
            const Icon = item.icon;
            return (
              <button
                key={item.title}
                type="button"
                onClick={() => setIslandMode(item.target)}
                className="group text-left rounded-xl bg-white dark:bg-white/[0.025] hover:bg-zinc-50 dark:hover:bg-white/[0.05] border border-zinc-200/90 dark:border-white/[0.07] p-4 transition-colors cursor-pointer flex flex-col gap-2"
              >
                <div className="flex items-center justify-between text-zinc-500 dark:text-zinc-400">
                  <Icon className="w-4 h-4" />
                  <ArrowUpRight className="w-3.5 h-3.5 opacity-0 group-hover:opacity-100 transition-opacity text-zinc-700 dark:text-zinc-300" />
                </div>
                <div className="text-[12px] text-zinc-500 dark:text-zinc-400">{item.title}</div>
                <div className="text-[14px] font-semibold font-mono-tabular text-zinc-900 dark:text-white truncate">
                  {item.value}
                </div>
                <div className="text-[11.5px] text-zinc-500 truncate">{item.meta}</div>
              </button>
            );
          })}
        </section>

        {/* Bottom 2-Column Split: Recent Clipboard Entries + Recent Activity & Alerts */}
        <div className="grid grid-cols-1 lg:grid-cols-2 gap-6 pb-2">
          {/* Recent Clipboard Entries */}
          <section className="rounded-2xl bg-white dark:bg-white/[0.025] border border-zinc-200/90 dark:border-white/[0.07] p-5 flex flex-col gap-3">
            <div className="flex items-center justify-between">
              <h2 className="text-[13.5px] font-semibold text-zinc-900 dark:text-white">
                Recent Clipboard Entries
              </h2>
              <button
                type="button"
                onClick={() => setIslandMode('clipboard')}
                className="text-[11.5px] text-zinc-500 dark:text-zinc-400 hover:text-zinc-900 dark:hover:text-white transition-colors cursor-pointer"
              >
                Open in Island
              </button>
            </div>

            <div className="divide-y divide-zinc-200/80 dark:divide-white/[0.06]">
              {clipboardItems.slice(0, 4).map((item) => (
                <div key={item.id} className="py-2.5 flex items-center justify-between gap-3">
                  <div className="min-w-0 flex-1">
                    <div className="flex items-center gap-1.5 text-[11px] text-zinc-500 dark:text-zinc-400">
                      <span className="font-medium text-zinc-800 dark:text-zinc-200 truncate">
                        {item.previewTitle || item.type}
                      </span>
                      <span aria-hidden="true">·</span>
                      <span>{item.sourceApp}</span>
                      <span aria-hidden="true">·</span>
                      <span>{item.timestamp}</span>
                    </div>
                    <p className="text-[12px] text-zinc-500 dark:text-zinc-400 truncate mt-0.5 font-mono-tabular">
                      {item.content}
                    </p>
                  </div>
                  <div className="flex items-center gap-1 shrink-0">
                    <button
                      type="button"
                      onClick={() => copyClipboardItem(item)}
                      className="p-1.5 rounded-md text-zinc-500 dark:text-zinc-400 hover:text-zinc-900 dark:hover:text-white hover:bg-zinc-100 dark:hover:bg-white/[0.08] cursor-pointer"
                      aria-label="Copy entry"
                    >
                      {copiedId === item.id ? (
                        <Check className="w-3.5 h-3.5 text-emerald-500" />
                      ) : (
                        <Copy className="w-3.5 h-3.5" />
                      )}
                    </button>
                    <button
                      type="button"
                      onClick={() => togglePinClipboard(item.id)}
                      className={`p-1.5 rounded-md hover:bg-zinc-100 dark:hover:bg-white/[0.08] cursor-pointer ${
                        item.pinned
                          ? 'text-zinc-900 dark:text-white'
                          : 'text-zinc-400 dark:text-zinc-500 hover:text-zinc-800 dark:hover:text-zinc-200'
                      }`}
                      aria-label="Pin entry"
                    >
                      <Pin className="w-3.5 h-3.5" />
                    </button>
                  </div>
                </div>
              ))}
            </div>
          </section>

          {/* Recent Activity & Notifications */}
          <section className="rounded-2xl bg-white dark:bg-white/[0.025] border border-zinc-200/90 dark:border-white/[0.07] p-5 flex flex-col gap-3">
            <div className="flex items-center justify-between">
              <h2 className="text-[13.5px] font-semibold text-zinc-900 dark:text-white">
                Recent Activity & Alerts
              </h2>
              <button
                type="button"
                onClick={() => setIslandMode('activity')}
                className="text-[11.5px] text-zinc-500 dark:text-zinc-400 hover:text-zinc-900 dark:hover:text-white transition-colors cursor-pointer"
              >
                View Live Feed
              </button>
            </div>

            <div className="divide-y divide-zinc-200/80 dark:divide-white/[0.06]">
              {activities.slice(0, 2).map((act) => (
                <div key={act.id} className="py-2.5 flex flex-col gap-1.5">
                  <div className="flex items-center justify-between gap-2 text-[12.5px]">
                    <span className="font-medium text-zinc-900 dark:text-zinc-100 truncate">
                      {act.title}
                    </span>
                    <span className="font-mono-tabular text-[11px] text-zinc-500 dark:text-zinc-400 shrink-0">
                      {act.progress}%
                    </span>
                  </div>
                  <div className="h-1 w-full rounded-full bg-zinc-200 dark:bg-white/[0.08] overflow-hidden">
                    <div
                      className="h-full rounded-full transition-[width] duration-300"
                      style={{
                        width: `${act.progress}%`,
                        backgroundColor:
                          act.status === 'completed' ? '#34d399' : 'var(--nova-accent)',
                      }}
                    />
                  </div>
                </div>
              ))}

              {notifications
                .filter((n) => !n.dismissed)
                .slice(0, 2)
                .map((notif) => (
                  <div key={notif.id} className="py-2.5 flex items-center gap-3">
                    <AppBrandIcon appName={notif.appName} />
                    <div className="min-w-0 flex-1">
                      <div className="flex items-center gap-1.5 text-[11px] text-zinc-500 dark:text-zinc-400">
                        <span className="font-medium text-zinc-800 dark:text-zinc-200">
                          {notif.appName}
                        </span>
                        <span aria-hidden="true">·</span>
                        <span>{notif.timestamp}</span>
                      </div>
                      <div className="text-[12px] text-zinc-600 dark:text-zinc-300 truncate mt-0.5">
                        {notif.body}
                      </div>
                    </div>
                  </div>
                ))}
            </div>
          </section>
        </div>
      </div>
    </DesktopWindow>
  );
}
