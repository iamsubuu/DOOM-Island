import React, { useEffect, useState } from 'react';
import {
  Battery,
  BatteryCharging,
  Bell,
  LayoutGrid,
  MicVocal,
  Settings,
  SlidersHorizontal,
  Volume2,
  VolumeX,
  Wifi,
  WifiOff,
} from 'lucide-react';
import { useNova } from '../../context/NovaContext';
import type { IslandMode } from '../../types/island';

const QUICK_ISLAND_MODES: { mode: IslandMode; label: string }[] = [
  { mode: 'compact', label: 'Compact' },
  { mode: 'music', label: 'Music' },
  { mode: 'expanded', label: 'Expanded' },
  { mode: 'lyrics', label: 'Lyrics' },
  { mode: 'clipboard', label: 'Clipboard' },
  { mode: 'notifications', label: 'Alerts' },
  { mode: 'timer', label: 'Timer' },
  { mode: 'system', label: 'System' },
  { mode: 'activity', label: 'Activity' },
];

export function Taskbar() {
  const {
    appName,
    islandMode,
    setIslandMode,
    openWindows,
    activeWindow,
    toggleWindow,
    setFullscreenLyricsOpen,
    system,
    unreadCount,
  } = useNova();

  const [now, setNow] = useState(() => new Date());

  useEffect(() => {
    const id = window.setInterval(() => setNow(new Date()), 15000);
    return () => window.clearInterval(id);
  }, []);

  const timeString = now.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' });
  const dateString = now.toLocaleDateString([], { month: 'short', day: 'numeric', year: 'numeric' });

  return (
    <footer className="fixed bottom-0 inset-x-0 h-12 z-50 bg-white/88 dark:bg-[#0A0B0F]/90 backdrop-blur-xl border-t border-zinc-200/90 dark:border-white/[0.08] px-4 flex items-center justify-between gap-4 select-none transition-colors">
      {/* Left: Quick Island State Switcher */}
      <div className="hidden xl:flex items-center gap-1 overflow-x-auto nova-scroll">
        <span className="text-[11px] text-zinc-500 mr-1.5 whitespace-nowrap">State</span>
        {QUICK_ISLAND_MODES.map((item) => (
          <button
            key={item.mode}
            type="button"
            onClick={() => setIslandMode(item.mode)}
            className={`px-2 py-1 rounded-md text-[11px] font-medium transition-colors whitespace-nowrap shrink-0 cursor-pointer ${
              islandMode === item.mode
                ? 'bg-zinc-900 text-white dark:bg-white/[0.14] dark:text-white'
                : 'text-zinc-600 dark:text-zinc-400 hover:text-zinc-900 dark:hover:text-zinc-200 hover:bg-zinc-200/60 dark:hover:bg-white/[0.06]'
            }`}
          >
            {item.label}
          </button>
        ))}
      </div>

      {/* Center: Desktop App Dock */}
      <div className="flex items-center gap-1.5 mx-auto xl:mx-0">
        <button
          type="button"
          onClick={() => toggleWindow('dashboard')}
          className={`relative flex items-center gap-2 px-3 py-1.5 rounded-lg text-[12px] font-medium transition-colors whitespace-nowrap cursor-pointer ${
            openWindows.dashboard && activeWindow === 'dashboard'
              ? 'bg-zinc-200/90 text-zinc-900 dark:bg-white/[0.13] dark:text-white'
              : openWindows.dashboard
              ? 'bg-zinc-100 text-zinc-800 hover:bg-zinc-200/70 dark:bg-white/[0.06] dark:text-zinc-200 dark:hover:bg-white/[0.10]'
              : 'text-zinc-600 dark:text-zinc-400 hover:text-zinc-900 dark:hover:text-white hover:bg-zinc-200/60 dark:hover:bg-white/[0.07]'
          }`}
          title={`${appName} Dashboard (Alt + D)`}
        >
          <LayoutGrid className="w-4 h-4" />
          <span>Dashboard</span>
          {openWindows.dashboard && (
            <span
              className="absolute bottom-0.5 left-1/2 -translate-x-1/2 w-3.5 h-[2px] rounded-full"
              style={{ backgroundColor: 'var(--nova-accent)' }}
            />
          )}
        </button>

        <button
          type="button"
          onClick={() => toggleWindow('settings')}
          className={`relative flex items-center gap-2 px-3 py-1.5 rounded-lg text-[12px] font-medium transition-colors whitespace-nowrap cursor-pointer ${
            openWindows.settings && activeWindow === 'settings'
              ? 'bg-zinc-200/90 text-zinc-900 dark:bg-white/[0.13] dark:text-white'
              : openWindows.settings
              ? 'bg-zinc-100 text-zinc-800 hover:bg-zinc-200/70 dark:bg-white/[0.06] dark:text-zinc-200 dark:hover:bg-white/[0.10]'
              : 'text-zinc-600 dark:text-zinc-400 hover:text-zinc-900 dark:hover:text-white hover:bg-zinc-200/60 dark:hover:bg-white/[0.07]'
          }`}
          title={`${appName} Settings (Alt + ,)`}
        >
          <Settings className="w-4 h-4" />
          <span>Settings</span>
          {openWindows.settings && (
            <span
              className="absolute bottom-0.5 left-1/2 -translate-x-1/2 w-3.5 h-[2px] rounded-full"
              style={{ backgroundColor: 'var(--nova-accent)' }}
            />
          )}
        </button>

        <button
          type="button"
          onClick={() => setFullscreenLyricsOpen(true)}
          className="flex items-center gap-2 px-3 py-1.5 rounded-lg text-[12px] font-medium text-zinc-600 dark:text-zinc-400 hover:text-zinc-900 dark:hover:text-white hover:bg-zinc-200/60 dark:hover:bg-white/[0.07] transition-colors whitespace-nowrap cursor-pointer"
          title="Fullscreen Synchronized Lyrics"
        >
          <MicVocal className="w-4 h-4" />
          <span className="hidden sm:inline">Lyrics View</span>
        </button>
      </div>

      {/* Right: System Tray */}
      <div className="flex items-center gap-1.5 shrink-0">
        <button
          type="button"
          onClick={() => setIslandMode(islandMode === 'system' ? 'compact' : 'system')}
          className="flex items-center gap-2.5 px-2.5 py-1.5 rounded-lg text-zinc-700 dark:text-zinc-300 hover:bg-zinc-200/70 dark:hover:bg-white/[0.08] transition-colors cursor-pointer"
          title={`Open System Controls in ${appName}`}
        >
          {system.wifiConnected ? (
            <Wifi className="w-3.5 h-3.5" />
          ) : (
            <WifiOff className="w-3.5 h-3.5 text-zinc-400 dark:text-zinc-500" />
          )}
          {system.isMuted || system.volume === 0 ? (
            <VolumeX className="w-3.5 h-3.5 text-zinc-400 dark:text-zinc-500" />
          ) : (
            <Volume2 className="w-3.5 h-3.5" />
          )}
          <span className="flex items-center gap-1 font-mono-tabular text-[11px]">
            {system.isCharging ? (
              <BatteryCharging className="w-3.5 h-3.5 text-emerald-600 dark:text-emerald-400" />
            ) : (
              <Battery className="w-3.5 h-3.5" />
            )}
            <span className="hidden md:inline">{system.batteryLevel}%</span>
          </span>
        </button>

        <button
          type="button"
          onClick={() =>
            setIslandMode(islandMode === 'notifications' ? 'compact' : 'notifications')
          }
          className="relative p-2 rounded-lg text-zinc-700 dark:text-zinc-300 hover:bg-zinc-200/70 dark:hover:bg-white/[0.08] transition-colors cursor-pointer"
          title="Notifications"
          aria-label="Open notifications"
        >
          <Bell className="w-3.5 h-3.5" />
          {unreadCount > 0 && (
            <span
              className="absolute top-1.5 right-1.5 w-1.5 h-1.5 rounded-full"
              style={{ backgroundColor: 'var(--nova-accent)' }}
            />
          )}
        </button>

        <button
          type="button"
          onClick={() => setIslandMode(islandMode === 'expanded' ? 'compact' : 'expanded')}
          className="hidden sm:flex flex-col items-end px-2 py-1 rounded-lg hover:bg-zinc-200/70 dark:hover:bg-white/[0.08] transition-colors cursor-pointer leading-tight"
          title="Toggle Dynamic Island"
        >
          <span className="font-mono-tabular text-[11.5px] font-medium text-zinc-900 dark:text-zinc-100">
            {timeString}
          </span>
          <span className="font-mono-tabular text-[10px] text-zinc-500 dark:text-zinc-400">
            {dateString}
          </span>
        </button>

        <button
          type="button"
          onClick={() => setIslandMode(islandMode === 'system' ? 'compact' : 'system')}
          className="xl:hidden p-2 rounded-lg text-zinc-700 dark:text-zinc-300 hover:bg-zinc-200/70 dark:hover:bg-white/[0.08] cursor-pointer"
          aria-label="System widgets"
        >
          <SlidersHorizontal className="w-3.5 h-3.5" />
        </button>
      </div>
    </footer>
  );
}
