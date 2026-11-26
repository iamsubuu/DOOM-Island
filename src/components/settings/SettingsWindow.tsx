import React, { useMemo, useState } from 'react';
import {
  Bell,
  ClipboardList,
  Disc3,
  HardDrive,
  Info,
  Keyboard,
  LayoutGrid,
  MicVocal,
  Move,
  Music2,
  Palette,
  Search,
  Settings,
  Shield,
  Sliders,
  Waves,
  X,
} from 'lucide-react';
import { useNova } from '../../context/NovaContext';
import type { SettingsSectionId } from '../../types/island';
import { DesktopWindow } from '../desktop/DesktopWindow';
import { SettingsSectionContent } from './SettingsSections';

interface SidebarSectionMeta {
  id: SettingsSectionId;
  label: string;
  subtitle: string;
  keywords: string[];
  icon: React.ComponentType<{ className?: string }>;
}

const SIDEBAR_SECTIONS: SidebarSectionMeta[] = [
  {
    id: 'general',
    label: 'General',
    subtitle: 'Brand name, startup, always on top, and collapse behavior',
    keywords: ['name', 'brand', 'startup', 'top', 'collapse', 'double click', 'nova'],
    icon: Sliders,
  },
  {
    id: 'appearance',
    label: 'Appearance',
    subtitle: 'Dark/Light/System theme, accent color, font, dimensions, and live preview',
    keywords: [
      'theme',
      'dark',
      'light',
      'system',
      'accent',
      'color',
      'font',
      'size',
      'weight',
      'width',
      'height',
      'radius',
      'opacity',
      'blur',
      'spacing',
      'black',
    ],
    icon: Palette,
  },
  {
    id: 'position',
    label: 'Position & Display',
    subtitle: 'Top center, left, right, custom X/Y coordinates, monitor, and live preview',
    keywords: ['position', 'display', 'monitor', 'custom', 'x', 'y', 'center', 'left', 'right', 'remember', 'reset'],
    icon: Move,
  },
  {
    id: 'animation',
    label: 'Animation',
    subtitle: 'Spring physics presets, speed, hover scale, bounce, and idle breathing',
    keywords: ['animation', 'spring', 'fluid', 'snappy', 'silk', 'speed', 'motion', 'hover', 'bounce', 'idle'],
    icon: Waves,
  },
  {
    id: 'music',
    label: 'Music',
    subtitle: 'Album artwork, audio-reactive equalizer, compact progress, and media keys',
    keywords: ['music', 'equalizer', 'visualizer', 'artwork', 'progress', 'media keys', 'scroll'],
    icon: Music2,
  },
  {
    id: 'spotify',
    label: 'Spotify',
    subtitle: 'Simulated Spotify bridge, playback device, and polling preferences',
    keywords: ['spotify', 'connect', 'account', 'device', 'queue', 'poll', 'covers'],
    icon: Disc3,
  },
  {
    id: 'lyrics',
    label: 'Lyrics',
    subtitle: 'Synchronized LRC, LRCLIB fallback, scrolling, and fullscreen backdrop',
    keywords: ['lyrics', 'lrclib', 'sync', 'scroll', 'blur', 'translation', 'fullscreen'],
    icon: MicVocal,
  },
  {
    id: 'clipboard',
    label: 'Clipboard',
    subtitle: 'History limit, auto-clear, sensitive filter, and excluded apps',
    keywords: ['clipboard', 'history', 'clear', 'sensitive', 'password', 'pin', 'images', 'excluded'],
    icon: ClipboardList,
  },
  {
    id: 'notifications',
    label: 'Notifications',
    subtitle: 'Toast routing, Focus Assist sync, previews, and per-app filters',
    keywords: ['notifications', 'alerts', 'dnd', 'focus', 'slack', 'github', 'outlook', 'discord'],
    icon: Bell,
  },
  {
    id: 'widgets',
    label: 'Widgets',
    subtitle: 'Battery, volume, brightness, Wi-Fi, audio devices, CPU, RAM, and activity',
    keywords: ['widgets', 'battery', 'volume', 'brightness', 'wifi', 'audio', 'devices', 'mic', 'camera', 'cpu', 'ram', 'downloads', 'screenshots'],
    icon: LayoutGrid,
  },
  {
    id: 'shortcuts',
    label: 'Shortcuts',
    subtitle: 'Editable global keyboard bindings for instant desktop navigation',
    keywords: ['shortcuts', 'hotkeys', 'keyboard', 'alt', 'ctrl', 'keys', 'bindings'],
    icon: Keyboard,
  },
  {
    id: 'privacy',
    label: 'Privacy',
    subtitle: 'Local-only processing, private browsing pause, and lock screen masking',
    keywords: ['privacy', 'local', 'incognito', 'private', 'lock', 'mask', 'logs'],
    icon: Shield,
  },
  {
    id: 'storage',
    label: 'Storage',
    subtitle: 'Local cache footprint, cleanup controls, JSON export, and factory reset',
    keywords: ['storage', 'cache', 'localstorage', 'export', 'json', 'reset', 'clear'],
    icon: HardDrive,
  },
  {
    id: 'about',
    label: 'About',
    subtitle: 'Application version, C# WPF target architecture, and prototype info',
    keywords: ['about', 'version', 'wpf', 'antigravity', 'updates', 'prototype'],
    icon: Info,
  },
];

export function SettingsWindow() {
  const {
    appName,
    openWindows,
    activeWindow,
    settingsSection,
    setSettingsSection,
    focusWindow,
    closeWindow,
  } = useNova();

  const [searchQuery, setSearchQuery] = useState('');

  const filteredSections = useMemo(() => {
    const q = searchQuery.trim().toLowerCase();
    if (!q) return SIDEBAR_SECTIONS;
    return SIDEBAR_SECTIONS.filter(
      (s) =>
        s.label.toLowerCase().includes(q) ||
        s.subtitle.toLowerCase().includes(q) ||
        s.keywords.some((kw) => kw.includes(q))
    );
  }, [searchQuery]);

  if (!openWindows.settings) return null;

  const activeMeta =
    SIDEBAR_SECTIONS.find((s) => s.id === settingsSection) ?? SIDEBAR_SECTIONS[0];

  return (
    <DesktopWindow
      id="settings"
      title={`${appName} Settings`}
      subtitle={activeMeta.label}
      icon={<Settings className="w-4 h-4" />}
      isActive={activeWindow === 'settings'}
      onFocus={() => focusWindow('settings')}
      onClose={() => closeWindow('settings')}
      defaultWidth={1020}
      defaultHeight={650}
      offsetX={24}
      offsetY={16}
    >
      {/* Left Navigation Sidebar with Built-in Search */}
      <aside className="w-56 sm:w-60 shrink-0 border-r border-zinc-200/80 dark:border-white/[0.07] bg-zinc-100/60 dark:bg-white/[0.015] p-3 flex flex-col gap-2.5 overflow-hidden">
        {/* Settings Search Input */}
        <div className="relative shrink-0">
          <Search className="w-3.5 h-3.5 text-zinc-400 dark:text-zinc-500 absolute left-2.5 top-1/2 -translate-y-1/2 pointer-events-none" />
          <input
            type="text"
            value={searchQuery}
            onChange={(e) => setSearchQuery(e.target.value)}
            placeholder="Find a setting..."
            aria-label="Search settings"
            className="w-full bg-white dark:bg-white/[0.04] border border-zinc-200/90 dark:border-white/[0.09] rounded-lg pl-8 pr-7 py-1.5 text-[12px] text-zinc-900 dark:text-zinc-100 placeholder:text-zinc-400 dark:placeholder:text-zinc-500 focus:outline-none focus:border-zinc-400 dark:focus:border-white/25 transition-colors"
          />
          {searchQuery && (
            <button
              type="button"
              onClick={() => setSearchQuery('')}
              aria-label="Clear search"
              className="absolute right-2 top-1/2 -translate-y-1/2 text-zinc-400 hover:text-zinc-700 dark:hover:text-zinc-200 cursor-pointer"
            >
              <X className="w-3 h-3" />
            </button>
          )}
        </div>

        {/* Section List */}
        <nav className="flex-1 overflow-y-auto nova-scroll flex flex-col gap-0.5 pr-0.5">
          {filteredSections.length === 0 ? (
            <div className="px-3 py-6 text-center text-[12px] text-zinc-500">
              No settings match "{searchQuery}".
            </div>
          ) : (
            filteredSections.map((item) => {
              const Icon = item.icon;
              const active = settingsSection === item.id;
              return (
                <button
                  key={item.id}
                  type="button"
                  onClick={() => setSettingsSection(item.id)}
                  className={`relative flex items-center gap-2.5 px-3 py-2 rounded-lg text-[13px] font-medium transition-colors text-left cursor-pointer whitespace-nowrap ${
                    active
                      ? 'bg-white text-zinc-950 shadow-sm dark:bg-white/[0.11] dark:text-white'
                      : 'text-zinc-600 dark:text-zinc-400 hover:text-zinc-950 dark:hover:text-zinc-100 hover:bg-zinc-200/60 dark:hover:bg-white/[0.05]'
                  }`}
                >
                  {active && (
                    <span
                      className="absolute left-0.5 top-2 bottom-2 w-[3px] rounded-full"
                      style={{ backgroundColor: 'var(--nova-accent)' }}
                    />
                  )}
                  <Icon className="w-4 h-4 shrink-0" />
                  <span className="truncate">{item.label}</span>
                </button>
              );
            })
          )}
        </nav>
      </aside>

      {/* Right Settings Content Pane */}
      <main className="flex-1 min-w-0 overflow-y-auto nova-scroll p-6 lg:p-7">
        <div className="max-w-2xl">
          <div className="pb-4 mb-2 border-b border-zinc-200/80 dark:border-white/[0.08]">
            <h2 className="text-[19px] font-semibold text-zinc-900 dark:text-white tracking-tight">
              {activeMeta.label}
            </h2>
            <p className="text-[12.5px] text-zinc-500 dark:text-zinc-400 mt-0.5">
              {activeMeta.subtitle}
            </p>
          </div>

          <SettingsSectionContent section={settingsSection} />
        </div>
      </main>
    </DesktopWindow>
  );
}
