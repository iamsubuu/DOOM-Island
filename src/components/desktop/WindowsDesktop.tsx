import React, { useState } from 'react';
import { AnimatePresence, motion } from 'motion/react';
import { LayoutGrid, MicVocal, Settings } from 'lucide-react';
import { DESKTOP_WALLPAPER } from '../../data/mockData';
import { useNova } from '../../context/NovaContext';
import { DashboardWindow } from '../dashboard/DashboardWindow';
import { DynamicIsland } from '../island/DynamicIsland';
import { FullscreenLyrics } from '../lyrics/FullscreenLyrics';
import { SettingsWindow } from '../settings/SettingsWindow';
import { Taskbar } from './Taskbar';

export function WindowsDesktop() {
  const {
    appShortName,
    resolvedColorMode,
    openWindows,
    openWindow,
    setFullscreenLyricsOpen,
    system,
    toast,
  } = useNova();
  const [wallpaperFailed, setWallpaperFailed] = useState(false);

  // Convert brightness (35-100) into subtle dimming overlay opacity
  const dimOpacity = Math.max(0, Math.min(0.55, (100 - system.brightness) / 115));

  return (
    <div className="relative w-screen h-screen overflow-hidden bg-[#ECEEF2] dark:bg-[#07080B] text-zinc-900 dark:text-zinc-100 select-none transition-colors duration-300">
      {/* Desktop Wallpaper (Obsidian Studio in Dark Mode, Soft Architectural Porcelain in Light Mode) */}
      {resolvedColorMode === 'dark' ? (
        !wallpaperFailed ? (
          <img
            src={DESKTOP_WALLPAPER}
            alt="Obsidian Studio Desktop Wallpaper"
            referrerPolicy="no-referrer"
            onError={() => setWallpaperFailed(true)}
            className="absolute inset-0 w-full h-full object-cover pointer-events-none"
          />
        ) : (
          <div
            className="absolute inset-0 pointer-events-none"
            style={{
              background:
                'radial-gradient(ellipse at 60% 35%, rgba(60, 78, 118, 0.25), transparent 60%), linear-gradient(160deg, #07080B 0%, #0E1118 100%)',
            }}
          />
        )
      ) : (
        <div
          className="absolute inset-0 pointer-events-none"
          style={{
            background:
              'radial-gradient(ellipse at 72% 24%, rgba(122, 162, 247, 0.16), transparent 55%), radial-gradient(ellipse at 18% 82%, rgba(148, 163, 184, 0.18), transparent 52%), linear-gradient(155deg, #F3F5F8 0%, #E5E8EE 100%)',
          }}
        />
      )}

      {/* Ambient contrast vignette in dark mode */}
      {resolvedColorMode === 'dark' && (
        <div className="absolute inset-0 bg-gradient-to-b from-black/25 via-transparent to-black/45 pointer-events-none" />
      )}

      {/* Minimal Desktop App Shortcuts */}
      <div className="absolute top-16 left-5 z-10 flex flex-col gap-2">
        {[
          {
            label: `${appShortName} Hub`,
            icon: LayoutGrid,
            onClick: () => openWindow('dashboard'),
          },
          {
            label: 'Settings',
            icon: Settings,
            onClick: () => openWindow('settings'),
          },
          {
            label: 'Lyrics Stage',
            icon: MicVocal,
            onClick: () => setFullscreenLyricsOpen(true),
          },
        ].map((shortcut) => {
          const Icon = shortcut.icon;
          return (
            <button
              key={shortcut.label}
              type="button"
              onClick={shortcut.onClick}
              className="w-24 py-2.5 px-2 rounded-xl flex flex-col items-center gap-1.5 text-zinc-800 dark:text-zinc-200 hover:bg-black/[0.05] dark:hover:bg-white/[0.08] border border-transparent hover:border-black/[0.06] dark:hover:border-white/[0.08] transition-colors cursor-pointer"
            >
              <div className="w-10 h-10 rounded-xl bg-white/90 dark:bg-[#0D1017]/85 backdrop-blur-xl border border-zinc-200/90 dark:border-white/[0.11] flex items-center justify-center shadow-sm dark:shadow-lg">
                <Icon className="w-4 h-4 text-zinc-800 dark:text-zinc-100" />
              </div>
              <span className="text-[11px] font-medium text-center leading-tight text-zinc-800 dark:text-zinc-200">
                {shortcut.label}
              </span>
            </button>
          );
        })}
      </div>

      {/* Floating Pure-Black Dynamic Island */}
      <DynamicIsland />

      {/* Desktop Application Windows */}
      <AnimatePresence>
        {openWindows.dashboard && <DashboardWindow key="dashboard-win" />}
        {openWindows.settings && <SettingsWindow key="settings-win" />}
      </AnimatePresence>

      {/* Fullscreen Synchronized Lyrics Stage */}
      <FullscreenLyrics />

      {/* Transient Subtle Action Toast */}
      <AnimatePresence>
        {toast && (
          <motion.div
            initial={{ opacity: 0, y: 10 }}
            animate={{ opacity: 1, y: 0 }}
            exit={{ opacity: 0, y: 8 }}
            transition={{ duration: 0.16 }}
            className="fixed bottom-16 right-5 z-[90] px-4 py-2.5 rounded-xl bg-zinc-950/95 dark:bg-[#0D1017]/95 backdrop-blur-xl border border-white/[0.12] text-[12.5px] text-zinc-100 shadow-2xl pointer-events-none"
          >
            {toast}
          </motion.div>
        )}
      </AnimatePresence>

      {/* Desktop Taskbar */}
      <Taskbar />

      {/* Hardware Brightness Dimming Overlay (driven by System Widget slider) */}
      {dimOpacity > 0 && (
        <div
          className="fixed inset-0 bg-black pointer-events-none z-[100] transition-opacity duration-150"
          style={{ opacity: dimOpacity }}
        />
      )}
    </div>
  );
}
