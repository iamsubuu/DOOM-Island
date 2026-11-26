import React, { useEffect, useRef, useState } from 'react';
import { AnimatePresence, motion } from 'motion/react';
import {
  Activity,
  Bell,
  ChevronUp,
  ClipboardList,
  Disc3,
  MicVocal,
  Pause,
  Play,
  Settings,
  SlidersHorizontal,
  Timer,
} from 'lucide-react';
import { formatTime, useNova } from '../../context/NovaContext';
import type { IslandMode } from '../../types/island';
import { SafeMediaImage } from '../ui/Controls';
import { ActivityPanel } from './ActivityPanel';
import { AudioEqualizer } from './AudioEqualizer';
import { ClipboardPanel } from './ClipboardPanel';
import { LyricsPanel } from './LyricsPanel';
import { MusicPanel } from './MusicPanel';
import { NotificationsPanel } from './NotificationsPanel';
import { SystemPanel } from './SystemPanel';
import { TimerPanel } from './TimerPanel';

const EXPANDED_TABS: {
  id: IslandMode;
  label: string;
  icon: React.ComponentType<{ className?: string }>;
}[] = [
  { id: 'expanded', label: 'Music', icon: Disc3 },
  { id: 'lyrics', label: 'Lyrics', icon: MicVocal },
  { id: 'clipboard', label: 'Clipboard', icon: ClipboardList },
  { id: 'notifications', label: 'Alerts', icon: Bell },
  { id: 'timer', label: 'Timer', icon: Timer },
  { id: 'system', label: 'System', icon: SlidersHorizontal },
  { id: 'activity', label: 'Activity', icon: Activity },
];

export function DynamicIsland() {
  const {
    appShortName,
    islandMode,
    setIslandMode,
    collapseIsland,
    handleIslandDoubleClick,
    isExpanded,
    notificationPulseKey,
    currentTrack,
    currentTime,
    isPlaying,
    togglePlay,
    unreadCount,
    timer,
    activities,
    settings,
    openWindow,
  } = useNova();

  const containerRef = useRef<HTMLDivElement | null>(null);
  const [isHovered, setIsHovered] = useState(false);
  const [pulseActive, setPulseActive] = useState(false);

  // Subtle arrival bounce when a notification triggers
  useEffect(() => {
    if (notificationPulseKey === 0 || !settings.animation.notificationBounce || settings.animation.reduceMotion) {
      return;
    }
    setPulseActive(true);
    const id = window.setTimeout(() => setPulseActive(false), 320);
    return () => window.clearTimeout(id);
  }, [notificationPulseKey, settings.animation.notificationBounce, settings.animation.reduceMotion]);

  // Outside click auto-collapse when configured
  useEffect(() => {
    if (!isExpanded || settings.general.collapseBehavior !== 'outside-click') return;

    const handlePointerDown = (e: MouseEvent) => {
      if (containerRef.current && !containerRef.current.contains(e.target as Node)) {
        collapseIsland();
      }
    };
    document.addEventListener('mousedown', handlePointerDown);
    return () => document.removeEventListener('mousedown', handlePointerDown);
  }, [isExpanded, settings.general.collapseBehavior, collapseIsland]);

  // Timed auto-collapse when idle & not hovered
  useEffect(() => {
    if (!isExpanded || settings.general.autoCollapseSeconds <= 0 || isHovered) return;
    const id = window.setTimeout(() => {
      collapseIsland();
    }, settings.general.autoCollapseSeconds * 1000);
    return () => window.clearTimeout(id);
  }, [isExpanded, settings.general.autoCollapseSeconds, isHovered, collapseIsland, islandMode]);

  if (!settings.general.islandEnabled) {
    return null;
  }

  const app = settings.appearance;
  const anim = settings.animation;
  const pos = settings.position;

  // Pure black macOS-inspired Dynamic Island material
  const alpha = app.opacity / 100;
  const surfaceBg =
    app.theme === 'pure-black'
      ? `rgba(0, 0, 0, ${alpha})`
      : app.theme === 'obsidian'
      ? `rgba(10, 11, 14, ${alpha})`
      : app.theme === 'graphite'
      ? `rgba(22, 23, 27, ${alpha})`
      : `rgba(0, 0, 0, 1)`;

  const borderStyle =
    app.theme === 'contrast'
      ? '1px solid rgba(255, 255, 255, 0.28)'
      : '1px solid rgba(255, 255, 255, 0.08)';

  // Dimensions calibrated per state to avoid abrupt layout jumps
  const targetWidth =
    islandMode === 'compact'
      ? app.islandWidth
      : islandMode === 'music'
      ? Math.max(app.islandWidth + 124, 344)
      : islandMode === 'system' || islandMode === 'clipboard'
      ? 560
      : 524;

  // Spring physics calibrated by preset & animation speed multiplier
  const speed = Math.max(0.5, app.animationSpeed);
  const shellTransition =
    anim.reduceMotion || anim.preset === 'instant'
      ? { duration: 0.06 }
      : anim.preset === 'snappy'
      ? { type: 'spring' as const, stiffness: Math.round(540 * speed), damping: 38, mass: 0.72 }
      : anim.preset === 'silk'
      ? { type: 'spring' as const, stiffness: Math.round(250 * speed), damping: 29, mass: 0.96 }
      : { type: 'spring' as const, stiffness: Math.round(390 * speed), damping: 33, mass: 0.84 };

  const contentDuration = anim.reduceMotion ? 0.04 : Math.max(0.11, 0.19 / speed);

  const activeTransferCount = activities.filter((a) => a.status === 'active').length;
  const activeTimerText = timer.pomodoroRunning
    ? formatTime(timer.pomodoroRemaining)
    : timer.countdownRunning
    ? formatTime(timer.countdownRemaining)
    : null;

  const isIdle = !activeTimerText && unreadCount === 0 && activeTransferCount === 0;
  const progressPct =
    currentTrack.duration > 0 ? Math.min(100, (currentTime / currentTrack.duration) * 100) : 0;

  // Compute positioning wrapper style (supports top-center, top-left, top-right, custom X/Y)
  const isCustomPos = pos.screenPosition === 'custom';
  const clampedCustomX = Math.max(12, Math.min(88, pos.customX));
  const clampedCustomY = Math.max(8, Math.min(180, pos.customY));

  const wrapperPositionClass = !isCustomPos
    ? pos.screenPosition === 'top-left'
      ? 'justify-start pl-6'
      : pos.screenPosition === 'top-right'
      ? 'justify-end pr-6'
      : 'justify-center'
    : '';

  return (
    <div
      className={`fixed inset-x-0 pointer-events-none px-4 ${
        isCustomPos ? '' : `flex ${wrapperPositionClass}`
      }`}
      style={{
        top: isCustomPos ? `${clampedCustomY}px` : '14px',
        zIndex: settings.general.alwaysOnTop ? 60 : 25,
      }}
    >
      <motion.div
        ref={containerRef}
        layout
        initial={false}
        animate={{
          width: `min(${targetWidth}px, calc(100vw - 32px))`,
          borderRadius: isExpanded
            ? Math.max(22, Math.min(34, app.cornerRadius + 4))
            : Math.max(18, app.cornerRadius),
          scale: pulseActive
            ? 1.032
            : !isExpanded && isHovered && anim.hoverExpand
            ? 1.022
            : 1,
          y: pulseActive ? 2 : 0,
        }}
        transition={shellTransition}
        onMouseEnter={() => setIsHovered(true)}
        onMouseLeave={() => {
          setIsHovered(false);
          if (isExpanded && settings.general.collapseBehavior === 'mouse-leave') {
            collapseIsland();
          }
        }}
        onDoubleClick={handleIslandDoubleClick}
        className="island-dark-scope pointer-events-auto relative overflow-hidden select-none text-zinc-100 will-change-transform"
        style={{
          position: isCustomPos ? 'fixed' : 'relative',
          left: isCustomPos ? `${clampedCustomX}%` : undefined,
          top: isCustomPos ? `${clampedCustomY}px` : undefined,
          marginLeft: isCustomPos ? `calc(min(${targetWidth}px, calc(100vw - 32px)) / -2)` : undefined,
          backgroundColor: surfaceBg,
          backdropFilter: app.blur > 0 ? `blur(${app.blur}px) saturate(135%)` : 'none',
          WebkitBackdropFilter: app.blur > 0 ? `blur(${app.blur}px) saturate(135%)` : 'none',
          border: borderStyle,
          boxShadow: isExpanded
            ? '0 26px 64px -14px rgba(0,0,0,0.78), 0 0 0 1px rgba(255,255,255,0.04) inset'
            : '0 12px 30px -8px rgba(0,0,0,0.65), 0 1px 0 rgba(255,255,255,0.06) inset',
          fontSize: `${app.fontSize}px`,
          fontWeight: app.fontWeight,
        }}
      >
        <AnimatePresence mode="wait" initial={false}>
          {/* COMPACT PILL STATE */}
          {islandMode === 'compact' && (
            <motion.button
              key="compact"
              type="button"
              initial={{ opacity: 0, scale: 0.96 }}
              animate={{ opacity: 1, scale: 1 }}
              exit={{ opacity: 0, scale: 0.96 }}
              transition={{ duration: contentDuration, ease: [0.16, 1, 0.3, 1] }}
              onClick={() => setIslandMode('expanded')}
              className="w-full relative flex items-center justify-between px-4 cursor-pointer focus-visible:outline-none"
              style={{ height: `${app.islandHeight}px` }}
              aria-label={`Expand ${appShortName} Island`}
            >
              <div className="flex items-center gap-2 min-w-0">
                <span
                  className={`w-1.5 h-1.5 rounded-full shrink-0 ${
                    isIdle && anim.idleBreathing && !anim.reduceMotion ? 'nova-idle-breathe' : ''
                  }`}
                  style={{ backgroundColor: 'var(--nova-accent)' }}
                />
                <span className="text-[12px] font-medium text-zinc-100 tracking-tight truncate">
                  {appShortName}
                </span>
              </div>

              <div className="flex items-center gap-2.5 text-[11px] font-mono-tabular text-zinc-400 shrink-0">
                {activeTimerText && <span className="text-zinc-200">{activeTimerText}</span>}
                {unreadCount > 0 && !settings.general.focusDndMode && (
                  <span className="flex items-center gap-1 text-zinc-300">
                    <Bell className="w-3 h-3" />
                    {unreadCount}
                  </span>
                )}
                {activeTransferCount > 0 && (
                  <span className="text-zinc-300">{activeTransferCount} active</span>
                )}
                {isIdle && (
                  <span className="flex items-center gap-1.5 text-zinc-500">
                    {anim.idleBreathing && !anim.reduceMotion && (
                      <span
                        className="inline-block w-5 h-[2px] rounded-full nova-idle-breathe"
                        style={{ backgroundColor: 'var(--nova-accent)' }}
                      />
                    )}
                    <span>Ready</span>
                  </span>
                )}
              </div>
            </motion.button>
          )}

          {/* MUSIC COMPACT PILL STATE */}
          {islandMode === 'music' && (
            <motion.div
              key="music-pill"
              initial={{ opacity: 0, scale: 0.97 }}
              animate={{ opacity: 1, scale: 1 }}
              exit={{ opacity: 0, scale: 0.97 }}
              transition={{ duration: contentDuration, ease: [0.16, 1, 0.3, 1] }}
              onClick={() => setIslandMode('expanded')}
              className="w-full relative flex items-center justify-between gap-3 px-2.5 cursor-pointer"
              style={{ height: `${Math.max(40, app.islandHeight + 4)}px` }}
            >
              <div className="flex items-center gap-2.5 min-w-0 flex-1">
                {settings.music.showAlbumArtInCompact && (
                  <SafeMediaImage
                    src={currentTrack.artwork}
                    alt={currentTrack.title}
                    className="w-6 h-6 rounded-full object-cover shrink-0 border border-white/15"
                  />
                )}
                <div className="min-w-0 flex items-center gap-1.5">
                  <span className="text-[12px] font-medium text-white truncate">
                    {currentTrack.title}
                  </span>
                  <span className="text-[11px] text-zinc-400 truncate hidden sm:inline">
                    · {currentTrack.artist}
                  </span>
                </div>
              </div>

              <div className="flex items-center gap-2.5 shrink-0 pr-1">
                {activeTimerText && (
                  <span className="font-mono-tabular text-[11px] text-zinc-400">
                    {activeTimerText}
                  </span>
                )}

                {settings.music.showAudioVisualizer && (
                  <AudioEqualizer
                    isPlaying={isPlaying}
                    bpm={currentTrack.bpm}
                    bars={5}
                    height={13}
                    reduceMotion={anim.reduceMotion}
                  />
                )}

                <button
                  type="button"
                  onClick={(e) => {
                    e.stopPropagation();
                    togglePlay();
                  }}
                  className="p-1 rounded-full text-zinc-200 hover:bg-white/[0.14] transition-colors cursor-pointer"
                  aria-label={isPlaying ? 'Pause playback' : 'Resume playback'}
                >
                  {isPlaying ? (
                    <Pause className="w-3.5 h-3.5 fill-current" />
                  ) : (
                    <Play className="w-3.5 h-3.5 fill-current" />
                  )}
                </button>
              </div>

              {/* Subtle bottom music progress hairline */}
              {settings.music.showCompactProgress && (
                <div className="absolute bottom-0 inset-x-4 h-[2px] bg-white/[0.06] overflow-hidden rounded-full">
                  <div
                    className="h-full transition-[width] duration-300"
                    style={{
                      width: `${progressPct}%`,
                      backgroundColor: 'var(--nova-accent)',
                    }}
                  />
                </div>
              )}
            </motion.div>
          )}

          {/* EXPANDED MODULAR SHELL */}
          {isExpanded && (
            <motion.div
              key="expanded-shell"
              initial={{ opacity: 0, scale: 0.98, y: -4 }}
              animate={{ opacity: 1, scale: 1, y: 0 }}
              exit={{ opacity: 0, scale: 0.98, y: -4 }}
              transition={{ duration: contentDuration, ease: [0.16, 1, 0.3, 1] }}
              style={{ padding: `${app.spacing}px` }}
              className="flex flex-col gap-3.5"
            >
              {/* Top Module Switcher Bar */}
              <div className="flex items-center justify-between gap-2 pb-2.5 border-b border-white/[0.08]">
                <div className="flex items-center gap-0.5 overflow-x-auto nova-scroll">
                  {EXPANDED_TABS.map((tab) => {
                    const Icon = tab.icon;
                    const active = islandMode === tab.id;
                    return (
                      <button
                        key={tab.id}
                        type="button"
                        onClick={() => setIslandMode(tab.id)}
                        className={`flex items-center gap-1.5 px-2.5 py-1.5 rounded-lg text-[11.5px] font-medium transition-colors whitespace-nowrap shrink-0 cursor-pointer ${
                          active
                            ? 'bg-white/[0.14] text-white'
                            : 'text-zinc-400 hover:text-zinc-200 hover:bg-white/[0.06]'
                        }`}
                      >
                        <Icon className="w-3.5 h-3.5 shrink-0" />
                        <span>{tab.label}</span>
                        {tab.id === 'notifications' && unreadCount > 0 && (
                          <span className="font-mono-tabular text-[10px] text-zinc-300">
                            {unreadCount}
                          </span>
                        )}
                      </button>
                    );
                  })}
                </div>

                <div className="flex items-center gap-0.5 shrink-0">
                  <button
                    type="button"
                    onClick={() => openWindow('settings')}
                    className="p-1.5 rounded-lg text-zinc-400 hover:text-white hover:bg-white/[0.08] transition-colors cursor-pointer"
                    title="Open Settings"
                    aria-label="Open Settings"
                  >
                    <Settings className="w-3.5 h-3.5" />
                  </button>
                  <button
                    type="button"
                    onClick={collapseIsland}
                    className="p-1.5 rounded-lg text-zinc-400 hover:text-white hover:bg-white/[0.08] transition-colors cursor-pointer"
                    title="Collapse Island"
                    aria-label="Collapse Dynamic Island"
                  >
                    <ChevronUp className="w-3.5 h-3.5" />
                  </button>
                </div>
              </div>

              {/* Smooth Inner Panel Crossfade */}
              <AnimatePresence mode="wait" initial={false}>
                <motion.div
                  key={islandMode}
                  initial={{ opacity: 0, y: 4 }}
                  animate={{ opacity: 1, y: 0 }}
                  exit={{ opacity: 0, y: -3 }}
                  transition={{ duration: contentDuration, ease: [0.16, 1, 0.3, 1] }}
                >
                  {islandMode === 'expanded' && <MusicPanel />}
                  {islandMode === 'lyrics' && <LyricsPanel />}
                  {islandMode === 'clipboard' && <ClipboardPanel />}
                  {islandMode === 'notifications' && <NotificationsPanel />}
                  {islandMode === 'timer' && <TimerPanel />}
                  {islandMode === 'system' && <SystemPanel />}
                  {islandMode === 'activity' && <ActivityPanel />}
                </motion.div>
              </AnimatePresence>
            </motion.div>
          )}
        </AnimatePresence>
      </motion.div>
    </div>
  );
}
