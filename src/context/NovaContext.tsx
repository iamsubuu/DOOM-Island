import React, {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useMemo,
  useRef,
  useState,
} from 'react';
import { APP_CONFIG } from '../config/appConfig';
import {
  CLIPBOARD_PREVIEW_IMAGE,
  DEFAULT_SETTINGS,
  INITIAL_ACTIVITIES,
  INITIAL_CLIPBOARD_ITEMS,
  INITIAL_NOTIFICATIONS,
  INITIAL_SYSTEM_METRICS,
  INITIAL_TIMER_STATE,
  INITIAL_TRACKS,
  type LrclibSearchEntry,
} from '../data/mockData';
import type {
  ActivityItem,
  AppSettings,
  ClipboardItem,
  DesktopWindowId,
  IslandMode,
  NotificationItem,
  PomodoroPhase,
  SettingsSectionId,
  SystemMetrics,
  TimerState,
  TimerTab,
  Track,
} from '../types/island';

export function formatTime(seconds: number): string {
  const safe = Math.max(0, Math.floor(seconds));
  const mins = Math.floor(safe / 60);
  const secs = safe % 60;
  return `${mins}:${secs.toString().padStart(2, '0')}`;
}

export function formatStopwatch(tenths: number): string {
  const totalSeconds = Math.floor(tenths / 10);
  const mins = Math.floor(totalSeconds / 60);
  const secs = totalSeconds % 60;
  const t = tenths % 10;
  return `${mins.toString().padStart(2, '0')}:${secs.toString().padStart(2, '0')}.${t}`;
}

function loadPersistedSettings(): AppSettings {
  try {
    const raw = window.localStorage.getItem(APP_CONFIG.storageKey);
    if (!raw) return DEFAULT_SETTINGS;
    const parsed = JSON.parse(raw) as Partial<AppSettings>;
    return {
      general: { ...DEFAULT_SETTINGS.general, ...(parsed.general ?? {}) },
      position: { ...DEFAULT_SETTINGS.position, ...(parsed.position ?? {}) },
      appearance: { ...DEFAULT_SETTINGS.appearance, ...(parsed.appearance ?? {}) },
      animation: { ...DEFAULT_SETTINGS.animation, ...(parsed.animation ?? {}) },
      music: { ...DEFAULT_SETTINGS.music, ...(parsed.music ?? {}) },
      spotify: { ...DEFAULT_SETTINGS.spotify, ...(parsed.spotify ?? {}) },
      lyrics: { ...DEFAULT_SETTINGS.lyrics, ...(parsed.lyrics ?? {}) },
      clipboard: {
        ...DEFAULT_SETTINGS.clipboard,
        ...(parsed.clipboard ?? {}),
        excludedApps: Array.isArray(parsed.clipboard?.excludedApps)
          ? parsed.clipboard.excludedApps
          : DEFAULT_SETTINGS.clipboard.excludedApps,
      },
      notifications: {
        ...DEFAULT_SETTINGS.notifications,
        ...(parsed.notifications ?? {}),
        enabledApps: {
          ...DEFAULT_SETTINGS.notifications.enabledApps,
          ...(parsed.notifications?.enabledApps ?? {}),
        },
      },
      widgets: { ...DEFAULT_SETTINGS.widgets, ...(parsed.widgets ?? {}) },
      shortcuts:
        Array.isArray(parsed.shortcuts) && parsed.shortcuts.length > 0
          ? parsed.shortcuts
          : DEFAULT_SETTINGS.shortcuts,
      privacy: { ...DEFAULT_SETTINGS.privacy, ...(parsed.privacy ?? {}) },
      storage: { ...DEFAULT_SETTINGS.storage, ...(parsed.storage ?? {}) },
    };
  } catch {
    return DEFAULT_SETTINGS;
  }
}

interface NovaContextValue {
  // Resolved App Identity & Theme
  appName: string;
  appShortName: string;
  resolvedColorMode: 'dark' | 'light';

  // Island State
  islandMode: IslandMode;
  setIslandMode: (mode: IslandMode) => void;
  collapseIsland: () => void;
  toggleExpandIsland: () => void;
  handleIslandDoubleClick: () => void;
  isExpanded: boolean;
  notificationPulseKey: number;

  // Desktop Windows
  openWindows: Record<DesktopWindowId, boolean>;
  activeWindow: DesktopWindowId | null;
  settingsSection: SettingsSectionId;
  setSettingsSection: (section: SettingsSectionId) => void;
  openWindow: (win: DesktopWindowId, settingsSec?: SettingsSectionId) => void;
  closeWindow: (win: DesktopWindowId) => void;
  toggleWindow: (win: DesktopWindowId) => void;
  focusWindow: (win: DesktopWindowId) => void;
  fullscreenLyricsOpen: boolean;
  setFullscreenLyricsOpen: (open: boolean) => void;

  // Music & Lyrics
  tracks: Track[];
  currentTrack: Track;
  currentTrackIndex: number;
  isPlaying: boolean;
  currentTime: number;
  shuffle: boolean;
  repeat: boolean;
  togglePlay: () => void;
  nextTrack: () => void;
  prevTrack: () => void;
  seekTo: (seconds: number) => void;
  selectTrack: (index: number) => void;
  setShuffle: React.Dispatch<React.SetStateAction<boolean>>;
  setRepeat: React.Dispatch<React.SetStateAction<boolean>>;
  applyLrclibEntry: (entry: LrclibSearchEntry) => void;

  // Clipboard
  clipboardItems: ClipboardItem[];
  copiedId: string | null;
  copyClipboardItem: (item: ClipboardItem) => void;
  togglePinClipboard: (id: string) => void;
  deleteClipboardItem: (id: string) => void;
  clearClipboardHistory: (keepPinned?: boolean) => void;
  addClipboardItem: (item: Omit<ClipboardItem, 'id' | 'timestamp'>) => void;

  // Notifications
  notifications: NotificationItem[];
  unreadCount: number;
  dismissNotification: (id: string) => void;
  markAllNotificationsRead: () => void;
  clearNotifications: () => void;
  simulateIncomingNotification: () => void;

  // Activity
  activities: ActivityItem[];
  toggleActivityPause: (id: string) => void;
  removeActivity: (id: string) => void;
  simulateDownload: () => void;
  simulateTransfer: () => void;
  simulateScreenshot: () => void;

  // Timer
  timer: TimerState;
  setTimerTab: (tab: TimerTab) => void;
  toggleCountdown: () => void;
  resetCountdown: () => void;
  setCountdownPreset: (seconds: number) => void;
  toggleStopwatch: () => void;
  resetStopwatch: () => void;
  lapStopwatch: () => void;
  togglePomodoro: () => void;
  resetPomodoro: () => void;
  setPomodoroPhase: (phase: PomodoroPhase) => void;

  // System
  system: SystemMetrics;
  updateSystem: (patch: Partial<SystemMetrics>) => void;

  // Settings
  settings: AppSettings;
  updateSettingsSection: <K extends keyof AppSettings>(
    section: K,
    patch: Partial<AppSettings[K]>
  ) => void;
  updateShortcut: (id: string, keys: string) => void;
  resetAppearance: () => void;
  resetPosition: () => void;
  resetShortcuts: () => void;
  clearStorageBucket: (bucket: keyof AppSettings['storage']) => void;
  resetAllSettings: () => void;

  // Subtle status toast
  toast: string | null;
  showToast: (message: string) => void;
}

const NovaContext = createContext<NovaContextValue | null>(null);

const SIMULATED_NOTIFICATIONS: Omit<NotificationItem, 'id' | 'timestamp' | 'read'>[] = [
  {
    appName: 'Slack',
    title: 'Elena Vance · #design-systems',
    body: 'Approved the final pure-black geometry and rounded corner tokens for Phase 2 WPF.',
    actionLabel: 'Reply',
  },
  {
    appName: 'GitHub',
    title: 'nova-island/desktop-wpf #148',
    body: 'Merged PR: Hardware-accelerated CompositionTarget spring interpolation.',
    actionLabel: 'View',
  },
  {
    appName: 'Windows System',
    title: 'Audio Device Switched',
    body: 'Studio Reference Monitors (USB-C) active at 96 kHz · 24-bit.',
  },
  {
    appName: 'Outlook',
    title: 'Architecture Review Notes',
    body: 'Shared updated C# WPF ViewModel dependency diagram for Antigravity migration.',
    actionLabel: 'Open',
  },
];

export function NovaProvider({ children }: { children: React.ReactNode }) {
  const [settings, setSettings] = useState<AppSettings>(loadPersistedSettings);
  const [systemPrefersDark, setSystemPrefersDark] = useState<boolean>(() =>
    typeof window !== 'undefined' && window.matchMedia
      ? window.matchMedia('(prefers-color-scheme: dark)').matches
      : true
  );

  const [islandMode, setIslandModeState] = useState<IslandMode>('music');
  const [notificationPulseKey, setNotificationPulseKey] = useState(0);

  const [openWindows, setOpenWindows] = useState<Record<DesktopWindowId, boolean>>({
    dashboard: true,
    settings: false,
  });
  const [activeWindow, setActiveWindow] = useState<DesktopWindowId | null>('dashboard');
  const [settingsSection, setSettingsSection] = useState<SettingsSectionId>('general');
  const [fullscreenLyricsOpen, setFullscreenLyricsOpen] = useState(false);

  const [tracks, setTracks] = useState<Track[]>(INITIAL_TRACKS);
  const [currentTrackIndex, setCurrentTrackIndex] = useState(0);
  const [isPlaying, setIsPlaying] = useState(true);
  const [currentTime, setCurrentTime] = useState(34);
  const [shuffle, setShuffle] = useState(false);
  const [repeat, setRepeat] = useState(false);

  const [clipboardItems, setClipboardItems] = useState<ClipboardItem[]>(INITIAL_CLIPBOARD_ITEMS);
  const [copiedId, setCopiedId] = useState<string | null>(null);

  const [notifications, setNotifications] = useState<NotificationItem[]>(INITIAL_NOTIFICATIONS);
  const [activities, setActivities] = useState<ActivityItem[]>(INITIAL_ACTIVITIES);
  const [timer, setTimer] = useState<TimerState>(INITIAL_TIMER_STATE);
  const [system, setSystem] = useState<SystemMetrics>(INITIAL_SYSTEM_METRICS);

  const [toast, setToast] = useState<string | null>(null);
  const toastTimerRef = useRef<number | null>(null);
  const simNotifIdxRef = useRef(0);

  const currentTrack = tracks[currentTrackIndex] ?? tracks[0];
  const isExpanded = islandMode !== 'compact' && islandMode !== 'music';

  const appName = settings.general.appName.trim() || APP_CONFIG.defaultAppName;
  const appShortName = settings.general.appShortName.trim() || APP_CONFIG.defaultShortName;

  const resolvedColorMode: 'dark' | 'light' =
    settings.appearance.colorMode === 'system'
      ? systemPrefersDark
        ? 'dark'
        : 'light'
      : settings.appearance.colorMode;

  const showToast = useCallback((message: string) => {
    setToast(message);
    if (toastTimerRef.current) {
      window.clearTimeout(toastTimerRef.current);
    }
    toastTimerRef.current = window.setTimeout(() => {
      setToast(null);
    }, 2400);
  }, []);

  // Listen to OS prefers-color-scheme when colorMode is 'system'
  useEffect(() => {
    if (!window.matchMedia) return;
    const mq = window.matchMedia('(prefers-color-scheme: dark)');
    const handler = (e: MediaQueryListEvent) => setSystemPrefersDark(e.matches);
    mq.addEventListener('change', handler);
    return () => mq.removeEventListener('change', handler);
  }, []);

  // Sync Tailwind .dark class on <html>, CSS custom properties, and document.title
  useEffect(() => {
    const root = document.documentElement;
    if (resolvedColorMode === 'dark') {
      root.classList.add('dark');
    } else {
      root.classList.remove('dark');
    }

    root.style.setProperty('--nova-accent', settings.appearance.accentColor);
    const fontStack =
      settings.appearance.fontFamily === 'Segoe UI Variable'
        ? "'Segoe UI Variable', 'Segoe UI', 'Plus Jakarta Sans', sans-serif"
        : `'${settings.appearance.fontFamily}', -apple-system, BlinkMacSystemFont, sans-serif`;
    root.style.setProperty('--nova-font', fontStack);
    document.title = `${appName} — Windows Dynamic Island`;
  }, [resolvedColorMode, settings.appearance.accentColor, settings.appearance.fontFamily, appName]);

  // Persist settings to localStorage
  useEffect(() => {
    try {
      const toPersist: AppSettings = settings.position.rememberLastPosition
        ? settings
        : {
            ...settings,
            position: {
              ...settings.position,
              screenPosition: DEFAULT_SETTINGS.position.screenPosition,
              customX: DEFAULT_SETTINGS.position.customX,
              customY: DEFAULT_SETTINGS.position.customY,
            },
          };
      window.localStorage.setItem(APP_CONFIG.storageKey, JSON.stringify(toPersist));
    } catch {
      // Ignore storage quota errors in sandboxed iframes
    }
  }, [settings]);

  const setIslandMode = useCallback((mode: IslandMode) => {
    setIslandModeState(mode);
  }, []);

  const collapseIsland = useCallback(() => {
    if (isPlaying && settings.spotify.connected) {
      setIslandModeState('music');
    } else {
      setIslandModeState('compact');
    }
  }, [isPlaying, settings.spotify.connected]);

  const toggleExpandIsland = useCallback(() => {
    setIslandModeState((prev) => {
      if (prev === 'compact' || prev === 'music') {
        return 'expanded';
      }
      return isPlaying && settings.spotify.connected ? 'music' : 'compact';
    });
  }, [isPlaying, settings.spotify.connected]);

  // Music playback ticking
  useEffect(() => {
    if (!isPlaying) return;
    const id = window.setInterval(() => {
      setCurrentTime((prev) => {
        if (prev + 1 >= currentTrack.duration) {
          if (repeat) return 0;
          setCurrentTrackIndex((idx) =>
            shuffle ? Math.floor(Math.random() * tracks.length) : (idx + 1) % tracks.length
          );
          return 0;
        }
        return prev + 1;
      });
    }, 1000);
    return () => window.clearInterval(id);
  }, [isPlaying, currentTrack.duration, repeat, shuffle, tracks.length]);

  // Countdown & Pomodoro ticking (1s)
  useEffect(() => {
    if (!timer.countdownRunning && !timer.pomodoroRunning) return;
    const id = window.setInterval(() => {
      setTimer((prev) => {
        const next = { ...prev };
        if (prev.countdownRunning) {
          if (prev.countdownRemaining <= 1) {
            next.countdownRemaining = 0;
            next.countdownRunning = false;
          } else {
            next.countdownRemaining = prev.countdownRemaining - 1;
          }
        }
        if (prev.pomodoroRunning) {
          if (prev.pomodoroRemaining <= 1) {
            const nextPhase: PomodoroPhase =
              prev.pomodoroPhase === 'focus' ? 'shortBreak' : 'focus';
            const nextDuration = nextPhase === 'focus' ? 25 * 60 : 5 * 60;
            next.pomodoroPhase = nextPhase;
            next.pomodoroDuration = nextDuration;
            next.pomodoroRemaining = nextDuration;
            next.pomodoroRunning = false;
            if (prev.pomodoroPhase === 'focus') {
              next.pomodoroSessionsCompleted = prev.pomodoroSessionsCompleted + 1;
            }
          } else {
            next.pomodoroRemaining = prev.pomodoroRemaining - 1;
          }
        }
        return next;
      });
    }, 1000);
    return () => window.clearInterval(id);
  }, [timer.countdownRunning, timer.pomodoroRunning]);

  // Stopwatch ticking (100ms)
  useEffect(() => {
    if (!timer.stopwatchRunning) return;
    const id = window.setInterval(() => {
      setTimer((prev) => ({ ...prev, stopwatchElapsed: prev.stopwatchElapsed + 1 }));
    }, 100);
    return () => window.clearInterval(id);
  }, [timer.stopwatchRunning]);

  // Live subtle CPU / RAM telemetry & active transfer/download progress
  useEffect(() => {
    const id = window.setInterval(() => {
      setSystem((prev) => {
        const cpuDelta = Math.round((Math.random() - 0.48) * 5);
        const nextCpu = Math.min(78, Math.max(8, prev.cpuUsage + cpuDelta));
        const ramDelta = (Math.random() - 0.5) * 0.4;
        const nextRamGb = Math.min(
          28,
          Math.max(10.2, Number((prev.ramUsedGb + ramDelta).toFixed(1)))
        );
        const nextRamPct = Math.round((nextRamGb / prev.ramTotalGb) * 100);
        return {
          ...prev,
          cpuUsage: nextCpu,
          ramUsedGb: nextRamGb,
          ramUsage: nextRamPct,
        };
      });

      setActivities((prev) =>
        prev.map((act) => {
          if (act.status !== 'active' || act.type === 'screenshot') return act;
          const step = act.type === 'download' ? 3 : 2;
          const nextProgress = Math.min(100, act.progress + step);
          return {
            ...act,
            progress: nextProgress,
            status: nextProgress >= 100 ? 'completed' : 'active',
            timestamp: nextProgress >= 100 ? 'Just finished' : 'Live',
          };
        })
      );
    }, settings.widgets.refreshIntervalMs);
    return () => window.clearInterval(id);
  }, [settings.widgets.refreshIntervalMs]);

  // Window Management
  const openWindow = useCallback((win: DesktopWindowId, settingsSec?: SettingsSectionId) => {
    if (win === 'settings' && settingsSec) {
      setSettingsSection(settingsSec);
    }
    setOpenWindows((prev) => ({ ...prev, [win]: true }));
    setActiveWindow(win);
  }, []);

  const closeWindow = useCallback((win: DesktopWindowId) => {
    setOpenWindows((prev) => ({ ...prev, [win]: false }));
    setActiveWindow((prev) => (prev === win ? null : prev));
  }, []);

  const toggleWindow = useCallback((win: DesktopWindowId) => {
    setOpenWindows((prev) => {
      const willOpen = !prev[win];
      if (willOpen) {
        setActiveWindow(win);
      } else {
        setActiveWindow((curr) => (curr === win ? null : curr));
      }
      return { ...prev, [win]: willOpen };
    });
  }, []);

  const focusWindow = useCallback((win: DesktopWindowId) => {
    setActiveWindow(win);
  }, []);

  // Music Actions
  const togglePlay = useCallback(() => {
    setIsPlaying((prev) => !prev);
  }, []);

  const nextTrack = useCallback(() => {
    setCurrentTrackIndex((idx) =>
      shuffle ? Math.floor(Math.random() * tracks.length) : (idx + 1) % tracks.length
    );
    setCurrentTime(0);
    setIsPlaying(true);
    if (settings.music.autoExpandOnTrackChange) {
      setIslandModeState('expanded');
    }
  }, [shuffle, tracks.length, settings.music.autoExpandOnTrackChange]);

  const prevTrack = useCallback(() => {
    if (currentTime > 4) {
      setCurrentTime(0);
      return;
    }
    setCurrentTrackIndex((idx) => (idx - 1 + tracks.length) % tracks.length);
    setCurrentTime(0);
    setIsPlaying(true);
  }, [currentTime, tracks.length]);

  const seekTo = useCallback(
    (seconds: number) => {
      setCurrentTime(Math.max(0, Math.min(seconds, currentTrack.duration)));
    },
    [currentTrack.duration]
  );

  const selectTrack = useCallback((index: number) => {
    setCurrentTrackIndex(index);
    setCurrentTime(0);
    setIsPlaying(true);
  }, []);

  const applyLrclibEntry = useCallback(
    (entry: LrclibSearchEntry) => {
      setTracks((prev) =>
        prev.map((t, idx) =>
          idx === currentTrackIndex
            ? {
                ...t,
                lyrics: entry.lines,
                lyricsSource: entry.source,
                isSynced: entry.synced,
              }
            : t
        )
      );
      showToast(`Loaded synced lyrics for "${entry.trackName}" via ${entry.source}`);
    },
    [currentTrackIndex, showToast]
  );

  // Double-click handler on Dynamic Island
  const handleIslandDoubleClick = useCallback(() => {
    switch (settings.general.doubleClickAction) {
      case 'toggle-lyrics':
        setIslandModeState((prev) => (prev === 'lyrics' ? 'expanded' : 'lyrics'));
        break;
      case 'open-fullscreen-lyrics':
        setFullscreenLyricsOpen(true);
        break;
      case 'open-dashboard':
        openWindow('dashboard');
        break;
      case 'toggle-play':
        togglePlay();
        break;
    }
  }, [settings.general.doubleClickAction, openWindow, togglePlay]);

  // Clipboard Actions
  const copyClipboardItem = useCallback(
    (item: ClipboardItem) => {
      if (navigator.clipboard && item.type !== 'image') {
        navigator.clipboard.writeText(item.content).catch(() => {});
      }
      setCopiedId(item.id);
      showToast(`Copied "${item.previewTitle || item.type}" to clipboard`);
      window.setTimeout(() => {
        setCopiedId((curr) => (curr === item.id ? null : curr));
      }, 1600);
    },
    [showToast]
  );

  const togglePinClipboard = useCallback((id: string) => {
    setClipboardItems((prev) =>
      prev.map((item) => (item.id === id ? { ...item, pinned: !item.pinned } : item))
    );
  }, []);

  const deleteClipboardItem = useCallback((id: string) => {
    setClipboardItems((prev) => prev.filter((item) => item.id !== id));
  }, []);

  const clearClipboardHistory = useCallback(
    (keepPinned = true) => {
      setClipboardItems((prev) => (keepPinned ? prev.filter((i) => i.pinned) : []));
      showToast(keepPinned ? 'Cleared unpinned clipboard history' : 'Cleared all clipboard history');
    },
    [showToast]
  );

  const addClipboardItem = useCallback(
    (item: Omit<ClipboardItem, 'id' | 'timestamp'>) => {
      const entry: ClipboardItem = {
        ...item,
        id: `clip-${Date.now()}`,
        timestamp: 'Just now',
      };
      setClipboardItems((prev) => [entry, ...prev].slice(0, settings.clipboard.historyLimit));
    },
    [settings.clipboard.historyLimit]
  );

  // Notification Actions
  const unreadCount = useMemo(
    () => notifications.filter((n) => !n.read && !n.dismissed).length,
    [notifications]
  );

  const dismissNotification = useCallback((id: string) => {
    setNotifications((prev) =>
      prev.map((n) => (n.id === id ? { ...n, dismissed: true, read: true } : n))
    );
  }, []);

  const markAllNotificationsRead = useCallback(() => {
    setNotifications((prev) => prev.map((n) => ({ ...n, read: true })));
  }, []);

  const clearNotifications = useCallback(() => {
    setNotifications([]);
  }, []);

  const simulateIncomingNotification = useCallback(() => {
    const template =
      SIMULATED_NOTIFICATIONS[simNotifIdxRef.current % SIMULATED_NOTIFICATIONS.length];
    simNotifIdxRef.current += 1;
    const newNotif: NotificationItem = {
      ...template,
      id: `notif-${Date.now()}`,
      timestamp: 'Just now',
      read: false,
    };
    setNotifications((prev) => [newNotif, ...prev]);
    setNotificationPulseKey((k) => k + 1);
    setIslandModeState('notifications');
    showToast(`New notification from ${template.appName}`);
  }, [showToast]);

  // Activity Actions
  const toggleActivityPause = useCallback((id: string) => {
    setActivities((prev) =>
      prev.map((a) => {
        if (a.id !== id || a.status === 'completed') return a;
        return { ...a, status: a.status === 'active' ? 'paused' : 'active' };
      })
    );
  }, []);

  const removeActivity = useCallback((id: string) => {
    setActivities((prev) => prev.filter((a) => a.id !== id));
  }, []);

  const simulateDownload = useCallback(() => {
    const newAct: ActivityItem = {
      id: `act-${Date.now()}`,
      type: 'download',
      title: `NovaIsland_SDK_v1.${Math.floor(Math.random() * 9)}.zip`,
      subtitle: 'Microsoft Edge · github.com/nova-island',
      progress: 8,
      speed: '54.2 MB/s',
      sizeText: '12 MB / 148 MB',
      status: 'active',
      timestamp: 'Live',
    };
    setActivities((prev) => [newAct, ...prev]);
    setIslandModeState('activity');
    showToast(`Started simulated download in ${appName}`);
  }, [showToast, appName]);

  const simulateTransfer = useCallback(() => {
    const newAct: ActivityItem = {
      id: `act-${Date.now()}`,
      type: 'transfer',
      title: 'WPF_Shader_Assets_4K.tar',
      subtitle: 'Transferring to \\\\WORKSTATION-02\\Shared',
      progress: 12,
      speed: '164.0 MB/s',
      sizeText: '240 MB / 2.00 GB',
      status: 'active',
      timestamp: 'Live',
    };
    setActivities((prev) => [newAct, ...prev]);
    setIslandModeState('activity');
    showToast('Started simulated local file transfer');
  }, [showToast]);

  const simulateScreenshot = useCallback(() => {
    const stamp = new Date().toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' });
    const newAct: ActivityItem = {
      id: `act-${Date.now()}`,
      type: 'screenshot',
      title: 'Screenshot captured to Clipboard',
      subtitle: `Capture_${stamp.replace(/[:\s]/g, '_')}.png · 2560×1440`,
      progress: 100,
      sizeText: '518 KB',
      status: 'completed',
      imageUrl: CLIPBOARD_PREVIEW_IMAGE,
      timestamp: 'Just now',
    };
    setActivities((prev) => [newAct, ...prev]);
    if (settings.clipboard.enabled && settings.clipboard.captureImages) {
      addClipboardItem({
        type: 'image',
        previewTitle: `Capture_${stamp.replace(/[:\s]/g, '_')}.png`,
        content: '2560 × 1440 · PNG · 518 KB',
        imageUrl: CLIPBOARD_PREVIEW_IMAGE,
        sourceApp: `${appShortName} Snip`,
        pinned: false,
      });
    }
    setIslandModeState('activity');
    showToast('Simulated screenshot captured & saved to clipboard');
  }, [
    settings.clipboard.enabled,
    settings.clipboard.captureImages,
    addClipboardItem,
    showToast,
    appShortName,
  ]);

  // Timer Actions
  const setTimerTab = useCallback((tab: TimerTab) => {
    setTimer((prev) => ({ ...prev, activeTab: tab }));
  }, []);

  const toggleCountdown = useCallback(() => {
    setTimer((prev) => ({
      ...prev,
      countdownRunning: prev.countdownRemaining > 0 ? !prev.countdownRunning : false,
    }));
  }, []);

  const resetCountdown = useCallback(() => {
    setTimer((prev) => ({
      ...prev,
      countdownRemaining: prev.countdownDuration,
      countdownRunning: false,
    }));
  }, []);

  const setCountdownPreset = useCallback((seconds: number) => {
    setTimer((prev) => ({
      ...prev,
      countdownDuration: seconds,
      countdownRemaining: seconds,
      countdownRunning: false,
    }));
  }, []);

  const toggleStopwatch = useCallback(() => {
    setTimer((prev) => ({ ...prev, stopwatchRunning: !prev.stopwatchRunning }));
  }, []);

  const resetStopwatch = useCallback(() => {
    setTimer((prev) => ({
      ...prev,
      stopwatchElapsed: 0,
      stopwatchRunning: false,
      stopwatchLaps: [],
    }));
  }, []);

  const lapStopwatch = useCallback(() => {
    setTimer((prev) => ({
      ...prev,
      stopwatchLaps: [prev.stopwatchElapsed, ...prev.stopwatchLaps].slice(0, 6),
    }));
  }, []);

  const togglePomodoro = useCallback(() => {
    setTimer((prev) => ({ ...prev, pomodoroRunning: !prev.pomodoroRunning }));
  }, []);

  const resetPomodoro = useCallback(() => {
    setTimer((prev) => ({
      ...prev,
      pomodoroRemaining: prev.pomodoroDuration,
      pomodoroRunning: false,
    }));
  }, []);

  const setPomodoroPhase = useCallback((phase: PomodoroPhase) => {
    const duration = phase === 'focus' ? 25 * 60 : phase === 'shortBreak' ? 5 * 60 : 15 * 60;
    setTimer((prev) => ({
      ...prev,
      pomodoroPhase: phase,
      pomodoroDuration: duration,
      pomodoroRemaining: duration,
      pomodoroRunning: false,
    }));
  }, []);

  // System Metrics
  const updateSystem = useCallback((patch: Partial<SystemMetrics>) => {
    setSystem((prev) => ({ ...prev, ...patch }));
  }, []);

  // Settings
  const updateSettingsSection = useCallback(
    <K extends keyof AppSettings>(section: K, patch: Partial<AppSettings[K]>) => {
      setSettings((prev) => ({
        ...prev,
        [section]: {
          ...(prev[section] as object),
          ...(patch as object),
        },
      }));
    },
    []
  );

  const updateShortcut = useCallback(
    (id: string, keys: string) => {
      setSettings((prev) => ({
        ...prev,
        shortcuts: prev.shortcuts.map((s) => (s.id === id ? { ...s, keys } : s)),
      }));
      showToast(`Shortcut updated to ${keys}`);
    },
    [showToast]
  );

  const resetAppearance = useCallback(() => {
    setSettings((prev) => ({ ...prev, appearance: DEFAULT_SETTINGS.appearance }));
    showToast('Appearance reset to defaults');
  }, [showToast]);

  const resetPosition = useCallback(() => {
    setSettings((prev) => ({ ...prev, position: DEFAULT_SETTINGS.position }));
    showToast('Island position reset to Top Center');
  }, [showToast]);

  const resetShortcuts = useCallback(() => {
    setSettings((prev) => ({ ...prev, shortcuts: DEFAULT_SETTINGS.shortcuts }));
    showToast('Keyboard shortcuts reset to defaults');
  }, [showToast]);

  const clearStorageBucket = useCallback(
    (bucket: keyof AppSettings['storage']) => {
      setSettings((prev) => ({
        ...prev,
        storage: {
          ...prev.storage,
          [bucket]: bucket === 'configDbMb' ? 0.4 : 0,
        },
      }));
      showToast('Local mock cache cleared');
    },
    [showToast]
  );

  const resetAllSettings = useCallback(() => {
    try {
      window.localStorage.removeItem(APP_CONFIG.storageKey);
    } catch {
      // ignore
    }
    setSettings(DEFAULT_SETTINGS);
    showToast('All settings restored to factory defaults');
  }, [showToast]);

  // Active keyboard shortcut handler
  useEffect(() => {
    const handleKeyDown = (e: KeyboardEvent) => {
      const target = e.target as HTMLElement | null;
      if (
        target &&
        (target.tagName === 'INPUT' || target.tagName === 'TEXTAREA' || target.isContentEditable)
      ) {
        return;
      }

      const parts: string[] = [];
      if (e.ctrlKey) parts.push('Ctrl');
      if (e.altKey) parts.push('Alt');
      if (e.shiftKey) parts.push('Shift');
      const rawKey = e.key === ' ' ? 'Space' : e.key.length === 1 ? e.key.toUpperCase() : e.key;
      if (!['Control', 'Alt', 'Shift', 'Meta'].includes(e.key)) {
        parts.push(rawKey);
      }
      const combo = parts.join(' + ');

      const match = settings.shortcuts.find(
        (sc) => sc.keys.toLowerCase() === combo.toLowerCase()
      );
      if (!match) return;

      e.preventDefault();
      switch (match.action) {
        case 'toggle-island':
          toggleExpandIsland();
          break;
        case 'open-music':
          setIslandModeState('expanded');
          break;
        case 'open-lyrics':
          setIslandModeState('lyrics');
          break;
        case 'open-clipboard':
          setIslandModeState('clipboard');
          break;
        case 'open-timer':
          setIslandModeState('timer');
          break;
        case 'open-dashboard':
          openWindow('dashboard');
          break;
        case 'open-settings':
          openWindow('settings');
          break;
        case 'trigger-screenshot':
          simulateScreenshot();
          break;
      }
    };

    window.addEventListener('keydown', handleKeyDown);
    return () => window.removeEventListener('keydown', handleKeyDown);
  }, [settings.shortcuts, toggleExpandIsland, openWindow, simulateScreenshot]);

  const value: NovaContextValue = {
    appName,
    appShortName,
    resolvedColorMode,
    islandMode,
    setIslandMode,
    collapseIsland,
    toggleExpandIsland,
    handleIslandDoubleClick,
    isExpanded,
    notificationPulseKey,
    openWindows,
    activeWindow,
    settingsSection,
    setSettingsSection,
    openWindow,
    closeWindow,
    toggleWindow,
    focusWindow,
    fullscreenLyricsOpen,
    setFullscreenLyricsOpen,
    tracks,
    currentTrack,
    currentTrackIndex,
    isPlaying,
    currentTime,
    shuffle,
    repeat,
    togglePlay,
    nextTrack,
    prevTrack,
    seekTo,
    selectTrack,
    setShuffle,
    setRepeat,
    applyLrclibEntry,
    clipboardItems,
    copiedId,
    copyClipboardItem,
    togglePinClipboard,
    deleteClipboardItem,
    clearClipboardHistory,
    addClipboardItem,
    notifications,
    unreadCount,
    dismissNotification,
    markAllNotificationsRead,
    clearNotifications,
    simulateIncomingNotification,
    activities,
    toggleActivityPause,
    removeActivity,
    simulateDownload,
    simulateTransfer,
    simulateScreenshot,
    timer,
    setTimerTab,
    toggleCountdown,
    resetCountdown,
    setCountdownPreset,
    toggleStopwatch,
    resetStopwatch,
    lapStopwatch,
    togglePomodoro,
    resetPomodoro,
    setPomodoroPhase,
    system,
    updateSystem,
    settings,
    updateSettingsSection,
    updateShortcut,
    resetAppearance,
    resetPosition,
    resetShortcuts,
    clearStorageBucket,
    resetAllSettings,
    toast,
    showToast,
  };

  return <NovaContext.Provider value={value}>{children}</NovaContext.Provider>;
}

export function useNova() {
  const ctx = useContext(NovaContext);
  if (!ctx) {
    throw new Error('useNova must be used inside NovaProvider');
  }
  return ctx;
}
